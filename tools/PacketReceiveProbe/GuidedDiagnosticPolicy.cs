using System.Globalization;

namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// An explicitly opted-in one-run, three-scenario comparative diagnostic.
/// Prompts must be answered by the operator, without automatic game input.
/// </summary>
internal static class GuidedDiagnosticPolicy
{
    internal const int StageSeconds = 10;
    internal const int MaxStages = 3;
    internal const int MaxTotalCaptureSeconds = StageSeconds * MaxStages;
    internal static readonly string[] StageNames =
    {
        "client_closed",
        "character_connected_idle",
        "character_connected_activity"
    };
    internal const string ExactConsent = "YES";

    internal static bool TryApprove(
        int availableDevices, string? adapterIndex,
        string? consent, out int approvedIndex)
    {
        approvedIndex = -1;
        if (availableDevices < 1 ||
            !int.TryParse(adapterIndex, NumberStyles.None,
                CultureInfo.InvariantCulture, out int parsed) ||
            parsed < 0 || parsed >= availableDevices ||
            consent != ExactConsent)
            return false;
        approvedIndex = parsed;
        return true;
    }

    internal static bool IsReady(string? input) => input == "READY";
}
