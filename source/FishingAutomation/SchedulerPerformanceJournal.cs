using System.Text.Json;

namespace FishingAutomation;

// Observational scheduler outcome; "completed" means the app's scheduler
// finished its selected phases, NOT that live-game quality was proven.
public sealed record SchedulerRunPerformance(
    string RunId,
    int ProcessId,
    DateTimeOffset EndedUtc,
    string Outcome,
    long TotalMilliseconds,
    IReadOnlyDictionary<string, long> PhaseMilliseconds);

internal static class SchedulerPerformanceJournal
{
    private const int MaximumRecords = 30;
    private const int MaximumBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal static void Append(string path, SchedulerRunPerformance result)
    {
        // The journal is diagnostic only; no game control depends on it.
        // Callers must not retry a scheduler task after a write failure.
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);

            List<SchedulerRunPerformance> recent = new();
            if (File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) &&
                new FileInfo(path).Length <= MaximumBytes)
            {
                try
                {
                    recent = JsonSerializer.Deserialize<List<SchedulerRunPerformance>>(
                        File.ReadAllText(path), JsonOptions) ?? new();
                }
                catch (JsonException)
                {
                    // Corrupt statistics never authorize recovery and never
                    // prevent the scheduler from completing.
                }
            }

            recent = recent.Where(x => x is not null &&
                                  x.RunId?.Length == 32 &&
                                  (x.Outcome == "completed" ||
                                   x.Outcome == "cancelled" || x.Outcome == "failed"))
                .TakeLast(MaximumRecords - 1).ToList();
            recent.RemoveAll(x => x.RunId == result.RunId);
            recent.Add(result);
            string tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(recent, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (NotSupportedException) { }
    }
}
