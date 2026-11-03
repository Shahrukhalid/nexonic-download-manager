using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Nexonic.Core;

namespace Nexonic.Linux;

public sealed class AddOptions
{
    public string? Folder { get; set; }
    public string? FileName { get; set; }
    public string QueueId { get; set; } = DownloadQueue.MainId;
    public bool Start { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Checksum { get; set; } = "";
    public int SpeedLimitKBps { get; set; }
    public bool AudioOnly { get; set; }
}

/// <summary>
/// Queues, scheduler, persistence and after-download actions, the same as the Windows app's (UI thread only).
/// The window calls <see cref="Tick"/> twice a second and hands copied text to <see cref="OfferClipboard"/>;
/// <c>post</c> runs work on the UI thread.
/// </summary>
public sealed class DownloadManager
{
    public ObservableCollection<Row> Rows { get; } = new();
    public ObservableCollection<DownloadQueue> Queues { get; } = new();
    public AppSettings Settings { get; private set; }
    public event Action<DownloadRecord>? Finished;
    public event Action<string, string>? Notify;
    public event Action<string>? ClipboardLink;
    public event Action? QuitRequested;
    public void RequestQuit() => QuitRequested?.Invoke();
    /// <summary>A link clicked in the browser that should open the download window.</summary>
    public event Action<DownloadJob>? DownloadDialogRequested;
    public event Action<Row>? ProgressRequested;
    public event Action? ShowRequested;

    readonly Dictionary<Guid, IDownloader> transfers = new();
    readonly HashSet<string> queuesWithWork = new();
    readonly SpeedLimiter limiter = new();
    readonly Action<Action> post;
    string? lastClipboard;
    string lastMinute = "";
    int ticks;

    public DownloadManager(Action<Action> post)
    {
        this.post = post;
        var first = !File.Exists(AppPaths.Settings);
        Settings = Load<AppSettings>(AppPaths.Settings) ?? new AppSettings();
        if (first) LinuxDefaults(Settings);
        foreach (var q in Load<List<DownloadQueue>>(AppPaths.Queues) ?? new()) Queues.Add(q);
        if (!Queues.Any(q => q.IsMain)) Queues.Insert(0, DownloadQueue.Main());
        foreach (var r in Load<List<DownloadRecord>>(AppPaths.Downloads) ?? new())
        {
            if (r.IsActive) r.Status = DownloadStatus.Queued;
            Rows.Add(new Row(r, QueueName));
        }
        // Lists and settings saved by 0.6.1 hold cookies and passwords in plain text: encrypt them now.
        if (File.Exists(AppPaths.Settings)) Write(AppPaths.Settings, Settings);
        if (File.Exists(AppPaths.Downloads)) Save();
        limiter.SetLimit(Settings.SpeedLimitKBps * 1024);
        // The app reads the inbox once its windows are ready (CollectInbox), so browser links get the download window.
        Schedule();
        Task.Run(SiteVideoTools.Prepare); // keeps the video engine current
    }

    /// <summary>First start on Linux: the desktop's Downloads folder and ClamAV for the optional virus scan.</summary>
    public static void LinuxDefaults(AppSettings s)
    {
        s.Folder = LinuxSystem.DownloadFolder();
        s.ScanCommand = "clamscan --no-summary";
    }

    public void ApplySettings(AppSettings s)
    {
        Settings = s;
        limiter.SetLimit(s.SpeedLimitKBps * 1024);
        Write(AppPaths.Settings, s);
        Schedule();
    }

    // ---- adding ----

    public int AddLinks(string text, AddOptions? o = null)
    {
        o ??= new AddOptions();
        var lines = Links.Split(text);
        var n = 0;
        foreach (var line in lines)
        {
            var job = new DownloadJob(line, lines.Count == 1 && !string.IsNullOrWhiteSpace(o.FileName) ? o.FileName : null);
            if (job.IsValid) { Add(job, o); n++; }
        }
        return n;
    }

    public Guid? Add(DownloadJob job, AddOptions? o = null)
    {
        if (!job.IsValid) return null;
        job = job with { Url = Links.Unwrap(job.Url) };  // e.g. a Google Images result: the picture itself, not Google's page
        o ??= new AddOptions();
        var site = job.Site == true || (Nexonic.Core.SiteVideo.IsSupported(job.Url) && job.Stream != true);
        var stream = !site && (job.Stream == true || HLS.IsPlaylistUrl(job.Url));
        var name = job.FileName is { Length: > 0 } n ? FileNames.Sanitize(n) : FileNames.FromUrl(job.Url);
        if (stream) name = Path.GetFileNameWithoutExtension(name) + ".mp4";
        if (site && string.IsNullOrEmpty(job.FileName)) name = $"Video from {new Uri(job.Url).Host}";
        // A link the user asked to refresh: keep what is downloaded and continue from the new address.
        if (Rows.FirstOrDefault(r => r.Record.AwaitingRefresh == true && r.Record.FileName == name) is { } waiting)
        {
            var w = waiting.Record;
            w.Url = job.Url;
            w.Cookies = job.Cookies ?? w.Cookies;
            w.Referrer = job.Referrer ?? w.Referrer;
            w.AwaitingRefresh = null;
            w.Status = DownloadStatus.Queued;
            w.Error = null;
            waiting.Refresh();
            Save();
            Schedule();
            return w.Id;
        }
        var limit = job.SpeedLimitKBps ?? o.SpeedLimitKBps;
        var rec = new DownloadRecord
        {
            Url = job.Url, Referrer = job.Referrer, Cookies = job.Cookies, UserAgent = job.UserAgent,
            Folder = job.Folder ?? o.Folder ?? Settings.Folder,
            FileName = name,
            QueueId = job.Queue is { Length: > 0 } qn ? QueueIdNamed(qn) : o.QueueId,
            Username = job.Username ?? NullIfEmpty(o.Username),
            Password = job.Password ?? NullIfEmpty(o.Password),
            Checksum = job.Checksum ?? NullIfEmpty(o.Checksum),
            SpeedLimitKBps = limit > 0 ? limit : null,
            Status = job.Start ?? o.Start ? DownloadStatus.Queued : DownloadStatus.Paused,
            IsStream = stream ? true : null,
            SiteVideo = site ? true : null,
            AudioOnly = site && (job.AudioOnly ?? o.AudioOnly) ? true : null,
            InfoJson = site ? job.InfoJson : null,
            MaxHeight = site && !(job.AudioOnly ?? o.AudioOnly) ? job.MaxHeight : null,
        };
        Rows.Insert(0, new Row(rec, QueueName));
        Schedule();
        Save();
        return rec.Id;
    }

    static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>
    /// Downloads handed over by the browser extension and the ndm command. A link clicked in the browser opens the
    /// download window (unless turned off); the same link already downloading is never added twice.
    /// </summary>
    public void CollectInbox()
    {
        var browser = false;
        foreach (var j in new Inbox().Collect())
        {
            if (j.FromBrowser == true) browser = true;
            if (Rows.FirstOrDefault(r => r.Record.Url == j.Url && (r.Record.IsActive || r.Record.Status == DownloadStatus.Queued)) is { } dup)
            {
                if (Settings.ShowProgressWindow && j.FromBrowser == true) ProgressRequested?.Invoke(dup);
                continue;
            }
            if (j.FromBrowser == true && Settings.ShowDownloadDialog && DownloadDialogRequested != null)
            {
                DownloadDialogRequested(j);
                continue;
            }
            var id = Add(j);
            if (id is Guid g && j.FromBrowser == true && j.Start != false && Settings.ShowProgressWindow
                && Rows.FirstOrDefault(r => r.Record.Id == g) is { } row)
                ProgressRequested?.Invoke(row);
        }
        if (browser && Settings.BringToFrontOnBrowserDownload) ShowRequested?.Invoke();
    }

    // ---- control ----

    public void Pause(Row row)
    {
        if (transfers.Remove(row.Record.Id, out var t)) t.Pause();
        else if (row.Record.Status == DownloadStatus.Queued) row.Record.Status = DownloadStatus.Paused;
        row.Refresh();
        Schedule();
    }

    public void Resume(Row row)
    {
        if (row.Record.Status is DownloadStatus.Paused or DownloadStatus.Failed)
        {
            row.Record.Status = DownloadStatus.Queued;
            row.Record.Error = null;
        }
        if (Queues.FirstOrDefault(q => q.Id == row.Record.Queue) is { Running: false } queue) queue.Running = true;
        row.Refresh();
        Schedule();
    }

    public void Remove(Row row)
    {
        if (transfers.Remove(row.Record.Id, out var t)) t.Discard();
        try { Directory.Delete(Path.Combine(AppPaths.Parts, row.Record.Id.ToString("N")), true); } catch { }
        try { if (row.Record.InfoJson is { } info) File.Delete(info); } catch { }
        Rows.Remove(row);
        Save();
        Schedule();
    }

    public void PauseAll() { foreach (var r in Rows.ToList()) if (r.Record.IsActive || r.Record.Status == DownloadStatus.Queued) Pause(r); }
    public void ResumeAll() { foreach (var r in Rows.ToList()) if (r.Record.Status is DownloadStatus.Paused or DownloadStatus.Failed) Resume(r); }
    public void ClearFinished() { foreach (var r in Rows.Where(r => r.Record.Status == DownloadStatus.Finished).ToList()) Remove(r); }

    /// <summary>Continues from a new address (or waits for one from the browser), keeping what is downloaded.</summary>
    public void Refresh(Row row, string? link)
    {
        if (transfers.Remove(row.Record.Id, out var t)) t.Pause();
        var r = row.Record;
        if (link != null && new DownloadJob(link).IsValid)
        {
            r.Url = link.Trim();
            r.AwaitingRefresh = null;
            r.Status = DownloadStatus.Queued;
            r.Error = null;
        }
        else
        {
            r.AwaitingRefresh = true;
            r.Status = DownloadStatus.Paused;
            r.Error = "Waiting for a fresh link from the browser";
        }
        row.Refresh();
        Save();
        Schedule();
    }

    public void SetDetails(Row row, string username, string password, string checksum, int limitKBps, string queueId)
    {
        var r = row.Record;
        r.Username = NullIfEmpty(username);
        r.Password = NullIfEmpty(password);
        r.Checksum = NullIfEmpty(checksum);
        r.SpeedLimitKBps = limitKBps > 0 ? limitKBps : null;
        r.QueueId = queueId;
        if (transfers.TryGetValue(r.Id, out var t)) t.OwnLimiter.SetLimit(Math.Max(0, limitKBps) * 1024);
        row.Refresh();
        Save();
        Schedule();
    }

    /// <summary>Checks the finished file against an expected checksum (the file is read in the background).</summary>
    public async Task<string> VerifyAsync(Row row, string expected)
    {
        if (row.Record.SavedPath is not { } path || !File.Exists(path)) return "The file is not downloaded yet";
        if (Nexonic.Core.Checksum.Parse(expected) == null) return "Paste an MD5, SHA-1, SHA-256 or SHA-512 value";
        var result = await Task.Run(() => Nexonic.Core.Checksum.Verify(expected, path));
        row.Record.Checksum = expected;
        row.Record.ChecksumResult = result;
        row.Refresh();
        Save();
        return result;
    }

    // ---- queues and scheduler ----

    public string QueueName(string id) => Queues.FirstOrDefault(q => q.Id == id)?.Name ?? "Main queue";

    string QueueIdNamed(string name)
    {
        if (Queues.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase)) is { } found) return found.Id;
        var q = new DownloadQueue { Name = name, Running = true };
        Queues.Add(q);
        SaveQueues();
        return q.Id;
    }

    public DownloadQueue NewQueue()
    {
        var n = Queues.Count + 1;
        while (Queues.Any(q => q.Name == $"Queue {n}")) n++;
        var queue = new DownloadQueue { Name = $"Queue {n}" };
        Queues.Add(queue);
        SaveQueues();
        return queue;
    }

    public void DeleteQueue(DownloadQueue q)
    {
        if (q.IsMain) return;
        foreach (var r in Rows.Where(r => r.Record.QueueId == q.Id)) { r.Record.QueueId = null; r.Refresh(); }
        Queues.Remove(q);
        SaveQueues();
        Save();
        Schedule();
    }

    public void StartQueue(string id)
    {
        if (Queues.FirstOrDefault(q => q.Id == id) is not { } queue) return;
        queue.Running = true;
        foreach (var row in Rows.Where(r => r.Record.Queue == id && r.Record.AwaitingRefresh != true &&
                                            r.Record.Status is DownloadStatus.Paused or DownloadStatus.Failed))
        {
            row.Record.Status = DownloadStatus.Queued;
            row.Record.Error = null;
            row.Refresh();
        }
        SaveQueues();
        Save();
        Schedule();
    }

    public void StopQueue(string id)
    {
        if (Queues.FirstOrDefault(q => q.Id == id) is not { } queue) return;
        queue.Running = false;
        foreach (var row in Rows.Where(r => r.Record.Queue == id && r.Record.IsActive).ToList())
        {
            if (transfers.Remove(row.Record.Id, out var t)) t.Pause();
            row.Record.Status = DownloadStatus.Queued; // waits for the queue to start again
            row.Refresh();
        }
        SaveQueues();
        Save();
    }

    public void SaveQueues() => Write(AppPaths.Queues, Queues.ToList());

    void RunTimetable()
    {
        var now = DateTime.Now;
        var minute = QueueClock.HhMm(now);
        if (minute == lastMinute) return;
        lastMinute = minute;
        foreach (var q in Queues.ToList())
        {
            if (QueueClock.ShouldStart(q, now))
            {
                StartQueue(q.Id);
                q.LastStartDay = QueueClock.DayKey(now);
                SaveQueues();
            }
            else if (QueueClock.ShouldStop(q, now)) StopQueue(q.Id);
        }
    }

    void CheckQueuesDone()
    {
        foreach (var q in Queues.Where(q => q.Running).ToList())
        {
            var busy = Rows.Any(r => r.Record.Queue == q.Id && (r.Record.IsActive || r.Record.Status == DownloadStatus.Queued));
            if (busy) { queuesWithWork.Add(q.Id); continue; }
            if (!queuesWithWork.Remove(q.Id)) continue;
            if (!q.IsMain) { q.Running = false; SaveQueues(); }
            AfterQueue(q);
        }
    }

    void AfterQueue(DownloadQueue q)
    {
        switch (q.AfterDone)
        {
            case Nexonic.Core.AfterQueue.Quit:
                Save();
                QuitRequested?.Invoke();
                break;
            case Nexonic.Core.AfterQueue.Sleep:
                Save();
                Notify?.Invoke("Queue finished", $"{q.Name} is done. The computer will sleep now.");
                Task.Run(() => { if (!LinuxSystem.Suspend()) post(() => Notify?.Invoke("Could not sleep", "The system did not allow the app to suspend the computer.")); });
                break;
            case Nexonic.Core.AfterQueue.ShutDown:
                Save();
                Task.Run(() =>
                {
                    var ok = LinuxSystem.ShutDownSoon();
                    post(() => Notify?.Invoke(ok ? "Queue finished" : "Could not shut down",
                        ok ? $"{q.Name} is done. The computer shuts down in one minute (shutdown -c cancels)."
                           : $"{q.Name} is done, but the system did not allow the app to shut down the computer."));
                });
                break;
        }
    }

    void Schedule()
    {
        foreach (var q in Queues.Where(q => q.Running).ToList())
        {
            var limit = q.IsMain ? Settings.MaxActive : q.MaxActive;
            var slots = Math.Max(1, limit) - transfers.Values.Count(t => t.Record.Queue == q.Id);
            foreach (var row in Rows.Reverse().Where(r => r.Record.Queue == q.Id && r.Record.Status == DownloadStatus.Queued).ToList())
            {
                if (slots-- <= 0) break;
                IDownloader t = row.Record.SiteVideo == true
                    ? new SiteVideoTransfer(row.Record, Settings.SortIntoCategoryFolders, Settings.SpeedLimitKBps, Settings.Proxy)
                    : row.Record.IsStream == true
                    ? new StreamTransfer(row.Record, Settings.Connections, limiter, Settings.SortIntoCategoryFolders, Settings.Proxy)
                    : new Transfer(row.Record, Settings.Connections, limiter, Settings.SortIntoCategoryFolders, Settings.Proxy);
                transfers[row.Record.Id] = t;
                row.Record.Status = DownloadStatus.Probing;
                t.Changed += rec => post(() => Changed(row, rec, t));
                _ = t.StartAsync();
            }
        }
    }

    void Changed(Row row, DownloadRecord rec, IDownloader t)
    {
        row.Refresh();
        if (rec.Status is not (DownloadStatus.Finished or DownloadStatus.Failed or DownloadStatus.Paused)) return;
        if (!transfers.TryGetValue(rec.Id, out var current) || current != t) return; // already stopped by the app
        transfers.Remove(rec.Id);
        if (rec.Status == DownloadStatus.Finished) AfterDownload(rec);
        if (rec.Status == DownloadStatus.Failed) Notify?.Invoke("Download failed", $"{rec.FileName}: {rec.Error}");
        Save();
        Schedule();
    }

    // ---- after a download ----

    void AfterDownload(DownloadRecord rec)
    {
        if (rec.SavedPath is not { } path) return;
        // Where the file came from, as browsers record it on Linux (shown by file managers).
        if (Settings.MarkDownloads) LinuxSystem.MarkOrigin(path, rec.Url);
        if (Settings.PlaySound) Task.Run(LinuxSystem.PlayDoneSound);
        if (Settings.ScanEnabled) Scan(path);
        Finished?.Invoke(rec);
    }

    void Scan(string path)
    {
        var words = CommandLineTool.Words(Settings.ScanCommand);
        if (words.Count == 0) return;
        var name = Path.GetFileName(path);
        Task.Run(() =>
        {
            bool ok;
            try
            {
                var psi = new ProcessStartInfo(words[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var w in words.Skip(1)) psi.ArgumentList.Add(w);
                psi.ArgumentList.Add(path);
                using var p = Process.Start(psi)!;
                _ = p.StandardOutput.ReadToEndAsync();
                _ = p.StandardError.ReadToEndAsync();
                p.WaitForExit();
                ok = p.ExitCode == 0;
            }
            catch { ok = false; }
            post(() =>
                Notify?.Invoke(ok ? "Virus scan passed" : "Virus scan warning", ok ? name : $"{name}: the scanner reported a problem or could not run"));
        });
    }

    // ---- clipboard and timer ----

    /// <summary>
    /// The window reads the clipboard every tick and passes the text here: a newly copied download link opens the Add window.
    /// The first text seen is only remembered (a link copied before the app started is not offered again).
    /// </summary>
    public void OfferClipboard(string? text)
    {
        text ??= "";
        if (lastClipboard == null) { lastClipboard = text; return; }
        if (text == lastClipboard) return;
        lastClipboard = text;
        if (!Settings.MonitorClipboard) return;
        if (FileTypes.DownloadLink(text, FileTypes.Parse(Settings.ClipboardTypes)) is { } u && Rows.All(r => r.Record.Url != u.AbsoluteUri && r.Record.Url != text.Trim()))
            ClipboardLink?.Invoke(u.AbsoluteUri);
    }

    /// <summary>Twice a second: speeds, the inbox, the timetable and saving.</summary>
    public void Tick()
    {
        foreach (var r in Rows) if (r.Record.IsActive) r.Tick();
        if (++ticks % 2 == 0)
        {
            CollectInbox();
            RunTimetable();
            CheckQueuesDone();
        }
        if (ticks % 4 == 0) Save();
    }

    public void Save() => Write(AppPaths.Downloads, Rows.Select(r => r.Record).ToList());

    /// <summary>Saves atomically, with cookies and passwords encrypted with the keyring's key (see Secrets).</summary>
    static void Write<T>(string path, T value)
    {
        try { SafeFile.Write(path, Secrets.SerializeSealed(value)); }
        catch { }
    }

    static T? Load<T>(string path) => SafeFile.Load(path, Secrets.DeserializeOpened<T>);
}

/// <summary>One row in the table; raises change notifications for the bound columns.</summary>
public sealed class Row : System.ComponentModel.INotifyPropertyChanged
{
    public DownloadRecord Record { get; }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    readonly Func<string, string> queueName;
    readonly Queue<long> recent = new();   // received bytes at each of the last ticks
    public double SpeedBytes { get; private set; }

    public Row(DownloadRecord r, Func<string, string> queueName)
    {
        Record = r;
        this.queueName = queueName;
    }

    public string Name => Record.FileName;
    public string Size => Record.Size is long s ? Bytes(s) : "–";
    public double Percent => Record.Progress * 100;
    public string Speed => Record.IsActive && SpeedBytes > 0 ? Bytes((long)SpeedBytes) + "/s" : "";
    public string TimeLeft => Record.IsActive ? TimeLeftText.Of(Record.Size, Record.Received, SpeedBytes) : "";
    /// <summary>Video sites and streams take a few seconds to look up the best quality before the first byte arrives.</summary>
    public bool PreparingVideo => Record.IsActive && (Record.SiteVideo == true || Record.IsStream == true) && Record.Received == 0;
    public string Status => PreparingVideo ? "Preparing video…" : Record.Status switch
    {
        DownloadStatus.Queued => "Queued",
        DownloadStatus.Probing => "Connecting…",
        DownloadStatus.Downloading => $"{Percent:0}%",
        DownloadStatus.Paused => Record.AwaitingRefresh == true ? "Waiting for link" : "Stopped",
        DownloadStatus.Finished => Record.ChecksumResult?.Contains("not") == true ? "Checksum failed" : "Complete",
        _ => "Error",
    };
    public string Queue => queueName(Record.Queue);
    /// <summary>Status colour (#RRGGBB), the same as the Windows app's.</summary>
    public string StatusColor => Record.Status switch
    {
        DownloadStatus.Downloading or DownloadStatus.Probing => StatusColors.Accent,
        DownloadStatus.Queued => StatusColors.Waiting,
        DownloadStatus.Paused => StatusColors.Idle,
        DownloadStatus.Finished => Record.ChecksumResult?.Contains("not") == true ? StatusColors.Bad : StatusColors.Good,
        _ => StatusColors.Bad,
    };
    public string Added => Record.Added.ToString("d MMM yyyy, HH:mm");
    public string Category => Record.Category.ToString();

    public void Tick()
    {
        // Averaged over the last 6 seconds: video sites deliver in bursts, so a single tick is often 0.
        recent.Enqueue(Record.Received);
        while (recent.Count > 13) recent.Dequeue();
        SpeedBytes = recent.Count > 1 ? Math.Max(0, (recent.Last() - recent.Peek()) / ((recent.Count - 1) * 0.5)) : 0;
        Refresh();
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new(""));

    public static string Bytes(long n)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = n;
        var i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}

/// <summary>The app's colours (Nexonic blue and the status colours).</summary>
public static class StatusColors
{
    public const string Accent = "#1F6FEB";
    public const string Waiting = "#D9821E";
    public const string Idle = "#8B949E";
    public const string Good = "#2DA44E";
    public const string Bad = "#CF222E";
}
