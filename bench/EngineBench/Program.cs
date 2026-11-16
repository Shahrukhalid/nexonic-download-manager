// Engine speed benchmark: real downloads from a local test server, with the app's default connection settings.
// Every result is checked byte for byte (SHA-256) before its time counts.
//
//   run:      EngineBench --out result.json [--runs 3]
//   compare:  EngineBench --compare base.json new.json [--tolerance 0.15]   (exit 1 when a scenario got slower)
//
// The scenarios model what real servers do: a CDN that limits each connection's speed, a server that refuses more than a
// few connections at once, and a fast local link where only the engine's own overhead counts.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexonic.Core;

static class Program
{
    sealed record Scenario(string Name, string Description, int SizeMB, long BytesPerSecondPerConnection, int MaxConcurrent);

    sealed record Result(string Name, string Description, int SizeMB, double MedianSeconds, double MBps, int Runs);

    static readonly Scenario[] Scenarios =
    {
        new("cdn-per-connection-limit", "64 MB, each connection limited to 1 MB/s", 64, 1L << 20, 0),
        new("server-allows-6", "32 MB, 2 MB/s per connection, at most 6 connections (more get 503)", 32, 2L << 20, 6),
        new("fast-local-link", "512 MB, no limits (engine overhead)", 512, 0, 0),
    };

    static async Task<int> Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--compare") return Compare(args[1], args[2], Option(args, "--tolerance", 0.15));
        var output = OptionText(args, "--out") ?? "bench.json";
        var runs = (int)Option(args, "--runs", 3);
        var results = new List<Result>();
        foreach (var s in Scenarios)
        {
            var content = Pattern(s.SizeMB << 20);
            var expected = SHA256.HashData(content);
            var times = new List<double>();
            for (var i = 0; i < runs; i++)
            {
                using var server = new Server(content, s.BytesPerSecondPerConnection, s.MaxConcurrent);
                var folder = Directory.CreateTempSubdirectory("ndm-bench-").FullName;
                try
                {
                    var record = new DownloadRecord { Url = $"http://127.0.0.1:{server.Port}/{s.Name}.bin", Folder = folder, FileName = s.Name + ".bin" };
                    var watch = Stopwatch.StartNew();
                    var done = await Run(new Transfer(record, 32, new SpeedLimiter(), autoConnections: true));
                    watch.Stop();
                    if (done.Status != DownloadStatus.Finished) throw new Exception($"{s.Name}: download did not finish ({done.Error})");
                    await using (var file = File.OpenRead(done.SavedPath!))
                        if (!SHA256.HashData(file).AsSpan().SequenceEqual(expected)) throw new Exception($"{s.Name}: the file is not exact");
                    times.Add(watch.Elapsed.TotalSeconds);
                    Console.WriteLine($"{s.Name} run {i + 1}: {watch.Elapsed.TotalSeconds:F2} s");
                }
                finally { try { Directory.Delete(folder, true); } catch { } }
            }
            times.Sort();
            var median = times[times.Count / 2];
            results.Add(new Result(s.Name, s.Description, s.SizeMB, Math.Round(median, 3), Math.Round(s.SizeMB / median, 1), runs));
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine();
        Console.WriteLine("| Scenario | Median | Speed |");
        Console.WriteLine("|---|---|---|");
        foreach (var r in results) Console.WriteLine($"| {r.Description} | {r.MedianSeconds:F2} s | {r.MBps:F1} MB/s |");
        return 0;
    }

    /// <summary>Writes a Markdown comparison; 1 when a scenario is slower than the base by more than the tolerance.</summary>
    static int Compare(string basePath, string newPath, double tolerance)
    {
        var old = JsonSerializer.Deserialize<List<Result>>(File.ReadAllText(basePath)) ?? new();
        var now = JsonSerializer.Deserialize<List<Result>>(File.ReadAllText(newPath)) ?? new();
        var worse = false;
        Console.WriteLine("| Scenario | Previous release | This version | Change |");
        Console.WriteLine("|---|---|---|---|");
        foreach (var n in now)
        {
            var o = old.FirstOrDefault(x => x.Name == n.Name);
            if (o == null) { Console.WriteLine($"| {n.Description} | – | {n.MedianSeconds:F2} s | new |"); continue; }
            var change = (n.MedianSeconds - o.MedianSeconds) / o.MedianSeconds;
            var slower = change > tolerance;
            worse |= slower;
            var mark = slower ? " (slower)" : change < -0.05 ? " (faster)" : "";
            Console.WriteLine($"| {n.Description} | {o.MedianSeconds:F2} s | {n.MedianSeconds:F2} s | {change:+0%;-0%;0%}{mark} |");
        }
        return worse ? 1 : 0;
    }

    static async Task<DownloadRecord> Run(IDownloader t)
    {
        var done = new TaskCompletionSource();
        t.Changed += r => { if (r.Status is DownloadStatus.Finished or DownloadStatus.Failed) done.TrySetResult(); };
        await t.StartAsync();
        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(5)));
        return t.Record;
    }

    static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++) data[i] = (byte)(i * 31 + i / 997);
        return data;
    }

    static double Option(string[] args, string name, double fallback) =>
        OptionText(args, name) is { } v && double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;

    static string? OptionText(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>HTTP/1.1 with ranges; each connection is held to a speed, and connections over the limit get 503.</summary>
    sealed class Server : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly byte[] content;
        readonly long perConnection;
        readonly int maxConcurrent;
        int active;
        public int Port { get; }

        public Server(byte[] content, long perConnection, int maxConcurrent)
        {
            this.content = content;
            this.perConnection = perConnection;
            this.maxConcurrent = maxConcurrent;
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await listener.AcceptTcpClientAsync(); } catch { return; }
                    _ = Task.Run(() => Serve(c));
                }
            });
        }

        async Task Serve(TcpClient c)
        {
            using var _ = c;
            try
            {
                var s = c.GetStream();
                var reader = new StreamReader(s, Encoding.ASCII);
                while (true)
                {
                    var first = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(first)) return;
                    string? range = null, line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                        if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = line[6..].Trim();
                    var busy = Interlocked.Increment(ref active);
                    try
                    {
                        if (maxConcurrent > 0 && busy > maxConcurrent)
                        {
                            await s.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 503 Busy\r\nRetry-After: 1\r\nContent-Length: 0\r\n\r\n"));
                            continue;
                        }
                        long from = 0, to = content.Length - 1;
                        var head = new StringBuilder();
                        if (range == null) head.Append("HTTP/1.1 200 OK\r\nAccept-Ranges: bytes\r\nETag: \"bench\"\r\n");
                        else
                        {
                            var p = range["bytes=".Length..].Split('-');
                            from = long.Parse(p[0]);
                            if (p[1].Length > 0) to = Math.Min(long.Parse(p[1]), content.Length - 1);
                            head.Append($"HTTP/1.1 206 Partial\r\nAccept-Ranges: bytes\r\nETag: \"bench\"\r\nContent-Range: bytes {from}-{to}/{content.Length}\r\n");
                        }
                        head.Append($"Content-Length: {to - from + 1}\r\n\r\n");
                        await s.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
                        await Send(s, from, to);
                    }
                    finally { Interlocked.Decrement(ref active); }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        /// <summary>Sends the range in 64 KB pieces, pausing so the connection keeps to its speed.</summary>
        async Task Send(Stream s, long from, long to)
        {
            const int chunk = 64 * 1024;
            var watch = Stopwatch.StartNew();
            long sent = 0;
            for (var at = from; at <= to; at += chunk)
            {
                var n = (int)Math.Min(chunk, to - at + 1);
                await s.WriteAsync(content.AsMemory((int)at, n));
                sent += n;
                if (perConnection > 0)
                {
                    var due = TimeSpan.FromSeconds((double)sent / perConnection) - watch.Elapsed;
                    if (due > TimeSpan.Zero) await Task.Delay(due);
                }
            }
        }

        public void Dispose() => listener.Stop();
    }
}
