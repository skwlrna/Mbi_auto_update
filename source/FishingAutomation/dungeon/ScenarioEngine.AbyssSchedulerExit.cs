using System.Diagnostics;

namespace DungeonVisionBot;

// Optional one-cycle-only mode; ordinary Abyss always uses RetryAbyssResultAsync.
internal sealed partial class ScenarioEngine
{
    private async Task ExitAbyssResultForScheduledProcessingAsync(CancellationToken ct)
    {
        if (!IsAbyss || !_scheduledResultExit || _scenario.Repeat)
            throw new InvalidOperationException("병행 모드가 아닌 결과 퇴장 요청을 차단합니다.");

        var timer = Stopwatch.StartNew();
        DetectionResult previousRetry = DetectionResult.NotFound;

        while (timer.Elapsed < TimeSpan.FromSeconds(120))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            var retry = await DetectAbyssResultRetryAsync(frame, ct);

            if (retry.Found && previousRetry.Found &&
                retry.Bounds.IntersectsWith(previousRetry.Bounds))
            {
                // Reuse the same 2-frame, 3-button proof as normal Abyss.
                AbyssTransitionTo(AbyssFlowState.ResultConfirmed,
                    "병행 어비스 종료 결과 화면 2회 연속 확인");
                AbyssRequireState(AbyssFlowState.ResultConfirmed, "병행 나가기");

                // Preserve existing loot accounting but DO NOT prepare/commit
                // another retry, because this is the last dungeon round.
                if (!_abyssLootCountedForCurrentResult)
                {
                    try { await CountInventoryLootAsync(ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (RestartCycleException) { throw; }
                    catch (Exception ex)
                    {
                        _abyssLootCountedForCurrentResult = true;
                        Log?.Invoke("[병행] 전리품 집계 오류(퇴장은 계속 검증): " + ex.Message);
                    }
                }

                // No blind left-button click. Require an exact Korean EXIT
                // label within the visually-confirmed left button region.
                _abyssResultOcr ??= new OcrRecognizer();
                var left = ScaleAbyssResultRoi(AbyssExitButtonRoi, frame.Size);
                var firstExit = await _abyssResultOcr.FindCompactLabelAsync(
                    frame, left, "나가기", ct);
                if (!firstExit.Found || !left.Contains(firstExit.Center))
                {
                    previousRetry = DetectionResult.NotFound;
                    await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                    continue;
                }

                await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
                using var fresh = await CaptureGameWindowAsync(ct);
                var freshRetry = await DetectAbyssResultRetryAsync(fresh, ct);
                var freshLeft = ScaleAbyssResultRoi(AbyssExitButtonRoi, fresh.Size);
                var confirmedExit = await _abyssResultOcr.FindCompactLabelAsync(
                    fresh, freshLeft, "나가기", ct);
                if (!freshRetry.Found || !freshRetry.Bounds.IntersectsWith(retry.Bounds) ||
                    !confirmedExit.Found || !freshLeft.Contains(confirmedExit.Center) ||
                    !freshLeft.IntersectsWith(firstExit.Bounds))
                {
                    previousRetry = DetectionResult.NotFound;
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                _hwnd = await ResolveRequiredGameWindowAsync(ct);
                NativeMethods.SetForegroundWindow(_hwnd);
                Log?.Invoke("[병행] 결과 2회 + 나가기 텍스트 2회 확인 -> 나가기 1회 클릭");
                DiagnosticObserveClick("abyss_scheduler_exit", confirmedExit.Center);
                _input.ClickClientPoint(_hwnd, confirmedExit.Center);
                await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);
                await WaitForAbyssHomeAfterNormalExitAsync(ct);
                Log?.Invoke("[병행] 던전 밖 HUD 3회 확인 -> 가공 관리자로 제어권 반환");
                return;
            }

            previousRetry = retry;
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }

        DiagnosticPersistFailure("abyss_scheduler_exit_unverified");
        throw new InvalidOperationException(
            "병행 어비스의 나가기 화면/문구 검증 실패. 다른 버튼을 누르지 않고 가공도 안전 정지합니다.");
    }
}
