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
            // A post from the page (Instagram): the gallery engine downloads its video or photos.
            var post = e.Kind == "post";
            // A post goes with the sign-in the extension handed over for that site, as the browser would open it.
            var signedIn = post ? ProfileCookies.FirstOrDefault(c => Host(c.Key) == Host(e.Url)).Value : default;
            var job = post ? new DownloadJob(e.Url, null, null, signedIn.Cookies, signedIn.UserAgent) { Gallery = true, Folder = folder, Start = start }
                : e.Direct ? new DownloadJob(e.Url, name) { Folder = folder, Start = start }
                : new DownloadJob(e.Url, name) { Site = true, MaxHeight = audio ? null : height, AudioOnly = audio, Folder = folder, Start = start };
            var options = post ? new AddOptions { QueueId = queueId, Folder = folder, Start = start, Gallery = true }
                : e.Direct ? new AddOptions { QueueId = queueId, Folder = folder, Start = start }
                : new AddOptions { QueueId = queueId, Folder = folder, Start = start, AudioOnly = audio, MaxHeight = height };
            if (Add(job, options) != null) n++;
        }
        Save();
        Schedule();
        return n;
    }

    static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";

    /// <summary>
    /// The browser's cookies for profiles the extension opened in Profile Batch (Download all), by profile address; kept in
    /// memory only, for listing that profile as the user sees it.
    /// </summary>
    public Dictionary<string, (string? Cookies, string? UserAgent)> ProfileCookies { get; } = new();

    /// <summary>The extension's Download all: open Profile Batch with this profile (also when its background listing is done).</summary>
    public event Action<string>? ProfileRequested;

    /// <summary>A background listing ended (its profile address): an open Profile Batch window shows the result.</summary>
    public event Action<string>? ListingDone;

    /// <summary>Profile addresses with a Profile Batch window open (the window keeps this up to date).</summary>
    public HashSet<string> ProfileWindows { get; } = new();

    /// <summary>Profiles being listed (or listed) with those cookies, by profile address (see <see cref="StartListing"/>).</summary>
    public Dictionary<string, ProfileListing> Listings { get; } = new();

    /// <summary>
    /// Starts listing a profile with the gallery engine and the browser's cookies, in the background: the Profile Batch
    /// window may close meanwhile. <paramref name="seed"/> (the page's own posts, from the extension) is shown at once. When
    /// the user already chose in Profile Batch, what is found is added that way; otherwise Profile Batch opens again with it
    /// (or with what visitors see, when the engine can't read the site). Call on the UI thread.
    /// </summary>
    public async void StartListing(string url, int max, List<BatchEntry>? seed = null)
    {
        var key = ProfileBatch.Key(url);
        if (Listings.TryGetValue(key, out var busy) && busy.Running) return;
        var social = SocialPages.Find(url);
        var listing = Listings[key] = new ProfileListing { Url = url, Running = true };
        seed ??= new();
        if (seed.Count > 0) listing.Scan = new ProfileBatch.Scan { Url = url, Owner = social?.Owner ?? "", Entries = seed };
        if (!ProfileCookies.TryGetValue(key, out var signedIn)) { listing.Running = false; return; }
        var proxy = HostProfiles.Match(Settings.HostProfiles, url)?.Proxy ?? Settings.Proxy;
        (List<BatchEntry> Entries, string Owner)? found = null;
        var problem = "";
        try { found = await GalleryList.ListAsync(url, signedIn.Cookies, signedIn.UserAgent, proxy, max, listing.Stop.Token); }
        catch (OperationCanceledException) { listing.Stopped = true; }
        catch (Exception e) when (e is System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { problem = e.Message; }
        listing.Running = false;
        if (listing.Stopped) return;
        if (found is { } listed)
        {
            // The profile's name (its folder): Instagram's or Facebook's owner, else the account the files belong to.
            var owner = social?.Owner ?? listed.Owner;
            // A post the page already gave is not listed twice.
            var posts = seed.Where(e => e.Kind == "post").Select(e => e.Id).ToHashSet();
            var fresh = listed.Entries.Where(e => e.Id.Length == 0 || !posts.Contains(e.Id)).ToList();
            var all = seed.Concat(fresh).ToList();
            var videos = all.Count(x => x.Kind is "video" or "shorts");
            listing.Scan = new ProfileBatch.Scan { Url = url, Owner = owner, Entries = all, Complete = true,
                Note = string.Format(L.T("Found {0} videos and {1} photos with your browser's sign-in."), videos, all.Count - videos) };
            Notify?.Invoke(L.T("Profile Batch"), (owner.Length > 0 ? owner + ": " : "") + string.Format(L.T("{0} videos and {1} photos found"), videos, all.Count - videos));
            if (listing.Follow is { } f)
            {
                // Chosen in Profile Batch while it searched: the rest goes the same way.
                var take = fresh.Where(e => !AlreadyDownloaded(e.Url)).ToList();
                if (take.Count > 0) QueueBatch(owner, take, f.Folder, f.Quality, f.Parallel, f.Start);
                ListingDone?.Invoke(key);
                return;
            }
        }
        else if (seed.Count > 0)
        {
            listing.Scan = new ProfileBatch.Scan { Url = url, Owner = social?.Owner ?? "", Entries = seed, Complete = true,
                Note = string.Format(L.T("Only the posts the page showed: {0}"), problem) };
            if (listing.Follow != null) { ListingDone?.Invoke(key); return; }
        }
        else
        {
            listing.VisitorsOnly = true;  // the engine can't read this site or address
            ProfileCookies.Remove(key);
        }
        ListingDone?.Invoke(key);
        if (!ProfileWindows.Contains(key)) ProfileRequested?.Invoke(url);
    }

    /// <summary>
    /// The extension's requests that are not single downloads: Download all (Profile Batch opens with the page's own posts
    /// while the rest is found with the browser's cookies; the user picks the folder and quality), or one post for the
    /// gallery engine. True when handled.
    /// </summary>
    bool TakeBrowserRequest(DownloadJob j)
    {
        if (j.Profile == true)
        {
            ProfileCookies[ProfileBatch.Key(j.Url)] = (j.Cookies, j.UserAgent);
            var key = ProfileBatch.Key(j.Url);
            StartListing(j.Url, ProfileBatch.Limit, ProfileBatch.PageEntries(j.Links));
            if (ProfileWindows.Contains(key)) ListingDone?.Invoke(key);  // its window, already open, shows them
            else ProfileRequested?.Invoke(j.Url);
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
    /// <summary>What the user chose in Profile Batch while it searched: what is found is added the same way.</summary>
    public (string Folder, string Quality, int Parallel, bool Start)? Follow { get; set; }
    public System.Threading.CancellationTokenSource Stop { get; } = new();
}
