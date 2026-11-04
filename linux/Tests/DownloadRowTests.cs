using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nexonic.App;
using Nexonic.Core;
using Nexonic.Linux;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Nexonic.Linux.Tests.TestAppBuilder))]

namespace Nexonic.Linux.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class TestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class DownloadRowTests
{
    [AvaloniaFact]
    public void TorrentFileChoicesChangeSelectedBytesWithoutMutatingTheSavedTask()
    {
        var metadata = new TorrentMetadata { Files = new() { new("a.bin", 100), new("b.bin", 200) } };
        var selection = new TorrentFileSelection();
        selection.Load(metadata);
        var window = new Window { Width = 640, Height = 300, Content = selection };
        window.Show();
        try
        {
            window.UpdateLayout();
            var choices = selection.GetVisualDescendants().OfType<ComboBox>().ToList();
            Assert.Equal(2, choices.Count);
            choices[0].SelectedIndex = 0; // Skip first file.
            Assert.Equal(new[] { 0, 4 }, selection.Priorities);
            Assert.Equal(200, selection.SelectedSize);
            Assert.Null(metadata.FilePriorities); // Cancel leaves the running record intact.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClearedDownloadHasNoRowVisual()
    {
        Assert.Null(MainWindow.RowTemplate.Build(null));
    }

    [AvaloniaFact]
    public void VisibleDownloadListCanBeRebuiltAndRenamed()
    {
        var rows = new System.Collections.ObjectModel.ObservableCollection<Row>();
        var list = new ListBox { ItemsSource = rows, ItemTemplate = MainWindow.RowTemplate };
        var window = new Window { Width = 1000, Height = 400, Content = list };
        Row Make(string name) => new(new DownloadRecord { FileName = name, Url = "http://127.0.0.1/" + name }, _ => "Main");
        var first = Make("first.bin");
        rows.Add(first);
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.NotNull(list.ContainerFromIndex(0));
            rows.Clear();
            rows.Add(first);
            rows.Add(Make("second.txt"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(list.ContainerFromIndex(1));
            first.Record.FileName = "renamed.zip";
            first.Refresh();
            rows.Clear();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); }
    }
}
