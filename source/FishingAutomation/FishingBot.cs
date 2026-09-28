using System.Diagnostics;
using System.Drawing.Imaging;

namespace FishingAutomation;

public sealed class FishingBot : IDisposable
{
    private const int MaxDebugFiles = 150;
    private const long MaxDebugBytes = 100L * 1024 * 1024;

    private readonly AutomationConfig _cfg;
    private readonly AppLog _log;
    private readonly CaptureService _capture = new();
    private readonly TemplateMatcher _templates;
    private readonly IInputSender _input;
    private readonly OcrCoordinator _ocr;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private DateTime _lastMessageOcr = DateTime.MinValue;
    private DateTime _lastNetworkOcr = DateTime.MinValue;
    private int _round;
    private bool _secondRoundLocked;
    private bool _noSecondHandledThisRound;

    public event Action<string>? StatusChanged;
    public bool IsRunning => _task is { IsCompleted: false };
    public bool InputReady => _input.IsAvailable;
    public string InputName => _input.Name;

    public FishingBot(AutomationConfig cfg, AppLog log)
    {
        _cfg = cfg;
        _log = log;
        string baseDir = AppContext.BaseDirectory;
        _templates = new TemplateMatcher(Path.Combine(baseDir, "templates"), log);
        _input = InputSenderFactory.Create(cfg, log);
        _ocr = new OcrCoordinator(log);
        Directory.CreateDirectory(Path.Combine(baseDir, cfg.DebugFolder));
        PruneDebugScreenshots();
        _log.Write("입력 방식: " + _input.Name);
    }

    public void Start()
    {
        if (IsRunning) return;
        if (!_input.IsAvailable)
        {
            Status("입력 준비 안 됨 · Interception 설치/재부팅 필요");
            _log.Write("시작 취소: Interception 입력이 준비되지 않음");
            return;
        }
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    public void TestSpace()
    {
        if (!_input.IsAvailable)
        {
            Status("입력 테스트 불가 · Interception 설치 필요");
            _log.Write("입력 테스트 취소: Interception 준비 안 됨");
            return;
        }

        Task.Run(() =>
        {
            GameWindow? window = WindowLocator.FindAndPrepare(_cfg, _log);
            if (window is null)
            {
                Status("입력 테스트 실패 · 게임 창을 찾지 못함");
                return;
            }
            WindowLocator.ActivateForInput(window);
            Thread.Sleep(120);
            bool ok = _input.TapSpace();
            Status(ok ? "입력 테스트 · Space 전송 성공" : "입력 테스트 · Space 전송 실패");
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        Status("정지");
        _log.Write("사용자 정지(F10)");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                GameWindow? window = WindowLocator.FindAndPrepare(_cfg, _log);
                if (window is null)
                {
                    Status($"게임 창 대기 · {_cfg.ClientWidth}x{_cfg.ClientHeight} 필요");
                    await Task.Delay(1000, ct);
                    continue;
                }

                _round++;
                _ocr.ClearSignal();
                _secondRoundLocked = false;
                _noSecondHandledThisRound = false;
                WindowLocator.ActivateForInput(window);
                Status($"시전 아이콘 대기 · {_round}판");
                bool cast = await WaitReadyAndCast(window, ct);
                if (!cast) continue;

                GaugeAnchor? anchor = await WaitFirstGauge(window, ct);
                if (anchor is null)
                {
                    // Do not immediately recast while the previous fishing action may
                    // still be finishing.  Wait until the center fishing slot really
                    // returns to the hook icon.
                    await WaitRoundReturn(window, ct);
                    continue;
                }

                bool dropped = await WaitDrop(window, anchor, ct);
                if (!dropped)
                {
                    if (!_noSecondHandledThisRound)
                        await WaitRoundReturn(window, ct);
                    continue;
                }

                // WaitDrop decides whether a late fall / OCR hint locks this round
                // as a second-rebound round.  Once locked, later OCR ambiguity must
                // never send S and cancel a valid second round.
                await WaitReboundAndHook(window, anchor, ct);

                // Whether we hooked successfully or the gauge simply disappeared, do
                // not start a new round until the actual hook icon has returned.  v9
                // used to jump straight back to stage 1 on a stage-4 miss, which caused
                // false compass matches and repeated S presses during the end animation.
                await WaitRoundReturn(window, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Write("치명적 오류: " + ex);
            Status("오류 · fishing.log 확인");
        }
        finally
        {
            Status("정지");
        }
    }

    private async Task<bool> WaitReadyAndCast(GameWindow window, CancellationToken ct)
    {
        DateTime lastCompassTap = DateTime.MinValue;
        DateTime lastDiagnostic = DateTime.MinValue;
        Stopwatch readyWait = Stopwatch.StartNew();
        Stopwatch hardReadyWait = Stopwatch.StartNew();
        int hookFrames = 0;
        int compassFrames = 0;
        int compassRecoveryFrames = 0;
        MatchResult? lastCompass = null;

        while (!ct.IsCancellationRequested)
        {
            using var f = _capture.Capture(window);
            QueueOcr(f, allowMessage: false);
            if (await HandleNetworkIfNeeded(window, ct)) return false;

            Rectangle slot = _cfg.HookRoi.ToRectangle();
            var hook = _templates.MatchHook(f.Gray, slot);
            if (hook.Score >= 0.80 && hook.Scale >= 0.90 && hook.Scale <= 1.10)
            {
                hookFrames++;
                compassFrames = 0;
                if (hookFrames >= 2)
                {
                    _log.Write($"낚싯대 아이콘 {hook.Score:F3} scale={hook.Scale:F2}, 2프레임 -> Space");
                    WindowLocator.ActivateForInput(window);
                    Status($"낚싯대 인식 {hook.Score:F2} · Space 입력");
                    _input.TapSpace();
                    await Task.Delay(120, ct);
                    return true;
                }
            }
            else
            {
                hookFrames = 0;

                var compass = _templates.MatchCompass(f.Gray, slot);
                lastCompass = compass;
                bool compassCentered = compass is not null &&
                    Math.Abs((compass.Rect.Left + compass.Rect.Width / 2) - (slot.Left + slot.Width / 2)) <= 60 &&
                    Math.Abs((compass.Rect.Top + compass.Rect.Height / 2) - (slot.Top + slot.Height / 2)) <= 60;
                bool primaryCompass = compass is not null && compass.Score >= _cfg.CompassThreshold && hook.Score < 0.75;
                bool recoveryCompass = compass is not null && readyWait.ElapsedMilliseconds >= 1000 &&
                    compass.Score >= 0.28 && hook.Score < 0.70 && compassCentered && compass.Scale >= 0.75 && compass.Scale <= 1.15;

                if (primaryCompass)
                {
                    compassFrames++;
                    compassRecoveryFrames = 0;
                    if (compassFrames >= 2 && DateTime.UtcNow - lastCompassTap > TimeSpan.FromMilliseconds(900))
                    {
                        _log.Write($"나침반 아이콘 {compass.Score:F3} scale={compass.Scale:F2}, 2프레임 -> S");
                        WindowLocator.ActivateForInput(window);
                        _input.TapS();
                        lastCompassTap = DateTime.UtcNow;
                        readyWait.Restart();
                        compassFrames = 0;
                    }
                }
                else if (recoveryCompass)
                {
                    compassFrames = 0;
                    compassRecoveryFrames++;
                    if (compassRecoveryFrames >= 6 && DateTime.UtcNow - lastCompassTap > TimeSpan.FromMilliseconds(900))
                    {
                        _log.Write($"나침반 복구 인식 {compass.Score:F3} scale={compass.Scale:F2}, 중앙 6프레임 -> S");
                        WindowLocator.ActivateForInput(window);
                        _input.TapS();
                        lastCompassTap = DateTime.UtcNow;
                        readyWait.Restart();
                        compassRecoveryFrames = 0;
                    }
                }
                else
                {
                    compassFrames = 0;
                    compassRecoveryFrames = 0;
                }
            }

            if (DateTime.UtcNow - lastDiagnostic >= TimeSpan.FromSeconds(1))
            {
                lastDiagnostic = DateTime.UtcNow;
                string compassStatus = lastCompass is null ? "없음" : $"{lastCompass.Score:F2} x{lastCompass.Scale:F2}";
                Status($"시전 대기 · Hook {hook.Score:F2} x{hook.Scale:F2} · Compass {compassStatus}");
                _log.Write($"진단 Hook score={hook.Score:F3}, scale={hook.Scale:F2}, rect={hook.Rect.X},{hook.Rect.Y},{hook.Rect.Width},{hook.Rect.Height}; Compass={compassStatus}");
            }

            // Final safety net: stage 1 must never wait forever.
            // Normal hook/compass recovery keeps priority for 10 seconds.
            // If unresolved, send one S, clear OCR, return false and let RunAsync start fresh.
            if (hardReadyWait.ElapsedMilliseconds >= 10000 && hook.Score < _cfg.HookThreshold)
            {
                string hardCompass = lastCompass is null ? "없음" : $"{lastCompass.Score:F2} x{lastCompass.Scale:F2}";
                Status("시전 대기 10초 초과 · 초기화 후 재시작");
                _log.Write($"시전 대기/복구 10초 초과: Hook={hook.Score:F3} x{hook.Scale:F2}; Compass={hardCompass} -> S 복구 후 처음부터 재시작");
                SaveDebug(f.Bitmap, "stage1_hard_reset_10s");
                WindowLocator.ActivateForInput(window);
                _input.TapS();
                _ocr.ClearSignal();
                await Task.Delay(500, ct);
                return false;
            }

            await Task.Delay(80, ct);
        }
        return false;
    }

    private async Task<GaugeAnchor?> WaitFirstGauge(GameWindow window, CancellationToken ct)
    {
        Status("게이지 1차 감지");
        Stopwatch sw = Stopwatch.StartNew();
        CapturedFrame? last = null;
        DateTime lastDiagnostic = DateTime.MinValue;
        try
        {
            while (sw.ElapsedMilliseconds < _cfg.Stage2TimeoutMs && !ct.IsCancellationRequested)
            {
                last?.Dispose();
                last = _capture.Capture(window);
                QueueOcr(last, allowMessage: true);
                if (await HandleNetworkIfNeeded(window, ct)) return null;

                GaugeAnchor? anchor = _templates.DetectGauge(last, _cfg, out MatchResult gaugeMatch);
                if (anchor is not null)
                {
                    FillMeasurement first = TemplateMatcher.MeasureFill(last.Bgr, anchor);
                    _log.Write($"1차 게이지 감지 score={anchor.Score:F3}, scale={anchor.Scale:F2}, bar=({anchor.LeftX},{anchor.CenterY}), len={anchor.Length}, fill={first.Fill}, visible={first.Visible}, empty={(anchor.HasEmptySample ? $"{anchor.EmptyB}/{anchor.EmptyG}/{anchor.EmptyR}" : "none")}");
                    Status($"게이지 인식 · {first.Fill}px / {anchor.Length}px");
                    return anchor;
                }

                if (DateTime.UtcNow - lastDiagnostic >= TimeSpan.FromSeconds(1))
                {
                    lastDiagnostic = DateTime.UtcNow;
                    Status($"게이지 감지 · {gaugeMatch.Score:F2} x{gaugeMatch.Scale:F2}");
                    _log.Write($"진단 Gauge shape={gaugeMatch.Score:F3}, scale={gaugeMatch.Scale:F2}, rect={gaugeMatch.Rect.X},{gaugeMatch.Rect.Y},{gaugeMatch.Rect.Width},{gaugeMatch.Rect.Height}");
                }

                await Task.Delay(_cfg.CaptureIntervalMs, ct);
            }

            if (last is null) last = _capture.Capture(window);
            SaveDebug(last.Bitmap, "stage2_first_fail_15s");
            _log.Write("1차 실패: 15초 내 게이지 미검출");
            return null;
        }
        finally
        {
            last?.Dispose();
        }
    }

    private async Task<bool> WaitDrop(GameWindow window, GaugeAnchor anchor, CancellationToken ct)
    {
        Status("빠짐 감지");
        Stopwatch sw = Stopwatch.StartNew();
        int maxFill = 0;
        int downFrames = 0;
        DateTime? missingSince = null;
        bool zeroSaved = false;

        // Capture a message that may have appeared just before the first gauge
        // detector returned.  We then latch both categories locally for the round.
        OcrSignal initialSignal = _ocr.PeekRecentSignal(TimeSpan.FromSeconds(4));
        bool secondHintSeen = initialSignal == OcrSignal.GreatSuccessHint;
        bool noSecondHintSeen = initialSignal == OcrSignal.NoSecondRound;

        // Explicit no-second text is authoritative and should be acted on immediately;
        // do not wait for the gauge timing heuristic.
        if (noSecondHintSeen)
        {
            _log.Write("2차 없음 확정: 게이지 감지 시점에 OCR 문구 확인 -> 즉시 S 두 번");
            _noSecondHandledThisRound = true;
            await HandleNoSecondRound(window, ct);
            return false;
        }
        if (secondHintSeen)
            LockSecondRound("게이지 감지 시점 OCR 2차문구");

        while (sw.ElapsedMilliseconds < _cfg.Stage3TimeoutMs && !ct.IsCancellationRequested)
        {
            using var f = _capture.Capture(window);
            QueueOcr(f, allowMessage: true);
            if (await HandleNetworkIfNeeded(window, ct)) return false;

            OcrSignal sig = _ocr.PeekRecentSignal(TimeSpan.FromSeconds(3));
            if (sig == OcrSignal.NoSecondRound)
            {
                noSecondHintSeen = true;
                _log.Write("2차 없음 확정: Stage3 OCR 문구 확인 -> 즉시 S 두 번");
                _noSecondHandledThisRound = true;
                await HandleNoSecondRound(window, ct);
                return false;
            }
            if (sig == OcrSignal.GreatSuccessHint)
            {
                secondHintSeen = true;
                LockSecondRound("Stage3 OCR 2차문구");
            }

            FillMeasurement m = TemplateMatcher.MeasureFill(f.Bgr, anchor);
            if (m.Fill == 0 && !zeroSaved)
            {
                SaveDebug(f.Bitmap, "fill_zero_stage3");
                zeroSaved = true;
            }

            if (m.Visible)
            {
                missingSince = null;
                maxFill = Math.Max(maxFill, m.Fill);
                if (maxFill - m.Fill >= _cfg.DropPixels)
                    downFrames++;
                else
                    downFrames = 0;

                Status($"빠짐 감지 · {m.Fill}px (최고 {maxFill})");
                if (downFrames >= _cfg.DropFrames)
                {
                    long dropDelayMs = sw.ElapsedMilliseconds;
                    _log.Write($"빠짐 확정: fill={m.Fill}, max={maxFill}, {downFrames}프레임, firstGauge→drop={dropDelayMs}ms");
                    return await ClassifyDropAndContinue(window, dropDelayMs, secondHintSeen, noSecondHintSeen, ct);
                }
            }
            else
            {
                missingSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - missingSince >= TimeSpan.FromMilliseconds(_cfg.Stage3MissingMs))
                {
                    long dropDelayMs = sw.ElapsedMilliseconds;
                    _log.Write($"게이지 0.6초 이상 미표시 -> 빠짐으로 인정, firstGauge→drop={dropDelayMs}ms");
                    return await ClassifyDropAndContinue(window, dropDelayMs, secondHintSeen, noSecondHintSeen, ct);
                }
            }
            await Task.Delay(_cfg.CaptureIntervalMs, ct);
        }

        using (var f = _capture.Capture(window)) SaveDebug(f.Bitmap, "stage3_stuck_8s");
        _log.Write("8초 정체 -> 처음으로");
        return false;
    }

    private async Task<bool> ClassifyDropAndContinue(
        GameWindow window, long dropDelayMs, bool secondHintSeen, bool noSecondHintSeen, CancellationToken ct)
    {
        bool lateDrop = dropDelayMs >= _cfg.LateDropSecondRoundMs;

        // User rule: explicit "반응이 왔다" / "뭔가 걸렸다" always means NO SECOND.
        // Message evidence has priority over timing, even when the drop is late.
        if (noSecondHintSeen)
        {
            _log.Write($"2차 없음 확정: OCR 우선, drop={dropDelayMs}ms (늦은하강 여부={lateDrop})");
            _noSecondHandledThisRound = true;
            await HandleNoSecondRound(window, ct);
            return false;
        }

        if (secondHintSeen)
        {
            LockSecondRound($"OCR 2차문구, drop={dropDelayMs}ms", "2차 판정 · OCR");
            return true;
        }

        // Timing is only a fallback when OCR did not identify either message family.
        if (lateDrop)
        {
            LockSecondRound($"OCR 미확정 + 늦은하강 {dropDelayMs}ms≥{_cfg.LateDropSecondRoundMs}ms",
                $"2차 판정 · 늦은하강 {dropDelayMs}ms");
            return true;
        }

        // No reliable message and a fast drop: do NOT guess "no second".  Waiting
        // for the rebound is safer than cancelling a possible valid second round.
        _log.Write($"2차 판정 미확정: 빠른하강 {dropDelayMs}ms, OCR 없음 -> 반등 감시 계속");
        return true;
    }

    private bool LockSecondRound(string reason, string? status = null)
    {
        if (_secondRoundLocked) return false;
        _secondRoundLocked = true;
        _log.Write("2차 판정 잠금: " + reason);
        if (!string.IsNullOrWhiteSpace(status)) Status(status);
        return true;
    }

    private async Task<bool> WaitReboundAndHook(GameWindow window, GaugeAnchor anchor, CancellationToken ct)
    {
        Status("2차 재차오름 감지");
        Stopwatch sw = Stopwatch.StartNew();
        int low = int.MaxValue;
        bool rebound = false;
        int reboundStart = 0;
        DateTime reboundAt = DateTime.MinValue;
        DateTime? missingSince = null;
        bool zeroSaved = false;

        while (sw.ElapsedMilliseconds < _cfg.Stage4TimeoutMs && !ct.IsCancellationRequested)
        {
            using var f = _capture.Capture(window);
            QueueOcr(f, allowMessage: true);
            if (await HandleNetworkIfNeeded(window, ct)) return false;

            var hook = _templates.MatchHook(f.Gray, _cfg.HookRoi.ToRectangle());
            if (hook.Score >= _cfg.HookThreshold)
            {
                _log.Write("2차 대기 중 낚싯대 아이콘 재등장 -> 판 종료");
                return false;
            }

            OcrSignal sig = _ocr.PeekRecentSignal(TimeSpan.FromSeconds(3));
            if (sig == OcrSignal.NoSecondRound)
            {
                // Explicit no-second text overrides a timing-only second lock.
                _log.Write("2차 없음 확정: Stage4 OCR 문구 확인 -> S 두 번");
                _secondRoundLocked = false;
                _noSecondHandledThisRound = true;
                await HandleNoSecondRound(window, ct);
                return false;
            }
            if (sig == OcrSignal.GreatSuccessHint)
                LockSecondRound("Stage4 OCR 2차문구");

            FillMeasurement m = TemplateMatcher.MeasureFill(f.Bgr, anchor);
            if (m.Fill == 0 && !zeroSaved)
            {
                SaveDebug(f.Bitmap, "fill_zero_stage4");
                zeroSaved = true;
            }

            if (!m.Visible)
            {
                missingSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - missingSince >= TimeSpan.FromMilliseconds(_cfg.Stage4MissingMs))
                {
                    SaveDebug(f.Bitmap, "stage4_gauge_missing_1s");
                    _log.Write("2차 놓침: 게이지 1초 이상 사라짐");
                    return false;
                }
                await Task.Delay(_cfg.CaptureIntervalMs, ct);
                continue;
            }

            missingSince = null;

            if (!rebound)
            {
                low = Math.Min(low, m.Fill);
                Status($"2차 대기 · {m.Fill}px (최저 {low})");
                if (m.Fill >= low + _cfg.ReboundStartPixels)
                {
                    rebound = true;
                    reboundStart = m.Fill;
                    reboundAt = DateTime.UtcNow;
                    _log.Write($"반등 시작: low={low}, start={reboundStart}");
                }
            }
            else
            {
                Status($"반등 확인 · 시작 {reboundStart}px → 현재 {m.Fill}px");
                if (m.Fill <= reboundStart - _cfg.ReboundCancelPixels)
                {
                    _log.Write($"반등 취소: {m.Fill}px");
                    rebound = false;
                    low = m.Fill;
                }
                else if (DateTime.UtcNow - reboundAt <= TimeSpan.FromMilliseconds(_cfg.ReboundWindowMs) &&
                         m.Fill >= reboundStart + _cfg.ReboundConfirmPixels)
                {
                    _log.Write($"챔질: {reboundStart}px -> {m.Fill}px, Space");
                    WindowLocator.ActivateForInput(window);
                    _input.TapSpace();
                    await Task.Delay(120, ct);
                    return true;
                }
                else if (DateTime.UtcNow - reboundAt > TimeSpan.FromMilliseconds(_cfg.ReboundWindowMs))
                {
                    // 1초 안에 +4px 확정되지 않으면 새 최저점부터 다시 본다.
                    rebound = false;
                    low = Math.Min(low, m.Fill);
                }
            }

            await Task.Delay(_cfg.CaptureIntervalMs, ct);
        }

        using (var f = _capture.Capture(window)) SaveDebug(f.Bitmap, "stage4_no_rebound_15s");
        _log.Write("2차 놓침: 15초 내 반등 없음");
        return false;
    }

    private async Task HandleNoSecondRound(GameWindow window, CancellationToken ct)
    {
        Status("2차 없음 · 낚시 정리");
        _log.Write("2차 없는 판 -> S 두 번");
        WindowLocator.ActivateForInput(window);
        _input.TapS();
        await Task.Delay(500, ct);
        WindowLocator.ActivateForInput(window);
        _input.TapS();
        _ocr.ClearSignal();
        await WaitRoundReturn(window, ct, 10000);
    }

    private async Task WaitRoundReturn(GameWindow window, CancellationToken ct, int timeoutMs = 10000)
    {
        Status("다음 판 준비 대기");
        Stopwatch sw = Stopwatch.StartNew();
        DateTime lastCompassTap = DateTime.MinValue;
        DateTime lastDiagnostic = DateTime.MinValue;
        int hookFrames = 0;
        int compassFrames = 0;
        int compassRecoveryFrames = 0;
        int compassTaps = 0;
        MatchResult? lastCompass = null;

        while (sw.ElapsedMilliseconds < timeoutMs && !ct.IsCancellationRequested)
        {
            using var f = _capture.Capture(window);
            QueueOcr(f, allowMessage: false);
            if (await HandleNetworkIfNeeded(window, ct)) return;

            Rectangle slot = _cfg.HookRoi.ToRectangle();
            var hook = _templates.MatchHook(f.Gray, slot);
            if (hook.Score >= 0.80 && hook.Scale >= 0.90 && hook.Scale <= 1.10)
            {
                hookFrames++;
                compassFrames = 0;
                if (hookFrames >= 2)
                {
                    _log.Write($"낚싯대 아이콘 복귀 {hook.Score:F3}, 2프레임 -> 다음 판");
                    return;
                }
            }
            else
            {
                hookFrames = 0;
                var compass = _templates.MatchCompass(f.Gray, slot);
                lastCompass = compass;
                bool compassCentered = compass is not null &&
                    Math.Abs((compass.Rect.Left + compass.Rect.Width / 2) - (slot.Left + slot.Width / 2)) <= 60 &&
                    Math.Abs((compass.Rect.Top + compass.Rect.Height / 2) - (slot.Top + slot.Height / 2)) <= 60;
                bool primaryCompass = compass is not null && compass.Score >= _cfg.CompassThreshold && hook.Score < 0.75;
                bool recoveryCompass = compass is not null && sw.ElapsedMilliseconds >= 1000 &&
                    compass.Score >= 0.28 && hook.Score < 0.70 && compassCentered && compass.Scale >= 0.75 && compass.Scale <= 1.15;

                if (primaryCompass)
                {
                    compassFrames++;
                    compassRecoveryFrames = 0;
                    if (compassFrames >= 2 && compassTaps < 3 &&
                        DateTime.UtcNow - lastCompassTap > TimeSpan.FromMilliseconds(1000))
                    {
                        WindowLocator.ActivateForInput(window);
                        _input.TapS();
                        lastCompassTap = DateTime.UtcNow;
                        compassTaps++;
                        compassFrames = 0;
                        _log.Write($"종료 후 나침반 감지 {compass.Score:F3}, 2프레임 -> S ({compassTaps}/3)");
                    }
                }
                else if (recoveryCompass)
                {
                    compassFrames = 0;
                    compassRecoveryFrames++;
                    if (compassRecoveryFrames >= 6 && compassTaps < 3 &&
                        DateTime.UtcNow - lastCompassTap > TimeSpan.FromMilliseconds(1000))
                    {
                        WindowLocator.ActivateForInput(window);
                        _input.TapS();
                        lastCompassTap = DateTime.UtcNow;
                        compassTaps++;
                        compassRecoveryFrames = 0;
                        _log.Write($"종료 후 나침반 복구 {compass.Score:F3}, 중앙 6프레임 -> S ({compassTaps}/3)");
                    }
                }
                else
                {
                    compassFrames = 0;
                    compassRecoveryFrames = 0;
                }
            }

            if (DateTime.UtcNow - lastDiagnostic >= TimeSpan.FromSeconds(1))
        {
            lastDiagnostic = DateTime.UtcNow;
            string compassStatus = lastCompass is null ? "없음" : $"{lastCompass.Score:F2} x{lastCompass.Scale:F2}";
            Status($"다음 판 대기 · Hook {hook.Score:F2} x{hook.Scale:F2} · Compass {compassStatus}");
            _log.Write($"복귀 진단 Hook={hook.Score:F3} x{hook.Scale:F2}; Compass={compassStatus}; S={compassTaps}/3");
        }

        await Task.Delay(100, ct);
    }
    _log.Write("다음 판 준비 10초 대기 만료");
    }

    private void QueueOcr(CapturedFrame frame, bool allowMessage)
    {
        DateTime now = DateTime.UtcNow;
        bool messageDue = allowMessage && now - _lastMessageOcr >= TimeSpan.FromMilliseconds(_cfg.MessageOcrIntervalMs);
        if (messageDue)
        {
            _lastMessageOcr = now;
            _ocr.QueueMessage(frame.Bitmap, _cfg.MessageRoi.ToRectangle());
        }

        bool networkDue = now - _lastNetworkOcr >= TimeSpan.FromMilliseconds(_cfg.NetworkOcrIntervalMs);
        if (networkDue)
        {
            _lastNetworkOcr = now;
            _ocr.QueueNetworkCheck(frame.Bitmap, _cfg.NetworkRoi.ToRectangle());
        }
    }

    private async Task<bool> HandleNetworkIfNeeded(GameWindow window, CancellationToken ct)
    {
        if (!_ocr.ConsumeRetry()) return false;
        Status("네트워크 재시도");
        WindowLocator.ActivateForInput(window);
        _input.TapEnter();
        await Task.Delay(1500, ct);
        return true;
    }

    private void SaveDebug(Bitmap bitmap, string reason)
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, _cfg.DebugFolder);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{reason}.png");
            bitmap.Save(path, ImageFormat.Png);
            PruneDebugScreenshots();
            _log.Write("DEBUG 저장: " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _log.Write("DEBUG 저장 실패: " + ex.Message);
        }
    }

    private void PruneDebugScreenshots()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, _cfg.DebugFolder);
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            long total = 0;
            for (int i = 0; i < files.Count; i++)
            {
                FileInfo file = files[i];
                if (i >= MaxDebugFiles || total + file.Length > MaxDebugBytes)
                {
                    try { file.Delete(); } catch { }
                }
                else total += file.Length;
            }
        }
        catch { }
    }

    private void Status(string text) => StatusChanged?.Invoke(text);

    public void Dispose()
    {
        Stop();
        _input.Dispose();
        _templates.Dispose();
        _cts?.Dispose();
    }
}
