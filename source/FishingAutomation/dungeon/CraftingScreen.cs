using FishingAutomation;
using System.Text.RegularExpressions;

namespace DungeonVisionBot;

internal sealed class CraftingScreen : ICraftingScreen
{
    private readonly CraftingCliData _data;
    private readonly GatheringCliData _activity;
    private readonly ProductionUiRuntime _ui;
    private readonly ProductionStageMachine _stage = new("제작");

    internal CraftingScreen(
        nint hwnd,
        AppSettings settings,
        string debugDir,
        MabinogiMobileCli cli)
    {
        _data = new CraftingCliData(cli);
        _activity = new GatheringCliData(cli);
        _ui = new ProductionUiRuntime(hwnd, settings, debugDir, "crafting");
        _stage.Changed += (stage, detail) =>
            Log?.Invoke($"[제작][상태] {stage} · {detail}");
    }

    public string InputMode => _ui.InputMode;
    public event Action<string>? Log;

    private Bitmap Capture(CancellationToken ct) => _ui.Capture(ct);

    public async Task CreateQuestAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
    {
        plan.Validate();
        if (craftCount is < 1 or > 10)
            throw new InvalidDataException("제작 퀘스트는 한 번에 1~10회만 만들 수 있습니다.");

        await OpenProductAsync(plan, ct);
        await SetCraftCountAsync(craftCount, ct);
        _stage.Move(ProductionStage.CreateQuest, $"{plan.DisplayName} {craftCount}회 퀘스트 생성");

        await ClickExactAsync(
            "퀘스트 만들기",
            new Rectangle(0, 0, 800, 1000),
            ct,
            "제작 상세 화면의 퀘스트 만들기 버튼을 확인하지 못했습니다.");

        await Task.Delay(800, ct);
        Log?.Invoke($"[제작] {plan.DisplayName} {craftCount}회 퀘스트 생성");
    }

    private async Task OpenProductAsync(CraftingPlan plan, CancellationToken ct)
    {
        _stage.Move(ProductionStage.OpenHub, $"{plan.DisplayName} 제작 허브 진입");
        // If a matching detail sheet is already open, reuse it. The detail OCR
        // can take long enough for animated field pixels/players/chat to change.
        // Never compare the entire field frame after that OCR: K is a harmless
        // navigation shortcut and only needs a fresh 800x1000/window guard.
        using (var current = Capture(ct))
        {
            if (await IsProductDetailAsync(current, plan.DisplayName, ct))
                return;
        }

        using (var fresh = Capture(ct))
        {
            _ui.TapFresh(0x25, ct); // K: 가공/제작 허브
            Log?.Invoke("[제작] 제작 허브 열기 · K 입력 · fresh frame");
        }
        await Task.Delay(750, ct);

        // The user-confirmed layout keeps 제작 immediately to the right of 가공.
        // OCR first, then click the fresh exact label inside the fixed bottom navigation.
        await ClickExactAsync(
            "제작",
            new Rectangle(285, 850, 320, 145),
            ct,
            "하단 제작 탭을 확인하지 못했습니다.");
        await Task.Delay(650, ct);

        string category = plan.Category == CraftingCategory.Food ? "음식" : "아이템";
        _stage.Move(ProductionStage.SelectCategory, category);
        await ClickCraftingCategoryCardAsync(plan.Category, category, ct);
        await Task.Delay(650, ct);

        _stage.Move(ProductionStage.Search, plan.DisplayName);
        await SearchProductAsync(plan.DisplayName, category, ct);
        _stage.Move(ProductionStage.Detail, plan.DisplayName);
    }

    private async Task ClickCraftingCategoryCardAsync(
        CraftingCategory category,
        string label,
        CancellationToken ct)
    {
        Rectangle titleArea = CraftingHubLayout.CategoryTitleArea(category);
        Rectangle cardArea = CraftingHubLayout.CategoryCardArea(category);
        Point fallbackPoint = CraftingHubLayout.CategoryClickPoint(category);
        if (titleArea.IsEmpty || cardArea.IsEmpty ||
            !CraftingHubLayout.IsSafeFallbackPoint(category))
            throw new InvalidOperationException("지원하지 않는 제작 허브 분류입니다.");

        DetectionResult? firstHeader = null;
        DetectionResult? firstCategory = null;
        bool firstTitleSignal = false;

        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);

            var header = await _ui.Ocr.FindCompactLabelAsync(
                frame, CraftingHubLayout.HubHeaderArea, "제작", ct);
            if (!header.Found)
            {
                var headerExact = await _ui.Ocr.FindAlteringLabelsAsync(
                    frame,
                    CraftingHubLayout.HubHeaderArea,
                    "제작",
                    ct,
                    acceptedBounds: CraftingHubLayout.HubHeaderArea,
                    dimText: true);
                if (headerExact.Count != 1)
                    throw Fail(frame,
                        "제작 허브 상단 제목을 확인하지 못해 카드 클릭을 중단합니다.");
                header = headerExact[0];
            }

            var compact = await _ui.Ocr.FindCompactLabelAsync(
                frame, titleArea, label, ct);
            DetectionResult? categoryFound = compact.Found ? compact : null;
            bool titleSignal = HasBrightTitleSignal(frame, titleArea);

            if (pass == 0)
            {
                firstHeader = header;
                firstCategory = categoryFound;
                firstTitleSignal = titleSignal;
                await Task.Delay(170, ct);
                continue;
            }

            if (firstHeader is null ||
                !CraftingHubLayout.IsStableTitle(firstHeader.Value.Bounds, header.Bounds))
                throw Fail(frame,
                    "제작 허브 상단 제목 위치가 두 프레임에서 안정적으로 일치하지 않았습니다.");

            // Preferred path: exact compact OCR on the small title band in both frames.
            if (firstCategory is not null && categoryFound is not null &&
                CraftingHubLayout.IsStableTitle(
                    firstCategory.Value.Bounds, categoryFound.Value.Bounds))
            {
                _ui.ClickFresh(categoryFound.Value.Center, ct);
                Log?.Invoke(
                    $"[제작] 제작 허브 카드 확인 · {label} · 제목 compact OCR 2프레임");
                return;
            }

            // Live V2.0.4 evidence: Windows OCR can miss the clearly visible
            // 아이템 title even though the fixed 800x1000 hub is correct. For this
            // navigation-only click, a stable 제작 header plus visible bright title
            // signal authorizes the user-confirmed card center. No crafting or paid
            // action happens on this click.
            if (!firstTitleSignal || !titleSignal)
                throw Fail(frame,
                    $"제작 허브의 {label} 카드 제목 OCR이 실패했고 제목 시각 신호도 확인하지 못했습니다.");

            var guardArea = Rectangle.Intersect(
                Rectangle.Inflate(titleArea, 12, 12),
                new Rectangle(Point.Empty, frame.Size));
            _ui.ClickFresh(fallbackPoint, ct);
            Log?.Invoke(
                $"[제작] 제작 허브 카드 확인 · {label} · OCR 미검출 → 800x1000 검증 좌표 fallback ({fallbackPoint.X},{fallbackPoint.Y})");
            return;
        }

        using var failed = Capture(ct);
        throw Fail(failed, $"제작 허브에서 {label} 카드를 확인하지 못했습니다.");
    }

    private static bool HasBrightTitleSignal(Bitmap frame, Rectangle area)
    {
        area = Rectangle.Intersect(area, new Rectangle(Point.Empty, frame.Size));
        if (area.Width < 20 || area.Height < 20)
            return false;

        int brightNeutral = 0;
        int sampled = 0;
        for (int y = area.Top; y < area.Bottom; y += 2)
        for (int x = area.Left; x < area.Right; x += 2)
        {
            Color p = frame.GetPixel(x, y);
            sampled++;
            int max = Math.Max(p.R, Math.Max(p.G, p.B));
            int min = Math.Min(p.R, Math.Min(p.G, p.B));
            if (p.R >= 175 && p.G >= 175 && p.B >= 175 && max - min <= 55)
                brightNeutral++;
        }

        return sampled > 0 && brightNeutral >= 24;
    }

    private async Task SearchProductAsync(string displayName, string category, CancellationToken ct)
    {
        using (var frame = Capture(ct))
        {
            if (await FindUniqueAsync(frame, new Rectangle(15, 20, 300, 100), category, ct) is null)
                throw Fail(frame, $"{category} 제작 목록 화면을 확인하지 못했습니다.");

            var all = await FindUniqueAsync(frame, new Rectangle(35, 75, 220, 120), "전체", ct)
                ?? throw Fail(frame, "제작 목록의 전체 필터를 확인하지 못했습니다.");
            _ui.ClickFresh(new Point(Math.Max(18, all.Bounds.Left - 42), all.Center.Y), ct);
        }
        await Task.Delay(400, ct);

        await ClickExactAsync(
            "결과물 또는 재료 이름을 검색해 보세요",
            new Rectangle(65, 350, 675, 360),
            ct,
            "제작 검색 입력칸을 확인하지 못했습니다.",
            pasteText: displayName);
        await Task.Delay(120, ct);
        using (var frame = Capture(ct))
            _ui.TapFresh(0x1C, ct); // Enter: 검색어 입력 확정
        await Task.Delay(200, ct);
        await ClickExactAsync(
            "적용하기",
            new Rectangle(65, 350, 675, 360),
            ct,
            "Enter 입력 후 제작 검색 적용하기 버튼을 확인하지 못했습니다.");
        await Task.Delay(650, ct);

        await ClickExactAsync(
            displayName,
            new Rectangle(35, 390, 730, 535),
            ct,
            $"제작 검색 결과에서 정확한 {displayName} 품목을 찾지 못했습니다.");
        await Task.Delay(500, ct);

        using var verify = Capture(ct);
        if (!await IsProductDetailAsync(verify, displayName, ct))
            throw Fail(verify, $"선택 후 {displayName} 제작 상세 화면을 확인하지 못했습니다.");
    }

    private async Task<bool> IsProductDetailAsync(Bitmap frame, string displayName, CancellationToken ct)
    {
        var client = new Rectangle(Point.Empty, frame.Size);
        var title = await FindUniqueAsync(frame, client, displayName, ct);
        if (title is null) return false;
        var materials = await FindUniqueAsync(frame, client, "필요한 재료", ct);
        var quest = await FindUniqueAsync(frame, client, "퀘스트 만들기", ct);
        return materials is not null && quest is not null &&
            title.Value.Center.Y < materials.Value.Center.Y &&
            materials.Value.Center.Y < quest.Value.Center.Y;
    }

    private async Task SetCraftCountAsync(int wanted, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            using var frame = Capture(ct);
            var count = await ReadCraftCountAsync(frame, ct);
            if (count is null)
                throw Fail(frame, "제작 횟수 표시를 확인하지 못했습니다.");
            if (count.Value.Count == wanted)
            {
                Log?.Invoke($"[제작] 배치 횟수 확인 · {wanted}회");
                return;
            }

            int direction = wanted > count.Value.Count ? 1 : -1;
            var click = new Point(
                Math.Clamp(count.Value.Center.X + direction * 125, 20, 779),
                Math.Clamp(count.Value.Center.Y, 20, 979));
            _ui.ClickFresh(click, ct);
            await Task.Delay(180, ct);
        }

        using var failed = Capture(ct);
        throw Fail(failed, $"제작 횟수를 {wanted}회로 맞추지 못했습니다.");
    }

    private async Task<(int Count, Point Center)?> ReadCraftCountAsync(Bitmap frame, CancellationToken ct)
    {
        var client = new Rectangle(Point.Empty, frame.Size);
        var materials = await FindUniqueAsync(frame, client, "필요한 재료", ct);
        var quest = await FindUniqueAsync(frame, client, "퀘스트 만들기", ct);
        if (materials is null || quest is null || materials.Value.Bounds.Bottom >= quest.Value.Bounds.Top)
            return null;
        // Derive the count region from labels in this fresh 800x1000 client frame.
        // Uploaded partial captures never supply an origin or a saved click point.
        var countArea = Rectangle.FromLTRB(0, materials.Value.Bounds.Bottom,
            frame.Width, quest.Value.Bounds.Top);
        var lines = await _ui.Ocr.ReadLinesAsync(frame, countArea, 3, ct);
        var candidates = new List<(int Count, Point Center)>();
        foreach (var line in lines.OrderBy(x => x.Center.Y))
        {
            var match = Regex.Match((line.ReadText ?? "").Replace(" ", ""), @"^(?<n>\d{1,2})회$");
            if (!match.Success || !int.TryParse(match.Groups["n"].Value, out int count) || count is < 1 or > 10)
                continue;
            candidates.Add((count, line.Center));
        }
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public async Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(
        CraftingPlan plan,
        CancellationToken ct)
    {
        _stage.Move(ProductionStage.ReadQuest, $"{plan.DisplayName} 부족 재료 확인");
        await CloseOverlayAsync(ct);
        using (var field = Capture(ct))
        {
            if (await FindQuestStageAsync(field, plan, directOnly: true, ct) is not null)
                throw Fail(field, "즉시 제작 퀘스트는 일반 제작대 복귀 경로로 실행하지 않습니다. 별도 화면 검증이 필요합니다.");
            if (await FindQuestStageAsync(field, plan, directOnly: false, ct) is not null &&
                (await _data.ExactAsync(plan.DisplayName, ct)).Craftable)
            {
                Log?.Invoke("[제작] 대상 제작대 단계 + CLI 재료 준비 확인");
                return Array.Empty<CraftingQuestDeficit>();
            }
        }
        await OpenQuestPopupAsync(plan, ct);
        using var frame = Capture(ct);
        var roi = new Rectangle(70, 130, 670, 520);
        var lines = (await _ui.Ocr.ReadLinesAsync(frame, roi, 3, ct))
            .OrderBy(x => x.Center.Y)
            .ThenBy(x => x.Center.X)
            .ToArray();

        var ratios = new List<(long Current, long Required, int Y, string Text)>();
        foreach (var line in lines)
        {
            string text = (line.ReadText ?? "").Replace(" ", "");
            var m = Regex.Match(text, @"(?<cur>\d+)\s*/\s*(?<req>\d+)");
            if (!m.Success ||
                !long.TryParse(m.Groups["cur"].Value, out long current) ||
                !long.TryParse(m.Groups["req"].Value, out long required) ||
                required <= 0)
                continue;
            ratios.Add((current, required, line.Center.Y, line.ReadText ?? ""));
        }

        var deficits = new List<CraftingQuestDeficit>();
        if (ratios.Count == 0)
            throw Fail(frame, "제작 퀘스트 재료 수량을 읽지 못했습니다. 재료 준비 완료로 간주하지 않습니다.");
        foreach (var ratio in ratios)
        {
            // The final station step also has 0/N; it is not a material row.
            if (ratio.Text.Replace(" ", "").Contains("제작대", StringComparison.Ordinal))
                continue;
            string inline = Regex.Replace(ratio.Text, @"\d+\s*/\s*\d+.*$", "").Trim();
            string? name = CleanName(inline);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = lines
                    .Where(x => Math.Abs(x.Center.Y - ratio.Y) <= 35)
                    .Where(x => x.Center.X < 470)
                    .Select(x => CleanName(x.ReadText ?? ""))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            }
            if (string.IsNullOrWhiteSpace(name))
                throw Fail(frame, "제작 퀘스트 재료 이름을 확인하지 못했습니다.");
            if (ratio.Current < ratio.Required)
                deficits.Add(new(name!, ratio.Current, ratio.Required, ratio.Y));
        }

        Log?.Invoke(deficits.Count == 0
            ? $"[제작] {plan.DisplayName} 퀘스트 부족 재료 없음"
            : "[제작] 퀘스트 부족 재료 · " +
              string.Join(", ", deficits.Select(x => $"{x.DisplayName} {x.Current}/{x.Required}")));
        return deficits;
    }

    private static string? CleanName(string text)
    {
        string cleaned = Regex.Replace(text, @"[^가-힣A-Za-z0-9+()\s]", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return null;
        string compact = cleaned.Replace(" ", "");
        string[] blocked = { "퀘스트", "필요한아이템", "구하는방법", "취소", "제작" };
        if (blocked.Any(x => compact.Contains(x, StringComparison.Ordinal))) return null;
        if (!Regex.IsMatch(cleaned, "[가-힣A-Za-z]", RegexOptions.CultureInvariant)) return null;
        return cleaned;
    }

    private async Task OpenQuestPopupAsync(CraftingPlan plan, CancellationToken ct)
    {
        // Ensure overlays from a previous acquisition are closed before reading the
        // persistent right-side production quest.
        await CloseOverlayAsync(ct);
        await Task.Delay(180, ct);

        using var frame = Capture(ct);
        var lines = await _ui.Ocr.ReadLinesAsync(frame, new Rectangle(500, 140, 300, 600), 3, ct);
        var quest = lines
            .Where(x => CraftingQuestText.IsTitle(x.ReadText ?? "", plan.DisplayName))
            .OrderBy(x => x.Center.Y)
            .FirstOrDefault();

        if (!quest.Found)
            throw Fail(frame, $"{plan.DisplayName} 제작 퀘스트를 오른쪽 목록에서 찾지 못했습니다.");

        _ui.ClickFresh(new Point(Math.Clamp(quest.Center.X, 535, 760), quest.Center.Y), ct);
        await Task.Delay(450, ct);
    }

    public async Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct)
    {
        _stage.Move(ProductionStage.AcquireMaterial, deficit.DisplayName);
        // The deficit popup is already open. Select the exact row by its paired OCR Y.
        using (var frame = Capture(ct))
        {
            var exact = await FindUniqueAsync(frame, new Rectangle(80, Math.Max(120, deficit.RowY - 45), 600, 90),
                deficit.DisplayName, ct);
            if (exact is null)
                throw Fail(frame, $"부족 재료 {deficit.DisplayName}의 정확한 행을 다시 확인하지 못했습니다.");
            _ui.ClickFresh(exact.Value.Center, ct);
        }
        await Task.Delay(450, ct);

        using (var frame = Capture(ct))
        {
            var header = await FindUniqueAsync(frame, new Rectangle(45, 230, 560, 180), "구하는 방법", ct)
                ?? throw Fail(frame, $"{deficit.DisplayName} 구하는 방법을 확인하지 못했습니다.");
            var roi = Rectangle.Intersect(
                new Rectangle(55, header.Bounds.Bottom + 20, 690, 520),
                new Rectangle(Point.Empty, frame.Size));
            var lines = await _ui.Ocr.ReadLinesAsync(frame, roi, 3, ct);
            var candidate = lines
                .Where(x => AcquisitionMethodPolicy.IsLifeSkill(x.ReadText))
                .OrderByDescending(x => AcquisitionMethodPolicy.IsRecommended(x.ReadText))
                .ThenBy(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .FirstOrDefault();
            if (!candidate.Found)
                throw Fail(frame, $"{deficit.DisplayName} 추천 획득처를 확인하지 못했습니다.");

            Log?.Invoke($"[제작] {deficit.DisplayName} 추천 획득처 선택 · 필요 {deficit.Required}개");
            _stage.Move(ProductionStage.Travel, $"{deficit.DisplayName} 추천 획득처 이동/채집");
            _ui.ClickFresh(new Point(390, candidate.Center.Y), ct);
        }

        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        int stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            long current = await _data.InventoryOnlyCountAsync(deficit.DisplayName, ct);
            var activity = await _activity.ActivityAsync(ct);
            if (!activity.IsSafeField)
                throw new InvalidOperationException("제작 재료 채집 중 안전하지 않은 상태가 확인되어 정지합니다.");
            bool active = activity.IsAutoTraveling || activity.IsGathering || activity.IsFishing ||
                          activity.MainButtonState == "Stop";
            if (current >= deficit.Required && !active)
            {
                stable++;
                if (stable >= 2)
                {
                    Log?.Invoke($"[제작] {deficit.DisplayName} 준비 완료 · {current}/{deficit.Required} · 초과 허용");
                    _stage.Move(ProductionStage.VerifyInventory, $"{deficit.DisplayName} {current}/{deficit.Required}");
                    return;
                }
            }
            else
            {
                stable = 0;
            }
            await Task.Delay(1000, ct);
        }

        long final = await _data.InventoryOnlyCountAsync(deficit.DisplayName, ct);
        throw new InvalidOperationException(
            $"{deficit.DisplayName} 제작 퀘스트 채집이 필요한 수량에 도달하지 못했습니다: {final}/{deficit.Required}");
    }

    public async Task CloseOverlayAsync(CancellationToken ct)
    {
        // ESC is idempotent for the supplied bottom/detail modals. Do not press it
        // when the persistent field/quest view is already visible.
        using var frame = Capture(ct);
        var modalHeader = await FindUniqueAsync(frame, new Rectangle(40, 120, 720, 520), "구하는 방법", ct);
        var cancel = await FindUniqueAsync(frame, new Rectangle(100, 350, 600, 280), "취소", ct);
        if (modalHeader is null && cancel is null)
            return;
        _ui.TapFresh(0x01, ct);
        await Task.Delay(300, ct);
    }

    public async Task ReturnToStationAndCraftAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
    {
        _stage.Move(ProductionStage.Travel, $"{plan.DisplayName} 제작대로 이동");
        await CloseOverlayAsync(ct);
        await Task.Delay(200, ct);

        // The station stage may omit 0/N. Instant crafting is a separate flow.
        // Only accept a stage immediately below this exact product's quest title.
        using (var frame = Capture(ct))
        {
            if (await FindQuestStageAsync(frame, plan, directOnly: true, ct) is not null)
                throw Fail(frame, "즉시 제작 단계는 일반 제작대 복귀로 처리하지 않습니다.");
            var final = await FindQuestStageAsync(frame, plan, directOnly: false, ct);
            if (final is null)
                throw Fail(frame, "제작 퀘스트의 제작대 복귀 단계를 찾지 못했습니다.");
            _ui.ClickFresh(final.Value.Center, ct);
        }

        DateTime stationDeadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < stationDeadline)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);
            if (await IsProductDetailAsync(frame, plan.DisplayName, ct))
            {
                var count = await ReadCraftCountAsync(frame, ct);
                if (count is null || count.Value.Count != craftCount)
                    throw Fail(frame, $"제작대 도착 후 퀘스트 제작 횟수 {craftCount}회가 유지되지 않았습니다.");
                _stage.Move(ProductionStage.Process, $"{plan.DisplayName} {craftCount}회 제작");
                _ui.TapFresh(0x39, ct); // Space = 제작하기
                Log?.Invoke($"[제작] 제작대 도착 · {plan.DisplayName} {craftCount}회 유지 확인 · 제작 시작");
                await WaitForCompletionAsync(plan.DisplayName, ct);
                return;
            }
            await Task.Delay(800, ct);
        }

        using var failed = Capture(ct);
        throw Fail(failed, "제작대로 이동 후 제작 상세 화면이 열리지 않았습니다.");
    }

    private async Task<DetectionResult?> FindQuestStageAsync(
        Bitmap frame, CraftingPlan plan, bool directOnly, CancellationToken ct)
    {
        var lines = await _ui.Ocr.ReadLinesAsync(frame, new Rectangle(500, 140, 300, 650), 3, ct);
        var titles = lines.Where(x => CraftingQuestText.IsTitle(x.ReadText ?? "", plan.DisplayName)).ToArray();
        if (titles.Length != 1) return null;
        var title = titles[0];
        var stages = lines.Where(x => x.Center.Y > title.Center.Y && x.Center.Y <= title.Bounds.Bottom + 70)
            .Where(x => CraftingQuestText.IsDirectStage(x.ReadText ?? "") ||
                (!directOnly && CraftingQuestText.IsStationStage(x.ReadText ?? ""))).ToArray();
        return stages.Length == 1 ? stages[0] : null;
    }

    private async Task WaitForCompletionAsync(string displayName, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        int stableCompletion = 0;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);
            var busy = await _ui.Ocr.ReadLinesAsync(frame, new Rectangle(100, 35, 650, 400), 3, ct);
            if (busy.Any(x => (x.ReadText ?? "").Replace(" ", "").Contains("제작대사용중", StringComparison.Ordinal)))
            {
                stableCompletion = 0;
                await Task.Delay(700, ct);
                continue;
            }
            var complete = await FindUniqueAsync(frame, new Rectangle(150, 35, 520, 260), "제작 완료", ct);
            if (complete is not null)
            {
                var product = await FindUniqueAsync(frame, new Rectangle(140, 300, 520, 420), displayName, ct);
                if (product is null)
                    throw Fail(frame, $"제작 완료 화면의 결과물이 {displayName}과 일치하지 않습니다.");
                if (++stableCompletion < 2)
                {
                    await Task.Delay(250, ct);
                    continue;
                }
                Log?.Invoke($"[제작] 제작 완료 화면 확인 · {displayName}");
                _stage.Move(ProductionStage.Complete, $"{displayName} 제작 완료");
                _ui.TapFresh(0x39, ct); // Space = 확인
                await Task.Delay(500, ct);
                return;
            }
            stableCompletion = 0;
            await Task.Delay(700, ct);
        }

        using var failed = Capture(ct);
        throw Fail(failed, $"{displayName} 제작 완료 화면을 제한 시간 안에 확인하지 못했습니다.");
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
        Bitmap frame, Rectangle roi, string text, CancellationToken ct)
        => _ui.FindUniqueAsync(frame, roi, text, ct, dimText: true);

    private InvalidOperationException Fail(Bitmap frame, string message)
        => _ui.Failure(frame, message);

    public void Dispose() => _ui.Dispose();
}
