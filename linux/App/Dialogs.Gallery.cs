using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

public static partial class Dialogs
{
    // ---- Download images from a page ----

    public static async Task Gallery(Window owner, DownloadManager m)
    {
        var f = new Form(owner, "Download Images from Page", 660);
        f.Note("Lists the pictures of one web page (full-size versions where the page links to them). Only use it on pages you are allowed to download from.");
        var page = f.Text("Page address", await Ui.ClipboardLink(owner));
        var minSide = f.Text("Leave out pictures smaller than (pixels, when the page says)", "100");
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        var find = new Button { Content = "Find Images" };
        var status = new TextBlock { Opacity = 0.7, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var all = new Button { Content = "All", Margin = new Thickness(4, 0, 0, 0) };
        var none = new Button { Content = "None", Margin = new Thickness(4, 0, 0, 0) };
        DockPanel.SetDock(find, Dock.Left);
        DockPanel.SetDock(none, Dock.Right);
        DockPanel.SetDock(all, Dock.Right);
        bar.Children.Add(find);
        bar.Children.Add(none);
        bar.Children.Add(all);
        bar.Children.Add(status);
        f.Body.Children.Add(bar);
        var results = new StackPanel();
        AutomationProperties.SetName(results, "Images found");
        f.Body.Children.Add(new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 200,
            Child = new ScrollViewer { Content = results, Padding = new Thickness(6, 2) },
        });
        var folder = f.Folder(m.Settings.Folder);
        var ownFolder = f.Check("Save them in a folder named after the page", true);
        var queue = f.Queue(m, DownloadQueue.MainId);
        CancellationTokenSource? cts = null;
        Uri? found = null;
        var pageName = "";

        List<CheckBox> Boxes() => results.Children.OfType<CheckBox>().ToList();
        all.Click += (_, _) => Boxes().ForEach(b => b.IsChecked = true);
        none.Click += (_, _) => Boxes().ForEach(b => b.IsChecked = false);
        find.Click += async (_, _) =>
        {
            if (cts != null) { cts.Cancel(); return; }
            var link = (page.Text ?? "").Trim();
            if (!new DownloadJob(link).IsValid) { status.Text = "Enter an http or https address."; return; }
            cts = new CancellationTokenSource();
            find.Content = "Stop";
            results.Children.Clear();
            status.Text = "Reading the page…";
            try
            {
                var uri = new Uri(link);
                var html = await ImageGallery.LoadAsync(uri, m.Settings.Proxy, cts.Token);
                var images = ImageGallery.Find(html, uri, Math.Clamp(Ui.Number(minSide.Text, 100), 0, 4000));
                found = uri;
                pageName = ImageGallery.FolderName(html, uri);
                foreach (var i in images)
                {
                    var box = new CheckBox { Content = i.Url.AbsoluteUri, IsChecked = true };
                    ToolTip.SetTip(box, i.Name);
                    results.Children.Add(box);
                }
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
            var links = Boxes().Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList();
            if (links.Count == 0 || found == null) return;
            var target = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : m.Settings.Folder;
            if (ownFolder.IsChecked == true) target = Path.Combine(target, pageName);
            var options = new AddOptions { Folder = target, QueueId = Form.QueueId(queue), Start = startNow };
            // The page is sent as the referrer: many image hosts refuse pictures requested without it.
            foreach (var l in links) m.Add(new DownloadJob(l) { Referrer = found.AbsoluteUri }, options);
            cts?.Cancel();
            f.Close();
        }
        f.AddButton("Close", () => { cts?.Cancel(); f.Close(); }, isCancel: true);
        f.AddButton("Download Later", () => Go(false));
        f.AddButton("Download Checked", () => Go(true), isDefault: true);
        f.Window.Closed += (_, _) => cts?.Cancel();
        await f.Show();
    }

    // ---- Network diagnostics ----

    public static async Task Diagnostics(Window owner, DownloadManager m, string? link)
    {
        var f = new Form(owner, "Network Diagnostics", 640);
        f.Note("Checks a link step by step with your connection settings: name lookup, connection, secure connection and the server's answer. Cookies and passwords are never sent.");
        var start = link ?? await Ui.ClipboardLink(owner);
        var address = f.Text("Link", string.IsNullOrWhiteSpace(start) ? "https://github.com/" : start);
        var output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 220, Margin = new Thickness(0, 10, 0, 0) };
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
            try { output.Text = NetworkDiagnostics.Report(await NetworkDiagnostics.RunAsync(address.Text ?? "", m.Settings.Proxy, cts.Token)); }
            catch (OperationCanceledException) { output.Text = "Stopped."; }
            finally { run.Content = "Run"; cts = null; }
        }, isDefault: true);
        f.AddButton("Copy", () => { if (!string.IsNullOrEmpty(output.Text)) _ = f.Window.Clipboard?.SetTextAsync(output.Text); });
        f.AddButton("Close", () => { cts?.Cancel(); f.Close(); }, isCancel: true);
        f.Window.Closed += (_, _) => cts?.Cancel();
        await f.Show();
    }

    // ---- Link Grabber ----

    public static async Task LinkGrabber(Window owner, DownloadManager m)
    {
        var f = new Form(owner, "Link Grabber", 700);
        f.Note("Paste text with links, or a web page address and press Find Links. Parts of a split archive are grouped into one package.");
        string clip = "";
        try { clip = (owner.Clipboard is { } c ? await c.GetTextAsync() : null) ?? ""; } catch { }
        var input = f.Text("Text, links or page address", clip.Length > 20000 ? "" : clip, multi: true);
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        var find = new Button { Content = "Find Links" };
        var status = new TextBlock { Opacity = 0.7, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(find, Dock.Left);
        bar.Children.Add(find);
        bar.Children.Add(status);
        f.Body.Children.Add(bar);
        var results = new StackPanel();
        AutomationProperties.SetName(results, "Packages and links");
        f.Body.Children.Add(new Border
        {
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Height = 220,
            Child = new ScrollViewer { Content = results, Padding = new Thickness(6, 2) },
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
                var head = new CheckBox { Content = $"{L.T(p.Name)}  ({p.Links.Count})", IsChecked = true, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
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
            var text = (input.Text ?? "").Trim();
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
            var target = (folder.Text ?? "").Trim().Length > 0 ? folder.Text!.Trim() : m.Settings.Folder;
            m.RememberFolder(target);
            var added = 0;
            foreach (var (p, boxes) in packages)
            {
                var links = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList();
                if (links.Count == 0) continue;
                var isPackage = p.Name != Nexonic.Core.LinkGrabber.OtherFiles;
                var options = new AddOptions
                {
                    Folder = isPackage && ownFolder.IsChecked == true ? Path.Combine(target, p.Name) : target,
                    QueueId = Form.QueueId(queue), Start = startNow,
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
        if ((input.Text ?? "").Trim().Length > 0 && Nexonic.Core.LinkGrabber.PageAddress(input.Text!) == null) Show(Nexonic.Core.LinkGrabber.Find(input.Text!));
        await f.Show();
    }
}
