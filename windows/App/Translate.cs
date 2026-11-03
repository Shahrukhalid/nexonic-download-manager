using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Shows every window in the chosen language: when a window or menu appears, its texts (labels, buttons, menus,
/// column headers, tips) are swapped for their translation. Texts without a translation stay in English.
/// </summary>
public static class Translate
{
    public static void Install()
    {
        if (L.Code == "en") return;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => Apply((DependencyObject)s)));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, _) => Apply((DependencyObject)s)));
    }

    public static void Apply(DependencyObject o)
    {
        switch (o)
        {
            case Window w:
                w.Title = L.T(w.Title ?? "");
                if (L.RightToLeft) w.FlowDirection = FlowDirection.RightToLeft;
                break;
            case TextBlock t when t.Inlines.Count <= 1 && t.Inlines.FirstInline is null or Run:
                if (!string.IsNullOrEmpty(t.Text)) t.Text = L.T(t.Text);
                break;
            case HeaderedItemsControl h when h.Header is string hs:
                h.Header = L.T(hs);
                break;
            case HeaderedContentControl h when h.Header is string hs:
                h.Header = L.T(hs);
                break;
            case ContentControl c when c.Content is string cs:
                c.Content = L.T(cs);
                break;
            case DataGrid g:
                foreach (var col in g.Columns) if (col.Header is string ch) col.Header = L.T(ch);
                break;
        }
        if (o is FrameworkElement fe)
        {
            if (fe.ToolTip is string tip) fe.ToolTip = L.T(tip);
            if (fe.ContextMenu != null) Apply(fe.ContextMenu);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>().ToList()) Apply(child);
    }
}
