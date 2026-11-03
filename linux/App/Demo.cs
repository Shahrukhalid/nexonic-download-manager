using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>Opens one window at start for screenshots and checks (only when the app is started with NDM_SHOW=name).</summary>
static class Demo
{
    public static void Show(string what, MainWindow w, DownloadManager m)
    {
        w.Show();
        switch (what)
        {
            case "add": _ = Dialogs.Add(w, m, "https://example.com/files/ubuntu-24.04-desktop-amd64.iso", DownloadQueue.MainId); break;
            case "batch": _ = Dialogs.Batch(w, m, DownloadQueue.MainId); break;
            case "grabber": _ = Dialogs.Grabber(w, m, DownloadQueue.MainId); break;
            case "scheduler": _ = Dialogs.Scheduler(w, m, DownloadQueue.MainId); break;
            case "options": _ = Dialogs.Options(w, m); break;
            case "help": HelpWindows.ShowHelp(w); break;
            case "about": _ = HelpWindows.ShowAbout(w); break;
            case "info":
                DownloadWindows.ShowInfo(m, new DownloadJob("https://example.com/files/annual-report.pdf") { Size = 2_345_678, FromBrowser = true });
                break;
            case "progress":
                if (m.Add(new DownloadJob("https://example.com/files/archive.zip"), new AddOptions { Start = false }) is Guid id
                    && m.Rows.FirstOrDefault(r => r.Record.Id == id) is { } row)
                    DownloadWindows.ShowProgress(m, row);
                break;
        }
    }
}
