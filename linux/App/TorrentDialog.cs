using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

public static partial class Dialogs
{
    /// <summary>
    /// Add Torrent. The torrent's files load by themselves as soon as there is a source (opened, dropped, chosen or
    /// pasted), the message for any problem sits right under the source, and the rarely changed limits, seeding and
    /// trackers are folded away so Start Download is always on screen.
    /// </summary>
    public static async Task Torrent(Window owner, DownloadManager manager, string source, string queueId)
    {
        var f = new Form(owner, "Add Torrent", 640);
        var enabled = f.Check("Turn on torrents (peers and trackers can see your IP address)", manager.Settings.TorrentsEnabled);
        enabled.IsVisible = !manager.Settings.TorrentsEnabled;
        var input = new TextBox { Text = source };
        var choose = new Button { Content = "Choose File…", Margin = new Thickness(6, 0, 0, 0) };
        var sourceRow = new DockPanel();
        DockPanel.SetDock(choose, Dock.Right); sourceRow.Children.Add(choose); sourceRow.Children.Add(input);
        f.Field("Magnet link, torrent link or .torrent file", sourceRow);
        var status = f.Note(source.Length == 0 ? "Paste a magnet link or choose a .torrent file." : "");
        var normal = status.Foreground;
        var folder = f.Folder(manager.Settings.Folder);
        var queue = f.Queue(manager, queueId);
        var fileList = new TorrentFileSelection();
        f.Field("Files — choose Skip, Low, Normal or High", fileList);
        var filesLabel = f.Target.Children[f.Target.Children.Count - 2];
        void ShowFiles(bool show) { fileList.IsVisible = filesLabel.IsVisible = show; }
        ShowFiles(false);
        var draft = new TorrentMetadata();
        NativeTorrentOptionsForm? options = null;
        f.Collapsed("More options: speed limits, connections, seeding and trackers", () => options = new NativeTorrentOptionsForm(f, draft, 0));
        f.Note("Only download what you may share. Torrents use your proxy setting and the speed limit for all downloads. Local-network peers and web seeds are off.");
        using var cancel = new CancellationTokenSource();
        f.Window.Closed += (_, _) => cancel.Cancel();
        TorrentMetadata? metadata = null; string? previewSource = null; Task<bool>? loading = null;

        void Say(string text, bool problem = false) { status.Text = text; status.Foreground = problem ? Ui.Bad : normal; }
        Task<bool> Load()
        {
            var requested = (input.Text ?? "").Trim();
            if (metadata != null && previewSource == requested) return Task.FromResult(true);
            if (loading != null) return loading;
            return loading = Run();
            async Task<bool> Run()
            {
                try
                {
                    if (requested.Length == 0) { Say("Paste a magnet link or choose a .torrent file.", true); return false; }
                    if (SmartDownload.Detect(requested) != DownloadEngine.Torrent) { Say("This isn't a magnet link, a torrent link or a .torrent file.", true); return false; }
                    if (enabled.IsChecked != true) { Say("Torrents are off. Tick \"Turn on torrents\" above to load this torrent.", true); return false; }
                    if (TorrentRuntime.Unavailable(manager.Settings) is { } reason) { Say(reason, true); return false; }
                    Say("Loading the torrent's files… (a magnet link can take up to a minute while peers are found)");
                    var found = await TorrentRuntime.PreviewAsync(requested, cancel.Token, manager.Settings.Proxy);
                    if (requested != (input.Text ?? "").Trim()) return false;
                    metadata = found; previewSource = requested; fileList.Load(found); ShowFiles(true);
                    Say($"{found.Name}\n{Row.Bytes(found.Size)} · {found.Files.Count(x => !x.Pad)} files");
                    return true;
                }
                catch (OperationCanceledException) { return false; }
                catch (Exception e) { Say("Couldn't load the torrent: " + e.Message, true); return false; }
                finally { loading = null; }
            }
        }

        enabled.PropertyChanged += (_, change) =>
        {
            if (change.Property != ToggleButton.IsCheckedProperty) return;
            var settings = manager.Settings;
            settings.TorrentsEnabled = enabled.IsChecked == true;
            manager.ApplySettings(settings);
            if (settings.TorrentsEnabled) _ = Load();
        };
        input.TextChanged += (_, _) => { metadata = null; previewSource = null; fileList.Load(null); ShowFiles(false); };
        input.LostFocus += (_, _) => { if ((input.Text ?? "").Trim().Length > 0) _ = Load(); };
        choose.Click += async (_, _) =>
        {
            var files = await f.Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Choose torrent", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("BitTorrent") { Patterns = new[] { "*.torrent" } } } });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { input.Text = path; _ = Load(); }
        };

        async void Add(bool start)
        {
            try
            {
                if (!await Load() || metadata == null || previewSource == null) return;
                metadata.FilePriorities = fileList.Priorities.ToList();
                _ = metadata.SelectedSize; // Reject empty selection before creating a task.
                var destination = (folder.Text ?? "").Trim();
                if (!Path.IsPathFullyQualified(destination)) { Say("Choose an absolute destination folder.", true); return; }
                var limit = options!.Save(draft);
                metadata.Seed = draft.Seed; metadata.Dht = draft.Dht; metadata.RatioLimit = draft.RatioLimit; metadata.SeedSeconds = draft.SeedSeconds;
                metadata.UploadLimitKBps = draft.UploadLimitKBps; metadata.MaxConnections = draft.MaxConnections;
                metadata.MaxUploads = draft.MaxUploads; metadata.Sequential = draft.Sequential;
                if (draft.Trackers.Count > 0) metadata.Trackers = draft.Trackers;
                manager.AddTorrent(previewSource, metadata, new AddOptions { Folder = destination,
                    QueueId = Form.QueueId(queue), Start = start, SpeedLimitKBps = limit });
                f.Close();
            }
            catch (Exception e) { Say(e.Message, true); }
        }
        if (DebridTorrents.Unsupported(Debrid.Settings) == null)
            f.AddButton("Download with Debrid", async () =>
            {
                // The debrid service fetches the torrent; the files come here over HTTP (this computer joins no swarm).
                var magnet = (input.Text ?? "").Trim();
                var destination = (folder.Text ?? "").Trim();
                if (!System.IO.Path.IsPathFullyQualified(destination)) { Say("Choose an absolute destination folder.", true); return; }
                Say("Sending the magnet link to " + DebridSettings.Title(Debrid.Settings.Service) + "…");
                try
                {
                    using var http = new System.Net.Http.HttpClient(Network.Handler(manager.Settings.Proxy)) { Timeout = TimeSpan.FromSeconds(60) };
                    var list = await DebridTorrents.ResolveAsync(Debrid.Settings, magnet, http, t => Say(t), cancel.Token);
                    // Several files keep the torrent's name as their folder.
                    var dn = System.Web.HttpUtility.ParseQueryString(magnet.Length > 8 ? magnet[8..] : "")["dn"];
                    var target = list.Count > 1 && FileNames.Sanitize(dn ?? "") is { Length: > 0 } sub ? System.IO.Path.Combine(destination, sub) : destination;
                    foreach (var file in list)
                        manager.Add(new DownloadJob(file.Url, file.Name), new AddOptions { Folder = target, QueueId = Form.QueueId(queue), Start = true });
                    f.Close();
                }
                catch (Exception e) when (e is InvalidOperationException or System.Net.Http.HttpRequestException or TaskCanceledException)
                {
                    if (!cancel.IsCancellationRequested) Say(e.Message, true);
                }
            });
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Download Later", () => Add(false));
        f.AddButton("Start Download", () => Add(true), isDefault: true);
        f.Window.Opened += (_, _) => { if (source.Trim().Length > 0) _ = Load(); else input.Focus(); };
        await f.Show();
    }
    public static async Task TorrentFiles(Window owner, DownloadManager manager, Row row)
    {
        if (row.Record.Torrent is not { MetadataPending: false } torrent) return;
        var f = new Form(owner, "Torrent Files and Settings", 700);
        f.Note($"{torrent.Name} · {Row.Bytes(torrent.Size)} total\nA running torrent takes the changes right away; existing files stay on disk. " +
               "Skipping a file can still require boundary pieces shared with selected files.");
        var selection = f.Field("Files and priorities", new TorrentFileSelection());
        selection.Load(torrent);
        f.Note(TorrentText.Summary(torrent));
        var options = new NativeTorrentOptionsForm(f, torrent, row.Record.SpeedLimitKBps ?? 0);
        var error = f.Error();
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save", () =>
        {
            try
            {
                var draft = new TorrentMetadata(); var limit = options.Save(draft);
                manager.ApplyTorrentSettings(row, draft, limit, selection.Priorities, draft.Trackers); f.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        }, isDefault: true);
        await f.Show();
    }

}
