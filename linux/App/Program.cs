using Avalonia;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine(HelpWindows.Version); return 0; }
        if (args.Contains("--remove-integration"))
        {
            new Integration().Remove();
            Console.WriteLine("Removed the browser helper, the ndm command and the menu entry for this user. Downloads and settings were kept.");
            return 0;
        }
        Directory.CreateDirectory(AppPaths.Support);
        // Errors that stop the app are written to a crash report on this computer (Help → Crash Reports).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) CrashReports.Write(ex, "app", HelpWindows.Version);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashReports.Write(e.Exception, "background task", HelpWindows.Version); e.SetObserved(); };
        // Links and link files given on the command line (Open With, nexonic: links) go to the inbox like the ndm command's.
        LaunchArgs.Drop(args);
        var single = new SingleInstance();
        if (!single.TryBecomeFirst())
        {
            // Another copy runs: wake it (it adds the new downloads and shows its window) and quit. Never open a second window.
            // One byte tells it why: 'i' = new downloads from the browser, 's' = the user opened the app.
            single.Signal(args.Contains("--inbox") ? (byte)'i' : (byte)'s');
            return 0;
        }
        App.Single = single;
        ApplyInterfaceScale();
        App.StartedForInbox = args.Contains("--inbox");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Options → General → Interface size: the whole interface is drawn larger or smaller (read when the app starts).
    /// A scale the user set for Avalonia apps themselves is kept.</summary>
    static void ApplyInterfaceScale()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR"))) return;
        try
        {
            if (!File.Exists(AppPaths.Settings)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(AppPaths.Settings));
            if (doc.RootElement.TryGetProperty("InterfaceScale", out var v) && v.TryGetInt32(out var percent) && percent is >= 75 and <= 200 and not 100)
                Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR", (percent / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
