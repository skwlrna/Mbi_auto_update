using System.Runtime.Versioning;

namespace FishingAutomation;

internal static class Program
{
    [STAThread]
    [SupportedOSPlatform("windows10.0.19041.0")]
    private static void Main()
    {
#if STAGE16_PREVIEW_ONLY
        // GitHub CI smoke test: no window, no CLI process, no updater or
        // AI worker. A bad command line cannot accidentally start a macro.
        string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (arguments.Length > 0)
        {
            Environment.ExitCode = arguments.Length == 1 &&
                arguments[0] == "--stage16-self-test" &&
                Telemetry.Stage16PreviewGate.Check(false, false, false) ==
                    "explicit_click_required" &&
                Telemetry.Stage16PreviewGate.ExactReadOnlyQueries.Length == 5
                ? 0 : 2;
            return;
        }
#endif
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => WriteCrash("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteCrash("Unhandled exception", ex);
        };

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#if STAGE16_PREVIEW_ONLY
        // Independent read-only preview: cannot instantiate macro, update
        // check, bot, Telegram remote control or AI watcher.
        Application.Run(new Stage16ReadOnlyPreviewForm());
#else
        Application.Run(new MainForm());
#endif
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
