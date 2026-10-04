using System.Drawing.Imaging;

namespace DungeonVisionBot;

/// <summary>
/// Shared production-screen runtime for crafting / altering / gathering.
///
/// V3 rules:
/// - every input starts from a fresh 800x1000 client capture;
/// - dynamic field animation is never compared against an old full-frame image;
/// - OCR-authorized clicks are re-read on a second fresh frame and the second
///   geometry is the one that is clicked;
/// - fixed navigation coordinates are only used after a stable screen anchor
///   has been verified.
/// </summary>
internal sealed class ProductionUiRuntime : IDisposable
{
    private readonly nint _hwnd;
    private readonly GuardedInputController _input;
    private readonly WindowCapture _capture = new();
    private readonly OcrRecognizer _ocr = new();
    private readonly TemplateMatcher _templates = new(AppContext.BaseDirectory);
    private readonly string _debugDir;
    private readonly string _debugStem;
    private bool _disposed;

    internal ProductionUiRuntime(
        nint hwnd,
        AppSettings settings,
        string debugDir,
        string debugStem)
    {
        _hwnd = hwnd;
        _debugDir = debugDir;
        _debugStem = string.IsNullOrWhiteSpace(debugStem) ? "production" : debugStem;
        _input = new GuardedInputController(
            new InterceptionInput(
                settings.InterceptionMouseDevice,
                settings.InterceptionKeyboardDevice));
    }

    internal string InputMode => _input.ModeName;
    internal OcrRecognizer Ocr => _ocr;
    internal nint WindowHandle => _hwnd;

    internal Bitmap Capture(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProductionUiRuntime));

        _input.SetCancellation(ct);
        if (!WindowTools.IsRequiredGameWindow(_hwnd))
            throw new InvalidOperationException(
                "게임 창이 변경되어 생산 자동화 입력을 정지합니다.");

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

    internal void TapFresh(ushort scanCode, CancellationToken ct)
    {
        using var frame = Capture(ct);
        _input.TapScanCode(scanCode);
    }

    internal void PasteFresh(string text, CancellationToken ct)
    {
        using var frame = Capture(ct);
        _input.PasteText(text);
    }

    internal void ClickFresh(Point point, CancellationToken ct)
    {
        using var frame = Capture(ct);
        _input.ClickClientPoint(_hwnd, point);
    }

    internal void DragFresh(Point start, Point end, int durationMs, CancellationToken ct)
    {
        using var frame = Capture(ct);
        _input.DragClientPoint(_hwnd, start, end, durationMs);
    }

    internal async Task<DetectionResult?> FindUniqueAsync(
        Bitmap frame,
        Rectangle roi,
        string text,
        CancellationToken ct,
        bool dimText = true)
    {
        roi = Rectangle.Intersect(roi, new Rectangle(Point.Empty, frame.Size));
        if (roi.Width <= 0 || roi.Height <= 0)
            return null;

        var found = await _ocr.FindAlteringLabelsAsync(
            frame,
            roi,
            text,
            ct,
            acceptedBounds: roi,
            dimText: dimText);
        return found.Count == 1 ? found[0] : null;
    }

    internal async Task<DetectionResult?> FindCompactAsync(
        Bitmap frame,
        Rectangle roi,
        string text,
        CancellationToken ct)
    {
        roi = Rectangle.Intersect(roi, new Rectangle(Point.Empty, frame.Size));
        if (roi.Width <= 0 || roi.Height <= 0)
            return null;

        var found = await _ocr.FindCompactLabelAsync(frame, roi, text, ct);
        return found.Found ? found : null;
    }

    internal async Task<DetectionResult> RequireStableExactAsync(
        string text,
        Rectangle roi,
        CancellationToken ct,
        string failure,
        bool compact = false,
        bool dimText = true,
        int settleMs = 140)
    {
        DetectionResult? first = null;

        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            DetectionResult? found = compact
                ? await FindCompactAsync(frame, roi, text, ct)
                : await FindUniqueAsync(frame, roi, text, ct, dimText);

            if (found is null)
                throw Failure(frame, failure);

            if (pass == 0)
            {
                first = found;
                await Task.Delay(settleMs, ct);
                continue;
            }

            if (first is null || !Stable(first.Value.Bounds, found.Value.Bounds))
                throw Failure(
                    frame,
                    failure + " · 두 프레임의 위치가 안정적으로 일치하지 않았습니다.");

            return found.Value;
        }

        using var failed = Capture(ct);
        throw Failure(failed, failure);
    }

    internal async Task<DetectionResult> ClickStableExactAsync(
        string text,
        Rectangle roi,
        CancellationToken ct,
        string failure,
        bool compact = false,
        bool dimText = true,
        int settleMs = 140)
    {
        var second = await RequireStableExactAsync(
            text, roi, ct, failure, compact, dimText, settleMs);

        // Renew the guard and re-read only the small target ROI immediately
        // before the click. This replaces the old whole-screen pixel-diff gate.
        using var fresh = Capture(ct);
        DetectionResult? current = compact
            ? await FindCompactAsync(fresh, roi, text, ct)
            : await FindUniqueAsync(fresh, roi, text, ct, dimText);

        if (current is null || !Stable(second.Bounds, current.Value.Bounds))
            throw Failure(
                fresh,
                failure + " · 클릭 직전 대상 위치가 바뀌었습니다.");

        _input.ClickClientPoint(_hwnd, current.Value.Center);
        return current.Value;
    }

    internal async Task<DetectionResult> ClickOffsetFromStableExactAsync(
        string text,
        Rectangle roi,
        Func<DetectionResult, Point> clickPoint,
        CancellationToken ct,
        string failure,
        bool compact = false,
        bool dimText = true)
    {
        var second = await RequireStableExactAsync(
            text, roi, ct, failure, compact, dimText);

        using var fresh = Capture(ct);
        DetectionResult? current = compact
            ? await FindCompactAsync(fresh, roi, text, ct)
            : await FindUniqueAsync(fresh, roi, text, ct, dimText);
        if (current is null || !Stable(second.Bounds, current.Value.Bounds))
            throw Failure(fresh, failure + " · 클릭 직전 기준점이 바뀌었습니다.");

        Point point = clickPoint(current.Value);
        _input.ClickClientPoint(_hwnd, point);
        return current.Value;
    }

    internal DetectionResult FindBrightTemplate(
        Bitmap frame,
        Rectangle roi,
        string templatePath,
        double threshold,
        double minScale = 0.75,
        double maxScale = 1.35,
        double step = 0.05)
    {
        roi = Rectangle.Intersect(roi, new Rectangle(Point.Empty, frame.Size));
        if (roi.Width <= 0 || roi.Height <= 0)
            return DetectionResult.NotFound;

        return _templates.FindBrightGlyphMultiScale(
            frame,
            roi,
            templatePath,
            threshold,
            minScale,
            maxScale,
            step);
    }

    internal async Task<DetectionResult> ClickOffsetFromStableBrightTemplateAsync(
        string templatePath,
        Rectangle roi,
        Func<DetectionResult, Point> clickPoint,
        CancellationToken ct,
        string failure,
        double threshold = 0.78,
        double minScale = 0.75,
        double maxScale = 1.35,
        double step = 0.05,
        int settleMs = 160)
    {
        DetectionResult? first = null;
        DetectionResult second = DetectionResult.NotFound;

        for (int pass = 0; pass < 2; pass++)
        {
            using var frame = Capture(ct);
            var found = FindBrightTemplate(
                frame, roi, templatePath, threshold, minScale, maxScale, step);
            if (!found.Found)
                throw Failure(
                    frame,
                    failure + $" · 템플릿 점수={found.Score:F3}");

            if (pass == 0)
            {
                first = found;
                await Task.Delay(settleMs, ct);
                continue;
            }

            if (first is null || !Stable(first.Value.Bounds, found.Bounds))
                throw Failure(
                    frame,
                    failure + " · 두 프레임의 템플릿 위치가 안정적으로 일치하지 않았습니다.");

            second = found;
        }

        using var fresh = Capture(ct);
        var current = FindBrightTemplate(
            fresh, roi, templatePath, threshold, minScale, maxScale, step);
        if (!current.Found || !Stable(second.Bounds, current.Bounds))
            throw Failure(
                fresh,
                failure + $" · 클릭 직전 템플릿 위치가 바뀌었습니다. 점수={current.Score:F3}");

        Point point = clickPoint(current);
        if (!new Rectangle(Point.Empty, fresh.Size).Contains(point))
            throw Failure(
                fresh,
                failure + " · 계산된 클릭 좌표가 게임 화면 밖입니다.");

        _input.ClickClientPoint(_hwnd, point);
        return current;
    }

    internal async Task<DetectionResult> ClickStableExactAndPasteAsync(
        string text,
        Rectangle roi,
        string value,
        CancellationToken ct,
        string failure,
        bool compact = false,
        bool dimText = true)
    {
        var found = await ClickStableExactAsync(
            text, roi, ct, failure, compact, dimText);
        await Task.Delay(100, ct);
        PasteFresh(value, ct);
        return found;
    }

    internal async Task VerifyStableAnchorAsync(
        string text,
        Rectangle roi,
        CancellationToken ct,
        string failure,
        bool compact = false,
        bool dimText = true)
    {
        _ = await RequireStableExactAsync(
            text, roi, ct, failure, compact, dimText);
    }

    internal static double MeasureVisualChangeRatio(
        Bitmap before,
        Bitmap after,
        Rectangle roi,
        int sampleStep = 8,
        int channelDelta = 24)
    {
        if (before.Width != after.Width || before.Height != after.Height)
            return 1.0;

        roi = Rectangle.Intersect(
            roi,
            new Rectangle(0, 0, Math.Min(before.Width, after.Width), Math.Min(before.Height, after.Height)));
        if (roi.Width <= 0 || roi.Height <= 0)
            return 0.0;

        int step = Math.Max(2, sampleStep);
        int changed = 0;
        int sampled = 0;

        for (int y = roi.Top; y < roi.Bottom; y += step)
        {
            for (int x = roi.Left; x < roi.Right; x += step)
            {
                Color a = before.GetPixel(x, y);
                Color b = after.GetPixel(x, y);
                int maxDelta = Math.Max(
                    Math.Abs(a.R - b.R),
                    Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                if (maxDelta >= channelDelta)
                    changed++;
                sampled++;
            }
        }

        return sampled == 0 ? 0.0 : (double)changed / sampled;
    }

    internal static bool HasMeaningfulVisualChange(
        Bitmap before,
        Bitmap after,
        Rectangle roi,
        double requiredRatio = 0.08)
        => MeasureVisualChangeRatio(before, after, roi) >= requiredRatio;

    internal InvalidOperationException Failure(Bitmap frame, string message)
    {
        Directory.CreateDirectory(_debugDir);
        string path = Path.Combine(
            _debugDir,
            _debugStem + "-last-failure.png");
        try
        {
            frame.Save(path, ImageFormat.Png);
            return new InvalidOperationException(message + " 진단: " + path);
        }
        catch
        {
            return new InvalidOperationException(message);
        }
    }

    internal static bool Stable(Rectangle a, Rectangle b)
    {
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
            return false;

        Point ac = Center(a);
        Point bc = Center(b);
        return Math.Abs(ac.X - bc.X) <= 18 &&
               Math.Abs(ac.Y - bc.Y) <= 18 &&
               Math.Abs(a.Width - b.Width) <= 24 &&
               Math.Abs(a.Height - b.Height) <= 18;
    }

    private static Point Center(Rectangle r)
        => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _input.Dispose();
    }
}
