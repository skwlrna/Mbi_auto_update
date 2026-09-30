using FishingAutomation;

namespace DungeonVisionBot;

internal sealed class AlteringScreen : IAlteringScreen
{
    private readonly GuardedInputController _input;
    private readonly WindowCapture _capture = new();
    private readonly OcrRecognizer _ocr = new();
    private readonly string _debugDir;
    private nint _hwnd;
    private static readonly Rectangle Whole = new(0, 0, 800, 1000);
    private static readonly Rectangle Header = new(0, 15, 450, 110);
    private static readonly Rectangle Cards = new(20, 350, 760, 550);
    private static readonly Rectangle Popup = new(270, 585, 360, 45);
    internal string InputMode => _input.ModeName;

    internal AlteringScreen(nint hwnd, AppSettings settings, string debugDir)
    {
        _hwnd = hwnd; _debugDir = debugDir;
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

    private async Task<DetectionResult?> FindRecipeAsync(Bitmap frame, AlteringPlan plan, CancellationToken ct)
    {
        var exact = await FindAsync(frame, Popup, plan.DisplayName, ct);
        if (exact is not null || plan.VerifiedOcrAlias is null) return exact;
        return await FindAsync(frame, Popup, plan.VerifiedOcrAlias, ct);
    }
    private async Task<bool> ClickLabelAsync(string text, Rectangle roi, string? header, CancellationToken ct, bool facilityTitle = false)
    {
        // Two observations; the second one alone supplies the input coordinates.
        using (var first = Capture(ct))
        {
            if (header is not null && await FindAsync(first, Header, header, ct) is null) return false;
            if (await FindAsync(first, roi, text, ct, facilityTitle) is null) return false;
        }
        await Task.Delay(180, ct);
        using var second = Capture(ct);
        if (header is not null && await FindAsync(second, Header, header, ct) is null) return false;
        var found = await FindAsync(second, roi, text, ct, facilityTitle);
        if (found is null) return false;
        _input.ClickClientPoint(_hwnd, found.Value.Center);
        await Task.Delay(550, ct);
        return true;
    }

    private async Task EnterFacilityAsync(AlteringPlan plan, CancellationToken ct)
    {
        using (var frame = Capture(ct))
        {
            if (await FindAsync(frame, new(100, 690, 580, 200), "필요한 재료", ct) is not null)
            {
                _input.TapScanCode(0x01);
                await Task.Delay(400, ct);
            }
        }
        using (var frame = Capture(ct))
            if (await FindAsync(frame, Header, plan.ScreenTitle, ct) is not null) return;
        using (var frame = Capture(ct))
        {
            foreach (string name in AlteringPlan.Facilities.Select(x => x.Replace(" 시설", "")))
            {
                if (await FindAsync(frame, Header, name, ct) is null) continue;
                // The supplied facility view has a back arrow at the top left.
                _input.ClickClientPoint(_hwnd, new(33, 55));
                await Task.Delay(500, ct);
                break;
            }
        }
        bool atHub;
        using (var frame = Capture(ct)) atHub = await FindAsync(frame, Header, "가공", ct) is not null;
        // K opens the known production menu; select the supplied bottom '가공' tab.
        if (!atHub)
        {
            using (var frame = Capture(ct)) _input.TapScanCode(0x25);
            await Task.Delay(700, ct);
        }
        using (var frame = Capture(ct))
            if (await FindAsync(frame, Header, plan.ScreenTitle, ct) is not null) return;
        using (var frame = Capture(ct))
        {
            if (await FindAsync(frame, Header, "가공", ct) is null)
                if (!await ClickLabelAsync("가공", new(180, 880, 420, 120), null, ct))
                    Fail(frame, "K키 후 가공 탭을 확인하지 못했습니다.");
        }
        if (!await ClickLabelAsync(plan.ScreenTitle, AlteringFacilityLayout.TitleArea(plan.ScreenTitle), "가공", ct, facilityTitle: true))
        {
            using var frame = Capture(ct);
            Fail(frame, "가공 시설 선택 화면을 확인하지 못했습니다.");
        }
        using var facility = Capture(ct);
        if (await FindAsync(facility, Header, plan.ScreenTitle, ct) is null)
            Fail(facility, "선택한 가공 시설 화면으로 전환되지 않았습니다.");
    }

    private async Task SelectRecipeAsync(AlteringPlan plan, CancellationToken ct)
    {
        // First scroll to the top. Each drag is guarded by a fresh facility observation.
        for (int i = 0; i < 5; i++)
        {
            using var frame = Capture(ct);
            if (await FindAsync(frame, Header, plan.ScreenTitle, ct) is null) Fail(frame, "가공 목록이 사라졌습니다.");
            _input.DragClientPoint(_hwnd, new(735, 415), new(735, 895), 350);
            await Task.Delay(180, ct);
        }
        for (int page = 0; page < 16; page++)
        {
            using var frame = Capture(ct);
            if (await FindAsync(frame, Header, plan.ScreenTitle, ct) is null) Fail(frame, "가공 목록이 사라졌습니다.");
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
                if (await FindRecipeAsync(popup, plan, ct) is null)
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
        await EnterFacilityAsync(plan, ct);
        await SelectRecipeAsync(plan, ct);
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            if (await FindRecipeAsync(frame, plan, ct) is null) Fail(frame, "품목 상세 화면이 바뀌었습니다.");
            var paid = await FindAsync(frame, new(180, 860, 490, 130), "가공하러 가기", ct);
            if (paid is null) Fail(frame, "허용된 가공하러 가기 버튼을 확인하지 못했습니다.");
            var button = paid!.Value;
            var costRoi = Rectangle.Intersect(Whole, new(button.Bounds.Left - 25, button.Bounds.Top - 5, button.Bounds.Width + 25, button.Bounds.Height + 10));
            var cost = await FindAsync(frame, costRoi, "5", ct);
            if (cost is null || cost.Value.Bounds.Right >= button.Bounds.Left) Fail(frame, "가공 버튼의 재화 5개 표시를 확인하지 못했습니다.");
            if (pass == 0) { await Task.Delay(200, ct); continue; }
            // Reserve before input. Even an uncertain click cannot be repeated for free.
            reserveFiveWings();
            _input.ClickClientPoint(_hwnd, button.Center);
        }
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
        // The provided facility view advertises Space for collecting all completed work.
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            if (await FindAsync(frame, Header, plan.ScreenTitle, ct) is null ||
                await FindAsync(frame, new(0, 260, 170, 110), "Space", ct) is null)
                Fail(frame, "완료 작업 수령 단축키 Space를 확인하지 못했습니다.");
            if (pass == 0) { await Task.Delay(180, ct); continue; }
            _input.TapScanCode(0x39);
        }
        await Task.Delay(550, ct);
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
