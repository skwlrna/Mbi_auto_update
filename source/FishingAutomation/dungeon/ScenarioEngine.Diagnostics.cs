using System.Drawing;
using System.Drawing.Imaging;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private readonly object _diagnosticLock = new();
    private readonly Queue<Bitmap> _diagnosticFrames = new();
    private long _diagnosticLastFrameSampleTick;
    private string _diagnosticStage = "init";
    private string _diagnosticPreviousStage = "init";
    private string _diagnosticLastDetection = "none";
    private string _diagnosticLastOcr = "none";
    private string _diagnosticLastScore = "n/a";
    private string _diagnosticLastBounds = "n/a";
    private string _diagnosticLastClick = "none";
    private string _diagnosticLastTransition = "init";
    private string _diagnosticFrameSize = "unknown";

    private void DiagnosticSetStage(string target, string name)
    {
        if (!IsAbyss) return;

        string next = $"{target} | {name}";
        string? transition = null;
        lock (_diagnosticLock)
        {
            if (!string.Equals(_diagnosticStage, next, StringComparison.Ordinal))
            {
                _diagnosticPreviousStage = _diagnosticStage;
                _diagnosticStage = next;
                _diagnosticLastTransition = $"{_diagnosticPreviousStage} -> {_diagnosticStage}";
                transition = _diagnosticLastTransition;
            }
        }

        if (transition is not null)
            Log?.Invoke($"[진단] 어비스 상태 전환: {transition}");
    }

    private void DiagnosticObserveFrame(Bitmap frame)
    {
        if (!IsAbyss) return;

        long now = Environment.TickCount64;
        lock (_diagnosticLock)
        {
            _diagnosticFrameSize = $"{frame.Width}x{frame.Height}";

            // Keep five recent sampled frames in memory. Sampling prevents diagnostics
            // from changing the normal detector/click timing materially.
            if (_diagnosticLastFrameSampleTick != 0 &&
                now - _diagnosticLastFrameSampleTick < 500)
                return;

            _diagnosticLastFrameSampleTick = now;
            Bitmap copy = (Bitmap)frame.Clone();
            _diagnosticFrames.Enqueue(copy);
            while (_diagnosticFrames.Count > 5)
            {
                Bitmap old = _diagnosticFrames.Dequeue();
                old.Dispose();
            }
        }
    }

    private void DiagnosticObserveDetection(string id, DetectionResult result)
    {
        if (!IsAbyss) return;

        lock (_diagnosticLock)
        {
            _diagnosticLastDetection = $"{id}: found={(result.Found ? 1 : 0)}";
            _diagnosticLastOcr = string.IsNullOrWhiteSpace(result.ReadText) ? "none" : result.ReadText!;
            _diagnosticLastScore = result.Score.ToString("0.000");
            _diagnosticLastBounds = result.Bounds.ToString();
        }
    }

    private void DiagnosticObserveClick(string reason, Point point)
    {
        if (!IsAbyss) return;

        lock (_diagnosticLock)
            _diagnosticLastClick = $"{reason} @ {point.X},{point.Y}";

        Log?.Invoke($"[진단] 최근 어비스 클릭: {_diagnosticLastClick}");
    }

    private string DiagnosticSummary()
    {
        lock (_diagnosticLock)
        {
            return
                $"stage={_diagnosticStage}; " +
                $"transition={_diagnosticLastTransition}; " +
                $"frame={_diagnosticFrameSize}; " +
                $"detect={_diagnosticLastDetection}; " +
                $"ocr={_diagnosticLastOcr}; " +
                $"score={_diagnosticLastScore}; " +
                $"bounds={_diagnosticLastBounds}; " +
                $"click={_diagnosticLastClick}";
        }
    }

    private void DiagnosticPersistFailure(string label)
    {
        if (!IsAbyss) return;

        try
        {
            string dir = Path.Combine(_baseDir, "debug");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string safeLabel = Safe(label);

            List<Bitmap> snapshots;
            string summary;
            lock (_diagnosticLock)
            {
                snapshots = _diagnosticFrames.Select(f => (Bitmap)f.Clone()).ToList();
                summary = DiagnosticSummaryUnsafe();
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                using Bitmap shot = snapshots[i];
                string path = Path.Combine(dir, $"diag_{stamp}_{safeLabel}_before_{snapshots.Count - i}.png");
                shot.Save(path, ImageFormat.Png);
            }

            File.WriteAllText(
                Path.Combine(dir, $"diag_{stamp}_{safeLabel}.txt"),
                summary,
                System.Text.Encoding.UTF8);

            PruneDebugScreenshots();
            Log?.Invoke($"[진단] 실패 직전 프레임 {snapshots.Count}장 저장 · {summary}");
        }
        catch (Exception ex)
        {
            // Diagnostics must never change automation behavior.
            Log?.Invoke($"[진단] 실패 정보 저장 중 예외(동작에는 영향 없음): {ex.Message}");
        }
    }

    private string DiagnosticSummaryUnsafe()
    {
        return
            $"stage={_diagnosticStage}; " +
            $"transition={_diagnosticLastTransition}; " +
            $"frame={_diagnosticFrameSize}; " +
            $"detect={_diagnosticLastDetection}; " +
            $"ocr={_diagnosticLastOcr}; " +
            $"score={_diagnosticLastScore}; " +
            $"bounds={_diagnosticLastBounds}; " +
            $"click={_diagnosticLastClick}";
    }
}
