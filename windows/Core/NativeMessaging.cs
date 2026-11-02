using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexonic.Core;

/// <summary>Browser native messaging: 4-byte little-endian length + UTF-8 JSON.</summary>
public static class NativeMessaging
{
    public const string HostName = "com.nexonic.downloadmanager";
    public static readonly string[] AllowedExtensionIds = { "lbmdjnbmkidgmabcjdkhfldclkifidmi" };
    public const string FirefoxAddonId = "{dec29d70-4c6f-43f7-aacd-af2ae04cd4c2}";
    const int MaxMessage = 1 << 20;

    public static JsonObject? Read(Stream input)
    {
        var header = new byte[4];
        if (!ReadExactly(input, header)) return null;
        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > MaxMessage) throw new InvalidDataException("Bad message length");
        var body = new byte[length];
        if (!ReadExactly(input, body)) return null;
        return JsonNode.Parse(body) as JsonObject;
    }

    public static void Write(Stream output, object message)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        output.Write(BitConverter.GetBytes(body.Length));
        output.Write(body);
        output.Flush();
    }

    static bool ReadExactly(Stream s, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = s.Read(buffer, read, buffer.Length - read);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    public static string Manifest(string hostPath) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["name"] = HostName,
        ["description"] = "Nexonic Download Manager",
        ["path"] = hostPath,
        ["type"] = "stdio",
        ["allowed_origins"] = AllowedExtensionIds.Select(id => $"chrome-extension://{id}/").ToArray(),
    }, new JsonSerializerOptions { WriteIndented = true });

    public static string FirefoxManifest(string hostPath) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["name"] = HostName,
        ["description"] = "Nexonic Download Manager",
        ["path"] = hostPath,
        ["type"] = "stdio",
        ["allowed_extensions"] = new[] { FirefoxAddonId },
    }, new JsonSerializerOptions { WriteIndented = true });
}

public sealed record DownloadJob(string Url, string? FileName = null, string? Referrer = null, string? Cookies = null, string? UserAgent = null)
{
    // Optional extras from the command line and the extension
    public string? Folder { get; init; }
    public string? Queue { get; init; }
    public bool? Start { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Checksum { get; init; }
    public int? SpeedLimitKBps { get; init; }
    public bool? Stream { get; init; }
    public bool? Site { get; init; }
    public bool? AudioOnly { get; init; }
    public int? MaxHeight { get; init; }  // highest picture quality chosen for a video (null = best)
    // From the browser extension: the size the browser reported and whether the user clicked the link (shows the download window)
    public long? Size { get; init; }
    public bool? FromBrowser { get; init; }
    public string? InfoJson { get; init; }

    /// <summary>Only plain web links are accepted.</summary>
    public bool IsValid => Uri.TryCreate(Url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
}

/// <summary>Folder where the browser helper drops jobs (one JSON file each) for the app to collect.</summary>
public sealed class Inbox
{
    readonly string folder;
    public Inbox(string? folder = null) => this.folder = folder ?? AppPaths.Inbox;

    public void Drop(DownloadJob job)
    {
        Directory.CreateDirectory(folder);
        var tmp = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(job));
        File.Move(tmp, Path.ChangeExtension(tmp, ".json"));
    }

    public List<DownloadJob> Collect()
    {
        var jobs = new List<DownloadJob>();
        if (!Directory.Exists(folder)) return jobs;
        foreach (var f in Directory.GetFiles(folder, "*.json").OrderBy(f => File.GetCreationTimeUtc(f)))
        {
            try { if (JsonSerializer.Deserialize<DownloadJob>(File.ReadAllText(f)) is { } j) jobs.Add(j); } catch { }
            try { File.Delete(f); } catch { }
        }
        return jobs;
    }
}
