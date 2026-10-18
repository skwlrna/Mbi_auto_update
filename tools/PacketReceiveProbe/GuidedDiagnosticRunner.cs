using System.Diagnostics;
using SharpPcap;
using SharpPcap.LibPcap;

namespace FishingAutomation.PacketDiagnostics;

/// <summary>
/// A single operator-authorized test session: client closed, character idle,
/// character manually active. Capture metadata and two read-only CLI replies
/// are summarized independently, never used to drive game actions.
/// </summary>
internal static class GuidedDiagnosticRunner
{
    private sealed record ActiveCapture(
        ReceiveActivityWindows Windows, TcpFlowCorrelation FlowCorrelation, Stopwatch Clock);

    internal static async Task<int> RunAsync()
    {
        Console.WriteLine("=== One-run read-only comparative diagnostics ===");
        Console.WriteLine("No game controls, packet payloads, IP/PIDs, captures or CLI JSON are saved.");
        Console.WriteLine("3 manual stages: game closed -> character idle -> character active.");
        Console.WriteLine("Only 10 seconds of capture per stage (30 seconds total maximum).");
        Console.WriteLine("You can cancel by entering anything other than READY at a stage.");
        Console.WriteLine("Use only where passive network capture and read-only CLI queries are permitted.");

        try
        {
            var devices = LibPcapLiveDeviceList.Instance;
            if (devices.Count == 0)
            {
                Console.WriteLine("No capture adapters detected. No test started.");
                return 3;
            }
            Console.WriteLine($"interfaces={devices.Count}");
            for (int i = 0; i < devices.Count; i++)
            {
                var desc = (devices[i].Description ?? "(no description)")
                    .Replace('\r', ' ').Replace('\n', ' ');
                if (desc.Length > 120) desc = desc[..120];
                Console.WriteLine($"[{i}] {desc}");
            }

            Console.Write("Select adapter index (blank cancels): ");
            string? adapter = Console.ReadLine();
            Console.WriteLine("This test checks TCP source port 16000 and Windows IPv4");
            Console.WriteLine("remote-port owner candidates. It does not prove game attribution.");
            Console.WriteLine("Optional game CLI commands are exactly: status, get_activity.");
            Console.Write("For this one-time, 3-stage diagnostic enter YES (else cancel): ");
            string? consent = Console.ReadLine();
            if (!GuidedDiagnosticPolicy.TryApprove(
                    devices.Count, adapter, consent, out int deviceIndex))
            {
                Console.WriteLine("Cancelled. Nothing captured or queried.");
                return 0;
            }

            using var device = devices[deviceIndex];
            device.Open();
            device.Filter = CaptureProbeOptions.CaptureFilter;

            ActiveCapture? activeCapture = null;
            device.OnPacketArrival += (_, packet) =>
            {
                var current = Volatile.Read(ref activeCapture);
                if (current is not null)
                    current.Windows.Record(current.Clock.Elapsed, packet.Data.Length);
                if (current is not null)
                    current.FlowCorrelation.Record(packet.Data);
            };

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler stop = (_, e) =>
            {
                e.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += stop;
            try
            {
                var finalComparison = new List<string>();
                for (int stage = 0; stage < GuidedDiagnosticPolicy.MaxStages; stage++)
                {
                    string name = GuidedDiagnosticPolicy.StageNames[stage];
                    Console.WriteLine();
                    Console.WriteLine($"=== STAGE {stage + 1}/{GuidedDiagnosticPolicy.MaxStages}: {name} ===");
                    switch (stage)
                    {
                        case 0:
                            Console.WriteLine("Completely exit the game client (not just character logout).");
                            break;
                        case 1:
                            Console.WriteLine("Start the game, log into your character, and wait idle.");
                            break;
                        case 2:
                            Console.WriteLine("Keep your character logged in. After capture starts,");
                            Console.WriteLine("manually move in-game if permitted; nothing moves automatically.");
                            break;
                    }
                    Console.Write("Type READY to start 10s test; anything else cancels: ");
                    if (!GuidedDiagnosticPolicy.IsReady(Console.ReadLine()))
                    {
                        Console.WriteLine("Session ended by operator; no further stages performed.");
                        return 0;
                    }

                    var before = TcpSocketOwnerProbe.ReadMatchingIpv4RemotePort(
                        CaptureProbeOptions.ExpectedRemoteSourcePort);
                    var beforeFlows = TcpOsFlowSnapshot.ReadMatchingRemotePort(
                        CaptureProbeOptions.ExpectedRemoteSourcePort);
                    Console.WriteLine(
                        $"stage_owner_before stage={name} status={before.Status} " +
                        $"sockets={before.MatchingIpv4Sockets} names={before.ProcessNames}");

                    var windows = new ReceiveActivityWindows();
                    var correlation = new TcpFlowCorrelation();
                    var clock = new Stopwatch();
                    bool started = false;
                    try
                    {
                        clock.Start();
                        Volatile.Write(ref activeCapture, new ActiveCapture(windows, correlation, clock));
                        device.StartCapture();
                        started = true;
                        await Task.Delay(
                            TimeSpan.FromSeconds(GuidedDiagnosticPolicy.StageSeconds),
                            cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        Console.WriteLine("Cancelled by Ctrl+C.");
                        return 0;
                    }
                    finally
                    {
                        Volatile.Write(ref activeCapture, null);
                        if (started)
                            device.StopCapture();
                        clock.Stop();
                    }

                    var summary = windows.Summarize(GuidedDiagnosticPolicy.StageSeconds);
                    var after = TcpSocketOwnerProbe.ReadMatchingIpv4RemotePort(
                        CaptureProbeOptions.ExpectedRemoteSourcePort);
                    var afterFlows = TcpOsFlowSnapshot.ReadMatchingRemotePort(
                        CaptureProbeOptions.ExpectedRemoteSourcePort);
                    var comparison = correlation.Compare(
                        beforeFlows.Flows, afterFlows.Flows,
                        beforeFlows.Available, afterFlows.Available);
                    long namedCandidateFrames = correlation.CountNamedCandidateFrames(
                        beforeFlows.NamedGameCandidateFlows,
                        afterFlows.NamedGameCandidateFlows);
                    Console.WriteLine(
                        $"stage_capture stage={name} frames={summary.MatchedFrames} " +
                        $"frame_bytes={summary.FrameBytes} " +
                        $"active_seconds={summary.SecondsWithFrames} " +
                        $"longest_quiet_seconds={summary.LongestQuietSeconds}");
                    Console.WriteLine(
                        $"stage_owner_after stage={name} status={after.Status} " +
                        $"sockets={after.MatchingIpv4Sockets} names={after.ProcessNames}");
                    Console.WriteLine(
                        $"stage_flow_match stage={name} status={comparison.Status} " +
                        $"matched_frames={comparison.MatchedFrames} " +
                        $"unmatched_frames={comparison.UnmatchedFrames} " +
                        $"unparsed_frames={comparison.UnparsedFrames} " +
                        $"capacity_drops={comparison.FlowCapacityDrops} " +
                        $"named_game_candidate_frames={namedCandidateFrames}");

                    // Existing game CLI is queried separately from the network
                    // counter. Neither result validates or overrides the other.
                    string status = await ReadOnlyCliComparison.QueryAsync(
                        "status", cancellation.Token).ConfigureAwait(false);
                    string activity = await ReadOnlyCliComparison.QueryAsync(
                        "get_activity", cancellation.Token).ConfigureAwait(false);
                    Console.WriteLine(
                        $"stage_cli stage={name} status_query={status} " +
                        $"activity_query={activity}");
                    finalComparison.Add(
                        $"compare stage={name} frames={summary.MatchedFrames} " +
                        $"frame_bytes={summary.FrameBytes} " +
                        $"flow_matched={comparison.MatchedFrames} " +
                        $"flow_unmatched={comparison.UnmatchedFrames} " +
                        $"named_candidate_frames={namedCandidateFrames} " +
                        $"flow_status={comparison.Status} " +
                        $"cli_status={status} cli_activity={activity}");
                }
                Console.WriteLine();
                Console.WriteLine("=== 3-STAGE FINAL COMPARISON ===");
                foreach (string row in finalComparison)
                    Console.WriteLine(row);
                Console.WriteLine("ALL_STAGES_COMPLETE. Send one screenshot of the 3 compare lines.");
                Console.WriteLine(
                    "Port owner names, CLI accepted replies and frame counts are " +
                    "independent observations, NOT verified game packet mappings.");
                Console.WriteLine(
                    "IPv4 4-tuples match sampled OS endpoints only; no payload decoding, " +
                    "authentication, or ability to identify dungeon events.");
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= stop;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or
            TypeInitializationException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception or
            EntryPointNotFoundException)
        {
            Console.Error.WriteLine(
                "Diagnostic unavailable. Check Npcap, permissions and the selected adapter.");
            return 3;
        }
    }
}
