using System.Drawing.Imaging;
using System.Text;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private async Task<bool> ConfirmHallucinationLootAsync(
        Bitmap firstFrame, Bitmap secondFrame,
        Dictionary<string, DetectionResult> first, Dictionary<string, DetectionResult> second,
        CancellationToken ct)
    {
        var firstLines = new List<DetectionResult>();
        var secondLines = new List<DetectionResult>();
        bool accepted = false;
        string reason = "아이콘/정확한 이름 2프레임 일치 미확인 -> 집계 보류";
        try
        {
            _abyssLootOcr ??= new OcrRecognizer();
            foreach (int scale in new[] { 1, 2 })
            {
                firstLines.AddRange(await _abyssLootOcr.ReadLinesAsync(firstFrame,
                    ScaleAbyssResultRoi(AbyssLootCanonicalRoi, firstFrame.Size), scale, ct));
                secondLines.AddRange(await _abyssLootOcr.ReadLinesAsync(secondFrame,
                    ScaleAbyssResultRoi(AbyssLootCanonicalRoi, secondFrame.Size), scale, ct));
            }
            string key = FishingAutomation.LootStats.HallucinationStone;
            accepted = HallucinationLootPolicy.Confirm(first[key], second[key], firstLines, secondLines,
                firstFrame.Size, secondFrame.Size);
            if (accepted) reason = "아이콘 점수/위치 + 정확한 이름 2프레임 확인 -> 집계 후보 인정";
            ct.ThrowIfCancellationRequested();
            return accepted;
        }
        catch (Exception ex)
        {
            accepted = false;
            reason = "검증 중단 -> 집계 안 함: " + ex.GetType().Name;
            throw;
        }
        finally
        {
            SaveHallucinationEvidence(firstFrame, secondFrame, first, second,
                firstLines, secondLines, accepted, reason);
            Log?.Invoke($"[허상의 마력석 검증] {reason}");
        }
    }

    private void SaveHallucinationEvidence(Bitmap firstFrame, Bitmap secondFrame,
        Dictionary<string, DetectionResult> first, Dictionary<string, DetectionResult> second,
        IReadOnlyList<DetectionResult> firstLines, IReadOnlyList<DetectionResult> secondLines,
        bool accepted, string reason)
    {
        try
        {
            string dir = Path.Combine(_baseDir, "debug", "loot_hallucination");
            Directory.CreateDirectory(dir);
            string prefix = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..8]);
            firstFrame.Save(prefix + "_1.png", ImageFormat.Png);
            secondFrame.Save(prefix + "_2.png", ImageFormat.Png);
            var report = new StringBuilder();
            report.AppendLine($"version={FishingAutomation.UpdateManager.CurrentVersion}; cycle={_cycle}; candidateAccepted={accepted}");
            report.AppendLine(reason);
            report.AppendLine($"iconMinScore={HallucinationLootPolicy.MinimumIconScore}; firstSize={firstFrame.Size}; secondSize={secondFrame.Size}");
            foreach (var hit in first)
                report.AppendLine($"frame1 {hit.Key}: found={hit.Value.Found}; score={hit.Value.Score:F4}; bounds={hit.Value.Bounds}");
            foreach (var hit in second)
                report.AppendLine($"frame2 {hit.Key}: found={hit.Value.Found}; score={hit.Value.Score:F4}; bounds={hit.Value.Bounds}");
            foreach (var line in firstLines) report.AppendLine($"OCR1 {line.Bounds}: {line.ReadText}");
            foreach (var line in secondLines) report.AppendLine($"OCR2 {line.Bounds}: {line.ReadText}");
            File.WriteAllText(prefix + ".txt", report.ToString(), Encoding.UTF8);
            Log?.Invoke($"[허상의 마력석 진단] 판정 화면/점수/OCR 저장: {prefix}.txt");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[허상의 마력석 진단] 저장 실패: {ex.Message}");
        }
        finally
        {
            // Bound both image and text growth, including any partially written evidence.
            try
            {
                var files = new DirectoryInfo(Path.Combine(_baseDir, "debug", "loot_hallucination"))
                    .GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
                long total = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    total += files[i].Length;
                    if (i >= 150 || total > 100L * 1024 * 1024) files[i].Delete();
                }
            }
            catch { }
        }
    }
}
