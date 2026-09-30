using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace FishingAutomation;

public sealed record MabinogiCliResult(string Command, bool Success, string State,
    JsonElement? Data, int? ExitCode, string? Error);

internal sealed record CliProcessOutput(int ExitCode, string Stdout, string Stderr);

public sealed record MabinogiCliCapability(string Command, IReadOnlyDictionary<string, string> Metadata)
{
    public bool RequiresConfirm => Metadata.TryGetValue("requiresConfirm", out var value) &&
        value.Equals("true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Strict Mabinogi Mobile CLI adapter. Read commands are allowlisted. Action commands use
/// the exact base64 JSON body format observed in mobi-Support and are gated by capabilities.
/// </summary>
public sealed class MabinogiMobileCli
{
    public const string DefaultPath = @"C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe";
    private readonly AppLog _log;
    private readonly Func<string, string?, CancellationToken, Task<CliProcessOutput>> _run;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private int _ownedFishing;
    public bool ZeroWingMode { get; }

    public MabinogiMobileCli(AppLog log, bool zeroWingMode = true)
        : this(log, zeroWingMode, RunProcessAsync) { }

    internal MabinogiMobileCli(AppLog log, bool zeroWingMode,
        Func<string, CancellationToken, Task<CliProcessOutput>> run)
        : this(log, zeroWingMode, (command, bodyArgument, token) =>
        {
            if (bodyArgument is not null) throw new InvalidOperationException("body_not_supported_by_test_runner");
            return run(command, token);
        }) { }

    internal MabinogiMobileCli(AppLog log, bool zeroWingMode,
        Func<string, string?, CancellationToken, Task<CliProcessOutput>> run)
    {
        _log = log;
        ZeroWingMode = zeroWingMode;
        _run = run;
    }

    public Task<MabinogiCliResult> StatusAsync(CancellationToken token = default) => QueryAsync("status", token);
    public Task<MabinogiCliResult> CapabilitiesAsync(CancellationToken token = default) => QueryAsync("capabilities", token);
    public Task<MabinogiCliResult> GetItemsAsync(CancellationToken token = default) => QueryAsync("get_items", token);
    public Task<MabinogiCliResult> GetActivityAsync(CancellationToken token = default) => QueryAsync("get_activity", token);
    public Task<MabinogiCliResult> GetCurrentEnvironmentAsync(CancellationToken token = default) => QueryAsync("get_current_environment", token);
    public Task<MabinogiCliResult> GetAlterableItemsAsync(CancellationToken token = default) => QueryAsync("get_alterable_items", token);
    public Task<MabinogiCliResult> GetAlteringWorksAsync(CancellationToken token = default) => QueryAsync("get_altering_works", token);
    public Task<MabinogiCliResult> GetGatherableItemsAsync(CancellationToken token = default) => QueryAsync("get_gatherable_items", token);
    public Task<MabinogiCliResult> GetInventoryAsync(CancellationToken token = default) => QueryAsync("get_inventory", token);

    public async Task<IReadOnlyList<MabinogiCliCapability>> GetCapabilitiesAsync(CancellationToken token = default)
    {
        var result = await CapabilitiesAsync(token).ConfigureAwait(false);
        if (!result.Success || result.Data is not JsonElement body || body.ValueKind != JsonValueKind.Object ||
            !TryProperty(body, "commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("CLI 명령 목록을 확인할 수 없습니다.");
        if (TryProperty(body, "loading", out var loading) && loading.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException("CLI 명령 목록을 준비 중입니다.");

        var list = new List<MabinogiCliCapability>();
        foreach (var row in commands.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !TryProperty(row, "Command", out var commandValue) ||
                commandValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(commandValue.GetString()))
                continue;
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TryProperty(row, "Metadata", out var md) && md.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in md.EnumerateObject())
                {
                    metadata[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }
            }
            list.Add(new(commandValue.GetString()!, metadata));
        }
        return list;
    }

    public async Task<bool> ActionRequiresConfirmAsync(string command, CancellationToken token = default)
    {
        if (!IsAllowedAction(command)) throw new ArgumentOutOfRangeException(nameof(command));
        var capability = (await GetCapabilitiesAsync(token).ConfigureAwait(false))
            .SingleOrDefault(x => x.Command.Equals(command, StringComparison.Ordinal));
        if (capability is null) throw new InvalidOperationException($"현재 게임이 {command} 명령을 제공하지 않습니다.");
        return capability.RequiresConfirm;
    }

    public Task<MabinogiCliResult> CompleteAlteringWorkAsync(string displayName, CancellationToken token = default)
        => ActionAsync("complete_altering_work", displayName, allowConfirmationRequired: true, token);

    public Task<MabinogiCliResult> ExecuteAlteringAsync(string displayName, bool allowConfirmedCost,
        CancellationToken token = default)
        => ActionAsync("execute_altering", displayName, allowConfirmedCost, token);

    public async Task<MabinogiCliResult> ExecuteGatheringAsync(string displayName, CancellationToken token = default)
    {
        var result = await ActionAsync("execute_gathering", displayName,
            allowConfirmationRequired: !ZeroWingMode, token).ConfigureAwait(false);
        if (result.Success && result.Data is JsonElement data && data.ValueKind == JsonValueKind.Object &&
            TryProperty(data, "result", out var state) && state.ValueKind == JsonValueKind.String &&
            state.GetString()?.Equals("started", StringComparison.OrdinalIgnoreCase) == true)
            Interlocked.Exchange(ref _ownedFishing, 1);
        return result;
    }

    public async Task<MabinogiCliResult> StopOwnedFishingAsync(CancellationToken token = default)
    {
        const string command = "stop_action";
        if (Interlocked.CompareExchange(ref _ownedFishing, 0, 0) == 0)
            return Finish(new(command, false, "blocked", null, null, "action_not_owned"));

        try
        {
            _ = await ActionRequiresConfirmAsync(command, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            return Finish(new(command, false, "blocked", null, null, "capability_unavailable"));
        }

        var result = await RequestAsync(command, null, token).ConfigureAwait(false);
        if (result.Success) Interlocked.Exchange(ref _ownedFishing, 0);
        return result;
    }

    public async Task<MabinogiCliResult> QueryAsync(string command, CancellationToken token = default)
    {
        if (!IsAllowedQuery(command))
            return Finish(new(command, false, "blocked", null, null, "command_not_allowed"));
        return await RequestAsync(command, null, token).ConfigureAwait(false);
    }

    private async Task<MabinogiCliResult> ActionAsync(string command, string displayName,
        bool allowConfirmationRequired, CancellationToken token)
    {
        if (!IsAllowedAction(command) || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256 || displayName.Any(char.IsControl))
            return Finish(new(command, false, "blocked", null, null, "command_not_allowed"));

        bool requiresConfirm;
        try { requiresConfirm = await ActionRequiresConfirmAsync(command, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        { return Finish(new(command, false, "blocked", null, null, "capability_unavailable")); }

        if (requiresConfirm && !allowConfirmationRequired)
            return Finish(new(command, false, "blocked", null, null, "confirmation_required"));

        string body = JsonSerializer.Serialize(new { displayName },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return await RequestAsync(command, BodyArgument(body), token).ConfigureAwait(false);
    }

    private async Task<MabinogiCliResult> RequestAsync(string command, string? bodyArgument, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await _serial.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var output = await _run(command, bodyArgument, token).ConfigureAwait(false);
            return Finish(Parse(command, output));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return Finish(new(command, false, "disconnected", null, null, "timeout"));
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Finish(new(command, false, "disconnected", null, null, "cli_unavailable"));
        }
        finally { _serial.Release(); }
    }

    private MabinogiCliResult Finish(MabinogiCliResult result)
    {
        string label = result.State == "blocked" ? result.Command + "_blocked" : result.Command;
        _log.Write($"[CLI] {label}: {result.State} · exit={result.ExitCode?.ToString() ?? "none"} · error={result.Error ?? "none"}");
        return result;
    }

    internal static MabinogiCliResult Parse(string command, CliProcessOutput output)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(output.Stdout.Trim().TrimStart('\uFEFF'));
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new(command, false, output.ExitCode == 5 ? "disconnected" : "error",
                null, output.ExitCode, "invalid_json");
        }

        JsonElement body = Property(root, "body") ?? Property(root, "Body") ?? root;
        if (body.ValueKind == JsonValueKind.String)
        {
            var textBody = body.GetString();
            if (!string.IsNullOrWhiteSpace(textBody))
            {
                try
                {
                    using var inner = JsonDocument.Parse(textBody);
                    body = inner.RootElement.Clone();
                }
                catch (JsonException) { }
            }
        }

        string? pipe = Text(root, "pipe") ?? Text(body, "pipe");
        string? reason = Text(root, "reason") ?? Text(body, "reason");
        string? status = Text(root, "status") ?? Text(root, "Status") ?? Text(body, "status") ?? Text(body, "Status");
        string? error = Text(body, "error") ?? Text(body, "Error") ?? Text(root, "error") ?? Text(root, "Error");
        if (reason is "game_off" or "option_off" || error is "game_off" or "option_off")
            return new(command, false, reason is "game_off" or "option_off" ? reason : error!, body, output.ExitCode, error ?? reason);
        if (output.ExitCode == 5 || pipe == "disconnected" || status == "disconnected" || error == "disconnected")
            return new(command, false, "disconnected", body, output.ExitCode, "disconnected");
        if (output.ExitCode != 0 || status is "rejected" or "error" or "failed" || error is not null)
            return new(command, false, "error", body, output.ExitCode, error ?? status ?? "cli_rejected");

        bool valid = command switch
        {
            "status" => pipe == "connected",
            "get_items" => body.ValueKind == JsonValueKind.Array,
            "capabilities" => body.ValueKind == JsonValueKind.Object,
            var c when IsAllowedAction(c) => body.ValueKind is JsonValueKind.Object or JsonValueKind.String,
            _ => body.ValueKind == JsonValueKind.Object
        };
        return new(command, valid, valid ? "connected" : "error", body, output.ExitCode,
            valid ? null : "unexpected_response");
    }

    internal static string BodyArgument(string json)
        => "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static JsonElement? Property(JsonElement value, string name)
        => TryProperty(value, name, out var property) ? property : null;

    private static bool TryProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in value.EnumerateObject())
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                { property = p.Value; return true; }
            }
        }
        property = default;
        return false;
    }

    private static string? Text(JsonElement value, string name)
    {
        var p = Property(value, name);
        if (p is null || p.Value.ValueKind is JsonValueKind.Null or JsonValueKind.False) return null;
        return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
    }

    private static async Task<CliProcessOutput> RunProcessAsync(string command, string? bodyArgument, CancellationToken token)
    {
        if (!IsAllowedQuery(command) && !IsAllowedAction(command))
            throw new InvalidOperationException("command_not_allowed");
        var start = new ProcessStartInfo(DefaultPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(command);
        if (bodyArgument is not null) start.ArgumentList.Add(bodyArgument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(IsAllowedAction(command) ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("cli_unavailable");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
    }

    private static bool IsAllowedQuery(string command)
        => command is "status" or "capabilities" or "get_items" or "get_activity" or "get_current_environment"
            or "get_alterable_items" or "get_altering_works" or "get_gatherable_items" or "get_inventory";

    private static bool IsAllowedAction(string command)
        => command is "execute_gathering" or "execute_altering" or "complete_altering_work" or "stop_action";
}
