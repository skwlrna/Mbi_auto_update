using System.Text.Json;

namespace FishingAutomation;

/// <summary>
/// Observational, process-local scheduler guard and progress timeline.
/// Never restarts work, presses game input, or infers server-side completion.
/// The durable snapshot is diagnostics only, never an automatic resume record.
/// </summary>
public sealed class SchedulerRunMonitor
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _historyPath;
    private readonly Dictionary<string, long> _phaseMilliseconds = new(StringComparer.Ordinal);
    private SchedulerRunPerformance? _lastRun;
    private readonly Func<DateTimeOffset> _clock;
    private bool _running;
    private bool _stopRequested;
    private bool _advised;
    private string _runId = "";
    private string _phase = "idle";
    private string _outcome = "idle";
    private string _plan = "";
    // Diagnostics only. These labels are not permission to resume or replay
    // any crafting, registration, receipt or game action.
    private string _lastVerifiedBoundary = "none";
    private DateTimeOffset _startedAt;
    private DateTimeOffset _phaseAt;
    private DateTimeOffset _activityAt;
    private DateTimeOffset _observedAt;
    private DateTimeOffset _writtenAt;

    public SchedulerRunMonitor(string? path = null, Func<DateTimeOffset>? clock = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MabiAuto", "scheduler-runtime.json");
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _historyPath = Path.Combine(Path.GetDirectoryName(_path) ??
            Environment.CurrentDirectory, "scheduler-performance.json");
    }

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public SchedulerRunPerformance? LastRun
    {
        get { lock (_gate) return _lastRun; }
    }

    public string LastRunSummary
    {
        get
        {
            lock (_gate)
            {
                if (_lastRun is null) return "아직 실행 기록 없음";
                string metric(string name) =>
                    _lastRun.PhaseMilliseconds.TryGetValue(name, out long ms)
                        ? TimeSpan.FromMilliseconds(ms).ToString(@"hh\:mm\:ss") : "—";
                return $"결과={_lastRun.Outcome} · 총 {TimeSpan.FromMilliseconds(_lastRun.TotalMilliseconds):hh\\:mm\\:ss}" +
                       $" · 준비/선택 {metric("resume_selection")}" +
                       $" · 제작 {metric("crafting")} · 전환 {metric("handoff")}" +
                       $" · 가공 {metric("altering")} · 로컬 통계만 기록";
            }
        }
    }

    private void ClosePhaseLocked(DateTimeOffset at)
    {
        long ms = Math.Max(0, Math.Min((long)TimeSpan.FromDays(30).TotalMilliseconds,
            (long)(at - _phaseAt).TotalMilliseconds));
        _phaseMilliseconds[_phase] = (_phaseMilliseconds.GetValueOrDefault(_phase)) + ms;
    }

    private Dictionary<string, long> PhaseSnapshotLocked(DateTimeOffset at)
    {
        var snapshot = new Dictionary<string, long>(_phaseMilliseconds, StringComparer.Ordinal);
        if (_running)
        {
            long elapsed = Math.Max(0, Math.Min((long)TimeSpan.FromDays(30).TotalMilliseconds,
                (long)(at - _phaseAt).TotalMilliseconds));
            snapshot[_phase] = snapshot.GetValueOrDefault(_phase) + elapsed;
        }
        return snapshot;
    }

    public string PhaseLabel
    {
        get
        {
            lock (_gate)
                return _phase switch
                {
                    "preparing" => "작업 준비",
                    "resume_selection" => "이전 작업 확인 대기",
                    "crafting" => "제작 진행",
                    "handoff" => "제작 입력권 반환 확인",
                    "altering" => "다중가공 진행",
                    "stopping" => "사용자 정지 처리 중",
                    "completed" => "정상 완료",
                    "cancelled" => "사용자 중단",
                    "failed" => "안전 정지",
                    _ => "대기"
                };
        }
    }

    public bool TryStart(bool crafting, bool altering, bool abyss)
    {
        lock (_gate)
        {
            if (_running) return false;
            DateTimeOffset now = _clock();
            _running = true;
            _stopRequested = false;
            _advised = false;
            _runId = Guid.NewGuid().ToString("N");
            _phase = "preparing";
            _outcome = "running";
            // Flags only: never persist character names, recipes, credentials or screen data.
            _plan = $"crafting={crafting};altering={altering};abyss={abyss}";
            _lastVerifiedBoundary = "none";
            _lastRun = null;
            _phaseMilliseconds.Clear();
            _startedAt = _phaseAt = _activityAt = _observedAt = now;
            PersistLocked(now, true);
            return true;
        }
    }

    public void Advance(string phase)
    {
        if (phase is not ("resume_selection" or "crafting" or "handoff" or "altering"))
            throw new ArgumentOutOfRangeException(nameof(phase));
        lock (_gate)
        {
            if (!_running || _stopRequested) return;
            DateTimeOffset now = _clock();
            ClosePhaseLocked(now);
            _phase = phase;
            _phaseAt = _activityAt = _observedAt = now;
            _advised = false;
            PersistLocked(now, true);
        }
    }

    public void ObserveLog(string line)
    {
        lock (_gate)
        {
            if (!_running || _stopRequested) return;
            bool relevant = _phase switch
            {
                "crafting" => line.Contains("[제작]", StringComparison.Ordinal),
                "altering" => line.Contains("[다중가공]", StringComparison.Ordinal) ||
                              line.Contains("[중간재료 스케줄]", StringComparison.Ordinal) ||
                              line.Contains("[어비스]", StringComparison.Ordinal),
                _ => false
            };
            // Activity evidence, not verified game progress. Exclude obvious errors.
            if (!relevant || line.Contains("오류", StringComparison.Ordinal) ||
                line.Contains("안전 정지", StringComparison.Ordinal)) return;
            DateTimeOffset now = _clock();
            _activityAt = now;
            _advised = false;
            PersistLocked(now, false);
        }
    }

    /// <summary>
    /// Writes a bounded heartbeat and emits at most one advisory per quiet
    /// interval. No completion/stop/input action is authorized by this method.
    /// </summary>
    public bool Observe(TimeSpan advisoryThreshold)
    {
        lock (_gate)
        {
            if (!_running) return false;
            DateTimeOffset now = _clock();
            _observedAt = now;
            bool activePhase = _phase is "crafting" or "altering";
            bool overdue = !_stopRequested && activePhase &&
                           advisoryThreshold > TimeSpan.Zero &&
                           now >= _activityAt &&
                           now - _activityAt >= advisoryThreshold;
            if (overdue && !_advised)
            {
                _advised = true;
                PersistLocked(now, true);
                return true;
            }
            PersistLocked(now, false);
            return false;
        }
    }

    public void MarkVerifiedBoundary(string boundary)
    {
        if (boundary is not ("altering_resume_user_verified" or "crafting_input_released"))
            throw new ArgumentOutOfRangeException(nameof(boundary));
        lock (_gate)
        {
            if (!_running || _stopRequested) return;
            _lastVerifiedBoundary = boundary;
            PersistLocked(_clock(), true);
        }
    }

    public void RequestStop()
    {
        lock (_gate)
        {
            if (!_running || _stopRequested) return;
            _stopRequested = true;
            DateTimeOffset now = _clock();
            ClosePhaseLocked(now);
            _phase = "stopping";
            _phaseAt = _observedAt = now;
            PersistLocked(_observedAt, true);
        }
    }

    public void Finish(string outcome)
    {
        if (outcome is not ("completed" or "cancelled" or "failed"))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        lock (_gate)
        {
            if (!_running) return;
            DateTimeOffset now = _clock();
            ClosePhaseLocked(now);
            _outcome = _phase = _stopRequested ? "cancelled" : outcome;
            _phaseAt = _observedAt = now;
            _running = false;
            _advised = false;
            long elapsed = Math.Max(0, Math.Min((long)TimeSpan.FromDays(30).TotalMilliseconds,
                (long)(now - _startedAt).TotalMilliseconds));
            _lastRun = new SchedulerRunPerformance(
                _runId, Environment.ProcessId, now, _outcome, elapsed,
                new Dictionary<string, long>(_phaseMilliseconds, StringComparer.Ordinal));
            PersistLocked(now, true);
            SchedulerPerformanceJournal.Append(_historyPath, _lastRun);
        }
    }

    private void PersistLocked(DateTimeOffset now, bool force)
    {
        // Reduce unchanged scheduler diagnostic disk writes by half. The
        // independent MacroWatchdog heartbeat still runs every UI tick.
        if (!force && now >= _writtenAt && now - _writtenAt < TimeSpan.FromSeconds(30))
            return;
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                processId = Environment.ProcessId,
                runId = _runId,
                running = _running,
                stopRequested = _stopRequested,
                phase = _phase,
                outcome = _outcome,
                planFlags = _plan,
                lastVerifiedBoundary = _lastVerifiedBoundary,
                phaseElapsedMilliseconds = PhaseSnapshotLocked(now),
                performanceHistoryLocalOnly = true,
                resumeRequiresUserConfirmation = true,
                automaticGameActionRetry = false,
                startedUtc = _startedAt,
                phaseChangedUtc = _phaseAt,
                lastActivityObservedUtc = _activityAt,
                uiHeartbeatUtc = _observedAt,
                quietAdvisoryEmitted = _advised,
                automaticRestart = false
            });
            string tmp = _path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
            _writtenAt = now;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
