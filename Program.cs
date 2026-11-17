namespace ArubaMacViewer;

internal static class Program
{
    public static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArubaMacViewer", "crash.log");

    private static Icon? appIcon;

    /// <summary>The SF IT Americas icon embedded in the exe (null if it cannot be loaded).</summary>
    public static Icon? AppIcon
    {
        get
        {
            if (appIcon != null) return appIcon;
            try
            {
                using var st = typeof(Program).Assembly.GetManifestResourceStream("ArubaMacViewer.app.ico");
                if (st != null) appIcon = new Icon(st);
            }
            catch { /* fall back to the default icon */ }
            return appIcon;
        }
    }

    /// <summary>Set by MainForm so unhandled errors also appear in the in-app log.</summary>
    public static Action<string>? LogSink;

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Never let an unexpected exception kill the app: log it, show it, carry on.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash("UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash("Background thread", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { e.SetObserved(); LogOnly("Unobserved task", e.Exception); };

        Application.Run(new MainForm());
    }

    /// <summary>Background failures that don't affect the user: log + crash file, no message box.</summary>
    public static void LogOnly(string where, Exception? ex)
    {
        var inner = ex is AggregateException ae ? ae.Flatten().InnerExceptions.FirstOrDefault() ?? ex : ex;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{where}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
        try { LogSink?.Invoke($"Background error ({where}): {inner?.GetType().Name}: {inner?.Message}"); } catch { }
    }

    public static void ReportCrash(string where, Exception? ex)
    {
        var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{where}] {ex}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, text + Environment.NewLine + Environment.NewLine);
        }
        catch { /* ignore */ }
        try { LogSink?.Invoke($"UNEXPECTED ERROR ({where}): {ex?.GetType().Name}: {ex?.Message}  - details in {CrashLogPath}"); } catch { }
        try
        {
            MessageBox.Show($"An unexpected error occurred ({where}):\n\n{ex?.GetType().Name}: {ex?.Message}\n\n" +
                            $"The program will keep running. Full details were written to:\n{CrashLogPath}",
                            "Aruba Switch MAC Address Viewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { /* ignore */ }
    }
}
