using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nexonic.Core;

namespace Nexonic.App;

public static partial class Dialogs
{
    // ---- Download images from a page ----

    public static void Gallery(Window owner, DownloadManager m)
    {
        var f = new Form(owner, "Download Images from Page", 640);
        f.Note("Lists the pictures of one web page (full-size versions where the page links to them). Only use it on pages you are allowed to download from.");
        var page = f.Text("Page address", ClipboardLink());
        var minSide = f.Text("Leave out pictures smaller than (pixels, when the page says)", "100");
        var bar = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
        var find = new Button { Content = "Find Images", Padding = new Thickness(12, 3, 12, 3) };
        var status = new TextBlock { Foreground = Muted, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var all = new Button { Content = "All", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        var none = new Button { Content = "None", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        DockPanel.SetDock(find, Dock.Left);
        DockPanel.SetDock(none, Dock.Right);
        DockPanel.SetDock(all, Dock.Right);
        bar.Children.Add(find);
        bar.Children.Add(none);
        bar.Children.Add(all);
        bar.Children.Add(status);
        f.Body.Children.Add(bar);
        var results = new ListBox { Height = 190 };
        AutomationProperties.SetName(results, "Images found");
        f.Body.Children.Add(results);
        var folder = f.Folder(m.Settings.Folder);
        var ownFolder = f.Check("Save them in a folder named after the page", true);
        var queue = f.Queue(m, DownloadQueue.MainId);
        var whole = f.Check("Save the whole gallery with the gallery engine (sites like Reddit, Pixiv, DeviantArt, Imgur; the engine is downloaded the first time)", false);
        CancellationTokenSource? cts = null;
        Uri? found = null;
        var pageName = "";

        List<CheckBox> Boxes() => results.Items.OfType<CheckBox>().ToList();
        all.Click += (_, _) => Boxes().ForEach(b => b.IsChecked = true);
        none.Click += (_, _) => Boxes().ForEach(b => b.IsChecked = false);
        find.Click += async (_, _) =>
        {
            if (cts != null) { cts.Cancel(); return; }
            var link = page.Text.Trim();
            if (!new DownloadJob(link).IsValid) { status.Text = "Enter an http or https address."; return; }
            cts = new CancellationTokenSource();
            find.Content = "Stop";
            results.Items.Clear();
            status.Text = "Reading the page…";
            try
            {
                var uri = new Uri(link);
                var html = await ImageGallery.LoadAsync(uri, m.Settings.Proxy, cts.Token);
                var images = ImageGallery.Find(html, uri, Math.Clamp(Number(minSide.Text, 100), 0, 4000));
                found = uri;
                pageName = ImageGallery.FolderName(html, uri);
                foreach (var i in images) results.Items.Add(new CheckBox { Content = i.Url.AbsoluteUri, IsChecked = true, ToolTip = i.Name });
                status.Text = images.Count == 0 ? "No pictures found on this page." : $"{images.Count} pictures found";
            }
            catch (OperationCanceledException) { status.Text = "Stopped."; }
            catch (Exception e) { status.Text = "Could not read the page: " + e.Message; }
            finally
            {
                find.Content = "Find Images";
                cts = null;
            }
        };
        void Go(bool startNow)
        {
            if (whole.IsChecked == true)
            {
                var link = page.Text.Trim();
                if (!new DownloadJob(link).IsValid) { status.Text = "Enter an http or https address."; return; }
                var dir = folder.Text.Trim().Length > 0 ? folder.Text.Trim() : m.Settings.Folder;
                m.Add(new DownloadJob(link) { FileName = pageName.Length > 0 ? pageName : null }, new AddOptions
                {
                    Folder = dir, Gallery = true, Start = startNow, QueueId = (queue.SelectedItem as DownloadQueue)?.Id ?? DownloadQueue.MainId,
                });
                cts?.Cancel();
                f.Close();
                return;
            }
            var links = Boxes().Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList();
            if (links.Count == 0 || found == null) return;
            var target = folder.Text.Trim().Length > 0 ? folder.Text.Trim() : m.Settings.Folder;
            if (ownFolder.IsChecked == true) target = Path.Combine(target, pageName);
            var options = new AddOptions { Folder = target, QueueId = (queue.SelectedItem as DownloadQueue)?.Id ?? DownloadQueue.MainId, Start = startNow };
            // The page is sent as the referrer: many image hosts refuse pictures requested without it.
            foreach (var l in links) m.Add(new DownloadJob(l) { Referrer = found.AbsoluteUri }, options);
            cts?.Cancel();
            f.Close();
        }
        f.AddButton("Close", () => { cts?.Cancel(); f.Close(); }, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Download Checked", () => Go(true), isDefault: true);
        f.Window.Closed += (_, _) => cts?.Cancel();
        f.Show();
    }

    // ---- Network diagnostics ----

    public static void Diagnostics(Window owner, DownloadManager m, string? link)
    {
        var f = new Form(owner, "Network Diagnostics", 620);
        f.Note("Checks a link step by step with your connection settings: name lookup, connection, secure connection and the server's answer. Cookies and passwords are never sent.");
        var start = link ?? ClipboardLink();
        var address = f.Text("Link", string.IsNullOrWhiteSpace(start) ? "https://github.com/" : start);
        var output = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 220, Margin = new Thickness(0, 10, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"),
        };
        AutomationProperties.SetName(output, "Results");
        f.Body.Children.Add(output);
        CancellationTokenSource? cts = null;
        Button? run = null;
        run = f.AddButton("Run", async () =>
        {
            if (cts != null) { cts.Cancel(); return; }
            cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            run!.Content = "Stop";
            output.Text = "Checking…";
            try { output.Text = NetworkDiagnostics.Report(await NetworkDiagnostics.RunAsync(address.Text, m.Settings.Proxy, cts.Token)); }
            catch (OperationCanceledException) { output.Text = "Stopped."; }
            finally { run.Content = "Run"; cts = null; }
        }, isDefault: true);
        f.AddButton("Copy", () => { if (!string.IsNullOrEmpty(output.Text)) Clipboard.SetText(output.Text); });
        f.AddButton("Close", () => { cts?.Cancel(); f.Close(); }, isCancel: true);
        f.Window.Closed += (_, _) => cts?.Cancel();
        f.Show();
    }

    // ---- Link Grabber ----

    public static void LinkGrabber(Window owner, DownloadManager m)
    {
        var f = new Form(owner, "Link Grabber", 680);
        f.Note("Paste text with links, or a web page address and press Find Links. Parts of a split archive are grouped into one package.");
        string clip = "";
        try { clip = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { }
        var input = f.Text("Text, links or page address", clip.Length > 20000 ? "" : clip, multi: true);
        var bar = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
        var find = new Button { Content = "Find Links", Padding = new Thickness(12, 3, 12, 3) };
        var status = new TextBlock { Foreground = Muted, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(find, Dock.Left);
        bar.Children.Add(find);
        bar.Children.Add(status);
        f.Body.Children.Add(bar);
        var results = new StackPanel();
        AutomationProperties.SetName(results, "Packages and links");
        f.Body.Children.Add(new Border
        {
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Height = 220,
            Child = new ScrollViewer { Content = results, Padding = new Thickness(6, 2, 6, 2), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        });
        var folder = f.Folder(m.Settings.StartFolder);
        var ownFolder = f.Check("Put each package in its own folder", true);
        var unpack = f.Check("Unpack a package's archives when all of it is done", true);
        var queue = f.Queue(m, DownloadQueue.MainId);
        var packages = new List<(Nexonic.Core.LinkGrabber.Package Package, List<CheckBox> Boxes)>();
        CancellationTokenSource? cts = null;

        void Show(List<string> links)
        {
            results.Children.Clear();
            packages.Clear();
            foreach (var p in Nexonic.Core.LinkGrabber.Group(links))
            {
                var head = new CheckBox { Content = $"{L.T(p.Name)}  ({p.Links.Count})", IsChecked = true, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
                results.Children.Add(head);
                var boxes = p.Links.Select(l => new CheckBox { Content = l, IsChecked = true, Margin = new Thickness(20, 1, 0, 1) }).ToList();
                head.Click += (_, _) => boxes.ForEach(b => b.IsChecked = head.IsChecked);
                boxes.ForEach(b => results.Children.Add(b));
                packages.Add((p, boxes));
            }
            status.Text = links.Count == 0 ? "No links found." : $"{links.Count} links in {packages.Count} packages";
        }
        find.Click += async (_, _) =>
        {
            if (cts != null) { cts.Cancel(); return; }
            var text = input.Text.Trim();
            if (Nexonic.Core.LinkGrabber.PageAddress(text) is { } page)
            {
                cts = new CancellationTokenSource();
                find.Content = "Stop";
                status.Text = "Reading the page…";
                try { Show(Nexonic.Core.LinkGrabber.Find(await ImageGallery.LoadAsync(page, m.Settings.Proxy, cts.Token), page)); }
                catch (OperationCanceledException) { status.Text = "Stopped."; }
                catch (Exception e) { status.Text = "Could not read the page: " + e.Message; }
                finally { find.Content = "Find Links"; cts = null; }
            }
            else Show(Nexonic.Core.LinkGrabber.Find(text));
        };
        void Go(bool startNow)
        {
            var target = folder.Text.Trim().Length > 0 ? folder.Text.Trim() : m.Settings.Folder;
            m.RememberFolder(target);
            var added = 0;
            foreach (var (p, boxes) in packages)
            {
                var links = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList();
                if (links.Count == 0) continue;
                var isPackage = p.Name != Nexonic.Core.LinkGrabber.OtherFiles;
                var options = new AddOptions
                {
                    Folder = isPackage && ownFolder.IsChecked == true ? Path.Combine(target, p.Name) : target,
                    QueueId = (queue.SelectedItem as DownloadQueue)?.Id ?? DownloadQueue.MainId, Start = startNow,
                    Package = isPackage ? p.Name : null, Unpack = isPackage && unpack.IsChecked == true,
                };
                foreach (var l in links) if (m.Add(new DownloadJob(l), options) != null) added++;
            }
            if (added == 0) { status.Text = "Check at least one link."; return; }
            cts?.Cancel();
            f.Close();
        }
        f.AddButton("Close", () => { cts?.Cancel(); f.Close(); }, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Download Checked", () => Go(true), isDefault: true);
        f.Window.Closed += (_, _) => cts?.Cancel();
        if (input.Text.Trim().Length > 0 && Nexonic.Core.LinkGrabber.PageAddress(input.Text) == null) Show(Nexonic.Core.LinkGrabber.Find(input.Text));
        f.Show();
    }
}
