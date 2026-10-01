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

        if (await FindAsync(frame, CollectButton, "모두 받기", ct) is not null)
            return true;

        // The button label is much larger and more stable than the tiny Space badge,
        // but keep the compact OCR path as a fallback for anti-aliased UI text.
        var compact = await _ocr.FindCompactLabelAsync(frame, CollectButton, "모두 받기", ct);
        return compact.Found;
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


    private async Task<bool> ConfirmFacilityTravelPopupAsync(CancellationToken ct)
    {
        // Destination text varies. The question is usually a full sentence ending in
        // "... 이동할까요?", so exact-label matching cannot be used here.
        // Authorize Space only when the dialog sentence contains the generic travel
        // question and both confirm/cancel controls are present on two fresh frames.
        for (int wait = 0; wait < 8; wait++)
        {
            ct.ThrowIfCancellationRequested();

            using var first = Capture(ct);
            bool question1 = await HasFacilityTravelQuestionAsync(first, ct);
            bool confirm1 = await HasDialogButtonAsync(first, "확인", ct);
            bool cancel1 = await HasDialogButtonAsync(first, "취소", ct);

            if (!question1 || !confirm1 || !cancel1)
            {
                await Task.Delay(250, ct);
                continue;
            }

            await Task.Delay(180, ct);
            using var second = Capture(ct);
            bool question2 = await HasFacilityTravelQuestionAsync(second, ct);
            bool confirm2 = await HasDialogButtonAsync(second, "확인", ct);
            bool cancel2 = await HasDialogButtonAsync(second, "취소", ct);

            if (!question2 || !confirm2 || !cancel2)
                continue;

            Log?.Invoke("[자동 가공] 설비 이동 확인 팝업 · 목적지명 무관 · Space로 확인");
            _input.TapScanCode(0x39);
            await Task.Delay(700, ct);
            return true;
        }

        return false;
    }

    private async Task<bool> HasFacilityTravelQuestionAsync(Bitmap frame, CancellationToken ct)
    {
        string wanted = AlteringText.Normalize("이동할까요");
        foreach (int scale in new[] { 2, 3 })
        {
            var lines = await _ocr.ReadLinesAsync(frame, FacilityTravelDialog, scale, ct);
            if (lines.Any(x => AlteringText.Normalize(x.ReadText ?? "").Contains(wanted, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private async Task<bool> HasDialogButtonAsync(Bitmap frame, string text, CancellationToken ct)
    {
        if (await FindAsync(frame, FacilityTravelDialog, text, ct) is not null)
            return true;

        var compact = await _ocr.FindCompactLabelAsync(frame, FacilityTravelDialog, text, ct);
        return compact.Found;
    }

    private async Task TravelToFacilityAsync(AlteringPlan plan, CancellationToken ct)
    {
        if (_cli is null)
            throw new InvalidOperationException("무료 설비 이동 상태 확인용 CLI가 연결되지 않았습니다.");

        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
                Fail(frame, "설비 이동 전 선택한 가공 시설 화면을 확인하지 못했습니다.");
            var move = await FindAsync(frame, FacilityMoveButton, "설비로 이동", ct);
            if (move is null)
                Fail(frame, "무료 설비로 이동 버튼을 확인하지 못했습니다.");
            if (pass == 0) { await Task.Delay(180, ct); continue; }
            Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · 설비로 이동 클릭");
            _input.ClickClientPoint(_hwnd, move.Value.Center);
        }

        bool popupConfirmed = await ConfirmFacilityTravelPopupAsync(ct);
        if (popupConfirmed)
            Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · 이동 확인 완료 · 실제 이동 대기");
        else
            Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · 이동 확인 팝업 없음 · 직접 이동 시작 여부 확인");

        bool sawDeparture = false;
        bool sawTravel = false;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct);

            var activity = GatheringQueries.ParseActivity(await _cli.GetActivityAsync(ct));
            if (!activity.IsSafeField)
                throw new InvalidOperationException("설비 이동 중 전투·대화 등 안전하지 않은 상태가 확인되어 정지합니다.");

            if (activity.IsAutoTraveling)
            {
                sawTravel = true;
                sawDeparture = true;
            }

            using var frame = Capture(ct);
            bool facilityVisible = await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null;

            // The facility screen is open before the click, so it cannot count as
            // arrival until we have first observed travel or the facility screen
            // disappearing. This prevents the old false-positive "3 seconds = arrived".
            if (!facilityVisible)
                sawDeparture = true;

            if (sawDeparture && !activity.IsAutoTraveling && facilityVisible)
            {
                Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · 설비 도착 확인 · 가공창 재등장");
                return;
            }

            if (attempt > 0 && attempt % 10 == 0)
                Log?.Invoke($"[자동 가공] 설비 이동 대기 · {attempt / 2}초 · 이동감지={sawTravel} · 화면이탈={sawDeparture} · 가공창={facilityVisible}");
        }

        throw new InvalidOperationException("설비로 이동 후 가공창 재등장을 제한 시간 안에 확인하지 못해 정지합니다.");
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

        // Confirm the stable large facility title plus the visible "모두 받기" button.
        // Do not gate collection on the tiny Space badge; it is only a keyboard hint
        // and proved unreliable in the live V0.1.88 failure screenshot.
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            if (!await HasCollectPromptAsync(frame, plan, ct))
                Fail(frame, "완료 작업 수령 화면(시설 제목 + 모두 받기)을 확인하지 못했습니다.");
            if (pass == 0) { await Task.Delay(180, ct); continue; }

            Log?.Invoke($"[자동 가공] 수령 화면 확인 · {plan.ScreenTitle} + 모두 받기 · 1차 입력");
            _input.TapScanCode(0x39);
        }
        await Task.Delay(550, ct);
    }

    public async Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
    {
        // First input may only start automatic travel. Do not navigate or press K while
        // the character is moving. Wait passively for the same facility title plus
        // "모두 받기" button to reappear, verify both on a fresh frame, then press Space once.
        for (int i = 1; i <= 45; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct);
            using var frame = Capture(ct);
            if (!await HasCollectPromptAsync(frame, plan, ct)) continue;

            await Task.Delay(180, ct);
            using var fresh = Capture(ct);
            if (!await HasCollectPromptAsync(fresh, plan, ct)) continue;

            Log?.Invoke($"[자동 가공] 가공대 도착 확인 · {plan.ScreenTitle} + 모두 받기 · 2차 입력");
            _input.TapScanCode(0x39);
            await Task.Delay(700, ct);
            return true;
        }

        Log?.Invoke("[자동 가공] 가공대 도착 후 시설 제목 + 모두 받기 화면을 제한 시간 안에 확인하지 못했습니다.");
        return false;
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
