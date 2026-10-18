namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// Parses answers from an operator-visible prompt. It never starts capture.
/// Each invocation requires an explicit device, duration, and exact consent.
/// </summary>
internal static class InteractiveProbeConsent
{
    internal static bool TryApprove(
        int availableDevices,
        string? deviceInput,
        string? secondsInput,
        string? consentInput,
        out CaptureProbeOptions? approved)
    {
        approved = null;
        if (availableDevices <= 0 ||
            !int.TryParse(deviceInput, out int index) ||
            index < 0 || index >= availableDevices ||
            !int.TryParse(secondsInput, out int seconds) ||
            seconds < 1 || seconds > CaptureProbeOptions.MaxSeconds ||
            consentInput != "YES")
            return false;

        // Pass the same gates as noninteractive CLI mode.
        return CaptureProbeOptions.TryParse(
            new[]
            {
                "--probe", CaptureProbeOptions.ExplicitConsent,
                "--device-index", index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            out approved);
    }
}
