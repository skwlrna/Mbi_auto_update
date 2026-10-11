using DungeonVisionBot;

namespace FishingAutomation;

public sealed partial class MainForm
{
    // Runs inline inside the multi-altering coordinator's idle callback.
    // It owns the game window exclusively until the exit HUD is verified.
    // No normal dungeon task or second input worker is started.
    private async Task RunCombinedAbyssRoundAsync(
        CancellationToken token, string dungeonName, string dungeonTargetId)
    {
        token.ThrowIfCancellationRequested();
        var windows = WindowTools.EnumerateVisibleWindows();
        if (windows.Count != 1)
            throw new InvalidOperationException("[병행] 게임 창이 정확히 하나여야 합니다.");

        var baseDir = Path.Combine(AppContext.BaseDirectory, "abyss");
        var settings = LoadJson<AppSettings>(
            Path.Combine(baseDir, "config", "appsettings.json"));
        var targets = LoadJson<List<TargetDefinition>>(
            Path.Combine(baseDir, "config", "targets.json"));
        var scenario = LoadJson<ScenarioDefinition>(
            Path.Combine(baseDir, "config", "scenario.json"));

        // Copy loaded scenario instance only. Never mutate the packaged config
        // or ordinary Abyss infinite-repeat behavior.
        scenario.Repeat = false;
        foreach (var step in scenario.Steps)
            if (step.Target.Equals("abyss_selected_dungeon", StringComparison.OrdinalIgnoreCase))
                step.Target = dungeonTargetId;

        _log.Write("[병행] 대기 시간 활용 어비스 1판 시작 · " + dungeonName);
        using var engine = new ScenarioEngine(
            windows[0].Handle, settings, scenario, targets, baseDir, _cli,
            scheduledResultExit: true);

        engine.Log += message => Ui(() => _log.Write("[병행 어비스] " + message));
        await engine.RunAsync(token);
        token.ThrowIfCancellationRequested();
        _log.Write("[병행] 어비스 1판 후 밖 복귀 완료 · 가공 재점검");
    }
}
