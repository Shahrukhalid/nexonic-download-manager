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

    /// <summary>The extension's Download all: open Profile Batch with this profile (also when its background listing is done).</summary>
    public event Action<string>? ProfileRequested;

    /// <summary>Profiles being listed (or listed) with those cookies, by profile address (see <see cref="StartListing"/>).</summary>
    public Dictionary<string, ProfileListing> Listings { get; } = new();

    /// <summary>
    /// Starts listing a profile with the gallery engine and the browser's cookies, in the background: the Profile Batch
    /// window may close meanwhile. When it is done Nexonic says so and opens Profile Batch again with what was found (or
    /// with what visitors see, when the engine can't read the site). Call on the UI thread.
    /// </summary>
    public async void StartListing(string url, int max)
    {
        var key = ProfileBatch.Key(url);
        if (!ProfileCookies.TryGetValue(key, out var signedIn) || Listings.TryGetValue(key, out var busy) && busy.Running) return;
        var listing = Listings[key] = new ProfileListing { Url = url, Running = true };
        var proxy = HostProfiles.Match(Settings.HostProfiles, url)?.Proxy ?? Settings.Proxy;
        (List<BatchEntry> Entries, string Owner)? found = null;
        try { found = await GalleryList.ListAsync(SocialPages.Listable(url), signedIn.Cookies, signedIn.UserAgent, proxy, max, listing.Stop.Token); }
        catch (OperationCanceledException) { listing.Stopped = true; }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        listing.Running = false;
        if (listing.Stopped) return;
        if (found is { } listed)
        {
            var videos = listed.Entries.Count(x => x.Kind == "video");
            // The profile's name (its folder): Instagram's or Facebook's owner, else the account the files belong to.
            var owner = SocialPages.Find(url) is { } social ? social.Owner : listed.Owner;
            listing.Scan = new ProfileBatch.Scan { Url = url, Owner = owner, Entries = listed.Entries, Complete = true,
                Note = string.Format(L.T("Found {0} videos and {1} photos with your browser's sign-in."), videos, listed.Entries.Count - videos) };
            Notify?.Invoke(L.T("Profile Batch"), (owner.Length > 0 ? owner + ": " : "") + string.Format(L.T("{0} videos and {1} photos found"), videos, listed.Entries.Count - videos));
        }
        else
        {
            listing.VisitorsOnly = true;  // the engine can't read this site or address
            ProfileCookies.Remove(key);
        }
        ProfileRequested?.Invoke(url);
    }

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

/// <summary>A profile listed in the background with the browser's cookies (the extension's Download all).</summary>
public sealed class ProfileListing
{
    public string Url { get; init; } = "";
    public bool Running { get; set; }
    public bool Stopped { get; set; }
    public bool VisitorsOnly { get; set; }
    public ProfileBatch.Scan? Scan { get; set; }
    public CancellationTokenSource Stop { get; } = new();
}
