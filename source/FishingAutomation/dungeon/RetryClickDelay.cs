namespace DungeonVisionBot;

internal static class RetryClickDelay
{
    internal const int Milliseconds = 5000;
    internal static Task WaitAsync(CancellationToken ct) => Task.Delay(Milliseconds, ct);
}
