using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Shows every window in the chosen language: when a window or menu appears, its texts (labels, buttons, menus,
/// column headers, tips) are swapped for their translation. Texts without a translation stay in English.
/// Changing the language in Options re-translates every open window right away (<see cref="Refresh"/>).
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
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => Apply((DependencyObject)s)));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, _) => Apply((DependencyObject)s)));
    }

    public static void Apply(DependencyObject o)
    {
        switch (o)
        {
            case Window w:
                w.Title = T(w.Title ?? "");
                w.FlowDirection = L.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                break;
            case TextBlock t when t.Inlines.Count <= 1 && t.Inlines.FirstInline is null or Run:
                if (!string.IsNullOrEmpty(t.Text)) t.Text = T(t.Text);
                break;
            case HeaderedItemsControl h when h.Header is string hs:
                h.Header = T(hs);
                break;
            case HeaderedContentControl h when h.Header is string hs:
                h.Header = T(hs);
                break;
            case ContentControl c when c.Content is string cs:
                c.Content = T(cs);
                break;
            case DataGrid g:
                foreach (var col in g.Columns) if (col.Header is string ch) col.Header = T(ch);
                break;
        }
        if (o is FrameworkElement fe)
        {
            if (fe.ToolTip is string tip) fe.ToolTip = T(tip);
            if (fe.ContextMenu != null) Apply(fe.ContextMenu);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>().ToList()) Apply(child);
    }

    /// <summary>Shows every open window in the language now in use.</summary>
    public static void Refresh()
    {
        foreach (Window w in Application.Current.Windows) Apply(w);
    }
}
