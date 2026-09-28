using System.Diagnostics;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    private bool IsAbyss => string.Equals(new DirectoryInfo(_baseDir).Name, "abyss", StringComparison.OrdinalIgnoreCase);
    private OcrRecognizer? _abyssResultOcr;
    // 800x1000 client coordinates. Restrict result retry OCR to the middle bottom button only.
    private static readonly Rectangle AbyssRetrySafeRoi = new(280, 900, 240, 95);
    private static readonly Rectangle AbyssExitButtonRoi = new(160, 905, 180, 85);
    private static readonly Rectangle AbyssRetryButtonRoi = new(310, 905, 180, 85);
    private static readonly Rectangle AbyssOtherButtonRoi = new(460, 905, 180, 85);
    private string _lastAbyssResultDiagnostic = "no-frame";

    // V0168_ABYSS_LOOT_TRACKING
    private OcrRecognizer? _abyssLootOcr;
    private bool _abyssLootCountedForCurrentResult;
    private static readonly Rectangle AbyssLootCanonicalRoi = new(20, 80, 760, 800);

    // V0173_ABYSS_LOOT_IMAGE_MATCH
    // User-provided item icons are the primary authority. OCR remains only as fallback.
    private static readonly (string TargetId, string LootKey)[] AbyssLootTemplateTargets =
    {
        ("abyss_loot_hallucination_stone", FishingAutomation.LootStats.HallucinationStone),
        ("abyss_loot_devouring_stone", FishingAutomation.LootStats.DevouringStone),
        ("abyss_loot_abyss_stone", FishingAutomation.LootStats.AbyssStone),
        ("abyss_loot_rune_engraving_10", FishingAutomation.LootStats.RuneEngraving10),
        ("abyss_loot_rune_engraving_10_plus", FishingAutomation.LootStats.RuneEngraving10Plus),
        ("abyss_loot_rune_binding_10", FishingAutomation.LootStats.RuneBinding10),
        ("abyss_loot_rune_binding_10_plus", FishingAutomation.LootStats.RuneBinding10Plus),
        ("abyss_loot_mor_corsair_coat", FishingAutomation.LootStats.MorCorsairCoat),
        ("abyss_loot_mor_corsair_gloves", FishingAutomation.LootStats.MorCorsairGloves),
        ("abyss_loot_mor_corsair_boots", FishingAutomation.LootStats.MorCorsairBoots),
        ("abyss_loot_mor_corsair_tricorne", FishingAutomation.LootStats.MorCorsairTricorne),
    };

    private static Rectangle ScaleAbyssResultRoi(Rectangle canonical, Size frameSize)
    {
        if (frameSize.Width <= 0 || frameSize.Height <= 0)
            return Rectangle.Empty;

        double sx = frameSize.Width / 800.0;
        double sy = frameSize.Height / 1000.0;
        var scaled = new Rectangle(
            (int)Math.Round(canonical.X * sx),
            (int)Math.Round(canonical.Y * sy),
            Math.Max(1, (int)Math.Round(canonical.Width * sx)),
            Math.Max(1, (int)Math.Round(canonical.Height * sy)));

        return Rectangle.Intersect(new Rectangle(Point.Empty, frameSize), scaled);
    }

    private static double GetGreenButtonRatio(Bitmap frame, Rectangle roi)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
            return 0;

        int green = 0;
        int samples = 0;
        for (int y = roi.Top; y < roi.Bottom; y += 2)
        {
            for (int x = roi.Left; x < roi.Right; x += 2)
            {
                Color c = frame.GetPixel(x, y);
                samples++;

                // Allow both green and slightly cyan-tinted game buttons.
                if (c.G >= 70 && c.G >= c.R + 12 && c.G + 18 >= c.B)
                    green++;
            }
        }

        return samples == 0 ? 0 : green / (double)samples;
    }

    // V0161_ABYSS_RESULT_ASPECT_092: real client captures can be ~815x920 (aspect ~0.886).
    private bool TryDetectAbyssResultButtonRow(Bitmap frame, out Rectangle retryRoi)
    {
        retryRoi = Rectangle.Empty;

        double aspect = frame.Height == 0 ? 0 : frame.Width / (double)frame.Height;
        if (aspect < 0.74 || aspect > 0.92)
        {
            _lastAbyssResultDiagnostic = $"frame={frame.Width}x{frame.Height} aspect={aspect:0.000} outside-safe-range";
            return false;
        }

        var exit = ScaleAbyssResultRoi(AbyssExitButtonRoi, frame.Size);
        var retry = ScaleAbyssResultRoi(AbyssRetryButtonRoi, frame.Size);
        var other = ScaleAbyssResultRoi(AbyssOtherButtonRoi, frame.Size);

        double exitGreen = GetGreenButtonRatio(frame, exit);
        double retryGreen = GetGreenButtonRatio(frame, retry);
        double otherGreen = GetGreenButtonRatio(frame, other);

        _lastAbyssResultDiagnostic =
            $"frame={frame.Width}x{frame.Height} green(exit/retry/other)=" +
            $"{exitGreen:0.000}/{retryGreen:0.000}/{otherGreen:0.000}";

        // User result captures are around 0.48~0.52. Keep a comfortable margin for
        // compression/gamma differences while still requiring all three button regions.
        if (exitGreen < 0.18 || retryGreen < 0.18 || otherGreen < 0.18)
            return false;

        retryRoi = retry;
        return true;
    }
    private int AbyssCombatStepIndex
    {
        get
        {
            int index = _scenario.Steps.FindIndex(s => s.Target == "abyss_touch_screen");
            if (index < 0) throw new InvalidOperationException("어비스 전투 대기 단계가 없습니다.");
            return index;
        }
    }

    // Independent of normal-dungeon selected/challenge/retry targets and their ROIs.
    // Result screens are allowed to continue only when the exact middle-bottom retry label
    // is found inside its fixed safe ROI on the canonical 800x1000 client.
    private async Task<DetectionResult> DetectAbyssResultRetryAsync(Bitmap frame, CancellationToken ct)
    {
        if (!IsAbyss) return DetectionResult.NotFound;

        double aspect = frame.Height == 0 ? 0 : frame.Width / (double)frame.Height;
        if (aspect < 0.74 || aspect > 0.92)
        {
            _lastAbyssResultDiagnostic = $"frame={frame.Width}x{frame.Height} aspect={aspect:0.000} outside-safe-range";
            return DetectionResult.NotFound;
        }

        // V0160_ABYSS_RESULT_MULTI_SIGNAL
        // Signal A: exact retry OCR inside the fixed safe ROI.
        _abyssResultOcr ??= new OcrRecognizer();
        var safeRetry = ScaleAbyssResultRoi(AbyssRetrySafeRoi, frame.Size);
        var retryOcr = await _abyssResultOcr.FindCompactLabelAsync(frame, safeRetry, "다시 하기", ct);
        bool ocrSignal = retryOcr.Found && safeRetry.Contains(retryOcr.Center);
        DiagnosticObserveDetection("abyss_result_retry_ocr", retryOcr);

        // Signal B/C/D: the production visual-row detector requires all three
        // canonical green result-button regions (exit/retry/other). OCR alone
        // is deliberately not authority for ResultConfirmed.
        bool visualSignal = TryDetectAbyssResultButtonRow(frame, out var visualRetry);
        if (!visualSignal)
        {
            _lastAbyssResultDiagnostic =
                $"frame={frame.Width}x{frame.Height} result-multisignal rejected " +
                $"ocr={(ocrSignal ? 1 : 0)} visualRow=0; {_lastAbyssResultDiagnostic}";
            DiagnosticObserveDetection("abyss_result_visual_row", DetectionResult.NotFound);
            return DetectionResult.NotFound;
        }

        var acceptedBounds = visualRetry;
        string source = ocrSignal
            ? "ocr+visual_result_button_row"
            : "visual_result_button_row_3signal";

        // If OCR also agrees, retain its exact text bounds only when its center
        // is inside the independently confirmed retry-button region.
        if (ocrSignal && visualRetry.Contains(retryOcr.Center))
            acceptedBounds = retryOcr.Bounds;

        var accepted = new DetectionResult(true, acceptedBounds, 0.95, source);
        _lastAbyssResultDiagnostic =
            $"frame={frame.Width}x{frame.Height} result-multisignal accepted " +
            $"ocr={(ocrSignal ? 1 : 0)} visualRow=1 retry={acceptedBounds}";
        DiagnosticObserveDetection("abyss_result_visual_row", accepted);
        return accepted;
    }

    private static bool AbyssLootTextLooksPlus(Bitmap frame, Rectangle textBounds, string rawText)
    {
        if (rawText.Contains('+'))
            return true;

        Rectangle bounds = Rectangle.Intersect(
            new Rectangle(Point.Empty, frame.Size),
            Rectangle.Inflate(textBounds, 4, 3));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        int blue = 0;
        int green = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y += 2)
        {
            for (int x = bounds.Left; x < bounds.Right; x += 2)
            {
                Color c = frame.GetPixel(x, y);
                if (c.B >= 125 && c.B >= c.G + 18 && c.B >= c.R + 28)
                    blue++;
                if (c.G >= 115 && c.G >= c.B + 12 && c.G >= c.R + 22)
                    green++;
            }
        }

        return blue >= 4 && blue > green * 1.15;
    }

    private static void ClassifyAbyssLootLine(
        Bitmap frame,
        DetectionResult line,
        HashSet<string> found)
    {
        string raw = line.ReadText ?? "";
        string norm = FuzzyText.Normalize(raw);
        if (string.IsNullOrEmpty(norm))
            return;

        if (norm.Contains(FuzzyText.Normalize("허상의 마력석"), StringComparison.OrdinalIgnoreCase))
            found.Add(FishingAutomation.LootStats.HallucinationStone);
        if (norm.Contains(FuzzyText.Normalize("포식의 마력석"), StringComparison.OrdinalIgnoreCase))
            found.Add(FishingAutomation.LootStats.DevouringStone);
        if (norm.Contains(FuzzyText.Normalize("심해의 마력석"), StringComparison.OrdinalIgnoreCase))
            found.Add(FishingAutomation.LootStats.AbyssStone);

        if (norm.Contains(FuzzyText.Normalize("룬새김 장식"), StringComparison.OrdinalIgnoreCase))
        {
            found.Add(AbyssLootTextLooksPlus(frame, line.Bounds, raw)
                ? FishingAutomation.LootStats.RuneEngraving10Plus
                : FishingAutomation.LootStats.RuneEngraving10);
        }

        if (norm.Contains(FuzzyText.Normalize("룬결속 장식"), StringComparison.OrdinalIgnoreCase))
        {
            found.Add(AbyssLootTextLooksPlus(frame, line.Bounds, raw)
                ? FishingAutomation.LootStats.RuneBinding10Plus
                : FishingAutomation.LootStats.RuneBinding10);
        }

        if (norm.Contains(FuzzyText.Normalize("모르 코르셰어 코트"), StringComparison.OrdinalIgnoreCase))
            found.Add(FishingAutomation.LootStats.MorCorsairCoat);
        if (norm.Contains(FuzzyText.Normalize("모르 코르셰어 글러브"), StringComparison.OrdinalIgnoreCase))
            found.Add(FishingAutomation.LootStats.MorCorsairGloves);
        if (norm.Contains(FuzzyText.Normalize("모르 코르셰어 부츠"), StringComparison.OrdinalIgnoreCase))
            found.Add(FishingAutomation.LootStats.MorCorsairBoots);
        if (norm.Contains(FuzzyText.Normalize("모르 코르셰어 트리코른"), StringComparison.OrdinalIgnoreCase))
            found.Add(FishingAutomation.LootStats.MorCorsairTricorne);
    }

    private async Task<Dictionary<string, DetectionResult>> DetectAbyssLootTemplateFrameAsync(
        Bitmap frame,
        CancellationToken ct)
    {
        var hits = new Dictionary<string, DetectionResult>(StringComparer.Ordinal);
        foreach (var item in AbyssLootTemplateTargets)
        {
            ct.ThrowIfCancellationRequested();
            var hit = await _detector.DetectAsync(item.TargetId, frame, ct);
            hits[item.LootKey] = hit;
        }
        return hits;
    }

    private async Task<HashSet<string>> DetectAbyssLootAsync(Bitmap frame, CancellationToken ct)
    {
        // V0174_ABYSS_LOOT_SINGLE_WINNER
        // The tracked special loot is awarded one item at a time. Require the same
        // template in two consecutive frames, then count only the strongest candidate.
        var first = await DetectAbyssLootTemplateFrameAsync(frame, ct);
        await Task.Delay(180, ct);
        using var secondFrame = await CaptureGameWindowAsync(ct);
        var second = await DetectAbyssLootTemplateFrameAsync(secondFrame, ct);

        var stable = new List<(string Key, double Score, double First, double Second)>();
        foreach (var item in AbyssLootTemplateTargets)
        {
            if (first.TryGetValue(item.LootKey, out var a) &&
                second.TryGetValue(item.LootKey, out var b) &&
                a.Found && b.Found)
            {
                stable.Add((
                    item.LootKey,
                    Math.Min(a.Score, b.Score),
                    a.Score,
                    b.Score));
            }
        }

        string hallucinationKey = FishingAutomation.LootStats.HallucinationStone;
        bool hallucinationChecked = first[hallucinationKey].Found || second[hallucinationKey].Found;
        bool hallucinationConfirmed = hallucinationChecked && await ConfirmHallucinationLootAsync(
            frame, secondFrame, first, second, ct);
        if (!hallucinationConfirmed)
            stable.RemoveAll(x => x.Key == hallucinationKey);

        if (stable.Count > 0)
        {
            var winner = stable
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .First();

            Log?.Invoke(
                $"[어비스 전리품 이미지] 최종 1개 확정: " +
                $"{FishingAutomation.LootStats.GetDisplayName(winner.Key)} " +
                $"score={winner.First:0.000}/{winner.Second:0.000}");

            if (stable.Count > 1)
            {
                string suppressed = string.Join(
                    ", ",
                    stable
                        .Where(x => !string.Equals(x.Key, winner.Key, StringComparison.Ordinal))
                        .OrderByDescending(x => x.Score)
                        .Select(x =>
                            $"{FishingAutomation.LootStats.GetDisplayName(x.Key)}={x.Score:0.000}"));
                Log?.Invoke(
                    $"[어비스 전리품 이미지] 동시 교차매칭 {stable.Count - 1}개 제외: {suppressed}");
            }

            return new HashSet<string>(StringComparer.Ordinal) { winner.Key };
        }

        // Image matching stays primary. OCR is only a fallback.
        var best = second
            .OrderByDescending(kv => kv.Value.Score)
            .Take(3)
            .Select(kv =>
                $"{FishingAutomation.LootStats.GetDisplayName(kv.Key)}={kv.Value.Score:0.000}");
        Log?.Invoke(
            $"[어비스 전리품 이미지] 2프레임 확정 없음 · 상위 점수: " +
            $"{string.Join(", ", best)} -> OCR 보조 확인");

        _abyssLootOcr ??= new OcrRecognizer();
        var roi = ScaleAbyssResultRoi(AbyssLootCanonicalRoi, secondFrame.Size);
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (int scale in new[] { 1, 2 })
        {
            var lines = await _abyssLootOcr.ReadLinesAsync(secondFrame, roi, scale, ct);
            foreach (var line in lines)
                ClassifyAbyssLootLine(secondFrame, line, found);
        }

        // OCR-only matches must never bypass the protected item's icon+name gate.
        if (found.Contains(hallucinationKey) && !hallucinationChecked)
            hallucinationConfirmed = await ConfirmHallucinationLootAsync(frame, secondFrame, first, second, ct);
        if (!hallucinationConfirmed) found.Remove(hallucinationKey);

        if (found.Count == 0)
            return found;

        // OCR can also produce several fuzzy hits from the same visual row.
        // Honor the one-loot-per-round rule and use template similarity only as a
        // tie-breaker among OCR-recognized keys.
        string ocrWinner = found
            .OrderByDescending(key =>
                second.TryGetValue(key, out var hit) ? hit.Score : 0.0)
            .ThenBy(key => key, StringComparer.Ordinal)
            .First();

        if (found.Count > 1)
        {
            Log?.Invoke(
                $"[어비스 전리품 OCR 보조] 복수 후보 {found.Count}개 -> " +
                $"가장 강한 1개만 인정: {FishingAutomation.LootStats.GetDisplayName(ocrWinner)}");
        }
        else
        {
            Log?.Invoke(
                $"[어비스 전리품 OCR 보조] " +
                $"{FishingAutomation.LootStats.GetDisplayName(ocrWinner)}");
        }

        return new HashSet<string>(StringComparer.Ordinal) { ocrWinner };
    }

    private async Task RetryAbyssResultAsync(CancellationToken ct)
    {
        if (!IsAbyss) throw new InvalidOperationException("어비스 결과 전용 처리입니다.");
        var timer = Stopwatch.StartNew();
        DetectionResult previous = DetectionResult.NotFound;
        while (timer.Elapsed < TimeSpan.FromSeconds(120))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var retry = await DetectAbyssResultRetryAsync(frame, ct);
            if (retry.Found && previous.Found && retry.Bounds.IntersectsWith(previous.Bounds))
            {
                // Two consecutive production detections are the authority for ResultConfirmed.
                AbyssTransitionTo(AbyssFlowState.ResultConfirmed, "다시 하기 결과 화면 2회 연속 확인");
                AbyssRequireState(AbyssFlowState.ResultConfirmed, "다시 하기 클릭");

                ct.ThrowIfCancellationRequested();

                if (!_abyssLootCountedForCurrentResult)
                {
                    try
                    {
                        var foundLoot = await DetectAbyssLootAsync(frame, ct);
                        FishingAutomation.LootStats.RecordRound(foundLoot);
                        _abyssLootCountedForCurrentResult = true;

                        if (foundLoot.Count == 0)
                        {
                            Log?.Invoke("[어비스 전리품] 추적 대상 없음 -> 카운트 +0");
                        }
                        else
                        {
                            string names = string.Join(", ", foundLoot.Select(FishingAutomation.LootStats.GetDisplayName));
                            Log?.Invoke($"[어비스 전리품] 이번 판 획득 카운트: {names}");
                        }
                    }
                    catch (RestartCycleException) { throw; }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Loot statistics must never block the existing retry flow.
                        _abyssLootCountedForCurrentResult = true;
                        Log?.Invoke($"[어비스 전리품] 인식/저장 실패 -> 다시 하기 흐름 계속: {ex.Message}");
                    }
                }

                Log?.Invoke("[어비스] 다시 하기 인식 -> 1초 대기 후 버튼 재확인");
                await RetryClickDelay.WaitAsync(ct);

                // OCR/template counting can take seconds. Revalidate before sending input.
                using var fresh = await CaptureGameWindowAsync(ct);
                var freshRetry = await DetectAbyssResultRetryAsync(fresh, ct);
                if (!freshRetry.Found || !freshRetry.Bounds.IntersectsWith(retry.Bounds))
                { previous = DetectionResult.NotFound; continue; }
                retry = freshRetry;
                _hwnd = await ResolveRequiredGameWindowAsync(ct);
                NativeMethods.SetForegroundWindow(_hwnd);
                Log?.Invoke($"[어비스] 아래 중앙 다시 하기 2회 연속 확인 -> 다시 하기만 클릭 @ {retry.Center} source={retry.ReadText} safe={AbyssRetrySafeRoi}");
                DiagnosticObserveClick("abyss_result_retry", retry.Center);
                _input.ClickClientPoint(_hwnd, retry.Center);
                AbyssTransitionTo(AbyssFlowState.RetryClicked, "가운데 다시 하기 클릭 완료");
                await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);
                AbyssTransitionTo(AbyssFlowState.Reentering, "다시 하기 클릭 후 결과 화면 이탈 대기");
                await WaitForAbyssRetryTransitionAsync(ct);
                return;
            }
            previous = retry;
            // No monitors, alternate clicks or entry recovery while awaiting results.
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }
        DiagnosticPersistFailure("abyss_result_retry_timeout");
        throw new InvalidOperationException(
            $"어비스 결과의 아래 중앙 다시 하기를 확인하지 못해 정지합니다. " +
            $"나가기/다른 던전 가기로 우회하지 않습니다. 상태={AbyssFlowStateText()}; 진단: {_lastAbyssResultDiagnostic}; {DiagnosticSummary()}");
    }

    private async Task WaitForAbyssRetryTransitionAsync(CancellationToken ct)
    {
        AbyssRequireState(AbyssFlowState.Reentering, "다시 하기 후 재입장 전환 확인");

        var timer = Stopwatch.StartNew();
        int gone = 0;
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            // Check the retry label itself so a single missing companion label cannot authorize transition.
            var retry = await DetectAbyssResultRetryAsync(frame, ct);
            if (retry.Found) { gone = 0; }
            else
            {
                var enter = await _detector.DetectAsync("abyss_enter", frame, ct);
                if (enter.Found || await DetectAbyssOutsideWorkflowAsync(frame, ct))
                    throw new InvalidOperationException("어비스 다시 하기 후 예상과 다른 입장/필드 화면입니다. 추가 입력 없이 정지합니다.");
                if (!await HasAbyssCombatEvidenceAsync(frame, ct)) { gone = 0; await Task.Delay(300, ct); continue; }
                if (++gone >= 3)
                {
                    // Preserve the existing V0.1.58 transition rule exactly:
                    // retry screen absent for 3 frames and no entry/outside screen.
                    AbyssTransitionTo(
                        AbyssFlowState.CombatClearWait,
                        "결과 화면 3회 연속 이탈 + 입장/필드 화면 아님");
                    _abyssLootCountedForCurrentResult = false;
                    Log?.Invoke("[어비스] 다시 하기 결과 화면 이탈 확인 -> 선택 화면 없이 전투 대기로 복귀");
                    return;
                }
            }
            await Task.Delay(Math.Max(300, _settings.PollIntervalMs), ct);
        }
        throw new InvalidOperationException("어비스 다시 하기 후 화면 전환을 확인하지 못했습니다. 중복 클릭 없이 정지합니다.");
    }
}
