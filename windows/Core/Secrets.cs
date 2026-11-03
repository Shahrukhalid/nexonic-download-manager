using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexonic.Core;

/// <summary>Encrypts and decrypts short secrets for the current user (DPAPI on Windows, the keyring on Linux). Null when it fails.</summary>
public interface ISecretProtector
{
    string? Protect(string plain);
    string? Unprotect(string sealedText);
}

/// <summary>
/// Cookies, download passwords and the proxy password are saved encrypted: with Windows DPAPI, or on Linux with a key kept
/// in the desktop's keyring (Secret Service). Only this user on this computer can read them. Older plain-text values are
/// encrypted the next time the list is saved.
/// </summary>
public static class Secrets
{
    public const string Prefix = "dpapi:";
    public static ISecretProtector? Protector { get; set; } =
        OperatingSystem.IsWindows() ? new Dpapi() : OperatingSystem.IsLinux() ? new KeyringProtector() : null;

    /// <summary>Without a protector, secrets are written as they are only where the system has no keyring to offer (never on Linux).</summary>
    public static bool AllowPlainText { get; set; } = !OperatingSystem.IsLinux();

    /// <summary>Field names (as saved) that hold secrets, in download records and in settings.Proxy.</summary>
    static readonly string[] RecordFields = { "Cookies", "Password" };

    public static string? Seal(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.StartsWith(Prefix, StringComparison.Ordinal)) return plain;
        if (Protector == null) return AllowPlainText ? plain : null;
        // Could not encrypt: the secret is not written at all (never plain text). It stays in memory for this session.
        return Protector.Protect(plain) is { } p ? Prefix + p : null;
    }

    public static string? Open(string? stored)
    {
        if (stored == null || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        return Protector?.Unprotect(stored[Prefix.Length..]) ?? "";  // another user or computer: the secret is gone, not the download
    }

    /// <summary>The JSON of a value with its secret fields encrypted (`DownloadRecord` lists and `AppSettings`).</summary>
    public static string SerializeSealed<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value);
        Walk(node, Seal);
        return node?.ToJsonString() ?? "null";
    }

    /// <summary>Reads JSON saved by <see cref="SerializeSealed"/> (or older plain JSON) and decrypts the secret fields.</summary>
    public static T? DeserializeOpened<T>(string json)
    {
        var node = JsonNode.Parse(json);
        Walk(node, Open);
        return node == null ? default : node.Deserialize<T>();
    }

    static void Walk(JsonNode? node, Func<string?, string?> f)
    {
        switch (node)
        {
            case JsonArray a:
                foreach (var item in a) Walk(item, f);
                break;
            case JsonObject o:
                foreach (var name in RecordFields)
                    if (o[name] is JsonValue v && v.TryGetValue<string>(out var s)) o[name] = f(s);
                if (o["Proxy"] is JsonObject proxy && proxy["Password"] is JsonValue pv && pv.TryGetValue<string>(out var ps))
                    proxy["Password"] = f(ps);
                if (o["HostProfiles"] is JsonArray profiles) Walk(profiles, f);
                if (o["Automation"] is JsonObject auto && auto["Key"] is JsonValue kv && kv.TryGetValue<string>(out var ks)) auto["Key"] = f(ks);  // each site's login and proxy password
                break;
        }
    }

    /// <summary>CryptProtectData / CryptUnprotectData for the current user, with an app-specific entropy.</summary>
    sealed class Dpapi : ISecretProtector
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Nexonic Download Manager");

        public string? Protect(string plain)
        {
            var output = Run(Encoding.UTF8.GetBytes(plain), protect: true);
            return output == null ? null : Convert.ToBase64String(output);
        }

        public string? Unprotect(string sealedText)
        {
            try
            {
                var output = Run(Convert.FromBase64String(sealedText), protect: false);
                return output == null ? null : Encoding.UTF8.GetString(output);
            }
            catch (FormatException) { return null; }
        }

        static byte[]? Run(byte[] data, bool protect)
        {
            var input = new Blob(data);
            var entropy = new Blob(Entropy);
            var output = default(Blob);
            try
            {
                const int UiForbidden = 0x1;
                var ok = protect
                    ? CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
                if (!ok || output.Data == IntPtr.Zero) return null;
                var bytes = new byte[output.Size];
                Marshal.Copy(output.Data, bytes, 0, output.Size);
                return bytes;
            }
            finally
            {
                input.Free();
                entropy.Free();
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Blob
        {
            public int Size;
            public IntPtr Data;

            public Blob(byte[] bytes)
            {
                Size = bytes.Length;
                Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
                Marshal.Copy(bytes, 0, Data, bytes.Length);
            }

            public void Free()
            {
                if (Data != IntPtr.Zero) Marshal.FreeHGlobal(Data);
                Data = IntPtr.Zero;
            }
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref Blob dataIn, string? description, ref Blob entropy, IntPtr reserved,
            IntPtr prompt, int flags, ref Blob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr description, ref Blob entropy, IntPtr reserved,
            IntPtr prompt, int flags, ref Blob dataOut);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr mem);
    }
}

/// <summary>Saves app data so a crash or power cut never leaves a half-written list, and never drops an unreadable one.</summary>
public static class SafeFile
{
    /// <summary>Writes to a temporary file first, then replaces the old file in one step.</summary>
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Reads a saved list. A file that can't be read is kept as "name.unreadable-DATE.json" (instead of being overwritten
    /// by an empty list on the next save) and the default is returned.
    /// </summary>
    public static T? Load<T>(string path, Func<string, T?> parse)
    {
        if (!File.Exists(path)) return default;
        try { return parse(File.ReadAllText(path)); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException or ArgumentException
                                       or IOException or UnauthorizedAccessException)
        {
            try { File.Copy(path, Path.ChangeExtension(path, $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); } catch { }
            return default;
        }
    }
}
