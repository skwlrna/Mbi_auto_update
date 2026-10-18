using SharpPcap;
using SharpPcap.LibPcap;
using FishingAutomation.PacketDiagnostics;

if (!CaptureProbeOptions.TryParse(args, out var options) || options is null)
{
    Console.Error.WriteLine(
        "Usage: --list OR --probe --allow-read-only-capture --device-index N --seconds 1..30");
    return 2;
}

// No capture device is ever opened unless the user invokes an explicit command.
try
{
    var devices = LibPcapLiveDeviceList.Instance;
    if (options.ListOnly)
    {
        Console.WriteLine($"interfaces={devices.Count}");
        for (int index = 0; index < devices.Count; index++)
        {
            // Descriptions may be provided by the local driver. Keep each line
            // bounded and never display addresses, raw packets, or credentials.
            var display = (devices[index].Description ?? "(no description)")
                .Replace('\r', ' ').Replace('\n', ' ');
            if (display.Length > 120) display = display[..120];
            Console.WriteLine($"[{index}] {display}");
        }
        return 0;
    }

    if (options.DeviceIndex >= devices.Count)
    {
        Console.Error.WriteLine("Selected adapter is unavailable; use --list.");
        return 2;
    }

    using var device = devices[options.DeviceIndex];
    long frames = 0;
    long bytes = 0;

    // This is a packet *counter*, not a decoder. No frame or payload is kept,
    // printed, transmitted or passed to MabiAuto; no capture file is written.
    device.OnPacketArrival += (_, packet) =>
    {
        Interlocked.Increment(ref frames);
        Interlocked.Add(ref bytes, packet.Data.Length);
    };
    device.Open();
    device.Filter = CaptureProbeOptions.CaptureFilter;
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler stop = (_, e) =>
    {
        e.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += stop;
    try
    {
        Console.WriteLine(
            $"read_only_probe_start interface_index={options.DeviceIndex} " +
            $"filter=\"{CaptureProbeOptions.CaptureFilter}\" seconds={options.Seconds}");
        device.StartCapture();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.Seconds), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Ctrl+C is a normal user-requested stop.
        }
        finally
        {
            device.StopCapture();
        }
    }
    finally
    {
        Console.CancelKeyPress -= stop;
    }

    long count = Interlocked.Read(ref frames);
    long total = Interlocked.Read(ref bytes);
    Console.WriteLine($"read_only_probe_result matched_frames={count} " +
                      $"frame_bytes={total} status={(count > 0 ? "observed" : "not_observed")}");
    Console.WriteLine(
        "NOT VERIFIED: game ownership, TCP payload, protocol fields, " +
        "Mobilay API compatibility or permission for automation.");
    return 0;
}
catch (Exception e) when (
    e is InvalidOperationException or DllNotFoundException or
    TypeInitializationException or UnauthorizedAccessException or
    System.ComponentModel.Win32Exception)
{
    // A native capture runtime may be unavailable, or no permission granted.
    // No exception text (which may contain local adapter paths) is printed.
    Console.Error.WriteLine(
        "Capture unavailable. Confirm Npcap, permissions and the selected adapter.");
    return 3;
}
