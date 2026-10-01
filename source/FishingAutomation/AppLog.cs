namespace FishingAutomation;

public sealed class AppLog
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();
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
        if (publishToScreen)
            Line?.Invoke(line);
    }
}
