using System.Diagnostics;

namespace DungeonVisionBot;

internal sealed class RestartCycleException : Exception { }

internal sealed partial class ScenarioEngine : IScenarioRunner
{
    private nint _hwnd;
    private readonly AppSettings _settings;
    private readonly ScenarioDefinition _scenario;
    private readonly TargetDetector _detector;
    private readonly WindowCapture _capture = new();
    private readonly GuardedInputController _input;
    private readonly string _baseDir;
    private readonly Dictionary<string, long> _monitorLastAction = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _monitorLastScan = new(StringComparer.OrdinalIgnoreCase);
    private int _cycle;
    private long _lastAbyssOutsideHudScoreLog;
    private int _currentStepIndex;
    private int _resumeStepIndex;

    // V0172_ABYSS_CLEAR_TITLE_FALLBACK
    // The real clear screen can show a stable "던전 클리어!" title while the bottom
    // "화면을 터치해 주세요" template scores below its 0.68 threshold.
    // Keep the strict title+touch path first, then allow a guarded 2-frame title fallback.
    private int _abyssClearTitleFallbackConsecutive;
    private const double AbyssClearTitleFallbackMinScore = 0.68;
    private const int AbyssClearTitleFallbackRequiredFrames = 2;
    private static readonly Rectangle AbyssClearSafeTouch = new(360, 915, 80, 60);
    private bool AbyssClearTitleFallbackPending => _abyssClearTitleFallbackConsecutive > 0;

    public event Action<string>? Log;
    public string InputMode => _input.ModeName;

    public ScenarioEngine(nint hwnd, AppSettings settings, ScenarioDefinition scenario, List<TargetDefinition> targets, string baseDir)
    {
        _hwnd = hwnd;
        _settings = settings;
        _scenario = scenario;
        _baseDir = baseDir;
        _detector = new TargetDetector(targets.Where(t => !t.Id.StartsWith("route_")), baseDir);
        _input = new GuardedInputController(CreateInput(settings));
    }

    private IInputController CreateInput(AppSettings s)
    {
        if (!s.UseInterception)
        {
            throw new InvalidOperationException(
                "UseInterception=false 입니다. 마비노기 모바일 클릭은 Interception만 사용하도록 설정하세요.");
        }

        try
        {
            return new InterceptionInput(
                s.InterceptionMouseDevice,
                s.InterceptionKeyboardDevice);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "interception.dll을 찾지 못했습니다. " +
                "interception.dll(x64)을 DungeonVisionBot.exe 옆에 두세요.",
                ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "interception.dll 비트수가 맞지 않습니다. x64 DLL을 사용하세요.",
                ex);
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _input.SetCancellation(ct);
        try { await RunCoreAsync(ct); }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                DiagnosticPersistFailure("stability_stop");
                Log?.Invoke($"[안정화 정지] {ex.Message}");
            }
            throw;
        }
        finally { _input.Invalidate(); }
    }

    private async Task RunCoreAsync(CancellationToken ct)
    {
        Log?.Invoke($"입력 모드: {InputMode}");

        int recoveryFailures = 0;
        int repeatedRecoveryStep = -1;
        int repeatedRecoveryCount = 0;

        do
        {
            int maxStep = Math.Max(0, _scenario.Steps.Count - 1);
            int startStep = Math.Clamp(_resumeStepIndex, 0, maxStep);
            bool resuming = _resumeStepIndex > 0;
            _resumeStepIndex = 0;

            if (IsAbyss && startStep == 0)
                AbyssResetFlowState(resuming ? "전체 사이클 복구 재시작" : "새 어비스 사이클");

            _cycle++;
            if (resuming)
                Log?.Invoke($"===== {_cycle}판 자동복구 재개: {startStep + 1}. {_scenario.Steps[startStep].Name} =====");
            else
                Log?.Invoke($"===== {_cycle}판 시작 =====");

            try
            {
                for (int i = startStep; i < _scenario.Steps.Count; i++)
                {
                    _currentStepIndex = i;
                    ct.ThrowIfCancellationRequested();
                    await ExecuteStepAsync(_scenario.Steps[i], ct);
                    // Only completed work resets the repeated-recovery budget.
                    repeatedRecoveryStep = -1;
                    repeatedRecoveryCount = 0;
                }

                recoveryFailures = 0;
                repeatedRecoveryStep = -1;
                repeatedRecoveryCount = 0;
                _resumeStepIndex = IsAbyss ? AbyssCombatStepIndex : 0;
                Log?.Invoke($"===== {_cycle}판 완료 =====");
            }
            catch (RestartCycleException)
            {
                recoveryFailures = 0;
                repeatedRecoveryStep = -1;
                repeatedRecoveryCount = 0;

                if (_networkRestartRequested)
                    CompleteNetworkRestartRequest("재접속 완료");
                else
                    _resumeStepIndex = 0;

                Log?.Invoke("감시 항목에 의해 현재 판을 처음부터 다시 시작합니다.");
            }
            catch (TimeoutException ex) when (_settings.AutoRecoveryEnabled)
            {
                int max = Math.Max(1, _settings.AutoRecoveryMaxAttempts);
                bool recovered = false;
                bool networkCycleRestart = false;

                while (!recovered)
                {
                    recoveryFailures++;
                    Log?.Invoke($"[자동복구] {recoveryFailures}/{max} 단계 시간초과: {ex.Message}");
                    if (IsAbyss)
                    {
                        Log?.Invoke($"[진단] {DiagnosticSummary()}");
                        DiagnosticPersistFailure($"timeout_step_{_currentStepIndex + 1}_attempt_{recoveryFailures}");
                    }
                    await SaveRecoveryScreenshotAsync($"recovery_{recoveryFailures}", ct);

                    try
                    {
                        recovered = await TrySmartRecoveryAsync(ct);
                    }
                    catch (RestartCycleException)
                    {
                        networkCycleRestart = true;
                        break;
                    }

                    if (recovered)
                    {
                        if (IsAbyss)
                        {
                            if (repeatedRecoveryStep == _resumeStepIndex) repeatedRecoveryCount++;
                            else { repeatedRecoveryStep = _resumeStepIndex; repeatedRecoveryCount = 1; }
                            if (repeatedRecoveryCount >= max)
                                throw new InvalidOperationException("어비스 동일 단계 복구 반복 한도 도달 -> 안전 정지");
                        }
                        bool abyss = string.Equals(new DirectoryInfo(_baseDir).Name, "abyss", StringComparison.OrdinalIgnoreCase);
                        if (abyss)
                        {
                            Log?.Invoke($"[자동복구] {recoveryFailures}/{max} 어비스 복구 -> {_resumeStepIndex + 1}단계 재개");
                        }
                        else
                        {
                            int s = Math.Clamp(_resumeStepIndex, 0, maxStep);
                            if (repeatedRecoveryStep == s)
                                repeatedRecoveryCount++;
                            else
                            {
                                repeatedRecoveryStep = s;
                                repeatedRecoveryCount = 1;
                            }

                            Log?.Invoke($"[던전 자동복구] 현재 화면 확인 완료 -> {s + 1}. {_scenario.Steps[s].Name} 단계에서 재시작 (동일 단계 복구 {repeatedRecoveryCount}/{max})");
                            if (repeatedRecoveryCount >= max)
                            {
                                Log?.Invoke($"[던전 자동복구] 같은 복구 단계가 {max}회 반복됨 -> 무한 반복 방지를 위해 안전 정지");
                                throw new TimeoutException($"같은 복구 단계 '{_scenario.Steps[s].Name}'가 {max}회 반복되어 안전 정지합니다. 마지막 오류: {ex.Message}", ex);
                            }
                        }

                        recoveryFailures = 0;
                        break;
                    }

                    Log?.Invoke($"[던전 자동복구] {recoveryFailures}/{max} ESC 후 알고 있는 단계 화면 미확인");
                    if (recoveryFailures >= max)
                    {
                        Log?.Invoke($"[자동복구] {max}/{max} 실패 -> 안전 정지");
                        throw new TimeoutException($"Smart Recovery가 {max}회 실패했습니다. 마지막 오류: {ex.Message}", ex);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.AutoRecoveryDelaySeconds)), ct);
                }

                if (networkCycleRestart)
                {
                    if (_networkRestartRequested) CompleteNetworkRestartRequest("자동복구 중 재접속 완료");
                    else _resumeStepIndex = 0;
                    continue;
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.AutoRecoveryDelaySeconds)), ct);
            }
        } while (_scenario.Repeat && !ct.IsCancellationRequested);
    }

    private int FindDungeonStepIndex(string targetId)
    {
        for (int i = 0; i < _scenario.Steps.Count; i++)
        {
            if (_scenario.Steps[i].Target.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    private async Task<int?> DetectKnownDungeonRecoveryStepAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);

            if (await CheckMonitorsAsync(frame, ct))
            {
                await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                continue;
            }

            // V0.1.50 recovery priority:
            // If the current screen is an entry-state screen, it must win over retry/touch.
            // This prevents the loose bottom-area retry detector from sending recovery back
            // to step 4 while the game is actually showing selected/challenge/entry.
            var selected = await _detector.DetectAsync("selected_ocr_strict", frame, ct);
            var challenge = await _detector.DetectAsync("challenge_confirm_strict", frame, ct);
            var enter = await _detector.DetectAsync("enter_bottom", frame, ct);

            if (selected.Found || challenge.Found || enter.Found)
            {
                int step = FindDungeonStepIndex("enter_bottom");
                Log?.Invoke(
                    $"[던전 자동복구] 입장 화면 우선 판정 -> {step + 1}단계 재개 " +
                    $"selected={(selected.Found ? 1 : 0)} " +
                    $"challenge={(challenge.Found ? 1 : 0)} " +
                    $"enter={(enter.Found ? 1 : 0)}");
                return step;
            }

            var touch = await _detector.DetectAsync("touch_result", frame, ct);
            if (touch.Found)
            {
                int step = FindDungeonStepIndex("touch_result");
                Log?.Invoke($"[던전 자동복구] 전투 종료/터치 화면 확인 -> {step + 1}단계 재개");
                return step;
            }

            // Retry recovery is allowed only after exact entry-state checks failed.
            // Use the strict OCR-only target added in V0.1.50; never the loose hybrid retry here.
            var retry = await _detector.DetectAsync("retry_ocr_strict", frame, ct);
            if (retry.Found)
            {
                int step = FindDungeonStepIndex("retry");
                Log?.Invoke($"[던전 자동복구] 정확 OCR 다시 하기 확인 -> {step + 1}단계 재개 OCR={retry.ReadText} bounds={retry.Bounds}");
                return step;
            }

            await Task.Delay(Math.Max(300, _settings.PollIntervalMs), ct);
        }

        return null;
    }

    // DUNGEON_SCREEN_FIRST_RECOVERY_V6
    // Recovery rule: classify the CURRENT screen first. Only if no known state is found
    // is one ESC sent as a last resort, followed by one more classification pass.
    private async Task<bool> TrySmartRecoveryAsync(CancellationToken ct)
    {
        bool abyss = string.Equals(new DirectoryInfo(_baseDir).Name, "abyss", StringComparison.OrdinalIgnoreCase);
        if (abyss)
            return await TryAbyssInternalRecoveryAsync(ct);

        try
        {
            _hwnd = await ResolveRequiredGameWindowAsync(ct);
            NativeMethods.SetForegroundWindow(_hwnd);

            Log?.Invoke($"[던전 자동복구] {_currentStepIndex + 1}단계 오류 -> ESC 없이 현재 화면 먼저 판별");
            int? resumeStep = await DetectKnownDungeonRecoveryStepAsync(ct);
            if (resumeStep.HasValue)
            {
                _resumeStepIndex = resumeStep.Value;
                Log?.Invoke($"[던전 자동복구] 현재 화면에서 알려진 단계 확인 -> ESC 없이 {_resumeStepIndex + 1}단계 재개");
                return true;
            }

            // Unknown screen only: one ESC is the last resort for this recovery attempt.
            Log?.Invoke("[던전 자동복구] 현재 화면 8초간 미확인 -> 최후 수단 ESC 1회 입력 후 재판별");
            _input.TapScanCode(0x01);
            await Task.Delay(800, ct);

            resumeStep = await DetectKnownDungeonRecoveryStepAsync(ct);
            if (resumeStep.HasValue)
            {
                _resumeStepIndex = resumeStep.Value;
                Log?.Invoke($"[던전 자동복구] ESC 후 알려진 단계 확인 -> {_resumeStepIndex + 1}단계 재개");
                return true;
            }

            Log?.Invoke("[던전 자동복구] ESC 후에도 알려진 단계 미확인 -> 이번 복구 시도 실패 (추가 ESC 없음)");
            return false;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log?.Invoke($"[던전 자동복구] 화면 우선 판별 예외: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryAbyssInternalRecoveryAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        const int RecoveryTimeoutSeconds = 660;
        long lastTouchClick = 0;
        long lastExitClick = 0;
        long lastPopupClick = 0;
        int touchClicks = 0;
        int exitClicks = 0;
        bool treasureSeen = false;
        bool clearScreenSeen = false;
        bool clearTransitionConfirmed = false;
        int clearGoneConsecutive = 0;
        int outsideConsecutive = 0;

        Log?.Invoke("[어비스 자동복구] 상태 기반 복구 시작: 전투 대기 -> 클리어 터치 -> 보물상자 확인 -> 나가기 -> 던전 밖 HUD 확인");

        while (sw.Elapsed < TimeSpan.FromSeconds(RecoveryTimeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            if ((await DetectAbyssResultRetryAsync(frame, ct)).Found)
            {
                await RetryAbyssResultAsync(ct);
                _resumeStepIndex = AbyssCombatStepIndex;
                return true;
            }
            long now = Environment.TickCount64;

            if (await DetectAbyssOutsideWorkflowAsync(frame, ct))
            {
                if (++outsideConsecutive < 3)
                {
                    await Task.Delay(250, ct);
                    continue;
                }
                _abyssCombatStartedAt = null;
                _resumeStepIndex = 0;
                Log?.Invoke("[어비스 자동복구] 던전 밖 HUD 3/4 이상 확인 -> 복구 즉시 완료, 처음부터 재시작");
                return true;
            }
            outsideConsecutive = 0;
            if (_abyssCombatStartedAt.HasValue && now - _abyssCombatStartedAt.Value >= 600_000)
            {
                await ExitAbyssAfterCombatTimeoutAsync(_scenario.Steps[AbyssCombatStepIndex], ct);
                _resumeStepIndex = FindDungeonStepIndex("abyss_result_retry");
                return true;
            }

            if (now - lastPopupClick >= 2500)
            {
                var popupTitle = await _detector.DetectAsync("abyss_potion_popup_title", frame, ct);
                var popupClose = await _detector.DetectAsync("abyss_popup_close", frame, ct);
                if (popupTitle.Found || popupClose.Found)
                {
                    string popupSource = popupTitle.Found ? "title-ocr" : "close-template";
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    NativeMethods.SetForegroundWindow(_hwnd);
                    Log?.Invoke($"[어비스 자동복구] 회복 물약 팝업 확인({popupSource}) -> 마우스 클릭 없이 ESC 1회");
                    _input.TapScanCode(0x01);
                    lastPopupClick = now;

                    var popupTimer = Stopwatch.StartNew();
                    int popupGoneFrames = 0;
                    while (popupTimer.Elapsed < TimeSpan.FromSeconds(4))
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(180, ct);
                        using var popupCheck = await CaptureGameWindowAsync(ct);
                        var titleAfter = await _detector.DetectAsync("abyss_potion_popup_title", popupCheck, ct);
                        var closeAfter = await _detector.DetectAsync("abyss_popup_close", popupCheck, ct);
                        if (titleAfter.Found || closeAfter.Found)
                        {
                            popupGoneFrames = 0;
                            continue;
                        }

                        if (++popupGoneFrames >= 2)
                        {
                            Log?.Invoke("[어비스 자동복구] 회복 물약 팝업 ESC 후 닫힘 2프레임 확인");
                            break;
                        }
                    }

                    if (popupGoneFrames < 2)
                    {
                        Log?.Invoke("[어비스 자동복구] 회복 물약 팝업 ESC 후 닫힘 확인 실패 -> 구매/닫기 좌표 클릭 없이 복구 실패 처리");
                        return false;
                    }

                    continue;
                }
            }

            var clearVisual = await DetectAbyssConfirmedClearAsync(frame, ct);
            if (clearVisual.Found)
            {
                clearScreenSeen = true;
                clearTransitionConfirmed = false;
                clearGoneConsecutive = 0;

                // Use the already-confirmed point directly. This is either the real touch
                // template or the guarded V0.1.72 safe fallback centered at (400,945).
                if (now - lastTouchClick >= 1800)
                {
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    NativeMethods.SetForegroundWindow(_hwnd);
                    if (touchClicks >= 3) throw new InvalidOperationException("클리어 터치 재시도 한도 초과");
                    touchClicks++;
                    Log?.Invoke(
                        $"[어비스 자동복구] 클리어 화면 확인 완료 -> 화면 터치 {touchClicks}회 " +
                        $"@ {clearVisual.Bounds} source={clearVisual.ReadText}");
                    _input.ClickClientPoint(_hwnd, clearVisual.Center);
                    lastTouchClick = now;
                    await Task.Delay(Math.Max(900, _settings.ClickSettleMs), ct);
                    continue;
                }
                await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                continue;
            }

            if (clearScreenSeen && !clearTransitionConfirmed)
            {
                var residual = await DetectAbyssClearVisualAsync(frame, ct);
                if (!residual.Found)
                {
                    clearGoneConsecutive++;
                    Log?.Invoke($"[어비스 자동복구] 클리어 화면 사라짐 확인 {clearGoneConsecutive}/3");
                    if (clearGoneConsecutive >= 3)
                    {
                        clearTransitionConfirmed = true;
                        Log?.Invoke("[어비스 자동복구] 클리어 화면 3회 연속 사라짐 확인 -> 보물상자/나가기 탐색");
                    }
                }
                else
                {
                    clearGoneConsecutive = 0;
                }
                if (!clearTransitionConfirmed)
                {
                    await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                    continue;
                }
            }

            if (!treasureSeen)
            {
                var treasure = await _detector.DetectAsync("abyss_treasure_chest", frame, ct);
                if (treasure.Found)
                {
                    treasureSeen = true;
                    Log?.Invoke($"[어비스 자동복구] 보물상자 확인 완료 @ {treasure.Bounds} -> 나가기 탐색");
                }
            }

            if ((treasureSeen || clearTransitionConfirmed) && now - lastExitClick >= 2200)
            {
                var exit = await _detector.DetectAsync("abyss_exit", frame, ct);
                if (exit.Found)
                {
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    NativeMethods.SetForegroundWindow(_hwnd);
                    if (exitClicks >= 3) throw new InvalidOperationException("나가기 재시도 한도 초과");
                    exitClicks++;
                    Log?.Invoke($"[어비스 자동복구] 나가기 확인 {exitClicks}회 -> 클릭 @ {exit.Bounds}");
                    _input.ClickClientPoint(_hwnd, exit.Center);
                    lastExitClick = now;
                    await Task.Delay(Math.Max(1000, _settings.ClickSettleMs), ct);
                    continue;
                }
            }

            // No recovery UI found: this can simply be active combat. Do not press ESC.
            await Task.Delay(Math.Max(300, _settings.PollIntervalMs), ct);
        }

        Log?.Invoke($"[어비스 자동복구] {RecoveryTimeoutSeconds}초 동안 던전 밖 복귀를 완료하지 못함");
        return false;
    }

    private const int MaxDebugFiles = 200;
    private const long MaxDebugBytes = 268435456;

    private void PruneDebugScreenshots()
    {
        try
        {
            string dir = Path.Combine(_baseDir, "debug");
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir).GetFiles("*.png")
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            long total = 0;
            for (int i = 0; i < files.Count; i++)
            {
                FileInfo f = files[i];
                if (i >= MaxDebugFiles || total + f.Length > MaxDebugBytes)
                {
                    try { f.Delete(); } catch { }
                }
                else total += f.Length;
            }
        }
        catch { }
    }

    private async Task SaveRecoveryScreenshotAsync(string label, CancellationToken ct)
    {
        if (!_settings.SaveScreenshotOnTimeout) return;
        try
        {
            Directory.CreateDirectory(Path.Combine(_baseDir, "debug"));
            using var shot = _capture.CaptureClient(_hwnd);
            string path = Path.Combine(_baseDir, "debug", $"{label}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            shot.Save(path);
            PruneDebugScreenshots();
            Log?.Invoke($"[자동복구] 디버그 캡처 저장: {path}");
        }
        catch { }
    }

    // DUNGEON_CURRENT_CHALLENGE_DIRECT_ENTRY_V9
    // Current-screen rule:
    // 1) exact '도전' + exact right-bottom '입장하기' means the screen is already ready;
    //    do not require a prior '선택됨' observation.
    // 2) exact '선택됨' is clicked only inside the dungeon-card safe region to change it to '도전'.
    // 3) entry itself is still performed later by Space only; no mouse click is used for '입장하기'.
    // 4) strict retry OCR is checked only after every entry-state signal is absent.
    private async Task VerifyChallengeBeforeEntryAsync(CancellationToken ct)
    {
        const int RequiredReadyFrames = 2;
        const int ConfirmIntervalMs = 250;
        const int MaxVerifySeconds = 45;

        var selectedClickSafe = new Rectangle(430, 640, 330, 230);
        var entryKeySafe = new Rectangle(360, 900, 440, 100);

        int readyConsecutive = 0;
        int normalResultRetryClicks = 0;
        long lastSelectedClick = 0;
        long lastStateLog = 0;
        var timer = Stopwatch.StartNew();

        Log?.Invoke("[도전 안전확인] 현재 화면 우선 판정 시작: 도전+입장하기면 즉시 입장 흐름");

        while (timer.Elapsed < TimeSpan.FromSeconds(MaxVerifySeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);

            // Entry state always wins over retry/result handling.
            var challenge = await _detector.DetectAsync("challenge_confirm_strict", frame, ct);
            var enter = await _detector.DetectAsync("enter_bottom", frame, ct);
            var selected = await _detector.DetectAsync("selected_ocr_strict", frame, ct);

            if (challenge.Found && enter.Found)
            {
                if (!entryKeySafe.Contains(enter.Center))
                {
                    readyConsecutive = 0;
                    Log?.Invoke($"[도전 안전확인] 도전+입장하기 감지했지만 입장하기가 안전영역 밖 -> 대기 @ {enter.Center} safe={entryKeySafe}");
                    await Task.Delay(ConfirmIntervalMs, ct);
                    continue;
                }

                readyConsecutive++;
                Log?.Invoke(
                    $"[도전 안전확인] 현재 도전 상태 + 오른쪽 입장하기 확인 {readyConsecutive}/{RequiredReadyFrames} " +
                    $"challenge={challenge.Bounds} enter={enter.Bounds}");

                if (readyConsecutive >= RequiredReadyFrames)
                {
                    Log?.Invoke("[도전 안전확인] 현재 화면이 이미 도전 상태 -> 선택됨 이력 없이 바로 입장 허용");
                    return;
                }

                await Task.Delay(ConfirmIntervalMs, ct);
                continue;
            }

            readyConsecutive = 0;

            // Selected means the toggle still needs one safe card click to return to challenge.
            if (selected.Found)
            {
                if (!selectedClickSafe.Contains(selected.Center))
                {
                    Log?.Invoke($"[도전 안전확인] 선택됨 검출 좌표가 카드 안전영역 밖 -> 클릭 안 함 @ {selected.Center} safe={selectedClickSafe}");
                    await Task.Delay(ConfirmIntervalMs, ct);
                    continue;
                }

                long now = Environment.TickCount64;
                if (now - lastSelectedClick >= 1200)
                {
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    NativeMethods.SetForegroundWindow(_hwnd);
                    Log?.Invoke($"[도전 안전확인] 선택됨 확인 -> 카드 안 선택됨만 1회 클릭하여 도전으로 전환 @ {selected.Center}");
                    _input.ClickClientPoint(_hwnd, selected.Center);
                    lastSelectedClick = now;
                    await Task.Delay(Math.Max(650, _settings.ClickSettleMs), ct);
                }
                else
                {
                    await Task.Delay(ConfirmIntervalMs, ct);
                }
                continue;
            }

            // Challenge without the right entry word is not ready yet. Never click challenge.
            if (challenge.Found)
            {
                long now = Environment.TickCount64;
                if (now - lastStateLog >= 1200)
                {
                    Log?.Invoke("[도전 안전확인] 도전은 확인됐지만 오른쪽 입장하기 미확인 -> 도전은 누르지 않고 대기");
                    lastStateLog = now;
                }
                await Task.Delay(ConfirmIntervalMs, ct);
                continue;
            }

            // Entry text alone is not enough to authorize Space. Also do not let retry logic
            // run on an entry-like screen; wait for the exact state word to settle.
            if (enter.Found)
            {
                long now = Environment.TickCount64;
                if (now - lastStateLog >= 1200)
                {
                    Log?.Invoke($"[도전 안전확인] 오른쪽 입장하기는 보이지만 도전/선택됨 상태 미확인 -> 입력 없이 재확인 @ {enter.Bounds}");
                    lastStateLog = now;
                }
                await Task.Delay(ConfirmIntervalMs, ct);
                continue;
            }

            // Normal result/retry fast path. Exact OCR only, and only after all entry-state
            // signals above are absent. This prevents the party-search / entry area from
            // ever being treated as a retry control.
            var normalRetry = await _detector.DetectAsync("retry_ocr_strict", frame, ct);
            if (normalRetry.Found)
            {
                Log?.Invoke($"[던전 결과] 정확 OCR 다시 하기 감지 1/2 OCR={normalRetry.ReadText} bounds={normalRetry.Bounds}");
                await Task.Delay(220, ct);

                using var retryConfirmFrame = await CaptureGameWindowAsync(ct);

                var guardSelected = await _detector.DetectAsync("selected_ocr_strict", retryConfirmFrame, ct);
                var guardChallenge = await _detector.DetectAsync("challenge_confirm_strict", retryConfirmFrame, ct);
                var guardEnter = await _detector.DetectAsync("enter_bottom", retryConfirmFrame, ct);
                if (guardSelected.Found || guardChallenge.Found || guardEnter.Found)
                {
                    Log?.Invoke(
                        $"[던전 결과] 입장 화면 신호 재확인 -> 다시 하기 클릭 금지 " +
                        $"selected={(guardSelected.Found ? 1 : 0)} " +
                        $"challenge={(guardChallenge.Found ? 1 : 0)} " +
                        $"enter={(guardEnter.Found ? 1 : 0)}");
                    await Task.Delay(ConfirmIntervalMs, ct);
                    continue;
                }

                var retryConfirm = await _detector.DetectAsync("retry_ocr_strict", retryConfirmFrame, ct);
                if (!retryConfirm.Found)
                {
                    Log?.Invoke("[던전 결과] 정확 OCR 다시 하기 2/2 확인 실패 -> 클릭 안 함");
                    await Task.Delay(ConfirmIntervalMs, ct);
                    continue;
                }

                Log?.Invoke($"[던전 결과] 정확 OCR 다시 하기 2/2 연속 확인 OCR={retryConfirm.ReadText} bounds={retryConfirm.Bounds}");

                if (normalResultRetryClicks >= 2)
                {
                    Log?.Invoke("[던전 결과] 정상 다시 하기 클릭 2회 소진 -> 기존 타임아웃/자동복구에 맡김");
                    await Task.Delay(ConfirmIntervalMs, ct);
                    continue;
                }

                await RetryClickDelay.WaitAsync(ct);
                using var settledRetryFrame = await CaptureGameWindowAsync(ct);
                if ((await _detector.DetectAsync("selected_ocr_strict", settledRetryFrame, ct)).Found ||
                    (await _detector.DetectAsync("challenge_confirm_strict", settledRetryFrame, ct)).Found ||
                    (await _detector.DetectAsync("enter_bottom", settledRetryFrame, ct)).Found)
                    continue;
                var settledRetry = await _detector.DetectAsync("retry_ocr_strict", settledRetryFrame, ct);
                if (!settledRetry.Found || !settledRetry.Bounds.IntersectsWith(retryConfirm.Bounds)) continue;
                retryConfirm = settledRetry;
                normalResultRetryClicks++;
                _hwnd = await ResolveRequiredGameWindowAsync(ct);
                NativeMethods.SetForegroundWindow(_hwnd);
                Log?.Invoke($"[던전 결과] 정상 다시 하기 클릭 {normalResultRetryClicks}/2 @ {retryConfirm.Center}");
                _input.ClickClientPoint(_hwnd, retryConfirm.Center);
                await Task.Delay(Math.Max(900, _settings.ClickSettleMs), ct);

                using var afterRetryFrame = await CaptureGameWindowAsync(ct);
                var stillRetry = await _detector.DetectAsync("retry_ocr_strict", afterRetryFrame, ct);
                if (!stillRetry.Found)
                {
                    timer.Restart();
                    lastSelectedClick = 0;
                    Log?.Invoke("[던전 결과] 다시 하기 화면 이탈 확인 -> 45초 검증 타이머 재시작");
                }
                else
                {
                    Log?.Invoke("[던전 결과] 다시 하기 클릭 후 화면 유지 -> 재확인 (임의 좌표 클릭 없음)");
                }

                await Task.Delay(ConfirmIntervalMs, ct);
                continue;
            }

            long unknownNow = Environment.TickCount64;
            if (unknownNow - lastStateLog >= 1500)
            {
                Log?.Invoke("[도전 안전확인] 현재 입장 상태 미확인 -> 클릭 없이 재확인");
                lastStateLog = unknownNow;
            }

            await Task.Delay(ConfirmIntervalMs, ct);
        }

        throw new TimeoutException(
            "45초 동안 현재 도전+입장하기 또는 선택됨->도전 상태를 안정적으로 확인하지 못했습니다.");
    }

    private async Task<bool> VerifyAbyssSelectionScreenAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        int consecutive = 0;
        string[] ids =
        {
            "abyss_dungeon_hallucination_anchorage",
            "abyss_dungeon_madness_cave",
            "abyss_dungeon_scattered_waterway"
        };

        while (sw.Elapsed < TimeSpan.FromSeconds(6))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            bool foundAny = false;
            string foundId = "";

            foreach (string id in ids)
            {
                var r = await _detector.DetectAsync(id, frame, ct);
                if (!r.Found) continue;
                foundAny = true;
                foundId = id;
                break;
            }

            if (foundAny)
            {
                consecutive++;
                Log?.Invoke($"[어비스] 어비스 던전 선택 화면 확인 {consecutive}/2 ({foundId})");
                if (consecutive >= 2)
                    return true;
            }
            else
            {
                consecutive = 0;
            }

            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }

        return false;
    }

    private async Task ExecuteStepAsync(ScenarioStep step, CancellationToken ct)
    {
        DiagnosticSetStage(step.Target, step.Name);

        if (IsAbyss && step.Target.Equals("abyss_touch_screen", StringComparison.OrdinalIgnoreCase))
        {
            AbyssEnterCombatClearWait("5단계 전투 종료/클리어 화면 대기 시작");
            await StartAbyssCombatClockAsync(ct);
        }

        if (IsAbyss && step.Target == "abyss_result_retry")
        {
            await RetryAbyssResultAsync(ct);
            return;
        }
        // STABLE_CHALLENGE_GUARD_V2_CALL
        if (step.Target.Equals("enter_bottom", StringComparison.OrdinalIgnoreCase))
        {
            await VerifyChallengeBeforeEntryAsync(ct);
        }

        Log?.Invoke($"[{step.Name}] 대기: {step.Target} / {step.TimeoutSeconds}s");
        var sw = Stopwatch.StartNew();
        bool alternativeClicked = false;
        int abyssClearConsecutive = 0;
        var abyssIconRejected = new List<Rectangle>();

        while (IsAbyss && step.Target == "abyss_touch_screen"
            ? Environment.TickCount64 - _abyssCombatStartedAt <= 600_000
            : sw.Elapsed < TimeSpan.FromSeconds(step.TimeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);

            // Abyss clear screen must win over the global scene-skip monitor.
            // v67: image-only multi-scale confirmation. Rank S/OCR are not used; transition is separately verified.
            DetectionResult found;
            if (step.Target.Equals("abyss_touch_screen", StringComparison.OrdinalIgnoreCase))
            {
                found = await DetectAbyssConfirmedClearAsync(frame, ct);
                if (!found.Found)
                {
                    abyssClearConsecutive = 0;
                    if (await CheckMonitorsAsync(frame, ct))
                        continue;
                }
                else
                {
                    Log?.Invoke("[어비스] 클리어 화면 동시 이미지 확인 완료 -> 터치 문구 클릭 단계 진행");
                    abyssClearConsecutive = 0;
                }
            }
            else
            {
                bool dungeonEntryStep =
                    step.Target.Equals("enter_bottom", StringComparison.OrdinalIgnoreCase) ||
                    step.Target.Equals("enter_confirm", StringComparison.OrdinalIgnoreCase);

                if (!dungeonEntryStep && await CheckMonitorsAsync(frame, ct))
                    continue;

                if (dungeonEntryStep)
                    Log?.Invoke($"[던전 입장] {step.Target} 단계에서는 scene_skip 포함 전역 모니터 클릭 비활성");

                if (step.Target.Equals("abyss_icon", StringComparison.OrdinalIgnoreCase) && abyssIconRejected.Count > 0)
                {
                    using var masked = (Bitmap)frame.Clone();
                    using (var g = Graphics.FromImage(masked))
                    {
                        foreach (var rejected in abyssIconRejected)
                            g.FillRectangle(Brushes.Black, rejected);
                    }
                    found = await _detector.DetectAsync(step.Target, masked, ct);
                }
                else
                {
                    found = await _detector.DetectAsync(step.Target, frame, ct);
                }
            }

            DiagnosticObserveDetection(step.Target, found);

            if (found.Found)
            {
                Log?.Invoke($"[{step.Name}] 발견: {found.ReadText ?? found.Score.ToString("0.000")} @ {found.Bounds}");
                if (step.Type.Equals("wait_click", StringComparison.OrdinalIgnoreCase))
                {
                    NativeMethods.SetForegroundWindow(_hwnd);
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);

                    if (step.Target.Equals("retry", StringComparison.OrdinalIgnoreCase))
                    {
                        await RetryClickDelay.WaitAsync(ct);
                        using var settledRetryFrame = await CaptureGameWindowAsync(ct);
                        var settledRetry = await _detector.DetectAsync("retry_ocr_strict", settledRetryFrame, ct);
                        if (!settledRetry.Found || !settledRetry.Bounds.IntersectsWith(found.Bounds)) continue;
                        found = settledRetry;
                        var retryGuardSelected = await _detector.DetectAsync("selected_ocr_strict", settledRetryFrame, ct);
                        var retryGuardChallenge = await _detector.DetectAsync("challenge_confirm_strict", settledRetryFrame, ct);
                        var retryGuardEnter = await _detector.DetectAsync("enter_bottom", settledRetryFrame, ct);

                        if (retryGuardSelected.Found || retryGuardChallenge.Found || retryGuardEnter.Found)
                        {
                            Log?.Invoke(
                                $"[retry guard] 입장 화면에서는 다시 하기 클릭 금지 " +
                                $"selected={(retryGuardSelected.Found ? 1 : 0)} " +
                                $"challenge={(retryGuardChallenge.Found ? 1 : 0)} " +
                                $"enter={(retryGuardEnter.Found ? 1 : 0)} " +
                                $"retryCandidate={found.Bounds}");
                            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                            continue;
                        }
                    }

                    if (step.Target.Equals("abyss_touch_screen", StringComparison.OrdinalIgnoreCase))
                    {
                        await AdvanceAbyssClearScreenAsync(found, ct);
                    }
                    else if (step.Target.Equals("abyss_icon", StringComparison.OrdinalIgnoreCase))
                    {
                        _input.ClickClientPoint(_hwnd, found.Center);
                        await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);

                        if (!await VerifyAbyssSelectionScreenAsync(ct))
                        {
                            var rejected = found.Bounds;
                            rejected.Inflate(24, 24);
                            abyssIconRejected.Add(rejected);
                            Log?.Invoke($"[어비스] 어비스 클릭 검증 실패 {abyssIconRejected.Count}/4 @ {found.Bounds} score={found.Score:0.000} -> ESC 후 해당 후보 제외");

                            _hwnd = await ResolveRequiredGameWindowAsync(ct);
                            NativeMethods.SetForegroundWindow(_hwnd);
                            _input.TapScanCode(0x01); // ESC: wrong content -> menu
                            await Task.Delay(900, ct);

                            if (abyssIconRejected.Count >= 4)
                            {
                                // Close the menu as well so Smart Recovery sees the outside HUD immediately.
                                _input.TapScanCode(0x01);
                                await Task.Delay(700, ct);
                                throw new TimeoutException("어비스 아이콘 후보 4개를 클릭 검증했지만 어비스 던전 선택 화면이 확인되지 않았습니다.");
                            }

                            continue;
                        }

                        Log?.Invoke($"[어비스] 어비스 클릭 검증 성공 @ {found.Bounds} score={found.Score:0.000}");
                    }
                    else
                    {
                        if (step.Target.Equals("enter_bottom", StringComparison.OrdinalIgnoreCase))
                        {
                            string exactEntry = FuzzyText.Normalize(found.ReadText ?? "");
                            var entryKeySafe = new Rectangle(360, 900, 440, 100);

                            if (!exactEntry.Equals(FuzzyText.Normalize("입장하기"), StringComparison.OrdinalIgnoreCase))
                            {
                                Log?.Invoke($"[던전 입장] 입장하기 정확 단어 확인 실패 -> Space 안 누름 · OCR={found.ReadText} @ {found.Bounds}");
                                await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                                continue;
                            }

                            if (!entryKeySafe.Contains(found.Center))
                            {
                                Log?.Invoke($"[던전 입장] 입장하기가 오른쪽 하단 안전영역 밖 -> Space 안 누름 @ {found.Center} safe={entryKeySafe}");
                                await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                                continue;
                            }

                            Log?.Invoke($"[던전 입장] 도전 상태 + 오른쪽 입장하기 확인 -> Space 입력 @ {found.Bounds}");
                            _input.TapScanCode(0x39);
                            await Task.Delay(_settings.ClickSettleMs, ct);
                        }
                        else
                        {
                            _input.ClickClientPoint(_hwnd, found.Center);
                            await Task.Delay(_settings.ClickSettleMs, ct);
                        }
                    }

                    if (step.Target.Equals("abyss_exit", StringComparison.OrdinalIgnoreCase))
                        await WaitForAbyssHomeAfterNormalExitAsync(ct);
                }
                else if (!step.Type.Equals("wait", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"알 수 없는 step type: {step.Type}");
                }
                return;
            }

            if (!alternativeClicked && step.ClickAlternativeThenWaitPrimary && !string.IsNullOrWhiteSpace(step.AlternativeTarget))
            {
                var alt = await _detector.DetectAsync(step.AlternativeTarget, frame, ct);
                if (alt.Found)
                {
                    Log?.Invoke($"[{step.Name}] 기본 타깃 없음, 대체 타깃 '{step.AlternativeTarget}' 클릭");
                    NativeMethods.SetForegroundWindow(_hwnd);
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    _input.ClickClientPoint(_hwnd, alt.Center);
                    alternativeClicked = true;
                    await Task.Delay(_settings.ClickSettleMs, ct);
                    continue;
                }
            }

            await Task.Delay(_settings.PollIntervalMs, ct);
        }

        if (IsAbyss && step.Target == "abyss_touch_screen")
        {
            await ExitAbyssAfterCombatTimeoutAsync(step, ct);
            return; // A late clear/result was handled by the existing normal path.
        }

        // Optional timeout recovery: click a leave/escape control, click a
        // follow-up confirmation, wait, then restart the scenario cycle.
        if (!string.IsNullOrWhiteSpace(step.TimeoutClickTarget))
        {
            Log?.Invoke($"[{step.Name}] 제한시간 {step.TimeoutSeconds}초 초과 -> 퇴장 절차 시작");
            await ClickTargetWithTimeoutAsync(
                step.TimeoutClickTarget,
                step.TimeoutClickTargetWaitSeconds,
                $"{step.Name} / 던전 퇴장",
                ct);

            if (!string.IsNullOrWhiteSpace(step.TimeoutFollowupClickTarget))
            {
                await ClickTargetWithTimeoutAsync(
                    step.TimeoutFollowupClickTarget,
                    step.TimeoutFollowupWaitSeconds,
                    $"{step.Name} / 나가기",
                    ct);
            }

            if (string.Equals(new DirectoryInfo(_baseDir).Name, "abyss", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(step.TimeoutFollowupClickTarget, "abyss_exit", StringComparison.OrdinalIgnoreCase))
            {
                Log?.Invoke("[어비스] 강제 퇴장 후 던전 밖 복귀 확인");
                await WaitForAbyssHomeAfterNormalExitAsync(ct);
            }

            if (step.TimeoutRestartDelaySeconds > 0)
            {
                Log?.Invoke($"[어비스] 강제 퇴장 완료 및 밖 확인 -> {step.TimeoutRestartDelaySeconds}초 대기 후 처음부터 재시작");
                await Task.Delay(TimeSpan.FromSeconds(step.TimeoutRestartDelaySeconds), ct);
            }

            throw new RestartCycleException();
        }

        Log?.Invoke($"[{step.Name}] TIMEOUT");
        if (_settings.SaveScreenshotOnTimeout)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(_baseDir, "debug"));
                using var shot = await CaptureGameWindowAsync(ct);
                var path = Path.Combine(_baseDir, "debug", $"timeout_{DateTime.Now:yyyyMMdd_HHmmss}_{Safe(step.Name)}.png");
                shot.Save(path);
                PruneDebugScreenshots();
                Log?.Invoke($"디버그 캡처 저장: {path}");
            }
            catch { }
        }
        // STRICT_CONFIRM_SOFT_TIMEOUT_V1
        if (step.Target.Equals("challenge_confirm_strict", StringComparison.OrdinalIgnoreCase) ||
            step.Name.Contains("도전 최종확인", StringComparison.OrdinalIgnoreCase))
        {
            Log?.Invoke($"[{step.Name}] {step.TimeoutSeconds}초 동안 확인되지 않음 -> 다음 단계 계속");
            return;
        }
        throw new TimeoutException($"'{step.Name}' 단계가 {step.TimeoutSeconds}초 안에 완료되지 않았습니다.");
    }

    private async Task<DetectionResult> DetectAbyssConfirmedClearAsync(Bitmap frame, CancellationToken ct)
    {
        var clearTitle = await _detector.DetectAsync("abyss_dungeon_clear_visual", frame, ct);
        DiagnosticObserveDetection("abyss_dungeon_clear_visual", clearTitle);
        var touch = await _detector.DetectAsync("abyss_touch_screen", frame, ct);
        DiagnosticObserveDetection("abyss_touch_screen", touch);

        // Preferred path: both independent clear signals are visible in the same frame.
        if (clearTitle.Found && touch.Found)
        {
            _abyssClearTitleFallbackConsecutive = 0;
            ResetAbyssExitState(true);
            AbyssTransitionTo(AbyssFlowState.ClearConfirmed, "클리어 타이틀 + 화면 터치 문구 동시 확인");
            return touch;
        }

        // V0.1.72 fallback:
        // In real captures the title stayed around 0.69~0.75 while the touch template
        // fluctuated around 0.48~0.63. Do not lower the global touch threshold.
        // Instead, on the canonical 800x1000 client require a strong clear title in
        // two consecutive frames and then use the already-established safe touch point
        // centered at (400,945).
        bool titleFallbackCandidate =
            frame.Width == 800 &&
            frame.Height == 1000 &&
            clearTitle.Found &&
            clearTitle.Score >= AbyssClearTitleFallbackMinScore;

        if (titleFallbackCandidate)
        {
            _abyssClearTitleFallbackConsecutive++;
            if (_abyssClearTitleFallbackConsecutive >= AbyssClearTitleFallbackRequiredFrames)
            {
                _abyssClearTitleFallbackConsecutive = 0;
                ResetAbyssExitState(true);
                AbyssTransitionTo(
                    AbyssFlowState.ClearConfirmed,
                    "클리어 타이틀 강한 신호 2프레임 + 안전 터치 fallback");
                Log?.Invoke(
                    $"[어비스] 클리어 타이틀 강한 신호 2/2 확인 " +
                    $"title={clearTitle.Score:0.000}, touch={touch.Score:0.000} " +
                    $"-> 안전 터치 위치 사용 @ {AbyssClearSafeTouch}");
                return new DetectionResult(
                    true,
                    AbyssClearSafeTouch,
                    clearTitle.Score,
                    "clear_title_2x_safe_fallback");
            }

            Log?.Invoke(
                $"[어비스] 클리어 타이틀 강한 신호 1/2 " +
                $"title={clearTitle.Score:0.000}, touch={touch.Score:0.000} -> 퇴장/모니터 입력 보류");
        }
        else
        {
            _abyssClearTitleFallbackConsecutive = 0;
        }

        // V0164_ABYSS_CLEAR_LOG_SIMPLIFIED:
        // Suppress ordinary combat similarity noise. This affects logging only.
        const double ClearCandidateLogThreshold = 0.65;
        if (clearTitle.Score >= ClearCandidateLogThreshold || touch.Score >= ClearCandidateLogThreshold)
            Log?.Invoke($"[어비스] 강한 클리어 후보: title={clearTitle.Score:0.000}, touch={touch.Score:0.000}");
        return DetectionResult.NotFound;
    }

    private async Task<DetectionResult> DetectAbyssClearVisualAsync(Bitmap frame, CancellationToken ct)
    {
        var clearTitle = await _detector.DetectAsync("abyss_dungeon_clear_visual", frame, ct);
        if (clearTitle.Found) return clearTitle;
        return await _detector.DetectAsync("abyss_touch_screen", frame, ct);
    }

    private async Task<DetectionResult> WaitForAbyssTouchPromptAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        int clearTitleConsecutive = 0;

        while (sw.Elapsed < TimeSpan.FromSeconds(20))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);

            // The only valid reason to finish waiting without a touch click is that
            // the actual result screen is already visible.
            var result = await DetectAbyssResultRetryAsync(frame, ct);
            if (result.Found)
            {
                AbyssTransitionTo(AbyssFlowState.ResultConfirmed, "터치 대기 중 실제 결과 화면 확인");
                Log?.Invoke("[어비스] 터치 문구 대기 중 실제 결과 화면 확인 -> 추가 터치 없이 결과 단계 진행");
                return DetectionResult.NotFound;
            }

            var touch = await _detector.DetectAsync("abyss_touch_screen", frame, ct);
            if (touch.Found)
            {
                AbyssTransitionTo(AbyssFlowState.TouchReady, "화면 터치 문구 확인");
                Log?.Invoke($"[어비스] 화면 터치 문구 확인 -> 해당 위치 클릭 준비 @ {touch.Bounds}");
                return touch;
            }

            var clearTitle = await _detector.DetectAsync("abyss_dungeon_clear_visual", frame, ct);
            if (clearTitle.Found)
            {
                clearTitleConsecutive++;
                if (clearTitleConsecutive >= 2 && frame.Width == 800 && frame.Height == 1000)
                {
                    var safeTouch = new Rectangle(360, 915, 80, 60);
                    AbyssTransitionTo(AbyssFlowState.TouchReady, "클리어 타이틀 2회 확인 + 기존 안전 터치 fallback");
                    Log?.Invoke($"[어비스] 클리어 타이틀 2회 확인 + 터치 문구 미검출 -> 안전 터치 위치 사용 @ {safeTouch}");
                    return new DetectionResult(true, safeTouch, clearTitle.Score, "clear_touch_safe_fallback");
                }
            }
            else
            {
                clearTitleConsecutive = 0;
            }

            // Important: missing clear/touch templates is NOT treated as a successful
            // transition anymore. Keep waiting until the result screen is actually seen.
            await Task.Delay(Math.Max(200, _settings.PollIntervalMs), ct);
        }

        throw new TimeoutException("어비스 클리어 화면 이후 실제 결과 화면 또는 '화면을 터치해 주세요'를 20초 안에 확정하지 못했습니다.");
    }

    private async Task WaitForAbyssClearScreenGoneAsync(CancellationToken ct)
    {
        AbyssRequireState(AbyssFlowState.TouchClicked, "클리어 화면 클릭 후 결과 화면 대기");

        var sw = Stopwatch.StartNew();
        int retryClicks = 0;
        int clearTitleConsecutive = 0;
        DetectionResult previousResult = DetectionResult.NotFound;

        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);

            // Do not advance merely because the clear templates disappeared for a few
            // frames. The next state must be the actual Abyss result screen.
            var result = await DetectAbyssResultRetryAsync(frame, ct);
            if (result.Found)
            {
                if (previousResult.Found && result.Bounds.IntersectsWith(previousResult.Bounds))
                {
                    AbyssTransitionTo(AbyssFlowState.ResultConfirmed, "클리어 화면 클릭 후 실제 결과 화면 2프레임 연속 확인");
                    Log?.Invoke($"[어비스] 실제 결과 화면 2/2 확인 -> 다시 하기 단계 진행 @ {result.Bounds}");
                    return;
                }

                previousResult = result;
                Log?.Invoke($"[어비스] 실제 결과 화면 후보 1/2 @ {result.Bounds}");
                await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                continue;
            }
            previousResult = DetectionResult.NotFound;

            var touch = await _detector.DetectAsync("abyss_touch_screen", frame, ct);
            var clearTitle = await _detector.DetectAsync("abyss_dungeon_clear_visual", frame, ct);

            if (clearTitle.Found)
                clearTitleConsecutive++;
            else
                clearTitleConsecutive = 0;

            if (retryClicks < 1 && sw.Elapsed >= TimeSpan.FromSeconds(2))
            {
                DetectionResult retryTouch = DetectionResult.NotFound;

                if (touch.Found)
                {
                    retryTouch = touch;
                }
                else if (clearTitleConsecutive >= 2 && frame.Width == 800 && frame.Height == 1000)
                {
                    retryTouch = new DetectionResult(
                        true,
                        new Rectangle(360, 915, 80, 60),
                        clearTitle.Score,
                        "clear_touch_safe_retry");
                }

                if (retryTouch.Found)
                {
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    NativeMethods.SetForegroundWindow(_hwnd);
                    Log?.Invoke($"[어비스] 클리어 화면 유지 -> 안전 터치 1회 재시도 @ {retryTouch.Bounds} source={retryTouch.ReadText}");
                    _input.ClickClientPoint(_hwnd, retryTouch.Center);
                    retryClicks++;
                    await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);
                    continue;
                }
            }

            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }

        throw new TimeoutException("어비스 클리어 화면 클릭 후 실제 결과 화면을 30초 안에 확인하지 못했습니다. 다시 하기 단계로 넘어가지 않습니다.");
    }

    private async Task AdvanceAbyssClearScreenAsync(DetectionResult firstDetection, CancellationToken ct)
    {
        // DetectAbyssConfirmedClearAsync returns the touch-prompt detection itself after
        // clear-title + touch are simultaneously confirmed. Use that confirmed point
        // immediately instead of throwing it away and trying to detect the prompt again.
        if (firstDetection.Found)
        {
            AbyssTransitionTo(AbyssFlowState.ClearConfirmed, "클리어 화면 확정 결과를 터치 단계로 전달");
            AbyssTransitionTo(AbyssFlowState.TouchReady, "확정된 터치 문구 위치 사용");
        }

        var touch = firstDetection.Found ? firstDetection : await WaitForAbyssTouchPromptAsync(ct);

        if (!touch.Found)
        {
            // NotFound is allowed only when WaitForAbyssTouchPromptAsync already proved
            // that the real result screen is visible.
            AbyssRequireState(AbyssFlowState.ResultConfirmed, "클리어 터치 생략");
            Log?.Invoke("[어비스] 실제 결과 화면이 이미 확인됨 -> 클리어 터치 생략");
            return;
        }

        // No touch input is permitted unless the state machine reached TouchReady.
        AbyssRequireState(AbyssFlowState.TouchReady, "클리어 화면 터치");

        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        NativeMethods.SetForegroundWindow(_hwnd);
        Log?.Invoke($"[어비스] 확인된 '화면을 터치해 주세요' 위치 클릭 @ {touch.Bounds} source={touch.ReadText}");
        DiagnosticObserveClick("abyss_clear_touch", touch.Center);
        _input.ClickClientPoint(_hwnd, touch.Center);
        AbyssTransitionTo(AbyssFlowState.TouchClicked, "클리어 화면 터치 입력 완료");
        await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);

        // Step 5 is not complete until the actual result screen is confirmed.
        await WaitForAbyssClearScreenGoneAsync(ct);
    }

    private async Task<bool> DetectAbyssOutsideWorkflowAsync(Bitmap frame, CancellationToken ct)
    {
        var home = await _detector.DetectAsync("abyss_outside_home_key", frame, ct);
        var end = await _detector.DetectAsync("abyss_outside_end_key", frame, ct);
        var kHud = await _detector.DetectAsync("abyss_outside_k_hud", frame, ct);
        var iHud = await _detector.DetectAsync("abyss_outside_i_hud", frame, ct);

        int matched =
            (home.Found ? 1 : 0) +
            (end.Found ? 1 : 0) +
            (kHud.Found ? 1 : 0) +
            (iHud.Found ? 1 : 0);

        long now = Environment.TickCount64;
        bool scoreLogDue = _lastAbyssOutsideHudScoreLog == 0 || now - _lastAbyssOutsideHudScoreLog >= 2000;

        if (matched < 3)
        {
            if (scoreLogDue)
            {
                Log?.Invoke(
                    $"[어비스] 던전 밖 HUD 점수 Home={home.Score:0.000} End={end.Score:0.000} K={kHud.Score:0.000} I={iHud.Score:0.000} / 인식={matched}/4 -> 3개 미만");
                _lastAbyssOutsideHudScoreLog = now;
            }
            return false;
        }

        // Three of four fixed outside HUD markers are enough. A clear/result overlay is still
        // explicit dungeon-internal evidence and blocks outside acceptance.
        var clearTitle = await _detector.DetectAsync("abyss_dungeon_clear_visual", frame, ct);
        var touch = await _detector.DetectAsync("abyss_touch_screen", frame, ct);
        bool outside = !clearTitle.Found && !touch.Found;

        if (outside || scoreLogDue)
        {
            Log?.Invoke(
                $"[어비스] 던전 밖 HUD 점수 Home={home.Score:0.000} End={end.Score:0.000} K={kHud.Score:0.000} I={iHud.Score:0.000} / 인식={matched}/4 / Clear={(clearTitle.Found ? 1 : 0)} Touch={(touch.Found ? 1 : 0)} -> {(outside ? "밖 인정" : "클리어 화면으로 보류")}");
            _lastAbyssOutsideHudScoreLog = now;
        }

        return outside;
    }

    private async Task WaitForAbyssHomeAfterNormalExitAsync(CancellationToken ct)
    {
        Log?.Invoke("[어비스] 나가기 클릭 완료 -> 던전 밖 고정 HUD 복귀 확인 중");
        var sw = Stopwatch.StartNew();
        int outsideConsecutive = 0;

        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            if (await DetectAbyssOutsideWorkflowAsync(frame, ct))
            {
                outsideConsecutive++;
                Log?.Invoke($"[어비스] 던전 밖 HUD 3/4 이상 확인 {outsideConsecutive}/3");
                if (outsideConsecutive >= 3)
                {
                    Log?.Invoke("[어비스] 던전 밖 HUD 3회 연속 확인 완료 -> 다음 입장 준비");
                    return;
                }
            }
            else
            {
                outsideConsecutive = 0;
            }
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }

        throw new TimeoutException("나가기 후 60초 안에 던전 밖 고정 HUD 4개 중 3개 이상을 확인하지 못했습니다.");
    }

    private async Task ClickTargetWithTimeoutAsync(
        string targetId,
        int timeoutSeconds,
        string label,
        CancellationToken ct)
    {
        Log?.Invoke($"[{label}] 대기: {targetId} / {timeoutSeconds}s");
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            if (await CheckMonitorsAsync(frame, ct))
                continue;

            var found = await _detector.DetectAsync(targetId, frame, ct);
            if (found.Found)
            {
                Log?.Invoke($"[{label}] 발견 -> 중앙 클릭: {found.Score:0.000} @ {found.Bounds}");
                _hwnd = await ResolveRequiredGameWindowAsync(ct);
                NativeMethods.SetForegroundWindow(_hwnd);
                _input.ClickClientPoint(_hwnd, found.Center);
                await Task.Delay(_settings.ClickSettleMs, ct);
                return;
            }

            await Task.Delay(_settings.PollIntervalMs, ct);
        }

        throw new TimeoutException($"'{label}' 타깃 '{targetId}'을(를) {timeoutSeconds}초 안에 찾지 못했습니다.");
    }

    // V0166_ABYSS_SCENE_SKIP_OUTLINE
    private static DetectionResult DetectAbyssSceneSkipOutline(Bitmap frame)
    {
        Rectangle bounds = new(0, 0, frame.Width, frame.Height);
        Rectangle roi = Rectangle.Intersect(new Rectangle(620, 20, 180, 90), bounds);
        if (roi.Width < 80 || roi.Height < 30)
            return DetectionResult.NotFound;

        int bestRun = 0;
        int bestStartX = 0;
        int bestY = 0;

        for (int y = roi.Top; y < roi.Bottom; y++)
        {
            int run = 0;
            int runStart = roi.Left;

            for (int x = roi.Left; x < roi.Right; x++)
            {
                Color p = frame.GetPixel(x, y);
                int max = Math.Max(p.R, Math.Max(p.G, p.B));
                int min = Math.Min(p.R, Math.Min(p.G, p.B));

                bool neutralBright = min > 105 && max - min < 90;
                if (neutralBright)
                {
                    if (run == 0)
                        runStart = x;
                    run++;

                    if (run > bestRun)
                    {
                        bestRun = run;
                        bestStartX = runStart;
                        bestY = y;
                    }
                }
                else
                {
                    run = 0;
                }
            }
        }

        // Recorded real "장면 넘기기" frames had a ~99-112px continuous border.
        // Normal HUD samples stayed around 28px or less.
        if (bestRun < 75)
            return DetectionResult.NotFound;

        int left = Math.Max(roi.Left, bestStartX - 6);
        int top = Math.Max(roi.Top, bestY - 8);
        int width = Math.Min(frame.Width - left, bestRun + 12);
        int height = Math.Min(frame.Height - top, 48);

        return new DetectionResult(
            true,
            new Rectangle(left, top, width, height),
            Math.Min(1.0, bestRun / 105.0),
            $"scene-outline:{bestRun}");
    }

    private async Task<bool> CheckMonitorsAsync(Bitmap frame, CancellationToken ct)
    {
        if (IsAbyss && (await DetectAbyssResultRetryAsync(frame, ct)).Found)
            return false;
        foreach (var m in _scenario.Monitors)
        {
            long now = Environment.TickCount64;

            if (_monitorLastAction.TryGetValue(m.Target, out var lastAction) &&
                now - lastAction < Math.Max(0, m.CooldownMs))
                continue;

            int scanInterval = Math.Max(0, m.ScanIntervalMs);
            if (scanInterval > 0 &&
                _monitorLastScan.TryGetValue(m.Target, out var lastScan) &&
                now - lastScan < scanInterval)
                continue;
            _monitorLastScan[m.Target] = now;

            bool abyssRuntime =
                string.Equals(new DirectoryInfo(_baseDir).Name, "abyss", StringComparison.OrdinalIgnoreCase);

            DetectionResult r;
            if (abyssRuntime &&
                     m.Target.Equals("scene_skip", StringComparison.OrdinalIgnoreCase))
            {
                r = DetectAbyssSceneSkipOutline(frame);
            }
            else
            {
                r = await _detector.DetectAsync(m.Target, frame, ct);
            }

            if (!r.Found) continue;

            if (m.Target.Equals("scene_skip", StringComparison.OrdinalIgnoreCase))
            {
                Log?.Invoke($"[monitor] scene_skip 1/2 이미지 확인 ({r.Score:0.000}) -> 별도 프레임 재확인");
                await Task.Delay(220, ct);
                using var confirmFrame = await CaptureGameWindowAsync(ct);

                var confirm = abyssRuntime
                    ? DetectAbyssSceneSkipOutline(confirmFrame)
                    : await _detector.DetectAsync(m.Target, confirmFrame, ct);
                if (!confirm.Found)
                {
                    Log?.Invoke("[monitor] scene_skip 2차 이미지 확인 실패 -> 클릭 취소");
                    continue;
                }
                r = confirm;
                Log?.Invoke($"[monitor] scene_skip 2/2 이미지 연속 확인 -> 클릭 허용 ({r.Score:0.000})");
            }

            _monitorLastAction[m.Target] = Environment.TickCount64;
            switch (m.Action.ToLowerInvariant())
            {
                case "escape":
                {
                    // V0174_POTION_ESC_RETRY
                    // A real potion-shortage popup occasionally ignores a single ESC.
                    // Never click the purchase button. Confirm the same popup again and
                    // retry ESC up to three times, verifying disappearance after each try.
                    Log?.Invoke($"[monitor] {m.Target} 1/2 확인 -> ESC 닫기 재확인");
                    await Task.Delay(180, ct);
                    using (var confirmFrame = await CaptureGameWindowAsync(ct))
                    {
                        var confirm = await _detector.DetectAsync(m.Target, confirmFrame, ct);
                        if (!confirm.Found)
                        {
                            Log?.Invoke($"[monitor] {m.Target} 2차 확인에서 사라짐 -> 입력 없이 진행");
                            return true;
                        }
                    }

                    const int maxEscAttempts = 3;
                    for (int escAttempt = 1; escAttempt <= maxEscAttempts; escAttempt++)
                    {
                        _hwnd = await ResolveRequiredGameWindowAsync(ct);
                        NativeMethods.SetForegroundWindow(_hwnd);
                        await Task.Delay(120, ct);

                        Log?.Invoke(
                            $"[monitor] {m.Target} 2/2 확인 -> ESC {escAttempt}/{maxEscAttempts} " +
                            "(구매 버튼 클릭 금지)");
                        _input.TapScanCode(0x01);

                        var closeTimer = Stopwatch.StartNew();
                        int goneFrames = 0;
                        bool stillConfirmed = false;

                        while (closeTimer.Elapsed < TimeSpan.FromSeconds(2.5))
                        {
                            ct.ThrowIfCancellationRequested();
                            await Task.Delay(220, ct);

                            using var afterEsc = await CaptureGameWindowAsync(ct);
                            var stillOpen = await _detector.DetectAsync(m.Target, afterEsc, ct);
                            if (stillOpen.Found)
                            {
                                stillConfirmed = true;
                                goneFrames = 0;
                                continue;
                            }

                            if (++goneFrames >= 2)
                            {
                                Log?.Invoke(
                                    $"[monitor] {m.Target} ESC {escAttempt}/{maxEscAttempts} 후 " +
                                    "닫힘 2프레임 확인 -> 원래 진행 계속");
                                return true;
                            }
                        }

                        if (!stillConfirmed)
                        {
                            // Detection became unstable but never produced the two clean
                            // gone frames required above. Let the next monitor scan decide
                            // instead of turning this into a fatal macro error.
                            Log?.Invoke(
                                $"[monitor] {m.Target} ESC {escAttempt}/{maxEscAttempts} 후 " +
                                "팝업 판정 불안정 -> 추가 구매/클릭 없이 다음 감시에서 재확인");
                            return true;
                        }

                        if (escAttempt < maxEscAttempts)
                        {
                            Log?.Invoke(
                                $"[monitor] {m.Target} ESC {escAttempt}/{maxEscAttempts} 후에도 " +
                                "동일 팝업 유지 -> ESC 재시도");
                            await Task.Delay(350, ct);
                        }
                    }

                    // Keep the run alive. The monitor cooldown will expire and the same
                    // confirmed popup will be retried again. This is safer than stopping
                    // the whole Abyss loop or clicking a purchase coordinate.
                    Log?.Invoke(
                        $"[monitor] {m.Target} ESC {maxEscAttempts}회 후에도 팝업 유지 -> " +
                        "매크로 정지하지 않고 다음 감시 주기에 다시 시도");
                    return true;
                }
                case "click":
                    Log?.Invoke($"[monitor] {m.Target} 발견 → 클릭 ({r.ReadText ?? r.Score.ToString("0.000")}) @ {r.Bounds}");
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    NativeMethods.SetForegroundWindow(_hwnd);
                    _input.ClickClientPoint(_hwnd, r.Center);
                    await Task.Delay(_settings.ClickSettleMs, ct);
                    return true;
                case "restart_cycle":
                    Log?.Invoke($"[monitor] {m.Target} 발견 → 현재 판 재시작");
                    throw new RestartCycleException();
                case "stop":
                    throw new OperationCanceledException($"감시 타깃 '{m.Target}' 발견으로 정지");
                default:
                    throw new InvalidOperationException($"알 수 없는 monitor action: {m.Action}");
            }
        }
        return false;
    }

    // DUNGEON_FULL_STABILITY_HARDENING_V7
    // HANDLE_RECOVERY_V2
    private async Task<nint> ResolveRequiredGameWindowAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (WindowTools.IsRequiredGameWindow(_hwnd))
                return _hwnd;

            var found = WindowTools.FindRequiredGameWindow();

            if (found != 0)
            {
                if (found != _hwnd)
                {
                    Log?.Invoke(
                        $"마비노기 모바일 창 핸들 재연결: " +
                        $"0x{_hwnd.ToInt64():X} -> 0x{found.ToInt64():X}");
                }

                _hwnd = found;
                return _hwnd;
            }

            if (attempt == 0)
            {
                Log?.Invoke(
                    "마비노기 모바일 창 핸들이 사라졌습니다. " +
                    "새 창을 다시 찾는 중...");
            }

            await Task.Delay(200, ct);
        }

        throw new OperationCanceledException(
            "마비노기 모바일 창을 약 6초 동안 다시 찾지 못해 자동화를 정지합니다.");
    }

    private async Task<Bitmap> CaptureGameWindowAsync(CancellationToken ct)
    {
        if (_networkRestartRequested)
            throw new RestartCycleException();

        Exception? lastError = null;

        for (int attempt = 1; attempt <= 12; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            _hwnd = await ResolveRequiredGameWindowAsync(ct);

            try
            {
                Bitmap frame = _capture.CaptureClient(_hwnd);
                try
                {
                    _input.ObserveFrame(_hwnd, frame.Size);
                    DiagnosticObserveFrame(frame);
                    await HandleNetworkReconnectIfNeededAsync(frame, ct);
                    return frame;
                }
                catch
                {
                    frame.Dispose();
                    throw;
                }
            }
            catch (Exception ex) when (
                ex is InvalidOperationException ||
                ex is System.ComponentModel.Win32Exception ||
                ex is System.Runtime.InteropServices.ExternalException)
            {
                lastError = ex;

                if (attempt == 1 || attempt == 6)
                {
                    Log?.Invoke(
                        $"화면 캡처 재시도 {attempt}/12: {ex.Message}");
                }

                await Task.Delay(150, ct);
            }
        }

        throw new InvalidOperationException(
            "마비노기 모바일 화면 캡처가 반복해서 실패했습니다.",
            lastError);
    }
    private static string Safe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    public void Dispose() => _input.Dispose();
}
