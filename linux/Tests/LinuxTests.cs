using Nexonic.Core;
using Nexonic.Linux;
using Xunit;

namespace Nexonic.Linux.Tests;

public sealed class TempHome : IDisposable
{
    public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ndm-test-" + Guid.NewGuid().ToString("N"));
    public TempHome() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class IntegrationTests
{
    static Integration Make(string home, string appDir, string? appImage = null) =>
        new(home: home, appDir: appDir, appImage: appImage, dataHome: Path.Combine(home, ".local", "share"), configHome: Path.Combine(home, ".config"));

    [Fact]
    public void RegistersOnlyBrowsersThatAreSetUp()
    {
        using var t = new TempHome();
        var app = Path.Combine(t.Path, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "nexonic-host"), "#!/bin/sh\n");
        Directory.CreateDirectory(Path.Combine(t.Path, ".config", "google-chrome"));
        Directory.CreateDirectory(Path.Combine(t.Path, ".mozilla"));
        var done = Make(t.Path, app).Install();
        Assert.Equal(2, done.Count);
        var chrome = File.ReadAllText(Path.Combine(t.Path, ".config", "google-chrome", "NativeMessagingHosts", NativeMessaging.HostName + ".json"));
        Assert.Contains("allowed_origins", chrome);
        Assert.Contains("nexonic-host", chrome);
        var firefox = File.ReadAllText(Path.Combine(t.Path, ".mozilla", "native-messaging-hosts", NativeMessaging.HostName + ".json"));
        Assert.Contains("allowed_extensions", firefox);
        Assert.False(Directory.Exists(Path.Combine(t.Path, ".config", "chromium", "NativeMessagingHosts")));
    }

    [Fact]
    public void AppImageGetsLaunchScriptsMenuEntryAndCommand()
    {
        using var t = new TempHome();
        var mount = Path.Combine(t.Path, "mount");
        Directory.CreateDirectory(mount);
        var image = Path.Combine(t.Path, "Apps", "Nexonic Download Manager.AppImage");
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllText(image, "");
        Directory.CreateDirectory(Path.Combine(t.Path, ".config", "BraveSoftware", "Brave-Browser"));
        var i = Make(t.Path, mount, image);
        Assert.Equal(InstallKind.AppImage, i.Kind);
        i.Install();
        var host = Path.Combine(t.Path, ".local", "share", Integration.AppId, "bin", "nexonic-host");
        Assert.Equal(i.HostCommand, host);
        Assert.Contains("--host", File.ReadAllText(host));
        Assert.True(File.GetUnixFileMode(host).HasFlag(UnixFileMode.UserExecute));
        var manifest = File.ReadAllText(Path.Combine(t.Path, ".config", "BraveSoftware", "Brave-Browser", "NativeMessagingHosts", NativeMessaging.HostName + ".json"));
        Assert.Contains("bin", manifest);
        var entry = File.ReadAllText(Path.Combine(t.Path, ".local", "share", "applications", Integration.AppId + ".desktop"));
        Assert.Contains("Exec=\"" + image + "\" %U", entry);
        Assert.True(File.Exists(Path.Combine(t.Path, ".local", "bin", "ndm")));

        i.Remove();
        Assert.False(File.Exists(Path.Combine(t.Path, ".local", "bin", "ndm")));
        Assert.False(File.Exists(host));
        Assert.False(File.Exists(Path.Combine(t.Path, ".local", "share", "applications", Integration.AppId + ".desktop")));
    }

    [Fact]
    public void NeverReplacesSomeoneElsesNdmCommand()
    {
        using var t = new TempHome();
        var app = Path.Combine(t.Path, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "nexonic-host"), "");
        var ndm = Path.Combine(t.Path, ".local", "bin", "ndm");
        Directory.CreateDirectory(Path.GetDirectoryName(ndm)!);
        File.WriteAllText(ndm, "#!/bin/sh\necho other\n");
        Make(t.Path, app).Install();
        Assert.Contains("other", File.ReadAllText(ndm));
        Make(t.Path, app).Remove();
        Assert.True(File.Exists(ndm));
    }

    [Fact]
    public void InstallKinds()
    {
        Assert.Equal(InstallKind.Package, new Integration(home: "/tmp", appDir: "/opt/nexonic-download-manager/", appImage: null).Kind);
        Assert.Equal(InstallKind.Folder, new Integration(home: "/tmp", appDir: "/home/me/ndm/", appImage: null).Kind);
    }

    [Fact]
    public void ScriptsAndDesktopEntriesQuotePaths()
    {
        Assert.Equal("#!/bin/sh\n# Written by Nexonic Download Manager\nexec '/a/it'\\''s here/x.AppImage' --host \"$@\"\n",
            Integration.ScriptText("/a/it's here/x.AppImage", "--host"));
        Assert.Equal("/opt/ndm/NexonicDownloadManager", Integration.EscapeExec("/opt/ndm/NexonicDownloadManager"));
        Assert.Equal("\"/home/me/My Apps/a\\\\$b.AppImage\"", Integration.EscapeExec("/home/me/My Apps/a$b.AppImage"));
    }
}

public class LinuxSystemTests
{
    [Fact]
    public void ReadsTheDownloadFolderFromUserDirs()
    {
        const string text = "# comment\nXDG_DESKTOP_DIR=\"$HOME/Desktop\"\nXDG_DOWNLOAD_DIR=\"$HOME/Téléchargements\"\n";
        Assert.Equal("/home/me/Téléchargements", LinuxSystem.UserDir(text, "XDG_DOWNLOAD_DIR", "/home/me"));
        Assert.Null(LinuxSystem.UserDir("XDG_DOWNLOAD_DIR=\"$HOME/\"\n", "XDG_DOWNLOAD_DIR", "/home/me"));
        Assert.Null(LinuxSystem.UserDir("", "XDG_DOWNLOAD_DIR", "/home/me"));
    }

    [Fact]
    public void TrashKeepsTheFileAndWhereItCameFrom()
    {
        using var t = new TempHome();
        var file = Path.Combine(t.Path, "my file.zip");
        File.WriteAllText(file, "x");
        var trash = Path.Combine(t.Path, "Trash");
        Assert.True(LinuxSystem.TrashByHand(file, trash));
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(trash, "files", "my file.zip")));
        var info = File.ReadAllText(Path.Combine(trash, "info", "my file.zip.trashinfo"));
        Assert.Contains("Path=" + t.Path + "/my%20file.zip", info);
        File.WriteAllText(file, "y");
        Assert.True(LinuxSystem.TrashByHand(file, trash));
        Assert.True(File.Exists(Path.Combine(trash, "files", "my file 2.zip")));
    }
}

public class SingleInstanceTests
{
    [Fact]
    public void SecondCopyWakesTheFirst()
    {
        using var t = new TempHome();
        using var first = new SingleInstance(t.Path);
        Assert.True(first.TryBecomeFirst());
        var got = new TaskCompletionSource<byte>();
        first.Listen(b => got.TrySetResult(b));
        using var second = new SingleInstance(t.Path);
        Assert.False(second.TryBecomeFirst());
        Assert.True(second.Signal((byte)'i'));
        Assert.True(got.Task.Wait(5000));
        Assert.Equal((byte)'i', got.Task.Result);
    }
}

[Collection("Secrets")]
public class KeyringTests
{
    /// <summary>A keyring in memory that answers like secret-tool.</summary>
    sealed class FakeKeyring
    {
        public string? Stored;
        public bool Broken;
        public int Stores;
        public (int, string, string) Run(string[] args, string? input)
        {
            if (Broken) return (1, "", "Cannot autolaunch D-Bus without X11 $DISPLAY");
            if (args[0] == "lookup") return Stored != null ? (0, Stored, "") : (1, "", "");
            Stores++;
            Stored = input;
            return (0, "", "");
        }
    }

    [Fact]
    public void CreatesOneKeyAndRoundTrips()
    {
        var k = new FakeKeyring();
        var p = new KeyringProtector(k.Run);
        var sealedText = p.Protect("sid=abc; token=xyz")!;
        Assert.DoesNotContain("sid=abc", sealedText);
        Assert.Equal("sid=abc; token=xyz", p.Unprotect(sealedText));
        // A new start reads the same key from the keyring.
        Assert.Equal("sid=abc; token=xyz", new KeyringProtector(k.Run).Unprotect(sealedText));
        Assert.Equal(1, k.Stores);
        Assert.Null(p.Unprotect("bm90IHNlYWxlZA=="));
    }

    [Fact]
    public void BrokenKeyringNeverWritesPlainTextOrReplacesTheKey()
    {
        var k = new FakeKeyring { Broken = true };
        var p = new KeyringProtector(k.Run);
        Assert.False(p.Available);
        Assert.Null(p.Protect("secret"));
        Assert.Equal(0, k.Stores);

        var before = (Secrets.Protector, Secrets.AllowPlainText);
        try
        {
            Secrets.Protector = p;
            Assert.Null(Secrets.Seal("sid=abc"));
            Secrets.Protector = null;
            Secrets.AllowPlainText = false;
            Assert.Null(Secrets.Seal("sid=abc"));
            var json = Secrets.SerializeSealed(new List<DownloadRecord> { new() { Url = "https://x/y.zip", Cookies = "sid=abc", Password = "pw" } });
            Assert.DoesNotContain("sid=abc", json);
            Assert.DoesNotContain("\"pw\"", json);
        }
        finally { (Secrets.Protector, Secrets.AllowPlainText) = before; }
    }
}

public class LaunchArgsTests
{
    [Fact]
    public void FindsLinksInArgumentsSchemesAndFiles()
    {
        using var t = new TempHome();
        var list = Path.Combine(t.Path, "links.txt");
        File.WriteAllText(list, "https://example.com/a.zip\nnot a link\nhttps://example.com/b.zip\n");
        var shortcut = Path.Combine(t.Path, "page.url");
        File.WriteAllText(shortcut, "[InternetShortcut]\r\nURL=https://example.com/c.iso\r\n");
        var links = LaunchArgs.Links(new[]
        {
            "--inbox", "https://example.com/file.zip", "nexonic://add?url=https%3A%2F%2Fexample.com%2Fd.mp4", list, "file://" + shortcut, "ftp://nope",
        });
        Assert.Equal(new[] { "https://example.com/file.zip", "https://example.com/d.mp4", "https://example.com/a.zip", "https://example.com/b.zip", "https://example.com/c.iso" }, links);
    }
}
