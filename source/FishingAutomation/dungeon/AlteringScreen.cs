using System.Diagnostics;
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
    private string? _selectedFixedRecipeKey;
    // Every managed K, including the first, needs positive ordinary-field proof.
    // Unknown->K navigation must
    // positively match 3/4 HUD templates on two fresh frames, or
    // K+I / Home+End pairs PLUS independent minimap + life-skill HUD evidence.
    // Both routes keep fresh modal/menu checks and CLI idle before any key.
    private bool _managedKInputIssued;
    private static readonly Rectangle Whole = new(0, 0, 800, 1000);
    private static readonly Rectangle Header = new(0, 15, 450, 110);
    private static readonly Rectangle Cards = new(20, 350, 760, 550);
    private static readonly Rectangle Popup = new(270, 585, 360, 45);
    private static readonly Rectangle CollectButton = new(0, 260, 170, 110);
    // Live V3.1.65 800x1000 frame: the blue \"모두 받기\" pill is
    // at x~23..107, y~258..285, not below y=270. Use the true fixed
    // button region, still ending before the first 100%-work circle.
    private static readonly Rectangle CollectVisualButton = new(15, 245, 100, 60);
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
        var unique = found.Count == 1 ? found[0] : (DetectionResult?)null;
        _ui?.NoteDiagnostic("OCR", text + "=" + (unique is not null ? "unique" : "unverified"));
        return unique;
    }

    // A previously confirmed facility uses its FIXED header position, never OCR.
    // This is a visual location check, not a guess based on a stale OCR result.
    private Task<DetectionResult?> FindFacilityHeaderAsync(Bitmap frame, string title, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // A world name/currency row can occupy both fixed header anchors.
        // Veto before cached proof AND OCR, so neither can bypass field evidence.
        if (HasWorldHudForFacilityVeto(frame))
            return Task.FromResult<DetectionResult?>(null);
        if (string.Equals(_repeatOcrFreeFacilityTitle, title, StringComparison.Ordinal) ||
            string.Equals(_postTravelProvenFacilityTitle, title, StringComparison.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            DetectionResult? fixedHit = HasFixedFacilityHeaderVisual(frame)
                ? new DetectionResult(true, new Rectangle(14, 42, 155, 45), 1.0, title)
                : null;
            _ui?.NoteDiagnostic("visual_header", title + "=" + (fixedHit is not null ? "verified" : "missing"));
            return Task.FromResult(fixedHit);
        }
        _ui?.NoteDiagnostic("header_check", title + "=OCR");
        return _ui.Ocr.FindAlteringFacilityHeaderAsync(frame, title, ct);
    }

    private static bool HasWorldHudForFacilityVeto(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000) return false;
        // Gold minimap + life-skill disc is the existing independent field
        // evidence. Underground gray minimaps instead retain the three exact
        // Home/End/ESC glyphs; use the existing production template matcher.
        // This is a negative facility veto, never permission to send input.
        return HasManagedKEntryFieldHudVisual(frame) ||
               DetectManagedWorldHudKeys(frame).Found;
    }

    // The "전체" search pill and THREE physically separated dark item cards
    // are present on both captured onsite and remote facility screens.
    // A world HUD may imitate top-left text and even a close-X detector, so
    // require these additional independent layout anchors before a move click.
    // This is never a travel/receipt authorization on its own.
    private static bool HasFacilityPanelStructureVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000) return false;

        int capsuleWhite = 0;
        for (int y = 106; y < 130; y += 3)
        for (int x = 75; x < 124; x += 3)
        {
            var p = frame.GetPixel(x, y);
            if (Math.Min(p.R, Math.Min(p.G, p.B)) >= 182 &&
                Math.Max(p.R, Math.Max(p.G, p.B)) -
                Math.Min(p.R, Math.Min(p.G, p.B)) <= 40)
                capsuleWhite++;
        }
        if (capsuleWhite < 32) return false;

        static int DarkCardSamples(Bitmap image, Rectangle area)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y += 4)
            for (int x = area.Left; x < area.Right; x += 4)
            {
                var p = image.GetPixel(x, y);
                if (Math.Max(p.R, Math.Max(p.G, p.B)) <= 74 &&
                    Math.Max(p.R, Math.Max(p.G, p.B)) -
                    Math.Min(p.R, Math.Min(p.G, p.B)) <= 48)
                    count++;
            }
            return count;
        }
        // Three separate product-card interiors (onsite and remote fixtures).
        return DarkCardSamples(frame, new Rectangle(178, 383, 83, 95)) >= 260 &&
               DarkCardSamples(frame, new Rectangle(300, 383, 83, 95)) >= 260 &&
               DarkCardSamples(frame, new Rectangle(421, 383, 83, 95)) >= 260;
    }

    // Both onsite and remote panels have seven horizontally repeated work
    // rings, even when the teal remote-move pill has not rendered clearly.
    // This is a third INDEPENDENT structural signal, not permission to click
    // outside the full facility header + category pill + three item cards.
    private static bool HasFacilityQueueSlotStructureVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000) return false;
        static int Light(Color p) => (p.R + p.G + p.B) / 3;
        // Real onsite / remote slot centers, respectively.
        foreach (int centerY in new[] { 275, 309 })
        {
            int rings = 0;
            foreach (int centerX in new[] { 153, 230, 307, 384, 461, 538, 615 })
            {
                int edges = 0;
                foreach (var (dx, dy) in new (int, int)[] {
                    (-32, 0), (32, 0), (0, -32), (0, 32)
                })
                {
                    var rim = frame.GetPixel(centerX + dx, centerY + dy);
                    var outside = frame.GetPixel(
                        centerX + dx * 46 / 32, centerY + dy * 46 / 32);
                    if (Light(rim) >= 40 && Light(rim) >= Light(outside) + 13)
                        edges++;
                }
                if (edges >= 2) rings++;
            }
            if (rings >= 3) return true;
        }
        return false;
    }

    // The move command is a fixed-coordinate input. A white title/subtitle
    // (or OCR/cached title) alone must never authorize a click: the 03:33
    // ordinary field had both bright anchors. Require an independent facility
    // UI control in the same latest frame; the true remote pill and onsite
    // close X both count, and neither requires an unreliable move-button OCR.
    // This is a pre-input guard, not proof that travel/arrival completed.
    private static bool HasVerifiedFacilityMoveClickScreen(Bitmap frame)
        => HasFixedFacilityHeaderVisual(frame) &&
           HasFacilityPanelStructureVisual(frame) &&
           !HasFacilityTravelConfirmationVisual(frame) &&
           !HasBottomConfirmationModal(frame) &&
           (HasFacilityMoveButtonVisual(frame) ||
            HasOnsiteCloseButtonVisual(frame) ||
            HasFacilityQueueSlotStructureVisual(frame));

    // A header can render before the rest of the facility panel. Observe only;
    // never send K/Esc/card/move input while waiting for this transition.
    private static int AdvanceFacilityReadyFrames(int previous, bool verified)
        => verified ? Math.Min(previous + 1, 2) : 0;

    private async Task<bool> WaitForStableFacilityMoveScreenAsync(CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        int consecutive = 0;
        while (elapsed.ElapsedMilliseconds < 2000)
        {
            ct.ThrowIfCancellationRequested();
            using (var frame = Capture(ct))
                consecutive = AdvanceFacilityReadyFrames(consecutive,
                    HasVerifiedFacilityMoveClickScreen(frame));
            if (consecutive >= 2)
            {
                Log?.Invoke("[자동 가공] 시설 전체 UI 연속 2프레임 확인 · 이동 전 화면 전환 대기 완료");
                return true;
            }
            await Task.Delay(150, ct);
        }
        return false;
    }

    // The actual 800x1000 facility capture has the large fixed title at
    // x=20,y=50 and a separate fixed 'facility Lv.' subtitle at x=20,y=155.
    // Neither is a dynamic OCR coordinate. Require BOTH independent anchors:
    // a single unrelated title or bright currency cannot prove this screen.
    private static bool HasFixedFacilityHeaderVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return false;
        // A six-card hub can also contain bright text at the same top-left
        // coordinates; never let it impersonate a single facility view.
        if (HasFixedProcessingHubVisual(frame)) return false;
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
               WhiteGlyphs(frame, new Rectangle(15, 144, 230, 48)) >= 20 &&
               !HasWorldHudForFacilityVeto(frame);
    }


    // The recorded 13:20 V3.1.62 failure shows the FULL six-card processing hub
    // already open, while OCR still sees '가공' in the persistent bottom nav.
    // Distinguish the real hub by its upper title AND two separate card rows;
    // a bottom tab alone is NEVER sufficient evidence of a K-menu overlay.
    private static bool HasFixedProcessingHubVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000) return false;

        static int WhiteGlyphSamples(Bitmap bitmap, Rectangle area)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y += 2)
            for (int x = area.Left; x < area.Right; x += 2)
            {
                Color p = bitmap.GetPixel(x, y);
                int max = Math.Max(p.R, Math.Max(p.G, p.B));
                int min = Math.Min(p.R, Math.Min(p.G, p.B));
                if (min >= 160 && max - min <= 50) count++;
            }
            return count;
        }

        // Actual 800x1000 hub: title '가공' at upper-left, three dark
        // facility cards per row and their large white titles at y~260/605.
        if (WhiteGlyphSamples(frame, new Rectangle(56, 43, 74, 39)) < 9)
            return false;

        Rectangle[] cardTitleAreas =
        {
            new(92, 239, 146, 51), new(334, 239, 146, 51),
            new(576, 239, 146, 51), new(92, 584, 146, 51),
            new(334, 584, 146, 51), new(576, 584, 146, 51)
        };
        int topTitles = 0, bottomTitles = 0, darkCards = 0;
        int[] cardCenterX = { 75, 315, 557 };
        for (int index = 0; index < cardTitleAreas.Length; index++)
        {
            if (WhiteGlyphSamples(frame, cardTitleAreas[index]) >= 10)
            {
                if (index < 3) topTitles++;
                else bottomTitles++;
            }
            int y = index < 3 ? 405 : 748;
            Color bg = frame.GetPixel(cardCenterX[index % 3], y);
            if (bg.R <= 70 && bg.G <= 70 && bg.B <= 75)
                darkCards++;
        }
        // Multi-point layout evidence, not OCR or the always-visible K tab.
        return topTitles >= 2 && bottomTitles >= 2 && darkCards >= 5;
    }

    private async Task<bool> IsProcessingHubAsync(Bitmap frame, CancellationToken ct)
    {
        if (HasFixedProcessingHubVisual(frame)) return true;
        // The single-facility screen also has a large top-left title and
        // facility-level subtitle; don't treat it as the hub via fuzzy OCR.
        if (HasFixedFacilityHeaderVisual(frame)) return false;
        return await FindFacilityHeaderAsync(frame, "가공", ct) is not null;
    }

    private async Task<bool> HasCollectPromptAsync(
        Bitmap frame,
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
    {
        // Managed F9 receipt uses authoritative CLI whole-lane completion
        // and the verified facility screen, NOT the blue button colour.
        // Legacy single-altering still uses the original blue-button guard.
        if (directive != AlteringFacilityEntryDirective.Automatic)
            return await HasManagedCollectReadyAsync(frame, plan, ct);

        if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null)
            return false;

        bool visualMoveButton = HasFacilityMoveButtonVisual(frame);
        bool exactMoveLabelVisible = false;
        if (visualMoveButton)
        {
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
                directive, trustedOnsiteFacility, visualMoveButton, exactMoveLabelVisible))
            return false;

        if (_cli is null)
            return false;

        var worksResponse = await _cli.GetAlteringWorksAsync(ct);
        if (!worksResponse.Success)
        {
            if (CliAutomationGuards.IsTransientLoadingRejection(worksResponse))
                return false;
            _ = AlteringQueries.ParseWorks(worksResponse);
        }

        var currentWorks = AlteringQueries.ParseWorks(worksResponse);
        if (!currentWorks.Any(x => x.FacilityName == plan.FacilityName && x.IsCompleted))
            return false;

        return HasCollectButtonVisual(frame);
    }

    // V3.1.74 F9 ONLY: once the coordinator has proved physical arrival
    // (FreshMoveRequired or ReuseCoordinatorConfirmedOnsite), never gate the
    // irreversible receive Space on blue pixels at fixed coordinates.
    // This independently refreshes facility UI, popup, activity and ALL
    // jobs in the exact facility. Fail closed on unknown CLI or partial work.
    private async Task<bool> HasManagedCollectReadyAsync(
        Bitmap frame, AlteringPlan plan, CancellationToken ct)
    {
        bool facility = await FindFacilityHeaderAsync(
            frame, plan.ScreenTitle, ct) is not null;
        bool confirmation = HasBottomConfirmationModal(frame) ||
                            HasManagedFacilityTravelConfirmationVisual(frame);
        bool travelDialog = await IsFacilityTravelDialogAsync(frame, ct);
        bool? moving = await TryAutoTravelingAsync(ct);

        int? worksCount = null;
        int? completeCount = null;
        bool allCompleted = false;
        if (_cli is not null)
        {
            var response = await _cli.GetAlteringWorksAsync(ct);
            if (response.Success)
            {
                var works = AlteringQueries.ParseWorks(response);
                var facilityWorks = works.Where(x =>
                    x.FacilityName == plan.FacilityName).ToArray();
                worksCount = facilityWorks.Length;
                completeCount = facilityWorks.Count(x => x.IsCompleted);
                allCompleted = AlteringReceiptPolicy.CanCollectManagedFacility(
                    works, plan.FacilityName);
            }
            else if (!CliAutomationGuards.IsTransientLoadingRejection(response))
            {
                _ = AlteringQueries.ParseWorks(response);
            }
        }

        bool approved = facility && !confirmation && !travelDialog &&
                        moving == false && allCompleted;
        Log?.Invoke(
            $"[다중가공][수령 검사] {plan.ScreenTitle} · 시설창={facility} · " +
            $"팝업={confirmation} · 이동팝업={travelDialog} · " +
            $"CLI이동={moving?.ToString() ?? "불명"} · " +
            $"CLI전체완료={completeCount?.ToString() ?? "불명"}/" +
            $"{worksCount?.ToString() ?? "불명"} · " +
            $"파란버튼픽셀=검사안함 · Space허가={approved}");
        return approved;
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
        => MeasureFacilityMoveButtonVisual(frame).Accepted;

    private readonly record struct FacilityMoveButtonVisualEvidence(
        bool Accepted, bool Shape, bool Anchor, double BoundedAnchorRatio);

    private static FacilityMoveButtonVisualEvidence MeasureFacilityMoveButtonVisual(Bitmap frame)
    {
        // The 2026-10-23 leather/metal backgrounds satisfy both legacy colour
        // gates. Prove a BOUNDED pill too: teal at the fixed anchor must be
        // brighter than the background both above AND below that same column.
        // A flat fill or monotonic gradient cannot supply those two edges.
        // The top-right X is advisory only because the 17:21 live failure showed
        // currency digits can mimic it and incorrectly erase a genuine move button.
        if (frame.Width != 800 || frame.Height != 1000)
            return default;
        var bounds = new Rectangle(Point.Empty, frame.Size);
        var roi = Rectangle.Intersect(
            AlteringFacilityLayout.MoveButtonVisualArea,
            bounds);
        var anchor = Rectangle.Intersect(
            AlteringFacilityLayout.MoveButtonAnchorArea,
            bounds);
        if (roi.Width < 120 || roi.Height < 45 ||
            anchor.Width < 80 || anchor.Height < 24)
            return default;

        static bool IsMoveTeal(Color p)
            => p.B >= 35 && p.G >= 30 && p.R <= 65 &&
               p.B >= p.R + 18 && p.G >= p.R + 10 &&
               p.G * 4 >= p.B * 3;

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
            return default;

        int width = maxX - minX;
        int height = maxY - minY;
        bool moveShape = width >= 90 && height >= 18;
        if (!moveShape)
            return default;

        int anchorSampled = 0;
        int anchorTeal = 0;
        int boundedAnchorTeal = 0;
        for (int y = anchor.Top; y < anchor.Bottom; y += 2)
        for (int x = anchor.Left; x < anchor.Right; x += 2)
        {
            anchorSampled++;
            Color p = frame.GetPixel(x, y);
            if (!IsMoveTeal(p))
                continue;
            anchorTeal++;

            // Sample outside the fixed pill, not a distant unrelated UI item.
            // Require both colour channels and both edges in the SAME column.
            Color above = frame.GetPixel(x, roi.Top);
            Color below = frame.GetPixel(x, roi.Bottom - 1);
            if (p.G >= above.G + 12 && p.B >= above.B + 12 &&
                p.G >= below.G + 12 && p.B >= below.B + 12)
                boundedAnchorTeal++;
        }

        // Text/antialiasing may interrupt the fill. A sizeable bounded anchor
        // must remain; sparse slot-ring fragments are not a remote control.
        bool moveAnchor = anchorSampled > 0 &&
            anchorTeal * 100 >= anchorSampled * 18;
        bool boundedAnchor = anchorSampled > 0 &&
            boundedAnchorTeal * 100 >= anchorSampled * 18;

        bool onsiteCloseVisible = HasOnsiteCloseButtonVisual(frame);
        bool accepted = boundedAnchor && AlteringFacilityLayout.ShouldAcceptMoveButton(
            onsiteCloseVisible,
            moveShapeVisible: moveShape,
            moveAnchorVisible: moveAnchor);
        return new(accepted, moveShape, moveAnchor,
            anchorSampled == 0 ? 0.0 : (double)boundedAnchorTeal / anchorSampled);
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
        // An already-selected exact fixed-grid card is identified by its
        // original recipe ordinal + click coordinate. OCR is not an identity
        // gate for that known location, even on the first registration.
        if (_repeatOcrFreeRecipe ||
            string.Equals(_selectedFixedRecipeKey, RecipeCacheKey(plan), StringComparison.Ordinal))
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
        if (_repeatOcrFreeRecipe || _selectedFixedRecipeKey is not null)
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
    // V3.1.72: Only F9 managed K navigation uses the verified 800x1000
    // production navigation bar. The genuine 13:20 hub screenshot has three
    // independent bottom-nav labels (빠른 제작 / 가공 / 연금술); the 06:15 world
    // screenshot instead has a dark chat strip and NO left/right nav labels.
    // The actual six-card hub and individual facility are handled earlier.
    // This is a positive visual gate, never an inference from CLI work counts.
    private static bool HasManagedProcessingBottomTabVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000 ||
            HasFixedProcessingHubVisual(frame) ||
            HasFixedFacilityHeaderVisual(frame))
            return false;

        static int NeutralGlyphs(Bitmap image, Rectangle roi)
        {
            int count = 0;
            for (int y = roi.Top; y < roi.Bottom; y += 2)
            for (int x = roi.Left; x < roi.Right; x += 2)
            {
                Color p = image.GetPixel(x, y);
                int max = Math.Max(p.R, Math.Max(p.G, p.B));
                int min = Math.Min(p.R, Math.Min(p.G, p.B));
                if (min >= 95 && max - min <= 45)
                    count++;
            }
            return count;
        }

        // Side labels are required: the center "가공" text alone is also
        // visible on the hub and may be erroneously read over a field HUD.
        return NeutralGlyphs(frame, new Rectangle(222, 940, 91, 45)) >= 8 &&
               NeutralGlyphs(frame, new Rectangle(410, 940, 82, 45)) >= 8 &&
               NeutralGlyphs(frame, new Rectangle(496, 940, 85, 45)) >= 8;
    }

    private async Task<bool> TrySelectManagedProcessingTabAsync(
        Bitmap first, AlteringPlan plan,
        AlteringFacilityEntryDirective directive, CancellationToken ct)
    {
        if (directive == AlteringFacilityEntryDirective.Automatic ||
            !HasManagedProcessingBottomTabVisual(first))
            return false;
        // A positive two-frame bar detection replaces THREE repeated OCR
        // operations (menu scan + ClickLabel first/second) on normal F9 screens.
        // The fixed "가공" tab anchor is from the real 800x1000 K-nav screenshot.
        await Task.Delay(160, ct);
        using var second = Capture(ct);
        if (!HasManagedProcessingBottomTabVisual(second))
            return false;
        if (HasManagedKEntryBlockingModal(first) ||
            HasManagedKEntryBlockingModal(second))
            Fail(second, "F9 가공 탭 클릭 전 확인창 패널/버튼 감지 · 입력 차단");

        await RequireManagedIdleAsync(plan, directive, "F9 가공 탭 고정 네비 클릭 직전", ct);
        _ui.ClickFresh(new Point(360, 948), ct);
        Log?.Invoke("[자동 가공] F9 CLI 유휴+네비 2프레임 확인 · 가공 탭 (360,948) · 반복 OCR 없이 1회 클릭");
        await Task.Delay(550, ct);
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

    private Task<bool> WaitForProcessingHubAsync(int timeoutMs, CancellationToken ct)
        => WaitForScreenStateAsync(
            frame => IsProcessingHubAsync(frame, ct),
            timeoutMs, ct);

    // F9 only: menu-ready polling is visual-only; never re-run OCR for
    // "가공" during the transient K-menu animation. The next step separately
    // validates the correct facility title/target and CLI idle authority.
    private Task<bool> WaitForManagedProcessingNavigationReadyAsync(
        int timeoutMs, CancellationToken ct)
        => WaitForScreenStateAsync(
            frame => Task.FromResult(
                HasFixedFacilityHeaderVisual(frame) ||
                HasFixedProcessingHubVisual(frame) ||
                HasManagedProcessingBottomTabVisual(frame)),
            timeoutMs, ct);

    private Task<bool> WaitForProcessingNavigationReadyAsync(
        AlteringPlan plan,
        int timeoutMs,
        CancellationToken ct)
        => WaitForScreenStateAsync(
            async frame =>
            {
                if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null)
                    return true;
                if (await IsProcessingHubAsync(frame, ct))
                    return true;
                return await FindAsync(frame, new(180, 880, 420, 120), "가공", ct) is not null;
            },
            timeoutMs,
            ct);

    // Fixed title/level pixels prove the facility layout, not its identity.
    // Never carry the previous facility's OCR-free visual authority into a
    // different requested facility. Same-facility repeats retain the cache.
    private void InvalidateForeignFacilityProof(string requestedTitle)
    {
        bool staleRepeat = _repeatOcrFreeFacilityTitle is not null &&
            !string.Equals(_repeatOcrFreeFacilityTitle, requestedTitle, StringComparison.Ordinal);
        bool staleTravel = _postTravelProvenFacilityTitle is not null &&
            !string.Equals(_postTravelProvenFacilityTitle, requestedTitle, StringComparison.Ordinal);
        if (staleRepeat) _repeatOcrFreeFacilityTitle = null;
        if (staleTravel) _postTravelProvenFacilityTitle = null;
        if (staleRepeat || staleTravel)
            Log?.Invoke($"[자동 가공] 시설 변경 · {requestedTitle} 진입 전 이전 시설 인식 캐시 해제");
    }

    private async Task EnterFacilityAsync(
        AlteringPlan plan, CancellationToken ct,
        AlteringFacilityEntryDirective directive = AlteringFacilityEntryDirective.Automatic)
    {
        try
        {
            await EnterFacilityCoreAsync(plan, ct, directive);
        }
        catch (InvalidOperationException ex)
        {
            // This method only navigates; no registration or receipt input.
            // Preserve diagnostics without claiming a possibly applied batch.
            throw new AlteringFacilityNavigationException(ex.Message, ex);
        }
    }

    private async Task EnterFacilityCoreAsync(
        AlteringPlan plan, CancellationToken ct,
        AlteringFacilityEntryDirective directive = AlteringFacilityEntryDirective.Automatic)
    {
        InvalidateForeignFacilityProof(plan.ScreenTitle);
        _stage.Move(ProductionStage.OpenHub, $"{plan.ScreenTitle} 진입");
        // Three ordinary navigation passes may end just after K has
        // reopened the six-card hub. Allow ONE extra pass only if that
        // hub is positively verified; never grant a fourth blind K/Esc.
        const int maxAttempts = 4;
        int hubCardClicks = 0; // At most one retry, at the unchanged fixed point.
        bool recoveredHubAfterK = false; // Only K issued after THIS target-card click.
        bool foodNavigationIssued = false;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using (var frame = Capture(ct))
            {
                // Before ANY fourth-pass UI input (including food navigation),
                // require a verified target facility OR the incident's proven
                // no-modal six-card hub after one prior card click and managed K.
                // This prevents a fourth K/Esc/tab click on an unknown screen.
                if (attempt == maxAttempts)
                {
                    if (HasVerifiedFacilityMoveClickScreen(frame) &&
                        await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null)
                    {
                        Log?.Invoke($"[자동 가공] 마지막 허브 복구 전 시설 진입 확인 · {plan.ScreenTitle}");
                        return;
                    }
                    if (!AlteringFacilityLayout.CanRecoverFinalHubSelection(
                            recoveredHubAfterK, hubCardClicks,
                            HasFixedProcessingHubVisual(frame),
                            HasBottomConfirmationModal(frame) ||
                            await IsFacilityTravelDialogAsync(frame, ct)))
                        Fail(frame,
                            "마지막 K 재진입 후 동일 가공 허브/팝업 상태를 확인하지 못했습니다. 추가 K/시설 클릭 없이 정지합니다.");
                }

                // Food quick-crafting has a cyan left tab, so the neutral-only
                // K navigation detector cannot identify it. Classify it BEFORE
                // the unknown-field/first-K path. No guessed Esc/K is needed.
                if (directive != AlteringFacilityEntryDirective.Automatic &&
                    await TrySelectManagedFoodCraftingTabAsync(frame, plan, directive,
                        foodNavigationIssued, ct))
                {
                    foodNavigationIssued = true;
                    await WaitForManagedProcessingNavigationReadyAsync(1000, ct);
                    continue;
                }
                if (HasVerifiedFacilityMoveClickScreen(frame) &&
                    await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is not null)
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

                // A positively identified six-card hub has priority over the
                // persistent '가공' bottom navigation label. Never send the K
                // menu click in a hub; use the unchanged fixed facility point.
                if (await IsProcessingHubAsync(frame, ct))
                {
                    Log?.Invoke($"[자동 가공] 현재 화면=가공 허브 · 시설 진입 시도 {attempt}/{maxAttempts}");
                    if (directive != AlteringFacilityEntryDirective.Automatic &&
                        (HasBottomConfirmationModal(frame) ||
                         await IsFacilityTravelDialogAsync(frame, ct)))
                        Fail(frame, "가공 허브 시설 선택 전 확인/이동 팝업 감지 · 입력 차단");
                    await RequireManagedIdleAsync(plan, directive, "시설명 메뉴 선택 직전", ct);
                    // The fixed six-card layout determines the selected point.
                    // OCR is not a prerequisite for retrying that same point.
                    if (!HasFixedProcessingHubVisual(frame))
                        Fail(frame, "가공 허브 6카드 구조 확인 실패 · 시설 선택 입력 차단");
                    if (hubCardClicks >= 2)
                        Fail(frame, "시설 카드 선택 후 화면 전환 2회 실패 · 추가 클릭 없이 안전 정지");
                    Rectangle titleArea = AlteringFacilityLayout.TitleArea(plan.ScreenTitle);
                    Point titleCenter = new(titleArea.Left + titleArea.Width / 2,
                        titleArea.Top + titleArea.Height / 2);
                    Point click = titleCenter;
                    if (hubCardClicks > 0)
                    {
                        await Task.Delay(180, ct);
                        using var freshHub = Capture(ct);
                        if (!HasFixedProcessingHubVisual(freshHub) ||
                            HasBottomConfirmationModal(freshHub) ||
                            await IsFacilityTravelDialogAsync(freshHub, ct))
                            Fail(freshHub, "시설 카드 재시도 전 허브/팝업 새 화면 검증 실패 · 추가 클릭 차단");
                        // Both captures must still prove the popup-free hub.
                        // A transition, facility page or modal cannot authorize retry.
                        if (!AlteringFacilityLayout.CanRetryFixedHubSelection(
                                hubCardClicks, HasFixedProcessingHubVisual(frame),
                                HasFixedProcessingHubVisual(freshHub),
                                HasManagedKEntryBlockingModal(frame) ||
                                HasManagedKEntryBlockingModal(freshHub)))
                            Fail(freshHub, "시설 카드 재시도 조건 미충족 · 추가 클릭 차단");
                    }
                    await RequireManagedIdleAsync(plan, directive, "시설 카드 클릭 직전 CLI 재검증", ct);
                    _ui.ClickFresh(click, ct);
                    hubCardClicks++;
                    Log?.Invoke($"[자동 가공] {plan.ScreenTitle} 시설 카드 클릭 {hubCardClicks}/2 · 좌표=({click.X},{click.Y}) · " +
                        (hubCardClicks == 1 ? "고정 카드 중심" : "새 허브 2프레임 확인 + 기존 고정 좌표 재시도"));
                    await Task.Delay(550, ct);

                    // The chosen facility card was positively clicked in
                    // the fixed hub. A fixed facility screen (title + level)
                    // is sufficient; do not require another title OCR pass.
                    int stableFacilityFrames = 0;
                    if (await WaitForScreenStateAsync(async image =>
                        {
                            bool facility = HasVerifiedFacilityMoveClickScreen(image) &&
                                !HasManagedKEntryBlockingModal(image) &&
                                (HasFixedFacilityHeaderVisual(image) ||
                                 await FindFacilityHeaderAsync(image, plan.ScreenTitle, ct) is not null);
                            stableFacilityFrames = facility ? stableFacilityFrames + 1 : 0;
                            return stableFacilityFrames >= 2;
                        }, 3000, ct))
                    {
                        Log?.Invoke($"[자동 가공] 시설 진입 성공 {attempt}/{maxAttempts} · {plan.ScreenTitle} · 고정 화면 위치 확인");
                        return;
                    }

                    Log?.Invoke($"[자동 가공] 시설 전환 확인 대기 {attempt}/{maxAttempts} · 현재 화면을 다시 판정합니다.");
                    await WaitForProcessingNavigationReadyAsync(plan, 900, ct);
                    continue;
                }

                // A K menu may overlay the previous facility title. Prefer its
                // explicit bottom entry; another Esc/K would toggle the wrong UI.
                // CLI remains the authority for permitted input. On F9, a
                // positively identified bottom-navigation bar is clicked using
                // the two-frame geometry guard instead of repeatedly OCR'ing
                // the static tab text. A real field HUD skips the menu OCR
                // entirely and goes directly to the existing safe K path.
                if (await TrySelectManagedProcessingTabAsync(frame, plan, directive, ct))
                {
                    await WaitForProcessingHubAsync(900, ct);
                    continue;
                }
                // Managed F9 must never interpret unrelated world OCR "가공"
                // as a K navigation menu before the first K. On a later
                // re-entry, permit the existing OCR fallback ONLY when the
                // real processing bottom bar is positively observed.
                bool mayUseMenuOcr = directive == AlteringFacilityEntryDirective.Automatic ||
                    (_managedKInputIssued && HasManagedProcessingBottomTabVisual(frame));
                if (mayUseMenuOcr &&
                    await FindAsync(frame, new(180, 880, 420, 120), "가공", ct) is not null)
                {
                    // A different bottom confirmation or travel overlay must never
                    // be mistaken for an actionable K menu entry.
                    if (directive != AlteringFacilityEntryDirective.Automatic &&
                        HasManagedKEntryBlockingModal(frame))
                        Fail(frame, "K 메뉴 가공 항목 클릭 전 확인/이동 팝업 감지 · 입력 차단");
                    Log?.Invoke("[자동 가공] K 메뉴 가공 항목 감지 · K 중복 입력 생략");
                    await RequireManagedIdleAsync(plan, directive, "K 메뉴 가공 선택 직전", ct);
                    if (await ClickLabelAsync("가공", new(180, 880, 420, 120), null, ct))
                        await WaitForProcessingHubAsync(900, ct);
                    else
                        Log?.Invoke("[자동 가공] K 메뉴 가공 항목 재확인 실패 · 클릭 없이 재판정");
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
                    // Require a NEW frame before sending Esc on a foreign title.
                    await Task.Delay(130, ct);
                    using (var confirmedOther = Capture(ct))
                    {
                        if (await FindFacilityHeaderAsync(confirmedOther, otherFacility, ct) is null)
                        {
                            Log?.Invoke($"[자동 가공] 이전 시설 {otherFacility} 단일 프레임 인식 · Esc 생략");
                            continue;
                        }
                    }
                    Log?.Invoke($"[자동 가공] 현재 화면={otherFacility} · 2프레임 확인 · 가공 허브로 돌아갑니다 · Esc");
                    await RequireManagedIdleAsync(plan, directive, "시설 상세/다른창 Esc 직전", ct);
                    _ui.TapFresh(0x01, ct);
                    await WaitForProcessingHubAsync(900, ct);
                    continue;
                }

                // Unknown after collection can be a transient reward/result screen or the
                // ordinary field. Do one bounded K re-entry only; never spam keys blindly.
                // A fourth pass is reserved exclusively for the visually
                // confirmed six-card hub above; no extra K is ever permitted.
                Log?.Invoke($"[자동 가공] 현재 화면=일반/전환 중 · 가공 메뉴 재진입 시도 {attempt}/{maxAttempts}");
                // Never press K over an unknown green/travel modal in a managed run.
                if (directive != AlteringFacilityEntryDirective.Automatic &&
                    HasManagedKEntryBlockingModal(frame))
                    Fail(frame, "관리 가공 K 메뉴 진입 전 확인창/이동창 감지 · 입력 차단");
                Func<Bitmap, Task<bool>>? verifyManagedK = null;
                if (directive != AlteringFacilityEntryDirective.Automatic)
                {
                    // First entry and re-entry both require actual consecutive
                    // field frames. The 14:16:48
                    // V3.1.99 live field had K/I=0.969/0.910 yet Home/End=0.37/0.39,
                    // so also accept two consecutive K+I plus the independent
                    // minimap-rim and lower-left life-skill HUD visual. Neither
                    // path relaxes the popup/menu veto or last-moment CLI idle.
                    // Limit remains 2 seconds; no unverified extra K is sent.
                    bool firstEntry = !_managedKInputIssued;
                    var started = Stopwatch.StartNew();
                    var previousHud = DetectManagedWorldHudTemplates(frame);
                    bool previousField = HasManagedKEntryFieldHudVisual(frame);
                    bool NeedsKeyOcr((int Matched, string Scores, bool KFound, bool IFound, bool HomeFound, bool EndFound) hud, bool field)
                        => hud.Matched < 3 && !(field && (hud.KFound && hud.IFound || hud.HomeFound && hud.EndFound));
                    var previousKeys = await FindManagedWorldHudKeysAsync(frame, NeedsKeyOcr(previousHud, previousField), ct);
                    int samples = 1;
                    bool menuAppeared = false;
                    while (true)
                    {
                        await Task.Delay(180, ct);
                        using var freshField = Capture(ct);
                        var currentHud = DetectManagedWorldHudTemplates(freshField);
                        bool currentField = HasManagedKEntryFieldHudVisual(freshField);
                        var currentKeys = await FindManagedWorldHudKeysAsync(freshField, NeedsKeyOcr(currentHud, currentField), ct);
                        samples++;
                        bool actualModal = HasManagedKEntryBlockingModal(freshField);
                        bool unexpectedMenu = HasFixedProcessingHubVisual(freshField) ||
                                              HasManagedProcessingBottomTabVisual(freshField) ||
                                              HasManagedFoodCraftingNavigationVisual(freshField);
                        Log?.Invoke(
                            $"[자동 가공] F9 K 진입 재판정 · " +
                            $"경로={(firstEntry ? "최초 필드 시작" : "중간 재진입")} · " +
                            $"HUD={previousHud.Matched}/4 → {currentHud.Matched}/4 · " +
                            $"이전={previousHud.Scores} · 현재={currentHud.Scores} · " +
                            $"필드독립증거={previousField}→{currentField} · " +
                            $"K/I={previousHud.KFound}/{previousHud.IFound}→{currentHud.KFound}/{currentHud.IFound} · " +
                            $"단축키={previousKeys.Scores}→{currentKeys.Scores} · " +
                            $"팝업={actualModal} · 가공메뉴={unexpectedMenu} · " +
                            $"재확인={samples}프레임/{started.Elapsed.TotalSeconds:F1}초");

                        if (actualModal)
                            Fail(freshField,
                                "관리 가공 K 직전 실제 확인창 패널+버튼 감지 · 입력 차단");
                        if (unexpectedMenu)
                        {
                            Log?.Invoke(
                                "[자동 가공] K 직전 가공 메뉴가 새로 표시됨 · " +
                                "중복 K 생략, 메뉴 재판정");
                            menuAppeared = true;
                            break;
                        }

                        // Each successful route must be consecutive across TWO
                        // actual frames. A 2/4 score alone is never authorization.
                        bool templateAuthorized = CanAuthorizeManagedKEntry(
                            false, previousHud.Matched, currentHud.Matched,
                            actualModal, unexpectedMenu);
                        bool fieldAuthorized =
                            CanAuthorizeManagedKFieldFallback(
                                previousHud.KFound, previousHud.IFound,
                                currentHud.KFound, currentHud.IFound,
                                previousField, currentField, actualModal, unexpectedMenu);
                        // 2026-10-20 23:45: Home/End ~0.94 both matched but
                        // K/I ~0.41/0.61 missed despite two positive field frames.
                        // Require BOTH Home+End on BOTH frames plus the existing
                        // independent gold-minimap/teal-skill field check.
                        bool homeEndAuthorized =
                            CanAuthorizeManagedKHomeEndFieldFallback(
                                previousHud.HomeFound, previousHud.EndFound,
                                currentHud.HomeFound, currentHud.EndFound,
                                previousField, currentField, actualModal, unexpectedMenu);
                        bool withinLimit = IsManagedKReentryWithinLimit(started.Elapsed);
                        bool keysAuthorized = previousKeys.IsConsecutiveWith(currentKeys) &&
                            previousField && currentField && !actualModal && !unexpectedMenu;
                        if ((templateAuthorized || fieldAuthorized || homeEndAuthorized || keysAuthorized) && withinLimit)
                        {
                            string route = templateAuthorized ? "templates" : fieldAuthorized ? "K/I" :
                                homeEndAuthorized ? "Home/End" : "key-labels";
                            verifyManagedK = last => VerifyManagedKFrameAsync(last, route, currentKeys, ct);
                            Log?.Invoke($"[자동 가공] K 화면 인증 · {route} · 실제 연속 2프레임 · CLI 후 입력 프레임 재검증");
                            break;
                        }

                        if (!withinLimit)
                            Fail(freshField,
                                "관리 가공 K 재진입 HUD 3/4 연속 2프레임 및 " +
                                "K/I 또는 Home/End 또는 가변배치 단축키 + 미니맵 + 생활스킬 필드 인증 모두 2초 내 실패 · 입력 없이 정지");
                        previousHud = currentHud;
                        previousField = currentField;
                        previousKeys = currentKeys;
                    }
                    if (menuAppeared)
                        continue;
                }
                await RequireManagedIdleAsync(plan, directive, "가공 메뉴 K 직전", ct);
                if (directive == AlteringFacilityEntryDirective.Automatic)
                    _ui.TapFresh(0x25, ct);
                else if (verifyManagedK is not null)
                    await _ui.TapVerifiedFreshAsync(0x25, verifyManagedK, ct);
                else
                    Fail(frame, "관리 가공 K 화면 인증 근거 없음 · 입력 없이 정지");
                if (directive != AlteringFacilityEntryDirective.Automatic)
                    _managedKInputIssued = true;
                if (directive != AlteringFacilityEntryDirective.Automatic &&
                    attempt == maxAttempts - 1 && hubCardClicks == 1)
                    recoveredHubAfterK = true;
                Log?.Invoke("[자동 가공] K 입력 전송 · 가공 메뉴 화면 확인 대기");
            }

            if (directive == AlteringFacilityEntryDirective.Automatic)
                await WaitForProcessingNavigationReadyAsync(plan, 1000, ct);
            else
                await WaitForManagedProcessingNavigationReadyAsync(1000, ct);
            using (var menu = Capture(ct))
            {
                if (await FindFacilityHeaderAsync(menu, plan.ScreenTitle, ct) is not null)
                {
                    Log?.Invoke($"[자동 가공] 시설 진입 성공 {attempt}/{maxAttempts} · {plan.ScreenTitle}");
                    return;
                }

                if (await TrySelectManagedProcessingTabAsync(menu, plan, directive, ct))
                {
                    await WaitForProcessingHubAsync(900, ct);
                    continue;
                }
                if (!await IsProcessingHubAsync(menu, ct))
                {
                    if (directive != AlteringFacilityEntryDirective.Automatic &&
                        HasManagedKEntryBlockingModal(menu))
                        Fail(menu, "K 메뉴 가공 항목 선택 전 확인/이동 팝업 감지 · 입력 차단");
                    await RequireManagedIdleAsync(plan, directive, "가공 메뉴 진입 클릭 직전", ct);
                    if (!await ClickLabelAsync("가공", new(180, 880, 420, 120), null, ct))
                    {
                        Log?.Invoke($"[자동 가공] 가공 메뉴 확인 실패 {attempt}/{maxAttempts} · 추가 입력 없이 재판정합니다.");
                        await Task.Delay(1200, ct);
                        continue;
                    }
                    await WaitForProcessingHubAsync(900, ct);
                }
            }
        }

        using var failed = Capture(ct);
        Fail(failed, "선택한 가공 시설 화면을 3회 확인하지 못했습니다. 마지막 허브 재확인 1회도 성공하지 못했습니다.");
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

        _selectedFixedRecipeKey = RecipeCacheKey(plan);
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
        Log?.Invoke($"[자동 가공] 약품 검색창 화면 변화 확인 · {openRatio:P1}");
        if (openRatio < AlteringRecipeLayout.ProcessingSearchOpenChangeRatio)
            Fail(searchDialog,
                $"약품 검색 돋보기 입력 후 검색창 화면 전환을 확인하지 못했습니다. 변화율={openRatio:P1}");

        Log?.Invoke("[자동 가공] 약품 검색창 열림 확인 · OCR 미사용 · 다음 입력 허용");
        await RequireManagedIdleAsync(plan, directive, "약품 검색어 입력 직전", ct);
        _ui.ClickFresh(AlteringRecipeLayout.ProcessingSearchInputPoint, ct);
        Log?.Invoke($"[자동 가공] 약품 검색 입력칸 · 고정좌표 ({AlteringRecipeLayout.ProcessingSearchInputPoint.X},{AlteringRecipeLayout.ProcessingSearchInputPoint.Y})");
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

        // 음식 제작과 동일: 검색어 OCR은 보조 증거다. 한 건만 일치하면
        // 성공으로 취급하고, OCR 미검출/중복이어도 검색 목록 화면이
        // 실제로 전환됐다면 고정 첫 번째 결과를 선택한다.
        bool exactUnique = exact.Count == 1;
        Log?.Invoke(
            exactUnique
                ? $"[자동 가공] 약품 검색 결과 · exact OCR 확인 · 화면 변화 {resultRatio:P1}"
                : $"[자동 가공] 약품 검색 결과 · OCR 미검출/중복 · 화면 변화 {resultRatio:P1}");

        if (!exactUnique &&
            resultRatio < AlteringRecipeLayout.ProcessingSearchResultChangeRatio)
            Fail(resultFrame,
                $"약품 검색 적용 후 결과 화면 전환을 확인하지 못했습니다. 변화율={resultRatio:P1} · 첫 결과 클릭 없이 정지");

        if (plan.RecipeOrdinal != 1)
            Fail(resultFrame,
                $"약품 검색 결과가 중복 제법 순번 {plan.RecipeOrdinal}이라 첫 결과 고정좌표를 사용하지 않습니다.");

        await RequireManagedIdleAsync(plan, directive, "약품 검색 결과 선택 직전", ct);
        _ui.ClickFresh(AlteringRecipeLayout.ProcessingSearchFirstResultPoint, ct);
        Log?.Invoke(
            $"[자동 가공] 약품 검색 첫 결과 선택 · 음식 제작과 동일한 고정좌표 " +
            $"({AlteringRecipeLayout.ProcessingSearchFirstResultPoint.X},{AlteringRecipeLayout.ProcessingSearchFirstResultPoint.Y})");
        await Task.Delay(550, ct);

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
        if (_repeatOcrFreeRecipe || _selectedFixedRecipeKey is not null)
        {
            ct.ThrowIfCancellationRequested();
            bool free = TryFindFreeProcessButtonVisual(frame, out _);
            return (!free, free ? "고정좌표 무료 가공 버튼 화면 확인" : "고정 무료 버튼 없음");
        }
        var paid = await FindAsync(frame, RecipeActionButton, "가공하러 가기", ct);
        return (paid is not null, paid is not null ? "가공하러 가기 OCR" : "없음");
    }


    // 2026-10-17 V3.2.21 GitHub screenshot: after ONE free registration
    // click the game may ask to transfer missing materials from storage.
    // This is NOT the facility travel / paid processing dialog. The green
    // button alone is never authority: require independent heading, weekly
    // checkbox label, and button OCR at their observed 800x1000 positions.
    private static readonly Rectangle MaterialTransferTitle = new(258, 665, 350, 62);
    private static readonly Rectangle MaterialTransferWeeklyLabel = new(334, 827, 250, 51);
    private static readonly Rectangle MaterialTransferConfirmLabel = new(420, 885, 160, 64);
    private static readonly Rectangle MaterialTransferCheckbox = new(300, 834, 31, 35);
    private static readonly Point MaterialTransferCheckboxPoint = new(315, 851);
    private static readonly Point MaterialTransferConfirmPoint = new(500, 915);

    private static int MaterialTransferWhiteGlyphs(Bitmap frame, Rectangle roi)
    {
        int count = 0;
        for (int y = roi.Top; y < roi.Bottom; y += 2)
        for (int x = roi.Left; x < roi.Right; x += 2)
        {
            Color p = frame.GetPixel(x, y);
            int high = Math.Max(p.R, Math.Max(p.G, p.B));
            int low = Math.Min(p.R, Math.Min(p.G, p.B));
            if (low >= 147 && high - low <= 65) count++;
        }
        return count;
    }

    internal static bool HasMaterialTransferModalVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000 ||
            !HasManagedFacilityTravelConfirmationVisual(frame))
            return false;
        // White heading AND separate weekly label are required in addition to
        // the slate dialog and green button. The recipe's green free button,
        // field HUD, or a different modal must not pass this quick gate.
        return MaterialTransferWhiteGlyphs(frame, MaterialTransferTitle) >= 22 &&
               MaterialTransferWhiteGlyphs(frame, MaterialTransferWeeklyLabel) >= 12;
    }

    internal static bool MaterialTransferCheckboxLooksSelected(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000) return false;
        int accent = 0;
        for (int y = 840; y <= 861; y += 2)
        for (int x = 304; x <= 326; x += 2)
        {
            Color p = frame.GetPixel(x, y);
            if (p.G >= 115 && p.G >= p.R + 24 && p.B >= 68)
                accent++;
        }
        // The supplied unchecked icon is muted slate grey. Never toggle a
        // clearly active green/teal checkbox back to the unchecked position.
        return accent >= 12;
    }

    internal static bool MaterialTransferCheckboxChanged(Bitmap before, Bitmap after)
    {
        if (before.Size != new Size(800, 1000) || after.Size != before.Size)
            return false;
        int changed = 0;
        for (int y = MaterialTransferCheckbox.Top; y < MaterialTransferCheckbox.Bottom; y += 2)
        for (int x = MaterialTransferCheckbox.Left; x < MaterialTransferCheckbox.Right; x += 2)
        {
            Color a = before.GetPixel(x, y), b = after.GetPixel(x, y);
            if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) +
                Math.Abs(a.B - b.B) >= 66)
                changed++;
        }
        return changed >= 9;
    }

    // The real 2026-10-26 incident OCR reads the title as
    // "재료를 전송하kl겠습LI까?" rather than exact Hangul. Accept that
    // bounded title-only confusion, never a generic green confirmation.
    // The independent weekly-option and confirm-label OCR remain mandatory.
    internal static bool MaterialTransferTitleMatchesStableAnchors(string? readText)
    {
        string text = FuzzyText.Normalize(readText ?? "");
        const string prefix = "재료를전송하";
        if (!text.StartsWith(prefix, StringComparison.Ordinal) ||
            !text.EndsWith("까", StringComparison.Ordinal) || text.Length > 23)
            return false;
        int suffix = text.IndexOf("겠습", prefix.Length, StringComparison.Ordinal);
        return suffix >= prefix.Length && suffix - prefix.Length <= 4 &&
               text.Length - (suffix + 2) <= 5;
    }

    private async Task<bool> IsExactMaterialTransferPromptAsync(Bitmap frame, CancellationToken ct)
    {
        if (!HasMaterialTransferModalVisual(frame)) return false;
        // Three separate ROIs must match the known transfer prompt. Exact OCR
        // occasionally drops a Korean syllable on scaled/dim game frames;
        // only within these narrow ROIs, allow a bounded two-edit fallback.
        // The independent slate panel, green confirm and two-frame checks
        // still veto unrelated dialogs. "확인" by itself never authorizes input.
        bool titleMatched = await FindAsync(frame, MaterialTransferTitle,
            "재료를 전송하시겠습니까?", ct) is not null ||
            await FindAsync(frame, MaterialTransferTitle,
                "재료를 전송하시겠습니까", ct) is not null;
        if (!titleMatched)
            titleMatched = (await _ui.Ocr.FindTextAsync(
                frame, MaterialTransferTitle, "재료를 전송하시겠습니까",
                maxEditDistance: 2, retry2x: true, ct: ct)).Found;
        if (!titleMatched)
        {
            // The actual incident mixes Latin "kl"/"LI" into one Hangul
            // title. Evaluate only the title ROI's own OCR line and require
            // three ordered, close Korean anchors, not broad fuzzy fragments.
            var titleLines = await _ui.Ocr.ReadLinesAsync(
                frame, MaterialTransferTitle, scale: 2, ct: ct);
            titleMatched = titleLines.Any(line =>
                MaterialTransferTitleMatchesStableAnchors(line.ReadText));
        }

        bool weeklyMatched = await FindAsync(frame, MaterialTransferWeeklyLabel,
            "일주일 동안 보지 않기", ct) is not null;
        if (!weeklyMatched)
            weeklyMatched = (await _ui.Ocr.FindTextAsync(
                frame, MaterialTransferWeeklyLabel, "일주일 동안 보지 않기",
                maxEditDistance: 2, retry2x: true, ct: ct)).Found;

        bool confirmMatched = await FindAsync(frame, MaterialTransferConfirmLabel,
            "확인", ct) is not null;
        if (!confirmMatched)
            confirmMatched = (await _ui.Ocr.FindTextAsync(
                frame, MaterialTransferConfirmLabel, "확인",
                maxEditDistance: 0, retry2x: true, ct: ct)).Found;

        Log?.Invoke($"[자동 가공] 재료 전송 문구 검증 · 제목={titleMatched} · 주간체크={weeklyMatched} · 확인={confirmMatched}");
        return titleMatched && weeklyMatched && confirmMatched;
    }

    private Task HandleOptionalMaterialTransferPromptAsync(CancellationToken ct)
        => RunMaterialTransferPromptAsync(
            Capture, IsExactMaterialTransferPromptAsync, _ui.ClickFresh,
            (milliseconds, token) => Task.Delay(milliseconds, token),
            (frame, message) => _ui.Failure(frame, message + " 추가 입력 없이 정지합니다."),
            message => Log?.Invoke(message), ct);

    // Execute the same control flow in regression tests without constructing
    // a window/input driver. Production always supplies the guarded runtime.
    internal static async Task RunMaterialTransferPromptAsync(
        Func<CancellationToken, Bitmap> capture,
        Func<Bitmap, CancellationToken, Task<bool>> verifyPrompt,
        Action<Point, CancellationToken> click,
        Func<int, CancellationToken, Task> delay,
        Func<Bitmap, string, Exception> failure,
        Action<string> log,
        CancellationToken ct)
    {
        // Run ONLY after exactly one verified free process click; never retry
        // that click here. A normal registration has no transfer popup.
        // Short visual-only polling avoids repeated OCR on every fast F9 slot.
        using var first = new Bitmap(800, 1000);
        bool present = false;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await delay(200, ct);
            using var observed = capture(ct);
            if (!HasMaterialTransferModalVisual(observed)) continue;
            using (var g = Graphics.FromImage(first))
                g.DrawImageUnscaled(observed, 0, 0);
            present = true;
            break;
        }
        if (!present) return;

        if (!await verifyPrompt(first, ct))
            throw failure(first, "재료 전송 모달 형태는 보이지만 제목/주간 체크/확인 문구가 불명확합니다.");

        // A checked checkbox is green. Two frames must agree before deciding
        // whether a click would turn it ON or accidentally turn it OFF.
        bool checkboxAlreadySelected = MaterialTransferCheckboxLooksSelected(first);
        await delay(180, ct);
        using (var second = capture(ct))
        {
            if (!await verifyPrompt(second, ct))
                throw failure(second, "재료 전송 모달이 새 프레임에서 동일하게 확인되지 않았습니다.");
            if (MaterialTransferCheckboxLooksSelected(second) != checkboxAlreadySelected)
                throw failure(second, "일주일 동안 보지 않기 체크 상태가 두 프레임에서 다릅니다. 입력 차단");
        }

        // Click the unchecked weekly checkbox once only. Already green means
        // it is selected and must never be toggled off by this automation.
        // The click is not a new production registration or a wing purchase.
        if (!checkboxAlreadySelected)
        {
            ct.ThrowIfCancellationRequested();
            click(MaterialTransferCheckboxPoint, ct);
            await delay(240, ct);
            using var checkedFrame = capture(ct);
            if (!await verifyPrompt(checkedFrame, ct) ||
                !MaterialTransferCheckboxChanged(first, checkedFrame) ||
                !MaterialTransferCheckboxLooksSelected(checkedFrame))
                throw failure(checkedFrame, "일주일 동안 보지 않기 체크가 초록색으로 바뀌지 않았습니다. 확인 버튼 입력 차단");
            log("[자동 가공] 보관함 재료 전송 팝업 · 일주일 동안 보지 않기 1회 선택 · 시각적 변화 확인");
        }
        else
        {
            log("[자동 가공] 보관함 재료 전송 팝업 · 일주일 동안 보지 않기 이미 선택됨 · 체크 재클릭 없음");
        }

        using (var final = capture(ct))
        {
            if (!await verifyPrompt(final, ct) ||
                !MaterialTransferCheckboxLooksSelected(final))
                throw failure(final, "재료 전송 확인 버튼 직전 팝업/초록 체크 재검증 실패");
        }
        // Exactly one modal confirmation is authorized by its unique wording.
        // Actual registration must still pass the caller's independent CLI
        // +1-work proof. Never replay the free-process click on failure.
        ct.ThrowIfCancellationRequested();
        click(MaterialTransferConfirmPoint, ct);
        log("[자동 가공] 보관함 재료 전송 팝업 확인 1회 · 작업 등록 CLI 검증 대기");
        for (int attempt = 0; attempt < 15; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await delay(250, ct);
            using var after = capture(ct);
            if (!HasMaterialTransferModalVisual(after))
                return;
        }
        using var stalled = capture(ct);
        throw failure(stalled, "재료 전송 확인 이후에도 같은 팝업이 남아 있습니다. 반복 클릭 금지");
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
            AlteringTravelActivityPolicy.IsManagedSafeIdle(activity))
            return;

        // 2026-10-25 07:16 F9: the preceding trip was still in progress when
        // a fresh manager-directed batch began. Watching a positively
        // reported auto-travel is read-only: NEVER send K/Esc/click/Space,
        // never trust a previous facility cache, and never infer arrival.
        // A dangerous/unknown CLI state fails immediately. Two independent
        // fresh safe-idle responses are needed before any existing visual
        // and coordinator authorization checks may run.
        if (activity is not null && _cli is not null &&
            AlteringTravelActivityPolicy.CanObserveExistingTravelBeforeManagedInput(activity))
        {
            Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · {phase} · 이전 자동이동 종료 관찰 시작" +
                $" · 최대 {AlteringTravelActivityPolicy.ManagedPreInputTravelWaitLimit.TotalSeconds:0}초" +
                " · 입력 없이 CLI만 재확인");
            var travelWait = Stopwatch.StartNew();
            int consecutiveIdleReads = 0;
            while (AlteringTravelActivityPolicy.IsWithinManagedPreInputTravelWait(
                       travelWait.Elapsed))
            {
                await Task.Delay(600, ct);
                ct.ThrowIfCancellationRequested();
                var fresh = await _cli.GetActivityAsync(ct);
                activity = fresh.Success ? GatheringQueries.ParseActivity(fresh) : null;
                if (activity is null)
                    break;
                if (AlteringTravelActivityPolicy.IsManagedSafeIdle(activity))
                {
                    consecutiveIdleReads++;
                    if (consecutiveIdleReads >=
                        AlteringTravelActivityPolicy.RequiredFreshManagedIdleReads)
                    {
                        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · {phase}" +
                            " · 이전 이동 종료 CLI 2회 확인 · 기존 화면 검증 계속");
                        return;
                    }
                }
                else if (AlteringTravelActivityPolicy.CanObserveExistingTravelBeforeManagedInput(
                             activity))
                {
                    consecutiveIdleReads = 0;
                }
                else
                {
                    break;
                }
            }
        }

        string reason = $"관리 가공 입력 차단 · {phase} · " +
            $"CLI activity={(activity is null ? "불명확" : activity.IsAutoTraveling ? "이동 중" : "안전한 대기 아님")}";
        Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · {reason} · 중앙 현장확정 무효화 요청");
        throw new AlteringCoordinatorFacilityMismatchException(
            plan.FacilityName, reason, new InvalidOperationException(reason));
    }

    // F9-specific popup gate. The normal idle check correctly rejects all
    // dialogs BEFORE inputs, but an already verified travel confirmation can
    // set Dialogue/WaitingSelection in the CLI. Only the exact two-frame
    // travel visual plus the earlier move click may authorize its Space.
    private async Task RequireManagedTravelPopupReadyAsync(
        AlteringPlan plan, string phase, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = await _cli.GetActivityAsync(ct);
        GatheringActivity? activity = response.Success
            ? GatheringQueries.ParseActivity(response)
            : null;
        if (activity is not null &&
            AlteringTravelActivityPolicy.CanConfirmVerifiedTravelDialog(activity))
            return;

        string reason = "관리 시설 이동 확인창 입력 차단 · " + phase +
            " · CLI " + (activity is null ? "조회 불명" :
                AlteringTravelActivityPolicy.Describe(activity));
        Log?.Invoke("[자동 가공] " + plan.ScreenTitle + " · " + reason);
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
        _selectedFixedRecipeKey = null;
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
                // Optional storage transfer after free registration. Without it
                // the caller waits for a CLI +1 that can never appear.
                await HandleOptionalMaterialTransferPromptAsync(ct);
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

    // F9-only facility travel modal: the confirmed 800x1000 game screenshot
    // shows a slate panel at x175..625/y754..973 and a WIDE green confirm
    // button at x405..595/y889..944. The in-world riding STOP control is a
    // small GREEN CIRCLE near x355..445/y830..915, with NO slate panel.
    // Do NOT use the broad green-pixel travel detector for managed retries:
    // otherwise a stopped/active HUD is misclassified as the old modal.
    private static bool HasManagedFacilityTravelConfirmationVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return false;

        static bool Slate(Color p) =>
            p.R >= 17 && p.R <= 85 &&
            p.G >= 18 && p.G <= 95 &&
            p.B >= 23 && p.B <= 115 &&
            p.B >= p.R && p.B >= p.G;

        // Six independent fixed panel points outside the green/cancel controls.
        // A normal world image, chat HUD, or bottom riding control has no
        // continuous dark slate dialog spanning this region.
        Point[] panelPoints =
        {
            new(213, 792), new(397, 792), new(587, 792),
            new(215, 840), new(396, 840), new(585, 840)
        };
        if (panelPoints.Count(p => Slate(frame.GetPixel(p.X, p.Y))) < 5)
            return false;

        // Fixed horizontal confirm-button interior (avoid round corners).
        // A circular riding STOP button cannot fill this right-hand strip.
        int green = 0, samples = 0;
        for (int y = 898; y <= 934; y += 4)
        for (int x = 414; x <= 586; x += 4)
        {
            Color p = frame.GetPixel(x, y);
            samples++;
            if (p.G >= 95 && p.G >= p.R + 40 &&
                p.G >= p.B + 15)
                green++;
        }
        return samples > 0 && green * 100 >= samples * 58;
    }

    // V3.1.71: manager F9 K-menu only. The 06:15 field screenshot is
    // green foliage across the old bottom confirmation ROI. Colour by itself
    // is NOT modal evidence. Require a real dark slate panel AND a broad
    // rectangular green confirm button, as in the 19:35 real modal screenshot.
    private static bool HasManagedKEntryBlockingModal(Bitmap frame)
        => HasManagedFacilityTravelConfirmationVisual(frame);

    private static readonly Rectangle ManagedHomeKeyArea = new(550, 70, 135, 30);
    private static readonly Rectangle ManagedEndKeyArea = new(680, 70, 60, 30);
    private static readonly Rectangle ManagedEscapeKeyArea = new(750, 70, 50, 30);

    internal static ManagedWorldHudKeys DetectManagedWorldHudKeys(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return new("glyph", Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, "크기 불일치");
        var matcher = new TemplateMatcher(AppContext.BaseDirectory);
        DetectionResult Find(Rectangle area, string name) => matcher.FindBrightGlyphMultiScale(
            frame, area, "abyss/templates/" + name, 0.90, 0.95, 1.05, 0.05);
        var home = Find(ManagedHomeKeyArea, "managed_home_key.png");
        var mailHome = Find(ManagedHomeKeyArea, "managed_home_mail_key.png");
        if (mailHome.Score > home.Score) home = mailHome;
        var end = Find(ManagedEndKeyArea, "managed_end_key.png");
        var escape = Find(ManagedEscapeKeyArea, "managed_escape_key.png");
        return new("glyph", home.Found ? home.Bounds : Rectangle.Empty,
            end.Found ? end.Bounds : Rectangle.Empty, escape.Found ? escape.Bounds : Rectangle.Empty,
            $"글자 Home={home.Score:F3},End={end.Score:F3},ESC={escape.Score:F3}");
    }

    private async Task<ManagedWorldHudKeys> FindManagedWorldHudKeysAsync(Bitmap frame, bool allowOcr, CancellationToken ct)
    {
        var glyphs = DetectManagedWorldHudKeys(frame);
        if (glyphs.Found || !allowOcr || !HasManagedKEntryFieldHudVisual(frame)) return glyphs;
        return await ReadManagedWorldHudKeysAsync(frame, ct);
    }

    private async Task<ManagedWorldHudKeys> ReadManagedWorldHudKeysAsync(Bitmap frame, CancellationToken ct)
    {
        // Tight Latin shortcut labels only; exact compact OCR already retries
        // enlarged images. Never search arbitrary world/chat/quest text.
        var home = await _ui.FindCompactAsync(frame, ManagedHomeKeyArea, "Home", ct);
        var end = await _ui.FindCompactAsync(frame, ManagedEndKeyArea, "End", ct);
        var escape = await _ui.FindCompactAsync(frame, ManagedEscapeKeyArea, "ESC", ct);
        return new("ocr", home?.Bounds ?? Rectangle.Empty, end?.Bounds ?? Rectangle.Empty,
            escape?.Bounds ?? Rectangle.Empty,
            $"정확 OCR Home={home is not null},End={end is not null},ESC={escape is not null}");
    }

    private async Task<bool> VerifyManagedKFrameAsync(Bitmap frame, string route,
        ManagedWorldHudKeys expectedKeys, CancellationToken ct)
    {
        if (HasManagedKEntryBlockingModal(frame) || HasFixedProcessingHubVisual(frame) ||
            HasManagedProcessingBottomTabVisual(frame) || HasManagedFoodCraftingNavigationVisual(frame))
            return false;
        if (route == "key-labels")
        {
            var keys = expectedKeys.Method == "glyph" ? DetectManagedWorldHudKeys(frame) :
                await ReadManagedWorldHudKeysAsync(frame, ct);
            return HasManagedKEntryFieldHudVisual(frame) && expectedKeys.IsConsecutiveWith(keys);
        }
        var hud = DetectManagedWorldHudTemplates(frame);
        return route switch
        {
            "templates" => hud.Matched >= 3,
            "K/I" => hud.KFound && hud.IFound && HasManagedKEntryFieldHudVisual(frame),
            "Home/End" => hud.HomeFound && hud.EndFound && HasManagedKEntryFieldHudVisual(frame),
            _ => false
        };
    }

    internal static bool HasManagedFoodCraftingNavigationVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000) return false;
        static int Count(Bitmap image, Rectangle area, bool cyan = false)
        {
            int n = 0;
            for (int y = area.Top; y < area.Bottom; y += 2)
            for (int x = area.Left; x < area.Right; x += 2)
            {
                Color p = image.GetPixel(x, y);
                if (cyan ? p.G >= 100 && p.B >= 130 && p.B >= p.R + 50 :
                    Math.Min(p.R, Math.Min(p.G, p.B)) >= 140 &&
                    Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) <= 45)
                    n++;
            }
            return n;
        }
        // Active cyan '빠른 제작', three separate navigation labels and a
        // bright list header. Darkened detail sheets do not satisfy this proof.
        return Count(frame, new(12, 35, 190, 40)) >= 18 &&
               Count(frame, new(240, 940, 90, 40), true) >= 12 &&
               Count(frame, new(325, 940, 75, 40)) >= 8 &&
               Count(frame, new(410, 940, 75, 40)) >= 8 &&
               Count(frame, new(490, 940, 85, 40)) >= 8;
    }

    private async Task<bool> TrySelectManagedFoodCraftingTabAsync(Bitmap first, AlteringPlan plan,
        AlteringFacilityEntryDirective directive, bool inputAlreadyIssued, CancellationToken ct)
    {
        Rectangle header = new(12, 24, 220, 65), tab = new(325, 935, 75, 50);
        if (!HasManagedFoodCraftingNavigationVisual(first) ||
            await _ui.FindUniqueAsync(first, header, "음식 제작", ct) is null) return false;
        if (inputAlreadyIssued) Fail(first, "음식 제작 메뉴에서 가공 탭 전환 미확인 · 중복 입력 없이 정지");
        if (HasManagedKEntryBlockingModal(first)) Fail(first, "음식 제작 메뉴 위 확인창 감지 · 입력 차단");
        var firstTab = await _ui.FindUniqueAsync(first, tab, "가공", ct);
        if (firstTab is null) Fail(first, "음식 제작 메뉴 가공 탭 정확한 글자 미확인 · 입력 차단");
        await Task.Delay(160, ct);
        using var second = Capture(ct);
        var secondTab = await _ui.FindUniqueAsync(second, tab, "가공", ct);
        if (!HasManagedFoodCraftingNavigationVisual(second) || HasManagedKEntryBlockingModal(second) ||
            await _ui.FindUniqueAsync(second, header, "음식 제작", ct) is null || secondTab is null ||
            !ProductionUiRuntime.Stable(firstTab!.Value.Bounds, secondTab.Value.Bounds))
            Fail(second, "음식 제작 메뉴/가공 탭 연속 2프레임 확인 실패 · 입력 차단");
        await RequireManagedIdleAsync(plan, directive, "음식 제작에서 가공 탭 복귀 직전", ct);
        await _ui.ClickVerifiedFreshAsync(secondTab!.Value.Center, async fresh =>
        {
            if (!HasManagedFoodCraftingNavigationVisual(fresh) || HasManagedKEntryBlockingModal(fresh) ||
                await _ui.FindUniqueAsync(fresh, header, "음식 제작", ct) is null) return false;
            var current = await _ui.FindUniqueAsync(fresh, tab, "가공", ct);
            return current is not null && ProductionUiRuntime.Stable(secondTab.Value.Bounds, current.Value.Bounds);
        }, ct);
        Log?.Invoke("[자동 가공] 음식 제작 메뉴 정확 인증 · 가공 탭 1회 선택 · K/Esc 없이 허브 전환 확인");
        return true;
    }

    // K is safe only in an idle, modal-free and non-menu world context.
    // CLI idle is checked separately IMMEDIATELY before the real input.
    internal static bool CanAuthorizeManagedKEntry(
        bool firstEntry, int firstHudMatches, int secondHudMatches,
        bool popupVisible, bool processingMenuVisible)
        => !popupVisible && !processingMenuVisible &&
           firstHudMatches >= 3 && secondHudMatches >= 3;

    // V3.2.0: the fallback does not relax the standard 3/4 threshold.
    // K and I must BOTH match and the field minimap/K-disc pixels must BOTH
    // match on two consecutive frames, while modal/menu remain absent.
    // Caller separately checks fresh CLI idle immediately before input.
    internal static bool CanAuthorizeManagedKFieldFallback(
        bool previousK, bool previousI, bool currentK, bool currentI,
        bool previousField, bool currentField,
        bool popupVisible, bool processingMenuVisible)
        => !popupVisible && !processingMenuVisible &&
           previousK && previousI && currentK && currentI &&
           previousField && currentField;

    // Independent visual fallback for the 2026-10-20 production failure:
    // matching just two HUD icons never suffices; both Home and End and the
    // minimap+skill world evidence must persist across two actual frames.
    // Popup/menu veto and fresh CLI-idle checks stay in the caller.
    internal static bool CanAuthorizeManagedKHomeEndFieldFallback(
        bool previousHome, bool previousEnd, bool currentHome, bool currentEnd,
        bool previousField, bool currentField,
        bool popupVisible, bool processingMenuVisible)
        => !popupVisible && !processingMenuVisible &&
           previousHome && previousEnd && currentHome && currentEnd &&
           previousField && currentField;

    // Bounded visual stabilization after Esc; clock runs continuously from
    // the first unknown-field frame and is never reset on weak HUD matches.
    internal static readonly TimeSpan ManagedKReentryHudTimeout = TimeSpan.FromSeconds(2);

    internal static bool IsManagedKReentryWithinLimit(TimeSpan elapsed)
        => elapsed >= TimeSpan.Zero && elapsed < ManagedKReentryHudTimeout;

    // V3.2.0: keep the SAME four 800x1000 grayscale, multi-scale
    // template assets/thresholds/ROIs as Abyss outside confirmation.
    // These HUD icons also appear INSIDE Abyss, so they are never proof by
    // themselves: caller must first rule out processing menus/popup and
    // independently verify the CLI idle state. Standard route remains >=3/4
    // on TWO frames. The field fallback also requires K+I and separate visual proof.
    private (int Matched, string Scores, bool KFound, bool IFound, bool HomeFound, bool EndFound) DetectManagedWorldHudTemplates(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return (0, "크기 불일치", false, false, false, false);

        var home = _ui.FindTemplate(frame, new Rectangle(590, 20, 150, 130),
            "abyss/templates/outside_home_v75.png", 0.76);
        var end = _ui.FindTemplate(frame, new Rectangle(630, 20, 150, 130),
            "abyss/templates/outside_end_v75.png", 0.70);
        var k = _ui.FindTemplate(frame, new Rectangle(0, 820, 180, 180),
            "abyss/templates/outside_k_v75.png", 0.72);
        var i = _ui.FindTemplate(frame, new Rectangle(620, 800, 180, 200),
            "abyss/templates/outside_i_v75.png", 0.72);
        int matched = (home.Found ? 1 : 0) + (end.Found ? 1 : 0) +
                      (k.Found ? 1 : 0) + (i.Found ? 1 : 0);
        return (matched,
            $"Home={home.Score:0.000}, End={end.Score:0.000}, K={k.Score:0.000}, I={i.Score:0.000}",
            k.Found, i.Found, home.Found, end.Found);
    }

    // Positive ordinary-world evidence for the managed unknown->K route.
    // On the real 800x1000 field the top-right circular minimap has a gold
    // rim and the bottom-left K life-skill button has a bright teal disc.
    // Do NOT use green field vegetation or the center Space compass.
    private static bool HasManagedKEntryFieldHudVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return false;

        int minimapGold = 0;
        for (int y = 104; y <= 215; y += 4)
        for (int x = 663; x <= 783; x += 4)
        {
            Color p = frame.GetPixel(x, y);
            if (p.R >= 135 && p.G >= 105 &&
                p.R >= p.B + 42 && p.G >= p.B + 25)
                minimapGold++;
        }

        int kButtonTeal = 0;
        for (int y = 874; y <= 934; y += 3)
        for (int x = 18; x <= 70; x += 3)
        {
            Color p = frame.GetPixel(x, y);
            if (p.G >= 105 && p.G >= p.R + 20 &&
                p.G >= p.B + 8)
                kButtonTeal++;
        }
        // The minimap's small orange clock icon is a DAY indicator; it is
        // absent at night and can appear on ordinary fields and in caves.
        // Never use the day/night indicator as proof that K is safe.
        // Preserve the proven gold-world route and separately recognize the
        // actual cyan/blue circular minimap rim + K disc + Space ring.
        return (minimapGold >= 20 && kButtonTeal >= 10) ||
               HasManagedKEntryNonGoldFieldHudVisual(frame, kButtonTeal);
    }

    // 17:45 V3.2.66 and 19:09 V3.2.67 are the same cave WORLD at different
    // day/night indicator states. The blue circular minimap structure (not
    // an orange time-of-day icon), the life-skill K disc and the center
    // green Space compass ring are three independent physical anchors.
    // The CALLER additionally requires 2 stable K/I or Home/End frames,
    // no menu/modal, 2s bound, fresh CLI idle and pre-input revalidation.
    private static bool HasManagedKEntryNonGoldFieldHudVisual(
        Bitmap frame, int kButtonTeal)
    {
        if (frame.Width != 800 || frame.Height != 1000 || kButtonTeal < 10)
            return false;

        int compassGreenRing = 0;
        for (int y = 810; y <= 928; y += 3)
        for (int x = 344; x <= 457; x += 3)
        {
            int dx = x - 400, dy = y - 869;
            int radiusSquared = dx * dx + dy * dy;
            if (radiusSquared < 38 * 38 || radiusSquared > 56 * 56)
                continue;
            Color p = frame.GetPixel(x, y);
            if (p.G >= 105 && p.G >= p.R + 18 && p.G >= p.B + 9)
                compassGreenRing++;
        }

        return compassGreenRing >= 22 && HasManagedBlueMinimapRingVisual(frame);
    }

    // Restrict samples to a circular arc around the actual 720,161 minimap
    // center; arbitrary blue scenery elsewhere never counts as a minimap.
    // Require pixels in at least two spatial sectors, not a single blue blob.
    // Independent K and Space geometry is checked by the caller above.
    internal static bool HasManagedBlueMinimapRingVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return false;
        int blueRing = 0;
        int[] sectors = new int[4];
        for (int y = 94; y <= 228; y += 3)
        for (int x = 649; x <= 791; x += 3)
        {
            int dx = x - 720, dy = y - 161;
            int radiusSquared = dx * dx + dy * dy;
            if (radiusSquared < 50 * 50 || radiusSquared > 74 * 74)
                continue;
            Color p = frame.GetPixel(x, y);
            if (p.B < 130 || p.B < p.R + 38 || p.B < p.G + 17)
                continue;
            blueRing++;
            int sector = (x < 720 ? 0 : 1) + (y < 161 ? 0 : 2);
            sectors[sector]++;
        }
        int populatedSectors = 0;
        foreach (int pixels in sectors)
            if (pixels >= 3) populatedSectors++;
        return blueRing >= 45 && populatedSectors >= 2;
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

    // Screenshot 2026-10-09 07:00, 800x1000: the genuine processing-result
    // screen has FOUR independent anchors (blue chest halo, brown chest body, white centered
    // result heading, cyan reward cards). A green travel popup has none of
    // this fixed reward layout. Visual proof never replaces CLI receipt proof.
    private static bool HasManagedCompletionResultVisual(Bitmap frame)
    {
        if (frame.Width != 800 || frame.Height != 1000)
            return false;

        static int Pixels(Bitmap bitmap, Rectangle roi, int step, Func<Color, bool> match)
        {
            int found = 0;
            for (int y = roi.Top; y < roi.Bottom; y += step)
            for (int x = roi.Left; x < roi.Right; x += step)
                if (match(bitmap.GetPixel(x, y)))
                    found++;
            return found;
        }

        int chestBlue = Pixels(frame, new Rectangle(295, 58, 215, 145), 4,
            p => p.B >= 110 && p.G >= 65 &&
                 p.B >= p.R + 28 && p.B >= p.G + 12);
        // The processing result specifically shows a wooden materials chest
        // inside the halo, not only a generic blue celebration background.
        int woodenChest = Pixels(frame, new Rectangle(350, 90, 105, 111), 3,
            p => p.R >= 94 && p.G >= 48 && p.R >= p.G + 18 &&
                 p.G >= p.B + 8 && p.B <= 125);
        int titleWhite = Pixels(frame, new Rectangle(329, 202, 151, 50), 2,
            p => p.R >= 165 && p.G >= 165 && p.B >= 165 &&
                 Math.Max(p.R, Math.Max(p.G, p.B)) -
                 Math.Min(p.R, Math.Min(p.G, p.B)) <= 48);
        int rewardCyan = Pixels(frame, new Rectangle(92, 398, 618, 93), 3,
            p => p.B >= 80 && p.G >= 72 &&
                 p.B >= p.R + 24 && p.G >= p.R + 24);

        return chestBlue >= 20 && woodenChest >= 18 &&
               titleWhite >= 20 && rewardCyan >= 32;
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
        if (forceMoveClick)
        {
            // V3.1.47 live log 20:19: the newly opened wood facility was remote,
            // but a false-negative move-button detector allowed "onsite" proof and
            // selected 상급 목재 before 설비로 이동. On a fresh facility entry
            // QueueAsync now owns the order: facility -> move once -> recipe.
            bool stableFacility = await WaitForStableFacilityMoveScreenAsync(ct);
            using var confirmed = Capture(ct);
            // Fixed title/subtitle pixels are primary; first-entry OCR is
            // secondary and never by itself blocks a valid fixed facility UI.
            if (!stableFacility || !HasVerifiedFacilityMoveClickScreen(confirmed))
            {
                Log?.Invoke($"[자동 가공] {plan.ScreenTitle} · 시설 전환 검증 실패 · " +
                    $"연속2프레임={stableFacility} · 전체시설UI={HasFacilityPanelStructureVisual(confirmed)} · " +
                    $"시설제목={HasFixedFacilityHeaderVisual(confirmed)} · " +
                    $"이동버튼={HasFacilityMoveButtonVisual(confirmed)} · " +
                    $"현장닫기={HasOnsiteCloseButtonVisual(confirmed)} · " +
                    $"작업슬롯={HasFacilityQueueSlotStructureVisual(confirmed)}");
                Fail(confirmed, "설비 이동 전 실제 시설창의 제목/독립 UI 구조를 확인하지 못했습니다 · 일반 필드 좌표 클릭 차단");
            }

            // V3.1.65: K/facility always has a move control. Its OCR/teal
            // detector is unreliable; use the fixed pixel baseline taken
            // immediately BEFORE clicking, never the detector as a prerequisite.
            Log?.Invoke(
                $"[자동 가공] {plan.ScreenTitle} · 설비 이동 1회 필수 경로 · " +
                "클릭 전 버튼 OCR 인식 생략 · 고정 영역 픽셀 비교로 클릭 후 소멸 확인");
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

        // Capture identical fixed ROI before any move click, including when
        // the remote button OCR incorrectly says it is absent (15:29 failure).
        // No position/size is changed: canonical region (15,205,155,65).
        if (shouldClickMove)
            await RequireManagedIdleAsync(plan, directive, "설비 이동 클릭 직전", ct);
        using var moveBaseline = shouldClickMove ? Capture(ct) : null;
        bool moveClickSent = false;
        DateTime moveClickAt = DateTime.MinValue;
        bool moveRetried = false;
        int identicalMoveFrames = 0;
        if (shouldClickMove)
        {
            _stage.Move(ProductionStage.Travel, $"{plan.ScreenTitle} 설비로 이동");
            Log?.Invoke(
                $"[자동 가공] {plan.ScreenTitle} · 설비로 이동 고정좌표 클릭 " +
                $"({AlteringFacilityLayout.MoveButtonPoint.X},{AlteringFacilityLayout.MoveButtonPoint.Y}) · " +
                "첫 입력 1회 · 픽셀 동일할 때만 추가 1회 가능 · 품목 선택 전 실행");
            // ClickFresh captures a fresh image but never checks its contents.
            // Revalidate the actual latest image inside the input boundary,
            // after CLI idle and before the fixed coordinate is sent.
            await _ui.ClickVerifiedFreshAsync(
                AlteringFacilityLayout.MoveButtonPoint,
                latest => Task.FromResult(HasVerifiedFacilityMoveClickScreen(latest)), ct);
            moveClickSent = true;
            moveClickAt = DateTime.UtcNow;
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
        int ambiguousTravelActivityFrames = 0;

        // V3.1.76: start the timer after the first confirmed move click.
        // The old 120 polls (~54 seconds of delays plus CLI/OCR time) could
        // stop a legitimately moving character before reaching a far facility.
        // Time is monotonic, never reset by CLI errors, retry, or popup Space.
        // Preserve the legacy 120-poll bound ONLY for Automatic single-altering.
        var managedTravelClock = directive != AlteringFacilityEntryDirective.Automatic
            ? Stopwatch.StartNew()
            : null;
        if (managedTravelClock is not null)
            Log?.Invoke(
                $"[자동 가공] {plan.ScreenTitle} · F9 설비 이동 제한 " +
                $"{AlteringFacilityTravelConfirmPolicy.ManagedTravelTimeout.TotalMinutes:0}분 " +
                "· 실제 경과 시간 기준 · 자동이동 중 추가 클릭/Space 금지");

        for (int attempt = 0;
             directive == AlteringFacilityEntryDirective.Automatic
                 ? attempt < 120
                 : AlteringFacilityTravelConfirmPolicy.IsManagedTravelWithinLimit(
                     managedTravelClock!.Elapsed);
             attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(attempt < 20 ? 200 : 500, ct);

            // V3.1.69 F9: AFTER the first popup Space, test actual movement
            // BEFORE doing any modal/green-HUD OCR or authorizing a retry.
            // CLI unknown means wait without input, not a blind second Space.
            if (directive != AlteringFacilityEntryDirective.Automatic &&
                travelConfirmationSpaces > 0 && !sawDeparture)
            {
                bool? movingAfterConfirmation = await TryAutoTravelingAsync(ct);
                if (movingAfterConfirmation == true)
                {
                    sawTravel = true;
                    sawDeparture = true;
                    confirmedMoveTransition = true;
                    Log?.Invoke(
                        $"[자동 가공] {plan.ScreenTitle} · 이동 확인 Space 후 CLI 자동이동 확인 · " +
                        "후속 Space 금지, 설비 도착 확인 대기");
                }
                else if (movingAfterConfirmation is null)
                {
                    Log?.Invoke(
                        $"[자동 가공] {plan.ScreenTitle} · 이동 확인 후 CLI 불명 · " +
                        "중복 Space 차단, 이동 상태 재조회");
                    continue;
                }
            }

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
                (directive == AlteringFacilityEntryDirective.Automatic
                    ? HasFacilityTravelConfirmationVisual(frame)
                    : HasManagedFacilityTravelConfirmationVisual(frame));

            if (AlteringFacilityTravelConfirmPolicy.ShouldConfirmAfterMoveClick(
                    travelPopupVisual,
                    travelConfirmationSpaces,
                    sawDeparture))
            {
                bool wordingMatched = await IsFacilityTravelDialogAsync(frame, ct);

                if (directive != AlteringFacilityEntryDirective.Automatic)
                {
                    // V3.1.68 (managed F9 only): restore V3.1.47's immediately
                    // post-move-click visual confirmation. Real 19:35 travel
                    // dialogs can have correct green/Cancel/Space UI but Korean
                    // OCR fails. OCR is diagnostic here, NOT the Space gate.
                    // Never accept a green popup outside this proven move click.
                    await RequireManagedTravelPopupReadyAsync(
                        plan, "시설 이동 확인 팝업 Space 직전", ct);
                    using var freshDialog = Capture(ct);
                    // A second frame must confirm the EXACT travel-dialog UI.
                    // Dialogue/WaitingSelection CLI flags are tolerated ONLY
                    // within this already verified managed move sequence.
                    await RequireManagedTravelPopupReadyAsync(
                        plan, "시설 이동 팝업 최종 CLI 허가", ct);
                    bool? freshAutoTravel = await TryAutoTravelingAsync(ct);
                    if (!AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
                            moveClickSent,
                            HasManagedFacilityTravelConfirmationVisual(freshDialog),
                            travelConfirmationSpaces, sawDeparture, freshAutoTravel))
                        Fail(freshDialog,
                            "관리 시설 이동 팝업 입력 직전 화면/설비이동 클릭/CLI 비이동 재확인 실패 · Space 차단");
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

                // The 20:03 V3.1.68 log shows that the first Space started
                // auto-travel. Test CLI FIRST: the in-world green STOP circle
                // is not a second travel popup and must never receive Space.
                if (directive != AlteringFacilityEntryDirective.Automatic)
                {
                    bool? movingAfterSpace = await TryAutoTravelingAsync(ct);
                    if (movingAfterSpace == true)
                    {
                        sawTravel = true;
                        sawDeparture = true;
                        confirmedMoveTransition = true;
                        Log?.Invoke(
                            $"[자동 가공] {plan.ScreenTitle} · Space {travelConfirmationSpaces} 후 " +
                            "CLI 이동 중 확인 · 추가 Space 없이 현장 도착 대기");
                        continue;
                    }
                    if (movingAfterSpace is null)
                    {
                        Log?.Invoke(
                            $"[자동 가공] {plan.ScreenTitle} · Space 후 CLI 활동 조회 불명 · " +
                            "팝업 재입력 없이 상태 재확인");
                        continue;
                    }
                }

                using var afterSpace = Capture(ct);
                bool popupStillVisible =
                    directive == AlteringFacilityEntryDirective.Automatic
                        ? HasFacilityTravelConfirmationVisual(afterSpace)
                        : HasManagedFacilityTravelConfirmationVisual(afterSpace);

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
                var travelActivity = AlteringTravelActivityPolicy.AssessAfterMove(activity);
                if (travelActivity == AlteringTravelActivityVerdict.AwaitPossibleTravelDialog)
                {
                    ambiguousTravelActivityFrames++;
                    Log?.Invoke("[자동 가공] " + plan.ScreenTitle +
                        " · 이동 직후 CLI 대화/선택 상태, 이동 확인창 재탐색 " +
                        ambiguousTravelActivityFrames + "/" +
                        AlteringTravelActivityPolicy.MaxAmbiguousFrames +
                        " · 입력 없음 · " + AlteringTravelActivityPolicy.Describe(activity));
                    if (ambiguousTravelActivityFrames <
                        AlteringTravelActivityPolicy.MaxAmbiguousFrames)
                        continue;

                    throw new InvalidOperationException(
                        "설비 이동 중 확인되지 않은 대화/선택 상태가 반복되어 정지합니다. " +
                        AlteringTravelActivityPolicy.Describe(activity));
                }

                ambiguousTravelActivityFrames = 0;
                if (travelActivity == AlteringTravelActivityVerdict.Unsafe)
                {
                    Log?.Invoke("[자동 가공] " + plan.ScreenTitle +
                        " · 설비 이동 CLI 위험 상태 · " +
                        AlteringTravelActivityPolicy.Describe(activity));
                    throw new InvalidOperationException(
                        "설비 이동 중 안전하지 않은 상태가 확인되어 정지합니다. " +
                        AlteringTravelActivityPolicy.Describe(activity));
                }

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

            double moveRegionChange = moveBaseline is null ? 0.0 :
                ProductionUiRuntime.MeasureVisualChangeRatio(
                    moveBaseline, frame, AlteringFacilityLayout.MoveButtonVisualArea,
                    sampleStep: 3, channelDelta: 24);
            bool managedFreshArrival =
                directive == AlteringFacilityEntryDirective.FreshMoveRequired && moveClickSent;
            bool popupVisible =
                HasFacilityTravelConfirmationVisual(frame) || HasBottomConfirmationModal(frame);
            // No PRE-click label/color recognition required. Only observed
            // post-click change in the exact same fixed button rectangle.
            bool instantArrival = managedFreshArrival &&
                AlteringFacilityTravelConfirmPolicy.HasVerifiedFixedMovePixelDisappearance(
                    moveClickSent, moveRegionChange, facilityVisible,
                    HasOnsiteCloseButtonVisual(frame), moveVisible,
                    activity?.IsAutoTraveling, popupVisible);

            // If the SAME button-area pixels stay unchanged for >=3 seconds,
            // the click likely did not register. One controlled retry ONLY;
            // no re-click for any observed movement, popup or uncertain CLI.
            if (moveBaseline is not null && moveClickSent)
            {
                if (moveRegionChange <= AlteringFacilityTravelConfirmPolicy.UnchangedMoveRegionMaxRatio &&
                    activity?.IsAutoTraveling == false && facilityVisible && !popupVisible &&
                    !sawDeparture && !sawTravel)
                    identicalMoveFrames++;
                else
                    identicalMoveFrames = 0;

                TimeSpan elapsedSinceMove = DateTime.UtcNow - moveClickAt;
                bool retryCandidate =
                    AlteringFacilityTravelConfirmPolicy.ShouldRetryUnchangedFixedMove(
                        forceMoveClick, moveRetried, sawDeparture || sawTravel,
                        moveRegionChange, identicalMoveFrames, elapsedSinceMove,
                        facilityVisible, activity?.IsAutoTraveling, popupVisible);
                if (retryCandidate)
                {
                    // Fresh proof immediately before the optional 2nd click.
                    await RequireManagedIdleAsync(plan, directive,
                        "변화 없는 설비 이동 재클릭 직전", ct);
                    using var retryFrame = Capture(ct);
                    double retryChange = ProductionUiRuntime.MeasureVisualChangeRatio(
                        moveBaseline, retryFrame, AlteringFacilityLayout.MoveButtonVisualArea,
                        sampleStep: 3, channelDelta: 24);
                    if (retryChange <= AlteringFacilityTravelConfirmPolicy.UnchangedMoveRegionMaxRatio &&
                        HasVerifiedFacilityMoveClickScreen(retryFrame) &&
                        await TryAutoTravelingAsync(ct) == false)
                    {
                        // The awaited activity read and ClickFresh's own
                        // recapture can race a field/menu transition. The
                        // final click boundary must independently check both
                        // the facility UI and the unchanged move-button ROI.
                        await _ui.ClickVerifiedFreshAsync(
                            AlteringFacilityLayout.MoveButtonPoint,
                            latest => Task.FromResult(
                                HasVerifiedFacilityMoveClickScreen(latest) &&
                                ProductionUiRuntime.MeasureVisualChangeRatio(
                                    moveBaseline!, latest,
                                    AlteringFacilityLayout.MoveButtonVisualArea,
                                    sampleStep: 3, channelDelta: 24) <=
                                AlteringFacilityTravelConfirmPolicy.UnchangedMoveRegionMaxRatio),
                            ct);
                        moveRetried = true;
                        moveClickAt = DateTime.UtcNow;
                        identicalMoveFrames = 0;
                        onsiteStableFrames = 0;
                        onsiteCandidateSince = null;
                        Log?.Invoke(
                            $"[자동 가공] {plan.ScreenTitle} · 버튼 영역 픽셀 동일 " +
                            $"({retryChange:P1}) 3초 유지 · 같은 고정좌표 재클릭 1/1");
                        continue;
                    }
                }
                if (moveRetried && identicalMoveFrames >=
                        AlteringFacilityTravelConfirmPolicy.RequiredIdenticalMoveFrames &&
                    elapsedSinceMove >= AlteringFacilityTravelConfirmPolicy.FirstMoveRetryGrace)
                    Fail(frame,
                        $"설비 이동 고정좌표 2회 클릭 후에도 버튼 영역 픽셀이 동일 " +
                        $"({moveRegionChange:P1}) · 추가 클릭 없이 안전 정지");
            }
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
                    double finalMoveRegionChange = moveBaseline is null ? 0.0 :
                        ProductionUiRuntime.MeasureVisualChangeRatio(
                            moveBaseline, finalFrame, AlteringFacilityLayout.MoveButtonVisualArea,
                            sampleStep: 3, channelDelta: 24);
                    bool finalInstantArrival = managedFreshArrival &&
                        AlteringFacilityTravelConfirmPolicy.HasVerifiedFixedMovePixelDisappearance(
                            moveClickSent, finalMoveRegionChange,
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
                            $"픽셀버튼소멸={finalInstantArrival}, 픽셀변화={finalMoveRegionChange:P1}, " +
                            $"재클릭사용={moveRetried}, 이동버튼={finalMoveVisible}) " +
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
            {
                var moveEvidence = MeasureFacilityMoveButtonVisual(frame);
                string arrivalWaitReason = !facilityVisible ? "가공창 미확인" :
                    activity is null ? "CLI 활동 미확정" :
                    activity.IsAutoTraveling ? "자동이동 중" :
                    popupVisible ? "확인 팝업 유지" :
                    moveVisible ? "이동버튼 양성(경계검증 또는 문구OCR)" :
                    managedFreshArrival && !confirmedMoveTransition && !instantArrival
                        ? "이동전환/픽셀소멸 증거 부족" : "현장 안정화/후행 재확인";
                Log?.Invoke(
                    $"[자동 가공] 설비 이동 대기 · 이동감지={sawTravel} · " +
                    $"화면이탈/로딩={sawDeparture} · 가공창={facilityVisible} · " +
                    $"이동버튼확정={moveVisible} · 수령상태분리={receiptMode && moveClickSent} · " +
                    $"F01 검증전환={confirmedMoveTransition} · 픽셀버튼소멸={instantArrival} · " +
                    $"픽셀변화={moveRegionChange:P1} · 픽셀동일연속={identicalMoveFrames} · " +
                    $"재클릭사용={moveRetried} · 현장닫기X={HasOnsiteCloseButtonVisual(frame)} · " +
                    $"파란수령버튼={HasCollectButtonVisual(frame)} · 동시로딩/타이틀부재연속={loadingDepartureStreak} · " +
                    $"이동확인Space={travelConfirmationSpaces} · CLI로딩거부={loadingCliRejects} · " +
                    $"이동버튼형태={moveEvidence.Shape} · 중심색상={moveEvidence.Anchor} · " +
                    $"상하경계앵커={moveEvidence.BoundedAnchorRatio:P1} · " +
                    $"도착차단={arrivalWaitReason}");
            }
        }

        if (managedTravelClock is not null)
            throw new InvalidOperationException(
                $"설비 이동 시작 후 최대 {AlteringFacilityTravelConfirmPolicy.ManagedTravelTimeout.TotalMinutes:0}분 경과 " +
                $"(실제 {managedTravelClock.Elapsed.TotalSeconds:F0}초) · " +
                "자동이동/현장 도착을 안전하게 확정하지 못해 추가 이동 클릭/Space 없이 정지합니다.");
        throw new InvalidOperationException(
            "설비로 이동 입력 후 확인창/자동이동/현장 전환을 제한 시간 안에 확인하지 못해 " +
            "추가 설비 이동 재클릭 없이 정지합니다.");
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

        bool moving = GatheringQueries.ParseActivity(response).IsAutoTraveling;
        _ui?.NoteDiagnostic("CLI", "activity_auto_traveling=" + moving);
        return moving;
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

    // A confirmed result may overlay a stale or falsely read facility title.
    // The manager's full receipt, positive modal evidence, and known idle CLI
    // must ALL agree before the header can be ignored for modal-only handling.
    private async Task<bool> IsVerifiedManagedCompletionOverlayAsync(
        Bitmap frame, AlteringPlan plan, bool managedReceipt,
        int? receiptWorkCountBefore, bool greenConfirm,
        bool facilityHeaderVisible, bool travelDialogVisible,
        bool? autoTraveling, CancellationToken ct)
    {
        if (!managedReceipt || receiptWorkCountBefore is not > 0 ||
            !greenConfirm || !facilityHeaderVisible ||
            travelDialogVisible || autoTraveling != false)
            return false;
        int? after = await TryFacilityWorkCountAsync(plan, ct);
        if (after != 0) return false;
        bool fixedResult = HasManagedCompletionResultVisual(frame);
        bool resultTitle = !fixedResult &&
            await FindAsync(frame, new Rectangle(325, 195, 165, 75),
                "가공 완료", ct) is not null;
        return AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
            managedReceipt, after == 0, greenConfirm, fixedResult,
            resultTitle, travelDialogVisible, autoTraveling);
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

    // Called ONLY by manager-directed receive-close. Require a fresh whole-
    // facility CLI empty queue after the authorized blue-button input.
    // Reward-layout pixels may stand in for a missed title OCR, but NEVER
    // for receipt, safe idle, facility or travel-popup verification.
    private async Task RequireManagedCompletionCloseAsync(
        AlteringPlan plan, int? receiptWorkCountBefore, CancellationToken ct)
    {
        await RequireManagedIdleAsync(
            plan, AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
            "완료 결과창 Space 직전 활동 검사", ct);

        int? receiptWorkCountAfter = await TryFacilityWorkCountAsync(plan, ct);
        bool verifiedWholeReceipt = receiptWorkCountBefore is int before &&
            receiptWorkCountAfter is int after &&
            AlteringReceiptPolicy.IsManagedFacilityReceiptConfirmed(before, after);

        using var frame = Capture(ct);
        bool green = HasBottomConfirmationModal(frame);
        bool visualResult = green && HasManagedCompletionResultVisual(frame);
        // A result title OCR miss on the real white-on-blue result is not a
        // veto once the fixed reward result screen is positively proven.
        bool heading = green && !visualResult &&
            await FindAsync(frame, new Rectangle(325, 195, 165, 75),
                "가공 완료", ct) is not null;
        bool facility = await FindFacilityHeaderAsync(
            frame, plan.ScreenTitle, ct) is not null;
        bool travelDialog = await IsFacilityTravelDialogAsync(frame, ct);
        bool? traveling = await TryAutoTravelingAsync(ct);
        bool provenResultOverlay = facility &&
            AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
                managedReceipt: true, cliWholeFacilityReceiptConfirmed: verifiedWholeReceipt,
                greenConfirmVisible: green, fixedRewardResultVisible: visualResult,
                completionTitleVisible: heading, travelDialogVisible: travelDialog,
                autoTraveling: traveling);
        bool facilityForPolicy = facility && !provenResultOverlay;

        Log?.Invoke(
            $"[자동 가공] 관리 수령 완료창 검증 · 초록버튼={green} · " +
            $"보상화면고정={visualResult} · 제목OCR={heading} · " +
            $"시설작업={receiptWorkCountBefore?.ToString() ?? "불명"}->{receiptWorkCountAfter?.ToString() ?? "불명"} · " +
            $"시설창={facility} · 완료팝업겹침확정={provenResultOverlay} · 이동팝업={travelDialog} · CLI이동={traveling?.ToString() ?? "불명"}");

        if (!AlteringReceiptPolicy.CanCloseManagedCompletionResult(
                green, heading, visualResult, verifiedWholeReceipt,
                facilityForPolicy, travelDialog, traveling))
            Fail(frame,
                "관리 수령 완료창 고정 보상구조/OCR/시설 전체 수령/CLI 비이동 증거 부족 · " +
                "다른 초록 팝업 Space 차단");

        // OCR may be slow; the final managed activity check remains mandatory.
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
            bool verifiedOverlay = cliReceiptConfirmed &&
                await IsVerifiedManagedCompletionOverlayAsync(
                    beforeClose, plan, managedReceipt, receiptWorkCountBefore,
                    greenConfirm, facilityVisible, travelDialog, autoTraveling, ct);
            bool facilityForPolicy = facilityVisible && !verifiedOverlay;
            bool canClose = AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                    managedReceipt, autoTraveling) &&
                (cliReceiptConfirmed
                    ? AlteringReceiptPolicy.CanConfirmCliReceiptCompletion(
                        greenConfirm, facilityForPolicy, travelDialog)
                    : AlteringReceiptPolicy.CanConfirmCompletion(
                        greenConfirm, facilityForPolicy, travelDialog,
                        autoTraveling == true));
            if (!canClose)
                Fail(beforeClose,
                    "수령 완료창 Space 직전 재검증 실패 · 완료창/이동창/시설창 상태 불확실 · 추가 입력 없이 정지합니다.");
        }

        if (managedReceipt)
            await RequireManagedCompletionCloseAsync(plan, receiptWorkCountBefore, ct);
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
            bool verifiedOverlay = cliReceiptConfirmed &&
                await IsVerifiedManagedCompletionOverlayAsync(
                    verify, plan, managedReceipt, receiptWorkCountBefore,
                    greenConfirm, facilityVisible, travelDialog, autoTraveling, ct);
            bool facilityForPolicy = facilityVisible && !verifiedOverlay;

            bool canRetryClose = AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                    managedReceipt, autoTraveling) &&
                (cliReceiptConfirmed
                    ? AlteringReceiptPolicy.CanRetryCliReceiptCompletionClose(
                        greenConfirm, facilityForPolicy, travelDialog)
                    : AlteringReceiptPolicy.CanRetryCompletionClose(
                    greenConfirm,
                    facilityForPolicy,
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
                bool retryVerifiedOverlay = cliReceiptConfirmed &&
                    await IsVerifiedManagedCompletionOverlayAsync(
                        retryFrame, plan, managedReceipt, receiptWorkCountBefore,
                        retryGreenConfirm, retryFacilityVisible, retryTravelDialog,
                        retryAutoTraveling, ct);
                bool retryFacilityForPolicy = retryFacilityVisible && !retryVerifiedOverlay;

                bool canRetryAgain = AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                        managedReceipt, retryAutoTraveling) &&
                    (cliReceiptConfirmed
                        ? AlteringReceiptPolicy.CanRetryCliReceiptCompletionClose(
                            retryGreenConfirm, retryFacilityForPolicy, retryTravelDialog)
                        : AlteringReceiptPolicy.CanRetryCompletionClose(
                        retryGreenConfirm,
                        retryFacilityVisible,
                        retryTravelDialog,
                        retryAutoTraveling == true));

                if (canRetryAgain)
                {
                    if (managedReceipt)
                        await RequireManagedCompletionCloseAsync(plan, receiptWorkCountBefore, ct);
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
            bool verifiedOverlay = cliReceiptConfirmed &&
                await IsVerifiedManagedCompletionOverlayAsync(
                    closedCheck, plan, managedReceipt, receiptWorkCountBefore,
                    greenConfirm, facilityVisible, travelDialog, autoTraveling, ct);
            bool facilityForPolicy = facilityVisible && !verifiedOverlay;

            bool completionStillVisible = AlteringReceiptPolicy.CanSendCompletionCloseSpace(
                    managedReceipt, autoTraveling) &&
                (cliReceiptConfirmed
                    ? AlteringReceiptPolicy.CanRetryCliReceiptCompletionClose(
                        greenConfirm, facilityForPolicy, travelDialog)
                    : AlteringReceiptPolicy.CanRetryCompletionClose(
                    greenConfirm,
                    facilityForPolicy,
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

                bool travelDialogAfterReceipt =
                    await IsFacilityTravelDialogAsync(frame, ct);
                bool? autoTravelAfterReceipt = await TryAutoTravelingAsync(ct);
                bool verifiedOverlay = await IsVerifiedManagedCompletionOverlayAsync(
                    frame, plan, managedReceipt, receiptBefore,
                    greenConfirm, facilityVisible, travelDialogAfterReceipt,
                    autoTravelAfterReceipt, ct);
                bool facilityForPolicy = facilityVisible && !verifiedOverlay;
                Log?.Invoke(
                    $"[자동 가공][수령 후 재판정] {plan.ScreenTitle} · CLI={receiptBefore}->{receiptNow} · " +
                    $"시설헤더={facilityVisible} · 초록확인={greenConfirm} · " +
                    $"완료팝업겹침확정={verifiedOverlay} · 이동창={travelDialogAfterReceipt} · " +
                    $"CLI이동={autoTravelAfterReceipt?.ToString() ?? "불명"}");

                if (facilityForPolicy)
                {
                    // Actual physical return still needs two clean fresh frames.
                    if (await WaitForReceiptFacilityReturnAsync(
                            plan, receiptBefore, managedReceipt, ct))
                        return true;

                    using var ambiguous = Capture(ct);
                    Fail(ambiguous,
                        "CLI 수령 확정 후 시설창 복귀 2프레임/완료창 없음/CLI 비이동을 확인하지 못했습니다.");
                }

                if (!AlteringReceiptPolicy.CanConfirmCliReceiptCompletion(
                        greenConfirm,
                        facilityForPolicy,
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

        int? receiptWorkCountBefore;
        if (directive == AlteringFacilityEntryDirective.Automatic)
        {
            // Keep the proven V3.1.50 two-frame + 400ms path for legacy single-altering.
            if (!await WaitForCollectPromptAsync(plan, attempts: 24, delayMs: 250, ct, directive))
            {
                using var failed = Capture(ct);
                Fail(failed, "현장 가공대 도착 후 완료 작업 + 파란 수령 버튼을 제한 시간 안에 확인하지 못했습니다.");
            }
            receiptWorkCountBefore = await TryFacilityWorkCountAsync(plan, ct);
            Log?.Invoke(
                $"[자동 가공] 파란 수령 버튼 입력 전 400ms 안정화 · {plan.ScreenTitle} · " +
                $"수령 전 시설 전체 작업수={(receiptWorkCountBefore?.ToString() ?? "확인불가")}");
            await Task.Delay(400, ct);
            if (!await WaitForCollectPromptAsync(plan, attempts: 2, delayMs: 100, ct, directive))
            {
                using var failed = Capture(ct);
                Fail(failed, "단일가공 400ms 안정화 후 수령 가능 상태 불확실 · Space 없이 정지");
            }
        }
        else
        {
            // The coordinator already verified physical onsite arrival.
            // Require a fresh whole-facility CLI completion and matching
            // facility UI; button colour is NOT an input authorization.
            receiptWorkCountBefore = await TryFacilityWorkCountAsync(plan, ct);
            using var readyFrame = Capture(ct);
            if (receiptWorkCountBefore is not > 0 ||
                !await HasCollectPromptAsync(readyFrame, plan, directive, ct))
                Fail(readyFrame,
                    "관리 수령 직전 시설창/팝업/CLI 전체완료/비이동 검사 실패 · " +
                    "파란 버튼 색상 무관 · Space 없이 정지");
            Log?.Invoke(
                $"[자동 가공] 관리 수령 진입 · {plan.ScreenTitle} · " +
                $"시설 전체 {receiptWorkCountBefore}건 완료 · " +
                "CLI+현장 확인, 파란 버튼 색상 검사 생략 · " +
                "기존 2회/400ms/2회 중복 검사 생략");
        }

        _stage.Move(ProductionStage.Process, $"{plan.DisplayName} 완료 작업 수령");
        Log?.Invoke(
            $"[자동 가공] 현장 수령 화면 재확인 완료 · {plan.ScreenTitle} + 수령 현장 확정 + CLI 완료 작업 + " +
            (directive == AlteringFacilityEntryDirective.Automatic
                ? "파란 수령 버튼"
                : "파란 버튼 색상 검사 생략") +
            $" · Space 1회 · 수령 전 시설 전체 작업수={(receiptWorkCountBefore?.ToString() ?? "확인불가")}");
        // All visual/CLI checks have passed. Persist RecoveryRequired right
        // before the irreversible receive key, not before navigation/OCR.
        // A failure before this callback leaves the last known safe checkpoint.
        await beforeReceiveInput(ct);
        ct.ThrowIfCancellationRequested();
        if (directive != AlteringFacilityEntryDirective.Automatic)
        {
            // The durable receipt marker may require file IO. Revalidate the
            // actual facility queue and confirmed facility UI on a NEW frame afterwards,
            // immediately before the irreversible Space. A late running/new
            // work slot must not be partially collected. Because the marker
            // was already persisted, any failure remains RecoveryRequired:
            // fail closed rather than inferring a receipt never happened.
            using var finalFrame = Capture(ct);
            if (!await HasCollectPromptAsync(finalFrame, plan, directive, ct))
                Fail(finalFrame,
                    "F02 수령 입력 직전 시설 전체 완료/현장/CLI 안전 증거 소실(부분 완료/신규 대기/팝업/이동) · " +
                    "모두 받기 Space 0회 · RecoveryRequired 유지 · 자동 재수령 금지");
        }
        // F03: right before the irreversible receive input, the coordinator
        // must still have an independently fresh, safe character activity.
        await RequireManagedIdleAsync(
            plan, directive, "모두 받기 최종 Space 직전 활동 검사", ct);
        ct.ThrowIfCancellationRequested();
        _ui.TapFresh(0x39, ct);
        await Task.Delay(450, ct);

        bool confirmedReceipt = await ConfirmCompletionResultAsync(
            plan, ct, receiptWorkCountBefore: receiptWorkCountBefore, resultAttempts: 24,
            managedReceipt: directive != AlteringFacilityEntryDirective.Automatic);
        bool receiptRetryUsed = false;
        if (!confirmedReceipt && directive != AlteringFacilityEntryDirective.Automatic)
        {
            // The first Space may be ignored by the game. Wait for all
            // result/CLI evidence first: no repeat merely because a result
            // popup was missed. Compare TWO live CLI reads with the original
            // facility count, then require the actual onsite facility and
            // complete CLI lane again (no blue-colour dependency).
            int? firstPostCount = await TryFacilityWorkCountAsync(plan, ct);
            await Task.Delay(350, ct);
            int? secondPostCount = await TryFacilityWorkCountAsync(plan, ct);
            using var retryCandidate = Capture(ct);
            bool wholeLaneReady =
                await HasCollectPromptAsync(retryCandidate, plan, directive, ct);
            bool freshFacility =
                await FindFacilityHeaderAsync(retryCandidate, plan.ScreenTitle, ct) is not null;
            bool freshOnsiteX = HasOnsiteCloseButtonVisual(retryCandidate);
            bool anyModal = HasBottomConfirmationModal(retryCandidate) ||
                            HasManagedFacilityTravelConfirmationVisual(retryCandidate);
            bool? freshTravel = await TryAutoTravelingAsync(ct);
            bool authorizedRetry = firstPostCount == secondPostCount &&
                AlteringReceiptPolicy.CanRetryManagedFacilityReceipt(
                    managedReceipt: true, beforeCount: receiptWorkCountBefore,
                    afterCount: secondPostCount,
                    allFacilityWorksComplete: wholeLaneReady,
                    facilityVisible: freshFacility,
                    onsiteCloseVisible: freshOnsiteX,
                    anyModalVisible: anyModal,
                    autoTraveling: freshTravel,
                    alreadyRetried: receiptRetryUsed);

            Log?.Invoke(
                $"[자동 가공] 첫 모두 받기 Space 후 결과 미확인 · " +
                $"CLI 작업수={receiptWorkCountBefore?.ToString() ?? "불명"}→" +
                $"{firstPostCount?.ToString() ?? "불명"}→{secondPostCount?.ToString() ?? "불명"} · " +
                $"CLI전체완료+시설창={wholeLaneReady} · 파란픽셀=검사안함 · " +
                $"현장={freshFacility}/{freshOnsiteX} · 팝업={anyModal} · " +
                $"CLI이동={freshTravel?.ToString() ?? "불명"} · 재수령허가={authorizedRetry}");

            if (authorizedRetry)
            {
                // F02/N02: the durable RecoveryRequired marker already exists.
                // Immediately before the SECOND and LAST receive Space, use
                // another new screenshot and CLI. Any count decrease (including
                // partial receipt) or popup blocks the retry without side effects.
                await RequireManagedIdleAsync(plan, directive,
                    "모두 받기 재시도 Space 직전 활동 검사", ct);
                using var justBeforeRetry = Capture(ct);
                int? lastCount = await TryFacilityWorkCountAsync(plan, ct);
                bool stillReady =
                    await HasCollectPromptAsync(justBeforeRetry, plan, directive, ct);
                bool retryModal = HasBottomConfirmationModal(justBeforeRetry) ||
                                  HasManagedFacilityTravelConfirmationVisual(justBeforeRetry);
                bool stillAuthorized = AlteringReceiptPolicy.CanRetryManagedFacilityReceipt(
                    managedReceipt: true, beforeCount: receiptWorkCountBefore,
                    afterCount: lastCount,
                    allFacilityWorksComplete: stillReady,
                    facilityVisible: await FindFacilityHeaderAsync(
                        justBeforeRetry, plan.ScreenTitle, ct) is not null,
                    onsiteCloseVisible: HasOnsiteCloseButtonVisual(justBeforeRetry),
                    anyModalVisible: retryModal,
                    autoTraveling: await TryAutoTravelingAsync(ct),
                    alreadyRetried: receiptRetryUsed);
                if (!stillAuthorized)
                    Fail(justBeforeRetry,
                        "모두 받기 추가 1회 입력 직전 CLI/현장/팝업/활동 상태 변경 · " +
                        "RecoveryRequired 유지 · Space 재시도 차단");

                receiptRetryUsed = true;
                ct.ThrowIfCancellationRequested();
                _ui.TapFresh(0x39, ct); // The only possible second receive input.
                Log?.Invoke("[자동 가공] CLI 작업건 동일 + 현장/전체완료 재확인 · 파란색 검사 없이 Space 재시도 1/1");
                await Task.Delay(450, ct);
                confirmedReceipt = await ConfirmCompletionResultAsync(
                    plan, ct, receiptWorkCountBefore: receiptWorkCountBefore,
                    resultAttempts: 24, managedReceipt: true);
            }
        }

        if (!confirmedReceipt)
        {
            using var failed = Capture(ct);
            Fail(failed,
                $"현장 모두 받기 수령 확인 실패 · 재시도={receiptRetryUsed} · " +
                "CLI 작업 감소/정상 완료창 확정 불가 · 추가 수령 없이 RecoveryRequired 안전 정지");
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

