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
    internal event Action<string>? Log;

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

    private Task<DetectionResult?> FindFacilityHeaderAsync(Bitmap frame, string title, CancellationToken ct)
        => _ocr.FindAlteringFacilityHeaderAsync(frame, title, ct);

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

    public async Task CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        await EnterFacilityAsync(plan, ct);
        // The provided facility view advertises Space for collecting all completed work.
        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null ||
                await FindAsync(frame, new(0, 260, 170, 110), "Space", ct) is null)
                Fail(frame, "완료 작업 수령 단축키 Space를 확인하지 못했습니다.");
            if (pass == 0) { await Task.Delay(180, ct); continue; }
            _input.TapScanCode(0x39);
        }
        await Task.Delay(550, ct);
    }

    public async Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
    {
        // First Space may only start automatic travel. Do not navigate or press K while
        // the character is moving. Wait passively for the same facility + Space prompt
        // to reappear, verify it on a fresh frame, then press Space exactly once.
        for (int i = 1; i <= 45; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct);
            using var frame = Capture(ct);
            if (await FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct) is null) continue;
            if (await FindAsync(frame, new(0, 260, 170, 110), "Space", ct) is null) continue;

            await Task.Delay(180, ct);
            using var fresh = Capture(ct);
            if (await FindFacilityHeaderAsync(fresh, plan.ScreenTitle, ct) is null ||
                await FindAsync(fresh, new(0, 260, 170, 110), "Space", ct) is null)
                continue;

            Log?.Invoke($"[자동 가공] 가공대 도착 확인 · 모두 받기 2차 입력");
            _input.TapScanCode(0x39);
            await Task.Delay(700, ct);
            return true;
        }

        Log?.Invoke("[자동 가공] 가공대 도착 후 모두 받기 화면을 제한 시간 안에 확인하지 못했습니다.");
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
