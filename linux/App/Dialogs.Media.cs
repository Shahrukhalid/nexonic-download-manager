using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>Playlists and channels, and the Media Library (Milestone 8), the same as the Windows app's.</summary>
public static partial class Dialogs
{
    // ---- Playlist or channel ----

    /// <summary>Reads the videos of a playlist or channel, lets the user tick the ones to keep and opens the Add window with them.</summary>
    public static async Task Playlist(Window owner, DownloadManager m, string link, Func<string, Task> add)
    {
        var f = new Form(owner, "Add Playlist or Channel", 640);
        f.Note("Paste a playlist or channel link (YouTube, Vimeo, SoundCloud, Bandcamp and other video sites). " +
               $"Nexonic lists its videos (up to {Playlists.MaxEntries}); choose the ones you want and they open in the Add window.");
        var box = f.Text("Link", link.Length > 0 ? link : await Ui.ClipboardLink(owner));
        var status = f.Note("");
        var busy = new ProgressBar { IsIndeterminate = true, Height = 4, IsVisible = false, Margin = new Thickness(0, 0, 0, 6) };
        f.Body.Children.Add(busy);
        var all = new CheckBox { Content = "All videos", IsChecked = true, IsVisible = false, Margin = new Thickness(0, 0, 0, 4) };
        f.Body.Children.Add(all);
        var rows = new StackPanel();
        var scroll = new ScrollViewer { Content = rows, Height = 300, IsVisible = false };
        f.Body.Children.Add(scroll);
        var checks = new List<(CheckBox Box, Playlists.Entry Entry)>();
        Button? addButton = null;
        void Count()
        {
            var n = checks.Count(c => c.Box.IsChecked == true);
            if (addButton != null) { addButton.Content = n == 1 ? "Add 1 Video" : $"Add {n} Videos"; addButton.IsEnabled = n > 0; }
        }
        all.Click += (_, _) => { foreach (var c in checks) c.Box.IsChecked = all.IsChecked == true; Count(); };
        CancellationTokenSource? cancel = null;
        async void Read()
        {
            var url = (box.Text ?? "").Trim();
            if (!new DownloadJob(url).IsValid) { status.Text = "Paste a web link (http or https)."; return; }
            cancel?.Cancel();
            cancel = new CancellationTokenSource();
            var token = cancel.Token;
            busy.IsVisible = true;
            status.Text = "Reading the list of videos… (large channels can take a minute)";
            try
            {
                var r = await Task.Run(() => Playlists.Read(url, m.Settings.Proxy, token));
                checks.Clear();
                rows.Children.Clear();
                foreach (var e in r.Entries)
                {
                    var c = new CheckBox { IsChecked = true, Content = e.Title + (e.Duration is double d && d > 0 ? $"   ({TimeSpan.FromSeconds(d):g})" : "") };
                    ToolTip.SetTip(c, e.Url);
                    c.Click += (_, _) => Count();
                    checks.Add((c, e));
                    rows.Children.Add(c);
                }
                status.Text = (r.Title.Length > 0 ? r.Title + ": " : "") + (r.Entries.Count == 1 ? "1 video" : $"{r.Entries.Count} videos")
                              + (r.Entries.Count >= Playlists.MaxEntries ? $" (the first {Playlists.MaxEntries})" : "");
                scroll.IsVisible = all.IsVisible = true;
                Count();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!token.IsCancellationRequested) status.Text = e.Message; }
            finally { busy.IsVisible = false; }
        }
        f.Window.Closed += (_, _) => cancel?.Cancel();
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Read Videos", Read);
        string? chosen = null;
        addButton = f.AddButton("Add Videos", () =>
        {
            var links = checks.Where(c => c.Box.IsChecked == true).Select(c => c.Entry.Url).ToList();
            if (links.Count == 0) return;
            chosen = string.Join("\n", links);
            f.Close();
        }, isDefault: true);
        addButton.IsEnabled = false;
        if ((box.Text ?? "").Trim().Length > 0) f.Window.Opened += (_, _) => Read();
        await f.Show();
        if (chosen != null) await add(chosen);
    }

    // ---- Media Library ----

    static Window? library;

    /// <summary>Finished videos and music with preview pictures: search, play, show in folder.</summary>
    public static void MediaLibraryWindow(Window owner, DownloadManager m)
    {
        if (library != null) { library.Activate(); return; }
        var search = new TextBox { Width = 240, Watermark = "Search by name" };
        var kind = new ComboBox { ItemsSource = new[] { "Videos and music", "Videos", "Music" }, SelectedIndex = 0, Margin = new Thickness(8, 0, 0, 0), MinWidth = 150 };
        var count = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 12, 12, 8), Children = { search, kind, count } };
        var grid = new WrapPanel { Margin = new Thickness(8, 0, 8, 0) };
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        string? selected = null;
        Border? selectedCard = null;
        void Play(string? path) { if (path != null && File.Exists(path)) LinuxSystem.Open(path); }
        var open = Ui.Button("Play", () => Play(selected));
        var folder = Ui.Button("Show in Folder", () => { if (selected is { } p && File.Exists(p)) LinuxSystem.ShowInFolder(p); });
        var w = library = new Window
        {
            Title = "Media Library", Width = 920, Height = 660, MinWidth = 520, MinHeight = 360, Icon = Ui.WindowIcon,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        var close = Ui.Button("Close", () => w.Close(), isCancel: true);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Margin = new Thickness(12), Children = { open, folder, close } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(scroll);
        w.Content = root;
        var version = 0;
        void Fill()
        {
            var v = ++version;
            FileCategory? only = kind.SelectedIndex switch { 1 => FileCategory.Video, 2 => FileCategory.Music, _ => null };
            var items = MediaLibrary.Items(m.Rows.Select(r => r.Record).ToList(), (search.Text ?? "").Trim(), only);
            count.Text = items.Count == 1 ? "1 file" : $"{items.Count} files";
            grid.Children.Clear();
            selected = null;
            selectedCard = null;
            var pictures = new List<(Image Image, string Path)>();
            foreach (var i in items)
            {
                var picture = new Image { Width = 200, Height = 112, Stretch = Stretch.UniformToFill };
                var frame = new Panel { Width = 200, Height = 112, Background = Ui.Shade, Children = { new Border { Child = Ui.FileBadge(i.Record.FileName, 44), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, picture } };
                var size = i.Record.Size is long s ? Ui.Bytes(s) : "";
                var info = new StackPanel
                {
                    Children =
                    {
                        new Border { Child = frame, CornerRadius = new CornerRadius(6), ClipToBounds = true },
                        new TextBlock { Text = i.Record.FileName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) },
                        new TextBlock { Text = $"{size}   {(i.Record.Finished ?? i.Record.Added):d MMM yyyy}", Opacity = 0.7, FontSize = 11 },
                    },
                };
                var card = new Border { Width = 212, Padding = new Thickness(6), Margin = new Thickness(2), CornerRadius = new CornerRadius(8), Child = info, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
                ToolTip.SetTip(card, i.Path);
                var path = i.Path;
                card.PointerPressed += (_, e) =>
                {
                    if (selectedCard != null) selectedCard.Background = Brushes.Transparent;
                    selectedCard = card;
                    card.Background = Ui.Shade;
                    selected = path;
                    if (e.ClickCount == 2) Play(path);
                };
                grid.Children.Add(card);
                if (i.Kind == FileCategory.Video) pictures.Add((picture, path));
            }
            // Preview pictures are made one at a time in the background and shown as they arrive.
            _ = Task.Run(() =>
            {
                foreach (var (image, path) in pictures)
                {
                    if (v != version) return;
                    if (MediaLibrary.Thumbnail(path) is not { } jpg) continue;
                    Dispatcher.UIThread.Post(() => { try { image.Source = new Bitmap(jpg); } catch { } });
                }
            });
        }
        search.TextChanged += (_, _) => Fill();
        kind.SelectionChanged += (_, _) => Fill();
        w.Closed += (_, _) => { version++; library = null; };
        Fill();
        w.Show();
    }
}
