using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DungeonVisionBot;

namespace FishingAutomation;

// VISUAL_TEST_ENV_V10
// Offline recognition regression test. It never resolves a game window and never invokes
// Interception/input APIs. Real stored screenshot pixels are rendered into an 800x1000
// synthetic client frame and passed to the same OCR/OpenCV detector used by the live bot.
public sealed class VisualTestOutcome
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Status { get; init; } = "FAIL";
    public string Method { get; init; } = "";
    public string Detail { get; init; } = "";
    public double Score { get; init; }
    public Rectangle Bounds { get; init; }
    public byte[]? FailurePng { get; init; }
}

public sealed class VisualTestRunReport
{
    public DateTimeOffset Time { get; init; } = DateTimeOffset.Now;
    public string Version { get; init; } = UpdateManager.CurrentVersion;
    public List<VisualTestOutcome> Outcomes { get; init; } = new();
    public int Passed => Outcomes.Count(x => x.Status == "PASS");
    public int Failed => Outcomes.Count(x => x.Status == "FAIL");
    public int Skipped => Outcomes.Count(x => x.Status == "SKIP");
}

public sealed class VisualRecognitionTester
{
    private readonly string _baseDir;
    private readonly string _dungeonDir;
    private readonly string _assetDir;
    private readonly TargetDetector _detector;
    private readonly OcrRecognizer _ocr;

    public VisualRecognitionTester(string baseDir)
    {
        _baseDir = baseDir;
        _dungeonDir = Path.Combine(baseDir, "dungeon");
        _assetDir = Path.Combine(baseDir, "visual-tests", "assets");
        string targetPath = Path.Combine(_dungeonDir, "config", "targets.json");
        if (!File.Exists(targetPath))
            throw new FileNotFoundException("인식 테스트용 targets.json을 찾지 못했습니다.", targetPath);

        var targets = System.Text.Json.JsonSerializer.Deserialize<List<TargetDefinition>>(
            File.ReadAllText(targetPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("던전 targets.json을 읽지 못했습니다.");

        _detector = new TargetDetector(targets, _dungeonDir);
        _ocr = new OcrRecognizer();
    }

    public async Task<VisualTestRunReport> RunAllAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var report = new VisualTestRunReport();
        progress?.Report("[인식 테스트] 실제 게임 입력 없음 · 저장 스크린샷만 사용");

        await AddTarget(report, progress, "peaca-map", "월드맵 · 페카 고분 OCR",
            "peaca_map_actual_v0136.jpg", new Rectangle(315, 135, 115, 75), "route_peaca_map", ct);

        await AddDirectOcr(report, progress, "fiod-map", "월드맵 · 피오드 던전 OCR",
            "fiod_label.jpg", new Rectangle(180, 260, 220, 180),
            new Rectangle(20, 110, 660, 650), "피오드 던전", 2, ct);

        await AddTarget(report, progress, "peaca-popup", "페카 팝업 · 페카 고분",
            "peaca_popup_title.jpg", new Rectangle(100, 520, 600, 260),
            "route_peaca_popup_title", ct);

        await AddTarget(report, progress, "go-here", "페카 팝업 · 여기로 가기",
            "go_here.jpg", new Rectangle(90, 805, 620, 178),
            "route_go_here", ct);

        await AddTarget(report, progress, "d1-1", "심층 던전 · D1-1 실제 템플릿",
            "slot_peaca_d1_1_v0139.jpg", new Rectangle(295, 490, 45, 55),
            "route_d1_1", ct);

        await AddTarget(report, progress, "d2-1", "심층 던전 · D2-1 실제 템플릿",
            "slot_peaca_d2_1_v0139.jpg", new Rectangle(295, 800, 45, 50),
            "route_d2_1", ct);

        await AddTarget(report, progress, "regular-1-1", "룬다/피오드 · 1-1 실제 템플릿",
            "slot_regular_1_1_v0139.jpg", new Rectangle(298, 570, 45, 50),
            "route_regular_1_1", ct);

        await AddTarget(report, progress, "regular-2-1", "룬다/피오드 · 2-1 실제 템플릿",
            "slot_regular_2_1_v0139.jpg", new Rectangle(338, 790, 45, 55),
            "route_regular_2_1", ct);

        await AddTarget(report, progress, "d2-enter", "심층 던전 · 2층 1구역 진입",
            "d2_enter.jpg", new Rectangle(85, 850, 630, 129),
            "route_enter_d2_1", ct);

        await AddTarget(report, progress, "retry", "결과 화면 · 다시 하기 하이브리드",
            "retry_candidate.jpg", new Rectangle(235, 835, 300, 155),
            "retry", ct);

        await AddDirectOcr(report, progress, "runda-map", "월드맵 · 룬다 던전 OCR",
            "runda_map_actual_v0136.jpg", new Rectangle(235, 685, 130, 75),
            new Rectangle(200, 650, 240, 150), "룬다 던전", 2, ct);

        progress?.Report($"[인식 테스트] 완료 · PASS {report.Passed} / FAIL {report.Failed} / SKIP {report.Skipped}");
        return report;
    }

    private async Task AddTarget(
        VisualTestRunReport report,
        IProgress<string>? progress,
        string id,
        string label,
        string asset,
        Rectangle destination,
        string targetId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var frame = BuildFrame(asset, destination);
        DetectionResult result;
        try
        {
            result = await _detector.DetectAsync(targetId, frame, ct);
        }
        catch (Exception ex)
        {
            var error = Fail(id, label, _detector.Get(targetId).Kind, "검출기 예외: " + ex.Message, frame);
            report.Outcomes.Add(error);
            progress?.Report(Format(error));
            return;
        }

        string method = _detector.Get(targetId).Kind;
        var outcome = result.Found
            ? new VisualTestOutcome
            {
                Id = id, Label = label, Status = "PASS", Method = method,
                Detail = string.IsNullOrWhiteSpace(result.ReadText)
                    ? $"검출 성공 · score={result.Score:0.000}"
                    : $"검출 성공 · OCR=\"{result.ReadText}\"",
                Score = result.Score, Bounds = result.Bounds
            }
            : Fail(id, label, method,
                $"검출 실패 · best score={result.Score:0.000} · target={targetId}", frame, result.Score, result.Bounds);

        report.Outcomes.Add(outcome);
        progress?.Report(Format(outcome));
    }

    private async Task AddDirectOcr(
        VisualTestRunReport report,
        IProgress<string>? progress,
        string id,
        string label,
        string asset,
        Rectangle destination,
        Rectangle roi,
        string wanted,
        int editDistance,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var frame = BuildFrame(asset, destination);
        DetectionResult result;
        try
        {
            result = await _ocr.FindTextAsync(frame, roi, wanted, editDistance, true, ct);
        }
        catch (Exception ex)
        {
            var error = Fail(id, label, "ocr", "OCR 예외: " + ex.Message, frame);
            report.Outcomes.Add(error);
            progress?.Report(Format(error));
            return;
        }

        var outcome = result.Found
            ? new VisualTestOutcome
            {
                Id = id, Label = label, Status = "PASS", Method = "ocr",
                Detail = $"검출 성공 · OCR=\"{result.ReadText}\"",
                Score = result.Score, Bounds = result.Bounds
            }
            : Fail(id, label, "ocr", $"OCR 실패 · wanted=\"{wanted}\"", frame, result.Score, result.Bounds);

        report.Outcomes.Add(outcome);
        progress?.Report(Format(outcome));
    }

    private Bitmap BuildFrame(string assetName, Rectangle destination)
    {
        string path = Path.Combine(_assetDir, assetName);
        if (!File.Exists(path))
            throw new FileNotFoundException("인식 테스트 이미지를 찾지 못했습니다.", path);

        using var src = new Bitmap(path);
        var frame = new Bitmap(800, 1000, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(frame);
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, destination);
        return frame;
    }

    private static VisualTestOutcome Fail(
        string id,
        string label,
        string method,
        string detail,
        Bitmap frame,
        double score = 0,
        Rectangle bounds = default)
    {
        using var ms = new MemoryStream();
        frame.Save(ms, ImageFormat.Png);
        return new VisualTestOutcome
        {
            Id = id, Label = label, Status = "FAIL", Method = method,
            Detail = detail, Score = score, Bounds = bounds, FailurePng = ms.ToArray()
        };
    }

    private static string Format(VisualTestOutcome x)
        => $"[{x.Status}] {x.Label} · {x.Detail}";
}
