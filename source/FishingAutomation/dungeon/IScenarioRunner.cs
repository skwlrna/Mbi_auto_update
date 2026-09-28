namespace DungeonVisionBot;
internal interface IScenarioRunner : IDisposable
{
    event Action<string>? Log;
    string InputMode { get; }
    Task RunAsync(CancellationToken ct);
}
