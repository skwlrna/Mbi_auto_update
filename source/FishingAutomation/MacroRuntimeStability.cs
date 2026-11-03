using System.Text.Json;

namespace FishingAutomation;

// Diagnostic/runtime ownership only. Never sends game input, writes a recipe
// ledger, confirms a receipt, or authorizes an Abyss step/clear.
internal enum MacroStableMode
{
    None,
    Crafting,
    Altering,
    MultiAltering,
    Abyss,
    Scheduler
}

internal enum MacroStableState
{
    Idle,
    Starting,
    Running,
    Quiet,
    ReviewRequired,
    StopRequested
}

internal sealed record MacroStabilityEvent(
    DateTimeOffset Utc,
    string Code,
    string Mode,
    string State,
    int RecoveryObservations,
    long QuietSeconds);

internal sealed class MacroStabilitySupervisor
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<MacroStabilityEvent>? _emit;
    private readonly TimeSpan _quietAfter;
    private readonly int _maxRecoveries;
    private MacroStableMode _owner = MacroStableMode.None;
    private MacroStableState _state = MacroStableState.Idle;
    private DateTimeOffset _lastActivity;
    private int _idleFrames;
    private int _recoveries;
    private bool _conflictReported;

    internal MacroStabilitySupervisor(
        Func<DateTimeOffset>? clock = null,
        Action<MacroStabilityEvent>? emit = null,
        TimeSpan? quietAfter = null,
        int maxRecoveries = 2)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _emit = emit;
        _quietAfter = quietAfter ?? TimeSpan.FromMinutes(15);
        if (_quietAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(quietAfter));
        if (maxRecoveries < 0 || maxRecoveries > 100)
            throw new ArgumentOutOfRangeException(nameof(maxRecoveries));
        _maxRecoveries = maxRecoveries;
    }

    internal static MacroStableMode FromUi(string mode) => mode switch
    {
        "제작" => MacroStableMode.Crafting,
        "가공" => MacroStableMode.Altering,
        "어비스" => MacroStableMode.Abyss,
        "스케줄러" => MacroStableMode.Scheduler,
        _ => MacroStableMode.None
    };

    internal MacroStableMode Owner
    {
        get { lock (_gate) return _owner; }
    }

    internal MacroStableState State
    {
        get { lock (_gate) return _state; }
    }

    // UI-level admission guard in addition to MainForm.AnyRunning.
    // A different mode cannot take over an unfinished run, including its
    // asynchronous start and F10 cancellation window.
    internal bool TryBegin(MacroStableMode mode)
    {
        if (mode == MacroStableMode.None)
            throw new ArgumentOutOfRangeException(nameof(mode));
        lock (_gate)
        {
            if (_owner != MacroStableMode.None)
            {
                EventLocked("START_REJECTED");
                return false;
            }
            BeginLocked(mode, "START_ACCEPTED");
            return true;
        }
    }

    private void BeginLocked(MacroStableMode mode, string code)
    {
        _owner = mode;
        _state = MacroStableState.Starting;
        _idleFrames = 0;
        _recoveries = 0;
        _conflictReported = false;
        _lastActivity = _clock();
        EventLocked(code);
    }

    // Poll only observed WinForms mode/worker flags. This is not evidence of
    // crafting output, inventory receipt, or successful Abyss completion.
    internal void ObserveRuntime(MacroStableMode observed, bool busy)
    {
        lock (_gate)
        {
            if (!busy)
            {
                if (_owner != MacroStableMode.None && ++_idleFrames >= 2)
                {
                    EventLocked("RUN_ENDED_OBSERVED");
                    ResetLocked();
                }
                return;
            }
            _idleFrames = 0;
            if (observed == MacroStableMode.None) return;
            if (_owner == MacroStableMode.None)
                BeginLocked(observed, "RUN_ADOPTED");

            // A direct multi-altering run may begin through its dedicated F9
            // entry and initially be labelled '가공' by the main UI.
            if (_owner == MacroStableMode.Altering &&
                observed == MacroStableMode.MultiAltering)
            {
                _owner = observed;
                EventLocked("MULTI_ALTERING_IDENTIFIED");
            }
            else if (_owner != observed &&
                     !(_owner == MacroStableMode.Scheduler &&
                       observed is MacroStableMode.Crafting or
                                   MacroStableMode.Altering or
                                   MacroStableMode.MultiAltering or
                                   MacroStableMode.Abyss))
            {
                if (!_conflictReported)
                {
                    _conflictReported = true;
                    EventLocked("OWNER_CONFLICT_OBSERVED");
                }
                // Never promote a conflicting owner or replay an input.
                return;
            }

            if (_state == MacroStableState.Starting)
            {
                _state = MacroStableState.Running;
                EventLocked("RUNNING_OBSERVED");
            }
        }
    }

    private static bool Relevant(MacroStableMode mode, string line)
    {
        if (line.Contains("[안정화]", StringComparison.Ordinal) ||
            line.Contains("오류", StringComparison.Ordinal) ||
            line.Contains("실패", StringComparison.Ordinal) ||
            line.Contains("안전 정지", StringComparison.Ordinal))
            return false;
        bool craft = line.Contains("[제작]", StringComparison.Ordinal);
        bool alter = line.Contains("[다중가공]", StringComparison.Ordinal) ||
                     line.Contains("[중간재료 스케줄]", StringComparison.Ordinal);
        bool abyss = line.Contains("[어비스]", StringComparison.Ordinal);
        return mode switch
        {
            MacroStableMode.Crafting => craft,
            MacroStableMode.Altering => line.Contains("[가공]", StringComparison.Ordinal),
            MacroStableMode.MultiAltering => alter,
            MacroStableMode.Abyss => abyss,
            MacroStableMode.Scheduler => craft || alter || abyss ||
                line.Contains("[스케줄러]", StringComparison.Ordinal),
            _ => false
        };
    }

    // Activity logs are *not* proof of game-side progress. The only automatic
    // recovery here is clearing a transient quiet-state diagnostic after fresh
    // local activity; no clicks, resumption, or durable credit are replayed.
    internal void ObserveActivity(string line)
    {
        lock (_gate)
        {
            if (_owner == MacroStableMode.None ||
                _state is MacroStableState.StopRequested or MacroStableState.ReviewRequired ||
                !Relevant(_owner, line))
                return;
            var now = _clock();
            if (now < _lastActivity)
            {
                // System clock correction must not manufacture a long stall.
                _lastActivity = now;
                return;
            }
            if (_state == MacroStableState.Quiet)
            {
                if (_recoveries >= _maxRecoveries)
                {
                    _state = MacroStableState.ReviewRequired;
                    EventLocked("QUIET_RECOVERY_LIMIT");
                    return;
                }
                _recoveries++;
                _state = MacroStableState.Running;
                EventLocked("ACTIVITY_RESUMED_NO_INPUT");
            }
            _lastActivity = now;
        }
    }

    // One quiet alert per episode; further action is intentionally reserved
    // for existing mode-specific, evidence-verified recovery paths.
    internal bool Tick()
    {
        lock (_gate)
        {
            if (_state is not (MacroStableState.Starting or MacroStableState.Running))
                return false;
            var now = _clock();
            if (now < _lastActivity)
            {
                _lastActivity = now;
                return false;
            }
            if (now - _lastActivity < _quietAfter) return false;
            _state = MacroStableState.Quiet;
            EventLocked("QUIET_ACTIVITY_WARNING");
            return true;
        }
    }

    internal void RequestStop()
    {
        lock (_gate)
        {
            if (_owner == MacroStableMode.None || _state == MacroStableState.StopRequested)
                return;
            _state = MacroStableState.StopRequested;
            EventLocked("STOP_REQUESTED_NO_REPLAY");
        }
    }

    // Only synchronous setup failure before work is observed may drop a lease.
    internal void AbortUnstarted()
    {
        lock (_gate)
        {
            if (_state != MacroStableState.Starting) return;
            EventLocked("START_ABORTED");
            ResetLocked();
        }
    }

    private void ResetLocked()
    {
        _owner = MacroStableMode.None;
        _state = MacroStableState.Idle;
        _idleFrames = 0;
        _recoveries = 0;
        _conflictReported = false;
    }

    private void EventLocked(string code)
    {
        var now = _clock();
        long quiet = now >= _lastActivity
            ? (long)(now - _lastActivity).TotalSeconds : 0;
        try
        {
            _emit?.Invoke(new MacroStabilityEvent(
                now, code, _owner.ToString(), _state.ToString(),
                _recoveries, quiet));
        }
        catch (IOException) { } // Diagnostic logging cannot crash game control.
        catch (UnauthorizedAccessException) { }
    }
}

// Small, local-only, bounded JSONL log, intentionally without user identity,
// CLI payloads, inventory items, screenshots or raw packet contents.
internal static class MacroStabilityJournal
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MabiAuto", "macro-stability-events.jsonl");

    internal static void Append(MacroStabilityEvent entry)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_048_576)
                    File.WriteAllText(FilePath, "");
                File.AppendAllText(FilePath,
                    JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
