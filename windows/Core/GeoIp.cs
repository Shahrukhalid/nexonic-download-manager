using System.IO.Compression;
using System.Net;
using System.Numerics;

namespace Nexonic.Core;

/// <summary>
/// The country of a server's address, looked up in a local copy of the free DB-IP country database (CC BY 4.0,
/// "IP Geolocation by DB-IP", https://db-ip.com). The copy is downloaded the first time a server is checked and refreshed
/// monthly; addresses are never sent anywhere for a lookup.
/// </summary>
public static class GeoIp
{
    public const string Attribution = "IP Geolocation by DB-IP (db-ip.com, CC BY 4.0)";
    static readonly SemaphoreSlim loading = new(1, 1);
    static Table? table;

    public static string File => Path.Combine(SiteVideoTools.Folder, "ip-country.csv.gz");

    /// <summary>Address ranges sorted by start: IPv4 as 32-bit numbers, IPv6 as 128-bit numbers, each with its country.</summary>
    public sealed class Table
    {
        readonly UInt128[] starts, ends;
        readonly string[] codes;

        public Table(List<(UInt128 Start, UInt128 End, string Code)> rows)
        {
            rows.Sort((a, b) => a.Start.CompareTo(b.Start));
            starts = rows.Select(r => r.Start).ToArray();
            ends = rows.Select(r => r.End).ToArray();
            codes = rows.Select(r => r.Code).ToArray();
        }

        public int Count => starts.Length;

        public string? Country(IPAddress ip)
        {
            var key = Key(ip);
            var i = Array.BinarySearch(starts, key);
            if (i < 0) i = ~i - 1;
            return i >= 0 && key <= ends[i] ? codes[i] : null;
        }
    }

    /// <summary>IPv4 (and IPv4-mapped IPv6) in one number space, IPv6 above it, so both sort into one table.</summary>
    static UInt128 Key(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 4) return ((UInt128)b[0] << 24) | ((UInt128)b[1] << 16) | ((UInt128)b[2] << 8) | b[3];
        UInt128 v = 0;
        foreach (var x in b) v = (v << 8) | x;
        return v | ((UInt128)1 << 127);  // IPv6 sorts after every IPv4 address (2000::/3 and the like keep their order)
    }

    /// <summary>Reads the database's lines ("start,end,CC"); lines that are not valid are skipped.</summary>
    public static Table Parse(TextReader reader)
    {
        var rows = new List<(UInt128, UInt128, string)>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var p = line.Split(',');
            if (p.Length < 3) continue;
            var cc = p[2].Trim().Trim('"');
            if (cc.Length != 2 || !cc.All(char.IsAsciiLetterUpper)) continue;
            if (!IPAddress.TryParse(p[0].Trim('"'), out var a) || !IPAddress.TryParse(p[1].Trim('"'), out var b)) continue;
            if (a.AddressFamily != b.AddressFamily) continue;
            var (s, e) = (Key(a), Key(b));
            if (e >= s) rows.Add((s, e, cc));
        }
        return new Table(rows);
    }

    /// <summary>A flag from a country code (regional indicator letters), e.g. "DE" → 🇩🇪.</summary>
    public static string Flag(string code) => code.Length == 2 && code.All(char.IsAsciiLetterUpper)
        ? char.ConvertFromUtf32(0x1F1E6 + code[0] - 'A') + char.ConvertFromUtf32(0x1F1E6 + code[1] - 'A')
        : "";

    /// <summary>The address list of the month (the current one, else the last), as DB-IP publishes it.</summary>
    public static IEnumerable<string> Sources(DateTime utc)
    {
        foreach (var m in new[] { utc, utc.AddMonths(-1) })
            yield return $"https://download.db-ip.com/free/dbip-country-lite-{m:yyyy-MM}.csv.gz";
    }

    /// <summary>The country of an address, downloading the database the first time (and again after 35 days); null when
    /// it cannot be had or the address is not listed (private and local addresses are never listed).</summary>
    public static async Task<string?> CountryAsync(IPAddress ip, CancellationToken ct)
    {
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return null;
        if (table == null || DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(File) > TimeSpan.FromDays(35))
        {
            await loading.WaitAsync(ct);
            try
            {
                if (!System.IO.File.Exists(File) || DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(File) > TimeSpan.FromDays(35))
                    await DownloadAsync(ct);
                if (table == null && System.IO.File.Exists(File))
                {
                    await using var gz = new GZipStream(System.IO.File.OpenRead(File), CompressionMode.Decompress);
                    using var reader = new StreamReader(gz);
                    table = await Task.Run(() => Parse(reader), ct);
                }
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or TaskCanceledException && !ct.IsCancellationRequested) { }
            finally { loading.Release(); }
        }
        return table?.Country(ip);
    }

    static async Task DownloadAsync(CancellationToken ct)
    {
        using var http = Network.Client(TimeSpan.FromMinutes(3));
        foreach (var url in Sources(DateTime.UtcNow))
        {
            using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) continue;
            Directory.CreateDirectory(SiteVideoTools.Folder);
            var tmp = File + ".tmp";
            await using (var f = System.IO.File.Create(tmp)) await (await res.Content.ReadAsStreamAsync(ct)).CopyToAsync(f, ct);
            // Only a readable list with plenty of ranges replaces the copy in use.
            await using (var gz = new GZipStream(System.IO.File.OpenRead(tmp), CompressionMode.Decompress))
            using (var reader = new StreamReader(gz))
                if (Parse(reader).Count < 10_000) { System.IO.File.Delete(tmp); continue; }
            System.IO.File.Move(tmp, File, true);
            table = null;
            return;
        }
    }
}
