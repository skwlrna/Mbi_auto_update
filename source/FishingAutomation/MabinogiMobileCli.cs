using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FishingAutomation;

public sealed record MabinogiCliResult(string Command, bool Success, string State,
    JsonElement? Data, int? ExitCode, string? Error);

internal sealed record CliProcessOutput(int ExitCode, string Stdout, string Stderr);

/// <summary>Read-only CLI connector. No action commands can reach the process boundary.</summary>
public sealed class MabinogiMobileCli
{
    public const string DefaultPath = @"C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe";
    private readonly AppLog _log;
    private readonly Func<string, CancellationToken, Task<CliProcessOutput>> _run;
    public bool ZeroWingMode { get; }

    public MabinogiMobileCli(AppLog log, bool zeroWingMode = true)
        : this(log, zeroWingMode, RunProcessAsync) { }

    internal MabinogiMobileCli(AppLog log, bool zeroWingMode,
        Func<string, CancellationToken, Task<CliProcessOutput>> run)
    {
        _log = log;
        ZeroWingMode = zeroWingMode;
        _run = run;
    }

    public Task<MabinogiCliResult> StatusAsync(CancellationToken token = default) => QueryAsync("status", token);
    public Task<MabinogiCliResult> GetItemsAsync(CancellationToken token = default) => QueryAsync("get_items", token);
    public Task<MabinogiCliResult> GetActivityAsync(CancellationToken token = default) => QueryAsync("get_activity", token);
    public Task<MabinogiCliResult> GetCurrentEnvironmentAsync(CancellationToken token = default) => QueryAsync("get_current_environment", token);

    public async Task<MabinogiCliResult> QueryAsync(string command, CancellationToken token = default)
    {
        // Exact allowlist also rejects extra arguments, shell syntax, and every future command.
        // Costs remain blocked even if ZeroWingMode is explicitly set to false in this phase.
        if (command is not ("status" or "get_items" or "get_activity" or "get_current_environment"))
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
        // Inventory and environment data stay out of the existing log.
        // Do not echo arbitrary rejected input or CLI stderr into the log.
        string label = result.State == "blocked" ? "blocked_command" : result.Command;
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

        string? pipe = Text(root, "pipe");
        string? reason = Text(root, "reason");
        string? status = Text(root, "status");
        string? error = Text(root, "error");
        JsonElement body = Property(root, "body") ?? root;
        reason ??= Text(body, "reason");
        error ??= Text(body, "error");
        status ??= Text(body, "status");
        if (reason is "game_off" or "option_off" || error is "game_off" or "option_off")
            return new(command, false, reason is "game_off" or "option_off" ? reason : error!, root, output.ExitCode, error ?? reason);
        if (output.ExitCode == 5 || pipe == "disconnected" || status == "disconnected" || error == "disconnected")
            return new(command, false, "disconnected", root, output.ExitCode, "disconnected");
        if (output.ExitCode != 0 || status is "rejected" or "error" or "failed" || error is not null)
            return new(command, false, "error", root, output.ExitCode, "cli_rejected");

        bool valid = command switch
        {
            "status" => pipe == "connected",
            "get_items" => body.ValueKind == JsonValueKind.Array,
            _ => body.ValueKind == JsonValueKind.Object
        };
        return new(command, valid, valid ? "connected" : "error", root, output.ExitCode,
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

    private static async Task<CliProcessOutput> RunProcessAsync(string command, CancellationToken token)
    {
        // Defense at the actual launch boundary as well as the public API.
        if (command is not ("status" or "get_items" or "get_activity" or "get_current_environment"))
            throw new InvalidOperationException("command_not_allowed");
        var start = new ProcessStartInfo(DefaultPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(command);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
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
}
