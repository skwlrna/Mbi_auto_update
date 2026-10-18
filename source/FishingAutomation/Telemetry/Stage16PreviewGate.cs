namespace FishingAutomation.Telemetry;

/// <summary>
/// A separate, explicitly launched preview GUI is the only caller.
/// Never starts a macro, polls, captures frames, invokes AI repair, or
/// authorizes gameplay. This guard is tested without a game client.
/// </summary>
internal static class Stage16PreviewGate
{
    internal static string Check(bool explicitClick, bool anotherMacroProcess,
        bool diagnosticInProgress)
    {
        if (!explicitClick)
            return "explicit_click_required";
        if (anotherMacroProcess)
            return "close_other_macro_first";
        if (diagnosticInProgress)
            return "single_session_only";
        return "allowed";
    }

    internal static readonly CliDiagnosticQuery[] ExactReadOnlyQueries =
    {
        CliDiagnosticQuery.Status,
        CliDiagnosticQuery.Activity,
        CliDiagnosticQuery.AlteringWorks,
        CliDiagnosticQuery.Items,
        CliDiagnosticQuery.AlteringCatalog
    };
}
