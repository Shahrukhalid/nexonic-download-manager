using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Options → After Download → Drop target: a small round window above other windows. Links or text with links dropped
/// on it open the Download File Info window, as for a browser download; a .torrent file opens Add Torrent. Drag it
/// anywhere; double-click it to show the downloads.
/// </summary>
public sealed class DropTarget : Window
{
    static DropTarget? current;
    readonly DownloadManager manager;
    readonly Action<string> torrent;
    static string PlaceFile => System.IO.Path.Combine(AppPaths.Support, "drop-target.txt");

    public static void Update(DownloadManager m, Action show, Action<string> torrent)
    {
        if (!m.Settings.Finish.DropTarget) { current?.Close(); current = null; return; }
        if (current != null) return;
        current = new DropTarget(m, show, torrent);
        current.Show();
    }

    DropTarget(DownloadManager m, Action show, Action<string> torrent)
    {
        manager = m;
        this.torrent = torrent;
        Width = Height = 64;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; CanResize = false; ShowActivated = false;
        Title = "Nexonic drop target";
        ToolTip.SetTip(this, L.T("Drop links here to download them"));
        try
        {
            var p = File.ReadAllText(PlaceFile).Split(',');
            if (p.Length == 2 && int.TryParse(p[0], out var x) && int.TryParse(p[1], out var y)) Position = new PixelPoint(x, y);
        }
        catch { WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        var circle = new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(37, 99, 235)), Stroke = Brushes.White, StrokeThickness = 2, Margin = new Thickness(4) };
        var arrow = new TextBlock { Text = "↓", Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeight.Bold,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Content = new Grid { Children = { circle, arrow } };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { show(); return; }
            BeginMoveDrag(e);
        };
        PositionChanged += (_, _) => { try { File.WriteAllText(PlaceFile, $"{Position.X},{Position.Y}"); } catch { } };
        var hide = new MenuItem { Header = L.T("Hide Drop Target") };
        hide.Click += (_, _) => { manager.Settings.Finish.DropTarget = false; manager.ApplySettings(manager.Settings); };
        ContextMenu = new ContextMenu { Items = { hide } };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, (_, e) => { circle.Opacity = 0.7; e.DragEffects = DragDropEffects.Copy; });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => circle.Opacity = 1);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        AddHandler(DragDrop.DropEvent, (_, e) => { circle.Opacity = 1; Take(e.Data); });
    }

    void Take(IDataObject data)
    {
        var file = data.GetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null && p.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase));
        if (file != null) { torrent(file); return; }
        var text = data.GetText() ?? "";
        foreach (var link in LinkGrabber.Find(text).Take(50))
            DownloadWindows.ShowInfo(manager, new DownloadJob(link) { FromBrowser = true });
    }
}
