using System.Diagnostics;

namespace FishingAutomation;

public sealed class WatchdogClient : IDisposable
{
    private readonly string _baseDir;
    private readonly string _heartbeat;
    private readonly string _graceful;
    private Process? _process;

    public WatchdogClient(string baseDir)
    {
        _baseDir = baseDir;
        int pid = Environment.ProcessId;
        _heartbeat = Path.Combine(baseDir, $"watchdog_heartbeat_{pid}.txt");
        _graceful = Path.Combine(baseDir, $"watchdog_graceful_{pid}.flag");
        try { File.Delete(_graceful); } catch { }
        Touch();

        string exe = Path.Combine(baseDir, "MacroWatchdog.exe");
        if (File.Exists(exe))
        {
            try
            {
                _process = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--pid {pid} --base \"{baseDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = baseDir
                });
            }
            catch { }
        }
    }

    public void Touch()
    {
        try { File.WriteAllText(_heartbeat, DateTime.UtcNow.Ticks.ToString()); } catch { }
    }

    public void Dispose()
    {
        try { File.WriteAllText(_graceful, "ok"); } catch { }
        Touch();
        try
        {
            if (_process is not null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch { }
    }
}
