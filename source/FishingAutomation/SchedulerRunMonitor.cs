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
    }

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
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
            _phase = "stopping";
            _phaseAt = _observedAt = _clock();
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
            _outcome = _phase = _stopRequested ? "cancelled" : outcome;
            _phaseAt = _observedAt = now;
            _running = false;
            _advised = false;
            PersistLocked(now, true);
        }
    }

    private void PersistLocked(DateTimeOffset now, bool force)
    {
        if (!force && now >= _writtenAt && now - _writtenAt < TimeSpan.FromSeconds(15))
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
