using FishingAutomation.PacketDiagnostics;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}

bool Try(params string[] args) =>
    CaptureProbeOptions.TryParse(args, out _);

Check(!Try(), "automatic capture allowed on empty invocation");
Check(!Try("--probe"), "probe missing explicit consent");
Check(!Try("--probe", "--device-index", "0", "--seconds", "3"),
    "probe accepted missing consent flag");
Check(!Try("--list", "--probe"), "list command initiated probe");
Check(Try("--list"), "read-only adapter listing rejected");
Check(CaptureProbeOptions.TryParse(new[] { "--list" }, out var listing) &&
    listing is { ListOnly: true, DeviceIndex: -1, Seconds: 0 },
    "listing returned wrong mode");

var valid = new[] {
    "--probe", "--allow-read-only-capture", "--device-index", "0", "--seconds", "10" };
Check(CaptureProbeOptions.TryParse(valid, out var probe) &&
    probe is { ListOnly: false, DeviceIndex: 0, Seconds: 10 },
    "explicit bounded probe was rejected");
Check(Try("--probe", "--allow-read-only-capture", "--device-index", "100",
    "--seconds", "30"), "nonzero device index / maximum duration rejected");

foreach (string index in new[] { "-1", "", "bad", "0;rm", "1.5" })
    Check(!Try("--probe", "--allow-read-only-capture",
        "--device-index", index, "--seconds", "10"), "invalid adapter index accepted");
foreach (string seconds in new[] { "0", "31", "300", "-2", "bad", "1.5" })
    Check(!Try("--probe", "--allow-read-only-capture",
        "--device-index", "0", "--seconds", seconds), "invalid duration accepted");
Check(!Try("--probe", "--ALLOW-READ-ONLY-CAPTURE", "--device-index",
    "0", "--seconds", "1"), "case-insensitive user consent");
Check(!Try("--probe", "--allow-read-only-capture", "--device-index",
    "0", "--seconds", "3", "--write-dump"), "unknown capture option accepted");
Check(!Try("--probe", "--allow-read-only-capture", "--device-index",
    "0", "--seconds", "3", "--inject"), "packet-injection option accepted");
Check(!Try("--probe", "--allow-read-only-capture", "--device-index",
    "0", "--seconds", "3", "--filter=any"), "arbitrary BPF filter accepted");
Check(CaptureProbeOptions.CaptureFilter == "tcp src port 16000",
    "incoming fixed BPF contract changed");
Check(CaptureProbeOptions.MaxSeconds == 30,
    "capture upper bound changed");


Check(CaptureProbeOptions.TryParse(new[] { "--interactive" }, out var interactive) &&
    interactive is { Interactive: true, DeviceIndex: -1, Seconds: 0 },
    "one-click invocation not recognized");
Check(!Try("--INTERACTIVE"), "unexpected upper-case interactive flag accepted");
Check(!Try("--interactive", "--probe"), "interactive added arbitrary options");

bool Approve(int count, string? device, string? seconds, string? consent) =>
    InteractiveProbeConsent.TryApprove(count, device, seconds, consent, out _);
Check(InteractiveProbeConsent.TryApprove(3, "2", "10", "YES", out var accepted) &&
    accepted is { DeviceIndex: 2, Seconds: 10, Interactive: false },
    "interactive explicit approval rejected");
Check(Approve(1, "0", "1", "YES"), "minimum consent duration rejected");
Check(Approve(1, "0", "30", "YES"), "maximum consent duration rejected");
foreach (string? consent in new string?[] { "", "yes", "Yes", "Y", "NO", null, " YES" })
    Check(!Approve(3, "1", "10", consent), "consent not exact YES");
foreach (string? device in new string?[] { "", "-1", "3", "abc", "0 & whoami", null })
    Check(!Approve(3, device, "10", "YES"), "invalid or injected adapter selection allowed");
foreach (string? seconds in new string?[] { "", "0", "31", "999999", "1 & whoami", null })
    Check(!Approve(3, "1", seconds, "YES"), "invalid or injected time allowed");
Check(!Approve(0, "0", "5", "YES"), "capture with no adapters allowed");
Check(!Approve(3, "1", "5", null), "EOF during consent authorized capture");
Check(!Approve(3, "1", null, "YES"), "EOF during duration authorized capture");
Check(!Approve(3, null, "5", "YES"), "EOF during device selection authorized capture");

// Monotonic one-second aggregation has no SharpPcap dependency and no
// game data: all inputs are synthetic counts and lengths.
var windows = new ReceiveActivityWindows();
Check(windows.Summarize(3) is { MatchedFrames: 0, SecondsWithFrames: 0,
    TrailingQuietSeconds: 3, LongestQuietSeconds: 3, Activity: "not_observed" },
    "empty windows were treated as a connection");
windows.Record(TimeSpan.FromMilliseconds(10), 120);
windows.Record(TimeSpan.FromMilliseconds(900), 80);
windows.Record(TimeSpan.FromSeconds(2.1), 64);
Check(windows.ReadCompletedSecond(0) is
    { Second: 1, Frames: 2, FrameBytes: 200, Activity: "frames_observed" },
    "first synthetic window count incorrect");
Check(windows.ReadCompletedSecond(1) is
    { Second: 2, Frames: 0, FrameBytes: 0, Activity: "no_frames_in_window" },
    "quiet interval was not recorded");
Check(windows.ReadCompletedSecond(2) is
    { Second: 3, Frames: 1, FrameBytes: 64, Activity: "frames_observed" },
    "later activity did not return");
Check(windows.Summarize(1) is { MatchedFrames: 2, FrameBytes: 200,
    SecondsWithFrames: 1, TrailingQuietSeconds: 0,
    LongestQuietSeconds: 0, Activity: "frames_recently_observed" },
    "first-window aggregate incorrect");
Check(windows.Summarize(2) is { MatchedFrames: 2, FrameBytes: 200,
    SecondsWithFrames: 1, TrailingQuietSeconds: 1,
    LongestQuietSeconds: 1, Activity: "quiet_after_observation" },
    "temporary quiet incorrectly marked disconnect");
Check(windows.Summarize(3) is { MatchedFrames: 3, FrameBytes: 264,
    SecondsWithFrames: 2, TrailingQuietSeconds: 0,
    LongestQuietSeconds: 1, Activity: "frames_recently_observed" },
    "recovered receive activity aggregate incorrect");

// Reject out-of-range metadata instead of attributing it to another window.
windows.Record(TimeSpan.FromSeconds(-1), 100000);
windows.Record(TimeSpan.FromSeconds(30), 100000);
windows.Record(TimeSpan.FromSeconds(2.2), -5);
Check(windows.Summarize(3) is { MatchedFrames: 3, FrameBytes: 264 },
    "invalid duration or frame length polluted counters");
var boundary = new ReceiveActivityWindows();
boundary.Record(TimeSpan.FromMilliseconds(999), 100);
boundary.Record(TimeSpan.FromSeconds(1), 200);
boundary.Record(TimeSpan.FromSeconds(29.999), 300);
Check(boundary.ReadCompletedSecond(0).Frames == 1 &&
      boundary.ReadCompletedSecond(1).Frames == 1 &&
      boundary.ReadCompletedSecond(29).Frames == 1,
    "monotonic bucket boundary changed");
Check(boundary.Summarize(30) is { MatchedFrames: 3, FrameBytes: 600,
    SecondsWithFrames: 3, TrailingQuietSeconds: 0,
    LongestQuietSeconds: 27 }, "full 30-second summary incorrect");
try
{
    windows.ReadCompletedSecond(-1);
    throw new Exception("negative second was accepted");
}
catch (ArgumentOutOfRangeException) { checks++; }
try
{
    windows.Summarize(31);
    throw new Exception("oversized session length was accepted");
}
catch (ArgumentOutOfRangeException) { checks++; }

// A fixed, synthetic Windows socket-owner table. Nothing here reads the
// machine TCP table or exposes IP addresses/ports beyond the known filter.
var sockets = new[]
{
    new TcpSocketOwnerProbe.SocketOwner(16000, "GameCandidate"),
    new TcpSocketOwnerProbe.SocketOwner(16000, "GameCandidate"),
    new TcpSocketOwnerProbe.SocketOwner(16000, "other_process"),
    new TcpSocketOwnerProbe.SocketOwner(443, "web_browser")
};
var owners = TcpSocketOwnerProbe.Classify(sockets, 16000);
Check(owners is { Status: "port_matched_owner_candidates",
    MatchingIpv4Sockets: 3, DistinctOwnerNames: 2 } &&
    owners.ProcessNames == "GameCandidate,other_process",
    "per-port owner list duplicated names or leaked unrelated sockets");
var noOwner = TcpSocketOwnerProbe.Classify(sockets, 8000);
Check(noOwner is { Status: "no_ipv4_port_match", MatchingIpv4Sockets: 0,
    DistinctOwnerNames: 0, ProcessNames: "none" },
    "unmatched source port was reported as a game socket");
var sanitized = TcpSocketOwnerProbe.Classify(
    new[] { new TcpSocketOwnerProbe.SocketOwner(16000, "owner with spaces") },
    16000);
Check(sanitized.ProcessNames == "owner_with_spaces",
    "process labels not bounded/sanitized");
Check(TcpSocketOwnerProbe.ReadMatchingIpv4RemotePort(0).Status ==
    "os_tcp_table_unavailable", "invalid port permitted OS probing");
try
{
    TcpSocketOwnerProbe.Classify(sockets, 0);
    throw new Exception("port-zero owner lookup accepted");
}
catch (ArgumentOutOfRangeException) { checks++; }
try
{
    TcpSocketOwnerProbe.Classify(sockets, 65536);
    throw new Exception("out-of-range port owner lookup accepted");
}
catch (ArgumentOutOfRangeException) { checks++; }

// One-run batch diagnostics must not auto-run on startup, guess network
// adapters, or accept consent from a previous invocation.
Check(GuidedDiagnosticPolicy.StageNames.SequenceEqual(new[]
    { "client_closed", "character_connected_idle", "character_connected_activity" }),
    "three guided stages changed");
Check(GuidedDiagnosticPolicy.MaxTotalCaptureSeconds == 30 &&
    GuidedDiagnosticPolicy.StageSeconds == 10, "3-stage capture exceeded approved bound");
Check(GuidedDiagnosticPolicy.TryApprove(5, "3", "YES", out var guidedDevice) &&
    guidedDevice == 3, "guided explicit device choice or consent failed");
Check(!GuidedDiagnosticPolicy.TryApprove(5, "3", "yes", out _),
    "lowercase consent started batch");
Check(!GuidedDiagnosticPolicy.TryApprove(5, "5", "YES", out _),
    "out of range adapter started batch");
Check(!GuidedDiagnosticPolicy.TryApprove(0, "0", "YES", out _),
    "no adapters started batch");
Check(!GuidedDiagnosticPolicy.TryApprove(5, null, "YES", out _),
    "missing adapter started batch");
Check(!GuidedDiagnosticPolicy.TryApprove(5, "3", null, out _),
    "missing consent started batch");
Check(!GuidedDiagnosticPolicy.TryApprove(5, "-1", "YES", out _),
    "invalid adapter started batch");
Check(!GuidedDiagnosticPolicy.TryApprove(5, "3 & exit", "YES", out _),
    "injection-like adapter value started batch");
Check(GuidedDiagnosticPolicy.IsReady("READY") &&
    !GuidedDiagnosticPolicy.IsReady("ready") &&
    !GuidedDiagnosticPolicy.IsReady("") &&
    !GuidedDiagnosticPolicy.IsReady(null),
    "manual stage readiness gate changed");
Check(ReadOnlyCliComparison.IsAllowed("status") &&
    ReadOnlyCliComparison.IsAllowed("get_activity") &&
    !ReadOnlyCliComparison.IsAllowed("execute_gathering") &&
    !ReadOnlyCliComparison.IsAllowed("stop_action") &&
    !ReadOnlyCliComparison.IsAllowed("get_my_info") &&
    !ReadOnlyCliComparison.IsAllowed("get_inventory"),
    "guided CLI did not enforce two-command allowlist");
Check(ReadOnlyCliComparison.ClassifyResponse(0,
    """{"body":{"pipe":"connected","health":"SECRET"}}""", "status")
    == "cli_connected", "real CLI status envelope with no root.status misclassified");
Check(ReadOnlyCliComparison.ClassifyResponse(0,
    """{"body":{"activity":"SECRET"}}""", "get_activity")
    == "cli_readonly_object", "real CLI get_activity body without status misclassified");
Check(ReadOnlyCliComparison.ClassifyResponse(0,
    """{"body":"{\"pipe\":\"connected\"}"}""", "status")
    == "cli_connected", "escaped nested JSON body not recognized");
Check(ReadOnlyCliComparison.ClassifyResponse(0,
    """{"body":{"pipe":"disconnected"}}""", "status")
    == "cli_disconnected", "nested disconnected state not recognized");
Check(ReadOnlyCliComparison.ClassifyResponse(0,
    """{"body":{"reason":"game_off"}}""", "status")
    == "cli_game_or_option_off", "game_off not classified");
Check(ReadOnlyCliComparison.ClassifyResponse(0,
    """{"body":{"reason":"option_off"}}""", "status")
    == "cli_game_or_option_off", "option_off not classified");
Check(ReadOnlyCliComparison.ClassifyResponse(0,
    """{"body":{"error":"forbidden"}}""", "get_activity")
    == "cli_rejected", "error envelope accepted");
Check(ReadOnlyCliComparison.ClassifyResponse(5, "{}") == "cli_disconnected",
    "CLI disconnected exit not recognized");
Check(ReadOnlyCliComparison.ClassifyResponse(1, "{}") == "cli_rejected",
    "failed CLI exit accepted");
Check(ReadOnlyCliComparison.ClassifyResponse(0, "not-json") == "cli_invalid_json",
    "non-json CLI response accepted");
Check(ReadOnlyCliComparison.ClassifyResponse(0, "{}", "status")
    == "cli_unclassified_status", "unclassified status promoted to connection");
Check(ReadOnlyCliComparison.ClassifyResponse(0, "{}", "get_activity")
    == "cli_readonly_object", "valid empty activity object misclassified");
Check(ReadOnlyCliComparison.ClassifyResponse(0, """{"status":"rejected"}""") ==
    "cli_rejected", "CLI rejection was promoted to success");
Check(ReadOnlyCliComparison.ClassifyResponse(0, new string('x', 16385)) ==
    "cli_oversize", "oversize CLI response accepted");
Check(ReadOnlyCliComparison.ClassifyResponse(0, "") == "cli_empty",
    "empty CLI response accepted");
Check(ReadOnlyCliComparison.ClassifyResponse(0, "{}", "execute_gathering")
    == "cli_command_blocked", "action passed CLI parser");

// Ethernet II / IPv4 / TCP frames with synthetic documentation ranges only.
// Parser must never read or emit application-layer payloads.
byte[] MakeFrame(
    ushort sourcePort = 16000,
    ushort destPort = 51000,
    bool vlan = false,
    bool fragment = false)
{
    int ethernetHeader = vlan ? 18 : 14;
    var frame = new byte[ethernetHeader + 40];
    frame[12] = vlan ? (byte)0x81 : (byte)0x08;
    frame[13] = vlan ? (byte)0x00 : (byte)0x00;
    if (vlan)
    {
        frame[16] = 0x08;
        frame[17] = 0x00;
    }
    int ip = ethernetHeader;
    frame[ip] = 0x45;
    frame[ip + 2] = 0x00; frame[ip + 3] = 40;
    if (fragment) frame[ip + 6] = 0x20;
    frame[ip + 9] = 6;
    // TEST-NET-1 and RFC1918 addresses, never real user endpoints.
    frame[ip + 12] = 192; frame[ip + 13] = 0;
    frame[ip + 14] = 2; frame[ip + 15] = 10;
    frame[ip + 16] = 10; frame[ip + 17] = 0;
    frame[ip + 18] = 0; frame[ip + 19] = 15;
    int tcp = ip + 20;
    frame[tcp] = (byte)(sourcePort >> 8);
    frame[tcp + 1] = (byte)sourcePort;
    frame[tcp + 2] = (byte)(destPort >> 8);
    frame[tcp + 3] = (byte)destPort;
    frame[tcp + 12] = 0x50; // TCP header length = 20 bytes
    return frame;
}

Check(TcpIpv4FrameParser.TryRead(MakeFrame(), out var tuple) &&
    tuple.RemotePort == 16000 && tuple.LocalPort == 51000 &&
    tuple.RemoteAddress != tuple.LocalAddress, "header-only TCP tuple parse failed");
Check(TcpIpv4FrameParser.TryRead(MakeFrame(vlan: true), out var tagged) &&
    tagged.Equals(tuple), "802.1Q VLAN tuple failed");
Check(!TcpIpv4FrameParser.TryRead(MakeFrame(sourcePort: 443), out _),
    "wrong source port accepted as 16000");
Check(!TcpIpv4FrameParser.TryRead(MakeFrame(fragment: true), out _),
    "fragmented datagram accepted as complete");
Check(!TcpIpv4FrameParser.TryRead(MakeFrame()[..20], out _),
    "truncated frame accepted");
var udp = MakeFrame();
udp[14 + 9] = 17;
Check(!TcpIpv4FrameParser.TryRead(udp, out _), "UDP accepted as TCP");
var malformed = MakeFrame();
malformed[14 + 2] = 0; malformed[14 + 3] = 10;
Check(!TcpIpv4FrameParser.TryRead(malformed, out _),
    "invalid IP total length accepted");
var unsupported = MakeFrame();
unsupported[12] = 0x86; unsupported[13] = 0xdd;
Check(!TcpIpv4FrameParser.TryRead(unsupported, out _),
    "IPv6 under IPv4-only matcher accepted");

var flowCollector = new TcpFlowCorrelation();
flowCollector.Record(MakeFrame());
flowCollector.Record(MakeFrame(vlan: true));
flowCollector.Record(MakeFrame(destPort: 51001));
flowCollector.Record(udp);
var flowResult = flowCollector.Compare(
    new[] { tuple }, Array.Empty<TcpIpv4Flow>(), true, true);
Check(flowResult is { MatchedFrames: 2, UnmatchedFrames: 1,
    UnparsedFrames: 1, FlowCapacityDrops: 0,
    DistinctMatchedFlows: 1, Status: "ipv4_tcp_4tuple_matched" },
    "4-tuple comparison misattributed remote/local port");
var noOwners = flowCollector.Compare(
    Array.Empty<TcpIpv4Flow>(), Array.Empty<TcpIpv4Flow>(), true, true);
Check(noOwners is { MatchedFrames: 0, UnmatchedFrames: 3,
    Status: "no_ipv4_tcp_4tuple_match" },
    "matching source port alone incorrectly attributed to game");
var unknownTable = flowCollector.Compare(
    Array.Empty<TcpIpv4Flow>(), Array.Empty<TcpIpv4Flow>(), false, false);
Check(unknownTable.Status == "os_flow_table_unavailable",
    "missing socket snapshots reported as verified match");
Check(!TcpOsFlowSnapshot.ReadMatchingRemotePort(0).Available,
    "invalid port caused OS table access");
Check(TcpOsFlowSnapshot.IsNamedGameCandidate("MabinogiMobile") &&
    TcpOsFlowSnapshot.IsNamedGameCandidate("mabinogimobile") &&
    !TcpOsFlowSnapshot.IsNamedGameCandidate("MabinogiMobileHelper") &&
    !TcpOsFlowSnapshot.IsNamedGameCandidate(null),
    "named game candidate matching must be exact, case-insensitive");
Check(flowCollector.CountNamedCandidateFrames(new[] { tuple },
    Array.Empty<TcpIpv4Flow>()) == 2, "named game candidate 4-tuple counter failed");
Check(flowCollector.CountNamedCandidateFrames(Array.Empty<TcpIpv4Flow>(),
    Array.Empty<TcpIpv4Flow>()) == 0,
    "port-only matches incorrectly counted as game-process candidates");

// Case studies use synthetic metadata, never packet bytes or user details.
TransportEvidenceAssessment.Result Evidence(
    string cli, long frames, long namedFlows, bool osTable = true,
    long unparsed = 0, long overflow = 0) =>
    TransportEvidenceAssessment.Assess(new(cli, frames, namedFlows,
        unparsed, overflow, osTable));

var correlated = Evidence("cli_connected", 40, 25);
Check(correlated is { Evidence: "independent_sources_agree",
    PacketMeaningVerified: false, AuthorizesGameplay: false },
    "independent sources were promoted to decoded game event");
var cliOnly = Evidence("cli_connected", 40, 0);
Check(cliOnly.Evidence == "cli_only" && !cliOnly.AuthorizesGameplay,
    "CLI alone authorized packet-driven actions");
Check(Evidence("cli_disconnected", 30, 20).Evidence ==
    "conflicting_signals", "disconnect disagreement went undetected");
Check(Evidence("cli_game_or_option_off", 30, 20).Evidence ==
    "conflicting_signals", "game-off disagreement went undetected");
Check(Evidence("cli_timeout", 30, 20).Evidence ==
    "network_candidate_only", "timeout treated as disconnected");
Check(Evidence("cli_readonly_object", 30, 20).Evidence ==
    "network_candidate_only", "activity response alone inferred connection");
Check(Evidence("cli_disconnected", 0, 0).Evidence ==
    "no_observation", "silence was treated as proof game is off");
Check(Evidence("cli_connected", 0, 0).Evidence ==
    "cli_only", "no frames falsely supported socket agreement");
Check(Evidence("cli_timeout", 9, 0).Evidence ==
    "unattributed_traffic", "unmatched traffic was attributed to game");
Check(Evidence("cli_connected", 40, 25, osTable: false).Evidence ==
    "unavailable", "missing OS socket snapshot treated as agreement");
Check(Evidence("cli_connected", 40, 25, overflow: 1).Evidence ==
    "incomplete_evidence", "flow table overflow ignored");
Check(Evidence("cli_connected", 40, 41).Evidence ==
    "invalid_evidence", "more named frames than total frames accepted");
Check(Evidence("cli_connected", -1, 0).Evidence ==
    "invalid_evidence", "negative frame count accepted");
foreach (string cli in new[]
    { "cli_connected", "cli_timeout", "cli_disconnected", "cli_rejected", "cli_empty" })
{
    foreach (long frames in new long[] { 0, 1, 40 })
    {
        var result = Evidence(cli, frames, frames > 0 ? 1 : 0);
        Check(!result.PacketMeaningVerified && !result.AuthorizesGameplay,
            "gameplay authorization raised by diagnostic-only data");
    }
}

// Redacted report accepts only the three known stages, counts, fixed result
// codes and exact user SAVE consent; never console transcripts or CLI bodies.
SanitizedSummaryReport.Stage ReportStage(string name, long frames = 15,
    long bytes = 1024, long matched = 8, long named = 7,
    string cliStatus = "cli_connected", string evidence = "independent_sources_agree") =>
    new(name, frames, bytes, matched, frames - matched, named,
        "ipv4_tcp_4tuple_matched", cliStatus, "cli_readonly_object", evidence);
var reportStages = GuidedDiagnosticPolicy.StageNames.Select(x =>
    ReportStage(x)).ToArray();
Check(SanitizedSummaryReport.TryCreate(reportStages, out string safeReport),
    "valid allowlisted summary rejected");
Check(safeReport.Contains("client_closed", StringComparison.Ordinal) &&
    safeReport.Contains("character_connected_activity", StringComparison.Ordinal) &&
    safeReport.Split("compare stage=").Length == 4,
    "redacted report missing exactly three stage lines");
foreach (string banned in new[] { "ProcessName=", "remote_ip=", "pid=", "cli_json=",
    "auth_token=", "192.0.2.10", "MabinogiMobile", "SECRET" })
    Check(!safeReport.Contains(banned, StringComparison.OrdinalIgnoreCase),
        "report has raw data or process name: " + banned);
Check(!SanitizedSummaryReport.TryCreate(
    reportStages.Take(2).ToArray(), out _),
    "partial session report accepted");
Check(!SanitizedSummaryReport.TryCreate(new[] { reportStages[1],
    reportStages[0], reportStages[2] }, out _),
    "reordered stage report accepted");
Check(!SanitizedSummaryReport.TryCreate(null, out _),
    "null diagnostic report accepted");
var maliciousStage = ReportStage(GuidedDiagnosticPolicy.StageNames[1],
    cliStatus: "cli_connected\\r\\ntoken=SECRET");
Check(!SanitizedSummaryReport.TryCreate(
    new[] { reportStages[0], maliciousStage, reportStages[2] }, out _),
    "arbitrary CLI output injected into result file");
var fakeEvent = ReportStage(GuidedDiagnosticPolicy.StageNames[1],
    evidence: "boss_defeated");
Check(!SanitizedSummaryReport.TryCreate(
    new[] { reportStages[0], fakeEvent, reportStages[2] }, out _),
    "unverified game event name injected into report");
var badCounts = new SanitizedSummaryReport.Stage(
    GuidedDiagnosticPolicy.StageNames[1], 3, 256, 2, 2, 1,
    "ipv4_tcp_4tuple_matched", "cli_connected",
    "cli_readonly_object", "independent_sources_agree");
Check(!SanitizedSummaryReport.TryCreate(
    new[] { reportStages[0], badCounts, reportStages[2] }, out _),
    "impossible per-frame count accepted");
var testDirectory = Path.Combine(Path.GetTempPath(),
    "packet-summary-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);
try
{
    Check(!SanitizedSummaryReport.TrySave("yes", reportStages,
        testDirectory, out _) &&
        Directory.GetFiles(testDirectory).Length == 0,
        "silent report save occurred without exact SAVE consent");
    Check(!SanitizedSummaryReport.TrySave("", reportStages,
        testDirectory, out _) &&
        Directory.GetFiles(testDirectory).Length == 0,
        "empty save consent wrote files");
    Check(SanitizedSummaryReport.TrySave("SAVE", reportStages,
        testDirectory, out string fileName) &&
        fileName == "PacketDiagnostic_Summary.txt" &&
        File.ReadAllText(Path.Combine(testDirectory, fileName)) == safeReport,
        "explicit consent did not write only sanitized report");
    Check(SanitizedSummaryReport.TrySave("SAVE", reportStages,
        testDirectory, out string nextName) &&
        nextName == "PacketDiagnostic_Summary_001.txt" &&
        File.ReadAllText(Path.Combine(testDirectory, fileName)) == safeReport,
        "old report overwritten rather than uniquely preserved");
}
finally
{
    Directory.Delete(testDirectory, recursive: true);
}

Console.WriteLine($"PASS {checks} opt-in-only receive-probe argument regressions; no capture attempted.");
