namespace DungeonVisionBot;

// Observation-only. Never completes an Abyss transition, sends input or
// authorizes restart. Repeat notifications cannot be emitted in the same wait.
internal enum AbyssPassiveWaitKind { UnconfirmedLoading, Recruiting }

internal sealed class AbyssPassiveWaitAdvisory
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _threshold;
    private DateTimeOffset? _started;
    private AbyssPassiveWaitKind? _kind;
    private bool _alerted;

    internal AbyssPassiveWaitAdvisory(
        Func<DateTimeOffset>? clock = null,
        TimeSpan? threshold = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _threshold = threshold ?? TimeSpan.FromMinutes(15);
        if (_threshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(threshold));
    }

    internal TimeSpan? Observe(AbyssPassiveWaitKind kind)
    {
        var now = _clock();
        if (_kind != kind || _started is null || now < _started)
        {
            _kind = kind;
            _started = now;
            _alerted = false;
            return null;
        }
        if (_alerted || now - _started.Value < _threshold) return null;
        _alerted = true;
        return now - _started.Value;
    }
}
