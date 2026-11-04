namespace Nexonic.Core;

/// <summary>Resume metadata can include private tracker URLs. Keep bounded, atomic, owner-only checkpoints.</summary>
public static class TorrentCheckpoint
{
    public static void Write(string path, string data)
    {
        if (data.Length > 3 * 1024 * 1024 || Convert.FromBase64String(data).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Torrent checkpoint exceeds the size limit.");
        var parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var stream = new FileStream(temp, options))
            using (var writer = new StreamWriter(stream)) writer.Write(data);
            File.Move(temp, path, overwrite: true);
        }
        finally { try { File.Delete(temp); } catch { } }
    }
}
