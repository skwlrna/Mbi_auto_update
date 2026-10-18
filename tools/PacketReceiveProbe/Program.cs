using SharpPcap;
using System.Diagnostics;
using SharpPcap.LibPcap;
using FishingAutomation.PacketDiagnostics;

if (!CaptureProbeOptions.TryParse(args, out var options) || options is null)
{
    Console.Error.WriteLine(
        "Usage: --interactive OR --list OR --probe --allow-read-only-capture --device-index N --seconds 1..30");
    return 2;
}

// No capture device is ever opened unless the user invokes an explicit command.
try
{
    var devices = LibPcapLiveDeviceList.Instance;
    CaptureProbeOptions effective = options;
    if (options.ListOnly || options.Interactive)
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
        if (options.ListOnly)
            return 0;

        if (devices.Count == 0)
        {
            Console.WriteLine("No adapters found. Nothing was captured.");
            return 3;
        }

        Console.WriteLine();
        Console.WriteLine("Read-only diagnostic: count inbound TCP frames from source port 16000.");
        Console.WriteLine("No payloads, IP addresses, PCAP files, game state or account data saved.");
        Console.WriteLine("This does not verify that the traffic belongs to the game.");
        Console.WriteLine("You must have permission to passively observe the network interface.");
        Console.Write("Choose adapter index (blank cancels): ");
        string? chosenAdapter = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(chosenAdapter))
        {
            Console.WriteLine("Cancelled. No capture started.");
            return 0;
        }
        Console.Write("Duration in seconds (1-30; blank cancels): ");
        string? chosenDuration = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(chosenDuration))
        {
            Console.WriteLine("Cancelled. No capture started.");
            return 0;
        }
        Console.Write("To consent to passive capture, type YES exactly (anything else cancels): ");
        string? consent = Console.ReadLine();
        if (!InteractiveProbeConsent.TryApprove(
                devices.Count, chosenAdapter, chosenDuration, consent, out var approved) ||
            approved is null)
        {
            Console.WriteLine("Cancelled or invalid selection. No capture started.");
            return 0;
        }
        effective = approved;
    }

    if (effective.DeviceIndex >= devices.Count)
    {
        Console.Error.WriteLine("Selected adapter is unavailable; use --list.");
        return 2;
    }

    using var device = devices[effective.DeviceIndex];
    // This is Windows' socket-owner snapshot, not a packet/endpoint match.
    // It runs only after the user has explicitly approved the diagnostic.
    var tcpBefore = TcpSocketOwnerProbe.ReadMatchingIpv4RemotePort(
        CaptureProbeOptions.ExpectedRemoteSourcePort);
    Console.WriteLine(
        $"read_only_tcp_owner_before status={tcpBefore.Status} " +
        $"remote_port_sockets={tcpBefore.MatchingIpv4Sockets} " +
        $"owner_names={tcpBefore.ProcessNames} ipv4_only=true");
    var windows = new ReceiveActivityWindows();
    var captureClock = new Stopwatch();

    // This is a packet *counter*, not a decoder. No frame or payload is kept,
    // printed, transmitted or passed to MabiAuto; no capture file is written.
    device.OnPacketArrival += (_, packet) =>
    {
        windows.Record(captureClock.Elapsed, packet.Data.Length);
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
    int reportedSeconds = 0;
    try
    {
        Console.WriteLine(
            $"read_only_probe_start interface_index={effective.DeviceIndex} " +
            $"filter=\"{CaptureProbeOptions.CaptureFilter}\" seconds={effective.Seconds}");
        // Time is measured monotonically. Each line is a one-second capture
        // window, not a decoded game state or evidence of TCP connectivity.
        bool started = false;
        captureClock.Start();
        try
        {
            device.StartCapture();
            started = true;
            for (int second = 1; second <= effective.Seconds; second++)
            {
                TimeSpan remaining = TimeSpan.FromSeconds(second) - captureClock.Elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellation.Token);
                var window = windows.ReadCompletedSecond(second - 1);
                reportedSeconds = second;
                Console.WriteLine(
                    $"read_only_probe_window second={window.Second} frames={window.Frames} " +
                    $"frame_bytes={window.FrameBytes} activity={window.Activity}");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Ctrl+C is a normal user-requested stop, not a disconnect signal.
        }
        finally
        {
            if (started)
                device.StopCapture();
            captureClock.Stop();
        }
    }
    finally
    {
        Console.CancelKeyPress -= stop;
    }

    // A Ctrl+C can end the current second early. Count its partial bucket
    // in the final summary without pretending it was a full second.
    int countedSeconds = Math.Min(effective.Seconds,
        Math.Max(reportedSeconds, (int)Math.Ceiling(captureClock.Elapsed.TotalSeconds)));
    var summary = windows.Summarize(countedSeconds);
    Console.WriteLine($"read_only_probe_result matched_frames={summary.MatchedFrames} " +
                      $"frame_bytes={summary.FrameBytes} " +
                      $"status={(summary.MatchedFrames > 0 ? "observed" : "not_observed")}");
    Console.WriteLine($"read_only_probe_activity observed_seconds={summary.ObservedSeconds} " +
                      $"active_windows={summary.SecondsWithFrames} " +
                      $"trailing_quiet_windows={summary.TrailingQuietSeconds} " +
                      $"longest_quiet_windows={summary.LongestQuietSeconds} " +
                      $"activity={summary.Activity}");
    var tcpAfter = TcpSocketOwnerProbe.ReadMatchingIpv4RemotePort(
        CaptureProbeOptions.ExpectedRemoteSourcePort);
    Console.WriteLine(
        $"read_only_tcp_owner_after status={tcpAfter.Status} " +
        $"remote_port_sockets={tcpAfter.MatchingIpv4Sockets} " +
        $"owner_names={tcpAfter.ProcessNames} ipv4_only=true");
    Console.WriteLine(
        "OS TCP owners are IPv4 remote-port candidates only; " +
        "no source-IP/frame correlation or CLI status validation was performed.");
    Console.WriteLine("A quiet window does not prove disconnect; observed frames do not prove game origin.");
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
