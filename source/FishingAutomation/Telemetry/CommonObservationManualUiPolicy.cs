namespace FishingAutomation.Telemetry;

/// <summary>
/// The user must explicitly click one-shot inspect. Never run diagnostic CLI
/// in F9/F8/startup/timers/background and never compete with game automation
/// or an existing production CLI poll.
/// </summary>
internal static class CommonObservationManualUiPolicy
{
    internal static string Evaluate(
        bool userClicked, bool anyAutomationRunning,
        bool starting, bool productionPolling, bool anotherDiagnostic)
    {
        if (!userClicked)
            return "explicit_click_required";
        if (anotherDiagnostic)
            return "diagnostic_already_running";
        if (anyAutomationRunning || starting || productionPolling)
            return "automation_busy";
        return "allowed";
    }
}
