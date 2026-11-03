using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Nexonic.Core;

/// <summary>
/// Linux: secrets are encrypted with AES-256-GCM. The key is created once and kept in the desktop's keyring (GNOME Keyring,
/// KWallet and others through the Secret Service, using <c>secret-tool</c> from libsecret), never in a file.
/// Without a keyring nothing secret is written to disk: it is kept in memory until the app closes.
/// </summary>
public sealed class KeyringProtector : ISecretProtector
{
    const string Attributes = "application nexonic-download-manager key master";
    readonly Func<string[], string?, (int Code, string Output, string Error)> run;
    readonly object gate = new();
    byte[]? key;
    bool failed;

    /// <summary><paramref name="runner"/> runs secret-tool (arguments, standard input) for tests; the real one by default.</summary>
    public KeyringProtector(Func<string[], string?, (int Code, string Output, string Error)>? runner = null) => run = runner ?? SecretTool;

    /// <summary>True when the keyring answered and the key is ready (asks the keyring the first time).</summary>
    public bool Available => Key() != null;

    public string? Protect(string plain)
    {
        if (Key() is not { } k) return null;
        var data = Encoding.UTF8.GetBytes(plain);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(k, 16)) aes.Encrypt(nonce, data, cipher, tag);
        var output = new byte[1 + 12 + 16 + cipher.Length];
        output[0] = 1; // format version
        nonce.CopyTo(output, 1);
        tag.CopyTo(output, 13);
        cipher.CopyTo(output, 29);
        return Convert.ToBase64String(output);
    }

    public string? Unprotect(string sealedText)
    {
        try
        {
            var input = Convert.FromBase64String(sealedText);
            if (input.Length < 29 || input[0] != 1 || Key() is not { } k) return null;
            var plain = new byte[input.Length - 29];
            using (var aes = new AesGcm(k, 16)) aes.Decrypt(input.AsSpan(1, 12), input.AsSpan(29), input.AsSpan(13, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { return null; }
    }

    byte[]? Key()
    {
        lock (gate)
        {
            if (key != null || failed) return key;
            key = Lookup();
            if (key == null && !failed)
            {
                // No key yet: make one and keep it in the keyring. Read it back so a keyring that silently drops it is noticed.
                var fresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var stored = run(new[] { "store", "--label=Nexonic Download Manager" }.Concat(Attributes.Split(' ')).ToArray(), fresh);
                key = stored.Code == 0 ? Lookup() : null;
            }
            if (key == null) failed = true;
            return key;
        }
    }

    /// <summary>The saved key; null when there is none. A keyring that is missing or locked marks the protector as failed,
    /// so a key is never replaced (that would lose every saved secret).</summary>
    byte[]? Lookup()
    {
        var r = run(new[] { "lookup" }.Concat(Attributes.Split(' ')).ToArray(), null);
        if (r.Code == 0)
        {
            try
            {
                var k = Convert.FromBase64String(r.Output.Trim());
                if (k.Length == 32) return k;
            }
            catch (FormatException) { }
            failed = true;
            return null;
        }
        if (r.Code != 1 || r.Error.Trim().Length > 0) failed = true; // 1 with no message: simply not stored yet
        return null;
    }

    static (int, string, string) SecretTool(string[] args, string? input)
    {
        try
        {
            var psi = new ProcessStartInfo("secret-tool")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (input != null) p.StandardInput.Write(input);
            p.StandardInput.Close();
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            // The keyring may ask the user to unlock it: give them time, but never hang forever.
            if (!p.WaitForExit(120_000)) { try { p.Kill(); } catch { } return (-1, "", "timed out"); }
            return (p.ExitCode, output.Result, error.Result);
        }
        catch (Exception e) { return (-1, "", e.Message); }
    }
}
