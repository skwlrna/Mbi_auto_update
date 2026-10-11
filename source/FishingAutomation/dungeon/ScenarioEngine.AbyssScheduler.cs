using System.Diagnostics;

namespace DungeonVisionBot;

internal sealed partial class ScenarioEngine
{
    // Scheduler-only route. Unlike the ordinary Abyss run it MUST NOT press
    // "다시 하기": that would begin another battle before multi-altering resumes.
    private async Task ExitAbyssResultForAlteringAsync(CancellationToken ct)
    {
        if (!IsAbyss || !_exitAfterFirstResult)
            throw new InvalidOperationException("병행 스케줄러 전용 어비스 퇴장입니다.");

        _abyssResultOcr ??= new OcrRecognizer();
        var timer = Stopwatch.StartNew();
        DetectionResult previousRetry = DetectionResult.NotFound;
        DetectionResult previousExit = DetectionResult.NotFound;

        while (timer.Elapsed < TimeSpan.FromSeconds(120))
        {
            ct.ThrowIfCancellationRequested();
            using var frame = await CaptureGameWindowAsync(ct);
            // Same production result-row proof as the normal retry flow.
            var retry = await DetectAbyssResultRetryAsync(frame, ct);
            var exitArea = ScaleAbyssResultRoi(AbyssExitButtonRoi, frame.Size);
            var exit = retry.Found
                ? await _abyssResultOcr.FindCompactLabelAsync(frame, exitArea, "나가기", ct)
                : DetectionResult.NotFound;

            bool stable = retry.Found && previousRetry.Found &&
                retry.Bounds.IntersectsWith(previousRetry.Bounds) &&
                exit.Found && previousExit.Found &&
                exitArea.Contains(exit.Center) &&
                exit.Bounds.IntersectsWith(previousExit.Bounds);

            if (stable)
            {
                AbyssTransitionTo(AbyssFlowState.ResultConfirmed,
                    "병행 모드 결과 화면 및 왼쪽 나가기 OCR 2회 연속 확인");

                // The existing CLI inventory tracking remains in use.
                if (!_abyssLootCountedForCurrentResult)
                {
                    try { await CountInventoryLootAsync(ct); }
                    catch (RestartCycleException) { throw; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _abyssLootCountedForCurrentResult = true;
                        Log?.Invoke("[어비스+가공] 전리품 조회 실패 · 퇴장 흐름 유지: " + ex.Message);
                    }
                }

                // The inventory call may take time; all authority must be fresh.
                using var fresh = await CaptureGameWindowAsync(ct);
                var freshRetry = await DetectAbyssResultRetryAsync(fresh, ct);
                var freshArea = ScaleAbyssResultRoi(AbyssExitButtonRoi, fresh.Size);
                var freshExit = freshRetry.Found
                    ? await _abyssResultOcr.FindCompactLabelAsync(
                        fresh, freshArea, "나가기", ct)
                    : DetectionResult.NotFound;

                if (!freshRetry.Found ||
                    !freshRetry.Bounds.IntersectsWith(retry.Bounds) ||
                    !freshExit.Found || !freshArea.Contains(freshExit.Center) ||
                    !freshExit.Bounds.IntersectsWith(exit.Bounds))
                {
                    previousRetry = DetectionResult.NotFound;
                    previousExit = DetectionResult.NotFound;
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                _hwnd = await ResolveRequiredGameWindowAsync(ct);
                NativeMethods.SetForegroundWindow(_hwnd);
                DiagnosticObserveClick("abyss_scheduler_exit", freshExit.Center);
                Log?.Invoke("[어비스+가공] 결과 화면 검증 완료 -> 왼쪽 나가기 클릭 @ " +
                    freshExit.Center);
                _input.ClickClientPoint(_hwnd, freshExit.Center);
                await Task.Delay(Math.Max(700, _settings.ClickSettleMs), ct);

                // Only 3 consecutive outside-HUD confirmations release
                // the game screen back to the altering coordinator.
                await WaitForAbyssHomeAfterNormalExitAsync(ct);
                Log?.Invoke("[어비스+가공] 던전 밖 확인 완료 · 가공 제어권 반환");
                return;
            }

            previousRetry = retry;
            previousExit = exit;
            await Task.Delay(Math.Max(250, _settings.PollIntervalMs), ct);
        }

        DiagnosticPersistFailure("abyss_scheduler_exit_timeout");
        throw new InvalidOperationException(
            "병행 모드 결과 화면의 '나가기'를 안전하게 확정하지 못했습니다. " +
            "다시 하기/추측 클릭 없이 가공과 어비스를 모두 정지합니다. " +
            _lastAbyssResultDiagnostic);
    }
}
