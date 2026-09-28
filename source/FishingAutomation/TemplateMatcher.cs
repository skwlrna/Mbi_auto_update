using OpenCvSharp;

namespace FishingAutomation;

public sealed class MatchResult
{
    public double Score { get; init; }
    public Rectangle Rect { get; init; }
    public double Scale { get; init; } = 1.0;
}

public sealed class TemplateMatcher : IDisposable
{
    private readonly Mat _hook;
    private readonly Mat _gaugeBgr;
    private readonly Mat _gaugeMask;
    private readonly Mat? _health;
    private readonly Mat? _compass;
    private readonly List<(Mat Image, double Scale)> _hookVariants;
    private readonly List<(Mat Image, double Scale)> _gaugeVariants;
    private readonly List<(Mat Image, double Scale)>? _healthVariants;
    private readonly List<(Mat Image, double Scale)>? _compassVariants;
    private readonly AppLog _log;

    public bool HasHealth => _health is not null;
    public bool HasCompass => _compass is not null;

    public TemplateMatcher(string templateDir, AppLog log)
    {
        _log = log;
        _hook = LoadGray(Path.Combine(templateDir, "hook.png"), required: true)!;
        _gaugeBgr = LoadBgr(Path.Combine(templateDir, "gauge.png"), required: true)!;
        _gaugeMask = BuildGaugeBarMask(_gaugeBgr);
        _health = LoadGray(Path.Combine(templateDir, "healthbar.png"), required: false);
        _compass = LoadGray(Path.Combine(templateDir, "compass.png"), required: false);

        // UI assets can be captured at a slightly different internal render scale even
        // when the game client itself is exactly 800x1000. Pre-build multiple sizes so
        // matching stays fast during the 60-80 ms loops.
        _hookVariants = BuildVariants(_hook, 0.65, 1.55, 0.05, nearest: false);
        _gaugeVariants = BuildVariants(_gaugeMask, 0.70, 1.40, 0.05, nearest: true);
        _healthVariants = _health is null ? null : BuildVariants(_health, 0.60, 1.30, 0.05, nearest: false);
        _compassVariants = _compass is null ? null : BuildVariants(_compass, 0.65, 1.55, 0.05, nearest: false);

        _log.Write($"템플릿: hook={_hook.Width}x{_hook.Height}, gauge={_gaugeBgr.Width}x{_gaugeBgr.Height}" +
                   (HasHealth ? $", health={_health!.Width}x{_health.Height}" : "") +
                   (HasCompass ? $", compass={_compass!.Width}x{_compass.Height}" : ""));
    }

    private Mat? LoadGray(string path, bool required)
    {
        using Mat? src = LoadUnchanged(path, required);
        if (src is null) return null;
        Mat gray = new();
        if (src.Channels() == 4) Cv2.CvtColor(src, gray, ColorConversionCodes.BGRA2GRAY);
        else if (src.Channels() == 3) Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        else src.CopyTo(gray);
        return gray;
    }

    private Mat? LoadBgr(string path, bool required)
    {
        using Mat? src = LoadUnchanged(path, required);
        if (src is null) return null;
        Mat bgr = new();
        if (src.Channels() == 4) Cv2.CvtColor(src, bgr, ColorConversionCodes.BGRA2BGR);
        else if (src.Channels() == 3) src.CopyTo(bgr);
        else Cv2.CvtColor(src, bgr, ColorConversionCodes.GRAY2BGR);
        return bgr;
    }

    private Mat? LoadUnchanged(string path, bool required)
    {
        if (!File.Exists(path))
        {
            if (required) throw new FileNotFoundException($"필수 템플릿 없음: {path}");
            _log.Write($"선택 템플릿 없음: {Path.GetFileName(path)}");
            return null;
        }

        Mat src = Cv2.ImRead(path, ImreadModes.Unchanged);
        if (src.Empty())
        {
            src.Dispose();
            throw new InvalidDataException($"템플릿 읽기 실패: {path}");
        }
        return src;
    }

    private static List<(Mat Image, double Scale)> BuildVariants(Mat source, double minScale, double maxScale, double step, bool nearest)
    {
        var variants = new List<(Mat Image, double Scale)>();
        for (double scale = minScale; scale <= maxScale + 0.0001; scale += step)
        {
            int w = Math.Max(4, (int)Math.Round(source.Width * scale));
            int h = Math.Max(4, (int)Math.Round(source.Height * scale));
            Mat resized = new();
            Cv2.Resize(source, resized, new OpenCvSharp.Size(w, h), 0, 0,
                nearest ? InterpolationFlags.Nearest : (scale < 1.0 ? InterpolationFlags.Area : InterpolationFlags.Cubic));
            variants.Add((resized, scale));
        }
        return variants;
    }

    public MatchResult MatchHook(Mat gray, Rectangle roi) => MatchVariants(gray, roi, _hookVariants);

    public MatchResult? MatchCompass(Mat gray, Rectangle roi)
        => _compassVariants is null ? null : MatchVariants(gray, roi, _compassVariants);

    public MatchResult? MatchHealth(Mat gray, Rectangle roi)
        => _healthVariants is null ? null : MatchVariants(gray, roi, _healthVariants);

    private static MatchResult MatchSingle(Mat source, Rectangle roiRect, Mat template)
    {
        var safe = Clamp(roiRect, source.Width, source.Height);
        if (safe.Width < template.Width || safe.Height < template.Height)
            return new MatchResult { Score = 0, Rect = Rectangle.Empty, Scale = 1.0 };

        using var roi = new Mat(source, new OpenCvSharp.Rect(safe.X, safe.Y, safe.Width, safe.Height));
        using var result = new Mat();
        Cv2.MatchTemplate(roi, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out double max, out _, out OpenCvSharp.Point p);
        return new MatchResult
        {
            Score = max,
            Rect = new Rectangle(safe.X + p.X, safe.Y + p.Y, template.Width, template.Height),
            Scale = 1.0
        };
    }

    private static MatchResult MatchVariants(Mat source, Rectangle roiRect, List<(Mat Image, double Scale)> variants)
    {
        var safe = Clamp(roiRect, source.Width, source.Height);
        MatchResult best = new() { Score = double.NegativeInfinity, Rect = Rectangle.Empty, Scale = 1.0 };
        using var roi = new Mat(source, new OpenCvSharp.Rect(safe.X, safe.Y, safe.Width, safe.Height));

        foreach (var variant in variants)
        {
            Mat template = variant.Image;
            if (safe.Width < template.Width || safe.Height < template.Height) continue;

            using var result = new Mat();
            Cv2.MatchTemplate(roi, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double max, out _, out OpenCvSharp.Point p);
            if (max > best.Score)
            {
                best = new MatchResult
                {
                    Score = max,
                    Rect = new Rectangle(safe.X + p.X, safe.Y + p.Y, template.Width, template.Height),
                    Scale = variant.Scale
                };
            }
        }

        return double.IsNegativeInfinity(best.Score)
            ? new MatchResult { Score = 0, Rect = Rectangle.Empty, Scale = 1.0 }
            : best;
    }

    /// <summary>
    /// Gauge matching uses a fill-invariant mask. Both bright green fill pixels and
    /// neutral dark empty-bar pixels become white; the blue game background stays black.
    /// This preserves the capsule shape while ignoring how full the gauge happens to be.
    /// </summary>
    private MatchResult MatchGaugeShape(CapturedFrame frame, Rectangle roiRect)
    {
        Rectangle safe = Clamp(roiRect, frame.Bgr.Width, frame.Bgr.Height);
        using var roiBgr = new Mat(frame.Bgr, new OpenCvSharp.Rect(safe.X, safe.Y, safe.Width, safe.Height));
        using var roiMask = BuildGaugeBarMask(roiBgr);
        MatchResult local = MatchVariants(roiMask, new Rectangle(0, 0, roiMask.Width, roiMask.Height), _gaugeVariants);
        return new MatchResult
        {
            Score = local.Score,
            Rect = local.Rect.IsEmpty
                ? Rectangle.Empty
                : new Rectangle(safe.X + local.Rect.X, safe.Y + local.Rect.Y, local.Rect.Width, local.Rect.Height),
            Scale = local.Scale
        };
    }

    public GaugeAnchor? DetectGauge(CapturedFrame frame, AutomationConfig cfg, out MatchResult gaugeMatch)
    {
        Rectangle roi = cfg.GaugeRoi.ToRectangle();

        // v8: Find the physical gauge bar first from its very distinctive pixels.
        // The real bar is a ~100 px horizontal capsule made from strong green fill plus
        // the fixed neutral-dark empty section. This is far more reliable than asking
        // MatchTemplate to search a dark watery scene for a 12 px-high strip.
        PixelGaugeCandidate? direct = FindGaugeByPixels(frame.Bgr, roi);
        MatchResult templateMatch = MatchGaugeShape(frame, roi);

        if (direct is not null)
        {
            gaugeMatch = new MatchResult
            {
                Score = Math.Max(templateMatch.Score, direct.Confidence),
                Rect = new Rectangle(direct.Left, direct.Top, direct.Length, direct.Height),
                Scale = (double)direct.Length / Math.Max(1, _gaugeBgr.Width)
            };

            var refined = new RefinedBar(direct.Left, direct.CenterY, direct.Length, direct.GreenFill);
            var empty = SampleEmptyBarColor(frame.Bgr, refined);
            return new GaugeAnchor(
                direct.Left, direct.CenterY, direct.Length, gaugeMatch.Score, gaugeMatch.Scale,
                empty.HasSample, empty.B, empty.G, empty.R);
        }

        // Keep template matching as a fallback for unusual rendering conditions.
        gaugeMatch = templateMatch;
        bool strong = gaugeMatch.Score >= cfg.GaugeStrongThreshold;
        bool weakWithGreen = gaugeMatch.Score >= cfg.GaugeWeakThreshold &&
                             HasStrongGreen(frame.Bgr, Inflate(gaugeMatch.Rect, 6, 5), 120);
        if (!strong && !weakWithGreen) return null;

        MatchResult? hp = MatchHealth(frame.Gray, roi);
        if (hp is not null && hp.Score >= cfg.HealthBarThreshold &&
            OverlapRatio(gaugeMatch.Rect, hp.Rect) > 0.20)
        {
            return null;
        }

        var fallback = RefineInitialBar(frame.Bgr, gaugeMatch.Rect);
        if (fallback is null)
            return null;

        int length = fallback.Length;
        if (length < 60 || length > 180)
            length = Math.Clamp(cfg.GaugeLength, 60, 180);

        var fallbackEmpty = SampleEmptyBarColor(frame.Bgr, fallback);
        return new GaugeAnchor(
            fallback.Left, fallback.CenterY, length, gaugeMatch.Score, gaugeMatch.Scale,
            fallbackEmpty.HasSample, fallbackEmpty.B, fallbackEmpty.G, fallbackEmpty.R);
    }

    private sealed record RowGaugeCandidate(int Y, int Left, int Length, int GreenStart, int GreenEnd, int GreenCount);
    private sealed record PixelGaugeCandidate(int Left, int Top, int CenterY, int Length, int Height, int GreenFill, double Confidence);

    private static PixelGaugeCandidate? FindGaugeByPixels(Mat bgr, Rectangle roiRect)
    {
        Rectangle roi = Clamp(roiRect, bgr.Width, bgr.Height);
        var rows = new List<RowGaugeCandidate>();

        for (int y = roi.Top; y < roi.Bottom; y++)
        {
            int x = roi.Left;
            while (x < roi.Right)
            {
                while (x < roi.Right && !IsStrictGaugeGreen(bgr.At<Vec3b>(y, x))) x++;
                if (x >= roi.Right) break;

                int greenStart = x;
                while (x < roi.Right && IsStrictGaugeGreen(bgr.At<Vec3b>(y, x))) x++;
                int greenEnd = x;
                int greenCount = greenEnd - greenStart;
                if (greenCount < 4) continue;

                int left = greenStart;
                while (left > roi.Left && greenStart - left < 10 && IsDirectGaugeBodyPixel(bgr.At<Vec3b>(y, left - 1)))
                    left--;

                int right = greenEnd;
                while (right < roi.Right && right - left < 150 && IsDirectGaugeBodyPixel(bgr.At<Vec3b>(y, right)))
                    right++;

                int length = right - left;
                int greenOffset = greenStart - left;
                if (length < 85 || length > 135 || greenOffset > 12)
                    continue;

                rows.Add(new RowGaugeCandidate(y, left, length, greenStart, greenEnd, greenCount));
            }
        }

        if (rows.Count == 0) return null;

        List<RowGaugeCandidate>? bestCluster = null;
        int bestScore = int.MinValue;

        foreach (var seed in rows)
        {
            var cluster = rows
                .Where(c => Math.Abs(c.Y - seed.Y) <= 12 &&
                            Math.Abs(c.Left - seed.Left) <= 8 &&
                            Math.Abs(c.Length - seed.Length) <= 14)
                .OrderBy(c => c.Y)
                .ToList();

            int distinctRows = cluster.Select(c => c.Y).Distinct().Count();
            if (distinctRows < 3) continue;

            int medianLength = Median(cluster.Select(c => c.Length));
            int medianLeft = Median(cluster.Select(c => c.Left));
            int maxGreen = cluster.Max(c => c.GreenEnd - medianLeft);
            int score = distinctRows * 100 + cluster.Sum(c => c.GreenCount) - Math.Abs(medianLength - 101) * 2;

            if (score > bestScore)
            {
                bestScore = score;
                bestCluster = cluster;
            }
        }

        if (bestCluster is null) return null;

        int leftFinal = Median(bestCluster.Select(c => c.Left));
        int lenFinal = Median(bestCluster.Select(c => c.Length));
        int top = bestCluster.Min(c => c.Y);
        int bottom = bestCluster.Max(c => c.Y);
        int centerY = Median(bestCluster.Select(c => c.Y));
        int fill = Math.Clamp(bestCluster.Max(c => c.GreenEnd - leftFinal), 0, lenFinal);
        int support = bestCluster.Select(c => c.Y).Distinct().Count();

        // Three matching rows already form a strong structural signal; 7-9 rows is
        // what the current 800x1000 client normally produces.
        double confidence = Math.Min(0.995, 0.88 + support * 0.012 + (Math.Abs(lenFinal - 101) <= 12 ? 0.02 : 0.0));
        return new PixelGaugeCandidate(leftFinal, top, centerY, lenFinal, bottom - top + 1, fill, confidence);
    }

    private static int Median(IEnumerable<int> values)
    {
        int[] a = values.OrderBy(v => v).ToArray();
        return a.Length == 0 ? 0 : a[a.Length / 2];
    }

    private static bool IsStrictGaugeGreen(Vec3b p)
    {
        int b = p.Item0, g = p.Item1, r = p.Item2;
        return g >= 180 && g - r >= 110 && g - b >= 110;
    }

    private static bool IsDirectGaugeBodyPixel(Vec3b p)
    {
        if (IsStrictGaugeGreen(p)) return true;
        int b = p.Item0, g = p.Item1, r = p.Item2;
        int bright = (r + g + b) / 3;
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));

        // The empty section in the supplied 800x1000 captures is roughly RGB
        // 39/42/50. Keep this deliberately narrow so blue cave/water scenery cannot
        // become a fake bar.
        return bright >= 28 && bright <= 70 && max - min <= 20;
    }

    private sealed record RefinedBar(int Left, int CenterY, int Length, int GreenCount);

    private static RefinedBar? RefineInitialBar(Mat bgr, Rectangle match)
    {
        Rectangle search = Clamp(Inflate(match, 10, 8), bgr.Width, bgr.Height);
        RefinedBar? best = null;
        int bestScore = int.MinValue;

        for (int y = search.Top; y < search.Bottom; y++)
        {
            int x = search.Left;
            while (x < search.Right)
            {
                while (x < search.Right && !IsGaugeBarPixel(bgr.At<Vec3b>(y, x))) x++;
                if (x >= search.Right) break;

                int start = x;
                int greens = 0;
                int firstGreen = -1;
                while (x < search.Right && IsGaugeBarPixel(bgr.At<Vec3b>(y, x)))
                {
                    var p = bgr.At<Vec3b>(y, x);
                    if (IsGreen(p.Item2, p.Item1, p.Item0, 110))
                    {
                        greens++;
                        if (firstGreen < 0) firstGreen = x;
                    }
                    x++;
                }
                int len = x - start;
                if (len < 60 || len > 180 || greens < 4 || firstGreen < 0) continue;

                // Fishing fill grows from the left edge.  Reject green text/effects that
                // happen to sit inside a long dark run.
                if (firstGreen - start > Math.Max(18, len / 4)) continue;

                int centerDistance = Math.Abs(y - (match.Y + match.Height / 2));
                int lengthDistance = Math.Abs(len - match.Width);
                int score = len * 3 + greens * 3 - centerDistance * 3 - lengthDistance;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new RefinedBar(start, y, len, greens);
                }
            }
        }
        return best;
    }

    private sealed record EmptyBarSample(bool HasSample, int B, int G, int R);

    private static EmptyBarSample SampleEmptyBarColor(Mat bgr, RefinedBar bar)
    {
        int left = Math.Clamp(bar.Left, 0, bgr.Width - 1);
        int right = Math.Clamp(bar.Left + bar.Length, left + 1, bgr.Width);
        int y = Math.Clamp(bar.CenterY, 0, bgr.Height - 1);

        int rightmostGreen = -1;
        for (int x = left; x < right; x++)
        {
            var p = bgr.At<Vec3b>(y, x);
            if (IsGreen(p.Item2, p.Item1, p.Item0, 110))
                rightmostGreen = x;
        }

        var bs = new List<int>();
        var gs = new List<int>();
        var rs = new List<int>();

        int sampleStart = rightmostGreen >= 0 ? Math.Min(right, rightmostGreen + 2) : left;
        for (int x = sampleStart; x < right; x++)
        {
            var p = bgr.At<Vec3b>(y, x);
            int b = p.Item0, g = p.Item1, r = p.Item2;
            if (IsGreen(r, g, b, 110)) continue;

            int bright = (r + g + b) / 3;
            int spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            if (bright >= 20 && bright <= 180 && spread <= 45)
            {
                bs.Add(b); gs.Add(g); rs.Add(r);
            }
        }

        if (bs.Count < 4)
            return new EmptyBarSample(false, 0, 0, 0);

        bs.Sort(); gs.Sort(); rs.Sort();
        int mid = bs.Count / 2;
        return new EmptyBarSample(true, bs[mid], gs[mid], rs[mid]);
    }

    private static bool IsNearEmptyColor(Vec3b p, GaugeAnchor anchor)
    {
        if (!anchor.HasEmptySample) return false;
        int db = Math.Abs(p.Item0 - anchor.EmptyB);
        int dg = Math.Abs(p.Item1 - anchor.EmptyG);
        int dr = Math.Abs(p.Item2 - anchor.EmptyR);
        return db <= 24 && dg <= 24 && dr <= 24 && db + dg + dr <= 48;
    }

    public static FillMeasurement MeasureFill(Mat bgr, GaugeAnchor anchor)
    {
        int length = Math.Clamp(anchor.Length, 60, 180);
        int bestScore = int.MinValue;
        int bestFill = 0;
        bool bestVisible = false;
        int bestX = anchor.LeftX;
        int bestY = anchor.CenterY;

        for (int dy = -12; dy <= 12; dy++)
        {
            int y = anchor.CenterY + dy;
            if (y < 0 || y >= bgr.Height) continue;

            for (int dx = -8; dx <= 8; dx++)
            {
                int x0 = anchor.LeftX + dx;
                if (x0 < 0 || x0 + length > bgr.Width) continue;

                int greenCount = 0;
                int emptyCount = 0;
                int rightmostGreen = -1;
                int firstGreen = -1;
                int longestBodyRun = 0;
                int bodyRun = 0;

                for (int x = x0; x < x0 + length; x++)
                {
                    var p = bgr.At<Vec3b>(y, x);
                    int b = p.Item0, g = p.Item1, r = p.Item2;
                    bool green = IsGreen(r, g, b, 110);
                    bool empty = !green && IsNearEmptyColor(p, anchor);

                    if (green)
                    {
                        greenCount++;
                        if (firstGreen < 0) firstGreen = x;
                        rightmostGreen = x;
                    }
                    if (empty) emptyCount++;

                    if (green || empty)
                    {
                        bodyRun++;
                        if (bodyRun > longestBodyRun) longestBodyRun = bodyRun;
                    }
                    else
                    {
                        bodyRun = 0;
                    }
                }

                // Reacquire the same physical bar, not arbitrary dark scenery.  A visible
                // frame either contains green fill, or (at zero fill) a long run matching
                // the empty-bar color calibrated on the first gauge frame.
                bool greenVisible = greenCount >= 2 &&
                                    longestBodyRun >= Math.Max(24, (int)(length * 0.35));
                bool emptyVisible = anchor.HasEmptySample &&
                                    emptyCount >= Math.Max(28, (int)(length * 0.45)) &&
                                    longestBodyRun >= Math.Max(40, (int)(length * 0.55));
                bool visible = greenVisible || emptyVisible;

                int fill = 0;
                if (greenCount >= 2 && firstGreen >= 0 && rightmostGreen >= firstGreen)
                    fill = Math.Clamp(rightmostGreen - x0 + 1, 0, length);

                int score = greenCount * 6 + emptyCount * 4 + longestBodyRun * 3
                            - Math.Abs(dx) * 2 - Math.Abs(dy) * 2;
                if (visible) score += 1000;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestFill = fill;
                    bestVisible = visible;
                    bestX = x0;
                    bestY = y;
                }
            }
        }

        return new FillMeasurement(bestFill, bestVisible, bestX, bestY);
    }

    private static Mat BuildGaugeBarMask(Mat bgr)
    {
        Mat mask = new(bgr.Rows, bgr.Cols, MatType.CV_8UC1, Scalar.Black);
        for (int y = 0; y < bgr.Rows; y++)
        for (int x = 0; x < bgr.Cols; x++)
        {
            var p = bgr.At<Vec3b>(y, x);
            int b = p.Item0, g = p.Item1, r = p.Item2;
            if (IsGreen(r, g, b, 110) || IsNeutralDarkBar(r, g, b))
                mask.Set(y, x, (byte)255);
        }
        return mask;
    }

    private static bool IsGaugeBarPixel(Vec3b p)
    {
        int b = p.Item0, g = p.Item1, r = p.Item2;
        return IsGreen(r, g, b, 110) || IsNeutralDarkBar(r, g, b);
    }

    private static bool IsNeutralDarkBar(int r, int g, int b)
    {
        int bright = (r + g + b) / 3;
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        return bright >= 25 && bright <= 160 && max - min <= 35;
    }

    private static bool HasStrongGreen(Mat bgr, Rectangle rect, int delta)
    {
        if (rect.IsEmpty) return false;
        Rectangle r = Clamp(rect, bgr.Width, bgr.Height);
        int run;
        for (int y = r.Top; y < r.Bottom; y++)
        {
            run = 0;
            for (int x = r.Left; x < r.Right; x++)
            {
                var p = bgr.At<Vec3b>(y, x);
                if (p.Item1 - p.Item2 >= delta && p.Item1 - p.Item0 >= delta && p.Item1 >= 180)
                {
                    run++;
                    if (run >= 4) return true;
                }
                else run = 0;
            }
        }
        return false;
    }

    private static bool IsGreen(int r, int g, int b, int delta)
        => g >= 180 && g - r >= delta && g - b >= delta;

    private static double OverlapRatio(Rectangle a, Rectangle b)
    {
        Rectangle i = Rectangle.Intersect(a, b);
        if (i.IsEmpty || a.Width <= 0 || a.Height <= 0) return 0;
        return (double)(i.Width * i.Height) / (a.Width * a.Height);
    }

    private static Rectangle Inflate(Rectangle r, int x, int y)
    {
        if (r.IsEmpty) return r;
        r.Inflate(x, y);
        return r;
    }

    private static Rectangle Clamp(Rectangle r, int w, int h)
    {
        int x = Math.Clamp(r.X, 0, Math.Max(0, w - 1));
        int y = Math.Clamp(r.Y, 0, Math.Max(0, h - 1));
        int right = Math.Clamp(r.Right, x + 1, w);
        int bottom = Math.Clamp(r.Bottom, y + 1, h);
        return new Rectangle(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    public void Dispose()
    {
        foreach (var v in _hookVariants) v.Image.Dispose();
        foreach (var v in _gaugeVariants) v.Image.Dispose();
        if (_healthVariants is not null)
            foreach (var v in _healthVariants) v.Image.Dispose();
        if (_compassVariants is not null)
            foreach (var v in _compassVariants) v.Image.Dispose();

        _hook.Dispose();
        _gaugeMask.Dispose();
        _gaugeBgr.Dispose();
        _health?.Dispose();
        _compass?.Dispose();
    }
}

public sealed record GaugeAnchor(int LeftX, int CenterY, int Length, double Score, double Scale, bool HasEmptySample, int EmptyB, int EmptyG, int EmptyR);
public sealed record FillMeasurement(int Fill, bool Visible, int LeftX, int CenterY);
