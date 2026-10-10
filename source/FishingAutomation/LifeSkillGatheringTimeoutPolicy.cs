namespace FishingAutomation;

// Each life-skill 100-action cycle has a sliding 30-minute no-gain window.
// Real inventory gains renew it; a separate 6-hour elapsed limit does not reset.
internal enum LifeSkillGatheringTimeoutStatus
{
    None,
    Stalled,
    NoProgress,
    HardLimit
}

internal static class LifeSkillGatheringTimeoutPolicy
{
    internal static readonly TimeSpan NoProgressWindow = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan StallAfterGain = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan AbsoluteLimit = TimeSpan.FromHours(6);

    internal static LifeSkillGatheringTimeoutStatus Evaluate(
        TimeSpan elapsed, TimeSpan lastProgressElapsed, bool sawGain)
    {
        if (elapsed < TimeSpan.Zero || lastProgressElapsed < TimeSpan.Zero ||
            lastProgressElapsed > elapsed)
            throw new ArgumentOutOfRangeException(nameof(elapsed));

        if (elapsed >= AbsoluteLimit)
            return LifeSkillGatheringTimeoutStatus.HardLimit;

        TimeSpan idle = elapsed - lastProgressElapsed;
        if (sawGain && idle >= StallAfterGain)
            return LifeSkillGatheringTimeoutStatus.Stalled;
        if (idle >= NoProgressWindow)
            return LifeSkillGatheringTimeoutStatus.NoProgress;
        return LifeSkillGatheringTimeoutStatus.None;
    }
}
