using System.Net;
using System.Net.Sockets;
using System.Text;
using Nexonic.Core;
using Xunit;

/// <summary>Server country from the offline table, speed by time of day, and Check Server's speed and HTTP version.</summary>
public class ServerInfoTests
{
    [Fact]
    public void LooksUpCountriesInTheLocalTable()
    {
        // The DB-IP country file's own format: start,end,country (IPv4 and IPv6 ranges).
        const string csv = "1.0.0.0,1.0.0.255,AU\n8.8.8.0,8.8.8.255,US\n\"81.0.0.0\",\"81.0.255.255\",\"DE\"\n2a00:1450::,2a00:1450:ffff:ffff:ffff:ffff:ffff:ffff,IE\nbad,line\n9.9.9.9,9.9.9.9,zz\n";
        var t = GeoIp.Parse(new StringReader(csv));
        Assert.Equal(4, t.Count);  // the bad lines are skipped
        Assert.Equal("US", t.Country(IPAddress.Parse("8.8.8.8")));
        Assert.Equal("DE", t.Country(IPAddress.Parse("81.0.10.1")));
        Assert.Equal("IE", t.Country(IPAddress.Parse("2a00:1450:4001::8")));
        Assert.Equal("US", t.Country(IPAddress.Parse("::ffff:8.8.8.8")));
        Assert.Null(t.Country(IPAddress.Parse("8.8.9.1")));
        Assert.Null(t.Country(IPAddress.Parse("0.0.0.1")));
        Assert.Equal("🇩🇪", GeoIp.Flag("DE"));
        Assert.Equal("", GeoIp.Flag("d1"));
        Assert.Equal("https://download.db-ip.com/free/dbip-country-lite-2026-11.csv.gz", GeoIp.Sources(new DateTime(2026, 11, 10)).First());
    }

    [Fact]
    public void SpeedFollowsTheTimetable()
    {
        var (rules, problem) = SpeedSchedule.Parse("# work\nMon-Fri 09:00-17:00 300\nDaily 23:00-07:00 0\nWeekends 10:00-12:00 50");
        Assert.Null(problem);
        var monday = new DateTime(2026, 11, 9);  // a Monday
        Assert.Equal(300, SpeedSchedule.LimitAt(rules, monday.AddHours(10)));
        Assert.Null(SpeedSchedule.LimitAt(rules, monday.AddHours(18)));
        Assert.Equal(0, SpeedSchedule.LimitAt(rules, monday.AddHours(23.5)));
        Assert.Equal(0, SpeedSchedule.LimitAt(rules, monday.AddDays(1).AddHours(6)));  // after midnight, from Monday's rule
        Assert.Equal(50, SpeedSchedule.LimitAt(rules, monday.AddDays(5).AddHours(11)));  // Saturday
        Assert.Null(SpeedSchedule.LimitAt(rules, monday.AddDays(5).AddHours(14)));
        Assert.Contains("Line 2", SpeedSchedule.Parse("Daily 01:00-02:00 5\nsometimes 10:00-11:00 5").Problem);
        Assert.Contains("Line 1", SpeedSchedule.Parse("Mon 25:00-26:00 5").Problem);
        Assert.Contains("Line 1", SpeedSchedule.Parse("Mon 10:00-10:00 5").Problem);
        Assert.Equal(2, SpeedSchedule.Parse("Fri-Mon 22:00-23:00 1\nsat,sun 01:00-02:00 2").Rules.Count);
        Assert.Equal(1, SpeedSchedule.LimitAt(SpeedSchedule.Parse("Fri-Mon 22:00-23:00 1").Rules, monday.AddHours(22.5)));
    }

    [Fact]
    public async Task CheckServerMeasuresRealSpeedAndHttpVersion()
    {
        var body = new byte[3 * 1024 * 1024];
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(); } catch { return; }
                _ = Task.Run(async () =>
                {
                    using var _ = c;
                    var s = c.GetStream();
                    var reader = new StreamReader(s, Encoding.ASCII);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { } 
                        var head = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nContent-Type: application/zip\r\n\r\n";
                        await s.WriteAsync(Encoding.ASCII.GetBytes(head));
                        try { await s.WriteAsync(body); } catch { return; }
                    }
                });
            }
        });
        var steps = await NetworkDiagnostics.RunAsync($"http://127.0.0.1:{port}/file.zip", new ProxySettings { Mode = ProxyMode.None });
        listener.Stop();
        var answer = steps.Single(s => s.Name == "Server answer");
        Assert.Contains("HTTP/1.1", answer.Detail);
        var speed = steps.Single(s => s.Name == "Speed");
        Assert.True(speed.Ok);
        Assert.Contains("/s on one connection", speed.Detail);
        Assert.DoesNotContain("Server location", string.Join(",", steps.Select(s => s.Name)));  // local addresses have no country
    }
}
