using System.Runtime.Versioning;

namespace FishingAutomation;

internal static class Program
{
    [STAThread]
    [SupportedOSPlatform("windows10.0.19041.0")]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => WriteCrash("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteCrash("Unhandled exception", ex);
        };

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static void WriteCrash(string source, Exception ex)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "ui-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}\r\n{ex}\r\n\r\n");
        }
        catch { }
    }
}
