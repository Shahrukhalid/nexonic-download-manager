using Nexonic.Core;
using Xunit;

/// <summary>Colour schemes (readable by rule, import/export) and sounds per event with quiet hours.</summary>
public class LookAndSoundTests
{
    [Fact]
    public void EveryBuiltInSchemeIsReadable()
    {
        foreach (var s in ColorScheme.BuiltIn) Assert.Null(s.Problem());
        Assert.Null(new ColorScheme { Name = "Default" }.Problem());
        Assert.True(ColorScheme.Contrast("#000000", "#FFFFFF") > 20.9);
        Assert.Equal(1.0, Math.Round(ColorScheme.Contrast("#777777", "#777777"), 3));
        Assert.Equal("#808080", ColorScheme.Mix("#000000", "#FFFFFF", 0.5));
    }

    [Fact]
    public void SchemesRoundTripAndUnreadableOnesAreRefused()
    {
        var ocean = ColorScheme.BuiltIn[0];
        var (back, problem) = ColorScheme.Import(ocean.Export());
        Assert.Null(problem);
        Assert.Equal((ocean.Name, ocean.Accent, ocean.Base), (back!.Name, back.Accent, back.Base));
        Assert.Contains("too hard to read", ColorScheme.Import("""{"name":"Grey","text":"#999999","background":"#AAAAAA"}""").Problem);
        Assert.Contains("#RRGGBB", ColorScheme.Import("""{"name":"Bad","accent":"blue"}""").Problem);
        Assert.Contains("name", ColorScheme.Import("""{"name":""}""").Problem);
        Assert.Contains("not a colour scheme", ColorScheme.Import("[1,2]").Problem);
        Assert.Equal("light", ColorScheme.Import("""{"name":"X","base":"weird"}""").Scheme!.Base);
    }

    [Fact]
    public void SoundsFollowTheEventAndQuietHours()
    {
        var file = Path.GetTempFileName();
        try
        {
            var s = new AppSettings { SoundFinished = "", SoundFailed = file, SoundQueueDone = "none", QuietHours = "22:00-07:00" };
            var noon = new DateTime(2026, 11, 10, 12, 0, 0);
            Assert.Equal("", Sounds.Pick(s, Sounds.Event.Finished, noon));     // the system's sound
            Assert.Equal(file, Sounds.Pick(s, Sounds.Event.Failed, noon));
            Assert.Null(Sounds.Pick(s, Sounds.Event.QueueDone, noon));          // none
            Assert.Null(Sounds.Pick(s, Sounds.Event.Finished, noon.AddHours(11)));  // 23:00, quiet
            Assert.Null(Sounds.Pick(s, Sounds.Event.Finished, noon.AddHours(-6)));  // 06:00, quiet
            Assert.Null(Sounds.Pick(new AppSettings(), Sounds.Event.Failed, noon));  // failures are silent by default
            s.PlaySound = false;
            Assert.Null(Sounds.Pick(s, Sounds.Event.Finished, noon));
            s.PlaySound = true;
            s.SoundFailed = file + ".gone";
            Assert.Equal("", Sounds.Pick(s, Sounds.Event.Failed, noon));       // moved file: the system's sound
            Assert.Null(Sounds.Problem("", "22:00-07:00"));
            Assert.Null(Sounds.Problem("none", ""));
            Assert.Contains("quiet hours", Sounds.Problem("", "late"));
            Assert.Contains("no sound file", Sounds.Problem("/no/such.wav", ""));
        }
        finally { File.Delete(file); }
    }
}

public class ColumnLayoutTests
{
    [Fact]
    public void LayoutRoundTripsAndIgnoresNonsense()
    {
        var text = ColumnLayout.Format(new[] { ("0", 2, 230.4), ("1", 0, 80.0), ("2", 1, 100.0) });
        Assert.Equal("0=2:230;1=0:80;2=1:100", text);
        var back = ColumnLayout.Parse(text);
        Assert.Equal((2, 230.0), back["0"]);
        Assert.Equal(3, back.Count);
        var odd = ColumnLayout.Parse("a=1:5;b=x:80;c=99:80;;d=3:5000;e=2:120");
        Assert.True(double.IsNaN(odd["a"].Width));   // too narrow: the default width is kept
        Assert.True(double.IsNaN(odd["d"].Width));   // too wide
        Assert.False(odd.ContainsKey("b"));
        Assert.False(odd.ContainsKey("c"));          // index out of range
        Assert.Equal((2, 120.0), odd["e"]);
    }
}

public class CategoryEditorTests
{
    [Fact]
    public void MovesAndRemovesCategoryLinesKeepingComments()
    {
        const string text = "# mine\nA: zip\n\nB: pdf\nC: mp3";
        Assert.Equal(new[] { "A: zip", "B: pdf", "C: mp3" }, CustomCategory.Lines(text));
        Assert.Equal("# mine\nC: mp3\nA: zip\nB: pdf", CustomCategory.Move(text, 2, 0));
        Assert.Equal("# mine\nB: pdf\nA: zip\nC: mp3", CustomCategory.Move(text, 0, 1));
        Assert.Equal("# mine\nB: pdf\nC: mp3\nA: zip", CustomCategory.Move(text, 0, 99));   // past the end: last
        Assert.Equal(text, CustomCategory.Move(text, 7, 0));                                // nothing selected: unchanged
        Assert.Equal("# mine\nA: zip\nC: mp3", CustomCategory.Remove(text, 1));
        Assert.Equal(new[] { "C: mp3", "A: zip", "B: pdf" }, CustomCategory.ParseLines(CustomCategory.Move(text, 2, 0)).List.Select(c => c.Name + ": " + c.Extensions));
    }
}
