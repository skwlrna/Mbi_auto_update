using System.Diagnostics;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private OcrRecognizer? _abyssMenuOcr;

    /// <summary>
    /// The menu may already be open when the processing scheduler hands over input.
    /// Template recognition is preferred. Exact OCR of two independent menu
    /// labels is a fallback for version-dependent icon artwork. Neither
    /// classification nor recovery clicks an unverified position.
    /// </summary>
    private async Task<DetectionResult> DetectOpenAbyssMenuAsync(Bitmap frame, CancellationToken ct)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return DetectionResult.NotFound;

        var icon = await _detector.DetectAsync("abyss_icon", frame, ct);
        if (icon.Found && AbyssMenuEntryPolicy.IconEvidence(icon.Bounds))
            return icon;

        _abyssMenuOcr ??= new OcrRecognizer();
        var abyss = await _abyssMenuOcr.FindCompactLabelAsync(
            frame, AbyssMenuEntryPolicy.AbyssCell, "어비스", ct);
        if (!abyss.Found || !AbyssMenuEntryPolicy.IconEvidence(abyss.Bounds))
            return DetectionResult.NotFound;

        // The "길드" icon is below the "어비스" row in the same menu, and
        // independently disambiguates a bare '어비스' label in other screens.
        var guild = await _abyssMenuOcr.FindCompactLabelAsync(
            frame, AbyssMenuEntryPolicy.GuildLabel, "길드", ct);
        if (!guild.Found || !AbyssMenuEntryPolicy.ContextEvidence(guild.Bounds))
            return DetectionResult.NotFound;

        return abyss;
    }

    private async Task ExecuteAbyssMenuOpenStepAsync(ScenarioStep step, CancellationToken ct)
    {
        Log?.Invoke($"[{step.Name}] 대기: 이미 열린 메뉴/ESC 버튼 / {step.TimeoutSeconds}s");
        var sw = Stopwatch.StartNew();
        DetectionResult last = DetectionResult.NotFound;

        while (sw.Elapsed < TimeSpan.FromSeconds(step.TimeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var opened = await DetectOpenAbyssMenuAsync(frame, ct);
            DiagnosticObserveDetection("abyss_menu_already_open", opened);
            if (AbyssMenuEntryPolicy.Stable(last, opened))
            {
                Log?.Invoke("[어비스 메뉴] 우측 메뉴에서 어비스 항목 2프레임 확인 -> 메뉴 클릭 생략 · 2단계 진행");
                return;
            }
            last = opened;

            // One ambiguous frame is not permission to click the ESC icon:
            // the next fresh frame must settle the menu-open classification.
            if (!opened.Found)
            {
                var closedMenu = await _detector.DetectAsync("abyss_menu", frame, ct);
                DiagnosticObserveDetection("abyss_menu", closedMenu);
                if (closedMenu.Found)
                {
                    _hwnd = await ResolveRequiredGameWindowAsync(ct);
                    NativeMethods.SetForegroundWindow(_hwnd);
                    _input.ClickClientPoint(_hwnd, closedMenu.Center);
                    DiagnosticObserveClick("abyss_menu", closedMenu.Center);
                    Log?.Invoke($"[어비스 메뉴] 닫힌 메뉴 ESC 아이콘 확인 -> 열기 클릭 @ {closedMenu.Bounds}");
                    await Task.Delay(Math.Max(500, _settings.ClickSettleMs), ct);
                    return; // The next step must separately verify the actual open menu.
                }
            }

            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }

        throw new TimeoutException($"'{step.Name}' 메뉴 열림/메뉴 버튼을 {step.TimeoutSeconds}초 안에 확인하지 못했습니다.");
    }

    private async Task ExecuteAbyssMenuIconStepAsync(ScenarioStep step, CancellationToken ct)
    {
        Log?.Invoke($"[{step.Name}] 대기: 우측 메뉴 어비스 셀 2프레임 / {step.TimeoutSeconds}s");
        var sw = Stopwatch.StartNew();
        DetectionResult last = DetectionResult.NotFound;
        int rejectedClicks = 0;

        while (sw.Elapsed < TimeSpan.FromSeconds(step.TimeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var icon = await DetectOpenAbyssMenuAsync(frame, ct);
            DiagnosticObserveDetection("abyss_icon_menu", icon);
            bool stable = AbyssMenuEntryPolicy.Stable(last, icon);
            last = icon;
            if (!stable)
            {
                await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                continue;
            }

            _hwnd = await ResolveRequiredGameWindowAsync(ct);
            if (WindowTools.GetClientScreenRect(_hwnd).Size != frame.Size)
                throw new InvalidOperationException("어비스 메뉴 클릭 직전 게임 창 크기 변경 -> 입력 차단");
            ct.ThrowIfCancellationRequested();
            NativeMethods.SetForegroundWindow(_hwnd);
            _input.ClickClientPoint(_hwnd, icon.Center);
            DiagnosticObserveClick("abyss_icon_menu", icon.Center);
            Log?.Invoke($"[어비스 메뉴] 어비스 항목 2프레임 검증 -> 클릭 @ {icon.Bounds}");
            await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);

            if (await VerifyAbyssSelectionScreenAsync(ct))
            {
                Log?.Invoke("[어비스 메뉴] 어비스 던전 선택 화면 2프레임 확인 -> 다음 단계");
                return;
            }

            if (++rejectedClicks >= 3)
                throw new TimeoutException("어비스 메뉴 항목은 인식됐으나 3회 클릭 후 던전 선택 화면을 확인하지 못했습니다.");

            // Never guess an alternative icon; only the same restricted menu
            // cell can be clicked again after fresh 2-frame evidence.
            last = DetectionResult.NotFound;
            Log?.Invoke($"[어비스 메뉴] 화면 전환 미확인 {rejectedClicks}/3 -> ESC/다른 아이콘 클릭 없이 재확인");
            await Task.Delay(Math.Max(400, _settings.PollIntervalMs), ct);
        }

        throw new TimeoutException($"'{step.Name}' 메뉴의 어비스 항목을 {step.TimeoutSeconds}초 안에 확인하지 못했습니다.");
    }

    private async Task<bool> TryRecoverAbyssEntryScreenAsync(CancellationToken ct)
    {
        // Entry steps are NOT combat. In particular, never invoke the 660s
        // clear/treasure/exit recovery for a menu which is already open.
        var sw = Stopwatch.StartNew();
        DetectionResult previousMenu = DetectionResult.NotFound;
        DetectionResult previousClosed = DetectionResult.NotFound;
        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var opened = await DetectOpenAbyssMenuAsync(frame, ct);
            if (AbyssMenuEntryPolicy.Stable(previousMenu, opened))
            {
                _resumeStepIndex = FindDungeonStepIndex("abyss_icon");
                Log?.Invoke("[어비스 진입 복구] 메뉴가 이미 열려 있음 2프레임 확인 -> 어비스 아이콘 단계부터 재개 · 추가 클릭 없음");
                return true;
            }
            previousMenu = opened;

            // The dungeon-selection screen is also a valid, known, non-combat
            // state; resume selecting the configured dungeon, never ESC.
            if (await VerifyAbyssSelectionScreenAsync(ct))
            {
                _resumeStepIndex = FindDungeonStepIndex("abyss_selected_dungeon");
                Log?.Invoke("[어비스 진입 복구] 던전 선택 화면 확인 -> 던전 선택부터 재개");
                return true;
            }

            var closed = await _detector.DetectAsync("abyss_menu", frame, ct);
            if (closed.Found && previousClosed.Found &&
                closed.Bounds.IntersectsWith(previousClosed.Bounds))
            {
                _resumeStepIndex = FindDungeonStepIndex("abyss_menu");
                Log?.Invoke("[어비스 진입 복구] 닫힌 메뉴 ESC 버튼 2프레임 확인 -> 메뉴 열기 단계부터 재개");
                return true;
            }
            previousClosed = closed;
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }

        Log?.Invoke("[어비스 진입 복구] 8초 내 메뉴/던전 선택 화면을 확인하지 못함 -> 전투용 660초 복구와 ESC 클릭 없이 안전 실패");
        return false;
    }
}
