using FishingAutomation;
using System.Text.RegularExpressions;

namespace DungeonVisionBot;

/// <summary>
/// Zero-wing bulk gathering through the in-game inventory "구하는 방법" flow.
/// One acquisition request creates the game's 100-item gathering quest, then the
/// game performs normal travel and gathering. CLI is used only to verify inventory.
/// </summary>
internal sealed class InventoryBulkGatheringScreen : IGatheringScreen
{
    private readonly nint _hwnd;
    private readonly MabinogiMobileCli _cli;
    private readonly GatheringCliData _data;
    private readonly CraftingCliData _inventory;
    private readonly GatheringScreen _fallback;
    private readonly GuardedInputController _input;
    private readonly WindowCapture _capture = new();
    private readonly OcrRecognizer _ocr = new();
    private readonly string _debugDir;

    internal string InputMode => _input.ModeName;
    internal event Action<string>? Log;

    internal InventoryBulkGatheringScreen(
        nint hwnd,
        AppSettings settings,
        string debugDir,
        MabinogiMobileCli cli,
        GatheringCliData data)
    {
        _hwnd = hwnd;
        _cli = cli;
        _data = data;
        _inventory = new CraftingCliData(cli);
        _debugDir = debugDir;
        _fallback = new GatheringScreen(hwnd, settings, debugDir, cli, data);
        _fallback.Log += text => Log?.Invoke(text);
        _input = new GuardedInputController(
            new InterceptionInput(settings.InterceptionMouseDevice, settings.InterceptionKeyboardDevice));
    }

    private Bitmap Capture(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _input.SetCancellation(ct);
        if (!WindowTools.IsRequiredGameWindow(_hwnd))
            throw new InvalidOperationException("게임 창이 변경되어 대량 채집 입력을 정지합니다.");
        NativeMethods.SetForegroundWindow(_hwnd);
        var frame = _capture.CaptureClient(_hwnd);
        try
        {
            _input.ObserveFrame(_hwnd, frame.Size);
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    public async Task StartAsync(GatheringPlan plan, CancellationToken ct)
    {
        plan.Validate();
        long baseline = await _data.ItemCountAsync(plan.DisplayName, ct);
        long bag = await _inventory.InventoryOnlyCountAsync(plan.DisplayName, ct);

        if (bag == 0)
        {
            Log?.Invoke($"[대량 채집] {plan.DisplayName} 가방 보유 0개 · 최초 확보 1회 시작");
            if (plan.SourceRecipe is null)
                throw new InvalidOperationException(
                    $"{plan.DisplayName}이 가방에 없어 100개 채집 검색을 시작할 수 없습니다. " +
                    "생활 스킬/가공 재료 경로로 먼저 1개 이상 확보할 수 있는 시작 제법을 찾지 못했습니다.");

            long beforeSeed = await _data.ItemCountAsync(plan.DisplayName, ct);
            var seed = plan with { TargetQuantity = 1 };
            await _fallback.StartAsync(seed, ct);
            await WaitForNaturalStopAndInventoryAsync(
                plan.DisplayName, beforeSeed, minimumGain: 1, timeout: TimeSpan.FromMinutes(3), ct);

            bag = await _inventory.InventoryOnlyCountAsync(plan.DisplayName, ct);
            if (bag <= 0)
                throw new InvalidOperationException($"{plan.DisplayName} 최초 확보 후에도 가방 수량이 0개입니다.");
            Log?.Invoke($"[대량 채집] 최초 확보 확인 · {plan.DisplayName} 가방 {bag}개 · 100개 채집으로 전환");
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long current = await _data.ItemCountAsync(plan.DisplayName, ct);
            long gained = current - baseline;
            if (gained >= plan.TargetQuantity)
            {
                Log?.Invoke($"[대량 채집] 목표 확보 · {plan.DisplayName} +{gained}/{plan.TargetQuantity}");
                return;
            }

            long cycleBefore = current;
            await StartInventoryHundredQuestAsync(plan.DisplayName, ct);
            Log?.Invoke($"[대량 채집] {plan.DisplayName} 100개 채집 퀘스트 시작 · 현재 +{gained}/{plan.TargetQuantity}");

            // The inventory acquisition route always creates a 100-target gathering
            // quest. Never interrupt at the caller's remainder; let the game finish the
            // current 100 quest naturally, then accept any overshoot.
            await WaitForNaturalStopAndInventoryAsync(
                plan.DisplayName, cycleBefore, minimumGain: 100, timeout: TimeSpan.FromMinutes(6), ct);

            long after = await _data.ItemCountAsync(plan.DisplayName, ct);
            Log?.Invoke($"[대량 채집] 100개 채집 자연 종료 확인 · {plan.DisplayName} {cycleBefore}→{after} · 전체 +{after - baseline}/{plan.TargetQuantity}");
        }
    }

    private async Task StartInventoryHundredQuestAsync(string displayName, CancellationToken ct)
    {
        // I: inventory. The user-confirmed flow requires the orange "아이템" tab
        // before the magnifier search is used.
        using (var frame = Capture(ct))
        {
            _input.TapScanCode(0x17);
        }
        await Task.Delay(700, ct);

        await ClickExactAsync("아이템", new Rectangle(210, 810, 390, 175), ct,
            "가방 하단 아이템 탭을 확인하지 못했습니다.");
        await Task.Delay(350, ct);

        DetectionResult all;
        using (var frame = Capture(ct))
        {
            all = await FindUniqueAsync(frame, new Rectangle(25, 470, 300, 180), "전체", ct)
                ?? throw Fail(frame, "가방 아이템 탭의 전체 필터를 확인하지 못했습니다.");
            var search = new Point(Math.Max(22, all.Bounds.Left - 45), all.Center.Y);
            _input.ClickClientPoint(_hwnd, search);
        }
        await Task.Delay(400, ct);

        await ClickExactAsync("아이템 이름을 검색해 보세요", new Rectangle(80, 470, 650, 170), ct,
            "가방 검색 입력칸을 확인하지 못했습니다.", pasteText: displayName);
        await Task.Delay(120, ct);
        using (var frame = Capture(ct))
            _input.TapScanCode(0x39); // Space = 적용하기
        await Task.Delay(650, ct);

        await ClickExactAsync(displayName, new Rectangle(45, 535, 710, 310), ct,
            $"가방 검색 결과에서 정확한 {displayName} 항목을 찾지 못했습니다.");
        await Task.Delay(450, ct);

        long bagCount = await _inventory.InventoryOnlyCountAsync(displayName, ct);
        if (bagCount <= 0)
            throw new InvalidOperationException($"{displayName} CLI 가방 수량이 0개로 바뀌어 상세 선택을 중단합니다.");

        using (var frame = Capture(ct))
        {
            var detailTitle = await FindUniqueAsync(frame, new Rectangle(70, 90, 660, 390), displayName, ct)
                ?? throw Fail(frame, $"선택한 가방 상세 제목이 {displayName}과 일치하지 않습니다.");

            // The detail sheet shows the bag stack count on the same upper row as the
            // item title/icon. Derive the numeric ROI from the confirmed title instead
            // of relying on an uploaded screenshot's pixel size.
            string countText = bagCount.ToString();
            int countLeft = Math.Clamp(detailTitle.Bounds.Right + 80, 20, Math.Max(20, frame.Width - 180));
            int countTop = Math.Clamp(detailTitle.Bounds.Top - 35, 20, Math.Max(20, frame.Height - 150));
            var countRoi = Rectangle.Intersect(
                new Rectangle(countLeft, countTop, frame.Width - countLeft - 20, 150),
                new Rectangle(Point.Empty, frame.Size));
            var count = await _ocr.FindCompactLabelAsync(frame, countRoi, countText, ct);
            if (!count.Found)
                throw Fail(frame, $"가방 화면 수량과 CLI 수량({bagCount})을 함께 확인하지 못했습니다.");

            var method = await FindUniqueAsync(frame, new Rectangle(70, 250, 600, 300), "구하는 방법", ct)
                ?? throw Fail(frame, "가방 상세의 구하는 방법을 확인하지 못했습니다.");
            _input.ClickClientPoint(_hwnd, method.Center);
        }
        await Task.Delay(450, ct);

        await ClickFirstLifeSkillMethodAsync(ct);
    }

    private async Task ClickFirstLifeSkillMethodAsync(CancellationToken ct)
    {
        using var frame = Capture(ct);
        var header = await FindUniqueAsync(frame, new Rectangle(45, 285, 500, 160), "구하는 방법", ct)
            ?? throw Fail(frame, "구하는 방법 목록을 확인하지 못했습니다.");

        var roi = Rectangle.Intersect(new Rectangle(55, header.Bounds.Bottom + 25, 690, 500),
            new Rectangle(Point.Empty, frame.Size));
        var lines = await _ocr.ReadLinesAsync(frame, roi, 3, ct);
        var blocked = new[] { "던전", "전리품", "임무", "레이드", "구하는방법", "선택하세요" };

        var candidate = lines
            .Where(x => x.Center.Y > roi.Top + 10)
            .Where(x => Regex.IsMatch(x.ReadText ?? "", "[가-힣]", RegexOptions.CultureInvariant))
            .Where(x =>
            {
                string normalized = (x.ReadText ?? "").Replace(" ", "");
                return blocked.All(word => !normalized.Contains(word, StringComparison.Ordinal));
            })
            .OrderBy(x => x.Center.Y)
            .ThenBy(x => x.Center.X)
            .FirstOrDefault();

        if (!candidate.Found)
            throw Fail(frame, "구하는 방법에서 생활 스킬 획득 항목을 찾지 못했습니다.");

        Log?.Invoke("[대량 채집] 생활 스킬 획득 경로 선택 · " + (candidate.ReadText ?? "첫 항목"));
        _input.ClickClientPoint(_hwnd, new Point(390, candidate.Center.Y));
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
                Log?.Invoke($"[대량 채집] {displayName} 수량 변화 · {before}→{current} · +{gain}");
                last = current;
            }

            bool active = activity.IsAutoTraveling || activity.IsGathering || activity.IsFishing ||
                          activity.MainButtonState == "Stop";
            if (gain >= minimumGain && !active)
            {
                stableIdle++;
                if (stableIdle >= 2)
                    return;
            }
            else
            {
                stableIdle = 0;
            }

            if (!activity.IsSafeField)
                throw new InvalidOperationException("대량 채집 중 전투·대화 등 안전하지 않은 상태가 확인되어 정지합니다.");

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
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            var found = await FindUniqueAsync(frame, roi, text, ct);
            if (found is null)
                throw Fail(frame, failure);
            if (pass == 0)
            {
                await Task.Delay(160, ct);
                continue;
            }

            _input.ClickClientPoint(_hwnd, found.Value.Center);
            if (pasteText is not null)
            {
                await Task.Delay(120, ct);
                _input.PasteText(pasteText);
            }
        }
    }

    private async Task<DetectionResult?> FindUniqueAsync(
        Bitmap frame, Rectangle roi, string text, CancellationToken ct)
    {
        var found = await _ocr.FindAlteringLabelsAsync(frame, roi, text, ct, dimText: true);
        return found.Count == 1 ? found[0] : null;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        var state = await _data.ActivityAsync(ct);
        if (!state.IsGathering && !state.IsAutoTraveling && !state.IsFishing)
            return;

        using var frame = Capture(ct);
        if (state.MainButtonState != "Stop" || !GatheringVision.HasStopButton(frame))
            throw Fail(frame, "대량 채집 정지 버튼을 확인하지 못했습니다. 게임에서 직접 정지하세요.");
        _input.TapScanCode(0x39);
    }

    private InvalidOperationException Fail(Bitmap frame, string message)
    {
        Directory.CreateDirectory(_debugDir);
        string path = Path.Combine(_debugDir, "bulk-gathering-last-failure.png");
        frame.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return new InvalidOperationException(message + " 진단: " + path);
    }

    public void Dispose()
    {
        _fallback.Dispose();
        _input.Dispose();
    }
}
