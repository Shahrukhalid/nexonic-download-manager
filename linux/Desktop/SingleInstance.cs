using System.Net.Sockets;
using Nexonic.Core;

namespace Nexonic.Linux;

/// <summary>
/// One copy of the app per user. The first copy holds a lock file and listens on a private socket; a second start sends one
/// byte (<c>i</c> = new downloads in the inbox, <c>s</c> = show the window) and quits, like the Windows app's named pipe.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    readonly string lockPath;
    readonly string socketPath;
    FileStream? lockFile;
    Socket? listener;

    public SingleInstance(string? folder = null)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        folder ??= runtime is { Length: > 0 } && Directory.Exists(runtime) ? runtime : AppPaths.Support;
        Directory.CreateDirectory(folder);
        lockPath = Path.Combine(folder, "nexonic-download-manager.lock");
        socketPath = Path.Combine(folder, "nexonic-download-manager.sock");
    }

    /// <summary>True for the first copy (it then owns the lock until it exits).</summary>
    public bool TryBecomeFirst()
    {
        try
        {
            // FileShare.None takes an exclusive lock that the system drops when the process ends, even after a crash.
            lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Wakes the first copy; false when it could not be reached.</summary>
    public bool Signal(byte why)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                s.Connect(new UnixDomainSocketEndPoint(socketPath));
                s.Send(new[] { why });
                return true;
            }
            catch { Thread.Sleep(200); }
        }
        return false;
    }

    /// <summary>Listens for later starts; <paramref name="woken"/> runs on a background thread with the byte received.</summary>
    public void Listen(Action<byte> woken)
    {
        try { File.Delete(socketPath); } catch { }
        listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        try { File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
        listener.Listen(8);
        var l = listener;
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var client = l.Accept();
                    client.ReceiveTimeout = 2000;
                    var buffer = new byte[1];
                    var n = client.Receive(buffer);
                    woken(n == 1 ? buffer[0] : (byte)'s');
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) when (l.SafeHandle.IsClosed) { return; }
                catch { Thread.Sleep(200); }
            }
        }) { IsBackground = true, Name = "nexonic-single-instance" }.Start();
    }

    public void Dispose()
    {
        try { listener?.Dispose(); } catch { }
        try { File.Delete(socketPath); } catch { }
        lockFile?.Dispose();
    }
}
