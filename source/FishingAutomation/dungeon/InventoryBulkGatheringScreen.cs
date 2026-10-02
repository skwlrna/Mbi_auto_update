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
        if (LivingSkillGatheringCatalog.TryResolveBulk(plan.DisplayName, out var source))
        {
            _stage.Move(ProductionStage.OpenHub, $"생활 스킬 · {source.Category}");
            Log?.Invoke(
                $"[대량 채집] 생활 스킬 100회 우선 · {plan.DisplayName} → {source.Category}/{source.TargetName}");
            var automation = new LifeSkillBulkGatheringAutomation(
                token => _data.ItemCountAsync(plan.DisplayName, token),
                token => StartLifeSkillHundredAsync(source, token),
                (before, token) => WaitForLifeSkillHundredStopAndInventoryAsync(
                    plan.DisplayName, before, TimeSpan.FromMinutes(10), token));
            automation.Log += text => Log?.Invoke(text);
            await automation.RunAsync(plan, ct);
            return;
        }

        Log?.Invoke(
            $"[대량 채집] {plan.DisplayName} 생활 스킬 직접 매핑 없음 · 가방 100개 경로를 보조 경로로 사용");
        var fallbackAutomation = new InventoryBulkGatheringAutomation(
            token => _data.ItemCountAsync(plan.DisplayName, token),
            token => _inventory.InventoryOnlyCountAsync(plan.DisplayName, token),
            async token =>
            {
                if (plan.SourceRecipe is null)
                    throw new InvalidOperationException(
                        $"{plan.DisplayName} 최초 확보에 필요한 생활 스킬/가공 재료 시작 제법을 찾지 못했습니다.");
                await _fallback.StartAsync(plan with { TargetQuantity = 1 }, token);
            },
            token => StartInventoryHundredQuestAsync(plan.DisplayName, token),
            (before, minimum, token) => WaitForNaturalStopAndInventoryAsync(
                plan.DisplayName, before, minimum,
                minimum == 1 ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(6), token));
        fallbackAutomation.Log += text => Log?.Invoke(text);
        await fallbackAutomation.RunAsync(plan, ct);
    }

    private async Task StartLifeSkillHundredAsync(
        LivingSkillGatheringSource source,
        CancellationToken ct)
    {
        if (_stage.Current is not ProductionStage.OpenHub and not ProductionStage.VerifyInventory)
            _stage.Move(ProductionStage.OpenHub, $"생활 스킬 · {source.Category}");
        else if (_stage.Current == ProductionStage.VerifyInventory)
            _stage.Move(ProductionStage.OpenHub, $"생활 스킬 반복 · {source.Category}");
        // C = profile. No fixed list-row Y is used after this shortcut.
        _ui.TapFresh(0x2E, ct);
        await Task.Delay(700, ct);

        await ClickExactAsync(
            "생활 스킬",
            new Rectangle(20, 70, 760, 860),
            ct,
            "프로필에서 생활 스킬 메뉴를 확인하지 못했습니다.");
        await Task.Delay(550, ct);

        _stage.Move(ProductionStage.SelectCategory, source.Category);
        await ClickExactAsync(
            source.Category,
            new Rectangle(20, 100, 760, 820),
            ct,
            $"생활 스킬 분류 {source.Category}을(를) 확인하지 못했습니다.");
        await Task.Delay(450, ct);

        _stage.Move(ProductionStage.Search, source.TargetName);
        var row = await FindStableLifeSkillRowAsync(source, ct);
        if (row is null)
        {
            using var failed = Capture(ct);
            throw Fail(failed,
                $"{source.Category} 목록에서 {source.TargetName} 행을 OCR+아이콘 구조로 확인하지 못했습니다.");
        }

        using (var frame = Capture(ct))
        {
            // Reconfirm the exact label and same-row icon immediately before input.
            var exact = await FindUniqueAsync(
                frame,
                Rectangle.Intersect(
                    new Rectangle(150, Math.Max(90, row.Value.Bounds.Top - 50), 630, 100),
                    new Rectangle(Point.Empty, frame.Size)),
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
        await Task.Delay(500, ct);

        // Depending on the live detail layout, "100회 채집" may be an explicit
        // selector/button or the selected life-skill row may already represent it.
        bool hundredConfirmed = false;
        using (var frame = Capture(ct))
        {
            var hundred = await FindUniqueAsync(
                frame, new Rectangle(60, 160, 680, 700), "100회 채집", ct);
            if (hundred is not null)
            {
                _ui.ClickFresh(hundred.Value.Center, ct);
                hundredConfirmed = true;
            }
            else
            {
                var lines = await _ui.Ocr.ReadLinesAsync(
                    frame, new Rectangle(40, 120, 720, 760), 3, ct);
                hundredConfirmed = lines.Any(x =>
                    (x.ReadText ?? "").Replace(" ", "")
                        .Contains("100회", StringComparison.Ordinal));
            }
        }

        if (!hundredConfirmed)
        {
            using var failed = Capture(ct);
            throw Fail(failed,
                $"{source.TargetName} 생활 스킬에서 100회 채집 상태를 확인하지 못했습니다.");
        }

        _stage.Move(ProductionStage.Detail, source.TargetName);
        await Task.Delay(350, ct);
        _stage.Move(ProductionStage.Travel, $"{source.TargetName} 가까운 위치");
        await ClickExactAsync(
            "가까운 위치 찾기",
            new Rectangle(50, 180, 700, 760),
            ct,
            $"{source.TargetName}의 가까운 위치 찾기 버튼을 확인하지 못했습니다.");

        Log?.Invoke(
            $"[대량 채집] {source.Category} · {source.TargetName} · 100회 채집 경로 시작 · 추가 Space 입력 없음");
        await Task.Delay(700, ct);
    }

    private async Task<DetectionResult?> FindStableLifeSkillRowAsync(
        LivingSkillGatheringSource source,
        CancellationToken ct)
    {
        // Start at the category's current list position and scroll only the list area.
        // Individual gatherables never use a hard-coded Y coordinate.
        var listRoi = new Rectangle(150, 145, 630, 700);
        for (int page = 0; page < 9; page++)
        {
            ct.ThrowIfCancellationRequested();

            DetectionResult? first = null;
            using (var frame = Capture(ct))
            {
                var found = await _ui.Ocr.FindAlteringLabelsAsync(
                    frame, listRoi, source.TargetName, ct, dimText: true);
                if (found.Count == 1 && HasRowIconVisual(frame, found[0].Bounds))
                    first = found[0];
            }

            if (first is not null)
            {
                await Task.Delay(160, ct);
                using var fresh = Capture(ct);
                var found = await _ui.Ocr.FindAlteringLabelsAsync(
                    fresh, listRoi, source.TargetName, ct, dimText: true);
                if (found.Count == 1 &&
                    GatheringNavigationPolicy.IsStableFirstRow(first.Value.Bounds, found[0].Bounds) &&
                    HasRowIconVisual(fresh, found[0].Bounds))
                {
                    Log?.Invoke(
                        $"[대량 채집] 생활 스킬 행 확인 · {source.Category}/{source.TargetName} · OCR exact + 같은 행 아이콘");
                    return found[0];
                }
            }

            if (page == 8)
                break;

            using (var frame = Capture(ct))
            {
                _ui.DragFresh(
                    new Point(715, 765),
                    new Point(715, 345),
                    450,
                    ct);
            }
            await Task.Delay(420, ct);
        }

        return null;
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

    private async Task WaitForLifeSkillHundredStopAndInventoryAsync(
        string displayName,
        long before,
        TimeSpan timeout,
        CancellationToken ct)
    {
        DateTime startedAt = DateTime.UtcNow;
        DateTime deadline = startedAt + timeout;
        DateTime lastProgressAt = startedAt;
        long last = before;
        bool sawActive = false;
        bool sawGain = false;
        int stableIdle = 0;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            long current = await _data.ItemCountAsync(displayName, ct);
            var activity = await _data.ActivityAsync(ct);
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

            bool active = activity.IsAutoTraveling || activity.IsGathering || activity.IsFishing ||
                          activity.MainButtonState == "Stop";
            sawActive |= active;

            if (!activity.IsSafeField)
                throw new InvalidOperationException(
                    "생활 스킬 100회 채집 중 전투·대화 등 안전하지 않은 상태가 확인되어 정지합니다.");

            if (sawGain && !active)
            {
                stableIdle++;
                if ((sawActive && stableIdle >= 3) ||
                    (!sawActive &&
                     DateTime.UtcNow - startedAt >= TimeSpan.FromSeconds(20) &&
                     DateTime.UtcNow - lastProgressAt >= TimeSpan.FromSeconds(12)))
                {
                    _stage.Move(ProductionStage.VerifyInventory, $"{displayName} 100회 종료 · +{gain}");
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
            $"{displayName} 생활 스킬 100회의 자연 종료를 확인하지 못했습니다. 수량 {before}→{final}.");
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

        DetectionResult all;
        using (var frame = Capture(ct))
        {
            all = await FindUniqueAsync(frame, new Rectangle(25, 470, 300, 180), "전체", ct)
                ?? throw Fail(frame, "가방 아이템 탭의 전체 필터를 확인하지 못했습니다.");
            var search = new Point(Math.Max(22, all.Bounds.Left - 45), all.Center.Y);
            _ui.ClickFresh(search, ct);
        }
        await Task.Delay(400, ct);

        _stage.Move(ProductionStage.Search, displayName);
        await ClickExactAsync(
            "아이템 이름을 검색해 보세요",
            new Rectangle(80, 470, 650, 170),
            ct,
            "가방 검색 입력칸을 확인하지 못했습니다.",
            pasteText: displayName);
        await Task.Delay(120, ct);

        // Every item/material/product search confirms typed text with Enter first.
        _ui.TapFresh(0x1C, ct);
        await Task.Delay(220, ct);

        bool applied = false;
        for (int pass = 0; pass < 2 && !applied; pass++)
        {
            using var frame = Capture(ct);
            var apply = await FindUniqueAsync(
                frame, new Rectangle(80, 470, 650, 220), "적용하기", ct);
            if (apply is null)
                break;
            if (pass == 0)
            {
                await Task.Delay(130, ct);
                continue;
            }
            _ui.ClickFresh(apply.Value.Center, ct);
            applied = true;
        }
        if (!applied)
        {
            using var frame = Capture(ct);
            _ui.TapFresh(0x39, ct); // Enter confirmed; Space applies the active button.
        }
        await Task.Delay(650, ct);

        await ClickExactAsync(displayName, new Rectangle(45, 535, 710, 310), ct,
            $"가방 검색 결과에서 정확한 {displayName} 항목을 찾지 못했습니다.");
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
            long current = await _data.ItemCountAsync(displayName, ct);
            var activity = await _data.ActivityAsync(ct);
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

            if (!activity.IsSafeField)
                throw new InvalidOperationException(
                    "대량 채집 중 전투·대화 등 안전하지 않은 상태가 확인되어 정지합니다.");

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
