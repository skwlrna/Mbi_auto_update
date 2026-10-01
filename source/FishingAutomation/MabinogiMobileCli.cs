using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FishingAutomation;

public sealed record MabinogiCliResult(string Command, bool Success, string State,
    JsonElement? Data, int? ExitCode, string? Error);

internal sealed record CliProcessOutput(int ExitCode, string Stdout, string Stderr);

/// <summary>Read-only CLI connector for automation. Cost/action commands are blocked at the process boundary.</summary>
public sealed class MabinogiMobileCli
{
    public const string DefaultPath = @"C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe";
    private readonly AppLog _log;
    private readonly Func<string, CancellationToken, Task<CliProcessOutput>> _run;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<CliProcessOutput>> _runArguments;
    public bool ZeroWingMode { get; }
    internal Func<IReadOnlyList<string>, CancellationToken, Task<CliProcessOutput>> RunFilteredQuery { get; set; } =
        (args, ct) => RunProcessArgumentsAsync(args, ct, TimeSpan.FromSeconds(15));

    public MabinogiMobileCli(AppLog log, bool zeroWingMode = true)
    {
        _log = log;
        ZeroWingMode = zeroWingMode;
        _run = RunProcessAsync;
        _runArguments = RunActionProcessAsync;
    }

    internal MabinogiMobileCli(AppLog log, bool zeroWingMode,
        Func<string, CancellationToken, Task<CliProcessOutput>> run)
    {
        _log = log;
        ZeroWingMode = zeroWingMode;
        _run = run;
        _runArguments = (args, token) =>
        {
            if (args.Count != 1) throw new InvalidOperationException("action_runner_not_configured");
            return run(args[0], token);
        };
    }

    internal MabinogiMobileCli(AppLog log, bool zeroWingMode,
        Func<string, CancellationToken, Task<CliProcessOutput>> run,
        Func<IReadOnlyList<string>, CancellationToken, Task<CliProcessOutput>> runArguments)
    {
        _log = log;
        ZeroWingMode = zeroWingMode;
        _run = run;
        _runArguments = runArguments;
    }

    public Task<MabinogiCliResult> StatusAsync(CancellationToken token = default) => QueryAsync("status", token);
    public Task<MabinogiCliResult> GetItemsAsync(CancellationToken token = default) => QueryAsync("get_items", token);
    public Task<MabinogiCliResult> GetActivityAsync(CancellationToken token = default) => QueryAsync("get_activity", token);
    public Task<MabinogiCliResult> GetCurrentEnvironmentAsync(CancellationToken token = default) => QueryAsync("get_current_environment", token);
    public Task<MabinogiCliResult> GetAlterableItemsAsync(CancellationToken token = default) => QueryAsync("get_alterable_items", token);
    public Task<MabinogiCliResult> GetAlteringWorksAsync(CancellationToken token = default) => QueryAsync("get_altering_works", token);
    public Task<MabinogiCliResult> GetGatherableItemsAsync(CancellationToken token = default) => QueryAsync("get_gatherable_items", token);
    public Task<MabinogiCliResult> GetGatherableItemsAsync(string filterName, CancellationToken token = default)
        => FilteredQueryAsync("get_gatherable_items", filterName, token);
    public Task<MabinogiCliResult> GetInventoryAsync(CancellationToken token = default) => QueryAsync("get_inventory", token);
    public Task<MabinogiCliResult> CapabilitiesAsync(CancellationToken token = default) => QueryAsync("capabilities", token);
    public Task<MabinogiCliResult> GetMyInfoAsync(CancellationToken token = default) => QueryAsync("get_my_info", token);
    public Task<MabinogiCliResult> GetCurrenciesAsync(CancellationToken token = default) => QueryAsync("get_currencies", token);
    public Task<MabinogiCliResult> ExecuteGatheringAsync(string displayName, CancellationToken token = default)
        => ActionAsync("execute_gathering", displayName, token);
    public Task<MabinogiCliResult> ExecuteAlteringAsync(string displayName, CancellationToken token = default)
        => ActionAsync("execute_altering", displayName, token);
    public Task<MabinogiCliResult> CompleteAlteringWorkAsync(string displayName, CancellationToken token = default)
        => ActionAsync("complete_altering_work", displayName, token);
    public Task<MabinogiCliResult> StopActionAsync(CancellationToken token = default)
        => ActionAsync("stop_action", null, token);

    private async Task<MabinogiCliResult> ActionAsync(string command, string? displayName, CancellationToken token)
    {
        if (!IsAllowedAction(command))
            return Finish(new(command, false, "blocked", null, null, "command_not_allowed"));
        if (command == "stop_action" ? displayName is not null :
            string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256)
            return Finish(new(command, false, "blocked", null, null, "invalid_body"));

        token.ThrowIfCancellationRequested();
        try
        {
            var output = await _runArguments(BuildActionArguments(command, displayName), token).ConfigureAwait(false);
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
    }

    internal static IReadOnlyList<string> BuildActionArguments(string command, string? displayName)
    {
        if (!IsAllowedAction(command)) throw new InvalidOperationException("command_not_allowed");
        if (command == "stop_action")
        {
            if (displayName is not null) throw new InvalidOperationException("invalid_body");
            return new[] { command };
        }
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256)
            throw new InvalidOperationException("invalid_body");
        string json = JsonSerializer.Serialize(new { displayName });
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return new[] { command, "base64:" + encoded };
    }

    internal static IReadOnlyList<string> BuildFilteredQueryArguments(string command, string filterName)
    {
        if (command != "get_gatherable_items") throw new InvalidOperationException("command_not_allowed");
        string filter = filterName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(filter) || filter.Length > 256 || filter.Any(char.IsControl))
            throw new InvalidOperationException("invalid_body");
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(filter));
        return new[] { command, "base64:" + encoded };
    }

    private async Task<MabinogiCliResult> FilteredQueryAsync(
        string command, string filterName, CancellationToken token)
    {
        if (command != "get_gatherable_items")
            return Finish(new(command, false, "blocked", null, null, "command_not_allowed"));
        if (string.IsNullOrWhiteSpace(filterName) || filterName.Length > 256 || filterName.Any(char.IsControl))
            return Finish(new(command, false, "blocked", null, null, "invalid_body"));

        token.ThrowIfCancellationRequested();
        try
        {
            var output = await RunFilteredQuery(
                BuildFilteredQueryArguments(command, filterName), token).ConfigureAwait(false);
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
    }

    public async Task<MabinogiCliResult> QueryAsync(string command, CancellationToken token = default)
    {
        // Exact allowlist also rejects extra arguments, shell syntax, and every future command.
        // Costs remain blocked even if ZeroWingMode is explicitly set to false in this phase.
        if (!IsAllowedQuery(command))
            return Finish(new(command, false, "blocked", null, null, "command_not_allowed"));

        token.ThrowIfCancellationRequested();
        try
        {
            var output = await _run(command, token).ConfigureAwait(false);
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
    }

    private MabinogiCliResult Finish(MabinogiCliResult result)
    {
        // Never echo arbitrary rejected input or CLI stderr. Successful read-only
        // polling is still written to the file, but hidden from the on-screen log.
        // Failures/rejections plus startup status/capabilities remain visible.
        string label = result.State == "blocked" ? "blocked_command" : result.Command;
        string line = $"[CLI] {label}: {result.State} · exit={result.ExitCode?.ToString() ?? "none"} · error={result.Error ?? "none"}";
        bool backgroundOnly = result.Success &&
            result.Command is not ("status" or "capabilities");
        if (backgroundOnly)
            _log.WriteBackground(line);
        else
            _log.Write(line);
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

        string? status = Text(root, "status");
        JsonElement body = Property(root, "body") ?? root;
        if (body.ValueKind == JsonValueKind.String)
        {
            string? text = body.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var bodyDoc = JsonDocument.Parse(text);
                    body = bodyDoc.RootElement.Clone();
                }
                catch (JsonException) { }
            }
        }
        string? pipe = Text(body, "pipe") ?? Text(root, "pipe");
        string? reason = Text(body, "reason") ?? Text(root, "reason");
        string? error = Text(body, "error") ?? Text(root, "error");
        status ??= Text(body, "status");
        if (reason is "game_off" or "option_off" || error is "game_off" or "option_off")
            return new(command, false, reason is "game_off" or "option_off" ? reason : error!, body.Clone(), output.ExitCode, error ?? reason);
        if (output.ExitCode == 5 || pipe == "disconnected" || status == "disconnected" || error == "disconnected")
            return new(command, false, "disconnected", body.Clone(), output.ExitCode, "disconnected");
        if (output.ExitCode != 0 || status is "rejected" or "error" or "failed" || error is not null)
            return new(command, false, "error", body.Clone(), output.ExitCode, "cli_rejected");

        bool valid = command switch
        {
            "status" => pipe == "connected",
            "get_items" or "get_currencies" => body.ValueKind == JsonValueKind.Array,
            _ => body.ValueKind == JsonValueKind.Object
        };
        return new(command, valid, valid ? "connected" : "error", body.Clone(), output.ExitCode,
            valid ? null : "unexpected_response");
    }

    private static JsonElement? Property(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in value.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string? Text(JsonElement value, string name)
    {
        var p = Property(value, name);
        if (p is null || p.Value.ValueKind is JsonValueKind.Null or JsonValueKind.False) return null;
        return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
    }

    private static Task<CliProcessOutput> RunProcessAsync(string command, CancellationToken token)
    {
        if (!IsAllowedQuery(command))
            throw new InvalidOperationException("command_not_allowed");
        return RunProcessArgumentsAsync(new[] { command }, token, TimeSpan.FromSeconds(15));
    }

    private static Task<CliProcessOutput> RunActionProcessAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        if (args.Count == 0 || !IsAllowedAction(args[0]))
            throw new InvalidOperationException("command_not_allowed");
        bool shapeOk = args[0] == "stop_action"
            ? args.Count == 1
            : args.Count == 2 && args[1].StartsWith("base64:", StringComparison.Ordinal);
        if (!shapeOk) throw new InvalidOperationException("invalid_body");
        // Action commands can legitimately wait for the game. Match mobi-Support:
        // cancellation is user-controlled, but there is no arbitrary process timeout.
        return RunProcessArgumentsAsync(args, token, null);
    }

    private static async Task<CliProcessOutput> RunProcessArgumentsAsync(
        IReadOnlyList<string> args, CancellationToken token, TimeSpan? timeoutValue)
    {
        var start = new ProcessStartInfo(DefaultPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (timeoutValue is TimeSpan limit) timeout.CancelAfter(limit);
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
        => command is "status" or "capabilities" or "get_my_info" or "get_currencies"
            or "get_items" or "get_activity" or "get_current_environment"
            or "get_alterable_items" or "get_altering_works" or "get_gatherable_items" or "get_inventory";

    // execute_gathering consumes five spirit wings. The ZIP's free screen paths
    // must never launch it, even if ZeroWingMode is false or requiresConfirm is false.
    private static bool IsAllowedAction(string command)
        => false;
}
