using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

public static partial class Dialogs
{
    public static async Task Torrent(Window owner, DownloadManager manager, string source, string queueId)
    {
        var f = new Form(owner, "Add Torrent", 640);
        f.Note("Torrent peers and trackers can see your IP address. Only download material you may share. " +
               "The bundled native engine downloads and verifies the actual files. " +
               "It uses your proxy setting (HTTP or SOCKS5) and the speed limit for all downloads; DNS over HTTPS must be off. " +
               "Web seeds and local-network peers are disabled. Seeding uses your chosen ratio/time limits; partial files are retained on removal.");
        var enabled = f.Check("Enable experimental torrents", manager.Settings.TorrentsEnabled);
        enabled.PropertyChanged += (_, change) =>
        {
            if (change.Property != ToggleButton.IsCheckedProperty) return;
            var settings = manager.Settings;
            settings.TorrentsEnabled = enabled.IsChecked == true;
            manager.ApplySettings(settings);
        };
        var input = f.Text("Magnet, HTTP(S) torrent link or local .torrent file", source);
        var pick = new Button { Content = "Choose .torrent…" };
        f.Body.Children.Add(pick);
        pick.Click += async (_, _) =>
        {
            var files = await f.Window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Choose torrent", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("BitTorrent") { Patterns = new[] { "*.torrent" } } } });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) input.Text = path;
        };
        var folder = f.Folder(manager.Settings.Folder);
        var queue = f.Queue(manager, queueId);
        var draft = new TorrentMetadata();
        var options = new NativeTorrentOptionsForm(f, draft, 0);
        var preview = f.Note("Load metadata to preview the name, size and files before starting.");
        var fileList = f.Field("Files — choose Skip, Low, Normal or High", new TorrentFileSelection());
        var error = f.Error();
        using var cancel = new CancellationTokenSource();
        f.Window.Closed += (_, _) => cancel.Cancel();
        TorrentMetadata? metadata = null;
        string? previewSource = null;
        var busy = false;
        void Invalidate() { metadata = null; previewSource = null; fileList.Load(null); }
        input.TextChanged += (_, _) => Invalidate();
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Load metadata", async () =>
        {
            if (busy) return;
            error.Text = "";
            Invalidate();
            if (enabled.IsChecked != true) { error.Text = "Enable experimental torrents first."; return; }
            var settings = manager.Settings;
            settings.TorrentsEnabled = true;
            manager.ApplySettings(settings);
            if (TorrentRuntime.Unavailable(settings) is { } reason) { error.Text = reason; return; }
            busy = true;
            preview.Text = "Loading metadata… (up to 90 seconds; Cancel stops the connection)";
            var requestedSource = (input.Text ?? "").Trim();
            try
            {
                var found = await TorrentRuntime.PreviewAsync(requestedSource, cancel.Token, settings.Proxy);
                if (requestedSource != (input.Text ?? "").Trim()) { preview.Text = "Source changed. Load metadata again."; return; }
                metadata = found;
                previewSource = requestedSource;
                preview.Text = $"{found.Name}\n{Row.Bytes(found.Size)} · {found.Files.Count} files · Hash {found.InfoHash}";
                fileList.Load(found);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { error.Text = e.Message; preview.Text = "Metadata could not be loaded."; }
            finally { busy = false; }
        });
        void Add(bool start)
        {
            if (busy || metadata == null || previewSource == null) { error.Text = "Load metadata before adding the torrent."; return; }
            if (enabled.IsChecked != true) { error.Text = "Enable experimental torrents first."; return; }
            try
            {
                metadata.FilePriorities = fileList.Priorities.ToList();
                _ = metadata.SelectedSize; // Reject empty selection before creating a task.
                var destination = (folder.Text ?? "").Trim();
                if (!Path.IsPathFullyQualified(destination)) { error.Text = "Choose an absolute destination folder."; return; }
                var limit = options.Save(draft);
                metadata.Seed = draft.Seed; metadata.Dht = draft.Dht; metadata.RatioLimit = draft.RatioLimit; metadata.SeedSeconds = draft.SeedSeconds;
                metadata.UploadLimitKBps = draft.UploadLimitKBps;
                if (draft.Trackers.Count > 0) metadata.Trackers = draft.Trackers;
                manager.AddTorrent(previewSource, metadata, new AddOptions { Folder = destination,
                    QueueId = Form.QueueId(queue), Start = start, SpeedLimitKBps = limit });
                f.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        }
        f.AddButton("Download Later", () => Add(false));
        f.AddButton("Start Download", () => Add(true));
        await f.Show();
    }
    public static async Task TorrentFiles(Window owner, DownloadManager manager, Row row)
    {
        if (row.Record.Torrent is not { MetadataPending: false } torrent) return;
        var f = new Form(owner, "Torrent Files and Settings", 700);
        f.Note($"{torrent.Name} · {Row.Bytes(torrent.Size)} total\nChanging the selection pauses this torrent. Resume it when ready; existing files stay on disk. " +
               "Skipping a file can still require boundary pieces shared with selected files.");
        var selection = f.Field("Files and priorities", new TorrentFileSelection());
        selection.Load(torrent);
        f.Note(TorrentText.Summary(torrent));
        var options = new NativeTorrentOptionsForm(f, torrent, row.Record.SpeedLimitKBps ?? 0);
        var error = f.Error();
        f.AddButton("Cancel", f.Close, isCancel: true);
        f.AddButton("Save and Pause", () =>
        {
            try
            {
                var draft = new TorrentMetadata(); var limit = options.Save(draft);
                manager.SetTorrentPriorities(row, selection.Priorities);
                torrent.Seed = draft.Seed; torrent.Dht = draft.Dht; torrent.RatioLimit = draft.RatioLimit;
                torrent.UploadLimitKBps = draft.UploadLimitKBps; torrent.SeedSeconds = draft.SeedSeconds; torrent.Trackers = draft.Trackers;
                row.Record.SpeedLimitKBps = limit; manager.Save(); f.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        });
        await f.Show();
    }

}
