using FishingAutomation;

namespace DungeonVisionBot;

/// <summary>
/// Zero-wing bulk gathering. Supported raw materials use the user-confirmed
/// Life Skill 100-action route first. The inventory 100-item quest remains only
/// as a fallback/helper route when a direct life-skill mapping is unavailable.
/// CLI is read-only and inventory quantity is the source of truth.
/// </summary>
internal sealed class InventoryBulkGatheringScreen : IGatheringScreen
{
    private readonly MabinogiMobileCli _cli;
    private readonly GatheringCliData _data;
    private readonly CraftingCliData _inventory;
    private readonly GatheringScreen _fallback;
    private readonly ProductionUiRuntime _ui;
    private readonly ProductionStageMachine _stage = new("채집");

    // Fixed 800x1000 client coordinates confirmed from the live profile/life-skill UI.
    private static readonly Point ProfileLifeSkillPoint = new(400, 944);
    // Live 800x1000 life-skill detail popup: "가까운 위치 찾기" link center.
    private static readonly Point LifeSkillNearestLocationPoint = new(300, 534);

    private static bool TryLifeSkillCategoryPoint(string category, out Point point)
    {
        point = category switch
        {
            "일상 채집" => new Point(222, 206),
            "나무 베기" => new Point(341, 206),
            "광석 캐기" => new Point(460, 206),
            "약초 채집" => new Point(578, 206),
            "양털 깎기" => new Point(222, 383),
            "추수" => new Point(341, 383),
            "호미질" => new Point(460, 383),
            _ => Point.Empty
        };
        return point != Point.Empty;
    }

    internal string InputMode => _ui.InputMode;
    internal event Action<string>? Log;

    internal InventoryBulkGatheringScreen(
        nint hwnd,
        AppSettings settings,
        string debugDir,
        MabinogiMobileCli cli,
        GatheringCliData data)
    {
        _cli = cli;
        _data = data;
        _inventory = new CraftingCliData(cli);
        _fallback = new GatheringScreen(hwnd, settings, debugDir, cli, data);
        _fallback.Log += text => Log?.Invoke(text);
        _ui = new ProductionUiRuntime(hwnd, settings, debugDir, "bulk-gathering");
        _stage.Changed += (stage, detail) =>
            Log?.Invoke($"[대량 채집][상태] {stage} · {detail}");
    }

    private Bitmap Capture(CancellationToken ct) => _ui.Capture(ct);

    public async Task StartAsync(GatheringPlan plan, CancellationToken ct)
    {
        if (!LivingSkillGatheringCatalog.TryResolveBulk(plan.DisplayName, out var source))
        {
            string kind = LivingSkillGatheringCatalog.IsQuestOnlyMaterial(plan.DisplayName)
                ? "곤충채집/제작 퀘스트 전용"
                : "생활 스킬 직접 매핑 미확정";
            throw new InvalidOperationException(
                $"{plan.DisplayName}은(는) {kind} 재료라 생활 스킬 자동 채집을 시작할 수 없습니다.");
        }

        _stage.Move(ProductionStage.OpenHub, $"생활 스킬 · {source.Category}");
        Log?.Invoke(
            $"[대량 채집] 생활 스킬 100회 우선 · 생활 스킬 전용 · {plan.DisplayName} → {source.Category}/{source.TargetName}");

        var automation = new LifeSkillBulkGatheringAutomation(
            token => _data.ItemCountAsync(plan.DisplayName, token),
            token => StartLifeSkillHundredAsync(source, token),
            (before, targetTotal, token) => WaitForLifeSkillHundredStopOrTargetAsync(
                plan.DisplayName, before, targetTotal, TimeSpan.FromMinutes(30), token));
        automation.Log += text => Log?.Invoke(text);

        try
        {
            await automation.RunAsync(plan, ct);
        }
        catch (InvalidOperationException ex)
        {
            Log?.Invoke(
                $"[대량 채집] 생활 스킬 경로 실패 · 가방/재료상세 경로로 우회하지 않음 · {ex.Message}");
            throw;
        }
    }

    private static bool CanUseInventoryFallback(InvalidOperationException ex)
    {
        string message = ex.Message ?? "";
        // Only recognition/navigation failures may change route. Never hide unsafe
        // field state, inventory decrease, tool failures, or timeout after gathering.
        string[] safeNavigationFailures =
        {
            "프로필에서 생활 스킬",
            "생활 스킬 분류",
            "목록에서",
            "행의 이름/아이콘",
            "가까운 위치 찾기"
        };
        return safeNavigationFailures.Any(x =>
            message.Contains(x, StringComparison.Ordinal));
    }

    private async Task StartLifeSkillHundredAsync(
        LivingSkillGatheringSource source,
        CancellationToken ct)
    {
        if (_stage.Current is not ProductionStage.OpenHub and not ProductionStage.VerifyInventory)
            _stage.Move(ProductionStage.OpenHub, $"생활 스킬 · {source.Category}");
        else if (_stage.Current == ProductionStage.VerifyInventory)
            _stage.Move(ProductionStage.OpenHub, $"생활 스킬 반복 · {source.Category}");

        if (!TryLifeSkillCategoryPoint(source.Category, out Point categoryPoint))
            throw new InvalidOperationException(
                $"생활 스킬 분류 {source.Category}의 고정좌표가 등록되어 있지 않습니다.");

        // C is a toggle. Press it once, then prove that the profile opened.
        // Never keep toggling merely because one OCR read missed 전투력/생활력.
        // A second C is allowed only when the screen is still effectively unchanged
        // from the pre-C field frame on two independent profile regions.
        bool profileConfirmed = false;
        for (int attempt = 1; attempt <= 2 && !profileConfirmed; attempt++)
        {
            using var beforeProfileOpen = Capture(ct);
            Log?.Invoke($"[대량 채집] 프로필 열기 · C 입력 {attempt}/2");
            _ui.TapFresh(0x2E, ct);

            var observation = await WaitForProfileOpenAsync(beforeProfileOpen, ct);
            if (observation.Confirmed)
            {
                profileConfirmed = true;
                Log?.Invoke(
                    $"[대량 채집] 프로필 화면 확인 · {observation.Evidence} · C 추가 입력 없음");
                break;
            }

            if (!observation.MayRetryToggle)
            {
                using var failed = Capture(ct);
                throw Fail(
                    failed,
                    "C 입력 후 화면 전환은 감지됐지만 프로필 확정 증거가 부족합니다. " +
                    "토글키 C를 다시 누르지 않고 안전하게 정지합니다.");
            }

            if (attempt < 2)
            {
                Log?.Invoke(
                    "[대량 채집] C 입력 후 프로필 영역 변화 없음 2개 영역 확인 · " +
                    "프로필이 열리지 않은 것으로 보고 C 1회만 재시도");
                await Task.Delay(350, ct);
            }
        }

        if (!profileConfirmed)
        {
            using var failed = Capture(ct);
            throw Fail(
                failed,
                "C 입력 후 프로필 화면을 확인하지 못했습니다. " +
                "전투력/생활력 OCR 또는 프로필 고정 화면전환 증거가 필요합니다.");
        }

        Log?.Invoke(
            $"[대량 채집] 생활 스킬 고정좌표 클릭 · ({ProfileLifeSkillPoint.X},{ProfileLifeSkillPoint.Y})");
        _ui.ClickFresh(ProfileLifeSkillPoint, ct);
        await Task.Delay(650, ct);

        await _ui.VerifyStableAnchorAsync(
            "생활 스킬",
            new Rectangle(10, 20, 240, 100),
            ct,
            "고정좌표 클릭 후 생활 스킬 화면 제목을 확인하지 못했습니다.",
            dimText: false);
        Log?.Invoke("[대량 채집] 생활 스킬 화면 진입 확인 · 상단 제목 2프레임 안정");

        _stage.Move(ProductionStage.SelectCategory, source.Category);
        Log?.Invoke(
            $"[대량 채집] 생활 스킬 분류 고정좌표 클릭 · {source.Category} · ({categoryPoint.X},{categoryPoint.Y})");
        _ui.ClickFresh(categoryPoint, ct);
        await Task.Delay(500, ct);

        _stage.Move(ProductionStage.Search, source.TargetName);
        if (LifeSkillListLayout.TryFixedRowPoint(
                source.Category,
                source.TargetName,
                out Point fixedRowPoint))
        {
            // Confirmed one-page categories never drag. The selected fixed row is
            // followed directly by the shared fixed "가까운 위치 찾기" coordinate.
            Log?.Invoke(
                $"[대량 채집] {source.Category} 한 페이지 고정좌표 · {source.TargetName} · " +
                $"({fixedRowPoint.X},{fixedRowPoint.Y}) · 드래그 없음");
            _ui.ClickFresh(fixedRowPoint, ct);
        }
        else
        {
            var row = await FindStableLifeSkillRowAsync(source, ct);
            if (row is null)
            {
                using var failed = Capture(ct);
                throw Fail(failed,
                    $"{source.Category} 고정좌표 클릭 후 {source.TargetName} 행을 OCR+아이콘 구조로 확인하지 못했습니다.");
            }

            using var frame = Capture(ct);
            // Reconfirm the exact label and same-row icon immediately before input.
            var rowRoi = Rectangle.Intersect(
                new Rectangle(150, Math.Max(90, row.Value.Bounds.Top - 50), 630, 100),
                new Rectangle(Point.Empty, frame.Size));
            var exact = await FindLifeSkillLabelAsync(
                frame,
                rowRoi,
                source.TargetName,
                ct);
            if (exact is null || !exact.Value.Bounds.IntersectsWith(row.Value.Bounds) ||
                !HasRowIconVisual(frame, exact.Value.Bounds))
                throw Fail(frame,
                    $"{source.TargetName} 행의 이름/아이콘 2차 확인에 실패해 클릭하지 않습니다.");

            _ui.ClickFresh(new Point(
                    Math.Clamp(exact.Value.Center.X, 180, 740),
                    exact.Value.Center.Y), ct);
        }
        await Task.Delay(650, ct);

        // The selected material row already identifies the target. Do not OCR the
        // same material name again inside the detail popup; the live 800x1000
        // "가까운 위치 찾기" link uses the shared fixed coordinate for every life skill.
        Log?.Invoke(
            $"[대량 채집] {source.TargetName} 선택 후 상세 품목명 OCR 재확인 생략 · 가까운 위치 고정좌표 사용");

        _stage.Move(ProductionStage.Detail, source.TargetName);
        _stage.Move(ProductionStage.Travel, $"{source.TargetName} 가까운 위치");
        Log?.Invoke(
            $"[대량 채집] 가까운 위치 찾기 · 고정좌표 " +
            $"({LifeSkillNearestLocationPoint.X},{LifeSkillNearestLocationPoint.Y}) · " +
            "100회 문구 OCR 없음");
        _ui.ClickFresh(LifeSkillNearestLocationPoint, ct);

        Log?.Invoke(
            $"[대량 채집] {source.Category} · {source.TargetName} · 가까운 위치 이동 후 목표 수량/100회 자연 종료 감시 · " +
            "100회는 행동 횟수이며 획득 수량과 분리 · 목표 재료를 먼저 확보하면 안전 정지");
        await Task.Delay(700, ct);
    }

    private async Task<(bool Confirmed, bool MayRetryToggle, string Evidence)> WaitForProfileOpenAsync(
        Bitmap beforeOpen,
        CancellationToken ct)
    {
        var profileBody = new Rectangle(80, 120, 640, 760);
        var bottomBand = new Rectangle(245, 870, 310, 120);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        int stableProfileFrames = 0;
        double lastBodyChange = 0;
        double lastBottomChange = 0;
        string lastEvidence = "확인 증거 없음";

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);

            var power = await FindUniqueAsync(
                frame, new Rectangle(120, 630, 390, 250), "전투력", ct);
            var vitality = await FindUniqueAsync(
                frame, new Rectangle(120, 630, 390, 250), "생활력", ct);
            var lifeSkill = await _ui.Ocr.FindCompactLabelAsync(
                frame,
                bottomBand,
                "생활 스킬",
                ct);

            lastBodyChange = ProductionUiRuntime.MeasureVisualChangeRatio(
                beforeOpen,
                frame,
                profileBody,
                sampleStep: 8,
                channelDelta: 24);
            lastBottomChange = ProductionUiRuntime.MeasureVisualChangeRatio(
                beforeOpen,
                frame,
                bottomBand,
                sampleStep: 6,
                channelDelta: 24);

            bool primaryStatOcr = power is not null || vitality is not null;
            bool confirmed = LifeSkillProfilePolicy.IsConfirmed(
                primaryStatOcr,
                lifeSkill.Found,
                lastBodyChange,
                lastBottomChange);

            if (confirmed)
            {
                stableProfileFrames++;
                lastEvidence = primaryStatOcr
                    ? "전투력/생활력 OCR"
                    : lifeSkill.Found
                        ? $"하단 생활 스킬 OCR + 화면전환 body={lastBodyChange:F2}"
                        : $"프로필 고정 화면전환 body={lastBodyChange:F2}, bottom={lastBottomChange:F2}";

                if (stableProfileFrames >= 2)
                    return (true, false, lastEvidence);
            }
            else
            {
                stableProfileFrames = 0;
            }

            await Task.Delay(180, ct);
        }

        bool mayRetry = LifeSkillProfilePolicy.MayRetryToggle(
            lastBodyChange,
            lastBottomChange);
        Log?.Invoke(
            $"[대량 채집] 프로필 확인 미확정 · bodyChange={lastBodyChange:F3} · " +
            $"bottomChange={lastBottomChange:F3} · C재시도={(mayRetry ? "허용" : "금지")}");
        return (false, mayRetry, lastEvidence);
    }

    private async Task<DetectionResult?> FindStableLifeSkillRowAsync(
        LivingSkillGatheringSource source,
        CancellationToken ct)
    {
        // 추수는 현재 확인된 밀/옥수수/콩/쌀/귀리가 첫 화면 한 페이지에 모두 보인다.
        // 불필요한 드래그는 행 위치를 흔들 수 있으므로 추수는 첫 화면만 검사한다.
        // 다른 카테고리는 기존처럼 목록 영역만 제한적으로 스크롤한다.
        var listRoi = new Rectangle(150, 145, 630, 700);
        int maxPages = LifeSkillListLayout.NeverScroll(source.Category) ? 1 : 9;

        for (int page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            DetectionResult? first = null;
            using (var frame = Capture(ct))
            {
                first = await FindLifeSkillLabelAsync(
                    frame,
                    listRoi,
                    source.TargetName,
                    ct);
                if (first is not null && !HasRowIconVisual(frame, first.Value.Bounds))
                    first = null;
            }

            if (first is not null)
            {
                await Task.Delay(160, ct);
                using var fresh = Capture(ct);
                var second = await FindLifeSkillLabelAsync(
                    fresh,
                    listRoi,
                    source.TargetName,
                    ct);
                if (second is not null &&
                    GatheringNavigationPolicy.IsStableFirstRow(first.Value.Bounds, second.Value.Bounds) &&
                    HasRowIconVisual(fresh, second.Value.Bounds))
                {
                    string mode = source.TargetName.Length == 1 ? "compact exact" : "OCR exact";
                    Log?.Invoke(
                        $"[대량 채집] 생활 스킬 행 확인 · {source.Category}/{source.TargetName} · {mode} + 같은 행 아이콘");
                    return second;
                }
            }

            if (page + 1 >= maxPages)
                break;

            using (var frame = Capture(ct))
            {
                _ui.DragFresh(
                    LifeSkillListLayout.SafeDragStart,
                    LifeSkillListLayout.SafeDragEnd,
                    450,
                    ct);
            }
            await Task.Delay(420, ct);
        }

        return null;
    }

    private async Task<DetectionResult?> FindLifeSkillLabelAsync(
        Bitmap frame,
        Rectangle roi,
        string targetName,
        CancellationToken ct)
    {
        // Windows OCR is less reliable for a single Hangul syllable such as 밀/콩/쌀.
        // Keep exact matching, but use the compact-label recognizer for one-syllable
        // life-skill names. Longer names retain the existing dim-text exact matcher.
        if (targetName.Length == 1)
        {
            var compact = await _ui.Ocr.FindCompactLabelAsync(frame, roi, targetName, ct);
            return compact.Found ? compact : null;
        }

        return await FindUniqueAsync(frame, roi, targetName, ct);
    }

    private static bool HasRowIconVisual(Bitmap frame, Rectangle textBounds)
    {
        // The screenshots show a distinct gatherable icon immediately to the left
        // of each OCR name. The actual template crops are version-dependent, so the
        // safe in-code second signal is that this same-row icon cell contains
        // non-flat colored/edge detail on both fresh frames.
        int top = Math.Max(0, textBounds.Top - 30);
        int bottom = Math.Min(frame.Height, textBounds.Bottom + 30);
        int right = Math.Max(1, textBounds.Left - 8);
        int left = Math.Max(0, right - 115);
        if (right - left < 30 || bottom - top < 30)
            return false;

        int min = 255, max = 0, saturated = 0, sampled = 0;
        for (int y = top; y < bottom; y += 4)
        for (int x = left; x < right; x += 4)
        {
            Color p = frame.GetPixel(x, y);
            int bright = (p.R + p.G + p.B) / 3;
            min = Math.Min(min, bright);
            max = Math.Max(max, bright);
            if (Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) >= 28)
                saturated++;
            sampled++;
        }

        return sampled > 0 && max - min >= 32 && saturated * 100 >= sampled * 3;
    }

    private async Task<bool> WaitForLifeSkillHundredStopOrTargetAsync(
        string displayName,
        long before,
        long targetTotal,
        TimeSpan absoluteTimeout,
        CancellationToken ct)
    {
        DateTime startedAt = DateTime.UtcNow;
        DateTime deadline = startedAt + absoluteTimeout;
        DateTime lastProgressAt = startedAt;
        long last = before;
        bool sawActive = false;
        bool sawGain = false;
        int stableIdle = 0;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var activity = await _data.ActivityAsync(ct);
            if (!GatheringSafetyPolicy.IsSafeField(activity))
                throw new InvalidOperationException(
                    "생활 스킬 100회 채집 중 사망·대화·던전 등 진행 불가 상태가 확인되어 정지합니다.");
            activity = await WaitForCombatEndAsync(activity, $"{displayName} 100회 진행", ct);
            long current = await _data.ItemCountAsync(displayName, ct);
            long gain = current - before;

            if (current < before)
                throw new InvalidOperationException(
                    $"{displayName} 생활 스킬 100회 중 재고가 감소해 정지합니다.");

            if (current != last)
            {
                Log?.Invoke(
                    $"[대량 채집] {displayName} 100회 수량 변화 · {before}→{current} · +{gain}");
                last = current;
                lastProgressAt = DateTime.UtcNow;
                sawGain = gain > 0;
            }

            bool active = IsOwnedLifeSkillActivity(activity);
            sawActive |= active;

            if (current >= targetTotal)
            {
                Log?.Invoke(
                    $"[대량 채집] 목표 재료 확보 · {displayName} 현재 {current} / 목표 {targetTotal} · " +
                    "100회 자연 종료를 기다리지 않고 안전 정지");

                if (active)
                {
                    await StopAsync(ct);
                    Log?.Invoke(
                        $"[대량 채집] 목표 수량 도달 · Stop 버튼+CLI 상태 확인 후 Space 정지 입력 · {displayName}");
                }

                await ConfirmLifeSkillStoppedAsync(displayName, ct);
                long final = await _data.ItemCountAsync(displayName, ct);
                if (final < targetTotal)
                    throw new InvalidOperationException(
                        $"{displayName} 목표 수량 도달 후 정지 검증 중 재고가 목표 아래로 감소했습니다: " +
                        $"{final}/{targetTotal}");

                _stage.Move(
                    ProductionStage.VerifyInventory,
                    $"{displayName} 목표 확보 후 채집 종료 · {final}/{targetTotal}");
                Log?.Invoke(
                    $"[대량 채집] 목표 수량 안전 종료 확인 · {displayName} {final}/{targetTotal} · " +
                    "호출한 가공 단계로 복귀");
                return true;
            }

            if (sawGain && !active)
            {
                stableIdle++;
                if ((sawActive && stableIdle >= 3) ||
                    (!sawActive &&
                     DateTime.UtcNow - startedAt >= TimeSpan.FromSeconds(20) &&
                     DateTime.UtcNow - lastProgressAt >= TimeSpan.FromSeconds(12)))
                {
                    _stage.Move(ProductionStage.VerifyInventory, $"{displayName} 100회 자연 종료 · +{gain}");
                    return false;
                }
            }
            else
            {
                stableIdle = 0;
            }

            // Do not fail merely because a 100-action cycle is slow. The live
            // V3.0.32 log was still gaining logs at the old 10-minute boundary.
            // Only a long no-progress period while the action remains active is a
            // real stall; the absolute timeout is a final safety bound.
            if (sawGain &&
                DateTime.UtcNow - lastProgressAt >= TimeSpan.FromMinutes(5))
                throw new InvalidOperationException(
                    $"{displayName} 생활 스킬 채집 수량이 5분 이상 증가하지 않아 정지합니다. " +
                    $"현재 {current}, 이번 주기 +{gain}.");

            await Task.Delay(1000, ct);
        }

        long finalCount = await _data.ItemCountAsync(displayName, ct);
        throw new InvalidOperationException(
            $"{displayName} 생활 스킬 채집이 30분 안전 한도를 넘었습니다. 수량 {before}→{finalCount}.");
    }

    private static bool IsOwnedLifeSkillActivity(GatheringActivity activity)
        => activity.IsAutoTraveling ||
           activity.IsGathering ||
           activity.IsFishing ||
           activity.MainButtonState == "Stop";

    private async Task<GatheringActivity> WaitForCombatEndAsync(
        GatheringActivity state,
        string phase,
        CancellationToken ct)
    {
        int polls = 0;
        while (GatheringSafetyPolicy.ShouldWaitForCombat(state))
        {
            polls++;
            if (polls == 1 || polls % 5 == 0)
                Log?.Invoke($"[대량 채집] 전투 중 · 입력 없이 종료 대기 · {phase}");
            await Task.Delay(1000, ct);
            state = await _data.ActivityAsync(ct);
            if (!GatheringSafetyPolicy.IsSafeField(state))
                throw new InvalidOperationException(
                    $"{phase} 전투 대기 중 사망·대화·던전 등 진행 불가 상태가 확인되었습니다.");
        }

        if (polls > 0)
            Log?.Invoke($"[대량 채집] 전투 종료 · 기존 채집 흐름 계속 · {phase}");
        return state;
    }

    private async Task ConfirmLifeSkillStoppedAsync(
        string displayName,
        CancellationToken ct)
    {
        int stableStopped = 0;
        for (int attempt = 1; attempt <= 12; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var activity = await _data.ActivityAsync(ct);
            if (!GatheringSafetyPolicy.IsSafeField(activity))
                throw new InvalidOperationException(
                    $"{displayName} 목표 수량 정지 확인 중 사망·대화·던전 등 진행 불가 상태가 감지되었습니다.");
            activity = await WaitForCombatEndAsync(activity, $"{displayName} 정지 확인", ct);

            if (LifeSkillStopPolicy.IsStoppedAfterSpace(activity))
            {
                stableStopped++;
                Log?.Invoke(
                    $"[대량 채집] Space 후 종료 확인 {stableStopped}/2 · " +
                    $"Gathering={activity.IsGathering}, AutoTraveling={activity.IsAutoTraveling}, Fishing={activity.IsFishing}, " +
                    $"MainButton={activity.MainButtonState}(판정 제외)");
                if (stableStopped >= 2)
                {
                    Log?.Invoke(
                        $"[대량 채집] 채집/이동 종료 CLI 2회 확인 · {displayName} · " +
                        "MainButtonState 잔상은 종료 판정에서 제외");
                    return;
                }
            }
            else
            {
                stableStopped = 0;
            }

            await Task.Delay(500, ct);
        }

        throw new InvalidOperationException(
            $"{displayName} 목표 수량 도달 후 Space 정지 입력은 보냈지만 채집/이동 종료를 확인하지 못했습니다.");
    }

    private async Task StartInventoryHundredQuestAsync(string displayName, CancellationToken ct)
    {
        if (_stage.Current == ProductionStage.VerifyInventory)
            _stage.Move(ProductionStage.OpenHub, $"가방 보조 경로 반복 · {displayName}");
        else if (_stage.Current != ProductionStage.OpenHub)
            _stage.Move(ProductionStage.OpenHub, $"가방 보조 경로 · {displayName}");
        // Fallback only. I: inventory. Search always follows the shared rule:
        // magnifier -> input -> text -> Enter -> Apply -> exact result.
        _ui.TapFresh(0x17, ct);
        await Task.Delay(700, ct);

        await ClickExactAsync("아이템", new Rectangle(210, 810, 390, 175), ct,
            "가방 하단 아이템 탭을 확인하지 못했습니다.");
        await Task.Delay(350, ct);

        using (var frame = Capture(ct))
        {
            var tab = await FindUniqueAsync(frame, new Rectangle(210, 810, 390, 175), "아이템", ct)
                ?? throw Fail(frame, "아이템 탭 활성 상태를 확인하지 못했습니다.");
            var roi = Rectangle.Intersect(
                Rectangle.Inflate(tab.Bounds, 12, 12),
                new Rectangle(Point.Empty, frame.Size));
            int orange = 0;
            for (int y = roi.Top; y < roi.Bottom; y += 2)
            for (int x = roi.Left; x < roi.Right; x += 2)
            {
                var color = frame.GetPixel(x, y);
                if (color.R >= 170 && color.G >= 65 &&
                    color.G < color.R - 25 && color.B < color.G - 20)
                    orange++;
            }
            if (orange < 12)
                throw Fail(frame, "가방 아이템 탭의 주황색 활성 표시를 확인하지 못했습니다.");
        }

        _ = await _ui.ClickOffsetFromStableExactAsync(
            "전체",
            new Rectangle(25, 470, 300, 180),
            all => new Point(Math.Max(22, all.Bounds.Left - 45), all.Center.Y),
            ct,
            "가방 아이템 탭의 전체 필터/검색 아이콘 기준점을 확인하지 못했습니다.",
            dimText: true);
        await Task.Delay(400, ct);

        _stage.Move(ProductionStage.Search, displayName);
        await ProductionSearchFlow.SearchAndSelectAsync(
            _ui,
            "아이템 이름을 검색해 보세요",
            new Rectangle(80, 470, 650, 220),
            displayName,
            new Rectangle(45, 535, 710, 310),
            ct,
            "가방",
            text => Log?.Invoke(text));
        _stage.Move(ProductionStage.Detail, displayName);
        await Task.Delay(450, ct);

        long bagCount = await _inventory.InventoryOnlyCountAsync(displayName, ct);
        if (bagCount <= 0)
            throw new InvalidOperationException(
                $"{displayName} CLI 가방 수량이 0개로 바뀌어 상세 선택을 중단합니다.");

        using (var frame = Capture(ct))
        {
            var detailTitle = await FindUniqueAsync(
                frame, new Rectangle(70, 90, 660, 390), displayName, ct)
                ?? throw Fail(frame,
                    $"선택한 가방 상세 제목이 {displayName}과 일치하지 않습니다.");

            string countText = bagCount.ToString();
            int countLeft = Math.Clamp(
                detailTitle.Bounds.Right + 80, 20, Math.Max(20, frame.Width - 180));
            int countTop = Math.Clamp(
                detailTitle.Bounds.Top - 35, 20, Math.Max(20, frame.Height - 150));
            var countRoi = Rectangle.Intersect(
                new Rectangle(countLeft, countTop, frame.Width - countLeft - 20, 150),
                new Rectangle(Point.Empty, frame.Size));
            var count = await _ui.Ocr.FindCompactLabelAsync(frame, countRoi, countText, ct);
            if (!count.Found)
                throw Fail(frame,
                    $"가방 화면 수량과 CLI 수량({bagCount})을 함께 확인하지 못했습니다.");

            var method = await FindUniqueAsync(
                frame, new Rectangle(70, 250, 600, 300), "구하는 방법", ct)
                ?? throw Fail(frame, "가방 상세의 구하는 방법을 확인하지 못했습니다.");
            _ui.ClickFresh(method.Center, ct);
        }
        await Task.Delay(450, ct);

        _stage.Move(ProductionStage.Travel, $"{displayName} 보조 획득처");
        await ClickFirstLifeSkillMethodAsync(ct);
    }

    private async Task ClickFirstLifeSkillMethodAsync(CancellationToken ct)
    {
        using var frame = Capture(ct);
        var header = await FindUniqueAsync(
            frame, new Rectangle(45, 285, 500, 160), "구하는 방법", ct)
            ?? throw Fail(frame, "구하는 방법 목록을 확인하지 못했습니다.");

        var roi = Rectangle.Intersect(
            new Rectangle(55, header.Bounds.Bottom + 25, 690, 500),
            new Rectangle(Point.Empty, frame.Size));
        var lines = await _ui.Ocr.ReadLinesAsync(frame, roi, 3, ct);
        var blocked = new[]
        {
            "던전", "전리품", "임무", "레이드", "구하는방법", "선택하세요"
        };

        var candidate = lines
            .Where(x => x.Center.Y > roi.Top + 10)
            .Where(x => AcquisitionMethodPolicy.IsLifeSkill(x.ReadText))
            .Where(x =>
            {
                string normalized = (x.ReadText ?? "").Replace(" ", "");
                return blocked.All(word =>
                    !normalized.Contains(word, StringComparison.Ordinal));
            })
            .OrderBy(x => x.Center.Y)
            .ThenBy(x => x.Center.X)
            .FirstOrDefault();

        if (!candidate.Found)
            throw Fail(frame,
                "구하는 방법에서 생활 스킬 획득 항목을 찾지 못했습니다.");

        Log?.Invoke(
            "[대량 채집] 보조 경로 · 생활 스킬 획득처 선택 · " +
            (candidate.ReadText ?? "첫 항목"));
        _ui.ClickFresh(new Point(390, candidate.Center.Y), ct);
        await Task.Delay(700, ct);
    }

    private async Task WaitForNaturalStopAndInventoryAsync(
        string displayName,
        long before,
        long minimumGain,
        TimeSpan timeout,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        long last = before;
        int stableIdle = 0;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var activity = await _data.ActivityAsync(ct);
            if (!GatheringSafetyPolicy.IsSafeField(activity))
                throw new InvalidOperationException(
                    "대량 채집 중 사망·대화·던전 등 진행 불가 상태가 확인되어 정지합니다.");
            activity = await WaitForCombatEndAsync(activity, $"{displayName} 보조 경로", ct);
            long current = await _data.ItemCountAsync(displayName, ct);
            long gain = current - before;

            if (current != last)
            {
                Log?.Invoke(
                    $"[대량 채집] {displayName} 수량 변화 · {before}→{current} · +{gain}");
                last = current;
            }

            bool active = activity.IsAutoTraveling || activity.IsGathering || activity.IsFishing ||
                          activity.MainButtonState == "Stop";
            if (gain >= minimumGain && !active)
            {
                stableIdle++;
                if (stableIdle >= 2)
                {
                    _stage.Move(ProductionStage.VerifyInventory, $"{displayName} 보조 경로 종료 · +{gain}");
                    return;
                }
            }
            else
            {
                stableIdle = 0;
            }

            await Task.Delay(1000, ct);
        }

        long final = await _data.ItemCountAsync(displayName, ct);
        throw new InvalidOperationException(
            $"{displayName} 100개 채집의 자연 종료를 확인하지 못했습니다. 수량 {before}→{final}, 최소 증가 {minimumGain}.");
    }

    private async Task ClickExactAsync(
        string text,
        Rectangle roi,
        CancellationToken ct,
        string failure,
        string? pasteText = null)
    {
        if (pasteText is null)
        {
            _ = await _ui.ClickStableExactAsync(
                text, roi, ct, failure, dimText: true);
        }
        else
        {
            _ = await _ui.ClickStableExactAndPasteAsync(
                text, roi, pasteText, ct, failure, dimText: true);
        }
    }

    private Task<DetectionResult?> FindUniqueAsync(
        Bitmap frame,
        Rectangle roi,
        string text,
        CancellationToken ct)
        => _ui.FindUniqueAsync(frame, roi, text, ct, dimText: true);

    public async Task StopAsync(CancellationToken ct)
    {
        var state = await _data.ActivityAsync(ct);
        if (!state.IsGathering && !state.IsAutoTraveling && !state.IsFishing)
            return;

        using var frame = Capture(ct);
        if (state.MainButtonState != "Stop" || !GatheringVision.HasStopButton(frame))
            throw Fail(frame,
                "대량 채집 정지 버튼을 확인하지 못했습니다. 게임에서 직접 정지하세요.");
        _ui.TapFresh(0x39, ct);
    }

    private InvalidOperationException Fail(Bitmap frame, string message)
        => _ui.Failure(frame, message);

    public void Dispose()
    {
        _fallback.Dispose();
        _ui.Dispose();
    }
}
