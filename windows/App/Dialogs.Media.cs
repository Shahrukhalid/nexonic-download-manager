using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>Playlists and channels, and the Media Library (Milestone 8).</summary>
public static partial class Dialogs
{
    // ---- Playlist or channel ----

    /// <summary>Reads the videos of a playlist or channel, lets the user tick the ones to keep and opens the Add window with them.</summary>
    public static void Playlist(Window owner, DownloadManager m, string link, Action<string> add)
    {
        var f = new Form(owner, "Add Playlist or Channel", 620);
        f.Note("Paste a playlist or channel link (YouTube, Vimeo, SoundCloud, Bandcamp and other video sites). " +
               $"Nexonic lists its videos (up to {Playlists.MaxEntries}); choose the ones you want and they open in the Add window.");
        var box = f.Text("Link", link.Length > 0 ? link : ClipboardLink());
        var status = f.Note("");
        var busy = new ProgressBar { IsIndeterminate = true, Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
        f.Body.Children.Add(busy);
        var all = new CheckBox { Content = "All videos", IsChecked = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
        f.Body.Children.Add(all);
        var list = new ListBox { Height = 300, Visibility = Visibility.Collapsed };
        f.Body.Children.Add(list);
        var checks = new List<(CheckBox Box, Playlists.Entry Entry)>();
        Button? addButton = null;
        void Count()
        {
            var n = checks.Count(c => c.Box.IsChecked == true);
            if (addButton != null) { addButton.Content = n == 1 ? "Add 1 Video" : $"Add {n} Videos"; addButton.IsEnabled = n > 0; }
        }
        all.Click += (_, _) => { foreach (var c in checks) c.Box.IsChecked = all.IsChecked == true; Count(); };
        System.Threading.CancellationTokenSource? cancel = null;
        async void Read()
        {
            var url = box.Text.Trim();
            if (!new DownloadJob(url).IsValid) { status.Text = "Paste a web link (http or https)."; return; }
            cancel?.Cancel();
            cancel = new System.Threading.CancellationTokenSource();
            var token = cancel.Token;
            busy.Visibility = Visibility.Visible;
            status.Text = "Reading the list of videos… (large channels can take a minute)";
            try
            {
                var r = await Task.Run(() => Playlists.Read(url, m.Settings.Proxy, token));
                checks.Clear();
                list.Items.Clear();
                foreach (var e in r.Entries)
                {
                    var c = new CheckBox { IsChecked = true, Content = e.Title + (e.Duration is double d && d > 0 ? $"   ({TimeSpan.FromSeconds(d):g})" : ""), ToolTip = e.Url };
                    c.Click += (_, _) => Count();
                    checks.Add((c, e));
                    list.Items.Add(c);
                }
                status.Text = (r.Title.Length > 0 ? r.Title + ": " : "") + (r.Entries.Count == 1 ? "1 video" : $"{r.Entries.Count} videos")
                              + (r.Entries.Count >= Playlists.MaxEntries ? $" (the first {Playlists.MaxEntries})" : "");
                list.Visibility = all.Visibility = Visibility.Visible;
                Count();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!token.IsCancellationRequested) status.Text = e.Message; }
            finally { busy.Visibility = Visibility.Collapsed; }
        }
        f.Window.Closed += (_, _) => cancel?.Cancel();
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Read Videos", Read);
        addButton = f.AddButton("Add Videos", () =>
        {
            var links = checks.Where(c => c.Box.IsChecked == true).Select(c => c.Entry.Url).ToList();
            if (links.Count == 0) return;
            f.Close();
            add(string.Join("\n", links));
        }, isDefault: true);
        addButton.IsEnabled = false;
        if (box.Text.Trim().Length > 0) f.Window.Loaded += (_, _) => Read();
        f.Show();
    }

    // ---- Media Library ----

    static Window? library;

    /// <summary>Finished videos and music with preview pictures: search, play, show in folder.</summary>
    public static void MediaLibraryWindow(Window owner, DownloadManager m)
    {
        if (library is { IsLoaded: true }) { library.Activate(); return; }
        var search = new TextBox { Width = 240, Padding = new Thickness(4, 3, 4, 3), ToolTip = "Search by name" };
        var kind = new ComboBox { ItemsSource = new[] { "Videos and music", "Videos", "Music" }, SelectedIndex = 0, Margin = new Thickness(8, 0, 0, 0), MinWidth = 140 };
        var count = new TextBlock { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 12, 12, 8) };
        top.Children.Add(new TextBlock { Text = "Search ", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(search);
        top.Children.Add(kind);
        top.Children.Add(count);
        var list = new ListBox { BorderThickness = new Thickness(0), Margin = new Thickness(8, 0, 8, 0) };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
        var open = new Button { Content = "Play", MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        var folder = new Button { Content = "Show in Folder", MinWidth = 110, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        var close = new Button { Content = "Close", MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        bottom.Children.Add(open);
        bottom.Children.Add(folder);
        bottom.Children.Add(close);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(list);
        var w = library = new Window
        {
            Title = "Media Library", Width = 900, Height = 640, MinWidth = 520, MinHeight = 360, Content = root,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner.IsVisible ? owner : null,
        };
        var version = 0;
        string? Selected() => (list.SelectedItem as FrameworkElement)?.Tag as string;
        void Play(string? path) { if (path != null && File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        void Fill()
        {
            var v = ++version;
            FileCategory? only = kind.SelectedIndex switch { 1 => FileCategory.Video, 2 => FileCategory.Music, _ => null };
            var items = MediaLibrary.Items(m.Rows.Select(r => r.Record).ToList(), search.Text.Trim(), only);
            count.Text = items.Count == 1 ? "1 file" : $"{items.Count} files";
            list.Items.Clear();
            var pictures = new List<(Image Image, string Path)>();
            foreach (var i in items)
            {
                var picture = new Image { Width = 200, Height = 112, Stretch = Stretch.UniformToFill };
                var placeholder = new TextBlock
                {
                    Text = i.Kind == FileCategory.Music ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 40, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                var frame = new Grid { Width = 200, Height = 112, Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)) };
                frame.Children.Add(placeholder);
                frame.Children.Add(picture);
                var size = i.Record.Size is long s ? Row.Bytes(s) : "";
                var card = new StackPanel { Width = 200, Margin = new Thickness(6), Tag = i.Path, ToolTip = i.Path, Cursor = Cursors.Hand };
                card.Children.Add(new Border { Child = frame, CornerRadius = new CornerRadius(6), ClipToBounds = true });
                card.Children.Add(new TextBlock { Text = i.Record.FileName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
                card.Children.Add(new TextBlock { Text = $"{size}   {(i.Record.Finished ?? i.Record.Added):d MMM yyyy}", Foreground = Muted, FontSize = 11 });
                card.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) Play(i.Path); };
                list.Items.Add(card);
                if (i.Kind == FileCategory.Video) pictures.Add((picture, i.Path));
            }
            // Preview pictures are made one at a time in the background and shown as they arrive.
            _ = Task.Run(() =>
            {
                foreach (var (image, path) in pictures)
                {
                    if (v != version) return;
                    if (MediaLibrary.Thumbnail(path) is not { } jpg) continue;
                    w.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.UriSource = new Uri(jpg);
                            bmp.EndInit();
                            image.Source = bmp;
                        }
                        catch { }
                    });
                }
            });
        }
        search.TextChanged += (_, _) => Fill();
        kind.SelectionChanged += (_, _) => Fill();
        open.Click += (_, _) => Play(Selected());
        folder.Click += (_, _) => { if (Selected() is { } p && File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\""); };
        close.Click += (_, _) => w.Close();
        w.Closed += (_, _) => { version++; library = null; };
        Fill();
        w.Show();
    }
}
