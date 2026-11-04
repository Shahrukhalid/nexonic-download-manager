using System.Reflection;
using System.Text;
using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

[Collection("Secrets")]
public sealed class UpgradeTests
{
    static string Fixture(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"upgrade081.{name}.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    static void WithFixtureProtector(Action check)
    {
        var before = (Secrets.Protector, Secrets.AllowPlainText);
        try { Secrets.Protector = new FixtureProtector(); Secrets.AllowPlainText = false; check(); }
        finally { (Secrets.Protector, Secrets.AllowPlainText) = before; }
    }
    [Fact]
    public void V081SettingsRetainCredentialsAndExistingOptInsWithoutEnablingTorrents() => WithFixtureProtector(() =>
    {
        var settings = Secrets.DeserializeOpened<AppSettings>(Fixture("settings"))!;
        Assert.False(settings.TorrentsEnabled);
        Assert.Equal("/fixture/Downloads", settings.Folder);
        Assert.False(settings.SortIntoCategoryFolders);
        Assert.Equal(2, settings.MaxActive);
        Assert.Equal(ProxyMode.Socks, settings.Proxy.Mode);
        Assert.Equal("fixture-proxy-secret", settings.Proxy.Password);
        Assert.Equal("fixture-site-secret", settings.HostProfiles[0].Password);
        Assert.True(settings.Automation.Enabled);
        Assert.Equal("fixture-api-key-0123456789", settings.Automation.Key);
        var saved = Secrets.SerializeSealed(settings);
        Assert.DoesNotContain("fixture-proxy-secret", saved);
        Assert.Equal(settings.Proxy.Password, Secrets.DeserializeOpened<AppSettings>(saved)!.Proxy.Password);
    });

    [Fact]
    public void V081HistoryKeepsProgressQueuesAndExistingEngineSelection() => WithFixtureProtector(() =>
    {
        var records = Secrets.DeserializeOpened<List<DownloadRecord>>(Fixture("downloads"))!;
        Assert.Equal(3, records.Count);
        var file = records[0];
        Assert.Equal(Guid.Parse("08100000-0000-0000-0000-000000000001"), file.Id);
        Assert.Equal(DownloadStatus.Paused, file.Status);
        Assert.Equal("night-v081", file.Queue);
        Assert.Equal(250, file.Received);
        Assert.Equal(.25, file.Progress);
        Assert.Equal("\"fixture-etag\"", file.Validator);
        Assert.Equal("fixture-cookie=value", file.Cookies);
        Assert.Equal("fixture-download-secret", file.Password);
        Assert.Equal(DownloadEngine.File, DownloadProviders.EngineOf(file));
        Assert.Equal(DownloadEngine.Hls, DownloadProviders.EngineOf(records[1]));
        Assert.Equal(.3, records[1].Progress);
        Assert.Equal(DownloadEngine.Video, DownloadProviders.EngineOf(records[2]));
        Assert.Equal("/fixture/Downloads/video.mp4", records[2].SavedPath);
        Assert.All(records, record => Assert.Null(record.Torrent));
        var roundTrip = Secrets.DeserializeOpened<List<DownloadRecord>>(Secrets.SerializeSealed(records))!;
        Assert.Equal(file.Received, roundTrip[0].Received);
        Assert.Equal(file.Cookies, roundTrip[0].Cookies);
        var queues = System.Text.Json.JsonSerializer.Deserialize<List<DownloadQueue>>(Fixture("queues"))!;
        Assert.Contains(queues, q => q.Id == file.Queue && !q.Running && q.MaxActive == 1);
    });

    [Fact]
    public void V081BackupsImportWithTheirOriginalSafetyRules()
    {
        var backup = Backup.Parse(Fixture("backup"));
        Assert.Equal("0.8.1", backup.App);
        Assert.Equal(3, backup.Downloads!.Count);
        Assert.False(backup.Settings!.TorrentsEnabled);
        Assert.False(backup.Settings.Automation.Enabled);
        var partial = backup.Downloads[0];
        Assert.Empty(partial.Segments); // Backups never include downloaded part files.
        Assert.Null(partial.Password);
        Assert.Null(partial.Cookies);
        Assert.Equal(DownloadStatus.Paused, partial.Status);
        Assert.Equal(DownloadStatus.Finished, backup.Downloads[2].Status);
    }
    sealed class FixtureProtector : ISecretProtector
    {
        public string Protect(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture:" + text));
        public string Unprotect(string text) => Encoding.UTF8.GetString(Convert.FromBase64String(text))["fixture:".Length..];
    }
}
