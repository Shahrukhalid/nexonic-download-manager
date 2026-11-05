using System.IO.Compression;
using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

public sealed class ArchiveTests
{
    [Fact]
    public void UnzipsIntoNewFolderAndSkipsEntriesOutsideIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ndm-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zip = Path.Combine(dir, "Photos.zip");
            using (var a = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(a.CreateEntry("a/b.txt").Open())) w.Write("hi");
                using (var w = new StreamWriter(a.CreateEntry("../outside.txt").Open())) w.Write("x");
                a.CreateEntry("empty/");
            }
            Assert.True(Archives.IsZip(zip));
            Assert.False(Archives.IsZip(Path.Combine(dir, "missing.zip")));
            var first = Archives.ExtractZip(zip);
            var second = Archives.ExtractZip(zip);
            Assert.Equal("Photos", Path.GetFileName(first));
            Assert.Equal("Photos (2)", Path.GetFileName(second));
            Assert.Equal("hi", File.ReadAllText(Path.Combine(first, "a", "b.txt")));
            Assert.True(Directory.Exists(Path.Combine(first, "empty")));
            Assert.False(File.Exists(Path.Combine(dir, "outside.txt")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void NewSettingsDefaultToToolbarShownAndUnzipOff()
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.True(s.ShowToolbar);
        Assert.True(s.ToolbarText);
        Assert.False(s.ExtractZips);
    }
}
