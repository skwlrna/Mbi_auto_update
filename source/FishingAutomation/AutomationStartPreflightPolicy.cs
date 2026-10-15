namespace FishingAutomation;

/// <summary>
/// Side-effect-free readiness decision used by F9 and every visible Start button.
/// "Warning" is advisory when a mode has its own stricter UI/CLI checks.
/// Never asserts the game is safe for an input merely from these signals.
/// </summary>
internal sealed record AutomationStartReadiness(
    bool CanStart, string Summary, string[] Failures, string[] Warnings);

internal static class AutomationStartPreflightPolicy
{
    internal static AutomationStartReadiness Evaluate(
        string mode, bool gameWindow, bool inputReady,
        bool ocrReady, bool templatesReady, bool scenarioConfigReady)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        // Every macro mode uses an actual game window for its actions.
        if (!gameWindow) errors.Add("게임 창 없음");
        if (mode == "낚시" && !inputReady)
            errors.Add("낚시 입력 장치 준비 필요");
        else if (!inputReady)
            warnings.Add("입력 장치 확인 필요(모드별 기존 검증 유지)");

        if (mode is "던전" or "어비스" && !scenarioConfigReady)
            errors.Add("던전/어비스 시나리오 설정 누락");

        // OCR/templates can be optional in CLI or fixed-visual routes.
        // Do not veto these modes without independent scene evidence.
        if (!ocrReady) warnings.Add("OCR 인식 환경 확인 필요");
        if (!templatesReady) warnings.Add("기본 템플릿 확인 필요");

        string summary = errors.Count > 0 ? string.Join(" · ", errors)
            : warnings.Count > 0 ? "시작 가능 · 주의: " + string.Join(" · ", warnings)
            : "시작 조건 정상";
        return new AutomationStartReadiness(
            errors.Count == 0, summary, errors.ToArray(), warnings.ToArray());
    }
}
