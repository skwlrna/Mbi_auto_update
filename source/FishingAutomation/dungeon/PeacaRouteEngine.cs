using System.Diagnostics;
namespace DungeonVisionBot;

internal sealed class PeacaRouteEngine : IScenarioRunner
{
    private nint _hwnd;
    private readonly AppSettings _settings;
    private readonly TargetDetector _detector;
    private readonly WindowCapture _capture = new();
    private readonly IInputController _input;
    private readonly string _route;
    public event Action<string>? Log;
    public string InputMode => _input.ModeName;
    public PeacaRouteEngine(nint hwnd, AppSettings settings, List<TargetDefinition> targets, string baseDir, string route)
    {
        if (route != "peaca_d1_1" && route != "peaca_d2_1")
            throw new ArgumentException("페카 심층 구역만 선택할 수 있습니다.", nameof(route));
        _hwnd = hwnd;
        _settings = settings;
        _route = route;
        _detector = new TargetDetector(targets.Where(t => t.Id.StartsWith("route_") || t.Id == "enter_bottom"), baseDir);
        _input = CreateInput(settings);
    }
    public Task RunAsync(CancellationToken ct) => RunPeacaRouteAsync(_route, ct);
    private IInputController CreateInput(AppSettings s)
    {
        if (!s.UseInterception)
        {
            throw new InvalidOperationException(
                "UseInterception=false 입니다. 마비노기 모바일 클릭은 Interception만 사용하도록 설정하세요.");
        }

        try
        {
            return new InterceptionInput(
                s.InterceptionMouseDevice,
                s.InterceptionKeyboardDevice);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "interception.dll을 찾지 못했습니다. " +
                "interception.dll(x64)을 DungeonVisionBot.exe 옆에 두세요.",
                ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "interception.dll 비트수가 맞지 않습니다. x64 DLL을 사용하세요.",
                ex);
        }
    }

    // DUNGEON_WORLD_ROUTE_V11
    private async Task RunPeacaRouteAsync(string route, CancellationToken ct)
    {
        bool isPeaca = route.StartsWith("peaca_", StringComparison.OrdinalIgnoreCase);
        bool isRunda = route.StartsWith("runda_", StringComparison.OrdinalIgnoreCase);
        bool isFiod = route.StartsWith("fiod_", StringComparison.OrdinalIgnoreCase);
        bool d11 = route.EndsWith("d1_1", StringComparison.OrdinalIgnoreCase);
        bool d21 = route.EndsWith("d2_1", StringComparison.OrdinalIgnoreCase);
        if (!isPeaca || (!d11 && !d21))
            throw new InvalidOperationException($"지원하지 않는 던전 자동 이동 경로: {route}");

        string dungeonName = isPeaca ? "페카 고분" : isRunda ? "룬다 던전" : "피오드 던전";
        string destination = isPeaca
            ? (d11 ? "페카 심층 1-1" : "페카 심층 2-1")
            : $"{dungeonName.Replace(" 던전", "")} {(d11 ? "1-1" : "2-1")}";
        string mapTarget = isPeaca ? "route_peaca_map" : isRunda ? "route_runda_map" : "route_fiod_map";
        string popupTarget = isPeaca ? "route_peaca_popup_title" : isRunda ? "route_runda_popup_title" : "route_fiod_popup_title";
        string arrivalTarget = isPeaca ? "route_peaca_title" : isRunda ? "route_runda_title" : "route_fiod_title";

        Log?.Invoke($"[던전 자동이동] 시작 -> {destination}");
        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        NativeMethods.SetForegroundWindow(_hwnd);

        using var beforeMapOpen = await CaptureGameWindowAsync(ct);
        Log?.Invoke("[던전 자동이동] M 입력 -> 지도 열기");
        _input.TapScanCode(0x32); // M
        await Task.Delay(1200, ct);

        using var localMapFrame = await CaptureGameWindowAsync(ct);
        double mapOpenDiff = MapFrameDifference(beforeMapOpen, localMapFrame);
        Log?.Invoke($"[던전 자동이동] M 입력 후 화면변화={mapOpenDiff:0.00}");
        if (mapOpenDiff <= 6.0)
            throw new TimeoutException("M 입력 후 지도 화면 전환을 확인하지 못했습니다. 임의 좌표를 클릭하지 않고 정지합니다.");

        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        NativeMethods.SetForegroundWindow(_hwnd);
        var ullaBreadcrumbPoint = new Point(82, 66);
        Log?.Invoke($"[던전 자동이동] 좌측 상단 울라 대륙 고정 위치 클릭 @ {ullaBreadcrumbPoint}");
        _input.ClickClientPoint(_hwnd, ullaBreadcrumbPoint);
        await Task.Delay(1200, ct);

        using var worldMapFrame = await CaptureGameWindowAsync(ct);
        double continentOpenDiff = MapFrameDifference(localMapFrame, worldMapFrame);
        Log?.Invoke($"[던전 자동이동] 울라 대륙 클릭 후 화면변화={continentOpenDiff:0.00}");
        if (continentOpenDiff <= 6.0)
            throw new TimeoutException("좌측 상단 울라 대륙 고정 위치 클릭 후 월드맵 전환을 확인하지 못했습니다. 임의 좌표를 클릭하지 않고 정지합니다.");

        await AlignWorldMapTopLeftAsync(ct);
        var mapLabel = await FindDungeonOnWorldMapAsync(mapTarget, dungeonName, ct);
        if (!mapLabel.Found)
            throw new TimeoutException($"울라 대륙 지도를 제한 범위까지 탐색했지만 '{dungeonName}'을 찾지 못했습니다. 임의 좌표를 클릭하지 않고 정지합니다.");

        // The OCR label is ONLY an anchor. Never click the text itself.
        // Find the saturated purple/blue-highlight dungeon icon immediately above that label.
        using var iconFrame = await CaptureGameWindowAsync(ct);
        if (!TryFindDungeonIconAboveLabel(iconFrame, mapLabel.Bounds, out var iconBounds))
            throw new TimeoutException($"{dungeonName} 글씨는 찾았지만 위쪽 던전 아이콘을 확인하지 못했습니다. 글씨나 대체 좌표를 누르지 않고 정지합니다.");

        var iconCenter = new Point(iconBounds.Left + iconBounds.Width / 2, iconBounds.Top + iconBounds.Height / 2);
        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        NativeMethods.SetForegroundWindow(_hwnd);
        Log?.Invoke($"[던전 자동이동] {dungeonName} OCR 확인 -> 위 던전 아이콘 클릭 @ {iconBounds}");
        _input.ClickClientPoint(_hwnd, iconCenter);
        await Task.Delay(700, ct);

        bool popupReady = await WaitForTargetPairAsync(popupTarget, "route_go_here", 8, ct);
        if (!popupReady)
            throw new TimeoutException($"{dungeonName} 아이콘 클릭 후 '{dungeonName} + 여기로 가기' 확인에 실패했습니다. Space를 누르지 않습니다.");

        Log?.Invoke($"[던전 자동이동] {dungeonName} + 여기로 가기 확인 -> Space");
        _input.TapScanCode(0x39);
        await Task.Delay(800, ct);

        if (isPeaca)
        {
            bool arrived = await WaitForTargetPairAsync(arrivalTarget, "route_deep_tab", 300, ct);
            if (!arrived)
                throw new TimeoutException("이동 후 5분 안에 페카 고분 도착/심층 던전 탭을 확인하지 못했습니다.");

            DetectionResult deepTab;
            using (var arrivedFrame = await CaptureGameWindowAsync(ct))
                deepTab = await _detector.DetectAsync("route_deep_tab", arrivedFrame, ct);
            if (!deepTab.Found)
                throw new TimeoutException("페카 고분 도착 후 심층 던전 탭을 다시 확인하지 못했습니다.");

            _hwnd = await ResolveRequiredGameWindowAsync(ct);
            Log?.Invoke($"[던전 자동이동] 심층 던전 탭 클릭 @ {deepTab.Bounds}");
            _input.ClickClientPoint(_hwnd, deepTab.Center);
            await Task.Delay(900, ct);
        }
        else
        {
            var arrived = await WaitForTargetAsync(arrivalTarget, 300, ct);
            if (!arrived.Found)
                throw new TimeoutException($"이동 후 5분 안에 {dungeonName} 도착 화면을 확인하지 못했습니다.");
            Log?.Invoke($"[던전 자동이동] {dungeonName} 도착 화면 확인");
        }

        string slotTarget = isPeaca
            ? (d11 ? "route_d1_1" : "route_d2_1")
            : (d11 ? "route_regular_1_1" : "route_regular_2_1");
        string enterTarget = isPeaca
            ? (d11 ? "route_enter_d1_1" : "route_enter_d2_1")
            : (d11 ? "route_enter_regular_1_1" : "route_enter_regular_2_1");

        var slot = await WaitForDungeonSlotAsync(slotTarget, 30, ct);
        if (!slot.Found)
            throw new TimeoutException($"{destination}의 {(d11 ? "1-1" : "2-1")} 표기를 30초 안에 찾지 못했습니다. best={slot.Score:0.000}, bounds={slot.Bounds}. 다른 구역을 대신 누르지 않습니다.");

        _hwnd = await ResolveRequiredGameWindowAsync(ct);
        Log?.Invoke($"[던전 자동이동] {destination} 구역 확인 -> 클릭 @ {slot.Bounds}");
        _input.ClickClientPoint(_hwnd, slot.Center);
        await Task.Delay(650, ct);

        var enter = await WaitForTargetAsync(enterTarget, 8, ct);
        if (!enter.Found)
            throw new TimeoutException($"{destination} 선택 후 정확한 진입 문구를 확인하지 못했습니다. Space를 누르지 않습니다.");

        Log?.Invoke($"[던전 자동이동] {destination} 진입 문구 확인 -> Space");
        _input.TapScanCode(0x39);

        await EnterPeacaAtFixedPointAsync(ct);
        Log?.Invoke("[페카 심층] 입장 입력 완료 -> 이동 모드 종료");
    }

    private async Task EnterPeacaAtFixedPointAsync(CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        int ready = 0;
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            if (frame.Width != 800 || frame.Height != 1000)
                throw new InvalidOperationException("심층 입장은 800×1000 클라이언트에서만 가능합니다.");
            var title = await _detector.DetectAsync("route_peaca_title", frame, ct);
            var deep = await _detector.DetectAsync("route_deep_tab", frame, ct);
            var entry = await _detector.DetectAsync("enter_bottom", frame, ct);
            bool valid = title.Found && deep.Found && entry.Found &&
                FuzzyText.Normalize(entry.ReadText ?? "") == FuzzyText.Normalize("입장하기") &&
                new Rectangle(360, 900, 440, 100).Contains(entry.Center);
            ready = valid ? ready + 1 : 0;
            if (ready >= 2)
            {
                ct.ThrowIfCancellationRequested();
                _hwnd = await ResolveRequiredGameWindowAsync(ct);
                NativeMethods.SetForegroundWindow(_hwnd);
                Log?.Invoke("[페카 심층] 페카/심층/입장 화면 2회 확인 -> 클라이언트 (493,952) 클릭");
                _input.ClickClientPoint(_hwnd, new Point(493, 952));
                return;
            }
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }
        throw new TimeoutException("심층 입장 화면을 확인하지 못했습니다. 입장 좌표를 누르지 않습니다.");
    }

    private async Task<DetectionResult> WaitForDungeonSlotAsync(string targetId, int timeoutSeconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        DetectionResult best = DetectionResult.NotFound;
        int attempt = 0;

        while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var r = await _detector.DetectAsync(targetId, frame, ct);
            attempt++;

            if (r.Score > best.Score)
                best = r;

            if (r.Found)
            {
                Log?.Invoke($"[던전 자동이동] 구역 검출 성공 target={targetId} · score={r.Score:0.000} · bounds={r.Bounds}" +
                    (string.IsNullOrWhiteSpace(r.ReadText) ? "" : $" · OCR=\"{r.ReadText}\""));
                return r;
            }

            if (attempt == 1 || attempt % 8 == 0)
                Log?.Invoke($"[던전 자동이동] 구역 탐색 target={targetId} · best={best.Score:0.000} · bounds={best.Bounds}");

            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }

        Log?.Invoke($"[던전 자동이동] 구역 탐색 타임아웃 target={targetId} · best={best.Score:0.000} · bounds={best.Bounds}");
        return best;
    }

    private async Task<DetectionResult> WaitForTargetAsync(string targetId, int timeoutSeconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var r = await _detector.DetectAsync(targetId, frame, ct);
            if (r.Found) return r;
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }
        return DetectionResult.NotFound;
    }

    private async Task<bool> WaitForTargetPairAsync(string firstId, string secondId, int timeoutSeconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var first = await _detector.DetectAsync(firstId, frame, ct);
            var second = await _detector.DetectAsync(secondId, frame, ct);
            if (first.Found && second.Found)
            {
                Log?.Invoke($"[던전 자동이동] 동시 확인: {firstId} + {secondId}");
                return true;
            }
            await Task.Delay(Math.Max(300, _settings.PollIntervalMs), ct);
        }
        return false;
    }

    private async Task AlignWorldMapTopLeftAsync(CancellationToken ct)
    {
        const double StableThreshold = 6.0;
        int stable = 0;
        Log?.Invoke("[던전 자동이동] 울라 월드맵 좌상단 끝 정렬 시작");

        for (int attempt = 1; attempt <= 12; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var before = await CaptureGameWindowAsync(ct);
            _hwnd = await ResolveRequiredGameWindowAsync(ct);
            _input.DragClientPoint(_hwnd, new Point(270, 330), new Point(520, 620), 800);
            await Task.Delay(650, ct);
            using var after = await CaptureGameWindowAsync(ct);
            double diff = MapFrameDifference(before, after);
            stable = diff <= StableThreshold ? stable + 1 : 0;
            Log?.Invoke($"[던전 자동이동] 좌상단 정렬 {attempt}/12 · 화면변화={diff:0.00} · 끝판정={stable}/2");
            if (stable >= 2)
            {
                Log?.Invoke("[던전 자동이동] 화면 변화 거의 없음 2회 연속 -> 좌상단 끝 도달 확정");
                return;
            }
        }

        throw new TimeoutException("월드맵을 좌상단 끝으로 정렬하지 못했습니다. 12회 제한 후 정지합니다.");
    }

    private async Task<DetectionResult> FindDungeonOnWorldMapAsync(
        string mapTarget,
        string dungeonName,
        CancellationToken ct)
    {
        const double EdgeThreshold = 6.0;
        bool moveViewportRight = true;
        Log?.Invoke($"[던전 자동이동] {dungeonName} · 좌상단 기준 지그재그 탐색 시작");

        for (int row = 0; row < 4; row++)
        {
            int edgeStable = 0;
            for (int col = 0; col < 6; col++)
            {
                ct.ThrowIfCancellationRequested();
                using var frame = await CaptureGameWindowAsync(ct);
                var found = await _detector.DetectAsync(mapTarget, frame, ct);
                if (found.Found)
                {
                    Log?.Invoke($"[던전 자동이동] {dungeonName} 글씨 발견 row={row + 1}, col={col + 1} @ {found.Bounds}");
                    return found;
                }

                if (col == 5) break;
                var from = moveViewportRight ? new Point(530, 440) : new Point(220, 440);
                var to = moveViewportRight ? new Point(220, 440) : new Point(530, 440);
                _hwnd = await ResolveRequiredGameWindowAsync(ct);
                _input.DragClientPoint(_hwnd, from, to, 850);
                await Task.Delay(600, ct);
                using var after = await CaptureGameWindowAsync(ct);
                double diff = MapFrameDifference(frame, after);
                edgeStable = diff <= EdgeThreshold ? edgeStable + 1 : 0;
                Log?.Invoke($"[던전 자동이동] 가로 탐색 row={row + 1}, step={col + 1} · 화면변화={diff:0.00} · 경계={edgeStable}/2");
                if (edgeStable >= 2) break;
            }

            using (var last = await CaptureGameWindowAsync(ct))
            {
                var found = await _detector.DetectAsync(mapTarget, last, ct);
                if (found.Found) return found;
            }
            if (row == 3) break;

            using var beforeDown = await CaptureGameWindowAsync(ct);
            _hwnd = await ResolveRequiredGameWindowAsync(ct);
            _input.DragClientPoint(_hwnd, new Point(380, 650), new Point(380, 320), 850);
            await Task.Delay(650, ct);
            using var afterDown = await CaptureGameWindowAsync(ct);
            double downDiff = MapFrameDifference(beforeDown, afterDown);
            Log?.Invoke($"[던전 자동이동] 다음 줄 이동 {row + 1}-> {row + 2} · 화면변화={downDiff:0.00}");
            if (downDiff <= EdgeThreshold)
            {
                using var confirmBefore = await CaptureGameWindowAsync(ct);
                _input.DragClientPoint(_hwnd, new Point(380, 650), new Point(380, 320), 850);
                await Task.Delay(650, ct);
                using var confirmAfter = await CaptureGameWindowAsync(ct);
                double confirmDiff = MapFrameDifference(confirmBefore, confirmAfter);
                Log?.Invoke($"[던전 자동이동] 세로 경계 재확인 · 화면변화={confirmDiff:0.00}");
                if (confirmDiff <= EdgeThreshold) break;
            }
            moveViewportRight = !moveViewportRight;
        }

        return DetectionResult.NotFound;
    }

    // The map label is an OCR anchor only. This validates the colored dungeon icon above it.
    // Purple is the normal state; the selected/highlight state can render blue, so both are accepted.
    private static bool TryFindDungeonIconAboveLabel(Bitmap frame, Rectangle labelBounds, out Rectangle iconBounds)
    {
        iconBounds = Rectangle.Empty;
        if (labelBounds.Width <= 0 || labelBounds.Height <= 0) return false;

        int centerX = labelBounds.Left + labelBounds.Width / 2;
        int searchWidth = Math.Max(120, labelBounds.Width + 100);
        var search = WindowCapture.ClampRoi(
            new Rectangle(centerX - searchWidth / 2, labelBounds.Top - 105, searchWidth, 105),
            frame.Size);
        if (search.Width < 20 || search.Height < 20) return false;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        int pixels = 0;

        for (int y = search.Top; y < search.Bottom; y++)
        {
            for (int x = search.Left; x < search.Right; x++)
            {
                Color c = frame.GetPixel(x, y);
                bool purpleOrBlue =
                    c.B >= 140 &&
                    c.B > c.G * 1.12 &&
                    c.G < 190 &&
                    (c.R >= 65 || c.B >= 210);

                if (!purpleOrBlue) continue;
                pixels++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (pixels < 120 || minX == int.MaxValue) return false;

        var bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        if (bounds.Width < 18 || bounds.Height < 18 || bounds.Width > 85 || bounds.Height > 85)
            return false;

        // The icon must actually be above the OCR text, not beside/below it.
        if (bounds.Bottom > labelBounds.Top + 4)
            return false;

        iconBounds = bounds;
        return true;
    }

    private static double MapFrameDifference(Bitmap before, Bitmap after)
    {
        int width = Math.Min(before.Width, after.Width);
        int height = Math.Min(before.Height, after.Height);
        var roi = WindowCapture.ClampRoi(new Rectangle(70, 140, 490, 560), new Size(width, height));
        if (roi.Width < 20 || roi.Height < 20) return double.MaxValue;

        long sum = 0;
        long samples = 0;
        for (int y = roi.Top; y < roi.Bottom; y += 12)
        {
            for (int x = roi.Left; x < roi.Right; x += 12)
            {
                Color a = before.GetPixel(x, y);
                Color b = after.GetPixel(x, y);
                sum += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                samples++;
            }
        }
        return samples == 0 ? double.MaxValue : sum / (samples * 3.0);
    }

    // DUNGEON_FULL_STABILITY_HARDENING_V7
    // HANDLE_RECOVERY_V2
    private async Task<nint> ResolveRequiredGameWindowAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (WindowTools.IsRequiredGameWindow(_hwnd))
                return _hwnd;

            var found = WindowTools.FindRequiredGameWindow();

            if (found != 0)
            {
                if (found != _hwnd)
                {
                    Log?.Invoke(
                        $"마비노기 모바일 창 핸들 재연결: " +
                        $"0x{_hwnd.ToInt64():X} -> 0x{found.ToInt64():X}");
                }

                _hwnd = found;
                return _hwnd;
            }

            if (attempt == 0)
            {
                Log?.Invoke(
                    "마비노기 모바일 창 핸들이 사라졌습니다. " +
                    "새 창을 다시 찾는 중...");
            }

            await Task.Delay(200, ct);
        }

        throw new OperationCanceledException(
            "마비노기 모바일 창을 약 6초 동안 다시 찾지 못해 자동화를 정지합니다.");
    }

    private async Task<Bitmap> CaptureGameWindowAsync(CancellationToken ct)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= 12; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            _hwnd = await ResolveRequiredGameWindowAsync(ct);

            try
            {
                return _capture.CaptureClient(_hwnd);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException ||
                ex is System.ComponentModel.Win32Exception ||
                ex is System.Runtime.InteropServices.ExternalException)
            {
                lastError = ex;

                if (attempt == 1 || attempt == 6)
                {
                    Log?.Invoke(
                        $"화면 캡처 재시도 {attempt}/12: {ex.Message}");
                }

                await Task.Delay(150, ct);
            }
        }

        throw new InvalidOperationException(
            "마비노기 모바일 화면 캡처가 반복해서 실패했습니다.",
            lastError);
    }
    public void Dispose() => _input.Dispose();
}
