using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.Server;

/// <summary>
/// nexonic-server: the download manager without a window (NAS, home server, Docker). Phones and browsers manage it
/// through remote access (pair by scanning the QR code it prints); scripts can use the local API. Settings, the list and
/// the queues are kept in the same files as the desktop app's, in $XDG_DATA_HOME (Docker: /data).
/// </summary>
static class Program
{
    const string Usage = """
        nexonic-server: Nexonic Download Manager without a window, managed from your phone or browser.

        Options:
          --downloads FOLDER   where files are saved (also NDM_DOWNLOADS; default: the folder in settings)
          --port PORT          port for phones and browsers (also NDM_PORT; default 47813)
          --new-key            unpair every device (a new pairing code), then keep running
          --version            print the version

        Open the printed address on a phone on the same network, or scan the code with its camera.
        Settings and the downloads list are kept in $XDG_DATA_HOME/Nexonic Download Manager (Docker: /data).
        """;

    static readonly BlockingCollection<Action> work = new();

    public static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Usage); return 0; }
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        if (args.Contains("--version")) { Console.WriteLine(version); return 0; }
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        // The data folder must exist before .NET reports it (otherwise it reports an empty path).
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x && Path.IsPathFullyQualified(x) ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        Directory.CreateDirectory(dataHome);
        Directory.CreateDirectory(AppPaths.Support);
        // No desktop keyring here: secrets (the pairing key, passwords, cookies) are encrypted with a key kept in a file
        // that only this user can read, next to the settings.
        Secrets.Protector = new FileKeyProtector(Path.Combine(AppPaths.Support, "server.key"));

        DownloadManager? manager = null;
        void Post(Action a) { try { work.Add(a); } catch (InvalidOperationException) { } }
        Task<(int, object)> OnLoop(Func<(int, object)> call)
        {
            var done = new TaskCompletionSource<(int, object)>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() => { try { done.SetResult(call()); } catch (Exception e) { done.SetException(e); } });
            return done.Task;
        }

        manager = new DownloadManager(Post);
        var m = manager;
        var s = m.Settings;
        var folder = Arg("--downloads") ?? Environment.GetEnvironmentVariable("NDM_DOWNLOADS");
        if (!string.IsNullOrWhiteSpace(folder)) s.Folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(s.Folder);
        var port = int.TryParse(Arg("--port") ?? Environment.GetEnvironmentVariable("NDM_PORT"), out var p) && p is >= 1024 and <= 65535 ? p : s.Remote.Port;
        s.Remote.Enabled = true;
        s.Remote.Port = port;
        // No windows: links from the ndm command are added straight away, finished files open nothing.
        s.ShowDownloadDialog = false;
        s.ShowProgressWindow = false;
        s.ShowCompleteDialog = false;
        s.MonitorClipboard = false;
        m.ApplySettings(s);
        m.ApplyAutomation(OnLoop);
        m.ApplyRemote(OnLoop, newKey: args.Contains("--new-key"));
        if (m.RemoteProblem.Length > 0) { Console.Error.WriteLine(m.RemoteProblem); return 1; }

        m.Notify += (title, body) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {title}: {body}");
        m.Finished += r => Console.WriteLine($"{DateTime.Now:HH:mm:ss} Finished: {r.SavedPath ?? r.FileName}");
        m.QuitRequested += () => work.CompleteAdding();

        Console.WriteLine($"Nexonic Download Manager {version} (server) · saving to {s.Folder}");
        PrintPairing(m, port, showCode: true);

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; work.CompleteAdding(); };
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; work.CompleteAdding(); });
        using var ticker = new Timer(_ => Post(m.Tick), null, 500, 500);
        using var idle = new Timer(_ => Post(m.ApplyIdleLimit), null, 5000, 5000);

        foreach (var action in work.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception e) { Console.Error.WriteLine(e); }
        }
        m.Save();
        m.SaveQueues();
        Console.WriteLine("Stopped; downloads continue when the server starts again.");
        return 0;
    }

    static void PrintPairing(DownloadManager m, int port, bool showCode)
    {
        var addresses = RemoteAccess.LanAddresses();
        var host = Environment.GetEnvironmentVariable("NDM_HOST") is { Length: > 0 } h ? h : addresses.FirstOrDefault()?.ToString() ?? "127.0.0.1";
        if (m.PairingUrl(host) is not { } url) return;
        Console.WriteLine();
        Console.WriteLine("Pair a phone: scan this code with its camera, or open the address below on it (same network).");
        if (showCode) Console.WriteLine(RemoteAccess.TerminalQr(url));
        Console.WriteLine(url);
        if (addresses.Count > 1 && Environment.GetEnvironmentVariable("NDM_HOST") is not { Length: > 0 })
            Console.WriteLine("Other addresses of this computer: " + string.Join(", ", addresses.Skip(1)) + " (set NDM_HOST to choose one)");
        Console.WriteLine("Keep this address private: it lets a device manage your downloads. nexonic-server --new-key unpairs every device.");
        Console.WriteLine();
    }
}

/// <summary>Encrypts secrets with a random key kept in a file only this user can read (servers have no keyring).</summary>
sealed class FileKeyProtector(string path) : ISecretProtector
{
    byte[]? key;

    byte[] Key()
    {
        if (key != null) return key;
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim();
            try { var k = Convert.FromBase64String(text); if (k.Length == 32) return key = k; } catch (FormatException) { }
        }
        var fresh = RandomNumberGenerator.GetBytes(32);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Convert.ToBase64String(fresh));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, overwrite: true);
        return key = fresh;
    }

    public string? Protect(string plain)
    {
        try
        {
            var data = Encoding.UTF8.GetBytes(plain);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[data.Length];
            using (var aes = new AesGcm(Key(), 16)) aes.Encrypt(nonce, data, cipher, tag);
            return Convert.ToBase64String([1, .. nonce, .. tag, .. cipher]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException) { return null; }
    }

    public string? Unprotect(string sealedText)
    {
        try
        {
            var b = Convert.FromBase64String(sealedText);
            if (b.Length < 29 || b[0] != 1) return null;
            var plain = new byte[b.Length - 29];
            using var aes = new AesGcm(Key(), 16);
            aes.Decrypt(b.AsSpan(1, 12), b.AsSpan(29), b.AsSpan(13, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException or CryptographicException) { return null; }
    }
}
