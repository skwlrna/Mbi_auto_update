namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// In-memory window counters for an explicitly authorized receive-volume probe.
/// This class never receives raw packet contents or endpoint identifiers.
/// Time buckets are monotonic seconds since capture startup, not wall-clock time.
/// No network/packet SDK is referenced here.
/// </summary>
internal sealed class ReceiveActivityWindows
{
    internal const int MaximumSeconds = CaptureProbeOptions.MaxSeconds;
    private readonly long[] _frames = new long[MaximumSeconds];
    private readonly long[] _bytes = new long[MaximumSeconds];

    internal void Record(TimeSpan elapsedSinceStart, int frameLength)
    {
        if (frameLength < 0 ||
            elapsedSinceStart < TimeSpan.Zero ||
            elapsedSinceStart >= TimeSpan.FromSeconds(MaximumSeconds))
            return;

        var bucket = (int)elapsedSinceStart.TotalSeconds;
        Interlocked.Increment(ref _frames[bucket]);
        Interlocked.Add(ref _bytes[bucket], frameLength);
    }

    internal ReceiveActivityWindow ReadCompletedSecond(int secondIndex)
    {
        if (secondIndex < 0 || secondIndex >= MaximumSeconds)
            throw new ArgumentOutOfRangeException(nameof(secondIndex));

        long frames = Interlocked.Read(ref _frames[secondIndex]);
        long bytes = Interlocked.Read(ref _bytes[secondIndex]);
        return new(secondIndex + 1, frames, bytes,
            frames > 0 ? "frames_observed" : "no_frames_in_window");
    }

    internal ReceiveActivitySummary Summarize(int elapsedWholeSeconds)
    {
        if (elapsedWholeSeconds < 0 || elapsedWholeSeconds > MaximumSeconds)
            throw new ArgumentOutOfRangeException(nameof(elapsedWholeSeconds));

        long frames = 0, bytes = 0;
        int activeWindows = 0, consecutiveQuietWindows = 0, longestQuietWindows = 0;
        for (int index = 0; index < elapsedWholeSeconds; index++)
        {
            long bucketFrames = Interlocked.Read(ref _frames[index]);
            frames += bucketFrames;
            bytes += Interlocked.Read(ref _bytes[index]);
            if (bucketFrames > 0)
            {
                activeWindows++;
                consecutiveQuietWindows = 0;
            }
            else
            {
                consecutiveQuietWindows++;
                longestQuietWindows = Math.Max(longestQuietWindows, consecutiveQuietWindows);
            }
        }

        // A quiet interval is not proof of a TCP disconnect, nor does active
        // traffic authenticate the client/game or establish an event meaning.
        string activity = frames == 0 ? "not_observed" :
            consecutiveQuietWindows == 0 ? "frames_recently_observed" :
            "quiet_after_observation";

        return new(frames, bytes, elapsedWholeSeconds, activeWindows,
            longestQuietWindows, consecutiveQuietWindows, activity);
    }
}

internal sealed record ReceiveActivityWindow(
    int Second,
    long Frames,
    long FrameBytes,
    string Activity);

internal sealed record ReceiveActivitySummary(
    long MatchedFrames,
    long FrameBytes,
    int ObservedSeconds,
    int SecondsWithFrames,
    int LongestQuietSeconds,
    int TrailingQuietSeconds,
    string Activity);
