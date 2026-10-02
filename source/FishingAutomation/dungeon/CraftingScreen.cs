using FishingAutomation;
using System.Text.RegularExpressions;

namespace DungeonVisionBot;

internal sealed record CraftingQuestDeficit(string DisplayName, long Current, long Required, int RowY);

internal interface ICraftingScreen : IDisposable
{
    string InputMode { get; }
    event Action<string>? Log;
    Task CreateQuestAsync(CraftingPlan plan, int craftCount, CancellationToken ct);
    Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(CraftingPlan plan, CancellationToken ct);
    Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct);
    Task CloseOverlayAsync(CancellationToken ct);
    Task ReturnToStationAndCraftAsync(CraftingPlan plan, int craftCount, CancellationToken ct);
}

internal sealed class CraftingScreen : ICraftingScreen
{
    private readonly nint _hwnd;
    private readonly CraftingCliData _data;
    private readonly GuardedInputController _input;
    private readonly WindowCapture _capture = new();
    private readonly OcrRecognizer _ocr = new();
    private readonly string _debugDir;

    internal CraftingScreen(
        nint hwnd,
        AppSettings settings,
        string debugDir,
        MabinogiMobileCli cli)
    {
        _hwnd = hwnd;
        _data = new CraftingCliData(cli);
        _debugDir = debugDir;
        _input = new GuardedInputController(
            new InterceptionInput(settings.InterceptionMouseDevice, settings.InterceptionKeyboardDevice));
    }

    public string InputMode => _input.ModeName;
    public event Action<string>? Log;

    private Bitmap Capture(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _input.SetCancellation(ct);
        if (!WindowTools.IsRequiredGameWindow(_hwnd))
            throw new InvalidOperationException("게임 창이 변경되어 제작 입력을 정지합니다.");
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

    public async Task CreateQuestAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
    {
        plan.Validate();
        if (craftCount is < 1 or > 10)
            throw new InvalidDataException("제작 퀘스트는 한 번에 1~10회만 만들 수 있습니다.");

        await OpenProductAsync(plan, ct);
        await SetCraftCountAsync(craftCount, ct);

        await ClickExactAsync(
            "퀘스트 만들기",
            new Rectangle(65, 760, 390, 220),
            ct,
            "제작 상세 화면의 퀘스트 만들기 버튼을 확인하지 못했습니다.");

        await Task.Delay(800, ct);
        Log?.Invoke($"[제작] {plan.DisplayName} {craftCount}회 퀘스트 생성");
    }

    private async Task OpenProductAsync(CraftingPlan plan, CancellationToken ct)
    {
        // If a matching detail sheet is already open, reuse it.
        using (var current = Capture(ct))
        {
            if (await IsProductDetailAsync(current, plan.DisplayName, ct))
                return;
            _input.TapScanCode(0x25); // K: 가공/제작 허브
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
        await ClickExactAsync(
            category,
            new Rectangle(25, 120, 750, 735),
            ct,
            $"제작 허브에서 {category} 카드를 확인하지 못했습니다.");
        await Task.Delay(650, ct);

        await SearchProductAsync(plan.DisplayName, category, ct);
    }

    private async Task SearchProductAsync(string displayName, string category, CancellationToken ct)
    {
        using (var frame = Capture(ct))
        {
            if (await FindUniqueAsync(frame, new Rectangle(15, 20, 300, 100), category, ct) is null)
                throw Fail(frame, $"{category} 제작 목록 화면을 확인하지 못했습니다.");

            var all = await FindUniqueAsync(frame, new Rectangle(35, 75, 220, 120), "전체", ct)
                ?? throw Fail(frame, "제작 목록의 전체 필터를 확인하지 못했습니다.");
            _input.ClickClientPoint(_hwnd, new Point(Math.Max(18, all.Bounds.Left - 42), all.Center.Y));
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
            _input.TapScanCode(0x39); // Space = 적용하기
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
        var title = await FindUniqueAsync(frame, new Rectangle(55, 260, 690, 360), displayName, ct);
        if (title is null) return false;
        var quest = await FindUniqueAsync(frame, new Rectangle(45, 720, 420, 260), "퀘스트 만들기", ct);
        return quest is not null;
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
            _input.ClickClientPoint(_hwnd, click);
            await Task.Delay(180, ct);
        }

        using var failed = Capture(ct);
        throw Fail(failed, $"제작 횟수를 {wanted}회로 맞추지 못했습니다.");
    }

    private async Task<(int Count, Point Center)?> ReadCraftCountAsync(Bitmap frame, CancellationToken ct)
    {
        var lines = await _ocr.ReadLinesAsync(frame, new Rectangle(80, 500, 650, 340), 3, ct);
        foreach (var line in lines.OrderBy(x => x.Center.Y))
        {
            var match = Regex.Match((line.ReadText ?? "").Replace(" ", ""), @"(?<n>\d{1,2})회");
            if (!match.Success || !int.TryParse(match.Groups["n"].Value, out int count) || count is < 1 or > 10)
                continue;
            return (count, line.Center);
        }
        return null;
    }

    public async Task<IReadOnlyList<CraftingQuestDeficit>> ReadQuestDeficitsAsync(
        CraftingPlan plan,
        CancellationToken ct)
    {
        await OpenQuestPopupAsync(plan, ct);
        using var frame = Capture(ct);
        var roi = new Rectangle(70, 130, 670, 520);
        var lines = (await _ocr.ReadLinesAsync(frame, roi, 3, ct))
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
        foreach (var ratio in ratios)
        {
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
                continue;
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
        var lines = await _ocr.ReadLinesAsync(frame, new Rectangle(500, 140, 300, 600), 3, ct);
        string wanted = AlteringText.Normalize(plan.DisplayName);
        var quest = lines
            .Where(x =>
            {
                string text = AlteringText.Normalize(x.ReadText ?? "");
                return text.Contains(wanted, StringComparison.Ordinal) &&
                       text.Contains("제작", StringComparison.Ordinal);
            })
            .OrderBy(x => x.Center.Y)
            .FirstOrDefault();

        if (!quest.Found)
            throw Fail(frame, $"{plan.DisplayName} 제작 퀘스트를 오른쪽 목록에서 찾지 못했습니다.");

        _input.ClickClientPoint(_hwnd, new Point(Math.Clamp(quest.Center.X, 535, 760), quest.Center.Y));
        await Task.Delay(450, ct);
    }

    public async Task GatherQuestDeficitAsync(CraftingQuestDeficit deficit, CancellationToken ct)
    {
        // The deficit popup is already open. Select the exact row by its paired OCR Y.
        using (var frame = Capture(ct))
        {
            var exact = await FindUniqueAsync(frame, new Rectangle(80, Math.Max(120, deficit.RowY - 45), 600, 90),
                deficit.DisplayName, ct);
            var point = exact?.Center ?? new Point(300, deficit.RowY);
            _input.ClickClientPoint(_hwnd, point);
        }
        await Task.Delay(450, ct);

        using (var frame = Capture(ct))
        {
            var header = await FindUniqueAsync(frame, new Rectangle(45, 230, 560, 180), "구하는 방법", ct)
                ?? throw Fail(frame, $"{deficit.DisplayName} 구하는 방법을 확인하지 못했습니다.");
            var roi = Rectangle.Intersect(
                new Rectangle(55, header.Bounds.Bottom + 20, 690, 520),
                new Rectangle(Point.Empty, frame.Size));
            var lines = await _ocr.ReadLinesAsync(frame, roi, 3, ct);
            var candidate = lines
                .Where(x => Regex.IsMatch(x.ReadText ?? "", "[가-힣]", RegexOptions.CultureInvariant))
                .Where(x => !(x.ReadText ?? "").Contains("전리품", StringComparison.Ordinal))
                .OrderBy(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .FirstOrDefault();
            if (!candidate.Found)
                throw Fail(frame, $"{deficit.DisplayName} 추천 획득처를 확인하지 못했습니다.");

            Log?.Invoke($"[제작] {deficit.DisplayName} 추천 획득처 선택 · 필요 {deficit.Required}개");
            _input.ClickClientPoint(_hwnd, new Point(390, candidate.Center.Y));
        }

        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        int stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            long current = await _data.InventoryOnlyCountAsync(deficit.DisplayName, ct);
            if (current >= deficit.Required)
            {
                stable++;
                if (stable >= 2)
                {
                    Log?.Invoke($"[제작] {deficit.DisplayName} 준비 완료 · {current}/{deficit.Required} · 초과 허용");
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
        _input.TapScanCode(0x01);
        await Task.Delay(300, ct);
    }

    public async Task ReturnToStationAndCraftAsync(CraftingPlan plan, int craftCount, CancellationToken ct)
    {
        await CloseOverlayAsync(ct);
        await Task.Delay(200, ct);

        // The final right-side quest line is facility-specific (음식/약품/다목적...)
        // but always contains "제작대" and "제작". Select that line generically.
        using (var frame = Capture(ct))
        {
            var lines = await _ocr.ReadLinesAsync(frame, new Rectangle(500, 145, 300, 650), 3, ct);
            var final = lines
                .Where(x =>
                {
                    string text = (x.ReadText ?? "").Replace(" ", "");
                    return text.Contains("제작대", StringComparison.Ordinal) &&
                           text.Contains("제작", StringComparison.Ordinal);
                })
                .OrderByDescending(x => x.Center.Y)
                .FirstOrDefault();
            if (!final.Found)
                throw Fail(frame, "제작 퀘스트의 제작대 복귀 단계를 찾지 못했습니다.");
            _input.ClickClientPoint(_hwnd, new Point(Math.Clamp(final.Center.X, 535, 760), final.Center.Y));
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
                _input.TapScanCode(0x39); // Space = 제작하기
                Log?.Invoke($"[제작] 제작대 도착 · {plan.DisplayName} {craftCount}회 유지 확인 · 제작 시작");
                await WaitForCompletionAsync(plan.DisplayName, ct);
                return;
            }
            await Task.Delay(800, ct);
        }

        using var failed = Capture(ct);
        throw Fail(failed, "제작대로 이동 후 제작 상세 화면이 열리지 않았습니다.");
    }

    private async Task WaitForCompletionAsync(string displayName, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            using var frame = Capture(ct);
            var complete = await FindUniqueAsync(frame, new Rectangle(150, 35, 520, 260), "제작 완료", ct);
            if (complete is not null)
            {
                var product = await FindUniqueAsync(frame, new Rectangle(140, 300, 520, 420), displayName, ct);
                if (product is null)
                    throw Fail(frame, $"제작 완료 화면의 결과물이 {displayName}과 일치하지 않습니다.");
                Log?.Invoke($"[제작] 제작 완료 화면 확인 · {displayName}");
                _input.TapScanCode(0x39); // Space = 확인
                await Task.Delay(500, ct);
                return;
            }
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

    private InvalidOperationException Fail(Bitmap frame, string message)
    {
        Directory.CreateDirectory(_debugDir);
        string path = Path.Combine(_debugDir, "crafting-last-failure.png");
        frame.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return new InvalidOperationException(message + " 진단: " + path);
    }

    public void Dispose() => _input.Dispose();
}
