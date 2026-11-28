using FishingAutomation;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private sealed record AbyssWait(string Label, long StartedAt, int? Limit);
    private volatile AbyssWait? _abyssWait;
    private long? _reportedCombatStart;
    private bool _reportedReward;
    private long? _reportedFightSeconds;
    private IReadOnlyDictionary<string, int>? _reportedLoot;
    private int _reportedRecoveryAttempts;

    internal string AbyssDungeonName { get; set; } = "어비스";

    internal string? AbyssWaitDescription
    {
        get
        {
            if (_abyssWait is not { } wait) return null;
            long seconds = (Environment.TickCount64 - wait.StartedAt) / 1000;
            return wait.Limit is int limit
                ? $"{wait.Label} · {Math.Min(seconds, limit)}/{limit}초"
                : $"{wait.Label} · {seconds}초";
        }
    }

    partial void ReportAbyssWait(string label, int? limit)
    {
        _abyssWait = new AbyssWait(label, Environment.TickCount64, limit);
    }

    partial void ReportAbyssReward()
    {
        if (_reportedReward) return;
        _reportedReward = true;
        if (_reportedCombatStart is long started)
            _reportedFightSeconds = (Environment.TickCount64 - started) / 1000;
    }

    partial void ReportAbyssLoot(IReadOnlyDictionary<string, int> gains) =>
        _reportedLoot = new Dictionary<string, int>(gains);

    private void FinishAbyssReport(string outcome)
    {
        if (_reportedCombatStart is not null || _reportedReward)
        {
            AbyssRoundHistory.Append(new AbyssRoundRecord(DateTimeOffset.Now, AbyssDungeonName,
                _reportedFightSeconds, _reportedReward, _reportedLoot, _reportedRecoveryAttempts, outcome));
        }
        _reportedCombatStart = null;
        _reportedReward = false;
        _reportedFightSeconds = null;
        _reportedLoot = null;
        _reportedRecoveryAttempts = 0;
    }
}
