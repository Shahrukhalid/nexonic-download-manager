namespace Nexonic.Core;

/// <summary>The ndm command: turns command-line arguments into download jobs for the app.</summary>
public static class CommandLineTool
{
    public const string Usage = """
    Nexonic Download Manager command line

      ndm <link> [<link> …] [options]      add one or more downloads
      ndm batch <pattern> [options]        add a numbered series, e.g. "https://example.com/img[001-100].jpg"
      ndm list [--json]                    show the download list (--json: for scripts)
      ndm help                             show this help

    Options:
      --folder <folder>     save to this folder
      --name <file name>    file name (one link only)
      --queue <name>        add to this queue (it is created if needed)
      --later               add without starting
      --referrer <link>     page the link came from
      --user <name>         user name for password-protected downloads
      --password <pass>     password for password-protected downloads
      --checksum <value>    expected MD5, SHA-1, SHA-256 or SHA-512 (checked when finished)
      --limit <KB/s>        speed limit for these downloads
      --audio               video sites: save the sound only
      --json                print the result as JSON (for scripts)

    IDM-style switches work too:
      /d <link>  /p <folder>  /f <file name>  /a (add without starting)
      /n /q /h /s are accepted and ignored (the app never asks questions for added downloads).
    """;

    public sealed record Outcome(bool Help, List<DownloadJob> Jobs, string? Error, bool List = false, bool Json = false);

    /// <summary>True when the arguments are a command rather than a browser starting the helper.</summary>
    public static bool IsCommand(string[] args)
    {
        if (args.Length == 0) return false;
        var first = args[0].ToLowerInvariant();
        return first.StartsWith("http://") || first.StartsWith("https://") || first is "batch" or "help" or "add" or "list" or "--help" or "-h" or "/?"
            or "/d" or "/p" or "/f" or "/a" or "/n" or "/q" or "/h" or "/s";
    }

    static Outcome Fail(string e) => new(false, new(), e);

    public static Outcome Parse(string[] input)
    {
        var args = input.ToList();
        if (args.Count > 0 && args[0].Equals("add", StringComparison.OrdinalIgnoreCase)) args.RemoveAt(0);
        if (args.Count == 0 || args[0].ToLowerInvariant() is "help" or "--help" or "-h" or "/?") return new(true, new(), null);
        var json = args.Remove("--json");
        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            return args.Count == 1 ? new(false, new(), null, List: true, Json: json) : Fail("list takes no other options than --json");
        var links = new List<string>();
        string? batch = null;
        var t = new DownloadJob("");
        var i = 0;
        if (args[0].Equals("batch", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 2) return Fail("batch needs a pattern");
            batch = args[1];
            i = 2;
        }
        while (i < args.Count)
        {
            var a = args[i];
            // IDM's switches: /d link, /p folder, /f file name, /a = add without starting; /n /q /h /s need nothing here.
            if (a.Length == 2 && a[0] == '/' && "dpfanqhsDPFANQHS".Contains(a[1]))
            {
                var s = char.ToLowerInvariant(a[1]);
                if (s is 'n' or 'q' or 'h' or 's') { i++; continue; }
                if (s == 'a') { t = t with { Start = false }; i++; continue; }
                if (i + 1 >= args.Count) return Fail($"{a} needs a value");
                var v = args[i + 1];
                if (s == 'd') links.Add(v);
                else if (s == 'p') t = t with { Folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(v)) };
                else t = t with { FileName = v };
                i += 2;
                continue;
            }
            if (a.StartsWith("--"))
            {
                if (a == "--later") { t = t with { Start = false }; i++; continue; }
                if (a == "--audio") { t = t with { AudioOnly = true }; i++; continue; }
                if (i + 1 >= args.Count) return Fail($"{a} needs a value");
                var v = args[i + 1];
                switch (a)
                {
                    case "--folder": t = t with { Folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(v)) }; break;
                    case "--name": t = t with { FileName = v }; break;
                    case "--queue": t = t with { Queue = v }; break;
                    case "--referrer": t = t with { Referrer = v }; break;
                    case "--user": t = t with { Username = v }; break;
                    case "--password": t = t with { Password = v }; break;
                    case "--checksum":
                        if (Checksum.Parse(v) == null) return Fail($"{v} is not an MD5, SHA-1, SHA-256 or SHA-512 value");
                        t = t with { Checksum = v };
                        break;
                    case "--limit":
                        if (!int.TryParse(v, out var n) || n < 0) return Fail("--limit needs a number of KB/s");
                        t = t with { SpeedLimitKBps = n };
                        break;
                    default: return Fail($"Unknown option {a}");
                }
                i += 2;
            }
            else { links.Add(a); i++; }
        }
        if (batch != null)
        {
            try { links.AddRange(BatchPattern.Expand(batch)); } catch (ArgumentException e) { return Fail(e.Message); }
        }
        if (links.Count == 0) return Fail("No links given");
        if (t.FileName != null && links.Count > 1) return Fail("--name works with one link only");
        var jobs = new List<DownloadJob>();
        foreach (var link in links)
        {
            var job = t with { Url = link };
            if (!job.IsValid) return Fail($"Not a web link: {link}");
            jobs.Add(job);
        }
        return new(false, jobs, null, Json: json);
    }

    /// <summary>The download list for <c>ndm list</c>: no cookies, passwords or private settings.</summary>
    public static object ListItem(DownloadRecord r) => new
    {
        id = r.Id, url = r.Url, fileName = r.FileName, folder = ListColumns.Folder(r), status = r.Status.ToString().ToLowerInvariant(),
        size = r.Size, received = r.Received, progress = Math.Round(r.Progress * 100, 1), queue = r.Queue,
        added = r.Added, finished = r.Finished, error = r.Error,
    };

    public static string ListJson(IEnumerable<DownloadRecord> list) =>
        System.Text.Json.JsonSerializer.Serialize(list.Select(ListItem), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public static string ListText(IEnumerable<DownloadRecord> list)
    {
        var lines = list.Select(r => $"{r.Status,-11} {Math.Round(r.Progress * 100),3}%  {r.FileName}").ToList();
        return lines.Count == 0 ? "No downloads." : string.Join(Environment.NewLine, lines);
    }

    /// <summary>Splits a command such as "C:\Scan Tool\scan.exe" /quiet into words (quotes group words).</summary>
    public static List<string> Words(string? text)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;
        var has = false;
        foreach (var ch in text ?? "")
        {
            if (quote is { } q) { if (ch == q) quote = null; else current.Append(ch); }
            else if (ch is '"' or '\'') { quote = ch; has = true; }
            else if (char.IsWhiteSpace(ch))
            {
                if (has || current.Length > 0) result.Add(current.ToString());
                current.Clear();
                has = false;
            }
            else current.Append(ch);
        }
        if (has || current.Length > 0) result.Add(current.ToString());
        return result;
    }
}
