using Nexonic.Core;
using Xunit;

/// <summary>Pasted links: several at once, and the real file behind Google Images and other redirect links.</summary>
public class LinksTests
{
    const string Imgres = "https://www.google.com/imgres?q=desk%20wallpaper&imgurl=https%3A%2F%2Fstatic0.example.com%2Fuploads%2Fdesk-at-night.jpg%3Fw%3D1600%26h%3D900&imgrefurl=https%3A%2F%2Fwww.example.com%2Fpost%2F&docid=abc&w=1600&h=900";

    [Fact]
    public void GoogleImagesGivesThePicture()
    {
        Assert.Equal("https://static0.example.com/uploads/desk-at-night.jpg?w=1600&h=900", Links.Unwrap(Imgres));
        Assert.Equal("https://example.com/a.zip", Links.Unwrap("https://www.google.com/url?q=https://example.com/a.zip&sa=D"));
        Assert.Equal("https://example.com/b.pdf", Links.Unwrap("https://l.facebook.com/l.php?u=https%3A%2F%2Fexample.com%2Fb.pdf&h=x"));
        Assert.Equal("https://example.com/plain.zip", Links.Unwrap("https://example.com/plain.zip"));
        Assert.Equal("https://www.google.com/imgres?imgurl=javascript:alert(1)", Links.Unwrap("https://www.google.com/imgres?imgurl=javascript:alert(1)"));
        Assert.Equal("desk-at-night.jpg", FileNames.FromUrl(Links.Unwrap(Imgres)));
    }

    [Fact]
    public void SeveralLinksPastedTogether()
    {
        Assert.Equal(new[] { "https://a.example/1.jpg", "https://b.example/2.jpg", "https://c.example/x,y.zip" },
            Links.Split("https://a.example/1.jpg, https://b.example/2.jpg\n https://c.example/x,y.zip"));
    }

    [Fact]
    public void CategoryFolders()
    {
        Assert.Equal("Video", FileCategories.Folder(FileCategory.Video));
        Assert.Null(FileCategories.Folder(FileCategory.Other));  // stays in the download folder
        Assert.True(new AppSettings().SortIntoCategoryFolders);
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings());
        Assert.Contains("\"CategoryFolders\":true", json);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"SortIntoCategoryFolders\":false}")!.SortIntoCategoryFolders);
    }
}
