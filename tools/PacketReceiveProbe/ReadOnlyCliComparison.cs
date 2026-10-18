using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// Executes only two existing read-only game CLI queries. No CLI output,
/// identities, inventories or JSON bodies are ever printed or persisted.
/// An accepted CLI response is not proof of authenticated packet ownership.
/// </summary>
internal static class ReadOnlyCliComparison
{
    internal const string DefaultCliPath =
        @"C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe";
    internal const int MaximumOutputCharacters = 16 * 1024;
    internal static readonly TimeSpan PerQueryTimeout = TimeSpan.FromSeconds(5);

    internal static bool IsAllowed(string command) =>
        command is "status" or "get_activity";

    // Matches the current MabinogiMobileCli.Parse contract, but exposes only
    // minimal status categories and never the original JSON or character data.
    // In particular, root.status is OPTIONAL: pipe and nested body carry state.
    internal static string ClassifyResponse(int exitCode, string? stdout,
        string command = "get_activity")
    {
        if (!IsAllowed(command))
            return "cli_command_blocked";
        if (string.IsNullOrWhiteSpace(stdout))
            return exitCode == 5 ? "cli_disconnected" : "cli_empty";
        if (stdout.Length > MaximumOutputCharacters)
            return "cli_oversize";
        try
        {
            using var doc = JsonDocument.Parse(stdout.Trim().TrimStart('\uFEFF'),
                new JsonDocumentOptions { MaxDepth = 20, AllowTrailingCommas = false });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "cli_invalid_schema";
            var body = Property(root, "body") ?? root;
            if (body.ValueKind == JsonValueKind.String)
            {
                string? nested = body.GetString();
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    try
                    {
                        using var nestedDoc = JsonDocument.Parse(nested);
                        body = nestedDoc.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        // Mirror the existing macro's fallback: retain string body.
                    }
                }
            }
            string? status = Text(root, "status") ?? Text(body, "status");
            string? pipe = Text(body, "pipe") ?? Text(root, "pipe");
            string? reason = Text(body, "reason") ?? Text(root, "reason");
            string? error = Text(body, "error") ?? Text(root, "error");
            if (reason is "game_off" or "option_off" ||
                error is "game_off" or "option_off")
                return "cli_game_or_option_off";
            if (exitCode == 5 || pipe == "disconnected" ||
                status == "disconnected" || error == "disconnected")
                return "cli_disconnected";
            if (exitCode != 0 || status is "rejected" or "error" or "failed" ||
                error is not null)
                return "cli_rejected";
            if (command == "status")
                return pipe == "connected" ? "cli_connected" :
                    "cli_unclassified_status";
            if (body.ValueKind == JsonValueKind.Object)
                return "cli_readonly_object";
            return "cli_unexpected_response";
        }
        catch (JsonException)
        {
            return "cli_invalid_json";
        }
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }

    private static string? Text(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value is { ValueKind: JsonValueKind.String } ?
            value.Value.GetString() : null;
    }

    internal static async Task<string> QueryAsync(
        string command, CancellationToken cancellationToken)
    {
        if (!IsAllowed(command))
            return "cli_command_blocked";
        if (!File.Exists(DefaultCliPath))
            return "cli_not_installed";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PerQueryTimeout);
        try
        {
            var startInfo = new ProcessStartInfo(DefaultCliPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add(command);
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return "cli_not_started";
            try
            {
                // Limits are time-bound. Only a short read-only CLI output is
                // expected; no content is displayed or saved at any time.
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string response = await stdout.ConfigureAwait(false);
                await stderr.ConfigureAwait(false);
                return ClassifyResponse(process.ExitCode, response, command);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? "cli_cancelled" : "cli_timeout";
        }
        catch (Exception e) when (e is IOException or Win32Exception or
            InvalidOperationException or UnauthorizedAccessException)
        {
            return "cli_unavailable";
        }
    }
}
