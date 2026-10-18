namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// A deliberately limited, operator-invoked receive-volume probe.
/// It has no game events, packet decoder, output files or traffic injection.
/// </summary>
internal sealed record CaptureProbeOptions(bool ListOnly, int DeviceIndex, int Seconds, bool Interactive = false)
{
    internal const int ExpectedRemoteSourcePort = 16000;
    internal const int MaxSeconds = 30;
    internal const string ExplicitConsent = "--allow-read-only-capture";
    internal static string CaptureFilter => $"tcp src port {ExpectedRemoteSourcePort}";

    internal static bool TryParse(string[] args, out CaptureProbeOptions? options)
    {
        options = null;
        if (args.Length == 1 && args[0] == "--interactive")
        {
            options = new(false, -1, 0, true);
            return true;
        }
        if (args.Length == 1 && args[0] == "--list")
        {
            options = new(true, -1, 0);
            return true;
        }
        // No implicit capture, auto-selected device, unbounded duration or
        // configurable arbitrary capture filter. The user must opt in each run.
        if (args.Length != 6 ||
            args[0] != "--probe" || args[1] != ExplicitConsent ||
            args[2] != "--device-index" ||
            !int.TryParse(args[3], out var index) || index < 0 ||
            args[4] != "--seconds" ||
            !int.TryParse(args[5], out var seconds) ||
            seconds < 1 || seconds > MaxSeconds)
            return false;

        options = new(false, index, seconds);
        return true;
    }
}
