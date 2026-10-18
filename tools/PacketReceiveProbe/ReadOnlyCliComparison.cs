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

    internal static string ClassifyResponse(int exitCode, string? stdout)
    {
        if (exitCode != 0)
            return "cli_nonzero_exit";
        if (string.IsNullOrWhiteSpace(stdout))
            return "cli_empty";
        if (stdout.Length > MaximumOutputCharacters)
            return "cli_oversize";
        try
        {
            using var doc = JsonDocument.Parse(stdout, new JsonDocumentOptions
            {
                MaxDepth = 20, AllowTrailingCommas = false
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return "cli_invalid_schema";
            if (!doc.RootElement.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.String)
                return "cli_json_without_status";
            return status.GetString() switch
            {
                "accepted" => "cli_accepted",
                "success" => "cli_success",
                "rejected" => "cli_rejected",
                _ => "cli_other_status"
            };
        }
        catch (JsonException)
        {
            return "cli_invalid_json";
        }
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
                return ClassifyResponse(process.ExitCode, response);
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
