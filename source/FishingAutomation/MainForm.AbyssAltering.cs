using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    /// <summary>
    /// Called ONLY as the existing multi-altering coordinator's idle-delay
    /// delegate. No other processing screen owner can be active at this point.
    /// A single verified Abyss result -> outside transition must finish before
    /// this call returns. Errors propagate to the original batch checkpoint path.
    /// </summary>
    private async Task WaitOrRunScheduledAbyssAsync(
        TimeSpan ordinaryWait,
        IReadOnlyList<AlteringPlan> plans,
        AlteringCliData alteringData,
        AlteringScreen alteringScreen,
        FacilityLaneState laneState,
        nint gameHandle,
        string abyssTargetId,
        string abyssDungeonName,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Observation is read-only, and the existing coordinator still owns
        // ALL receipts, registrations, facility travel and durable records.
        var snapshot = await alteringData.WorksAsync(ct);
        if (!AbyssAlteringIdlePolicy.CanYieldToAbyss(snapshot, plans))
        {
            await Task.Delay(ordinaryWait, ct);
            return;
        }

        Ui(() => _log.Write(
            "[어비스+가공] 대기열 여유 확인 · 가공 UI 정리 후 어비스 1판 실행"));

        // This is the established altering-to-gathering field exit, not a new
        // blind ESC sequence. On uncertainty it throws and stops both modes.
        await alteringScreen.ExitToFieldAsync(ct);
        // Abyss movement invalidates any processing-bench locality proof.
        // On return, the original manager must make a fresh facility approach.
        laneState.InvalidateOnsite("병행 어비스 실행 · 가공 시설 현장확정 폐기");

        await RunOneScheduledAbyssCycleAsync(
            gameHandle, abyssTargetId, abyssDungeonName, ct);
    }

    private async Task RunOneScheduledAbyssCycleAsync(
        nint gameHandle, string abyssTargetId, string abyssDungeonName,
        CancellationToken ct)
    {
        string baseDir = Path.Combine(AppContext.BaseDirectory, "abyss");
        var settings = LoadJson<AppSettings>(
            Path.Combine(baseDir, "config", "appsettings.json"));
        var targets = LoadJson<List<TargetDefinition>>(
            Path.Combine(baseDir, "config", "targets.json"));
        var scenario = LoadJson<ScenarioDefinition>(
            Path.Combine(baseDir, "config", "scenario.json"));

        if (scenario.Steps.Count == 0 ||
            !scenario.Steps.Any(s => s.Target == "abyss_result_retry"))
            throw new InvalidOperationException(
                "병행 실행용 어비스 결과 단계가 없어 안전하게 시작할 수 없습니다.");

        foreach (var step in scenario.Steps)
            if (step.Target.Equals(
                    "abyss_selected_dungeon", StringComparison.OrdinalIgnoreCase))
                step.Target = abyssTargetId;

        using var engine = new ScenarioEngine(
            gameHandle, settings, scenario, targets, baseDir, _cli,
            exitAfterFirstResult: true);
        engine.Log += message => Ui(() =>
        {
            _log.Write("[병행] " + message);
            // Continue using the existing scenario stats and alert parser.
            ProcessScenarioLog("어비스", message);
        });

        Ui(() => _log.Write("[어비스+가공] " + abyssDungeonName +
            " 1판 시작 · 결과 화면에서 나가기 후 가공으로 복귀"));

        // The ordinary Abyss engine is unchanged; the scheduler-only
        // exitAfterFirstResult flag inserts one result-boundary exit.
        await engine.RunAsync(ct);
        Ui(() => _log.Write(
            "[어비스+가공] 어비스 1판 + 던전 밖 확인 완료 · 다중가공 상태 재검증"));
    }
}
