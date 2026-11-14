using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
    readonly Ellipse circle;

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
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; AllowDrop = true;
        ToolTip = L.T("Drop links here to download them");
        var area = SystemParameters.WorkArea;
        Left = area.Right - 96; Top = area.Top + 120;
        try
        {
            var p = System.IO.File.ReadAllText(PlaceFile).Split(',');
            if (p.Length == 2 && double.TryParse(p[0], System.Globalization.CultureInfo.InvariantCulture, out var l) && double.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var t)
                && l > SystemParameters.VirtualScreenLeft - 32 && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 32
                && t > SystemParameters.VirtualScreenTop - 32 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 32) { Left = l; Top = t; }
        }
        catch { }
        circle = new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(37, 99, 235)), Stroke = Brushes.White, StrokeThickness = 2, Margin = new Thickness(4) };
        var arrow = new TextBlock { Text = "↓", Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.Bold,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Content = new Grid { Children = { circle, arrow } };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { show(); return; }
            try { DragMove(); } catch (InvalidOperationException) { }
            try { System.IO.File.WriteAllText(PlaceFile, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Left},{Top}")); } catch { }
        };
        var menu = new ContextMenu();
        var hide = new MenuItem { Header = L.T("Hide Drop Target") };
        hide.Click += (_, _) => { manager.Settings.Finish.DropTarget = false; manager.ApplySettings(manager.Settings); };
        menu.Items.Add(hide);
        ContextMenu = menu;
        DragEnter += (_, e) => { circle.Opacity = 0.7; e.Effects = DragDropEffects.Copy; e.Handled = true; };
        DragLeave += (_, _) => circle.Opacity = 1;
        DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        Drop += (_, e) => { circle.Opacity = 1; Take(e.Data); };
    }

    void Take(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var file = files.FirstOrDefault(p => p.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase));
            if (file != null) { torrent(file); return; }
        }
        var text = data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string ?? "";
        foreach (var link in LinkGrabber.Find(text).Take(50))
            DownloadWindows.ShowInfo(manager, new DownloadJob(link) { FromBrowser = true });
    }

    // ---- the player Windows uses for a type, so the part already downloaded plays while the rest arrives ----

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int AssocQueryStringW(int flags, int str, string assoc, string? extra, StringBuilder? output, ref uint length);

    public static string? PlayerFor(string extension)
    {
        try
        {
            uint n = 1024;
            var sb = new StringBuilder((int)n);
            return AssocQueryStringW(0, 2 /* ASSOCSTR_EXECUTABLE */, extension, null, sb, ref n) == 0 && System.IO.File.Exists(sb.ToString()) ? sb.ToString() : null;
        }
        catch { return null; }
    }
}
