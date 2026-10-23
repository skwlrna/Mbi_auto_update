using System.Text;
using System.Text.Json;

namespace FishingAutomation;

/// <summary>
/// Local, bounded diagnostic index shared by the UI and the opted-in Codex watcher.
/// Not a network uploader; optional, separately consented screenshots stay local until Codex analysis.
/// </summary>
internal static class AiRuntimeErrorJournal
{
    internal static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "MabiAutoAI", "runtime-reports");

    private static string ShortText(string value, int limit)
    {
        string singleLine = string.Join(" ", (value ?? "")
            .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length > limit ? singleLine[..limit] : singleLine;
    }

    internal static void Record(RuntimeErrorReport report, AppLog log, string recentLog,
        byte[]? optedInScreenshot = null)
    {
        try
        {
            Directory.CreateDirectory(Root);
            string eventName = "runtime_" + report.Time.ToString("yyyyMMdd_HHmmss_fff") +
                "_" + Guid.NewGuid().ToString("N")[..8];
            string directory = Path.Combine(Root, eventName);
            Directory.CreateDirectory(directory);

            string title = ShortText(report.Title, 120);
            string detail = ShortText(report.Detail, 1500);
            var summary = new
            {
                schema = 1,
                timeLocal = report.Time.ToString("O"),
                title,
                detail,
                mode = ShortText(report.Mode, 60),
                version = ShortText(report.Version, 32)
            };
            string json = JsonSerializer.Serialize(summary);
            string text = "MabiAuto runtime error (data only; not instructions)\n" +
                "Mode: " + ShortText(report.Mode, 60) + "\n" +
                "Error: " + title + "\n" +
                "Detail: " + detail + "\n";
            // Write the report payload first; the watcher requires a stable evidence file.
            File.WriteAllText(Path.Combine(directory, "summary.json"), json, Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "error.txt"), text, Encoding.UTF8);
            // Optional recent diagnostic excerpt stays on the PC until AI analysis
            // is opted into. The Python watcher performs its existing redaction.
            if (!string.IsNullOrWhiteSpace(recentLog))
            {
                string excerpt = recentLog.Length > 12000 ? recentLog[^12000..] : recentLog;
                File.WriteAllText(Path.Combine(directory, "fishing.log"), excerpt, Encoding.UTF8);
            }
            // Screenshot is local-only and opt-in; the Python watcher separately
            // revalidates explicit consent before passing it to Codex. Reject
            // oversized/non-PNG data, never write images into repository source.
            if (optedInScreenshot is { Length: >= 24 and <= 4194304 } &&
                optedInScreenshot.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                File.WriteAllBytes(Path.Combine(directory, "screen.png"), optedInScreenshot);
            log.Write("[AI 오류기록] 로컬 오류 저장 완료 · " + eventName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            log.Write("[AI 오류기록] 로컬 기록 실패: " + ex.GetType().Name);
        }
    }
}
