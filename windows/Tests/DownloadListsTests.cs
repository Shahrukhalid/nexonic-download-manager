using Nexonic.Core;
using Xunit;

/// <summary>Importing download lists saved by other download managers.</summary>
public class DownloadListsTests
{
    [Fact]
    public void ReadsAngleBracketLists()
    {
        const string text = "<\r\nhttps://cdn.test/a.zip\r\nreferer: https://site.test/page\r\ncookie: sid=1\r\nUser-Agent: Agent/1\r\n" +
                            "username: me\r\npassword: p:w\r\n>\r\n<\r\nhttps://cdn.test/b.iso\r\n>\r\n";
        var (jobs, format) = DownloadLists.Read(text);
        Assert.Equal(DownloadLists.Format.Blocks, format);
        Assert.Equal(2, jobs.Count);
        Assert.Equal("https://cdn.test/a.zip", jobs[0].Url);
        Assert.Equal("https://site.test/page", jobs[0].Referrer);
        Assert.Equal("sid=1", jobs[0].Cookies);
        Assert.Equal("Agent/1", jobs[0].UserAgent);
        Assert.Equal(("me", "p:w"), (jobs[0].Username, jobs[0].Password));
        Assert.Equal("https://cdn.test/b.iso", jobs[1].Url);
    }

    [Fact]
    public void ReadsInputFilesWithMirrorsAndOptions()
    {
        const string sum = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
        var text = "# my list\nhttps://a.test/f.bin\thttps://b.test/f.bin\n  out=../../evil/name.bin\n  dir=/home/x\n" +
                   "  header=Cookie: k=v\n  referer=https://r.test/\n  http-user=u\n  http-passwd=s\n  checksum=sha-256=" + sum.ToUpperInvariant() + "\n" +
                   "https://c.test/g.bin\n";
        var (jobs, format) = DownloadLists.Read(text);
        Assert.Equal(DownloadLists.Format.InputFile, format);
        Assert.Equal(2, jobs.Count);
        var j = jobs[0];
        Assert.Equal("https://a.test/f.bin", j.Url);
        Assert.Equal(new[] { "https://b.test/f.bin" }, j.Mirrors);
        Assert.Equal("name.bin", j.FileName);  // never a path, and never the other computer's folder
        Assert.Null(j.Folder);
        Assert.Equal("k=v", j.Cookies);
        Assert.Equal("https://r.test/", j.Referrer);
        Assert.Equal(("u", "s"), (j.Username, j.Password));
        Assert.Equal(sum, j.Checksum);
        Assert.Equal("https://c.test/g.bin", jobs[1].Url);
    }

    [Fact]
    public void ReadsJsonHtmlAndPlainText()
    {
        var (json, f1) = DownloadLists.Read("{\"downloads\":[{\"url\":\"https://j.test/1.mp4\",\"fileName\":\"one.mp4\"},{\"link\":\"https://j.test/2\"},{\"x\":1}]}");
        Assert.Equal(DownloadLists.Format.Json, f1);
        Assert.Equal(new[] { "https://j.test/1.mp4", "https://j.test/2" }, json.Select(j => j.Url));
        Assert.Equal("one.mp4", json[0].FileName);

        var (html, f2) = DownloadLists.Read("<!DOCTYPE NETSCAPE-Bookmark-file-1><DL><DT><A HREF=\"https://h.test/a?x=1&amp;y=2\">A</A><a href='javascript:alert(1)'>x</a>");
        Assert.Equal(DownloadLists.Format.Html, f2);
        Assert.Equal(new[] { "https://h.test/a?x=1&y=2" }, html.Select(j => j.Url));

        var (text, f3) = DownloadLists.Read("https://t.test/1.zip\nfile:///etc/passwd\nhttps://t.test/1.zip\nftp://t.test/2.zip");
        Assert.Equal(DownloadLists.Format.Text, f3);
        Assert.Equal(new[] { "https://t.test/1.zip" }, text.Select(j => j.Url));  // web links only, no duplicates
    }

    [Fact]
    public void NeverThrowsOnJunk()
    {
        foreach (var seed in new[] { "<", "<\n>", "<\nhttps://x.test\n", "[", "{\"url\":", "  out=x", "﻿", new string('<', 5000) })
            DownloadLists.Read(seed);
        // A fixed, repeatable sequence of junk (no random generator, so every run tests the same inputs).
        const string alphabet = "<>\n\t =:[]{}\"'ahtps/.#";
        for (var i = 0; i < 500; i++)
        {
            var chars = new char[(i * 37) % 400];
            for (var k = 0; k < chars.Length; k++) chars[k] = alphabet[(i * 31 + k * 17 + k * k) % alphabet.Length];
            DownloadLists.Read(new string(chars));
        }
    }
}
