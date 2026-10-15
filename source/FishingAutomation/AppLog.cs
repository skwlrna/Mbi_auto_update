using System.Runtime.InteropServices;

namespace FishingAutomation;

public sealed class AppLog
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    // Explicitly opt in on the local Windows host before allowing a debugger
    // to consume scheduler messages. Existing file/UI logging is unchanged.
    private static readonly bool SchedulerDebugEnabled =
        Environment.GetEnvironmentVariable("MABIAUTO_SCHEDULER_DEBUG") == "1";

    [DllImport("kernel32.dll", EntryPoint = "OutputDebugStringW", CharSet = CharSet.Unicode)]
    private static extern void OutputDebugString(string message);

    public event Action<string>? Line;

    public AppLog(string path, long maxBytes)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public void Write(string text) => WriteCore(text, publishToScreen: true);

    // Routine polling can remain available in the file for diagnostics without
    // flooding the on-screen log that the user watches during automation.
    public void WriteBackground(string text) => WriteCore(text, publishToScreen: false);

    private void WriteCore(string text, bool publishToScreen)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {text}";
        lock (_gate)
        {
            try
            {
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > _maxBytes)
                    File.WriteAllText(_path, string.Empty);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch { }
        }
        if (SchedulerDebugEnabled && text.StartsWith("[스케줄러]", StringComparison.Ordinal))
        {
            // A DebugViewCLI capture must still be explicitly started. The
            // line is never transmitted to GitHub, Codex or an external host.
            try { OutputDebugString("MabiAuto " + line); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        if (publishToScreen)
            Line?.Invoke(line);
    }
}
