using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace DungeonVisionBot;

internal sealed class TemplateMatcher
{
    private readonly string _baseDir;
    public TemplateMatcher(string baseDir) => _baseDir = baseDir;

    public DetectionResult Find(Bitmap frame, Rectangle roi, string templatePath, double threshold)
        => FindMultiScale(frame, roi, templatePath, threshold, 1.0, 1.0, 0.10);

    public DetectionResult FindMultiScale(
        Bitmap frame,
        Rectangle roi,
        string templatePath,
        double threshold,
        double minScale,
        double maxScale,
        double step)
    {
        string fullPath = Path.IsPathRooted(templatePath)
            ? templatePath
            : Path.Combine(_baseDir, templatePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"템플릿 파일을 찾을 수 없습니다: {fullPath}", fullPath);
        if (frame.Width < 2 || frame.Height < 2) return DetectionResult.NotFound;

        Rectangle safeRoi = Rectangle.Intersect(
            new Rectangle(0, 0, frame.Width, frame.Height), roi);
        if (safeRoi.Width < 2 || safeRoi.Height < 2) return DetectionResult.NotFound;

        minScale = Math.Clamp(minScale, 0.20, 3.00);
        maxScale = Math.Clamp(maxScale, minScale, 3.00);
        step = Math.Clamp(step, 0.02, 0.50);

        using var frameMat = BitmapConverter.ToMat(frame);
        if (frameMat.Empty()) return DetectionResult.NotFound;

        using var frameGray = new Mat();
        if (frameMat.Channels() == 4)
            Cv2.CvtColor(frameMat, frameGray, ColorConversionCodes.BGRA2GRAY);
        else if (frameMat.Channels() == 3)
            Cv2.CvtColor(frameMat, frameGray, ColorConversionCodes.BGR2GRAY);
        else
            frameMat.CopyTo(frameGray);

        var cvRoi = new OpenCvSharp.Rect(safeRoi.X, safeRoi.Y, safeRoi.Width, safeRoi.Height);
        using var srcGray = new Mat(frameGray, cvRoi);
        using var tplGrayOriginal = Cv2.ImRead(fullPath, ImreadModes.Grayscale);
        if (tplGrayOriginal.Empty()) return DetectionResult.NotFound;

        double bestScore = double.MinValue;
        OpenCvSharp.Point bestLoc = default;
        int bestW = 0;
        int bestH = 0;

        var scales = new List<double>();
        for (double scale = minScale; scale <= maxScale + 1e-9; scale += step)
            scales.Add(scale);
        if (scales.Count == 0 || Math.Abs(scales[^1] - maxScale) > 1e-6)
            scales.Add(maxScale);

        foreach (double scale in scales)
        {
            int w = Math.Max(8, (int)Math.Round(tplGrayOriginal.Width * scale));
            int h = Math.Max(8, (int)Math.Round(tplGrayOriginal.Height * scale));
            if (w > srcGray.Width || h > srcGray.Height) continue;

            using var tplGray = new Mat();
            if (w == tplGrayOriginal.Width && h == tplGrayOriginal.Height)
                tplGrayOriginal.CopyTo(tplGray);
            else
                Cv2.Resize(
                    tplGrayOriginal,
                    tplGray,
                    new OpenCvSharp.Size(w, h),
                    0,
                    0,
                    scale < 1.0 ? InterpolationFlags.Area : InterpolationFlags.Cubic);

            using var result = new Mat();
            Cv2.MatchTemplate(srcGray, tplGray, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

            if (maxVal > bestScore)
            {
                bestScore = maxVal;
                bestLoc = maxLoc;
                bestW = w;
                bestH = h;
            }
        }

        if (bestW <= 0 || bestH <= 0)
            return DetectionResult.NotFound;

        var bounds = new Rectangle(
            safeRoi.X + bestLoc.X,
            safeRoi.Y + bestLoc.Y,
            bestW,
            bestH);
        if (bestScore < threshold)
            return new DetectionResult(false, bounds, bestScore, null);
        return new DetectionResult(true, bounds, bestScore, null);
    }

    public DetectionResult FindBrightGlyphMultiScale(
        Bitmap frame,
        Rectangle roi,
        string templatePath,
        double threshold,
        double minScale,
        double maxScale,
        double step)
    {
        string fullPath = Path.IsPathRooted(templatePath)
            ? templatePath
            : Path.Combine(_baseDir, templatePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"템플릿 파일을 찾을 수 없습니다: {fullPath}", fullPath);
        if (frame.Width < 2 || frame.Height < 2) return DetectionResult.NotFound;

        Rectangle safeRoi = Rectangle.Intersect(
            new Rectangle(0, 0, frame.Width, frame.Height), roi);
        if (safeRoi.Width < 2 || safeRoi.Height < 2) return DetectionResult.NotFound;

        minScale = Math.Clamp(minScale, 0.50, 1.60);
        maxScale = Math.Clamp(maxScale, minScale, 1.60);
        step = Math.Clamp(step, 0.02, 0.20);

        using var frameMat = BitmapConverter.ToMat(frame);
        if (frameMat.Empty()) return DetectionResult.NotFound;

        using var frameGray = new Mat();
        if (frameMat.Channels() == 4)
            Cv2.CvtColor(frameMat, frameGray, ColorConversionCodes.BGRA2GRAY);
        else if (frameMat.Channels() == 3)
            Cv2.CvtColor(frameMat, frameGray, ColorConversionCodes.BGR2GRAY);
        else
            frameMat.CopyTo(frameGray);

        var cvRoi = new OpenCvSharp.Rect(safeRoi.X, safeRoi.Y, safeRoi.Width, safeRoi.Height);
        using var srcGray = new Mat(frameGray, cvRoi);
        using var tplGrayOriginal = Cv2.ImRead(fullPath, ImreadModes.Grayscale);
        if (tplGrayOriginal.Empty()) return DetectionResult.NotFound;

        double bestScore = double.MinValue;
        OpenCvSharp.Point bestLoc = default;
        int bestW = 0;
        int bestH = 0;

        // Dungeon labels are white pixel text over a translucent tile. Matching the
        // grayscale background made V0.1.50 sensitive to panel tint/selection state.
        // Sweep a few white-text thresholds and match only the binary glyph shape.
        foreach (double whiteThreshold in new[] { 130.0, 150.0, 170.0 })
        {
            using var srcBinary = new Mat();
            using var tplBinaryOriginal = new Mat();
            Cv2.Threshold(srcGray, srcBinary, whiteThreshold, 255, ThresholdTypes.Binary);
            Cv2.Threshold(tplGrayOriginal, tplBinaryOriginal, whiteThreshold, 255, ThresholdTypes.Binary);

            for (double scale = minScale; scale <= maxScale + 1e-9; scale += step)
            {
                int w = Math.Max(8, (int)Math.Round(tplBinaryOriginal.Width * scale));
                int h = Math.Max(8, (int)Math.Round(tplBinaryOriginal.Height * scale));
                if (w > srcBinary.Width || h > srcBinary.Height) continue;

                using var tplBinary = new Mat();
                if (w == tplBinaryOriginal.Width && h == tplBinaryOriginal.Height)
                    tplBinaryOriginal.CopyTo(tplBinary);
                else
                    Cv2.Resize(
                        tplBinaryOriginal,
                        tplBinary,
                        new OpenCvSharp.Size(w, h),
                        0,
                        0,
                        InterpolationFlags.Nearest);

                using var result = new Mat();
                Cv2.MatchTemplate(srcBinary, tplBinary, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                if (maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestLoc = maxLoc;
                    bestW = w;
                    bestH = h;
                }
            }
        }

        if (bestW <= 0 || bestH <= 0)
            return DetectionResult.NotFound;

        var bounds = new Rectangle(
            safeRoi.X + bestLoc.X,
            safeRoi.Y + bestLoc.Y,
            bestW,
            bestH);

        if (bestScore < threshold)
            return new DetectionResult(false, bounds, bestScore, null);
        return new DetectionResult(true, bounds, bestScore, null);
    }

}
