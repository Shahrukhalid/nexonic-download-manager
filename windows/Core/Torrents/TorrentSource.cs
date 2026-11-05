namespace Nexonic.Core;

/// <summary>Resolves HTTP descriptors to bounded bytes; never treats a descriptor as completed torrent content.</summary>
public static class TorrentSource
{
    public const int MaximumMetadataBytes = 8 * 1024 * 1024;
    public static async Task<byte[]> ReadMetadataAsync(string source, CancellationToken cancellation = default)
    {
        Stream input;
        HttpResponseMessage? response = null;
        using var handler = Network.Handler(new ProxySettings { Mode = ProxyMode.None });
        handler.UseCookies = false;
        handler.Credentials = null;
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                response = await Network.SendAsync(http, uri, u => new HttpRequestMessage(HttpMethod.Get, u), HttpCompletionOption.ResponseHeadersRead, cancellation);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumMetadataBytes) throw new InvalidDataException("Torrent metadata exceeds 8 MiB.");
                input = await response.Content.ReadAsStreamAsync(cancellation);
            }
            else
            {
                if (!Path.IsPathFullyQualified(source) || !source.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Choose a local .torrent file or an HTTP(S) torrent link.");
                input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            }
            await using (input)
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    if (output.Length + count > MaximumMetadataBytes) throw new InvalidDataException("Torrent metadata exceeds 8 MiB.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                }
                return output.ToArray(); // Native parsing validates bencoding, paths, hashes and file count.
            }
        }
        finally { response?.Dispose(); }
    }
}

/// <summary>Torrents handed to the app from outside: a .torrent file opened from the file manager or a magnet link
/// clicked in a browser. A second start of the app drops them here and wakes the running copy, which shows the Add
/// Torrent window for each.</summary>
public static class TorrentInbox
{
    static string Folder => Path.Combine(AppPaths.Support, "Torrent inbox");

    /// <summary>A magnet link or a local .torrent file (path or file:// link), normalised; null for anything else.</summary>
    public static string? Source(string arg)
    {
        var text = (arg ?? "").Trim();
        if (text.Length is 0 or > 32768) return null;
        if (text.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return text;
        if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(text, UriKind.Absolute, out var u) && u.IsFile) text = u.LocalPath;
        return Path.IsPathFullyQualified(text) && text.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(text) ? text : null;
    }

    /// <summary>Saves the torrents among the arguments for the running app; returns how many.</summary>
    public static int Drop(IEnumerable<string> args)
    {
        var n = 0;
        foreach (var source in args.Select(Source).OfType<string>().Take(20))
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var tmp = Path.Combine(Folder, $"{DateTime.UtcNow.Ticks:D20}-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(tmp, source);
                File.Move(tmp, Path.ChangeExtension(tmp, ".txt"));
                n++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return n;
    }

    /// <summary>Takes the waiting torrents, oldest first.</summary>
    public static List<string> Collect()
    {
        var found = new List<string>();
        try
        {
            if (!Directory.Exists(Folder)) return found;
            foreach (var file in Directory.GetFiles(Folder, "*.txt").OrderBy(f => f, StringComparer.Ordinal).Take(20))
            {
                try
                {
                    var text = new FileInfo(file).Length <= 40000 ? File.ReadAllText(file) : "";
                    File.Delete(file);
                    if (Source(text) is { } source) found.Add(source);
                }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        return found;
    }
}
