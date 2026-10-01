using FishingAutomation;

namespace DungeonVisionBot;

internal sealed class AlteringScreen : IAlteringScreen
{
    private readonly GuardedInputController _input;
    private readonly WindowCapture _capture = new();
    private readonly OcrRecognizer _ocr = new();
    private readonly string _debugDir;
    private readonly MabinogiMobileCli? _cli;
    private nint _hwnd;
    private static readonly Rectangle Whole = new(0, 0, 800, 1000);
    private static readonly Rectangle Header = new(0, 15, 450, 110);
    private static readonly Rectangle Cards = new(20, 350, 760, 550);
    private static readonly Rectangle Popup = new(270, 585, 360, 45);
    private static readonly Rectangle CollectButton = new(0, 260, 170, 110);
    private static readonly Rectangle CollectVisualButton = new(10, 270, 110, 85);
    private static readonly Rectangle FacilityMoveButton = new(10, 180, 220, 120);
    private static readonly Rectangle FacilityTravelDialog = new(120, 700, 560, 290);
    private static readonly Rectangle RecipeActionButton = new(150, 820, 540, 170);
    internal string InputMode => _input.ModeName;
    internal event Action<string>? Log;

    internal AlteringScreen(nint hwnd, AppSettings settings, string debugDir, MabinogiMobileCli? cli = null)
    {
        _hwnd = hwnd; _debugDir = debugDir; _cli = cli;
        _input = new GuardedInputController(new InterceptionInput(settings.InterceptionMouseDevice, settings.InterceptionKeyboardDevice));
    }

    private Bitmap Capture(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _input.SetCancellation(ct);
        if (!WindowTools.IsRequiredGameWindow(_hwnd)) _hwnd = WindowTools.FindRequiredGameWindow();
        if (_hwnd == 0) throw new InvalidOperationException("마비노기 모바일 창이 없습니다.");
        NativeMethods.SetForegroundWindow(_hwnd);
        var frame = _capture.CaptureClient(_hwnd);
        try { _input.ObserveFrame(_hwnd, frame.Size); return frame; }
        catch { frame.Dispose(); throw; }
    }

    private async Task<DetectionResult?> FindAsync(Bitmap frame, Rectangle roi, string text, CancellationToken ct, bool facilityTitle = false)
    {
        var found = facilityTitle ? await _ocr.FindAlteringFacilityTitlesAsync(frame, text, ct) :
            await _ocr.FindAlteringLabelsAsync(frame, roi, text, ct);
        return found.Count == 1 ? found[0] : null;
    }

    private Task<DetectionResult?> FindFacilityHeaderAsync(Bitmap frame, string title, CancellationToken ct)
        => _ocr.FindAlteringFacilityHeaderAsync(frame, title, ct);

    private async Task<bool> HasCollectPromptAsync(Bitmap frame, AlteringPlan plan, CancellationToken ct)
    {
        if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
            return false;

        if (_cli is null)
            return false;

        var worksResponse = await _cli.GetAlteringWorksAsync(ct);
        if (!worksResponse.Success)
        {
            if (CliAutomationGuards.IsTransientLoadingRejection(worksResponse))
                return false;
            _ = AlteringQueries.ParseWorks(worksResponse); // preserve existing hard failure diagnostics
        }

        bool hasCompleted = AlteringQueries.ParseWorks(worksResponse)
            .Any(x => x.FacilityName == plan.FacilityName && x.IsCompleted);
        if (!hasCompleted)
            return false;

        return HasCollectButtonVisual(frame);
    }

    private static bool HasCollectButtonVisual(Bitmap frame)
    {
        // Live completion screen: the active receive control is a broad cyan/blue
        // capsule at the far-left. Use a deliberately tolerant colour gate because
        // capture brightness changes between field/bench states. The ROI ends before
        // the first 100% work circle, so completed-slot rings cannot satisfy this gate.
        var roi = Rectangle.Intersect(CollectVisualButton, new Rectangle(Point.Empty, frame.Size));
        if (roi.Width < 70 || roi.Height < 45)
            return false;

        int sampled = 0;
        int blue = 0;
        for (int y = roi.Top; y < roi.Bottom; y += 2)
        for (int x = roi.Left; x < roi.Right; x += 2)
        {
            Color p = frame.GetPixel(x, y);
            sampled++;
            if (p.B >= 95 && p.G >= 75 && p.R <= 150 &&
                p.B >= p.R + 20 && p.G >= p.R + 15)
                blue++;
        }

        // The real button occupies a large part of this ROI. A 2% floor is still
        // enough to reject the dark inactive area while tolerating dim captures.
        return sampled > 0 && blue * 100 >= sampled * 2;
    }

    private async Task<bool> WaitForCollectPromptAsync(
        AlteringPlan plan, int attempts, int delayMs, CancellationToken ct)
    {
        int stableFrames = 0;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);
            if (await HasCollectPromptAsync(frame, plan, ct))
            {
                stableFrames++;
                if (stableFrames >= 2)
                    return true;
            }
            else
            {
                stableFrames = 0;
            }

            if (attempt % 8 == 0)
                Log?.Invoke($"[자동 가공] 완료품 수령 버튼 대기 · {attempt}/{attempts}");

            await Task.Delay(delayMs, ct);
        }

        return false;
    }

    private async Task<DetectionResult?> FindRecipeAsync(Bitmap frame, AlteringPlan plan, CancellationToken ct)
    {
        // CLI recipe names can include an ingredient qualifier such as
        // "철괴(철 광석)", while the in-game detail sheet shows only "철괴".
        // The specific recipe card is selected and re-confirmed immediately before
        // opening this sheet, so the base output title is a valid detail-screen check.
        var exact = await FindAsync(frame, Popup, plan.DisplayName, ct);
        if (exact is not null) return exact;

        if (!plan.OutputName.Equals(plan.DisplayName, StringComparison.Ordinal))
        {
            var output = await FindAsync(frame, Popup, plan.OutputName, ct);
            if (output is not null)
            {
                var materials = await FindAsync(frame, new(100, 690, 580, 200), "필요한 재료", ct);
                if (materials is not null) return output;
            }
        }

        if (plan.VerifiedOcrAlias is not null)
        {
            var alias = await FindAsync(frame, Popup, plan.VerifiedOcrAlias, ct);
            if (alias is not null) return alias;
        }

        return null;
    }

    private async Task<bool> IsRecipeDetailAsync(Bitmap frame, AlteringPlan plan, CancellationToken ct)
    {
        bool titleMatched = await FindRecipeAsync(frame, plan, ct) is not null;
        if (titleMatched) return true;

        bool materialsVisible = await FindAsync(frame, new(100, 690, 580, 200), "필요한 재료", ct) is not null;
        bool freeVisible = await FindAsync(frame, RecipeActionButton, "가공하기", ct) is not null;
        bool paidVisible = await FindAsync(frame, RecipeActionButton, "가공하러 가기", ct) is not null;

        bool confirmed = AlteringDetailPolicy.IsConfirmed(titleMatched, materialsVisible, freeVisible, paidVisible);
        if (confirmed)
            Log?.Invoke($"[자동 가공] 상세 제목 OCR 보조 판정 · 필요한 재료 + {(freeVisible ? "가공하기" : "가공하러 가기")} 확인");

        return confirmed;
    }
    private async Task<bool> ClickLabelAsync(string text, Rectangle roi, string? header, CancellationToken ct, bool facilityTitle = false)
    {
        // Two observations; the second one alone supplies the input coordinates.
        using (var first = Capture(ct))
        {
            if (header is not null && await FindFacilityHeaderAsync(first, header, ct) is null) return false;
            if (await FindAsync(first, roi, text, ct, facilityTitle) is null) return false;
        }
        await Task.Delay(180, ct);
        using var second = Capture(ct);
        if (header is not null && await FindFacilityHeaderAsync(second, header, ct) is null) return false;
        var found = await FindAsync(second, roi, text, ct, facilityTitle);
        if (found is null) return false;
        _input.ClickClientPoint(_hwnd, found.Value.Center);
        await Task.Delay(550, ct);
        return true;
    }

    private async Task EnterFacilityAsync(AlteringPlan plan, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using (var frame = Capture(ct))
            {
                if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null)
                {
                    Log?.Invoke($"[자동 가공] 현재 화면={plan.ScreenTitle} · 시설 진입 확인");
                    return;
                }

                if (await FindAsync(frame, new(100, 690, 580, 200), "필요한 재료", ct) is not null)
                {
                    Log?.Invoke("[자동 가공] 현재 화면=품목 상세 · 닫고 시설 화면을 다시 확인합니다.");
                    _input.TapScanCode(0x01);
                    await Task.Delay(700, ct);
                    continue;
                }

                string? otherFacility = null;
                foreach (string name in AlteringPlan.Facilities.Select(x => x.Replace(" 시설", "")))
                {
                    if (name == plan.ScreenTitle) continue;
                    if (await FindFacilityHeaderAsync(frame, name, ct) is not null)
                    {
                        otherFacility = name;
                        break;
                    }
                }

                if (otherFacility is not null)
                {
                    Log?.Invoke($"[자동 가공] 현재 화면={otherFacility} · 가공 허브로 돌아갑니다.");
                    _input.ClickClientPoint(_hwnd, new(33, 55));
                    await Task.Delay(900, ct);
                    continue;
                }

                if (await FindFacilityHeaderAsync(frame, "가공", ct) is not null)
                {
                    Log?.Invoke($"[자동 가공] 현재 화면=가공 허브 · 시설 진입 시도 {attempt}/{maxAttempts}");
                    if (!await ClickLabelAsync(plan.ScreenTitle, AlteringFacilityLayout.TitleArea(plan.ScreenTitle), "가공", ct, facilityTitle: true))
                    {
                        Log?.Invoke($"[자동 가공] 시설 제목 확인 실패 {attempt}/{maxAttempts} · 추가 입력 없이 재판정합니다.");
                        await Task.Delay(1200, ct);
                        continue;
                    }

                    await Task.Delay(1200, ct);
                    using var verify = Capture(ct);
                    if (await FindFacilityHeaderAsync(verify, plan.ScreenTitle, ct) is not null)
                    {
                        Log?.Invoke($"[자동 가공] 시설 진입 성공 {attempt}/{maxAttempts} · {plan.ScreenTitle}");
                        return;
                    }

                    Log?.Invoke($"[자동 가공] 시설 전환 확인 대기 {attempt}/{maxAttempts} · 현재 화면을 다시 판정합니다.");
                    await Task.Delay(900, ct);
                    continue;
                }

                // Unknown after collection can be a transient reward/result screen or the
                // ordinary field. Do one bounded K re-entry only; never spam keys blindly.
                Log?.Invoke($"[자동 가공] 현재 화면=일반/전환 중 · 가공 메뉴 재진입 시도 {attempt}/{maxAttempts}");
                _input.TapScanCode(0x25);
            }

            await Task.Delay(1000, ct);
            using (var menu = Capture(ct))
            {
                if (await FindFacilityHeaderAsync(menu, plan.ScreenTitle, ct) is not null)
                {
                    Log?.Invoke($"[자동 가공] 시설 진입 성공 {attempt}/{maxAttempts} · {plan.ScreenTitle}");
                    return;
                }

                if (await FindFacilityHeaderAsync(menu, "가공", ct) is null)
                {
                    if (!await ClickLabelAsync("가공", new(180, 880, 420, 120), null, ct))
                    {
                        Log?.Invoke($"[자동 가공] 가공 메뉴 확인 실패 {attempt}/{maxAttempts} · 추가 입력 없이 재판정합니다.");
                        await Task.Delay(1200, ct);
                        continue;
                    }
                    await Task.Delay(900, ct);
                }
            }
        }

        using var failed = Capture(ct);
        Fail(failed, "선택한 가공 시설 화면을 3회 확인하지 못했습니다.");
    }

    private async Task SelectRecipeAsync(AlteringPlan plan, CancellationToken ct)
    {
        // First scroll to the top. Each drag is guarded by a fresh facility observation.
        for (int i = 0; i < 5; i++)
        {
            using var frame = Capture(ct);
            if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null) Fail(frame, "가공 목록이 사라졌습니다.");
            _input.DragClientPoint(_hwnd, new(735, 415), new(735, 895), 350);
            await Task.Delay(180, ct);
        }
        for (int page = 0; page < 16; page++)
        {
            using var frame = Capture(ct);
            if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null) Fail(frame, "가공 목록이 사라졌습니다.");
            var labels = await _ocr.FindAlteringLabelsAsync(frame, Cards, plan.DisplayName, ct, cardCandidate: true);
            if (labels.Count == plan.RecipeCount && labels.Count >= plan.RecipeOrdinal)
            {
                var candidate = labels[plan.RecipeOrdinal - 1];
                await Task.Delay(180, ct);
                using var fresh = Capture(ct);
                var confirmed = await _ocr.FindAlteringLabelsAsync(fresh, Cards, plan.DisplayName, ct, cardCandidate: true);
                if (confirmed.Count != plan.RecipeCount || confirmed.Count < plan.RecipeOrdinal || !confirmed[plan.RecipeOrdinal - 1].Bounds.IntersectsWith(candidate.Bounds))
                    continue;
                _input.ClickClientPoint(_hwnd, confirmed[plan.RecipeOrdinal - 1].Center);
                await Task.Delay(400, ct);
                using var popup = Capture(ct);
                if (!await IsRecipeDetailAsync(popup, plan, ct))
                    Fail(popup, "선택한 품목의 상세 화면을 확인하지 못했습니다.");
                return;
            }
            _input.DragClientPoint(_hwnd, new(735, 895), new(735, 415), 350);
            await Task.Delay(250, ct);
        }
        using var missing = Capture(ct);
        Fail(missing, "시설 목록에서 선택 품목/제법을 정확히 확인하지 못했습니다. 가공 시설과 품목을 확인하세요.");
    }

    public async Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
    {
        plan.Validate();
        if (plan.AllowPaidButton)
            throw new InvalidOperationException("정령의 날개를 사용하는 가공 경로는 실행하지 않습니다.");

        // Correct free processing flow:
        // 1) enter the facility category (e.g. 금속 가공)
        // 2) DO NOT open the recipe yet
        // 3) click "설비로 이동"
        // 4) wait until travel is complete and the facility screen is back
        // 5) only then select the recipe and press on-site "가공하기"
        await EnterFacilityAsync(plan, ct);
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 진입 · 품목 선택 전 설비로 이동합니다.");
        await TravelToFacilityAsync(plan, ct);

        // The facility window is expected to reopen on arrival. EnterFacilityAsync is
        // intentionally idempotent here: it accepts the already-open facility screen
        // and only navigates if the screen did not reappear yet.
        await EnterFacilityAsync(plan, ct);
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 설비 도착 · 이제 {plan.DisplayName} 선택");
        await SelectRecipeAsync(plan, ct);

        // Two fresh observations are required immediately before the only registration
        // click. At the physical facility the free "가공하기" label must be present,
        // and the remote "가공하러 가기" label must be absent.
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            if (!await IsRecipeDetailAsync(frame, plan, ct))
                Fail(frame, "설비 도착 후 품목 상세 화면을 확인하지 못했습니다.");

            var free = await FindAsync(frame, RecipeActionButton, "가공하기", ct);
            var paid = await FindAsync(frame, RecipeActionButton, "가공하러 가기", ct);
            if (free is null || paid is not null)
                Fail(frame, "설비 도착 후 무료 가공하기 버튼을 안전하게 확인하지 못했습니다.");

            if (pass == 0)
            {
                await Task.Delay(200, ct);
                continue;
            }

            Log?.Invoke("[자동 가공] 설비 도착 후 무료 가공하기 확인 · 정령의 날개 버튼 입력 없음");
            _input.ClickClientPoint(_hwnd, free.Value.Center);
        }
    }


    private async Task<bool> ConfirmFacilityTravelAsync(CancellationToken ct)
    {
        // Do not OCR this modal. Its stable visual signal is the large green
        // confirmation button in the lower-right area of the 800x1000 client.
        // Press Space only after that button is actually visible.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct);

            using var frame = Capture(ct);
            if (!HasBottomConfirmationModal(frame))
                continue;

            // One fresh frame prevents acting on a single capture artifact.
            await Task.Delay(120, ct);
            using var fresh = Capture(ct);
            if (!HasBottomConfirmationModal(fresh))
                continue;

            Log?.Invoke("[자동 가공] 하단 확인 팝업 화면 감지 · Space 입력");
            _input.TapScanCode(0x39);
            await Task.Delay(900, ct);
            return true;
        }

        Log?.Invoke("[자동 가공] 하단 확인 팝업 없음 · Space 입력 생략");
        return false;
    }

    private static bool HasBottomConfirmationModal(Bitmap frame)
    {
        // Screenshot evidence: the modal's confirm control is a broad saturated-green
        // button around the lower-right quarter. Sample sparsely so detection is fast.
        var roi = Rectangle.Intersect(new Rectangle(370, 840, 270, 140),
            new Rectangle(Point.Empty, frame.Size));
        if (roi.Width < 120 || roi.Height < 60)
            return false;

        int sampled = 0;
        int green = 0;
        for (int y = roi.Top; y < roi.Bottom; y += 4)
        for (int x = roi.Left; x < roi.Right; x += 4)
        {
            Color p = frame.GetPixel(x, y);
            sampled++;
            if (p.G >= 90 && p.G >= p.R + 45 && p.G >= p.B + 20)
                green++;
        }

        // The real confirmation button covers roughly a quarter of this ROI.
        // Eight percent leaves ample room for dimming/anti-aliasing while ordinary
        // facility screens stay far below the threshold.
        return sampled > 0 && green * 100 >= sampled * 8;
    }

    private async Task TravelToFacilityAsync(AlteringPlan plan, CancellationToken ct)
    {
        if (_cli is null)
            throw new InvalidOperationException("무료 설비 이동 상태 확인용 CLI가 연결되지 않았습니다.");

        // The same facility window has two real states:
        //   remote: "설비로 이동" is visible
        //   on-site: the facility list is visible but "설비로 이동" is gone
        // Require two stable frames before deciding which state we are in.
        DetectionResult? moveToClick = null;
        int moveFrames = 0;
        int onsiteFrames = 0;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);

            if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
                Fail(frame, "설비 이동 전 선택한 가공 시설 화면을 확인하지 못했습니다.");

            bool popupVisible = HasBottomConfirmationModal(frame);
            var move = popupVisible
                ? null
                : await FindAsync(frame, FacilityMoveButton, "설비로 이동", ct);

            if (!popupVisible && move is null)
            {
                onsiteFrames++;
                moveFrames = 0;
                if (onsiteFrames >= 2)
                {
                    Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · 설비로 이동 버튼 없음 · 이미 현장 가공창");
                    return;
                }
            }
            else if (!popupVisible && move is not null)
            {
                moveFrames++;
                onsiteFrames = 0;
                moveToClick = move;
                if (moveFrames >= 2)
                    break;
            }
            else
            {
                moveFrames = 0;
                onsiteFrames = 0;
            }

            await Task.Delay(180, ct);
        }

        if (moveToClick is null || moveFrames < 2)
        {
            using var failed = Capture(ct);
            Fail(failed, "설비 이동 상태를 안정적으로 확인하지 못했습니다.");
        }

        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · 설비로 이동 클릭");
        _input.ClickClientPoint(_hwnd, moveToClick.Value.Center);

        bool confirmationPressed = await ConfirmFacilityTravelAsync(ct);
        Log?.Invoke(confirmationPressed
            ? $"[자동 가공] {plan.ScreenTitle} · 이동 확인 Space 입력 완료 · 실제 이동 대기"
            : $"[자동 가공] {plan.ScreenTitle} · 확인 팝업 없음 · 직접 이동 여부 확인");

        bool sawDeparture = false;
        bool sawTravel = false;
        int loadingCliRejects = 0;
        int onsiteStableFrames = 0;

        for (int attempt = 0; attempt < 120; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct);

            GatheringActivity? activity = null;
            var activityResponse = await _cli.GetActivityAsync(ct);
            if (activityResponse.Success)
            {
                activity = GatheringQueries.ParseActivity(activityResponse);
                if (!activity.IsSafeField)
                    throw new InvalidOperationException("설비 이동 중 전투·대화 등 안전하지 않은 상태가 확인되어 정지합니다.");

                if (activity.IsAutoTraveling)
                {
                    sawTravel = true;
                    sawDeparture = true;
                }
            }
            else if (CliAutomationGuards.IsTransientLoadingRejection(activityResponse))
            {
                loadingCliRejects++;
                if (loadingCliRejects == 1 || loadingCliRejects % 5 == 0)
                    Log?.Invoke($"[자동 가공] 지역 이동 로딩 중 CLI 일시 거부 · 재시도 {loadingCliRejects}회");
            }
            else
            {
                _ = GatheringQueries.ParseActivity(activityResponse);
            }

            using var frame = Capture(ct);
            bool popupVisible = HasBottomConfirmationModal(frame);
            if (popupVisible)
            {
                Log?.Invoke("[자동 가공] 늦게 표시된 하단 확인 팝업 감지 · Space 입력");
                _input.TapScanCode(0x39);
                await Task.Delay(900, ct);
                onsiteStableFrames = 0;
                continue;
            }

            bool facilityVisible = await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null;
            bool moveVisible = false;
            if (facilityVisible)
                moveVisible = await FindAsync(frame, FacilityMoveButton, "설비로 이동", ct) is not null;

            if (!facilityVisible)
                sawDeparture = true;

            // On-site state is authoritative: facility window + no "설비로 이동".
            // This also covers the game's direct transition where there is no loading
            // screen and the remote button simply disappears.
            if (facilityVisible && !moveVisible && activity?.IsAutoTraveling != true)
            {
                onsiteStableFrames++;
                if (onsiteStableFrames >= 2)
                {
                    Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · 설비 도착 확인 · 설비로 이동 버튼 없음");
                    return;
                }
            }
            else
            {
                onsiteStableFrames = 0;
            }

            if (attempt > 0 && attempt % 10 == 0)
                Log?.Invoke($"[자동 가공] 설비 이동 대기 · {attempt / 2}초 · 이동감지={sawTravel} · 화면이탈={sawDeparture} · 가공창={facilityVisible} · 이동버튼={moveVisible} · CLI로딩거부={loadingCliRejects}");
        }

        throw new InvalidOperationException("설비로 이동 후 현장 가공창을 제한 시간 안에 확인하지 못해 정지합니다.");
    }

    // Free navigation only, for opening an ingredient's obtain-method route.
    // This entry point cannot reach QueueAsync or any paid button.
    internal async Task OpenRecipeAsync(AlteringPlan plan, CancellationToken ct)
    {
        if(plan.AllowPaidButton) throw new InvalidOperationException("채집 경로는 가공 비용 버튼을 사용할 수 없습니다.");
        await EnterFacilityAsync(plan, ct);
        await SelectRecipeAsync(plan, ct);
    }

    public async Task CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        await EnterFacilityAsync(plan, ct);

        // The facility can take a moment to paint the completed-work controls after
        // entering. Wait up to five seconds and require two consecutive confirmations:
        // facility header + completed CLI work + cyan receive button.
        if (!await WaitForCollectPromptAsync(plan, attempts: 20, delayMs: 250, ct))
        {
            using var failed = Capture(ct);
            Fail(failed, "완료 작업은 확인됐지만 왼쪽 파란 수령 버튼을 제한 시간 안에 확인하지 못했습니다.");
        }

        Log?.Invoke($"[자동 가공] 수령 화면 확인 · {plan.ScreenTitle} + CLI 완료 작업 + 파란 수령 버튼 · 1차 Space");
        _input.TapScanCode(0x39);
        await Task.Delay(700, ct);
    }

    public async Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
    {
        // First Space can start travel instead of collecting. Wait passively for the
        // same visual/CLI receipt evidence to become stable at the processing bench.
        if (!await WaitForCollectPromptAsync(plan, attempts: 90, delayMs: 500, ct))
        {
            Log?.Invoke("[자동 가공] 가공대 도착 후 CLI 완료 작업 + 파란 수령 버튼을 제한 시간 안에 확인하지 못했습니다.");
            return false;
        }

        Log?.Invoke($"[자동 가공] 가공대 도착 확인 · {plan.ScreenTitle} + CLI 완료 작업 + 파란 수령 버튼 · 2차 Space");
        _input.TapScanCode(0x39);
        await Task.Delay(700, ct);
        return true;
    }
    private void Fail(Bitmap frame, string message)
    {
        Directory.CreateDirectory(_debugDir);
        string path = Path.Combine(_debugDir, "altering-last-failure.png");
        frame.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        throw new InvalidOperationException(message + " 추가 입력 없이 정지합니다. 진단: " + path);
    }
    public void Dispose() => _input.Dispose();
}
