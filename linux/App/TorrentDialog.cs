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
        var f = new Form(owner, "Add Torrent (experimental)", 640);
        f.Note("Torrent peers and trackers can see your IP address. Only download material you may share. " +
               "This optional Linux engine uses /usr/bin/python3 with python3-libtorrent (2.x). " +
               "It connects directly: choose No proxy and disable the shared speed limit / DNS over HTTPS first. " +
               "Web seeds and local-network peers are disabled. Seeding stops on completion; partial files are retained on removal.");
        var enabled = f.Check("Enable experimental torrents", manager.Settings.TorrentsEnabled);
        enabled.PropertyChanged += (_, change) =>
        {
            if (change.Property != ToggleButton.IsCheckedProperty) return;
            var settings = manager.Settings;
            settings.TorrentsEnabled = enabled.IsChecked == true;
            manager.ApplySettings(settings);
        };
        var input = f.Text("Magnet link or absolute path to a local .torrent file", source);
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
        var limit = f.Text("Torrent download limit in KB/s (0 = unlimited)", "0");
        var preview = f.Note("Load metadata to preview the name, size and files before starting.");
        var fileList = f.Text("Files", "", multi: true);
        fileList.IsReadOnly = true;
        var error = f.Error();
        using var cancel = new CancellationTokenSource();
        f.Window.Closed += (_, _) => cancel.Cancel();
        TorrentMetadata? metadata = null;
        string? previewSource = null;
        var busy = false;
        void Invalidate() { metadata = null; previewSource = null; fileList.Text = ""; }
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
                var found = await TorrentRuntime.PreviewAsync(requestedSource, cancel.Token);
                if (requestedSource != (input.Text ?? "").Trim()) { preview.Text = "Source changed. Load metadata again."; return; }
                metadata = found;
                previewSource = requestedSource;
                preview.Text = $"{found.Name}\n{Row.Bytes(found.Size)} · {found.Files.Count} files · Hash {found.InfoHash}";
                fileList.Text = string.Join("\n", found.Files.Select(file => $"{file.Path} ({Row.Bytes(file.Size)})"));
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
                var destination = (folder.Text ?? "").Trim();
                if (!Path.IsPathFullyQualified(destination)) { error.Text = "Choose an absolute destination folder."; return; }
                manager.AddTorrent(previewSource, metadata, new AddOptions { Folder = destination,
                    QueueId = Form.QueueId(queue), Start = start, SpeedLimitKBps = Ui.Number(limit.Text) });
                f.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        }
        f.AddButton("Download Later", () => Add(false));
        f.AddButton("Start Download", () => Add(true));
        await f.Show();
    }
}
