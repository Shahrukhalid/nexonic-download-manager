using Nexonic.Core;
using Xunit;

/// <summary>Custom categories: icons, site rules, order and the Options text. In the "Settings" collection: these tests
/// change the shared list of custom categories, so they must not run at the same time as the others that do.</summary>
[Collection("Settings")]
public class CategoryRuleTests
{
    [Fact]
    public void SitesAndIconsRoundTripThroughTheOptionsText()
    {
        var text = "📚 Books: epub pdf > Books\nReleases: zip @ github.com/*/releases > Releases\n🎨 Art: @ *.deviantart.com pixiv.net";
        var (list, problem) = CustomCategory.ParseLines(text);
        Assert.Null(problem);
        Assert.Equal(3, list.Count);
        Assert.Equal(("📚", "Books"), (list[0].Icon, list[0].Name));
        Assert.Equal("github.com/*/releases", list[1].Sites);
        Assert.Equal(("🎨", "Art", ""), (list[2].Icon, list[2].Name, list[2].Extensions));
        Assert.Equal(text, CustomCategory.ToLines(list));
        Assert.Equal(("", "C# Notes"), CustomCategory.SplitIcon("C# Notes"));
    }

    [Fact]
    public void FirstMatchingCategoryInOrderWins()
    {
        var (list, _) = CustomCategory.ParseLines(
            "Releases: zip @ https://GitHub.com/*/releases > Releases\nZips: zip > Zips\nArt: @ *.deviantart.com pixiv.net > Art");
        var keep = FileCategories.Custom;
        try
        {
            FileCategories.Custom = list;
            Assert.Equal("Releases", FileCategories.NameOf("a.zip", "https://github.com/o/r/releases/download/v1/a.zip"));
            Assert.Equal("Zips", FileCategories.NameOf("a.zip", "https://github.com/o/r/archive/a.zip"));
            Assert.Equal("Zips", FileCategories.NameOf("a.zip"));
            Assert.Equal("Art", FileCategories.NameOf("x.png", "https://images.deviantart.com/x.png"));
            Assert.Equal("Art", FileCategories.NameOf("x", "https://www.pixiv.net/artworks/1"));
            Assert.Equal("Images", FileCategories.NameOf("x.png", "https://notpixiv.net/x.png"));
            Assert.Equal("Images", FileCategories.NameOf("x.png", "file:///pixiv.net/x.png"));
            Assert.Equal(Path.Combine("/dl", "Art"), FileCategories.TargetFolder("/dl", "x.jpg", false, "https://pixiv.net/a"));

            // Reordering is reordering the lines.
            FileCategories.Custom = CustomCategory.ParseLines("Zips: zip\nReleases: zip @ github.com/*/releases").List;
            Assert.Equal("Zips", FileCategories.NameOf("a.zip", "https://github.com/o/r/releases/a.zip"));
        }
        finally { FileCategories.Custom = keep; }
    }

    [Fact]
    public void BadSiteRulesAreReported()
    {
        Assert.Contains("Line 1", CustomCategory.ParseLines("Odd: @ ").Problem!);
        Assert.Contains("Line 2", CustomCategory.ParseLines("A: zip\nB: zip @ bad\\\\host").Problem!);
        Assert.False(CustomCategory.SiteMatches("example.com", "not a url"));
        Assert.True(CustomCategory.SiteMatches("example.com", "https://cdn.example.com/f"));
        Assert.False(CustomCategory.SiteMatches("example.com", "https://badexample.com/f"));
    }
}
