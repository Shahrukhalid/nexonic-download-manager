using Nexonic.Core;

#if NEXONIC_LINUX
namespace Nexonic.Linux;
#else
namespace Nexonic.App;
#endif

/// <summary>Profile Batch: the found videos go into a queue of their own; saved profiles are checked for new ones.</summary>
public sealed partial class DownloadManager
{
    public List<SavedProfile> Profiles { get; private set; } = SavedProfiles.Load();
    readonly HashSet<string> checking = new();

    /// <summary>
    /// Adds the chosen videos to the queue "owner" (made with its own limit when new) in quality <paramref name="quality"/>,
    /// each into <paramref name="folder"/>; starts that queue when <paramref name="start"/>. Returns how many were added.
    /// </summary>
    public int QueueBatch(string owner, IEnumerable<BatchEntry> take, string folder, string quality, int maxActive, bool start)
    {
        var (height, audio) = ProfileBatch.Quality(quality);
        var queueId = QueueIdNamed(owner.Length > 0 ? owner : "Profile Batch");
        var q = Queues.First(x => x.Id == queueId);
        if (!q.IsMain) { q.MaxActive = Math.Clamp(maxActive, 1, 10); q.Running = start; }
        SaveQueues();
        var n = 0;
        foreach (var e in take)
        {
            var name = e.Title.Length > 0 && e.Title != e.Url ? FileNames.Sanitize(e.Title) : null;
            // A gallery engine listing gives the file itself: downloaded as it is, not by the video engine.
            var job = e.Direct ? new DownloadJob(e.Url, name) { Folder = folder, Start = start }
                : new DownloadJob(e.Url, name) { Site = true, MaxHeight = audio ? null : height, AudioOnly = audio, Folder = folder, Start = start };
            var options = e.Direct ? new AddOptions { QueueId = queueId, Folder = folder, Start = start }
                : new AddOptions { QueueId = queueId, Folder = folder, Start = start, AudioOnly = audio, MaxHeight = height };
            if (Add(job, options) != null) n++;
        }
        Save();
        Schedule();
        return n;
    }

    /// <summary>
    /// The browser's cookies for profiles the extension opened in Profile Batch (Download all), by profile address; kept in
    /// memory only, for listing that profile as the user sees it.
    /// </summary>
    public Dictionary<string, (string? Cookies, string? UserAgent)> ProfileCookies { get; } = new();

    /// <summary>The extension's Download all: open Profile Batch with this profile.</summary>
    public event Action<string>? ProfileRequested;

    /// <summary>
    /// The extension's requests that are not single downloads: a profile for Profile Batch (listed with the browser's
    /// cookies, then the user picks Download Now or Add to Queue), or one post for the gallery engine. True when handled.
    /// </summary>
    bool TakeBrowserRequest(DownloadJob j)
    {
        if (j.Profile == true)
        {
            ProfileCookies[ProfileBatch.Key(j.Url)] = (j.Cookies, j.UserAgent);
            ProfileRequested?.Invoke(j.Url);
            return true;
        }
        if (j.Gallery == true)
        {
            Add(j with { FromBrowser = null }, new AddOptions { Gallery = true });
            return true;
        }
        return false;
    }

    public bool AlreadyDownloaded(string url) =>
        Rows.Any(r => string.Equals(ProfileBatch.Key(r.Record.Url), ProfileBatch.Key(url), StringComparison.OrdinalIgnoreCase));

    public void SaveProfiles() => SavedProfiles.Save(Profiles);

    /// <summary>Checks saved profiles whose time has come (called every minute); new videos are added and start.</summary>
    void CheckProfiles()
    {
        var now = DateTime.Now;
        foreach (var p in Profiles.Where(p => p.Due(now) && !checking.Contains(p.Id)).ToList())
        {
            checking.Add(p.Id);
            var proxy = Settings.Proxy;
            Task.Run(() =>
            {
                ProfileBatch.Scan? scan = null;
                string? problem = null;
                try { scan = ProfileBatch.Discover(p.Url, proxy, p.Filter.Max, fresh: true); }
                catch (Exception e) { problem = e.Message; }
                OnUi(() =>
                {
                    checking.Remove(p.Id);
                    p.LastChecked = DateTime.Now;
                    if (scan == null) { p.LastResult = problem ?? ""; SaveProfiles(); return; }
                    var seen = new HashSet<string>(p.Seen);
                    var (take, counts) = ProfileBatch.Choose(scan.Entries, p.Filter, AlreadyDownloaded, seen);
                    var added = take.Count > 0 ? QueueBatch(p.Title.Length > 0 ? p.Title : scan.Owner, take, p.Folder, p.Quality, 2, true) : 0;
                    foreach (var e in scan.Entries) if (seen.Add(ProfileBatch.Key(e.Url))) p.Seen.Add(ProfileBatch.Key(e.Url));
                    p.LastResult = counts with { Queued = added } is var c ? c.Text : "";
                    SaveProfiles();
                    if (added > 0) Notify?.Invoke(L.T("New videos"), $"{p.Title}: {added}");
                });
            });
        }
    }

#if NEXONIC_LINUX
    void OnUi(Action a) => post(a);
#else
    static void OnUi(Action a) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(a);
#endif
}
