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

Console.WriteLine($"PASS {checks} opt-in-only receive-probe argument regressions; no capture attempted.");
