using System.Text.RegularExpressions;
using Nexonic.Core;
using Xunit;

namespace Nexonic.Tests;

[Collection("Language")]
public class LocalizationTests
{
    [Fact]
    public void EveryLanguageFileMatchesTheEnglishTexts()
    {
        var english = L.Read("en")!.Strings.Keys.ToHashSet();
        Assert.True(english.Count > 50);
        foreach (var lang in L.Languages.Where(l => l.Code != "en"))
        {
            var file = L.Read(lang.Code);
            Assert.NotNull(file);
            Assert.Equal(lang.Name, file!.Language);
            if (!lang.Reviewed) Assert.Contains("review", file.Status);
            foreach (var (en, translated) in file.Strings)
            {
                Assert.Contains(en, english);
                // Shortcuts, the app's name and the ellipsis of "…" menu items must survive translation.
                foreach (Match m in Regex.Matches(en, @"\((Ctrl\+\w|F1)\)|Nexonic Download Manager|…$"))
                    Assert.Contains(m.Value.TrimStart('(').TrimEnd(')'), translated);
            }
        }
    }

    [Fact]
    public void FallsBackToEnglish()
    {
        try
        {
            L.Use("de");
            Assert.Equal("de", L.Code);
            Assert.Equal("Abbrechen", L.T("Cancel"));
            Assert.Equal("Something not translated", L.T("Something not translated"));
            L.Use("ur");
            Assert.True(L.RightToLeft);
            L.Use("xx");
            Assert.Equal("en", L.Code);
            Assert.Equal("Cancel", L.T("Cancel"));
        }
        finally { L.Use("en"); }
    }
}
