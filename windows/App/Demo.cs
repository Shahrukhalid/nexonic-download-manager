using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Fills the app with a sample list and saves pictures of its windows, for the README screenshots.
/// Only active when started with NDM_DEMO=1; NDM_SNAPSHOT=folder saves the pictures and quits,
/// NDM_SHEET=add|scheduler|batch|info|progress|options opens that window first, NDM_LIVE=link adds one real running download,
/// NDM_THEME=light|dark|scheme:Ocean… shows the app in that look.
/// </summary>
public static class Demo
{
    public const string NightQueueId = "demo-night";
    static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    public static void Start(DownloadManager m, MainWindow window)
    {
        if (Env("NDM_DEMO") != "1") return;
        Seed(m);
        if (Env("NDM_THEME") is { Length: > 0 } theme) AppTheme.Apply(theme);
        window.BuildSidebar();
        window.Width = Math.Min(1240, SystemParameters.WorkArea.Width - 20);
        window.Height = Math.Min(720, SystemParameters.WorkArea.Height - 20);
        window.Left = 10;
        window.Top = 10;
        var sheet = Env("NDM_SHEET");
        var dir = Env("NDM_SNAPSHOT");
        var name = Env("NDM_NAME") ?? "main";
        var open = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        open.Tick += (_, _) =>
        {
            open.Stop();
            if (dir != null)
            {
                var shot = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                shot.Tick += (_, _) => { shot.Stop(); Snapshot(window, dir, name); Environment.Exit(0); };
                shot.Start();
            }
            switch (sheet)
            {
                case "add": Dialogs.Add(window, m, "https://example.com/files/course-lecture-09-1080p.mp4", DownloadQueue.MainId); break;
                case "scheduler": Dialogs.Scheduler(window, m, NightQueueId); break;
                case "batch": Dialogs.Batch(window, m, DownloadQueue.MainId); break;
                case "options": Dialogs.Options(window, m); break;
                case "info":
                    DownloadWindows.ShowInfo(m, new DownloadJob("https://example.com/releases/studio-suite-2026-installer.exe")
                        { Size = 1_840_000_000, FromBrowser = true });
                    break;
                case "progress":
                    if (m.Rows.FirstOrDefault(r => r.Record.FileName == "conference-keynote-1080p.mp4") is { } row) DownloadWindows.ShowProgress(m, row);
                    break;
            }
        };
        open.Start();
    }

    static void Seed(DownloadManager m)
    {
        var folder = m.Settings.Folder;
        DownloadRecord Rec(string name, long size, DownloadStatus status, double done, int parts = 8, string? queue = null, double hoursAgo = 1)
        {
            var r = new DownloadRecord
            {
                Url = "https://example.com/files/" + name, FileName = name, Folder = folder, Size = size, AcceptsRanges = true,
                Status = status, QueueId = queue, Added = DateTime.Now.AddHours(-hoursAgo),
            };
            var each = size / parts;
            for (var i = 0; i < parts; i++)
            {
                var start = i * each;
                var end = i == parts - 1 ? size - 1 : start + each - 1;
                var share = done >= 1 ? 1 : Math.Clamp(done * parts - i * 0.6, 0, 1);
                r.Segments.Add(new Segment { Id = i, Start = start, End = end, Written = (long)((end - start + 1) * share) });
            }
            if (status == DownloadStatus.Finished)
            {
                r.SavedPath = Path.Combine(folder, name);
                r.Finished = r.Added.AddMinutes(10);
            }
            return r;
        }
        var list = new List<DownloadRecord>
        {
            Rec("design-handbook.pdf", 18_400_000, DownloadStatus.Finished, 1, 1, hoursAgo: 30),
            Rec("podcast-episode-42.mp3", 96_300_000, DownloadStatus.Finished, 1, hoursAgo: 26),
            Rec("project-assets.zip", 824_000_000, DownloadStatus.Finished, 1, hoursAgo: 20),
            Rec("course-lecture-08-1080p.mp4", 1_420_000_000, DownloadStatus.Finished, 1, hoursAgo: 6),
            Rec("dataset-2026.tar.gz", 3_900_000_000, DownloadStatus.Queued, 0, queue: NightQueueId, hoursAgo: 3),
            Rec("nature-documentary-4k.mkv", 6_200_000_000, DownloadStatus.Paused, 0.37, hoursAgo: 2),
            Rec("photo-library-backup.zip", 2_100_000_000, DownloadStatus.Paused, 0.62, hoursAgo: 1.5),
        };
        list[0].Checksum = "sha256:demo";
        list[0].ChecksumResult = "SHA-256 matches";
        if (Env("NDM_LIVE") is { } live)
        {
            list.Add(new DownloadRecord { Url = live, FileName = "conference-keynote-1080p.mp4", Folder = Path.Combine(folder, "NDM Demo"), Added = DateTime.Now });
            var s = m.Settings;
            if (int.TryParse(Env("NDM_LIMIT"), out var limit)) s.SpeedLimitKBps = limit;
            m.ApplySettings(s);
        }
        var night = new DownloadQueue
        {
            Id = NightQueueId, Name = "Night", MaxActive = 2, StartEnabled = true, StartTime = "01:30", StopEnabled = true,
            StopTime = "07:00", Days = new() { 1, 2, 3, 4, 5 }, AfterDone = AfterQueue.Sleep,
        };
        m.LoadDemo(list, night);
    }

    static BitmapSource Render(Window w)
    {
        var content = (FrameworkElement)w.Content;
        var dpi = VisualTreeHelper.GetDpi(w);
        var width = content.ActualWidth;
        var height = content.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
        }
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(visual);
        return bmp;
    }

    /// <summary>Saves the main window, with an open dialog drawn over it, as name.png.</summary>
    static void Snapshot(MainWindow main, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var baseImage = Render(main);
        var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(win => win != main && win.IsVisible && win.Content is FrameworkElement);
        var w = baseImage.Width;
        var h = baseImage.Height;
        var titleBar = 34.0;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Window frame and title, so the picture looks like the app on screen.
            dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE)), 1), new Rect(0, 0, w, h + titleBar));
            dc.DrawText(Text("Nexonic Download Manager", 13), new Point(14, 9));
            dc.DrawImage(baseImage, new Rect(0, titleBar, w, h));
            if (dialog != null)
            {
                var d = Render(dialog);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, new Rect(0, 0, w, h + titleBar));
                var x = (w - d.Width) / 2;
                var y = Math.Max(titleBar + 20, (h + titleBar - d.Height - titleBar) / 2);
                dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xC0)), 1), new Rect(x, y, d.Width, d.Height + titleBar));
                dc.DrawText(Text(dialog.Title, 13), new Point(x + 14, y + 9));
                dc.DrawImage(d, new Rect(x, y + titleBar, d.Width, d.Height));
            }
        }
        var dpi = VisualTreeHelper.GetDpi(main);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling((h + titleBar) * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(f);
    }

    static FormattedText Text(string s, double size) => new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), size, Brushes.Black, 1.0);
}
