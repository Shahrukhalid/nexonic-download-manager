using System.Xml.Linq;

namespace Nexonic.Core;

/// <summary>
/// Multi-source downloads: the same file from several servers at once. Each part goes to the source that has measured the
/// highest speed so far (sources not measured yet get a part first). A mirror that fails twice is dropped; the original
/// address is never dropped, so the download always has somewhere to continue from.
/// </summary>
public sealed class MirrorPool
{
    sealed class Source(Uri url, bool primary)
    {
        public readonly Uri Url = url;
        public readonly bool Primary = primary;
        public long Bytes;
        public double Seconds;
        public int Active;
        public int Failures;
        public bool Dropped;
        public double Rate => Seconds > 0.2 ? Bytes / Seconds : 0;
    }

    readonly List<Source> sources = new();
    readonly object gate = new();

    public MirrorPool(Uri primary, IEnumerable<Uri>? mirrors = null)
    {
        sources.Add(new Source(primary, true));
        foreach (var m in mirrors ?? Enumerable.Empty<Uri>())
            if (sources.All(s => s.Url != m)) sources.Add(new Source(m, false));
    }

    public int Count { get { lock (gate) return sources.Count(s => !s.Dropped); } }

    /// <summary>The source for the next part: an unmeasured one first, else the best measured speed per busy connection.</summary>
    public Uri Take()
    {
        lock (gate)
        {
            var live = sources.Where(s => !s.Dropped).ToList();
            var pick = live.FirstOrDefault(s => s.Seconds == 0 && s.Active == 0)
                       ?? live.OrderByDescending(s => s.Rate / (s.Active + 1)).ThenBy(s => s.Active).First();
            pick.Active++;
            return pick.Url;
        }
    }

    /// <summary>A part from <paramref name="url"/> stopped (finished, failed or paused) after this many bytes in this time.</summary>
    public void Done(Uri url, long bytes, TimeSpan time, bool failed)
    {
        lock (gate)
        {
            if (sources.FirstOrDefault(s => s.Url == url) is not { } s) return;
            s.Active = Math.Max(0, s.Active - 1);
            s.Bytes += Math.Max(0, bytes);
            s.Seconds += Math.Max(0, time.TotalSeconds);
            if (!failed) { s.Failures = 0; return; }
            if (++s.Failures >= 2 && !s.Primary) s.Dropped = true;
        }
    }

    /// <summary>Drops a mirror at once (it sent a different file).</summary>
    public void Drop(Uri url) { lock (gate) foreach (var s in sources) if (s.Url == url && !s.Primary) s.Dropped = true; }

    /// <summary>Each source with the bytes it sent, its measured speed (bytes per second) and whether it is still used.</summary>
    public List<(Uri Url, long Bytes, double BytesPerSecond, bool Used)> Report()
    {
        lock (gate) return sources.Select(s => (s.Url, s.Bytes, s.Rate, !s.Dropped)).ToList();
    }

    /// <summary>The extra addresses a user typed: web addresses only, no duplicates of the main one, at most 16.</summary>
    public static List<string> Clean(IEnumerable<string>? lines, string primary) =>
        (lines ?? Enumerable.Empty<string>()).Select(l => l.Trim())
            .Where(l => Uri.TryCreate(l, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "ftp" && l != primary)
            .Distinct().Take(16).ToList();
}

/// <summary>Metalink files (.meta4 and the older .metalink): one file with its size, checksum and mirrors.</summary>
public static class Metalink
{
    public sealed record Entry(string Name, long? Size, string? Checksum, List<string> Urls);

    /// <summary>The files in a Metalink document; an empty list when it is not one. Hashes become "sha256:…" and the like
    /// (the strongest one given), so the finished file is checked against it.</summary>
    public static List<Entry> Parse(string xml)
    {
        var result = new List<Entry>();
        XDocument doc;
        try
        {
            // No DTDs: a Metalink file has none, and refusing them rules out entity expansion and outside references.
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 };
            using var reader = System.Xml.XmlReader.Create(new StringReader(xml), settings);
            doc = XDocument.Load(reader);
        }
        catch (System.Xml.XmlException) { return result; }
        if (doc.Root is not { } root || root.Name.LocalName != "metalink") return result;
        foreach (var file in root.Descendants().Where(e => e.Name.LocalName == "file"))
        {
            var name = FileNames.Sanitize(Path.GetFileName((string?)file.Attribute("name") ?? ""));
            long? size = long.TryParse(Child(file, "size")?.Value, out var sz) && sz >= 0 ? sz : null;
            var hashes = file.Descendants().Where(e => e.Name.LocalName == "hash" && e.Parent?.Name.LocalName is "file" or "verification")
                .Select(h => (Type: ((string?)h.Attribute("type") ?? "").ToLowerInvariant().Replace("-", ""), Hex: h.Value.Trim().ToLowerInvariant()))
                .Where(h => h.Hex.Length > 0 && h.Hex.All(Uri.IsHexDigit)).ToList();
            string? checksum = null;
            foreach (var t in new[] { "sha512", "sha256", "sha1", "md5" })
                if (hashes.FirstOrDefault(h => h.Type == t) is { Hex.Length: > 0 } best) { checksum = $"{t}:{best.Hex}"; break; }
            // Both formats: <url priority="1">…</url> (v4: lower first) and <resources><url preference="100">…</url> (v3: higher first).
            var urls = file.Descendants().Where(e => e.Name.LocalName == "url")
                .Select(u => (Url: u.Value.Trim(),
                              Order: int.TryParse((string?)u.Attribute("priority"), out var p) ? p
                                   : int.TryParse((string?)u.Attribute("preference"), out var pr) ? 1000 - pr : 500))
                .Where(u => Uri.TryCreate(u.Url, UriKind.Absolute, out var x) && x.Scheme is "http" or "https" or "ftp")
                .OrderBy(u => u.Order).Select(u => u.Url).Distinct().Take(17).ToList();
            if (urls.Count > 0) result.Add(new Entry(name.Length > 0 ? name : FileNames.FromUrl(urls[0]), size, checksum, urls));
        }
        return result;
    }

    static XElement? Child(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name);

    public static bool Looks(string? fileName) =>
        fileName is { } f && (f.EndsWith(".meta4", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".metalink", StringComparison.OrdinalIgnoreCase));
}
