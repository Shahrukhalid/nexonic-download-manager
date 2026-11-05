using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nexonic.Core;

/// <summary>Owns one bundled native session. Calls and disposal are serialized, including native checkpoints.</summary>
public sealed class NativeTorrentSession : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Create([MarshalAs(UnmanagedType.LPUTF8Str)] string json);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Call(IntPtr session, [MarshalAs(UnmanagedType.LPUTF8Str)] string json);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Free(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Destroy(IntPtr session);
    readonly object gate = new();
    readonly Call call;
    readonly Free free;
    readonly Destroy destroy;
    IntPtr library, session;
    static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static string BundledLibrary => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "nexonic_torrent.dll" : OperatingSystem.IsMacOS() ? "libnexonic_torrent.dylib" : "libnexonic_torrent.so");

    // An explicit library path is for SDK consumers and integration tests, never read from download/history input.
    public NativeTorrentSession(string stateDirectory, bool allowLocalPeers = false, bool dht = true, string? libraryPath = null, object? proxy = null,
        bool portForwarding = false, string encryption = "prefer")
    {
        if (!Path.IsPathFullyQualified(stateDirectory)) throw new ArgumentException("Choose an absolute torrent state directory.");
        library = NativeLibrary.Load(libraryPath ?? BundledLibrary);
        try
        {
            T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            var create = Export<Create>("ndm_torrent_create");
            call = Export<Call>("ndm_torrent_call");
            free = Export<Free>("ndm_torrent_free");
            destroy = Export<Destroy>("ndm_torrent_destroy");
            session = create(JsonSerializer.Serialize(new { state_dir = stateDirectory, allow_local_peers = allowLocalPeers, dht, proxy,
                port_forwarding = portForwarding, encryption = encryption is "require" or "off" ? encryption : "prefer" }));
            if (session == IntPtr.Zero)
            {
                // The C API reports initialization errors through a null-session call.
                _ = Read(call(IntPtr.Zero, "{}"));
                throw new InvalidOperationException("Could not initialize the native torrent session.");
            }
        }
        catch { NativeLibrary.Free(library); library = IntPtr.Zero; throw; }
    }

    public JsonElement Invoke(object request)
    {
        var encoded = JsonSerializer.Serialize(request, json);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(session == IntPtr.Zero, this);
            return Read(call(session, encoded));
        }
    }
    JsonElement Read(IntPtr value)
    {
        if (value == IntPtr.Zero) throw new InvalidOperationException("Native torrent response allocation failed.");
        try
        {
            using var document = JsonDocument.Parse(Marshal.PtrToStringUTF8(value) ?? throw new InvalidDataException("Missing native response."));
            var result = document.RootElement;
            if (!result.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(result.GetProperty("error").GetString());
            return result.Clone();
        }
        finally { free(value); }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (session == IntPtr.Zero) return;
            destroy(session); session = IntPtr.Zero;
            NativeLibrary.Free(library); library = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
    ~NativeTorrentSession() { Dispose(); }
}
