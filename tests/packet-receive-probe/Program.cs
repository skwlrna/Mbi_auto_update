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

Console.WriteLine($"PASS {checks} opt-in-only receive-probe argument regressions; no capture attempted.");
