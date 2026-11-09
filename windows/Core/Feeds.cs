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
}

public sealed record FeedItem(string Id, string Title, string Link, DateTime? Published);

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
            items.Add(new FeedItem(id.Trim(), title, link.Trim(), published));
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
    public static async Task<List<FeedItem>> CheckAsync(FeedSubscription f, HttpClient http, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(f.Url.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("The feed address must start with http:// or https://");
        using var res = await Network.SendAsync(http, url, u => new HttpRequestMessage(HttpMethod.Get, u), HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        if (res.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new InvalidDataException("The feed is too large.");
        var items = Parse(await res.Content.ReadAsStringAsync(ct));
        var first = f.LastChecked == null;
        var seen = f.Seen.ToHashSet();
        var fresh = items.Where(i => !seen.Contains(i.Id)).ToList();
        f.Seen.AddRange(fresh.Select(i => i.Id));
        if (f.Seen.Count > MaxSeen) f.Seen.RemoveRange(0, f.Seen.Count - MaxSeen);
        f.LastChecked = DateTime.UtcNow;
        var wanted = first && f.SkipExisting ? new List<FeedItem>() : fresh.Where(i => Wanted(f, i)).ToList();
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
