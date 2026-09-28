namespace DungeonVisionBot;

internal static class RetryClickDelay
{
    internal const int Milliseconds = 1000;
    internal static Task WaitAsync(CancellationToken ct) => Task.Delay(Milliseconds, ct);
}
