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

Console.WriteLine($"PASS {checks} opt-in-only receive-probe argument regressions; no capture attempted.");
