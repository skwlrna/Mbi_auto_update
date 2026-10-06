using FishingAutomation;

namespace DungeonVisionBot;

internal sealed class AlteringScreen : IAlteringScreen, IAlteringRecoveryScreen, IAlteringFieldExitScreen
{
    private readonly ProductionUiRuntime _ui;
    private readonly ProductionStageMachine _stage = new("가공");
    private readonly string _debugDir;
    private readonly MabinogiMobileCli? _cli;
    private string? _confirmedOnsiteFacility;
    private string? _cachedRecipeKey;
    private Point _cachedRecipeCenter;
    private bool _hasCachedRecipeCenter;
    private static readonly Rectangle Whole = new(0, 0, 800, 1000);
    private static readonly Rectangle Header = new(0, 15, 450, 110);
    private static readonly Rectangle Cards = new(20, 350, 760, 550);
    private static readonly Rectangle Popup = new(270, 585, 360, 45);
    private static readonly Rectangle CollectButton = new(0, 260, 170, 110);
    private static readonly Rectangle CollectVisualButton = new(10, 270, 110, 85);
    private static readonly Rectangle FacilityTravelDialog = new(120, 700, 560, 290);
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

    private Task<DetectionResult?> FindFacilityHeaderAsync(Bitmap frame, string title, CancellationToken ct)
        => _ui.Ocr.FindAlteringFacilityHeaderAsync(frame, title, ct);

    private async Task<bool> HasCollectPromptAsync(Bitmap frame, AlteringPlan plan, CancellationToken ct)
    {
        if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
            return false;

        // Never authorize a receive Space from the remote facility screen.
        // Do not OCR the small "설비로 이동" label: its fixed teal button body is
        // the state signal. OCR failure must never be interpreted as on-site.
        if (HasFacilityMoveButtonVisual(frame))
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
        // Detect the broad remote "설비로 이동" pill first. The close-X detector
        // separately validates real diagonal X geometry so remote currency digits
        // cannot mask a genuine move button.
        var roi = Rectangle.Intersect(
            AlteringFacilityLayout.MoveButtonVisualArea,
            new Rectangle(Point.Empty, frame.Size));
        if (roi.Width < 120 || roi.Height < 45)
            return false;

        int sampled = 0;
        int teal = 0;
        int minX = roi.Right, minY = roi.Bottom, maxX = roi.Left, maxY = roi.Top;

        for (int y = roi.Top; y < roi.Bottom; y += 2)
        for (int x = roi.Left; x < roi.Right; x += 2)
        {
            Color p = frame.GetPixel(x, y);
            sampled++;

            // Remote "설비로 이동" is a broad blue/teal pill.  The on-site
            // "모두 받기" control and slot rings are gray and fail the channel
            // separation below even when brightness varies.
            bool button = p.B >= 35 && p.G >= 30 && p.R <= 65 &&
                p.B >= p.R + 18 && p.G >= p.R + 10;
            if (!button)
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

        // The user-confirmed on-site screen has a top-right X. It is authoritative:
        // the left-side "모두 받기" control can overlap this move-button ROI and must
        // never win merely because it is a strong blue/teal shape.
        bool onsiteCloseVisible = HasOnsiteCloseButtonVisual(frame);
        return AlteringFacilityLayout.ShouldAcceptMoveButton(
            onsiteCloseVisible,
            moveShapeVisible: true);
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

    private async Task<bool> IsRecipeDetailStructureAsync(Bitmap frame, CancellationToken ct)
    {
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

    private async Task EnterFacilityAsync(AlteringPlan plan, CancellationToken ct)
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
                    _ui.TapFresh(0x01, ct);
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
                    Log?.Invoke($"[자동 가공] 현재 화면={otherFacility} · 가공 허브로 돌아갑니다 · Esc");
                    _ui.TapFresh(0x01, ct);
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
                _ui.TapFresh(0x25, ct);
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

        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 연속 등록 · 현장 상태 유지 확인 · 우측 상단 X 우선 판정 · 설비 이동 오탐 차단");
        return true;
    }

    private async Task<bool> TrySelectFixedRecipeAsync(
        AlteringPlan plan,
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

            if (HasFacilityMoveButtonVisual(frame))
                Fail(frame,
                    $"{plan.ScreenTitle} 설비로 이동 버튼이 남아 있어 원격 화면으로 판정했습니다. " +
                    "품목 고정좌표 입력 없이 정지합니다.");
        }

        Log?.Invoke(
            $"[자동 가공] {plan.ScreenTitle} 고정좌표 선택 · {plan.DisplayName} · " +
            $"순번 {plan.RecipeOrdinal}/{plan.RecipeCount} · ({center.X},{center.Y}) · 카드명 OCR 없음");
        _ui.ClickFresh(center, ct);
        await Task.Delay(350, ct);

        using var popup = Capture(ct);
        if (!await IsRecipeDetailStructureAsync(popup, ct))
            Fail(popup,
                $"{plan.ScreenTitle} 고정좌표 ({center.X},{center.Y}) 클릭 후 품목 상세 구조를 확인하지 못했습니다.");

        _stage.Move(ProductionStage.Detail, plan.DisplayName);
        Log?.Invoke(
            $"[자동 가공] {plan.ScreenTitle} · {plan.DisplayName} 고정좌표 상세 구조 확인 완료");
        return true;
    }

    private async Task<bool> TrySelectMedicineRecipeBySearchAsync(
        AlteringPlan plan,
        CancellationToken ct)
    {
        if (!string.Equals(plan.FacilityName, "약품 가공 시설", StringComparison.Ordinal))
            return false;

        if (!AlteringRecipeLayout.IsSafeMedicineSearchGeometry())
            throw new InvalidOperationException("약품 검색 고정좌표가 800x1000 안전 영역을 벗어났습니다.");

        using var beforeSearch = Capture(ct);
        if (await FindFacilityHeaderAsync(beforeSearch, plan.ScreenTitle, ct) is null)
            Fail(beforeSearch, "약품 검색 전 약품 가공 화면을 확인하지 못했습니다.");
        if (HasFacilityMoveButtonVisual(beforeSearch))
            Fail(beforeSearch,
                "약품 가공 설비로 이동 버튼이 남아 있어 원격 화면으로 판정했습니다. 검색 입력을 차단합니다.");

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

        _ui.ClickFresh(AlteringRecipeLayout.ProcessingSearchInputPoint, ct);
        _ui.PasteFresh(plan.DisplayName, ct);
        await Task.Delay(120, ct);
        _ui.TapFresh(0x1C, ct); // Enter
        Log?.Invoke($"[자동 가공] 약품 검색어 입력 확정 · Enter · {plan.DisplayName}");
        await Task.Delay(220, ct);

        using var beforeApply = Capture(ct);
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
            cardCandidate: true);

        Log?.Invoke(
            exact.Count > 0
                ? $"[자동 가공] 약품 검색 결과 · exact OCR 후보 {exact.Count}개 · 화면 변화 {resultRatio:P1}"
                : $"[자동 가공] 약품 검색 결과 · OCR 미검출 · 화면 변화 {resultRatio:P1}");

        if (exact.Count == 0 && resultRatio < AlteringRecipeLayout.ProcessingSearchResultChangeRatio)
            Fail(resultFrame,
                $"약품 검색 적용 후 결과 화면 전환을 확인하지 못했습니다. 변화율={resultRatio:P1}");

        if (plan.RecipeOrdinal != 1)
            Fail(resultFrame,
                $"약품 검색 결과가 중복 제법 순번 {plan.RecipeOrdinal}이라 첫 결과 고정좌표를 사용하지 않습니다.");

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

    private async Task SelectRecipeAsync(AlteringPlan plan, CancellationToken ct)
    {
        _stage.Move(ProductionStage.Search, plan.DisplayName);
        if (await TrySelectFixedRecipeAsync(plan, ct))
            return;
        if (await TrySelectMedicineRecipeBySearchAsync(plan, ct))
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

                if (HasFacilityMoveButtonVisual(frame))
                    Fail(frame, "연속 등록 중 원격 가공 화면이 감지되어 고정 품목 좌표 입력을 차단했습니다.");
            }

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
        var paid = await FindAsync(frame, RecipeActionButton, "가공하러 가기", ct);
        return (paid is not null, paid is not null ? "가공하러 가기 OCR" : "없음");
    }

    public async Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
    {
        plan.Validate();
        if (plan.AllowPaidButton)
            throw new InvalidOperationException("정령의 날개를 사용하는 가공 경로는 실행하지 않습니다.");

        // Restore the facility-first order that was used through V3.1.11:
        // open the processing facility, move to the real bench first, then select
        // the recipe. Never open a recipe detail before the first facility move.
        bool reusedOnsite = await TryReuseOnsiteFacilityAsync(plan, ct);
        if (reusedOnsite)
        {
            _stage.Move(ProductionStage.OpenHub, $"{plan.ScreenTitle} 현장 상태 재사용");
        }
        else
        {
            _confirmedOnsiteFacility = null;
            await EnterFacilityAsync(plan, ct);
            Log?.Invoke(
                $"[자동 가공] {plan.ScreenTitle} 진입 · 품목 선택 전 설비로 이동");

            // Re-check remote vs on-site state immediately before input. In a
            // consecutive batch the facility screen may already be on-site; never
            // force the fixed move coordinate merely because reuse validation failed.
            await TravelToFacilityAsync(plan, ct);

            await EnterFacilityAsync(plan, ct);
            _confirmedOnsiteFacility = plan.FacilityName;
            Log?.Invoke(
                $"[자동 가공] {plan.ScreenTitle} 설비 도착 · 이제 {plan.DisplayName} 선택");
        }

        await SelectRecipeAsync(plan, ct);

        // V3.1.8 execution model for every processing facility:
        // recipe selection performs the first detail confirmation, then the only
        // remaining registration guard is the two-fresh-frame action check below.
        // Do not add a separate duplicate detail/OCR pass here.
        Point visualActionCenter = Point.Empty;
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
                    $"현장버튼={(firstOnsiteAction ? "확인" : "없음")} · " +
                    "단일 프레임으로 중단하지 않고 200ms 후 재확인");
                await Task.Delay(200, ct);

                using var confirm = Capture(ct);
                if (!await IsRecipeDetailStructureAsync(confirm, ct))
                    Fail(confirm, "원격 상태 재확인 중 품목 상세 화면이 사라졌습니다.");

                bool secondOnsiteAction = TryFindFreeProcessButtonVisual(
                    confirm,
                    out Point secondOnsiteActionCenter);
                var remoteConfirmed = await DetectRemoteProcessStateAsync(confirm, ct);
                bool onsiteFacilityConfirmed = string.Equals(
                    _confirmedOnsiteFacility,
                    plan.FacilityName,
                    StringComparison.Ordinal);

                if (AlteringRemoteProcessGuard.ShouldBlock(
                        remoteCandidate.IsRemote,
                        remoteConfirmed.IsRemote,
                        onsiteFacilityConfirmed,
                        firstOnsiteAction,
                        secondOnsiteAction))
                    Fail(
                        confirm,
                        "원격 가공 상태가 2프레임 연속 감지되어 현장 가공 입력을 차단했습니다. " +
                        $"1차={remoteCandidate.Evidence} · 2차={remoteConfirmed.Evidence} · " +
                        $"현장버튼={firstOnsiteAction}/{secondOnsiteAction}");

                if (remoteCandidate.IsRemote &&
                    remoteConfirmed.IsRemote &&
                    onsiteFacilityConfirmed &&
                    firstOnsiteAction &&
                    secondOnsiteAction)
                {
                    visualActionCenter = secondOnsiteActionCenter;
                    Log?.Invoke(
                        "[자동 가공] 가공하러 가기 OCR 2프레임 오탐 억제 · " +
                        "해당 시설 현장 확인 + 하단 현장 가공 버튼 2프레임 우선 · 현장 가공 계속");
                }
                else
                {
                    Log?.Invoke(
                        $"[자동 가공] 작업 등록 직전 원격 상태 1회 오탐 해제 · " +
                        $"1차={remoteCandidate.Evidence} · 2차={(remoteConfirmed.IsRemote ? remoteConfirmed.Evidence : "없음")} · 현장 가공 계속");

                    if (!secondOnsiteAction)
                        Fail(confirm, "원격 상태 오탐 해제 후 하단 현장 가공 실행 버튼을 확인하지 못했습니다.");

                    visualActionCenter = secondOnsiteActionCenter;
                }
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
            Log?.Invoke("[자동 가공] 설비 도착 후 현장 가공 버튼 화면 확인 · 숫자/아이콘 무시 · 정령의 날개 버튼 입력 없음");
            _ui.ClickFresh(visualActionCenter, ct);
        }
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
        bool remoteConfirmed = false)
    {
        if (_cli is null)
            throw new InvalidOperationException("무료 설비 이동 상태 확인용 CLI가 연결되지 않았습니다.");
        if (!AlteringFacilityLayout.IsSafeMoveGeometry())
            throw new InvalidOperationException("설비로 이동 고정좌표가 800x1000 안전 영역을 벗어났습니다.");

        // Primary state signal is the fixed teal move-button body, not its text.
        // V3.1.8 proved that OCR can miss the visible label and must never turn
        // "OCR 없음" into "already on-site".
        int moveFrames = 0;
        int onsiteFrames = 0;

        if (!remoteConfirmed)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                using var frame = Capture(ct);

                if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
                    Fail(frame, "설비 이동 전 선택한 가공 시설 화면을 확인하지 못했습니다.");

                bool moveVisible = HasFacilityMoveButtonVisual(frame);
                if (!moveVisible)
                {
                    onsiteFrames++;
                    moveFrames = 0;
                    if (onsiteFrames >= 2)
                    {
                        Log?.Invoke(
                            $"[자동 가공] {plan.ScreenTitle} · 설비로 이동 버튼 화면 없음 2프레임 · 이미 현장 가공창");
                        return;
                    }
                }
                else
                {
                    moveFrames++;
                    onsiteFrames = 0;
                    if (moveFrames >= 2)
                        break;
                }

                await Task.Delay(180, ct);
            }

            if (moveFrames < 2)
            {
                using var failed = Capture(ct);
                Fail(failed, "설비 이동 버튼 화면 상태를 안정적으로 확인하지 못했습니다.");
            }
        }
        else
        {
            using var confirmed = Capture(ct);
            if (await FindFacilityHeaderAsync(confirmed, plan.ScreenTitle, ct) is null)
                Fail(confirmed, "원격 상태 복구 전 가공 시설 화면을 확인하지 못했습니다.");

            Log?.Invoke(
                $"[자동 가공] {plan.ScreenTitle} · 상세창 원격 상태로 설비 이동 확정 · " +
                "작은 설비로 이동 문구 OCR 생략");
        }

        _stage.Move(ProductionStage.Travel, $"{plan.ScreenTitle} 설비로 이동");
        Log?.Invoke(
            $"[자동 가공] {plan.ScreenTitle} · 설비로 이동 고정좌표 클릭 " +
            $"({AlteringFacilityLayout.MoveButtonPoint.X},{AlteringFacilityLayout.MoveButtonPoint.Y}) · OCR 없음");
        _ui.ClickFresh(AlteringFacilityLayout.MoveButtonPoint, ct);

        Log?.Invoke(
            $"[자동 가공] {plan.ScreenTitle} · 설비로 이동 클릭 완료 · 게임 자동이동 대기 · 추가 Space 입력 없음");

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
                sawDeparture = true;
                if (loadingCliRejects == 1 || loadingCliRejects % 5 == 0)
                    Log?.Invoke($"[자동 가공] 지역 이동 로딩 중 CLI 일시 거부 · 재시도 {loadingCliRejects}회");
            }
            else
            {
                _ = GatheringQueries.ParseActivity(activityResponse);
            }

            using var frame = Capture(ct);
            bool facilityVisible = await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null;
            bool moveVisible = facilityVisible && HasFacilityMoveButtonVisual(frame);

            if (!facilityVisible)
                sawDeparture = true;

            // Restore the V3.1.8 arrival rule. Some facilities transition directly
            // to the on-site state without exposing auto-travel/loading/departure.
            // In that valid path the facility window stays visible and only the
            // "설비로 이동" button disappears. Two stable frames are sufficient.
            if (facilityVisible &&
                !moveVisible &&
                activity?.IsAutoTraveling != true)
            {
                onsiteStableFrames++;
                if (onsiteStableFrames >= 2)
                {
                    Log?.Invoke(
                        $"[자동 가공] {plan.ScreenTitle} · 설비 도착 확인 · " +
                        "가공창 유지 + 설비로 이동 버튼 없음 2프레임 · 직접 전환 허용");
                    return;
                }
            }
            else
            {
                onsiteStableFrames = 0;
            }

            if (attempt > 0 && attempt % 10 == 0)
                Log?.Invoke(
                    $"[자동 가공] 설비 이동 대기 · {attempt / 2}초 · 이동감지={sawTravel} · " +
                    $"화면이탈/로딩={sawDeparture} · 가공창={facilityVisible} · " +
                    $"이동버튼화면={moveVisible} · CLI로딩거부={loadingCliRejects}");
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

    private async Task<int?> TryMatchingWorkCountAsync(
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
            .Count(x =>
                x.FacilityName == plan.FacilityName &&
                (x.DisplayName == plan.DisplayName || x.DisplayName == plan.OutputName));
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
        // The travel-confirm dialog and the real completion result both have a large
        // green button. Distinguish them by the travel wording inside the dialog.
        return await FindAsync(frame, FacilityTravelDialog, "설비로 이동", ct) is not null ||
               await FindAsync(frame, FacilityTravelDialog, "이동", ct) is not null;
    }

    private async Task<bool> WaitForReceiptFacilityReturnAsync(
        AlteringPlan plan,
        CancellationToken ct)
    {
        int facilityFrames = 0;
        for (int wait = 0; wait < 60; wait++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct);

            using var returned = Capture(ct);
            if (await FindFacilityHeaderAsync(returned, plan.ScreenTitle, ct) is not null)
            {
                facilityFrames++;
                if (facilityFrames >= 2)
                {
                    _confirmedOnsiteFacility = plan.FacilityName;
                    _stage.Move(ProductionStage.VerifyInventory, $"{plan.DisplayName} 수령 후 시설 복귀");
                    Log?.Invoke($"[자동 가공] 가공 완료 확인창 닫기 완료 · {plan.ScreenTitle} 창 복귀 확인");
                    return true;
                }
            }
            else
            {
                facilityFrames = 0;
            }
        }

        return false;
    }

    private async Task<bool> CloseCompletionResultAndWaitForFacilityAsync(
        AlteringPlan plan,
        CancellationToken ct,
        string reason)
    {
        Log?.Invoke($"[자동 가공] {reason} · 완료창 닫기 Space 1차 입력");
        _ui.TapFresh(0x39, ct);
        await Task.Delay(450, ct);

        using (var verify = Capture(ct))
        {
            bool facilityVisible = await FindFacilityHeaderAsync(
                verify, plan.ScreenTitle, ct) is not null;
            bool greenConfirm = HasBottomConfirmationModal(verify);
            bool travelDialog = greenConfirm &&
                await IsFacilityTravelDialogAsync(verify, ct);
            bool? autoTraveling = await TryAutoTravelingAsync(ct);

            if (AlteringReceiptPolicy.CanRetryCompletionClose(
                    greenConfirm,
                    facilityVisible,
                    travelDialog,
                    autoTraveling == true))
            {
                Log?.Invoke(
                    "[자동 가공] 완료창 닫기 Space 1차 입력 후에도 실제 완료창 유지 · " +
                    "이동 상태 아님 재확인 · Space 1회 재시도");

                await Task.Delay(180, ct);
                using var retryFrame = Capture(ct);
                bool retryFacilityVisible = await FindFacilityHeaderAsync(
                    retryFrame, plan.ScreenTitle, ct) is not null;
                bool retryGreenConfirm = HasBottomConfirmationModal(retryFrame);
                bool retryTravelDialog = retryGreenConfirm &&
                    await IsFacilityTravelDialogAsync(retryFrame, ct);
                bool? retryAutoTraveling = await TryAutoTravelingAsync(ct);

                if (AlteringReceiptPolicy.CanRetryCompletionClose(
                        retryGreenConfirm,
                        retryFacilityVisible,
                        retryTravelDialog,
                        retryAutoTraveling == true))
                {
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
            bool travelDialog = greenConfirm &&
                await IsFacilityTravelDialogAsync(closedCheck, ct);
            bool? autoTraveling = await TryAutoTravelingAsync(ct);

            if (AlteringReceiptPolicy.CanRetryCompletionClose(
                    greenConfirm,
                    facilityVisible,
                    travelDialog,
                    autoTraveling == true))
            {
                Fail(
                    closedCheck,
                    "가공 완료 확인창이 Space 2회 입력 후에도 그대로 남아 있어 추가 입력 없이 정지합니다.");
            }
        }

        return await WaitForReceiptFacilityReturnAsync(plan, ct);
    }

    private async Task<bool> ConfirmCompletionResultAsync(
        AlteringPlan plan,
        CancellationToken ct,
        int? receiptWorkCountBefore = null,
        int resultAttempts = 24)
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
                ? await TryMatchingWorkCountAsync(plan, ct)
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
                    _confirmedOnsiteFacility = plan.FacilityName;
                    Log?.Invoke(
                        $"[자동 가공] 첫 수령 Space 후 CLI 동일 품목 작업 감소로 수령 확정 · " +
                        $"{receiptBefore}->{receiptNow} · 시설창 복귀 확인");
                    return true;
                }

                bool travelDialogAfterReceipt = await IsFacilityTravelDialogAsync(frame, ct);
                bool? autoTravelAfterReceipt = await TryAutoTravelingAsync(ct);
                if (travelDialogAfterReceipt || autoTravelAfterReceipt == true)
                {
                    cliReceiptStableFrames = 0;
                    Log?.Invoke(
                        $"[자동 가공] CLI 수령 확정 후 완료창 닫기 대기 · 이동 상태라 Space 차단 · " +
                        $"이동팝업={travelDialogAfterReceipt} · AutoTraveling={autoTravelAfterReceipt == true}");
                    continue;
                }

                cliReceiptStableFrames++;
                if (cliReceiptStableFrames < 2)
                    continue;

                bool? freshTravelAfterReceipt = await TryAutoTravelingAsync(ct);
                if (freshTravelAfterReceipt == true)
                {
                    cliReceiptStableFrames = 0;
                    Log?.Invoke("[자동 가공] CLI 수령 확정 후 완료창 닫기 직전 AutoTraveling=true · Space 차단");
                    continue;
                }

                _stage.Move(ProductionStage.Complete, $"{plan.DisplayName} 수령 완료 화면");
                if (await CloseCompletionResultAndWaitForFacilityAsync(
                        plan,
                        ct,
                        $"첫 수령 Space 후 CLI 동일 품목 작업 감소로 수령 확정 · {receiptBefore}->{receiptNow}"))
                    return true;

                using var cliFailed = Capture(ct);
                Fail(
                    cliFailed,
                    "CLI로 수령은 확인했고 완료창 닫기 입력도 처리했지만 가공 시설 화면 복귀를 확인하지 못했습니다.");
            }

            bool travelDialogVisible = greenConfirm && await IsFacilityTravelDialogAsync(frame, ct);
            bool? autoTraveling = await TryAutoTravelingAsync(ct);

            bool canConfirm = AlteringReceiptPolicy.CanConfirmCompletion(
                greenConfirm,
                facilityVisible,
                travelDialogVisible,
                autoTraveling == true);

            if (canConfirm)
            {
                stableFrames++;
                if (stableFrames < 2)
                    continue;

                // One more activity read immediately before the only result-confirm
                // Space. If travel started between the two visual frames, input stays
                // blocked.
                bool? freshTravel = await TryAutoTravelingAsync(ct);
                if (freshTravel == true)
                {
                    stableFrames = 0;
                    Log?.Invoke("[자동 가공] 완료창 후보 감지 중 AutoTraveling=true · 추가 Space 차단");
                    continue;
                }

                int? workCountBefore = receiptWorkCountBefore ?? await TryMatchingWorkCountAsync(plan, ct);
                _stage.Move(ProductionStage.Complete, $"{plan.DisplayName} 수령 완료 화면");
                if (await CloseCompletionResultAndWaitForFacilityAsync(
                        plan,
                        ct,
                        "가공 완료 결과창 확인 · 이동 팝업/자동이동 아님 · " +
                        $"첫 수령 전 동일 품목 작업수={(workCountBefore?.ToString() ?? "확인불가")}"))
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

    public async Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        await EnterFacilityAsync(plan, ct);

        // Receiving must use the same location proof as new work registration.
        // If this is the remote facility screen, travel first and wait until the
        // facility title is visible with "설비로 이동" gone for two frames.
        Log?.Invoke($"[자동 가공] 완료품 수령 전 현장 가공대 확인 · {plan.ScreenTitle}");
        await TravelToFacilityAsync(plan, ct);
        await EnterFacilityAsync(plan, ct);
        _confirmedOnsiteFacility = plan.FacilityName;

        // Require two consecutive on-site receive confirmations. HasCollectPromptAsync
        // itself vetoes any frame where "설비로 이동" is visible.
        if (!await WaitForCollectPromptAsync(plan, attempts: 24, delayMs: 250, ct))
        {
            using var failed = Capture(ct);
            Fail(failed, "현장 가공대 도착 후 완료 작업 + 파란 수령 버튼을 제한 시간 안에 확인하지 못했습니다.");
        }

        int? receiptWorkCountBefore = await TryMatchingWorkCountAsync(plan, ct);

        _stage.Move(ProductionStage.Process, $"{plan.DisplayName} 완료 작업 수령");
        Log?.Invoke(
            $"[자동 가공] 현장 수령 화면 확인 · {plan.ScreenTitle} + 설비로 이동 없음 + CLI 완료 작업 + 파란 수령 버튼 · Space · " +
            $"수령 전 동일 품목 작업수={(receiptWorkCountBefore?.ToString() ?? "확인불가")}");
        _ui.TapFresh(0x39, ct);
        await Task.Delay(450, ct);

        if (!await ConfirmCompletionResultAsync(
                plan, ct, receiptWorkCountBefore: receiptWorkCountBefore, resultAttempts: 24))
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
        await TravelToFacilityAsync(plan, ct);
        await EnterFacilityAsync(plan, ct);
        if (!await WaitForCollectPromptAsync(plan, attempts: 90, delayMs: 500, ct))
        {
            Log?.Invoke("[자동 가공] 가공대 도착 후 CLI 완료 작업 + 파란 수령 버튼을 제한 시간 안에 확인하지 못했습니다.");
            return false;
        }

        int? receiptWorkCountBefore = await TryMatchingWorkCountAsync(plan, ct);

        _stage.Move(ProductionStage.Process, $"{plan.DisplayName} 완료 작업 2차 수령");
        Log?.Invoke(
            $"[자동 가공] 가공대 도착 확인 · {plan.ScreenTitle} + CLI 완료 작업 + 파란 수령 버튼 · 2차 Space · " +
            $"수령 전 동일 품목 작업수={(receiptWorkCountBefore?.ToString() ?? "확인불가")}");
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
