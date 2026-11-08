using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Shows every window in the chosen language: when a window appears, its texts (labels, buttons, menus, tips) are
/// swapped for their translation. Texts without a translation stay in English. Changing the language in Options
/// re-translates every open window right away (<see cref="Refresh"/>).
/// </summary>
public static class Translate
{
    /// <summary>Every translation shown so far, back to its English text, so a window can switch languages again.</summary>
    static readonly Dictionary<string, string> english = new();

    static string T(string shown)
    {
        var source = english.TryGetValue(shown, out var e) ? e : shown;
        var result = L.T(source);
        if (result != source) english[result] = source;
        return result;
    }

    public static void Install()
    {
        Control.LoadedEvent.AddClassHandler<Window>((w, _) => Apply(w));
    }

    public static void Apply(ILogical o)
    {
        switch (o)
        {
            case Window w:
                w.Title = T(w.Title ?? "");
                w.FlowDirection = L.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                break;
            case TextBlock t when t.Inlines == null || t.Inlines.Count == 0:
                if (!string.IsNullOrEmpty(t.Text)) t.Text = T(t.Text);
                break;
            case HeaderedSelectingItemsControl h when h.Header is string hs:
                h.Header = T(hs);
                break;
            case HeaderedContentControl h when h.Header is string hs:
                h.Header = T(hs);
                break;
            case ContentControl c when c.Content is string cs:
                c.Content = T(cs);
                break;
            case TextBox box when box.Watermark is { } wm:
                box.Watermark = T(wm);
                break;
        }
        if (o is Control control)
        {
            if (ToolTip.GetTip(control) is string tip) ToolTip.SetTip(control, T(tip));
            if (control.ContextMenu != null) Apply(control.ContextMenu);
        }
        foreach (var child in o.GetLogicalChildren().ToList()) Apply(child);
    }

    /// <summary>Shows every open window in the language now in use.</summary>
    public static void Refresh()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var w in desktop.Windows.ToList()) Apply(w);
    }
}
