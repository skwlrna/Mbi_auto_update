using FishingAutomation;

namespace DungeonVisionBot;

internal sealed class AlteringScreen : IAlteringScreen, IAlteringCoordinatorQueueScreen, IAlteringCoordinatorReceiptScreen, IAlteringReceiptBoundaryScreen, IAlteringRecoveryScreen, IAlteringCoordinatorStallRecoveryScreen, IAlteringFieldExitScreen
{
    private readonly ProductionUiRuntime _ui;
    private readonly ProductionStageMachine _stage = new("가공");
    private readonly string _debugDir;
    private readonly MabinogiMobileCli? _cli;
    // Screen-local Automatic single-altering cache; never manager authority.
    private string? _confirmedOnsiteFacility;
    private string? _cachedRecipeKey;
    private Point _cachedRecipeCenter;
    private bool _hasCachedRecipeCenter;
    // Per-F9 verification cache. Only successfully registered fixed recipes
    // qualify; manager-directed same-facility repeats never call OCR again.
    private readonly HashSet<string> _verifiedFacilityTitles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _verifiedFixedRecipes = new(StringComparer.Ordinal);
    private string? _repeatOcrFreeFacilityTitle;
    private string? _postTravelProvenFacilityTitle;
    private bool _repeatOcrFreeRecipe;
    private static readonly Rectangle Whole = new(0, 0, 800, 1000);
    private static readonly Rectangle Header = new(0, 15, 450, 110);
    private static readonly Rectangle Cards = new(20, 350, 760, 550);
    private static readonly Rectangle Popup = new(270, 585, 360, 45);
    private static readonly Rectangle CollectButton = new(0, 260, 170, 110);
    private static readonly Rectangle CollectVisualButton = new(10, 270, 110, 85);
    private static readonly Rectangle FacilityTravelDialog = new(80, 260, 640, 700);
    private static readonly Rectangle FacilityTravelConfirmVisual = new(120, 340, 560, 620);
    private static readonly Rectangle RecipeActionButton = new(150, 820, 540, 170);
    private static readonly Rectangle FreeProcessVisualButton = new(180, 895, 470, 95);
    internal string InputMode => _ui.InputMode;
    internal event Action<string>? Log;

    internal AlteringScreen(nint hwnd, AppSettings settings, string debugDir, MabinogiMobileCli? cli = null)
    {
        _debugDir = debugDir; _cli = cli;
        _ui = new ProductionUiRuntime(hwnd, settings, debugDir, "altering");
        _stage.Changed += (stage, detail) =>
            Log?.Invoke($"[자동 가공][상태] {stage} · {detail}");
    }

    private Bitmap Capture(CancellationToken ct) => _ui.Capture(ct);

    private async Task<DetectionResult?> FindAsync(Bitmap frame, Rectangle roi, string text, CancellationToken ct, bool facilityTitle = false)
    {
        var found = facilityTitle ? await _ui.Ocr.FindAlteringFacilityTitlesAsync(frame, text, ct) :
            await _ui.Ocr.FindAlteringLabelsAsync(frame, roi, text, ct);
        return found.Count == 1 ? found[0] : null;
    }

    // A previously confirmed facility uses its FIXED header position, never OCR.
    // This is a visual location check, not a guess based on a stale OCR result.
    private Task<DetectionResult?> FindFacilityHeaderAsync(Bitmap frame, string title, CancellationToken ct)
    {
        if (string.Equals(_repeatOcrFreeFacilityTitle, title, StringComparison.Ordinal) ||
            string.Equals(_postTravelProvenFacilityTitle, title, StringComparison.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            DetectionResult? fixedHit = HasFixedFacilityHeaderVisual(frame)
                ? new DetectionResult(true, new Rectangle(14, 42, 155, 45), 1.0, title)
                : null;
            return Task.FromResult(fixedHit);
        }
        return _ui.Ocr.FindAlteringFacilityHeaderAsync(frame, title, ct);
    }

    // The actual 800x1000 facility capture has the large fixed title at
    // x=20,y=50 and a separate fixed 'facility Lv.' subtitle at x=20,y=155.
    // Neither is a dynamic OCR coordinate. Require BOTH independent anchors:
    // a single unrelated title or bright currency cannot prove this screen.
    private static bool HasFixedFacilityHeaderVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return false;
        static int WhiteGlyphs(Bitmap bitmap, Rectangle roi)
        {
            int count = 0;
            for (int y = roi.Top; y < roi.Bottom; y += 2)
            for (int x = roi.Left; x < roi.Right; x += 2)
            {
                Color p = bitmap.GetPixel(x, y);
                if (p.R >= 150 && p.G >= 150 && p.B >= 150 &&
                    Math.Max(p.R, Math.Max(p.G, p.B)) -
                    Math.Min(p.R, Math.Min(p.G, p.B)) <= 45)
                    count++;
            }
            return count;
        }
        return WhiteGlyphs(frame, new Rectangle(14, 42, 155, 46)) >= 18 &&
               WhiteGlyphs(frame, new Rectangle(15, 144, 230, 48)) >= 20;
    }

    private async Task<bool> HasCollectPromptAsync(
        Bitmap frame,
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
    {
        if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
            return false;

        if (directive != AlteringFacilityEntryDirective.Automatic)
        {
            // A coordinator-directed receipt must be at the expected facility
            // with no dialog and a positively observed non-travelling CLI state.
            // Button text/colour alone is not a reliable offsite signal.
            if (HasBottomConfirmationModal(frame) ||
                await TryAutoTravelingAsync(ct) != false)
                return false;
        }

        bool visualMoveButton = HasFacilityMoveButtonVisual(frame);
        bool exactMoveLabelVisible = false;
        if (visualMoveButton)
        {
            // The manager-confirmed repeated facility never re-reads a label.
            // The fixed visual move control remains a non-OCR safety veto.
            exactMoveLabelVisible = string.Equals(
                _repeatOcrFreeFacilityTitle, plan.ScreenTitle, StringComparison.Ordinal) ||
                await FindAsync(
                    frame, AlteringFacilityLayout.MoveButtonVisualArea,
                    "설비로 이동", ct) is not null;
        }

        bool trustedOnsiteFacility =
            AlteringScreenOnsiteCachePolicy.MayTrustForReceipt(
                directive, _confirmedOnsiteFacility, plan.FacilityName);
        if (AlteringReceiptPolicy.ShouldBlockReceiptForMoveButton(
                directive,
                trustedOnsiteFacility,
                visualMoveButton,
                exactMoveLabelVisible))
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

        var currentWorks = AlteringQueries.ParseWorks(worksResponse);
        // F02: a blue "모두 받기" drains the entire facility, not only the
        // selected item. The coordinator's earlier all-completed snapshot may
        // have gone stale while travelling or running OCR. Every managed
        // prompt observation must independently prove that ALL facility jobs
        // are completed; a mixed completed/running queue authorizes zero Space.
        bool eligible = directive == AlteringFacilityEntryDirective.Automatic
            ? currentWorks.Any(x => x.FacilityName == plan.FacilityName && x.IsCompleted)
            : AlteringReceiptPolicy.CanCollectManagedFacility(
                currentWorks, plan.FacilityName);
        if (!eligible)
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

    private static bool HasOnsiteCloseButtonVisual(Bitmap frame)
    {
        var roi = Rectangle.Intersect(
            AlteringFacilityLayout.OnsiteCloseVisualArea,
            new Rectangle(Point.Empty, frame.Size));
        if (roi.Width < 30 || roi.Height < 30)
            return false;

        static bool NeutralWhite(Color p)
        {
            int max = Math.Max(p.R, Math.Max(p.G, p.B));
            int min = Math.Min(p.R, Math.Min(p.G, p.B));
            return p.R >= 150 && p.G >= 150 && p.B >= 150 &&
                max - min <= 45;
        }

        int bright = 0;
        int minX = roi.Right, minY = roi.Bottom, maxX = roi.Left, maxY = roi.Top;

        for (int y = roi.Top; y < roi.Bottom; y++)
        for (int x = roi.Left; x < roi.Right; x++)
        {
            if (!NeutralWhite(frame.GetPixel(x, y)))
                continue;

            bright++;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (bright < 28)
            return false;

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;

        // V3.1.20 showed that the remote currency digits can enter this ROI.
        // A real close X is compact, roughly square, and has white pixels on both
        // diagonals in all four quadrants; a trailing price digit must not qualify.
        if (width < 16 || height < 16 || width > 34 || height > 34 ||
            Math.Abs(width - height) > 10)
            return false;

        double centerX = (minX + maxX) / 2.0;
        double centerY = (minY + maxY) / 2.0;
        int diagonal = 0;
        int centerBright = 0;
        int q1 = 0, q2 = 0, q3 = 0, q4 = 0;

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            if (!NeutralWhite(frame.GetPixel(x, y)))
                continue;

            double dx = x - centerX;
            double dy = y - centerY;
            if (Math.Abs(dx) <= 4 && Math.Abs(dy) <= 4)
                centerBright++;
            if (Math.Abs(Math.Abs(dx) - Math.Abs(dy)) > 3.5)
                continue;

            diagonal++;
            if (dx >= 0 && dy >= 0) q1++;
            else if (dx < 0 && dy >= 0) q2++;
            else if (dx < 0 && dy < 0) q3++;
            else q4++;
        }

        return centerBright >= 4 &&
            diagonal >= 18 &&
            diagonal * 100 >= bright * 35 &&
            q1 >= 3 && q2 >= 3 && q3 >= 3 && q4 >= 3;
    }

    private static bool HasFacilityMoveButtonVisual(Bitmap frame)
    {
        // Prove the remote "설비로 이동" pill with TWO left-side signals:
        // (1) the broad teal shape and (2) teal fill around the fixed click anchor.
        // The top-right X is advisory only because the 17:21 live failure showed
        // currency digits can mimic it and incorrectly erase a genuine move button.
        var bounds = new Rectangle(Point.Empty, frame.Size);
        var roi = Rectangle.Intersect(
            AlteringFacilityLayout.MoveButtonVisualArea,
            bounds);
        var anchor = Rectangle.Intersect(
            AlteringFacilityLayout.MoveButtonAnchorArea,
            bounds);
        if (roi.Width < 120 || roi.Height < 45 ||
            anchor.Width < 80 || anchor.Height < 24)
            return false;

        static bool IsMoveTeal(Color p)
            => p.B >= 35 && p.G >= 30 && p.R <= 65 &&
               p.B >= p.R + 18 && p.G >= p.R + 10;

        int sampled = 0;
        int teal = 0;
        int minX = roi.Right, minY = roi.Bottom, maxX = roi.Left, maxY = roi.Top;

        for (int y = roi.Top; y < roi.Bottom; y += 2)
        for (int x = roi.Left; x < roi.Right; x += 2)
        {
            Color p = frame.GetPixel(x, y);
            sampled++;
            if (!IsMoveTeal(p))
                continue;

            teal++;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (sampled == 0 || teal * 100 < sampled * 5)
            return false;

        int width = maxX - minX;
        int height = maxY - minY;
        bool moveShape = width >= 90 && height >= 18;
        if (!moveShape)
            return false;

        int anchorSampled = 0;
        int anchorTeal = 0;
        for (int y = anchor.Top; y < anchor.Bottom; y += 2)
        for (int x = anchor.Left; x < anchor.Right; x += 2)
        {
            anchorSampled++;
            if (IsMoveTeal(frame.GetPixel(x, y)))
                anchorTeal++;
        }

        // The real fixed button fills a large portion of the anchor area.
        // Sparse blue fragments from other on-site controls cannot satisfy this.
        bool moveAnchor = anchorSampled > 0 &&
            anchorTeal * 100 >= anchorSampled * 18;

        bool onsiteCloseVisible = HasOnsiteCloseButtonVisual(frame);
        return AlteringFacilityLayout.ShouldAcceptMoveButton(
            onsiteCloseVisible,
            moveShapeVisible: moveShape,
            moveAnchorVisible: moveAnchor);
    }

    private async Task<bool> WaitForCollectPromptAsync(
        AlteringPlan plan,
        int attempts,
        int delayMs,
        CancellationToken ct,
        AlteringFacilityEntryDirective directive = AlteringFacilityEntryDirective.Automatic)
    {
        int stableFrames = 0;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);
            if (await HasCollectPromptAsync(frame, plan, directive, ct))
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
        if (_repeatOcrFreeRecipe)
            return TryFindFreeProcessButtonVisual(frame, out _)
                ? new DetectionResult(true, Popup, 1.0, plan.DisplayName)
                : null;
        // CLI recipe names can include an ingredient qualifier such as
        // "철괴(철 광석)", while the in-game detail sheet shows only "철괴".
        // The specific recipe card is selected and re-confirmed immediately before
        // opening this sheet, so the base output title is a valid detail-screen check.
        var exact = await FindAsync(frame, Popup, plan.DisplayName, ct);
        if (exact is not null) return exact;

        // F07: a qualified recipe title is *not* interchangeable with its
        // output item title when multiple CLI recipes create that output.
        // "철괴(광석)" and "철괴(철 광석)" would otherwise both pass on "철괴".
        if (plan.MayUseBaseOutputTitle &&
            !plan.OutputName.Equals(plan.DisplayName, StringComparison.Ordinal))
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

    private async Task<bool> IsRecipeDetailStructureAsync(Bitmap frame, CancellationToken ct)
    {
        // Once this exact fixed-position recipe has been successfully registered,
        // repeat observations use only the existing fixed free-button geometry.
        // No '필요한 재료', action label or recipe OCR calls are made.
        if (_repeatOcrFreeRecipe)
        {
            ct.ThrowIfCancellationRequested();
            return TryFindFreeProcessButtonVisual(frame, out _);
        }
        // After a recipe card has already been selected, do not OCR the same recipe
        // name again. Only prove that the expected detail-sheet structure opened.
        bool materialsVisible = await FindAsync(frame, new(100, 690, 580, 200), "필요한 재료", ct) is not null;
        bool freeVisible = await FindAsync(frame, RecipeActionButton, "가공하기", ct) is not null;
        bool paidVisible = await FindAsync(frame, RecipeActionButton, "가공하러 가기", ct) is not null;
        bool visualAction = TryFindFreeProcessButtonVisual(frame, out _);

        bool confirmed = materialsVisible && (freeVisible || paidVisible || visualAction);
        if (confirmed)
        {
            string action = freeVisible ? "가공하기 OCR" :
                paidVisible ? "가공하러 가기 OCR" :
                "하단 실행 버튼 화면";
            Log?.Invoke($"[자동 가공] 선택 후 품목명 OCR 재확인 생략 · 필요한 재료 + {action} 확인");
        }

        return confirmed;
    }

    private static bool TryFindFreeProcessButtonVisual(Bitmap frame, out Point center)
    {
        center = Point.Empty;
        var roi = Rectangle.Intersect(FreeProcessVisualButton,
            new Rectangle(Point.Empty, frame.Size));
        if (roi.Width < 300 || roi.Height < 50)
            return false;

        int sampled = 0;
        int actionPixels = 0;
        int minX = roi.Right, minY = roi.Bottom, maxX = roi.Left, maxY = roi.Top;

        for (int y = roi.Top; y < roi.Bottom; y += 2)
        for (int x = roi.Left; x < roi.Right; x += 2)
        {
            Color p = frame.GetPixel(x, y);
            sampled++;

            // The live on-site action is a wide cyan-to-green capsule. Ignore all
            // text, icons and numeric values inside it; only the stable button body
            // participates in this detector.
            bool action = p.G >= 105 && p.B >= 70 && p.R <= 145 &&
                p.G >= p.R + 25 && p.B >= p.R + 5;
            if (!action)
                continue;

            actionPixels++;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (sampled == 0 || actionPixels * 100 < sampled * 8)
            return false;

        int width = maxX - minX;
        int height = maxY - minY;
        if (width < 300 || height < 28)
            return false;

        center = new Point((minX + maxX) / 2, (minY + maxY) / 2);
        return true;
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
        _ui.ClickFresh(found.Value.Center, ct);
        await Task.Delay(550, ct);
        return true;
    }

    private async Task<bool> WaitForScreenStateAsync(
        Func<Bitmap, Task<bool>> match,
        int timeoutMs,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);
            if (await match(frame))
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;

            await Task.Delay(100, ct);
        }
    }

    private Task<bool> WaitForFacilityHeaderAsync(
        string title,
        int timeoutMs,
        CancellationToken ct)
        => WaitForScreenStateAsync(
            async frame => await FindFacilityHeaderAsync(frame, title, ct) is not null,
            timeoutMs,
            ct);

    private Task<bool> WaitForProcessingNavigationReadyAsync(
        AlteringPlan plan,
        int timeoutMs,
        CancellationToken ct)
        => WaitForScreenStateAsync(
            async frame =>
            {
                if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null)
                    return true;
                if (await FindFacilityHeaderAsync(frame, "가공", ct) is not null)
                    return true;
                return await FindAsync(frame, new(180, 880, 420, 120), "가공", ct) is not null;
            },
            timeoutMs,
            ct);

    private async Task EnterFacilityAsync(
        AlteringPlan plan, CancellationToken ct,
        AlteringFacilityEntryDirective directive = AlteringFacilityEntryDirective.Automatic)
    {
        _stage.Move(ProductionStage.OpenHub, $"{plan.ScreenTitle} 진입");
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
                    await RequireManagedIdleAsync(plan, directive, "시설 상세/다른창 Esc 직전", ct);
                    _ui.TapFresh(0x01, ct);
                    await WaitForFacilityHeaderAsync(plan.ScreenTitle, 700, ct);
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
                    Log?.Invoke($"[자동 가공] 현재 화면={otherFacility} · 가공 허브로 돌아갑니다 · Esc");
                    await RequireManagedIdleAsync(plan, directive, "시설 상세/다른창 Esc 직전", ct);
                    _ui.TapFresh(0x01, ct);
                    await WaitForFacilityHeaderAsync("가공", 900, ct);
                    continue;
                }

                if (await FindFacilityHeaderAsync(frame, "가공", ct) is not null)
                {
                    Log?.Invoke($"[자동 가공] 현재 화면=가공 허브 · 시설 진입 시도 {attempt}/{maxAttempts}");
                    await RequireManagedIdleAsync(plan, directive, "시설명 메뉴 선택 직전", ct);
                    if (!await ClickLabelAsync(plan.ScreenTitle, AlteringFacilityLayout.TitleArea(plan.ScreenTitle), "가공", ct, facilityTitle: true))
                    {
                        Log?.Invoke($"[자동 가공] 시설 제목 확인 실패 {attempt}/{maxAttempts} · 추가 입력 없이 재판정합니다.");
                        await Task.Delay(1200, ct);
                        continue;
                    }

                    if (await WaitForFacilityHeaderAsync(plan.ScreenTitle, 1200, ct))
                    {
                        Log?.Invoke($"[자동 가공] 시설 진입 성공 {attempt}/{maxAttempts} · {plan.ScreenTitle} · 화면 확인 즉시 진행");
                        return;
                    }

                    Log?.Invoke($"[자동 가공] 시설 전환 확인 대기 {attempt}/{maxAttempts} · 현재 화면을 다시 판정합니다.");
                    await WaitForProcessingNavigationReadyAsync(plan, 900, ct);
                    continue;
                }

                // Unknown after collection can be a transient reward/result screen or the
                // ordinary field. Do one bounded K re-entry only; never spam keys blindly.
                Log?.Invoke($"[자동 가공] 현재 화면=일반/전환 중 · 가공 메뉴 재진입 시도 {attempt}/{maxAttempts}");
                // Never press K over an unknown green/travel modal in a managed run.
                if (directive != AlteringFacilityEntryDirective.Automatic &&
                    (HasBottomConfirmationModal(frame) ||
                     await IsFacilityTravelDialogAsync(frame, ct)))
                    Fail(frame, "관리 가공 K 메뉴 진입 전 확인창/이동창 감지 · 입력 차단");
                await RequireManagedIdleAsync(plan, directive, "가공 메뉴 K 직전", ct);
                _ui.TapFresh(0x25, ct);
            }

            await WaitForProcessingNavigationReadyAsync(plan, 1000, ct);
            using (var menu = Capture(ct))
            {
                if (await FindFacilityHeaderAsync(menu, plan.ScreenTitle, ct) is not null)
                {
                    Log?.Invoke($"[자동 가공] 시설 진입 성공 {attempt}/{maxAttempts} · {plan.ScreenTitle}");
                    return;
                }

                if (await FindFacilityHeaderAsync(menu, "가공", ct) is null)
                {
                    await RequireManagedIdleAsync(plan, directive, "가공 메뉴 진입 클릭 직전", ct);
                    if (!await ClickLabelAsync("가공", new(180, 880, 420, 120), null, ct))
                    {
                        Log?.Invoke($"[자동 가공] 가공 메뉴 확인 실패 {attempt}/{maxAttempts} · 추가 입력 없이 재판정합니다.");
                        await Task.Delay(1200, ct);
                        continue;
                    }
                    await WaitForFacilityHeaderAsync("가공", 900, ct);
                }
            }
        }

        using var failed = Capture(ct);
        Fail(failed, "선택한 가공 시설 화면을 3회 확인하지 못했습니다.");
    }

    private static string RecipeCacheKey(AlteringPlan plan)
        => $"{plan.FacilityName}\u001f{plan.DisplayName}\u001f{plan.RecipeOrdinal}";

    private async Task<bool> TryReuseOnsiteFacilityAsync(AlteringPlan plan, CancellationToken ct)
    {
        if (!string.Equals(_confirmedOnsiteFacility, plan.FacilityName, StringComparison.Ordinal))
            return false;

        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            bool facilityVisible = await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null;
            bool moveVisible = facilityVisible && HasFacilityMoveButtonVisual(frame);

            if (!facilityVisible || moveVisible)
            {
                _confirmedOnsiteFacility = null;
                return false;
            }

            if (pass == 0)
                await Task.Delay(100, ct);
        }

        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 연속 등록 · 현장 상태 유지 확인 · 고정 이동버튼 전체형태+중심앵커 부재 2회 확인");
        return true;
    }

    private async Task<bool> TrySelectFixedRecipeAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
    {
        if (!AlteringRecipeLayout.IsFixedFacility(plan.FacilityName))
            return false;

        if (!AlteringRecipeLayout.TryGetFixedCenter(plan, out Point center))
            throw new InvalidOperationException(
                $"{plan.ScreenTitle} 고정좌표가 정의되지 않았습니다: " +
                $"{plan.DisplayName} (순번 {plan.RecipeOrdinal}/{plan.RecipeCount})");

        if (!AlteringRecipeLayout.IsSafeFixedCenter(center))
            throw new InvalidOperationException(
                $"{plan.ScreenTitle} 고정좌표가 800x1000 안전 영역을 벗어났습니다: " +
                $"{plan.DisplayName} ({center.X},{center.Y})");

        using (var frame = Capture(ct))
        {
            if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
                Fail(frame, $"{plan.ScreenTitle} 고정좌표 입력 전 시설 화면을 확인하지 못했습니다.");

            if (directive != AlteringFacilityEntryDirective.Automatic &&
                HasBottomConfirmationModal(frame))
                Fail(frame, $"{plan.ScreenTitle} 품목 선택 전 확인창이 남아 있어 입력 없이 정지합니다.");

            if (AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
                    directive, HasFacilityMoveButtonVisual(frame)))
                Fail(frame,
                    $"{plan.ScreenTitle} 설비로 이동 버튼이 남아 있어 원격 화면으로 판정했습니다. " +
                    "품목 고정좌표 입력 없이 정지합니다.");
        }

        Log?.Invoke(
            $"[자동 가공] {plan.ScreenTitle} 고정좌표 선택 · {plan.DisplayName} · " +
            $"순번 {plan.RecipeOrdinal}/{plan.RecipeCount} · ({center.X},{center.Y}) · 카드명 OCR 없음");
        await RequireManagedIdleAsync(plan, directive, "고정 품목 카드 선택 직전", ct);
        _ui.ClickFresh(center, ct);
        await Task.Delay(350, ct);

        bool detailConfirmed;
        using (var popup = Capture(ct))
            detailConfirmed = await IsRecipeDetailStructureAsync(popup, ct);

        if (!detailConfirmed)
        {
            // A field/gathering return can leave the processing list visually valid
            // while the first card click is dropped or the detail transition is slow.
            // Re-observe before any retry so a delayed detail opening is never double-clicked.
            await Task.Delay(300, ct);
            using var retryGate = Capture(ct);

            if (await IsRecipeDetailStructureAsync(retryGate, ct))
            {
                detailConfirmed = true;
                Log?.Invoke(
                    $"[자동 가공] {plan.DisplayName} 상세 전환 지연 확인 · 추가 클릭 없이 계속");
            }
            else
            {
                bool facilityVisible =
                    await FindFacilityHeaderAsync(retryGate, plan.ScreenTitle, ct) is not null;
                if (directive != AlteringFacilityEntryDirective.Automatic &&
                    HasBottomConfirmationModal(retryGate))
                    Fail(retryGate,
                        $"{plan.ScreenTitle} 고정좌표 재시도 전 확인창이 남아 있어 추가 입력 없이 정지합니다.");

                bool moveVisible = facilityVisible &&
                    AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
                        directive, HasFacilityMoveButtonVisual(retryGate));

                if (!AlteringFixedRecipeRetryPolicy.ShouldRetry(
                        detailVisible: false,
                        facilityVisible,
                        moveVisible,
                        retryAlreadyUsed: false))
                {
                    if (!facilityVisible)
                        Fail(retryGate,
                            $"{plan.ScreenTitle} 고정좌표 1차 클릭 후 상세창도 시설 목록도 확인되지 않아 재클릭하지 않고 정지합니다.");

                    Fail(retryGate,
                        $"{plan.ScreenTitle} 고정좌표 1차 클릭 후 설비로 이동 버튼이 보여 원격 상태로 판정했습니다. 재클릭하지 않고 정지합니다.");
                }

                Log?.Invoke(
                    $"[자동 가공] {plan.DisplayName} 고정좌표 1차 클릭 미반영 확인 · " +
                    $"시설 목록 유지 + 중앙 지시/기존 안전판정 유지 · 동일 좌표 재클릭 1/1");
                await RequireManagedIdleAsync(plan, directive, "고정 품목 재클릭 직전", ct);
                _ui.ClickFresh(center, ct);
                await Task.Delay(450, ct);

                using var retryPopup = Capture(ct);
                if (!await IsRecipeDetailStructureAsync(retryPopup, ct))
                    Fail(retryPopup,
                        $"{plan.ScreenTitle} 고정좌표 ({center.X},{center.Y}) 1회 재클릭 후에도 품목 상세 구조를 확인하지 못했습니다. 추가 입력 없이 정지합니다.");

                detailConfirmed = true;
                Log?.Invoke(
                    $"[자동 가공] {plan.DisplayName} 고정좌표 재클릭 1/1 성공 · 추가 재시도 없음");
            }
        }

        _stage.Move(ProductionStage.Detail, plan.DisplayName);
        Log?.Invoke(
            $"[자동 가공] {plan.ScreenTitle} · {plan.DisplayName} 고정좌표 상세 구조 확인 완료");
        return true;
    }

    private async Task<bool> TrySelectMedicineRecipeBySearchAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
    {
        if (!string.Equals(plan.FacilityName, "약품 가공 시설", StringComparison.Ordinal))
            return false;

        if (!AlteringRecipeLayout.IsSafeMedicineSearchGeometry())
            throw new InvalidOperationException("약품 검색 고정좌표가 800x1000 안전 영역을 벗어났습니다.");

        using var beforeSearch = Capture(ct);
        if (await FindFacilityHeaderAsync(beforeSearch, plan.ScreenTitle, ct) is null)
            Fail(beforeSearch, "약품 검색 전 약품 가공 화면을 확인하지 못했습니다.");
        if (directive != AlteringFacilityEntryDirective.Automatic &&
            HasBottomConfirmationModal(beforeSearch))
            Fail(beforeSearch, "약품 검색 전 확인창이 남아 있어 입력 없이 정지합니다.");
        if (AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
                directive, HasFacilityMoveButtonVisual(beforeSearch)))
            Fail(beforeSearch,
                "약품 가공 설비로 이동 버튼이 남아 있어 원격 화면으로 판정했습니다. 검색 입력을 차단합니다.");

        await RequireManagedIdleAsync(plan, directive, "약품 검색 열기 직전", ct);
        _ui.ClickFresh(AlteringRecipeLayout.ProcessingSearchIconPoint, ct);
        Log?.Invoke(
            $"[자동 가공] 약품 검색 돋보기 · 고정좌표 " +
            $"({AlteringRecipeLayout.ProcessingSearchIconPoint.X},{AlteringRecipeLayout.ProcessingSearchIconPoint.Y})");
        await Task.Delay(450, ct);

        using var searchDialog = Capture(ct);
        double openRatio = ProductionUiRuntime.MeasureVisualChangeRatio(
            beforeSearch,
            searchDialog,
            AlteringRecipeLayout.ProcessingSearchDialogArea);
        if (openRatio < AlteringRecipeLayout.ProcessingSearchOpenChangeRatio)
            Fail(searchDialog,
                $"약품 검색 돋보기 입력 후 검색창 화면 전환을 확인하지 못했습니다. 변화율={openRatio:P1}");

        await RequireManagedIdleAsync(plan, directive, "약품 검색어 입력 직전", ct);
        _ui.ClickFresh(AlteringRecipeLayout.ProcessingSearchInputPoint, ct);
        _ui.PasteFresh(plan.DisplayName, ct);
        await Task.Delay(120, ct);
        await RequireManagedIdleAsync(plan, directive, "약품 검색 Enter 직전", ct);
        _ui.TapFresh(0x1C, ct); // Enter
        Log?.Invoke($"[자동 가공] 약품 검색어 입력 확정 · Enter · {plan.DisplayName}");
        await Task.Delay(220, ct);

        using var beforeApply = Capture(ct);
        await RequireManagedIdleAsync(plan, directive, "약품 검색 적용 Space 직전", ct);
        _ui.TapFresh(0x39, ct); // Space = 적용
        Log?.Invoke("[자동 가공] 약품 검색 적용 · Space");
        await Task.Delay(700, ct);

        using var resultFrame = Capture(ct);
        double resultRatio = ProductionUiRuntime.MeasureVisualChangeRatio(
            beforeApply,
            resultFrame,
            AlteringRecipeLayout.ProcessingSearchResultArea);
        var exact = await _ui.Ocr.FindAlteringLabelsAsync(
            resultFrame,
            AlteringRecipeLayout.ProcessingSearchResultArea,
            plan.DisplayName,
            ct,
            cardCandidate: directive == AlteringFacilityEntryDirective.Automatic);

        if (directive != AlteringFacilityEntryDirective.Automatic &&
            (exact.Count != 1 ||
             !AlteringRecipeIdentityPolicy.IsNearMedicineFirstResult(
                 exact[0].Center.X, exact[0].Center.Y,
                 AlteringRecipeLayout.ProcessingSearchFirstResultPoint.X,
                 AlteringRecipeLayout.ProcessingSearchFirstResultPoint.Y)))
            Fail(resultFrame,
                "관리 약품 검색 결과의 정확 일치·첫 행 위치를 확인하지 못해 선택하지 않습니다.");

        Log?.Invoke(
            exact.Count > 0
                ? $"[자동 가공] 약품 검색 결과 · OCR 후보 {exact.Count}개 · 화면 변화 {resultRatio:P1}"
                : $"[자동 가공] 약품 검색 결과 · OCR 미검출 · 화면 변화 {resultRatio:P1}");

        if (exact.Count == 0 && resultRatio < AlteringRecipeLayout.ProcessingSearchResultChangeRatio)
            Fail(resultFrame,
                $"약품 검색 적용 후 결과 화면 전환을 확인하지 못했습니다. 변화율={resultRatio:P1}");

        if (plan.RecipeOrdinal != 1)
            Fail(resultFrame,
                $"약품 검색 결과가 중복 제법 순번 {plan.RecipeOrdinal}이라 첫 결과 고정좌표를 사용하지 않습니다.");

        await RequireManagedIdleAsync(plan, directive, "약품 검색 결과 선택 직전", ct);
        _ui.ClickFresh(AlteringRecipeLayout.ProcessingSearchFirstResultPoint, ct);
        Log?.Invoke(
            $"[자동 가공] 약품 검색 첫 결과 선택 · 고정좌표 " +
            $"({AlteringRecipeLayout.ProcessingSearchFirstResultPoint.X},{AlteringRecipeLayout.ProcessingSearchFirstResultPoint.Y})");
        await Task.Delay(400, ct);

        using var popup = Capture(ct);
        if (!await IsRecipeDetailStructureAsync(popup, ct))
            Fail(popup, "약품 검색 첫 결과 클릭 후 품목 상세 구조를 확인하지 못했습니다.");

        _stage.Move(ProductionStage.Detail, plan.DisplayName);
        Log?.Invoke($"[자동 가공] 약품 검색 완료 · {plan.DisplayName} · 제작과 동일한 검색 흐름");
        return true;
    }

    private async Task SelectRecipeAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
    {
        await RequireManagedIdleAsync(plan, directive, "품목 선택 경로 진입", ct);
        _stage.Move(ProductionStage.Search, plan.DisplayName);
        if (await TrySelectFixedRecipeAsync(plan, directive, ct))
            return;
        if (await TrySelectMedicineRecipeBySearchAsync(plan, directive, ct))
            return;

        string cacheKey = RecipeCacheKey(plan);

        // Fallback only for a future/unknown facility. Current five fixed-grid
        // facilities return above, and medicine uses the search flow above.
        if (_hasCachedRecipeCenter &&
            string.Equals(_cachedRecipeKey, cacheKey, StringComparison.Ordinal))
        {
            using (var frame = Capture(ct))
            {
                if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
                    Fail(frame, "연속 등록 전 가공 시설 화면을 확인하지 못했습니다.");

                if (directive != AlteringFacilityEntryDirective.Automatic &&
                    HasBottomConfirmationModal(frame))
                    Fail(frame, "연속 등록 중 확인창이 남아 있어 고정 품목 좌표 입력 없이 정지합니다.");

                if (AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
                        directive, HasFacilityMoveButtonVisual(frame)))
                    Fail(frame, "연속 등록 중 원격 가공 화면이 감지되어 고정 품목 좌표 입력을 차단했습니다.");
            }

            await RequireManagedIdleAsync(plan, directive, "캐시 품목 선택 직전", ct);
            _ui.ClickFresh(_cachedRecipeCenter, ct);
            await Task.Delay(250, ct);

            using var popup = Capture(ct);
            if (!await IsRecipeDetailStructureAsync(popup, ct))
                Fail(popup, "저장된 고정 품목 좌표에서 선택한 품목 상세 화면을 확인하지 못했습니다.");

            _stage.Move(ProductionStage.Detail, plan.DisplayName);
            Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · {plan.DisplayName} 고정 품목 좌표 재사용");
            return;
        }

        // First observation for this recipe: discover the fixed card location by OCR,
        // confirm it on a fresh frame, then remember its center for later registrations.
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            using var frame = Capture(ct);
            if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
                Fail(frame, "가공 목록이 사라졌습니다.");

            var labels = await _ui.Ocr.FindAlteringLabelsAsync(
                frame, Cards, plan.DisplayName, ct, cardCandidate: true);
            if (labels.Count == plan.RecipeCount && labels.Count >= plan.RecipeOrdinal)
            {
                var candidate = labels[plan.RecipeOrdinal - 1];

                await Task.Delay(120, ct);
                using var fresh = Capture(ct);
                var confirmed = await _ui.Ocr.FindAlteringLabelsAsync(
                    fresh, Cards, plan.DisplayName, ct, cardCandidate: true);

                if (confirmed.Count != plan.RecipeCount ||
                    confirmed.Count < plan.RecipeOrdinal ||
                    !confirmed[plan.RecipeOrdinal - 1].Bounds.IntersectsWith(candidate.Bounds))
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                var selected = confirmed[plan.RecipeOrdinal - 1];
                _cachedRecipeKey = cacheKey;
                _cachedRecipeCenter = selected.Center;
                _hasCachedRecipeCenter = true;

                await RequireManagedIdleAsync(plan, directive, "OCR 품목 선택 직전", ct);
                _ui.ClickFresh(selected.Center, ct);
                await Task.Delay(350, ct);
                using var popup = Capture(ct);
                if (!await IsRecipeDetailStructureAsync(popup, ct))
                    Fail(popup, "선택한 품목의 상세 화면을 확인하지 못했습니다.");

                _stage.Move(ProductionStage.Detail, plan.DisplayName);
                Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · {plan.DisplayName} 고정 품목 좌표 저장");
                return;
            }

            await Task.Delay(150, ct);
        }

        using var missing = Capture(ct);
        Fail(missing, "고정된 가공 목록 위치에서 선택 품목/제법을 확인하지 못했습니다. 화면을 드래그하지 않고 정지합니다.");
    }

    private async Task<(bool IsRemote, string Evidence)> DetectRemoteProcessStateAsync(
        Bitmap frame,
        CancellationToken ct)
    {
        // This helper is called only after IsRecipeDetailStructureAsync has already
        // confirmed the recipe detail screen. The remote facility move-button ROI
        // belongs to the facility list screen and overlaps unrelated colours on the
        // detail popup, so it must never be used as a veto at this stage.
        if (_repeatOcrFreeRecipe)
        {
            ct.ThrowIfCancellationRequested();
            bool free = TryFindFreeProcessButtonVisual(frame, out _);
            return (!free, free ? "반복 가공 무료 버튼 고정 위치 확인" : "고정 무료 버튼 없음");
        }
        var paid = await FindAsync(frame, RecipeActionButton, "가공하러 가기", ct);
        return (paid is not null, paid is not null ? "가공하러 가기 OCR" : "없음");
    }

    public Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
        => QueueAsync(
            plan,
            AlteringFacilityEntryDirective.Automatic,
            reserveFiveWings,
            ct);


    // F03: manager authorization is a decision about facility ownership, not
    // permission to input while character activity is moving or unavailable.
    // Return a typed mismatch so the owner invalidates its onsite evidence.
    private async Task RequireManagedIdleAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        string phase,
        CancellationToken ct)
    {
        if (directive == AlteringFacilityEntryDirective.Automatic)
            return;

        ct.ThrowIfCancellationRequested();
        GatheringActivity? activity = null;
        if (_cli is not null)
        {
            var response = await _cli.GetActivityAsync(ct);
            if (response.Success)
                activity = GatheringQueries.ParseActivity(response);
        }

        if (activity is not null &&
            GatheringSafetyPolicy.IsSafeField(activity) &&
            !activity.IsAutoTraveling &&
            !activity.IsGathering &&
            !activity.IsFishing &&
            !activity.IsInCombat)
            return;

        string reason = $"관리 가공 입력 차단 · {phase} · " +
            $"CLI activity={(activity is null ? "불명확" : activity.IsAutoTraveling ? "이동 중" : "안전한 대기 아님")}";
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · {reason} · 중앙 현장확정 무효화 요청");
        throw new AlteringCoordinatorFacilityMismatchException(
            plan.FacilityName, reason, new InvalidOperationException(reason));
    }

    public async Task QueueAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        Action reserveFiveWings,
        CancellationToken ct)
    {
        plan.Validate();
        if (plan.AllowPaidButton)
            throw new InvalidOperationException("정령의 날개를 사용하는 가공 경로는 실행하지 않습니다.");

        await RequireManagedIdleAsync(plan, directive, "시설 진입/재사용 전", ct);

        // Cache is enabled only after the FIRST successful manager-directed
        // registration of this facility / exact fixed recipe in this run.
        _postTravelProvenFacilityTitle = null;
        _repeatOcrFreeFacilityTitle =
            directive == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite &&
            _verifiedFacilityTitles.Contains(plan.ScreenTitle)
                ? plan.ScreenTitle : null;
        _repeatOcrFreeRecipe =
            directive == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite &&
            _verifiedFixedRecipes.Contains(RecipeCacheKey(plan));
        if (_repeatOcrFreeFacilityTitle is not null)
            Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 1차 통과 · 같은 시설 반복 OCR 전체 생략");

        // Explicit manager directive supersedes any lower Automatic cache.
        if (directive != AlteringFacilityEntryDirective.Automatic)
            _confirmedOnsiteFacility = null;

        bool reusedOnsite = false;
        if (directive == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite)
        {
            using var frame = Capture(ct);
            bool facilityVisible =
                await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null;
            bool completionModal = HasBottomConfirmationModal(frame);
            if (!facilityVisible || completionModal)
                Fail(
                    frame,
                    "중간관리자가 같은 시설 현장 재사용을 지시했지만 시설창 복귀 상태를 확인하지 못했습니다. " +
                    "하위 가공 모듈이 임의로 설비 이동을 다시 판단하지 않습니다.");

            _confirmedOnsiteFacility =
                AlteringScreenOnsiteCachePolicy.AfterVerifiedFacilityEntry(
                    directive, plan.FacilityName);
            reusedOnsite = true;
            _stage.Move(
                ProductionStage.OpenHub,
                $"{plan.ScreenTitle} 중간관리자 현장 재사용");
            Log?.Invoke(
                $"[자동 가공] 중간관리자 지시 · {plan.ScreenTitle} 현장확정 재사용 · " +
                "설비 이동 버튼 색상/형태 재판정 없음 · 바로 품목 선택");
        }
        else if (directive == AlteringFacilityEntryDirective.FreshMoveRequired)
        {
            _confirmedOnsiteFacility = null;
            await EnterFacilityAsync(plan, ct, directive);
            Log?.Invoke(
                $"[자동 가공] 중간관리자 지시 · {plan.ScreenTitle} 새 시설 진입 · " +
                "품목 선택보다 설비 이동 1회 우선");
            await TravelToFacilityAsync(plan, ct, forceMoveClick: true, directive: directive);
            await EnterFacilityAsync(plan, ct, directive);
            _confirmedOnsiteFacility =
                AlteringScreenOnsiteCachePolicy.AfterVerifiedFacilityEntry(
                    directive, plan.FacilityName);
            Log?.Invoke(
                $"[자동 가공] 중간관리자 지시 이행 · {plan.ScreenTitle} 설비 도착 · " +
                $"이제 {plan.DisplayName} 선택");
        }
        else
        {
            reusedOnsite = await TryReuseOnsiteFacilityAsync(plan, ct);
            if (reusedOnsite)
            {
                _stage.Move(ProductionStage.OpenHub, $"{plan.ScreenTitle} 현장 상태 재사용");
            }
            else
            {
                _confirmedOnsiteFacility = null;
                await EnterFacilityAsync(plan, ct, directive);
                Log?.Invoke(
                    $"[자동 가공] {plan.ScreenTitle} 진입 · 새 시설 첫 등록 · " +
                    "품목 선택보다 설비 이동 1회 우선");
                await TravelToFacilityAsync(plan, ct, forceMoveClick: true);
                await EnterFacilityAsync(plan, ct, directive);
                _confirmedOnsiteFacility = plan.FacilityName;
                Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 설비 도착 · 이제 {plan.DisplayName} 선택");
            }
        }

        bool remoteRecoveryUsed = false;

        // V3.1.8 execution model for every processing facility remains the base:
        // select only after facility arrival, then validate fresh detail frames.
        // V3.1.31 only adds one bounded recovery when the paid remote state persists.
        while (true)
        {
            // Keep the coordinator's authority through the entire recipe-selection
            // chain (fixed card, retry, medicine, cached center).
            await RequireManagedIdleAsync(plan, directive, "품목 선택 전", ct);
            await SelectRecipeAsync(plan, directive, ct);
            Point visualActionCenter = Point.Empty;
            bool restartAfterRemoteRecovery = false;

            for (int pass = 0; pass < 2; pass++)
            {
                using var frame = Capture(ct);
                if (!await IsRecipeDetailStructureAsync(frame, ct))
                    Fail(frame, "설비 도착 후 품목 상세 화면을 확인하지 못했습니다.");

                bool firstOnsiteAction = TryFindFreeProcessButtonVisual(
                    frame,
                    out Point firstOnsiteActionCenter);
                var remoteCandidate = await DetectRemoteProcessStateAsync(frame, ct);

                if (remoteCandidate.IsRemote)
                {
                    Log?.Invoke(
                        $"[자동 가공] 작업 등록 직전 원격 상태 후보 1/2 · {remoteCandidate.Evidence} · " +
                        "단일 프레임으로 중단하지 않고 200ms 후 재확인");
                    await Task.Delay(200, ct);

                    using var confirm = Capture(ct);
                    if (!await IsRecipeDetailStructureAsync(confirm, ct))
                        Fail(confirm, "원격 상태 재확인 중 품목 상세 화면이 사라졌습니다.");

                    bool secondOnsiteAction = TryFindFreeProcessButtonVisual(
                        confirm,
                        out Point secondOnsiteActionCenter);
                    var remoteConfirmed = await DetectRemoteProcessStateAsync(confirm, ct);
                    bool remoteTwoFrames = AlteringRemoteProcessGuard.ShouldBlock(
                        remoteCandidate.IsRemote,
                        remoteConfirmed.IsRemote);

                    if (remoteTwoFrames)
                    {
                        if (AlteringRemoteProcessGuard.MustReportToCoordinator(
                                directive, remoteTwoFrames))
                        {
                            const string reason =
                                "가공하러 가기 OCR 2프레임 감지 · 중간관리자 시설 현장확정과 충돌";
                            Log?.Invoke(
                                $"[자동 가공] {plan.ScreenTitle} · {reason} · " +
                                "가공 클릭/설비 이동 0회 · 중간관리자에게 상태 충돌 반환");
                            throw new AlteringCoordinatorFacilityMismatchException(
                                plan.FacilityName,
                                reason,
                                _ui.Failure(confirm,
                                    reason + " · 하위 모듈 추가 입력 없이 정지합니다."));
                        }

                        // Only Automatic (single altering) may use the legacy
                        // one-shot recovery. A managed queue never reaches it.
                        if (!AlteringRemoteProcessGuard.CanRecover(true, remoteRecoveryUsed))
                            Fail(confirm,
                                "무료 설비 이동 복구 후에도 가공하러 가기 상태가 2프레임 연속 확인되어 현장 가공 입력 없이 정지합니다.");

                        Log?.Invoke(
                            "[자동 가공] 원격 상세 2프레임 확정 · 현장 버튼 색상만으로 누르지 않음 · 무료 설비 이동 1회 복구");
                        await RecoverRemoteDetailToOnsiteAsync(plan, ct);
                        remoteRecoveryUsed = true;
                        restartAfterRemoteRecovery = true;
                        break;
                    }

                    Log?.Invoke(
                        $"[자동 가공] 작업 등록 직전 원격 상태 1회 오탐 해제 · " +
                        $"1차={remoteCandidate.Evidence} · 2차=없음 · 현장 가공 계속");

                    if (!secondOnsiteAction)
                        Fail(confirm, "원격 상태 오탐 해제 후 하단 현장 가공 실행 버튼을 확인하지 못했습니다.");

                    visualActionCenter = secondOnsiteActionCenter;
                }
                else
                {
                    if (!firstOnsiteAction)
                        Fail(frame, "설비 도착 후 하단 현장 가공 실행 버튼을 화면에서 확인하지 못했습니다.");

                    visualActionCenter = firstOnsiteActionCenter;
                }

                if (pass == 0)
                {
                    await Task.Delay(200, ct);
                    continue;
                }

                _stage.Move(ProductionStage.Process, $"{plan.DisplayName} 작업 등록");
                Log?.Invoke(
                    "[자동 가공] 설비 도착 후 현장 가공 버튼 화면 확인 · 가공하러 가기 2프레임 아님 · 정령의 날개 버튼 입력 없음");
                await RequireManagedIdleAsync(plan, directive, "무료 가공 등록 버튼 직전", ct);
                if (directive != AlteringFacilityEntryDirective.Automatic)
                {
                    // F07: require matching recipe identity and the intended
                    // free button in TWO new images. Ambiguous output aliases
                    // never stand in for two differently-qualified recipes.
                    Point priorVerifiedCenter = Point.Empty;
                    bool exactDetail = await AlteringRecipeIdentityPolicy.VerifyTwoFreshObservationsAsync(
                        async token =>
                        {
                            using var fresh = Capture(token);
                            if (!await IsRecipeDetailStructureAsync(fresh, token) ||
                                await FindRecipeAsync(fresh, plan, token) is null ||
                                (await DetectRemoteProcessStateAsync(fresh, token)).IsRemote ||
                                !TryFindFreeProcessButtonVisual(fresh, out Point candidate))
                                return false;
                            if (priorVerifiedCenter != Point.Empty &&
                                !AlteringRecipeIdentityPolicy.IsStableFreshFreeActionTarget(
                                    priorVerifiedCenter, candidate, FreeProcessVisualButton))
                                return false;
                            priorVerifiedCenter = candidate;
                            return true;
                        },
                        Task.Delay,
                        ct);
                    if (!exactDetail)
                    {
                        const string reason = "F07 관리 제법 제목/무료 버튼 동일성 2회 확인 실패 · 출력명 중복/원격/좌표 변경 시 오등록 차단";
                        throw new AlteringCoordinatorFacilityMismatchException(
                            plan.FacilityName, reason, new InvalidOperationException(reason));
                    }
                    visualActionCenter = priorVerifiedCenter;
                    await RequireManagedIdleAsync(plan, directive, "품목명 확인 후 입력 직전", ct);
                }

                // N01: never click an earlier bitmap's remembered button
                // center after awaited OCR/activity checks. Take ONE last
                // live screenshot and click that exact image's center only
                // if the expected detail, free button and stable geometry
                // are all still present. This also protects single-altering.
                using var immediate = Capture(ct);
                bool correctDetail = await IsRecipeDetailStructureAsync(immediate, ct);
                bool correctIdentity = directive == AlteringFacilityEntryDirective.Automatic ||
                    await FindRecipeAsync(immediate, plan, ct) is not null;
                bool stillRemote = (await DetectRemoteProcessStateAsync(immediate, ct)).IsRemote;
                bool buttonFound = TryFindFreeProcessButtonVisual(immediate, out Point liveCenter);
                if (!correctDetail || !correctIdentity || stillRemote || !buttonFound ||
                    !AlteringRecipeIdentityPolicy.IsStableFreshFreeActionTarget(
                        visualActionCenter, liveCenter, FreeProcessVisualButton))
                {
                    const string reason = "N01/F07 무료 가공 버튼 입력 직전 최신 화면의 제법/버튼/좌표 불일치";
                    if (directive != AlteringFacilityEntryDirective.Automatic)
                        throw new AlteringCoordinatorFacilityMismatchException(
                            plan.FacilityName, reason, new InvalidOperationException(reason));
                    Fail(immediate, reason);
                }
                ct.ThrowIfCancellationRequested();
                _ui.ClickFresh(liveCenter, ct);
            }

            if (restartAfterRemoteRecovery)
                continue;

            // Only a successful first registration establishes this run's
            // verified fixed-coordinate path for subsequent repeats.
            if (directive != AlteringFacilityEntryDirective.Automatic)
            {
                _verifiedFacilityTitles.Add(plan.ScreenTitle);
                if (AlteringRecipeLayout.IsFixedFacility(plan.FacilityName))
                    _verifiedFixedRecipes.Add(RecipeCacheKey(plan));
            }
            return;
        }
    }

    private async Task RecoverRemoteDetailToOnsiteAsync(
        AlteringPlan plan,
        CancellationToken ct)
    {
        _confirmedOnsiteFacility = null;

        // EnterFacilityAsync already owns the bounded detail-close transition.
        await EnterFacilityAsync(plan, ct);
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 원격 상세 복구 · 무료 설비 이동 1회 실행");

        await TravelToFacilityAsync(plan, ct, forceMoveClick: true);
        await EnterFacilityAsync(plan, ct);

        _confirmedOnsiteFacility = plan.FacilityName;
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 무료 설비 이동 복구 완료 · {plan.DisplayName} 다시 선택");
    }

    private static bool HasFacilityTravelConfirmationVisual(Bitmap frame)
    {
        // The optional "nearby facility unavailable -> move to ..." dialog appears
        // around the middle of the screen, not only in the old bottom confirmation ROI.
        // Detect a broad saturated-green confirmation control in the center/lower modal
        // area. This detector is used as authoritative only immediately after a
        // facility-move click; elsewhere travel wording is still required.
        var roi = Rectangle.Intersect(
            FacilityTravelConfirmVisual,
            new Rectangle(Point.Empty, frame.Size));
        if (roi.Width < 300 || roi.Height < 250)
            return false;

        int sampled = 0;
        int green = 0;
        int minX = roi.Right, minY = roi.Bottom, maxX = roi.Left, maxY = roi.Top;
        for (int y = roi.Top; y < roi.Bottom; y += 3)
        for (int x = roi.Left; x < roi.Right; x += 3)
        {
            Color p = frame.GetPixel(x, y);
            sampled++;
            bool confirmGreen =
                p.G >= 95 &&
                p.G >= p.R + 40 &&
                p.G >= p.B + 15;
            if (!confirmGreen)
                continue;

            green++;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (sampled == 0 || green * 1000 < sampled * 12)
            return false;

        return maxX - minX >= 110 && maxY - minY >= 22;
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

    private async Task TravelToFacilityAsync(
        AlteringPlan plan,
        CancellationToken ct,
        bool forceMoveClick = false,
        bool receiptMode = false,
        AlteringFacilityEntryDirective directive = AlteringFacilityEntryDirective.Automatic)
    {
        if (_cli is null)
            throw new InvalidOperationException("무료 설비 이동 상태 확인용 CLI가 연결되지 않았습니다.");
        if (!AlteringFacilityLayout.IsSafeMoveGeometry())
            throw new InvalidOperationException("설비로 이동 고정좌표가 800x1000 안전 영역을 벗어났습니다.");

        int moveFrames = 0;
        bool shouldClickMove = forceMoveClick;
        bool preClickRemoteMoveButtonConfirmed = false;

        if (forceMoveClick)
        {
            // V3.1.47 live log 20:19: the newly opened wood facility was remote,
            // but a false-negative move-button detector allowed "onsite" proof and
            // selected 상급 목재 before 설비로 이동. On a fresh facility entry
            // QueueAsync now owns the order: facility -> move once -> recipe.
            using var confirmed = Capture(ct);
            if (await FindFacilityHeaderAsync(confirmed, plan.ScreenTitle, ct) is null)
                Fail(confirmed, "설비 이동 전 선택한 가공 시설 화면을 확인하지 못했습니다.");

            bool moveVisible =
                await HasFacilityMoveButtonPositiveEvidenceAsync(confirmed, ct);
            preClickRemoteMoveButtonConfirmed = moveVisible;
            Log?.Invoke(
                $"[자동 가공] {plan.ScreenTitle} · 설비 이동 1회 필수 경로 · " +
                $"버튼검출={(moveVisible ? "확인" : "미검출")} · " +
                "버튼 검출 결과로 품목 선택 순서를 바꾸지 않음");
        }
        else
        {
            // Non-forced callers (mainly receipt/location verification) may already
            // be onsite. Only those paths are allowed to use visual evidence to
            // decide whether a move click is necessary.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                using var frame = Capture(ct);

                if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
                    Fail(frame, "설비 이동 전 선택한 가공 시설 화면을 확인하지 못했습니다.");

                bool moveVisible =
                    await HasFacilityMoveButtonPositiveEvidenceAsync(frame, ct);
                if (moveVisible)
                {
                    moveFrames++;
                    if (moveFrames >= 2)
                    {
                        shouldClickMove = true;
                        break;
                    }
                }
                else
                {
                    moveFrames = 0;
                }

                await Task.Delay(180, ct);
            }

            if (!shouldClickMove)
            {
                Log?.Invoke(
                    $"[자동 가공] {plan.ScreenTitle} · 현장 가능 경로 · 이동버튼 미검출 · " +
                    "7프레임/3초+후행 재확인으로 현장 여부 검증");
            }
        }

        bool moveClickSent = false;
        if (shouldClickMove)
        {
            _stage.Move(ProductionStage.Travel, $"{plan.ScreenTitle} 설비로 이동");
            Log?.Invoke(
                $"[자동 가공] {plan.ScreenTitle} · 설비로 이동 고정좌표 클릭 " +
                $"({AlteringFacilityLayout.MoveButtonPoint.X},{AlteringFacilityLayout.MoveButtonPoint.Y}) · " +
                "입력 1회 고정 · 재클릭 금지 · 품목 선택 전 실행");
            await RequireManagedIdleAsync(plan, directive, "설비 이동 클릭 직전", ct);
            _ui.ClickFresh(AlteringFacilityLayout.MoveButtonPoint, ct);
            moveClickSent = true;
        }

        bool sawDeparture = false;
        bool sawTravel = false;
        int loadingCliRejects = 0;
        int onsiteStableFrames = 0;
        DateTime? onsiteCandidateSince = null;
        int travelConfirmationSpaces = 0;
        // F01: do not treat an isolated OCR title miss or unrelated CLI
        // loading rejection as a genuine relocation. Evidence must be
        // correlated in the same POST-CLICK frames, or CLI must report active
        // auto-travel. The counter is consecutive and resets on readable CLI.
        int loadingDepartureStreak = 0;
        bool confirmedMoveTransition = false;

        for (int attempt = 0; attempt < 120; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(attempt < 20 ? 200 : 500, ct);

            // Capture first: the optional travel popup can appear while the facility
            // header remains behind it. Do not wait for CLI/OCR cycles before handling it.
            using var frame = Capture(ct);
            // The fixed title/subtitle anchors take precedence over OCR after
            // the known facility was entered and the move click was sent.
            // This accepts the 03:08:57 live screenshot where OCR missed the
            // leftmost glyphs despite the real facility UI being visible.
            bool facilityVisible = HasFixedFacilityHeaderVisual(frame) ||
                await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null;

            bool travelPopupVisual =
                moveClickSent &&
                !sawDeparture &&
                HasFacilityTravelConfirmationVisual(frame);

            if (AlteringFacilityTravelConfirmPolicy.ShouldConfirmAfterMoveClick(
                    travelPopupVisual,
                    travelConfirmationSpaces,
                    sawDeparture))
            {
                bool wordingMatched = await IsFacilityTravelDialogAsync(frame, ct);

                if (directive != AlteringFacilityEntryDirective.Automatic)
                {
                    // F03/F04: a generic green confirmation shape cannot
                    // authorize managed Space. Require move-specific wording
                    // and a CURRENT idle activity reading before any input.
                    if (!wordingMatched)
                        Fail(frame,
                            "관리 시설 이동 확인창 문구 불명확 · 일반 초록창에 Space 입력 금지");
                    await RequireManagedIdleAsync(
                        plan, directive, "시설 이동 확인 팝업 Space 직전", ct);
                    using var freshDialog = Capture(ct);
                    if (!HasFacilityTravelConfirmationVisual(freshDialog) ||
                        !await IsFacilityTravelDialogAsync(freshDialog, ct))
                        Fail(freshDialog,
                            "관리 시설 이동 확인창이 입력 직전 변경됨 · Space 차단");
                    await RequireManagedIdleAsync(
                        plan, directive, "시설 이동 팝업 최종 CLI 허가", ct);
                }
                _ui.TapFresh(0x39, ct); // Space = confirm optional facility travel
                travelConfirmationSpaces++;
                onsiteStableFrames = 0;
                onsiteCandidateSince = null;

                Log?.Invoke(
                    $"[자동 가공] {plan.ScreenTitle} · 이동 확인창 감지 · " +
                    $"화면중앙 초록 확인 구조 + 이동문구OCR={(wordingMatched ? "확인" : "미검출(이동클릭 직후 전용 판정)")} · " +
                    $"Space {travelConfirmationSpaces}/{AlteringFacilityTravelConfirmPolicy.MaxTravelConfirmationSpaces}");

                await Task.Delay(450, ct);

                using var afterSpace = Capture(ct);
                bool popupStillVisible =
                    HasFacilityTravelConfirmationVisual(afterSpace);

                if (!popupStillVisible)
                {
                    Log?.Invoke(
                        $"[자동 가공] {plan.ScreenTitle} · 이동 확인창 닫힘 확인 · 자동이동/전환 대기");
                    continue;
                }

                if (travelConfirmationSpaces >=
                    AlteringFacilityTravelConfirmPolicy.MaxTravelConfirmationSpaces)
                {
                    Fail(
                        afterSpace,
                        "설비 이동 확인창이 Space 2회 입력 후에도 그대로 남아 있어 " +
                        "설비 이동 재클릭 없이 정지합니다.");
                }

                Log?.Invoke(
                    $"[자동 가공] {plan.ScreenTitle} · 이동 확인창 유지 · " +
                    "같은 팝업에 Space 1회만 재시도 예정");
                continue;
            }

            GatheringActivity? activity = null;
            var activityResponse = await _cli.GetActivityAsync(ct);
            if (activityResponse.Success)
            {
                loadingDepartureStreak = 0;
                activity = GatheringQueries.ParseActivity(activityResponse);
                if (!activity.IsSafeField)
                    throw new InvalidOperationException(
                        "설비 이동 중 전투·대화 등 안전하지 않은 상태가 확인되어 정지합니다.");

                if (activity.IsAutoTraveling)
                {
                    sawTravel = true;
                    sawDeparture = true;
                }
            }
            else if (CliAutomationGuards.IsTransientLoadingRejection(activityResponse))
            {
                loadingCliRejects++;
                // A transient CLI rejection with the facility header STILL
                // visible is not proof of leaving this facility. Count only
                // consecutive reject+header-absent observations after the
                // actual move click; do not combine unrelated observations.
                if (moveClickSent && !facilityVisible)
                    loadingDepartureStreak++;
                else
                    loadingDepartureStreak = 0;
                if (!facilityVisible)
                    sawDeparture = true;
                if (loadingCliRejects == 1 || loadingCliRejects % 5 == 0)
                    Log?.Invoke(
                        $"[자동 가공] 지역 이동 로딩 중 CLI 일시 거부 · 재시도 {loadingCliRejects}회");
            }
            else
            {
                loadingDepartureStreak = 0;
                _ = GatheringQueries.ParseActivity(activityResponse);
            }

            if (!facilityVisible)
                sawDeparture = true;

            if (AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedMoveTransition(
                    moveClickSent, sawTravel, loadingDepartureStreak))
                confirmedMoveTransition = true;

            bool moveVisible = false;
            if (facilityVisible)
            {
                if (receiptMode && moveClickSent)
                {
                    bool visualMoveButton = HasFacilityMoveButtonVisual(frame);
                    bool exactMoveLabelVisible = false;
                    if (visualMoveButton)
                    {
                        exactMoveLabelVisible = await FindAsync(
                            frame,
                            AlteringFacilityLayout.MoveButtonVisualArea,
                            "설비로 이동",
                            ct) is not null;
                    }

                    moveVisible = AlteringReceiptPolicy.IsRemoteMoveButtonAfterReceiptMove(
                        visualMoveButton,
                        exactMoveLabelVisible);
                }
                else
                {
                    moveVisible = await HasFacilityMoveButtonPositiveEvidenceAsync(frame, ct);
                }
            }

            bool managedFreshArrival =
                directive == AlteringFacilityEntryDirective.FreshMoveRequired && moveClickSent;
            bool instantArrival = managedFreshArrival && !receiptMode &&
                AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
                    moveClickSent, preClickRemoteMoveButtonConfirmed,
                    facilityVisible, HasOnsiteCloseButtonVisual(frame), moveVisible,
                    activity?.IsAutoTraveling,
                    HasFacilityTravelConfirmationVisual(frame) ||
                    HasBottomConfirmationModal(frame));
            bool onsiteObserved = managedFreshArrival
                ? AlteringFacilityTravelConfirmPolicy.IsManagedFreshArrivalObservation(
                    facilityVisible, activity?.IsAutoTraveling,
                    moveClickSent, confirmedMoveTransition || instantArrival)
                : AlteringFacilityTravelConfirmPolicy.IsOnsiteObservation(
                    facilityVisible, moveVisible, activity?.IsAutoTraveling);
            if (onsiteObserved)
            {
                onsiteCandidateSince ??= DateTime.UtcNow;
                onsiteStableFrames++;
                TimeSpan stableFor = DateTime.UtcNow - onsiteCandidateSince.Value;

                if (AlteringFacilityTravelConfirmPolicy.HasStableOnsiteEvidence(
                        onsiteStableFrames,
                        stableFor))
                {
                    await Task.Delay(
                        AlteringFacilityTravelConfirmPolicy.FinalOnsiteRecheckDelay,
                        ct);

                    GatheringActivity? finalActivity = null;
                    var finalActivityResponse = await _cli.GetActivityAsync(ct);
                    if (finalActivityResponse.Success)
                        finalActivity = GatheringQueries.ParseActivity(finalActivityResponse);

                    using var finalFrame = Capture(ct);
                    bool finalPopupVisible =
                        moveClickSent &&
                        HasFacilityTravelConfirmationVisual(finalFrame);
                    bool finalFacilityVisible = HasFixedFacilityHeaderVisual(finalFrame) ||
                        await FindFacilityHeaderAsync(finalFrame, plan.ScreenTitle, ct) is not null;
                    bool finalMoveVisible = false;
                    if (finalFacilityVisible)
                    {
                        if (receiptMode && moveClickSent)
                        {
                            bool visualMoveButton = HasFacilityMoveButtonVisual(finalFrame);
                            bool exactMoveLabelVisible = false;
                            if (visualMoveButton)
                            {
                                exactMoveLabelVisible = await FindAsync(
                                    finalFrame,
                                    AlteringFacilityLayout.MoveButtonVisualArea,
                                    "설비로 이동",
                                    ct) is not null;
                            }

                            finalMoveVisible = AlteringReceiptPolicy.IsRemoteMoveButtonAfterReceiptMove(
                                visualMoveButton,
                                exactMoveLabelVisible);
                        }
                        else
                        {
                            finalMoveVisible =
                                await HasFacilityMoveButtonPositiveEvidenceAsync(finalFrame, ct);
                        }
                    }
                    bool finalInstantArrival = managedFreshArrival && !receiptMode &&
                        AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
                            moveClickSent, preClickRemoteMoveButtonConfirmed,
                            finalFacilityVisible, HasOnsiteCloseButtonVisual(finalFrame),
                            finalMoveVisible, finalActivity?.IsAutoTraveling,
                            finalPopupVisible || HasBottomConfirmationModal(finalFrame));
                    bool finalArrivalObservation = managedFreshArrival
                        ? AlteringFacilityTravelConfirmPolicy.IsManagedFreshArrivalObservation(
                            finalFacilityVisible, finalActivity?.IsAutoTraveling,
                            moveClickSent, confirmedMoveTransition || finalInstantArrival)
                        : AlteringFacilityTravelConfirmPolicy.IsOnsiteObservation(
                            finalFacilityVisible, finalMoveVisible,
                            finalActivity?.IsAutoTraveling);
                    bool finalOnsite =
                        !finalPopupVisible &&
                        finalActivity is not null &&
                        GatheringSafetyPolicy.IsSafeField(finalActivity) &&
                        finalArrivalObservation;

                    if (finalOnsite)
                    {
                        // Proven by fixed UI anchors, two timed observations,
                        // a clean modal state and idle CLI; post-travel facility
                        // re-entry must not demand a second title OCR pass.
                        _postTravelProvenFacilityTitle = plan.ScreenTitle;
                        Log?.Invoke(
                            $"[자동 가공] {plan.ScreenTitle} · 설비 도착 확인 · " +
                            $"가공창 유지 + {onsiteStableFrames}프레임/{stableFor.TotalSeconds:F1}초 " +
                            $"(관리 Fresh={managedFreshArrival}, 실제 이동전환={confirmedMoveTransition}, " +
                            $"즉시 도착 이중확인={finalInstantArrival}, 이동버튼={finalMoveVisible}) " +
                            "+ 1.2초 후행 재확인 · CLI AutoTraveling=false · 이동확인창 없음");
                        return;
                    }

                    Log?.Invoke(
                        $"[자동 가공] {plan.ScreenTitle} · 설비 도착 후보 후행 재확인 실패 · " +
                        $"가공창={finalFacilityVisible} · 이동버튼={finalMoveVisible} · " +
                        $"이동확인창={finalPopupVisible} · " +
                        $"CLI AutoTraveling={(finalActivity?.IsAutoTraveling.ToString() ?? "조회불가")} · 계속 대기");
                    onsiteStableFrames = 0;
                    onsiteCandidateSince = null;
                }
            }
            else
            {
                onsiteStableFrames = 0;
                onsiteCandidateSince = null;
            }

            if (attempt > 0 && attempt % 10 == 0)
                Log?.Invoke(
                    $"[자동 가공] 설비 이동 대기 · 이동감지={sawTravel} · " +
                    $"화면이탈/로딩={sawDeparture} · 가공창={facilityVisible} · " +
                    $"이동버튼확정={moveVisible} · 수령상태분리={receiptMode && moveClickSent} · " +
                    $"F01 검증전환={confirmedMoveTransition} · 근거리즉시도착={instantArrival} · " +
                    $"원격버튼사전확인={preClickRemoteMoveButtonConfirmed} · 동시로딩/타이틀부재연속={loadingDepartureStreak} · " +
                    $"이동확인Space={travelConfirmationSpaces} · CLI로딩거부={loadingCliRejects}");
        }

        throw new InvalidOperationException(
            "설비로 이동 입력 후 확인창/자동이동/현장 전환을 제한 시간 안에 확인하지 못해 " +
            "추가 설비 이동 클릭 없이 정지합니다.");
    }

    // Free navigation only, for opening an ingredient's obtain-method route.
    // This entry point cannot reach QueueAsync or any paid button.
    internal async Task OpenRecipeAsync(AlteringPlan plan, CancellationToken ct)
    {
        if(plan.AllowPaidButton) throw new InvalidOperationException("채집 경로는 가공 비용 버튼을 사용할 수 없습니다.");
        await EnterFacilityAsync(plan, ct);
        // Gathering/recipe-inspection navigation is not a coordinator-issued queue.
        // Preserve the original Automatic move-visual veto.
        await SelectRecipeAsync(
            plan, AlteringFacilityEntryDirective.Automatic, ct);
    }

    private async Task<int?> TryFacilityWorkCountAsync(
        AlteringPlan plan,
        CancellationToken ct)
    {
        if (_cli is null)
            return null;

        var response = await _cli.GetAlteringWorksAsync(ct);
        if (!response.Success)
        {
            if (CliAutomationGuards.IsTransientLoadingRejection(response))
                return null;
            _ = AlteringQueries.ParseWorks(response);
        }

        return AlteringQueries.ParseWorks(response)
            .Count(x => x.FacilityName == plan.FacilityName);
    }

    private async Task<bool?> TryAutoTravelingAsync(CancellationToken ct)
    {
        if (_cli is null)
            return null;

        var response = await _cli.GetActivityAsync(ct);
        if (!response.Success)
        {
            if (CliAutomationGuards.IsTransientLoadingRejection(response))
                return null;
            return GatheringQueries.ParseActivity(response).IsAutoTraveling;
        }

        return GatheringQueries.ParseActivity(response).IsAutoTraveling;
    }

    private async Task<bool> IsFacilityTravelDialogAsync(Bitmap frame, CancellationToken ct)
    {
        if (!HasFacilityTravelConfirmationVisual(frame))
            return false;

        // Outside the immediate move-click transition, keep wording as the
        // discriminator so completion/result dialogs cannot be mistaken for travel.
        return await FindAsync(frame, FacilityTravelDialog, "근처", ct) is not null ||
               await FindAsync(frame, FacilityTravelDialog, "이동합니다", ct) is not null ||
               await FindAsync(frame, FacilityTravelDialog, "설비로 이동", ct) is not null ||
               await FindAsync(frame, FacilityTravelDialog, "이동", ct) is not null;
    }

    private async Task<bool> HasFacilityMoveButtonPositiveEvidenceAsync(
        Bitmap frame,
        CancellationToken ct)
    {
        if (HasFacilityMoveButtonVisual(frame))
            return true;

        // OCR is a positive-only fallback. Missing OCR never proves on-site, but
        // seeing the exact label must veto a false "button disappeared" decision.
        return await FindAsync(
            frame,
            AlteringFacilityLayout.MoveButtonVisualArea,
            "설비로 이동",
            ct) is not null;
    }

    private async Task<bool> WaitForReceiptFacilityReturnAsync(
        AlteringPlan plan,
        int? receiptWorkCountBefore,
        bool managedReceipt,
        CancellationToken ct)
    {
        int facilityFrames = 0;
        int fieldFrames = 0;
        for (int wait = 0; wait < 60; wait++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct);

            using var returned = Capture(ct);
            if (await FindFacilityHeaderAsync(returned, plan.ScreenTitle, ct) is not null)
            {
                bool completionModal = HasBottomConfirmationModal(returned);
                bool travelDialog = await IsFacilityTravelDialogAsync(returned, ct);
                bool? autoTraveling = await TryAutoTravelingAsync(ct);
                bool cleanReturn = AlteringReceiptPolicy.CanConfirmReceiptFacilityReturn(
                    facilityHeaderVisible: true,
                    completionModalVisible: completionModal,
                    travelDialogVisible: travelDialog,
                    autoTraveling: autoTraveling);
                fieldFrames = 0;
                facilityFrames = cleanReturn ? facilityFrames + 1 : 0;
                if (facilityFrames >= 2)
                {
                    _confirmedOnsiteFacility =
                        AlteringScreenOnsiteCachePolicy.AfterVerifiedReceiptReturn(
                            managedReceipt, plan.FacilityName);
                    _stage.Move(ProductionStage.VerifyInventory, $"{plan.DisplayName} 수령 후 시설 복귀");
                    Log?.Invoke(
                        $"[자동 가공] 가공 완료 확인창 닫기 완료 · {plan.ScreenTitle} 창 복귀 2프레임 · " +
                        "완료/이동 팝업 없음 · CLI 비이동 확정");
                    return true;
                }
                continue;
            }

            facilityFrames = 0;

            // V3.1.39 live log (14:23): after closing the result the game
            // returned to the FIELD, not to the metal-working facility.
            // Only read-only CLI + stable visual state may authorize ONE
            // navigation recovery. No second receive/confirmation Space here.
            if (receiptWorkCountBefore is not > 0 || wait < 5)
                continue;

            bool confirmationVisible = HasBottomConfirmationModal(returned);
            bool otherUi = await FindFacilityHeaderAsync(returned, "가공", ct) is not null ||
                           await FindAsync(returned, new(100, 690, 580, 200), "필요한 재료", ct) is not null;
            if (!otherUi)
            {
                foreach (string other in AlteringPlan.Facilities.Select(x => x.Replace(" 시설", "")))
                {
                    if (other == plan.ScreenTitle) continue;
                    if (await FindFacilityHeaderAsync(returned, other, ct) is not null)
                    {
                        otherUi = true;
                        break;
                    }
                }
            }

            // Unknown/loading CLI must not turn into a field re-entry command.
            GatheringActivity? activity = null;
            if (_cli is not null)
            {
                var activityResponse = await _cli.GetActivityAsync(ct);
                if (activityResponse.Success)
                    activity = GatheringQueries.ParseActivity(activityResponse);
            }

            bool safeField = activity is not null &&
                             GatheringSafetyPolicy.IsSafeField(activity) &&
                             !activity.IsGathering && !activity.IsFishing;
            bool mayRecover = AlteringReceiptPolicy.CanRecoverFieldAfterCompletion(
                fieldOnly: !otherUi,
                confirmationVisible: confirmationVisible,
                autoTraveling: activity?.IsAutoTraveling,
                safeField: safeField,
                receiptWorkCountBefore: receiptWorkCountBefore);

            fieldFrames = mayRecover ? fieldFrames + 1 : 0;
            if (fieldFrames < 3)
                continue;

            _confirmedOnsiteFacility = null;
            if (managedReceipt)
                throw new InvalidOperationException(
                    $"{plan.DisplayName} 수령 후 일반 필드 복귀가 3회 확인됐습니다. " +
                    "필드에서 K로 연 가공창은 물리적 현장 증거가 아니므로 중간관리자 재사용을 금지합니다. " +
                    "임의 메뉴 재진입/설비 이동/Space 없이 정지합니다.");

            Log?.Invoke($"[자동 가공] 수령 완료창 닫기 후 일반 필드 3회 확인 · " +
                        $"{plan.ScreenTitle} 가공 메뉴 1회 재진입 시도 · 재수령 Space 금지");
            await EnterFacilityAsync(plan, ct);

            int reopenedFrames = 0;
            for (int confirm = 0; confirm < 3; confirm++)
            {
                using var reopened = Capture(ct);
                if (await FindFacilityHeaderAsync(reopened, plan.ScreenTitle, ct) is not null)
                    reopenedFrames++;
                else
                    reopenedFrames = 0;
                if (reopenedFrames >= 2)
                    break;
                await Task.Delay(250, ct);
            }
            if (reopenedFrames < 2)
                throw new InvalidOperationException(
                    $"{plan.DisplayName} 완료창 닫기 후 가공 메뉴 재진입 상태를 2회 확인하지 못했습니다. 추가 입력 없이 정지합니다.");

            // Returning to a facility proves ONLY navigation. A result popup
            // disappearing does not prove an item was received. Fail closed if
            // whole-facility queue has not actually decreased.
            int? receiptWorkCountAfter = null;
            for (int retry = 0; retry < 4; retry++)
            {
                receiptWorkCountAfter = await TryFacilityWorkCountAsync(plan, ct);
                if (AlteringReceiptPolicy.IsProvenReceiptAfterReopen(
                        receiptWorkCountBefore, receiptWorkCountAfter))
                    break;
                await Task.Delay(350, ct);
            }

            if (!AlteringReceiptPolicy.IsProvenReceiptAfterReopen(
                    receiptWorkCountBefore, receiptWorkCountAfter))
            {
                Log?.Invoke($"[자동 가공] 일반 필드 복구 후 수령 미확정 · {plan.DisplayName} · " +
                            $"시설 전체 작업 {receiptWorkCountBefore}->{(receiptWorkCountAfter?.ToString() ?? "조회불가")} · 재수령 입력 없음");
                throw new InvalidOperationException(
                    $"{plan.DisplayName} 완료 화면에서 필드로 복귀했지만 시설 전체 작업 감소를 확인하지 못했습니다. " +
                    "중복 수령 방지를 위해 추가 입력 없이 정지합니다.");
            }

            _stage.Move(ProductionStage.VerifyInventory,
                $"{plan.DisplayName} 필드 복구 후 수령 확인");
            Log?.Invoke($"[자동 가공] 수령 후 필드 복귀 자동 복구 성공 · {plan.ScreenTitle} " +
                        $"시설창 2회 확인 · 작업 {receiptWorkCountBefore}->{receiptWorkCountAfter} · 추가 Space 0회");
            return true;
        }

        return false;
    }

    // Managed receipt: distinguish the actual "가공 완료" result from
    // unrelated green confirmations. The title is a positive OCR signal;
    // absence is uncertainty, never permission to press Space.
    private async Task RequireManagedCompletionCloseAsync(
        AlteringPlan plan, CancellationToken ct)
    {
        await RequireManagedIdleAsync(
            plan, AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
            "완료 결과창 Space 직전 활동 검사", ct);
        using var frame = Capture(ct);
        bool green = HasBottomConfirmationModal(frame);
        bool heading = green &&
            await FindAsync(frame, Whole, "가공 완료", ct) is not null;
        bool facility = await FindFacilityHeaderAsync(
            frame, plan.ScreenTitle, ct) is not null;
        bool travelDialog = await IsFacilityTravelDialogAsync(frame, ct);
        bool? traveling = await TryAutoTravelingAsync(ct);
        if (!AlteringReceiptPolicy.CanCloseManagedCompletionResult(
                green, heading, facility, travelDialog, traveling))
            Fail(frame,
                "관리 수령 완료 결과창 고유 제목/초록 버튼/CLI 비이동 증거 부족 · 다른 초록 팝업 Space 차단");
        // A second fresh activity gate after OCR prevents its await from
        // extending the lifetime of the input authorization.
        await RequireManagedIdleAsync(
            plan, AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
            "완료 결과창 최종 CLI 허가", ct);
    }

    private async Task<bool> CloseCompletionResultAndWaitForFacilityAsync(
        AlteringPlan plan,
        CancellationToken ct,
        string reason,
        bool cliReceiptConfirmed = false,
        int? receiptWorkCountBefore = null,
        bool managedReceipt = false)
    {
        // Revalidate the exact result modal immediately before any closing
        // Space. A prior two-frame observation can go stale during CLI reads.
        using (var beforeClose = Capture(ct))
        {
            bool facilityVisible = await FindFacilityHeaderAsync(
                beforeClose, plan.ScreenTitle, ct) is not null;
            bool greenConfirm = HasBottomConfirmationModal(beforeClose);
            bool travelDialog = await IsFacilityTravelDialogAsync(beforeClose, ct);
            bool? autoTraveling = await TryAutoTravelingAsync(ct);
            bool canClose = AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                    managedReceipt, autoTraveling) &&
                (cliReceiptConfirmed
                    ? AlteringReceiptPolicy.CanConfirmCliReceiptCompletion(
                        greenConfirm, facilityVisible, travelDialog)
                    : AlteringReceiptPolicy.CanConfirmCompletion(
                        greenConfirm, facilityVisible, travelDialog,
                        autoTraveling == true));
            if (!canClose)
                Fail(beforeClose,
                    "수령 완료창 Space 직전 재검증 실패 · 완료창/이동창/시설창 상태 불확실 · 추가 입력 없이 정지합니다.");
        }

        if (managedReceipt)
            await RequireManagedCompletionCloseAsync(plan, ct);
        Log?.Invoke($"[자동 가공] {reason} · 완료창 닫기 Space 1차 입력");
        _ui.TapFresh(0x39, ct);
        await Task.Delay(450, ct);

        using (var verify = Capture(ct))
        {
            bool facilityVisible = await FindFacilityHeaderAsync(
                verify, plan.ScreenTitle, ct) is not null;
            bool greenConfirm = HasBottomConfirmationModal(verify);
            bool travelDialog =
                await IsFacilityTravelDialogAsync(verify, ct);
            bool? autoTraveling = await TryAutoTravelingAsync(ct);

            bool canRetryClose = AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                    managedReceipt, autoTraveling) &&
                (cliReceiptConfirmed
                    ? AlteringReceiptPolicy.CanRetryCliReceiptCompletionClose(
                        greenConfirm, facilityVisible, travelDialog)
                    : AlteringReceiptPolicy.CanRetryCompletionClose(
                    greenConfirm,
                    facilityVisible,
                    travelDialog,
                    autoTraveling == true));

            if (canRetryClose)
            {
                Log?.Invoke(
                    "[자동 가공] 완료창 닫기 Space 1차 입력 후에도 실제 완료창 유지 · " +
                    "이동 상태 아님 재확인 · Space 1회 재시도");

                await Task.Delay(180, ct);
                using var retryFrame = Capture(ct);
                bool retryFacilityVisible = await FindFacilityHeaderAsync(
                    retryFrame, plan.ScreenTitle, ct) is not null;
                bool retryGreenConfirm = HasBottomConfirmationModal(retryFrame);
                bool retryTravelDialog =
                    await IsFacilityTravelDialogAsync(retryFrame, ct);
                bool? retryAutoTraveling = await TryAutoTravelingAsync(ct);

                bool canRetryAgain = AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                        managedReceipt, retryAutoTraveling) &&
                    (cliReceiptConfirmed
                        ? AlteringReceiptPolicy.CanRetryCliReceiptCompletionClose(
                            retryGreenConfirm, retryFacilityVisible, retryTravelDialog)
                        : AlteringReceiptPolicy.CanRetryCompletionClose(
                        retryGreenConfirm,
                        retryFacilityVisible,
                        retryTravelDialog,
                        retryAutoTraveling == true));

                if (canRetryAgain)
                {
                    if (managedReceipt)
                        await RequireManagedCompletionCloseAsync(plan, ct);
                    _ui.TapFresh(0x39, ct);
                    Log?.Invoke("[자동 가공] 완료창 닫기 Space 2차 입력 완료 · 추가 재시도 없음");
                    await Task.Delay(450, ct);
                }
                else
                {
                    Log?.Invoke(
                        "[자동 가공] 완료창 닫기 재시도 직전 화면 상태 변경 · " +
                        "추가 Space 입력 없이 복귀 상태 확인");
                }
            }
            else if (greenConfirm && (travelDialog || autoTraveling == true))
            {
                Log?.Invoke(
                    $"[자동 가공] 완료창 닫기 1차 입력 후 초록창/이동 상태 감지 · " +
                    $"이동팝업={travelDialog} · AutoTraveling={autoTraveling == true} · 추가 Space 없음");
            }
            else
            {
                Log?.Invoke("[자동 가공] 완료창 닫기 Space 1차 입력 후 완료창 이탈 확인");
            }
        }

        using (var closedCheck = Capture(ct))
        {
            bool facilityVisible = await FindFacilityHeaderAsync(
                closedCheck, plan.ScreenTitle, ct) is not null;
            bool greenConfirm = HasBottomConfirmationModal(closedCheck);
            bool travelDialog =
                await IsFacilityTravelDialogAsync(closedCheck, ct);
            bool? autoTraveling = await TryAutoTravelingAsync(ct);

            bool completionStillVisible = AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                    managedReceipt, autoTraveling) &&
                (cliReceiptConfirmed
                    ? AlteringReceiptPolicy.CanRetryCliReceiptCompletionClose(
                        greenConfirm, facilityVisible, travelDialog)
                    : AlteringReceiptPolicy.CanRetryCompletionClose(
                    greenConfirm,
                    facilityVisible,
                    travelDialog,
                    autoTraveling == true));

            if (completionStillVisible)
            {
                Fail(
                    closedCheck,
                    "가공 완료 확인창이 Space 2회 입력 후에도 그대로 남아 있어 추가 입력 없이 정지합니다.");
            }
        }

        return await WaitForReceiptFacilityReturnAsync(
            plan, receiptWorkCountBefore, managedReceipt, ct);
    }

    private async Task<bool> ConfirmCompletionResultAsync(
        AlteringPlan plan,
        CancellationToken ct,
        int? receiptWorkCountBefore = null,
        int resultAttempts = 24,
        bool managedReceipt = false)
    {
        // Successful receipt replaces the facility UI with the full-screen
        // "가공 완료" result. Detect the large green bottom confirmation shape,
        // while requiring the facility title to be absent so this cannot be confused
        // with the facility-travel confirmation popup.
        int stableFrames = 0;
        int cliReceiptStableFrames = 0;
        for (int attempt = 0; attempt < resultAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct);

            int? liveMatchingCount = receiptWorkCountBefore is int
                ? await TryFacilityWorkCountAsync(plan, ct)
                : null;

            using var frame = Capture(ct);
            bool greenConfirm = HasBottomConfirmationModal(frame);
            bool facilityVisible = await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null;

            if (receiptWorkCountBefore is int receiptBefore &&
                liveMatchingCount is int receiptNow &&
                AlteringReceiptPolicy.IsCliReceiptConfirmed(receiptBefore, receiptNow))
            {
                _stage.Move(ProductionStage.VerifyInventory, $"{plan.DisplayName} 수령 후 CLI 작업 감소 확인");

                if (facilityVisible)
                {
                    // CLI proves the receipt, but not the character's physical
                    // return. Never trust just one frame of facility header.
                    if (await WaitForReceiptFacilityReturnAsync(
                            plan, receiptBefore, managedReceipt, ct))
                        return true;

                    using var ambiguous = Capture(ct);
                    Fail(ambiguous,
                        "CLI 수령 확정 후 시설창 복귀 2프레임/완료창 없음/CLI 비이동을 확인하지 못했습니다.");
                }

                bool travelDialogAfterReceipt =
                    await IsFacilityTravelDialogAsync(frame, ct);
                bool? autoTravelAfterReceipt = await TryAutoTravelingAsync(ct);

                if (!AlteringReceiptPolicy.CanConfirmCliReceiptCompletion(
                        greenConfirm,
                        facilityVisible,
                        travelDialogAfterReceipt) ||
                    !AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                        managedReceipt, autoTravelAfterReceipt))
                {
                    cliReceiptStableFrames = 0;
                    if (travelDialogAfterReceipt)
                    {
                        Log?.Invoke(
                            "[자동 가공] CLI 수령 확정 후 실제 이동 확인창 감지 · 완료창 Space 차단");
                    }
                    continue;
                }

                cliReceiptStableFrames++;
                if (cliReceiptStableFrames < 2)
                    continue;

                if (autoTravelAfterReceipt == true)
                {
                    Log?.Invoke(
                        "[자동 가공] CLI 수령 확정 + 완료창 2프레임 + 이동팝업 없음 · " +
                        "AutoTraveling 잔류값 무시 · 완료창 Space 허용");
                }

                _stage.Move(ProductionStage.Complete, $"{plan.DisplayName} 수령 완료 화면");
                if (await CloseCompletionResultAndWaitForFacilityAsync(
                        plan,
                        ct,
                        $"첫 수령 Space 후 CLI 시설 전체 작업 감소로 수령 확정 · {receiptBefore}->{receiptNow}",
                        cliReceiptConfirmed: true,
                        receiptWorkCountBefore: receiptBefore,
                        managedReceipt: managedReceipt))
                    return true;

                using var cliFailed = Capture(ct);
                Fail(
                    cliFailed,
                    "CLI로 수령은 확인했고 완료창 닫기 입력도 처리했지만 가공 시설 화면 복귀를 확인하지 못했습니다.");
            }

            bool travelDialogVisible = await IsFacilityTravelDialogAsync(frame, ct);
            bool? autoTraveling = await TryAutoTravelingAsync(ct);

            bool canConfirm = AlteringReceiptPolicy.CanConfirmCompletion(
                greenConfirm,
                facilityVisible,
                travelDialogVisible,
                autoTraveling == true) &&
                AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                    managedReceipt, autoTraveling);

            if (canConfirm)
            {
                stableFrames++;
                if (stableFrames < 2)
                    continue;

                // One more activity read immediately before the only result-confirm
                // Space. If travel started between the two visual frames, input stays
                // blocked.
                bool? freshTravel = await TryAutoTravelingAsync(ct);
                if (!AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                        managedReceipt, freshTravel) || freshTravel == true)
                {
                    stableFrames = 0;
                    Log?.Invoke("[자동 가공] 완료창 후보 감지 중 AutoTraveling=true · 추가 Space 차단");
                    continue;
                }

                int? workCountBefore = receiptWorkCountBefore ?? await TryFacilityWorkCountAsync(plan, ct);
                _stage.Move(ProductionStage.Complete, $"{plan.DisplayName} 수령 완료 화면");
                if (await CloseCompletionResultAndWaitForFacilityAsync(
                        plan,
                        ct,
                        "가공 완료 결과창 확인 · 이동 팝업/자동이동 아님 · " +
                        $"첫 수령 전 시설 전체 작업수={(workCountBefore?.ToString() ?? "확인불가")}",
                        receiptWorkCountBefore: workCountBefore,
                        managedReceipt: managedReceipt))
                    return true;

                using var failed = Capture(ct);
                Fail(failed,
                    "가공 완료 확인창 닫기 입력 후 가공 시설 화면 복귀를 확인하지 못했습니다.");
            }
            else
            {
                if (greenConfirm && (travelDialogVisible || autoTraveling == true))
                    Log?.Invoke(
                        $"[자동 가공] 초록 확인창 감지했지만 이동 상태라 Space 차단 · " +
                        $"이동팝업={travelDialogVisible} · AutoTraveling={autoTraveling == true}");
                stableFrames = 0;
            }
        }

        return false;
    }

    public Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
        => CollectAsync(plan, AlteringFacilityEntryDirective.Automatic, ct);

    public Task<bool> CollectAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
        => CollectAsyncAtBoundary(plan, directive, _ => Task.CompletedTask, ct);

    public async Task<bool> CollectAsyncAtBoundary(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        Func<CancellationToken, Task> beforeReceiveInput,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(beforeReceiveInput);
        // L1: never retain a single-mode observation through managed receipt.
        if (directive != AlteringFacilityEntryDirective.Automatic)
            _confirmedOnsiteFacility = null;

        await EnterFacilityAsync(plan, ct, directive);

        if (directive == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite)
        {
            using var onsiteFrame = Capture(ct);
            bool correctFacility =
                await FindFacilityHeaderAsync(onsiteFrame, plan.ScreenTitle, ct) is not null;
            bool resultDialog = HasBottomConfirmationModal(onsiteFrame);
            bool? autoTravel = await TryAutoTravelingAsync(ct);
            if (!correctFacility || resultDialog || autoTravel != false)
                Fail(
                    onsiteFrame,
                    "중간관리자 수령 현장 재사용 지시와 실제 시설/팝업/이동 상태가 불일치하여 " +
                    "자체 설비 이동 없이 안전 정지합니다.");

            _confirmedOnsiteFacility =
                AlteringScreenOnsiteCachePolicy.AfterVerifiedFacilityEntry(
                    directive, plan.FacilityName);
            Log?.Invoke(
                $"[자동 가공] 중간관리자 수령 지시 · {plan.ScreenTitle} 같은 현장 재사용 · " +
                "설비 이동 0회 · 이동 버튼 OCR/색상은 위치 재판정에 사용하지 않음");
        }
        else if (directive == AlteringFacilityEntryDirective.FreshMoveRequired)
        {
            _confirmedOnsiteFacility = null;
            Log?.Invoke(
                $"[자동 가공] 중간관리자 수령 지시 · {plan.ScreenTitle} 새 시설 이동 1회 선행");
            await TravelToFacilityAsync(plan, ct, forceMoveClick: true, receiptMode: true, directive: directive);
            await EnterFacilityAsync(plan, ct, directive);
            _confirmedOnsiteFacility =
                AlteringScreenOnsiteCachePolicy.AfterVerifiedFacilityEntry(
                    directive, plan.FacilityName);
        }
        else
        {
            // Single-altering retains its proven legacy travel decision.
            Log?.Invoke($"[자동 가공] 완료품 수령 전 현장 가공대 확인 · {plan.ScreenTitle}");
            await TravelToFacilityAsync(plan, ct, receiptMode: true);
            await EnterFacilityAsync(plan, ct, directive);
            _confirmedOnsiteFacility = plan.FacilityName;
        }

        // All receipts still require two stable blue-button + completed-work
        // observations, 400ms settling and one last recheck before Space.
        if (!await WaitForCollectPromptAsync(plan, attempts: 24, delayMs: 250, ct, directive))
        {
            using var failed = Capture(ct);
            Fail(failed, "현장 가공대 도착 후 완료 작업 + 파란 수령 버튼을 제한 시간 안에 확인하지 못했습니다.");
        }

        int? receiptWorkCountBefore = await TryFacilityWorkCountAsync(plan, ct);

        // The receive button can become visible slightly before the game starts
        // accepting Space. Give the UI one short stabilization window, then prove
        // the exact receive state again before the single receive input.
        Log?.Invoke(
            $"[자동 가공] 파란 수령 버튼 입력 전 400ms 안정화 · {plan.ScreenTitle} · " +
            $"수령 전 시설 전체 작업수={(receiptWorkCountBefore?.ToString() ?? "확인불가")}");
        await Task.Delay(400, ct);

        if (!await WaitForCollectPromptAsync(plan, attempts: 2, delayMs: 100, ct, directive))
        {
            using var failed = Capture(ct);
            Fail(failed,
                "파란 수령 버튼 입력 직전 400ms 안정화 후 수령 가능 상태를 다시 확인하지 못했습니다. Space 입력 없이 정지합니다.");
        }

        _stage.Move(ProductionStage.Process, $"{plan.DisplayName} 완료 작업 수령");
        Log?.Invoke(
            $"[자동 가공] 현장 수령 화면 재확인 완료 · {plan.ScreenTitle} + 수령 현장 확정 + CLI 완료 작업 + 파란 수령 버튼 · Space 1회 · " +
            $"수령 전 시설 전체 작업수={(receiptWorkCountBefore?.ToString() ?? "확인불가")}");
        // All visual/CLI checks have passed. Persist RecoveryRequired right
        // before the irreversible receive key, not before navigation/OCR.
        // A failure before this callback leaves the last known safe checkpoint.
        await beforeReceiveInput(ct);
        ct.ThrowIfCancellationRequested();
        if (directive != AlteringFacilityEntryDirective.Automatic)
        {
            // The durable receipt marker may require file IO. Revalidate the
            // actual facility queue and blue UI on a NEW frame afterwards,
            // immediately before the irreversible Space. A late running/new
            // work slot must not be partially collected. Because the marker
            // was already persisted, any failure remains RecoveryRequired:
            // fail closed rather than inferring a receipt never happened.
            using var finalFrame = Capture(ct);
            if (!await HasCollectPromptAsync(finalFrame, plan, directive, ct))
                Fail(finalFrame,
                    "F02 수령 입력 직전 시설 전체 완료 증거 소실(부분 완료/신규 대기/화면·CLI 변경) · " +
                    "모두 받기 Space 0회 · RecoveryRequired 유지 · 자동 재수령 금지");
        }
        // F03: right before the irreversible receive input, the coordinator
        // must still have an independently fresh, safe character activity.
        await RequireManagedIdleAsync(
            plan, directive, "모두 받기 최종 Space 직전 활동 검사", ct);
        ct.ThrowIfCancellationRequested();
        _ui.TapFresh(0x39, ct);
        await Task.Delay(450, ct);

        if (!await ConfirmCompletionResultAsync(
                plan, ct, receiptWorkCountBefore: receiptWorkCountBefore, resultAttempts: 24,
                managedReceipt: directive != AlteringFacilityEntryDirective.Automatic))
        {
            using var failed = Capture(ct);
            Fail(failed, "현장 수령 Space 후 가공 완료 결과창을 안전하게 확인하지 못했습니다.");
        }

        return true;
    }

    public async Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
    {
        // Compatibility fallback. The primary CollectAsync now performs travel before
        // any receive Space, so this path should normally be unused. If called, it still
        // waits for proven on-site state before authorizing input.
        await EnterFacilityAsync(plan, ct);
        await TravelToFacilityAsync(plan, ct, receiptMode: true);
        await EnterFacilityAsync(plan, ct);
        if (!await WaitForCollectPromptAsync(plan, attempts: 90, delayMs: 500, ct))
        {
            Log?.Invoke("[자동 가공] 가공대 도착 후 CLI 완료 작업 + 파란 수령 버튼을 제한 시간 안에 확인하지 못했습니다.");
            return false;
        }

        int? receiptWorkCountBefore = await TryFacilityWorkCountAsync(plan, ct);

        _stage.Move(ProductionStage.Process, $"{plan.DisplayName} 완료 작업 2차 수령");
        Log?.Invoke(
            $"[자동 가공] 가공대 도착 확인 · {plan.ScreenTitle} + CLI 완료 작업 + 파란 수령 버튼 · 2차 Space · " +
            $"수령 전 시설 전체 작업수={(receiptWorkCountBefore?.ToString() ?? "확인불가")}");
        _ui.TapFresh(0x39, ct);
        await Task.Delay(700, ct);

        // At the bench this should be the real receipt. Close "가공 완료" when it is
        // visible, but a verified same-item CLI queue decrease is authoritative even
        // if the transient result screen is missed.
        if (!await ConfirmCompletionResultAsync(
                plan, ct, receiptWorkCountBefore: receiptWorkCountBefore))
        {
            using var failed = Capture(ct);
            Fail(failed, "2차 모두 받기 후 가공 완료 확인창을 확인하지 못했습니다.");
        }

        return true;
    }
    public async Task ExitToFieldAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Material gathering must start from the ordinary field. A completed receipt
        // returns to the facility window, so explicitly unwind detail -> facility ->
        // processing hub -> field before the gathering screen sends C.
        _confirmedOnsiteFacility = null;
        _cachedRecipeKey = null;
        _hasCachedRecipeCenter = false;

        int stableFieldFrames = 0;
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);

            bool detailVisible = await FindAsync(
                frame, new Rectangle(100, 690, 580, 200), "필요한 재료", ct) is not null;
            if (detailVisible)
            {
                stableFieldFrames = 0;
                Log?.Invoke($"[자동 가공] 채집 전 화면 정리 {attempt}/10 · 현재=품목 상세 · Esc");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(500, ct);
                continue;
            }

            string? facility = null;
            foreach (string name in AlteringPlan.Facilities.Select(x => x.Replace(" 시설", "")))
            {
                if (await FindFacilityHeaderAsync(frame, name, ct) is not null)
                {
                    facility = name;
                    break;
                }
            }

            if (facility is not null)
            {
                stableFieldFrames = 0;
                Log?.Invoke($"[자동 가공] 채집 전 화면 정리 {attempt}/10 · 현재={facility} 시설창 · Esc");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(700, ct);
                continue;
            }

            if (await FindFacilityHeaderAsync(frame, "가공", ct) is not null)
            {
                stableFieldFrames = 0;
                Log?.Invoke($"[자동 가공] 채집 전 화면 정리 {attempt}/10 · 현재=가공 허브 · Esc");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(700, ct);
                continue;
            }

            // Do not press anything on an unknown/transition frame. Two consecutive
            // frames with no altering detail/facility/hub evidence are required before
            // authorizing the gathering screen to open the profile with C.
            stableFieldFrames++;
            if (stableFieldFrames >= 2)
            {
                Log?.Invoke("[자동 가공] 채집 전 가공 UI 종료 확인 · 일반 필드 2프레임 확인");
                return;
            }

            await Task.Delay(250, ct);
        }

        using var failed = Capture(ct);
        Fail(failed, "채집 시작 전 가공 UI를 완전히 닫고 일반 필드로 복귀하지 못했습니다.");
    }

    public async Task<AlteringStallRecoveryObservation> RecoverStallForCoordinatorAsync(
        AlteringPlan plan, int attempt, string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Managed recovery may only restore the UI: no travel, recipe selection,
        // paid/free processing or local onsite decision from the move-button
        // OCR/colour. Clear the lower cache; the manager alone retains authority.
        _confirmedOnsiteFacility = null;
        _cachedRecipeKey = null;
        _hasCachedRecipeCenter = false;

        using (var snapshot = Capture(ct))
        {
            Directory.CreateDirectory(_debugDir);
            string path = Path.Combine(_debugDir, "altering-stall-last.png");
            try
            {
                snapshot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Log?.Invoke($"[자동 가공] 중간관리자 정체 진단 화면 저장 · {path}");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[자동 가공] 중간관리자 정체 진단 저장 실패 · {ex.Message}");
            }
        }

        // Preflight must prove no character travel is already underway BEFORE
        // sending any UI-only navigation. Unknown CLI state is not proof.
        if (await TryAutoTravelingAsync(ct) != false)
        {
            Log?.Invoke("[자동 가공] 정체 복구 · CLI 이동/불명확 · 추가 입력 없이 위치 미확정 반환");
            return AlteringStallRecoveryObservation.Unknown;
        }

        using (var first = Capture(ct))
        {
            if (HasBottomConfirmationModal(first) ||
                await IsFacilityTravelDialogAsync(first, ct))
            {
                Log?.Invoke("[자동 가공] 정체 복구 · 확인/이동 팝업 감지 · 추가 입력 없이 위치 미확정 반환");
                return AlteringStallRecoveryObservation.Unknown;
            }
        }

        // K, facility menu selection, and Esc only affect the menu. The
        // entry helper never invokes TravelToFacilityAsync or move clicks.
        await EnterFacilityAsync(plan, ct, AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite);

        // Two distinct, stable frames + no modal/detail + proven nontravelling
        // CLI. The persistent '설비로 이동' visual is deliberately ignored.
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            bool correctHeader =
                await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null;
            bool detail = await FindAsync(
                frame, new Rectangle(100, 690, 580, 200), "필요한 재료", ct) is not null;
            bool dialog = HasBottomConfirmationModal(frame) ||
                await IsFacilityTravelDialogAsync(frame, ct);
            bool? traveling = await TryAutoTravelingAsync(ct);
            if (!correctHeader || detail || dialog || traveling != false)
            {
                Log?.Invoke(
                    $"[자동 가공] 정체 복구 · 현장 화면 검증 실패 {pass + 1}/2 · " +
                    "추가 이동/클릭 없이 위치 미확정 반환");
                return AlteringStallRecoveryObservation.Unknown;
            }
            if (pass == 0)
                await Task.Delay(180, ct);
        }

        Log?.Invoke(
            $"[자동 가공] 정체 복구 · {plan.ScreenTitle} 시설 UI만 2프레임 복귀 확인 · " +
            "CLI 비이동 · 설비 이동 버튼으로 위치 재판정 안 함 · 중간관리자에게 보고");
        return AlteringStallRecoveryObservation.SameFacilityUiRestoredWithoutTravel;
    }

    public async Task RecoverStallAsync(
        AlteringPlan plan, int attempt, string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // A stall is the one time we deliberately discard fast-path assumptions.
        // The next registration must prove the facility and recipe again from scratch.
        _confirmedOnsiteFacility = null;
        _cachedRecipeKey = null;
        _hasCachedRecipeCenter = false;

        using (var snapshot = Capture(ct))
        {
            Directory.CreateDirectory(_debugDir);
            string path = Path.Combine(_debugDir, "altering-stall-last.png");
            try
            {
                snapshot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Log?.Invoke($"[자동 가공] 정체 진단 화면 저장 · {path}");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[자동 가공] 정체 진단 화면 저장 실패 · {ex.Message}");
            }

            bool sameFacility = await FindFacilityHeaderAsync(snapshot, plan.ScreenTitle, ct) is not null;
            bool detailVisible = await FindAsync(
                snapshot, new Rectangle(100, 690, 580, 200), "필요한 재료", ct) is not null;

            if (detailVisible)
            {
                Log?.Invoke($"[자동 가공] 정체 화면 재판정 {attempt}회 · 현재=품목 상세 · Esc로 시설창 복귀");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(500, ct);
            }
            else if (sameFacility)
            {
                bool popupVisible = HasBottomConfirmationModal(snapshot);
                bool moveVisible = !popupVisible && HasFacilityMoveButtonVisual(snapshot);

                if (!popupVisible)
                {
                    _confirmedOnsiteFacility = moveVisible ? null : plan.FacilityName;
                    Log?.Invoke(moveVisible
                        ? $"[자동 가공] 정체 화면 재판정 {attempt}회 · 현재={plan.ScreenTitle} 원격 시설창 · 입력 없이 대기 계속"
                        : $"[자동 가공] 정체 화면 재판정 {attempt}회 · 현재={plan.ScreenTitle} 현장 시설창 · 입력 없이 대기 계속");
                    return;
                }

                Log?.Invoke($"[자동 가공] 정체 화면 재판정 {attempt}회 · 현재={plan.ScreenTitle} + 하단 확인 팝업 · 확인하지 않고 Esc로 닫기");
                _ui.TapFresh(0x01, ct);
                await Task.Delay(400, ct);
            }
            else
            {
                string state = await FindFacilityHeaderAsync(snapshot, "가공", ct) is not null
                    ? "가공 허브"
                    : "일반/전환 중";
                Log?.Invoke($"[자동 가공] 정체 화면 재판정 {attempt}회 · 현재={state} · 안전한 가공 시설창으로 복귀 시도");
            }
        }

        // Only navigate back to the requested facility category. Recovery never calls
        // TravelToFacilityAsync, SelectRecipeAsync, QueueAsync or any processing action.
        await EnterFacilityAsync(plan, ct);
        using var verified = Capture(ct);
        if (await FindFacilityHeaderAsync(verified, plan.ScreenTitle, ct) is null)
            Fail(verified, "정체 복구 후 가공 시설 화면을 확인하지 못했습니다.");

        Log?.Invoke($"[자동 가공] 정체 화면 재판정 완료 · {plan.ScreenTitle} 시설창 확인 · 등록/가공 입력 없음 · {reason}");
    }

    private void Fail(Bitmap frame, string message)
        => throw _ui.Failure(frame, message + " 추가 입력 없이 정지합니다.");

    public void Dispose() => _ui.Dispose();
}
