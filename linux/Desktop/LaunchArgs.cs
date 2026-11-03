using Nexonic.Core;

namespace Nexonic.Linux;

/// <summary>
/// Links given to the app when it starts: http(s) links, nexonic://add?url=… links and link files opened with the app
/// (.url shortcuts and text files with one link per line). They go to the inbox, like the ndm command's downloads.
/// </summary>
public static class LaunchArgs
{
    const int MaxLinks = 1000;

    public static List<string> Links(IEnumerable<string> args)
    {
        var links = new List<string>();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal)) continue;
            if (arg.StartsWith("nexonic:", StringComparison.OrdinalIgnoreCase)) links.AddRange(FromScheme(arg));
            else if (new DownloadJob(arg).IsValid) links.Add(arg);
            else if (arg.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(arg, UriKind.Absolute, out var f)) links.AddRange(LinksIn(f.LocalPath));
            else if (arg.StartsWith('/')) links.AddRange(LinksIn(arg));
            if (links.Count >= MaxLinks) break;
        }
        return links.Take(MaxLinks).ToList();
    }

    /// <summary>nexonic://add?url=https%3A%2F%2F…&amp;url=… (the QR codes' links) or nexonic:https://….</summary>
    public static List<string> FromScheme(string arg)
    {
        var found = new List<string>();
        if (!Uri.TryCreate(arg, UriKind.Absolute, out var u)) return found;
        foreach (var part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            if (part.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
            {
                var link = Uri.UnescapeDataString(part[4..].Replace('+', ' '));
                if (new DownloadJob(link).IsValid) found.Add(link);
            }
        if (found.Count > 0) return found;
        var rest = arg[(arg.IndexOf(':') + 1)..].TrimStart('/');
        if (new DownloadJob(rest).IsValid) found.Add(rest);
        return found;
    }

    /// <summary>The links in a small file: a .url/.desktop shortcut (URL= line) or text with one link per line.</summary>
    public static List<string> LinksIn(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 2_000_000) return new List<string>();
            var text = File.ReadAllText(path);
            var shortcut = text.Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
            if (shortcut != null) return new DownloadJob(shortcut).IsValid ? new List<string> { shortcut } : new List<string>();
            return Nexonic.Core.Links.Split(text).Where(l => new DownloadJob(l).IsValid).Take(MaxLinks).ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Puts the links into the inbox; the running app picks them up (the Add window or the list).</summary>
    public static int Drop(IEnumerable<string> args)
    {
        var n = 0;
        foreach (var link in Links(args))
        {
            try { new Inbox().Drop(new DownloadJob(link)); n++; } catch { }
        }
        return n;
    }
}
