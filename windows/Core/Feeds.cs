using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Nexonic.Core;

/// <summary>An RSS or Atom feed whose new items are downloaded when they match the rule.</summary>
public sealed class FeedSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Minutes between checks (15 or more).</summary>
    public int IntervalMinutes { get; set; } = 30;
    /// <summary>Words that must all be in the title (empty = every item), or /regular expression/.</summary>
    public string Include { get; set; } = "";
    /// <summary>Items whose title has any of these words (or matches /regular expression/) are skipped.</summary>
    public string Exclude { get; set; } = "";
    /// <summary>Save folder (empty = the usual folder).</summary>
    public string Folder { get; set; } = "";
    /// <summary>Only items published after the feed was added are downloaded; earlier ones are only remembered.</summary>
    public bool SkipExisting { get; set; } = true;
    public DateTime? LastChecked { get; set; }
    public string LastResult { get; set; } = "";
    /// <summary>Items already seen (their id or link), newest last.</summary>
    public List<string> Seen { get; set; } = new();
    /// <summary>Size limits in MB (0 = none); items whose size the feed does not give pass.</summary>
    public int MinSizeMB { get; set; }
    public int MaxSizeMB { get; set; }
    /// <summary>Each episode (S01E02, 1x02, a date) is downloaded once, in the best quality the check offers.</summary>
    public bool OneOfEachEpisode { get; set; }
    /// <summary>Qualities in order of preference ("2160p 1080p 720p"); when set, other qualities are skipped.</summary>
    public string Quality { get; set; } = "";
    /// <summary>Episodes already downloaded ("show name|s01e02"), newest last.</summary>
    public List<string> Episodes { get; set; } = new();
}

public sealed record FeedItem(string Id, string Title, string Link, DateTime? Published, long? Size = null);

public static class Feeds
{
    public const int MaxSeen = 5000;
    public static string FilePath => Path.Combine(AppPaths.Support, "feeds.json");

    public static List<FeedSubscription> Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<FeedSubscription>>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    public static void Save(List<FeedSubscription> feeds)
    {
        Directory.CreateDirectory(AppPaths.Support);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(feeds, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>The items of an RSS 2.0, RSS 1.0 or Atom feed. The download link is the enclosure (a file or torrent),
    /// else a magnet link, else the item's link.</summary>
    public static List<FeedItem> Parse(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024 };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var doc = XDocument.Load(reader);
        var items = new List<FeedItem>();
        foreach (var e in doc.Descendants().Where(d => d.Name.LocalName is "item" or "entry"))
        {
            string? Child(string name) => e.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value.Trim();
            var title = Child("title") ?? "";
            var enclosure = e.Elements().FirstOrDefault(c => c.Name.LocalName == "enclosure")?.Attribute("url")?.Value;
            var magnet = e.Descendants().FirstOrDefault(c => c.Name.LocalName == "magnetURI")?.Value.Trim();
            var atomLinks = e.Elements().Where(c => c.Name.LocalName == "link" && c.Attribute("href") != null).ToList();
            var atomEnclosure = atomLinks.FirstOrDefault(l => l.Attribute("rel")?.Value == "enclosure")?.Attribute("href")?.Value;
            var atomLink = atomLinks.FirstOrDefault(l => l.Attribute("rel") is null || l.Attribute("rel")!.Value == "alternate")?.Attribute("href")?.Value;
            var link = new[] { enclosure, atomEnclosure, magnet, atomLink, Child("link") }
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && (l!.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)
                    || Uri.TryCreate(l, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"));
            if (link == null) continue;
            var date = Child("pubDate") ?? Child("published") ?? Child("updated") ?? Child("date");
            DateTime? published = DateTimeOffset.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;
            var id = Child("guid") ?? Child("id") ?? link;
            // Size: the enclosure's length, or the torrent extensions' contentLength / size.
            long? size = long.TryParse(e.Elements().FirstOrDefault(c => c.Name.LocalName == "enclosure")?.Attribute("length")?.Value, out var el) && el > 0 ? el
                : long.TryParse(e.Descendants().FirstOrDefault(c => c.Name.LocalName is "contentLength" or "contentlength")?.Value.Trim(), out var cl) && cl > 0 ? cl
                : Feeds.ParseSize(e.Descendants().FirstOrDefault(c => c.Name.LocalName == "size")?.Value);
            items.Add(new FeedItem(id.Trim(), title, link.Trim(), published, size));
        }
        return items;
    }

    static bool Matches(string title, string rule, bool any)
    {
        rule = rule.Trim();
        if (rule.Length > 2 && rule[0] == '/' && rule[^1] == '/')
        {
            try { return Regex.IsMatch(title, rule[1..^1], RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)); }
            catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException) { return false; }
        }
        var words = rule.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return any ? words.Any(w => title.Contains(w, StringComparison.OrdinalIgnoreCase))
                   : words.All(w => title.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the item's title passes the feed's include and exclude rules.</summary>
    public static bool Wanted(FeedSubscription f, FeedItem item) =>
        (f.Include.Trim().Length == 0 || Matches(item.Title, f.Include, any: false))
        && (f.Exclude.Trim().Length == 0 || !Matches(item.Title, f.Exclude, any: true));

    /// <summary>"1.4 GiB", "700 MB", "123456" → bytes; null when unreadable.</summary>
    public static long? ParseSize(string? text)
    {
        var m = Regex.Match((text ?? "").Trim(), @"^(\d+(?:[.,]\d+)?)\s*([KMGT]i?B|B)?$", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return null;
        var unit = m.Groups[2].Value.ToUpperInvariant();
        double mult = unit.StartsWith('K') ? 1024 : unit.StartsWith('M') ? 1024d * 1024 : unit.StartsWith('G') ? 1024d * 1024 * 1024
            : unit.StartsWith('T') ? 1024d * 1024 * 1024 * 1024 : 1;
        return v > 0 ? (long)(v * mult) : null;
    }

    static readonly Regex EpisodeSE = new(@"\b[Ss](\d{1,2})[ ._-]?[Ee](\d{1,4})\b|\b(\d{1,2})x(\d{2,3})\b", RegexOptions.Compiled);
    static readonly Regex EpisodeDate = new(@"\b(20\d{2})[ ._-](\d{2})[ ._-](\d{2})\b", RegexOptions.Compiled);
    static readonly Regex QualityTag = new(@"\b(4320p|2160p|1440p|1080p|720p|576p|480p|360p|4k|8k|uhd)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>"show name|s01e02" (or "show name|2026-11-10"), or null when the title names no episode.</summary>
    public static string? EpisodeKey(string title)
    {
        var m = EpisodeSE.Match(title);
        string? ep = null;
        var at = -1;
        if (m.Success)
        {
            var season = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value);
            var episode = int.Parse(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Value);
            ep = $"s{season:00}e{episode:00}";
            at = m.Index;
        }
        else if (EpisodeDate.Match(title) is { Success: true } d)
        {
            ep = $"{d.Groups[1].Value}-{d.Groups[2].Value}-{d.Groups[3].Value}";
            at = d.Index;
        }
        if (ep == null) return null;
        // The show: the words before the episode, without brackets ("[Group] Show Name - S01E02" → "show name").
        var show = Regex.Replace(title[..at], @"\[[^\]]*\]|\([^)]*\)", " ");
        show = Regex.Replace(show.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim();
        return show.Length > 0 ? show + "|" + ep : null;
    }

    /// <summary>The quality named in a title ("1080p"; 4k/uhd count as 2160p), lowercase, or "".</summary>
    public static string QualityOf(string title)
    {
        var q = QualityTag.Match(title).Value.ToLowerInvariant();
        return q is "4k" or "uhd" ? "2160p" : q == "8k" ? "4320p" : q;
    }

    public enum Decision { Download, Filtered, TooSmall, TooLarge, OtherQuality, HaveEpisode, BetterQualityInFeed }

    /// <summary>What a check would do with each new item, and why (used for the preview and for the check itself).</summary>
    public static List<(FeedItem Item, Decision Decision)> Decide(FeedSubscription f, IReadOnlyList<FeedItem> items)
    {
        var prefs = f.Quality.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(q => q is "4k" or "uhd" ? "2160p" : q).ToList();
        int Rank(FeedItem i) => prefs.Count == 0 ? 0 : prefs.IndexOf(QualityOf(i.Title)) is var r && r >= 0 ? r : int.MaxValue;
        var have = f.Episodes.ToHashSet();
        var result = new List<(FeedItem, Decision)>();
        var chosen = new Dictionary<string, FeedItem>();
        foreach (var i in items)
        {
            Decision d;
            if (!Wanted(f, i)) d = Decision.Filtered;
            else if (f.MinSizeMB > 0 && i.Size is long s1 && s1 < f.MinSizeMB * 1024L * 1024) d = Decision.TooSmall;
            else if (f.MaxSizeMB > 0 && i.Size is long s2 && s2 > f.MaxSizeMB * 1024L * 1024) d = Decision.TooLarge;
            else if (prefs.Count > 0 && Rank(i) == int.MaxValue) d = Decision.OtherQuality;
            else if (f.OneOfEachEpisode && EpisodeKey(i.Title) is { } key)
            {
                if (have.Contains(key)) d = Decision.HaveEpisode;
                else
                {
                    // One per episode: the best preferred quality among this check's items (the earliest on a tie).
                    if (!chosen.TryGetValue(key, out var best) || Rank(i) < Rank(best)) chosen[key] = i;
                    d = Decision.Download;
                }
            }
            else d = Decision.Download;
            result.Add((i, d));
        }
        for (var n = 0; n < result.Count; n++)
            if (result[n].Item2 == Decision.Download && f.OneOfEachEpisode && EpisodeKey(result[n].Item1.Title) is { } k && chosen[k] != result[n].Item1)
                result[n] = (result[n].Item1, Decision.BetterQualityInFeed);
        return result;
    }

    public static string Explain(Decision d) => d switch
    {
        Decision.Download => "Download",
        Decision.Filtered => "Skipped by the include/exclude rule",
        Decision.TooSmall => "Smaller than the minimum size",
        Decision.TooLarge => "Larger than the maximum size",
        Decision.OtherQuality => "Not one of the chosen qualities",
        Decision.HaveEpisode => "This episode was downloaded before",
        _ => "A better quality of this episode is in the feed",
    };

    /// <summary>A problem with a rule (an invalid regular expression), or null.</summary>
    public static string? RuleProblem(string rule)
    {
        rule = (rule ?? "").Trim();
        if (rule.Length > 2 && rule[0] == '/' && rule[^1] == '/')
            try { _ = new Regex(rule[1..^1]); } catch (ArgumentException) { return "The rule's regular expression is not valid."; }
        return null;
    }

    /// <summary>
    /// Reads the feed and returns the new items to download (marking every new item as seen). The first check of a
    /// feed with SkipExisting only remembers what is already there.
    /// </summary>
    static async Task<List<FeedItem>> FetchAsync(string address, HttpClient http, CancellationToken ct)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("The feed address must start with http:// or https://");
        using var res = await Network.SendAsync(http, url, u => new HttpRequestMessage(HttpMethod.Get, u), HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        if (res.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new InvalidDataException("The feed is too large.");
        return Parse(await res.Content.ReadAsStringAsync(ct));
    }

    /// <summary>What the rules would take from the feed right now, without changing the subscription.</summary>
    public static async Task<List<(FeedItem Item, Decision Decision)>> PreviewAsync(FeedSubscription f, HttpClient http, CancellationToken ct = default) =>
        Decide(f, await FetchAsync(f.Url, http, ct));

    public static async Task<List<FeedItem>> CheckAsync(FeedSubscription f, HttpClient http, CancellationToken ct = default)
    {
        var items = await FetchAsync(f.Url, http, ct);
        var first = f.LastChecked == null;
        var seen = f.Seen.ToHashSet();
        var fresh = items.Where(i => !seen.Contains(i.Id)).ToList();
        f.Seen.AddRange(fresh.Select(i => i.Id));
        if (f.Seen.Count > MaxSeen) f.Seen.RemoveRange(0, f.Seen.Count - MaxSeen);
        f.LastChecked = DateTime.UtcNow;
        var wanted = first && f.SkipExisting ? new List<FeedItem>()
            : Decide(f, fresh).Where(x => x.Decision == Decision.Download).Select(x => x.Item).ToList();
        if (f.OneOfEachEpisode)
        {
            f.Episodes.AddRange(wanted.Select(i => EpisodeKey(i.Title)).OfType<string>());
            if (f.Episodes.Count > MaxSeen) f.Episodes.RemoveRange(0, f.Episodes.Count - MaxSeen);
        }
        f.LastResult = $"{items.Count} items, {wanted.Count} new to download";
        return wanted;
    }

    /// <summary>Feeds whose interval has passed.</summary>
    public static IEnumerable<FeedSubscription> Due(IEnumerable<FeedSubscription> feeds, DateTime now) =>
        feeds.Where(f => f.Enabled && (f.LastChecked is not { } last || now - last >= TimeSpan.FromMinutes(Math.Max(15, f.IntervalMinutes))));
}

/// <summary>Checks the feeds that are due (one check at a time) and hands each new item to the app.</summary>
public sealed class FeedRunner
{
    readonly SemaphoreSlim gate = new(1, 1);
    public List<FeedSubscription> Subscriptions { get; } = Feeds.Load();

    public void Save() { lock (Subscriptions) Feeds.Save(Subscriptions); }

    /// <summary>Checks `which` (default: the feeds that are due) and calls `add` for every new matching item.</summary>
    public async Task CheckAsync(ProxySettings proxy, Func<FeedSubscription, FeedItem, Task> add, IEnumerable<FeedSubscription>? which = null)
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            List<FeedSubscription> due;
            lock (Subscriptions) due = (which ?? Feeds.Due(Subscriptions, DateTime.UtcNow)).ToList();
            if (due.Count == 0) return;
            using var http = new HttpClient(Network.Handler(proxy)) { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NexonicDownloadManager");
            foreach (var f in due)
            {
                try
                {
                    foreach (var item in await Feeds.CheckAsync(f, http))
                    {
                        try { await add(f, item); }
                        catch (Exception e) { f.LastResult = $"{item.Title}: {e.Message}"; }
                    }
                }
                catch (Exception e) { f.LastChecked = DateTime.UtcNow; f.LastResult = "Check failed: " + (e.InnerException ?? e).Message; }
            }
            Save();
        }
        finally { gate.Release(); }
    }
}
