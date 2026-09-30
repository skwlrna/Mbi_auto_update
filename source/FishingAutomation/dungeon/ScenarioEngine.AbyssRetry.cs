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

    private async Task<IReadOnlyList<DetectionResult>> ReadAbyssLootLinesAsync(
        Bitmap frame,
        CancellationToken ct)
    {
        _abyssLootOcr ??= new OcrRecognizer();
        var roi = ScaleAbyssResultRoi(AbyssLootCanonicalRoi, frame.Size);
        var lines = new List<DetectionResult>();
        foreach (int scale in new[] { 1, 2 })
        {
            ct.ThrowIfCancellationRequested();
            lines.AddRange(await _abyssLootOcr.ReadLinesAsync(frame, roi, scale, ct));
        }
        return lines;
    }

    private static string FormatLootTopScores(
        IReadOnlyDictionary<string, DetectionResult> hits,
        int take = 4)
        => string.Join(
            ", ",
            hits.OrderByDescending(kv => double.IsFinite(kv.Value.Score) ? kv.Value.Score : double.NegativeInfinity)
                .Take(take)
                .Select(kv =>
                    $"{FishingAutomation.LootStats.GetDisplayName(kv.Key)}={kv.Value.Score:0.000}"));

    private async Task<HashSet<string>> DetectAbyssLootAsync(Bitmap frame, CancellationToken ct)
    {
        // V0180_ABYSS_LOOT_STRICT_PROOF
        // False positives are worse than a missed statistic. Count only when the same
        // item has strong/stable visual evidence in three frames, clearly beats all
        // competing tracked templates, and its exact name is next to that icon in
        // at least two frames. OCR alone is never allowed to create a loot count.
        var first = await DetectAbyssLootTemplateFrameAsync(frame, ct);

        await Task.Delay(180, ct);
        using var secondFrame = await CaptureGameWindowAsync(ct);
        var second = await DetectAbyssLootTemplateFrameAsync(secondFrame, ct);

        await Task.Delay(180, ct);
        using var thirdFrame = await CaptureGameWindowAsync(ct);
        var third = await DetectAbyssLootTemplateFrameAsync(thirdFrame, ct);

        var stable = new List<(string Key, double MinScore, double First, double Second, double Third)>();
        foreach (var item in AbyssLootTemplateTargets)
        {
            var a = first[item.LootKey];
            var b = second[item.LootKey];
            var d = third[item.LootKey];

            if (!StrictLootPolicy.IsStable(
                    a, b, d,
                    frame.Size, secondFrame.Size, thirdFrame.Size))
                continue;

            stable.Add((
                item.LootKey,
                Math.Min(a.Score, Math.Min(b.Score, d.Score)),
                a.Score,
                b.Score,
                d.Score));
        }

        if (stable.Count == 0)
        {
            Log?.Invoke(
                $"[어비스 전리품 엄격판정] 3프레임 강한 동일위치 후보 없음 -> +0 · " +
                $"최근 상위: {FormatLootTopScores(third)}");
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var winner = stable
            .OrderByDescending(x => x.MinScore)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .First();

        if (!StrictLootPolicy.IsUnambiguousWinner(winner.Key, first, second, third))
        {
            string candidates = string.Join(
                ", ",
                stable.OrderByDescending(x => x.MinScore)
                    .Select(x => $"{FishingAutomation.LootStats.GetDisplayName(x.Key)}={x.MinScore:0.000}"));
            Log?.Invoke(
                $"[어비스 전리품 엄격판정] 유사 아이콘 점수 차이 부족/프레임별 우승 불일치 -> +0 · " +
                $"후보: {candidates}");
            return new HashSet<string>(StringComparer.Ordinal);
        }

        string expectedName = FishingAutomation.LootStats.GetDisplayName(winner.Key);
        var firstLines = await ReadAbyssLootLinesAsync(frame, ct);
        var secondLines = await ReadAbyssLootLinesAsync(secondFrame, ct);
        var thirdLines = await ReadAbyssLootLinesAsync(thirdFrame, ct);

        int exactNameFrames = 0;
        if (StrictLootPolicy.HasExactAdjacentName(expectedName, first[winner.Key], firstLines, frame.Size))
            exactNameFrames++;
        if (StrictLootPolicy.HasExactAdjacentName(expectedName, second[winner.Key], secondLines, secondFrame.Size))
            exactNameFrames++;
        if (StrictLootPolicy.HasExactAdjacentName(expectedName, third[winner.Key], thirdLines, thirdFrame.Size))
            exactNameFrames++;

        if (exactNameFrames < 2)
        {
            Log?.Invoke(
                $"[어비스 전리품 엄격판정] {expectedName} 아이콘은 강하지만 정확한 인접 이름이 " +
                $"{exactNameFrames}/3프레임만 확인됨 -> +0");
            return new HashSet<string>(StringComparer.Ordinal);
        }

        Log?.Invoke(
            $"[어비스 전리품 엄격판정] 최종 1개 확정: {expectedName} · " +
            $"icon={winner.First:0.000}/{winner.Second:0.000}/{winner.Third:0.000} · " +
            $"exactName={exactNameFrames}/3");

        return new HashSet<string>(StringComparer.Ordinal) { winner.Key };
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
                        await CountInventoryLootAsync(ct);
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

                await PrepareInventoryLootRetryAsync(ct);

                Log?.Invoke("[어비스] 다시 하기 인식 -> 1초 대기 후 버튼 재확인");
                await RetryClickDelay.WaitAsync(ct);

                // Inventory queries can take time. Revalidate before sending input.
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
                _inventoryNextRoundClicked = true;
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
                if (enter.Found)
                    throw new InvalidOperationException("어비스 다시 하기 후 입장하기 화면으로 돌아왔습니다. 추가 입력 없이 정지합니다.");
                if (!await HasAbyssCombatEvidenceAsync(frame, ct)) { gone = 0; await Task.Delay(300, ct); continue; }
                if (++gone >= 3)
                {
                    // Preserve the existing V0.1.58 transition rule exactly:
                    // retry screen absent for 3 frames and no entry/outside screen.
                    AbyssTransitionTo(
                        AbyssFlowState.CombatClearWait,
                        "결과 화면 3회 연속 이탈 + 입장/필드 화면 아님");
                    CommitInventoryLootRetry();
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
