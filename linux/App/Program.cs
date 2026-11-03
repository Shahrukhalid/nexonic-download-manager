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
        App.StartedForInbox = args.Contains("--inbox");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
