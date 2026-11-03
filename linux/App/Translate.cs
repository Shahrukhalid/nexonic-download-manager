using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Nexonic.Core;

namespace Nexonic.App;

/// <summary>
/// Shows every window in the chosen language: when a window appears, its texts (labels, buttons, menus, tips) are
/// swapped for their translation. Texts without a translation stay in English.
/// </summary>
public static class Translate
{
    public static void Install()
    {
        if (L.Code == "en") return;
        Control.LoadedEvent.AddClassHandler<Window>((w, _) => Apply(w));
    }

    public static void Apply(ILogical o)
    {
        switch (o)
        {
            case Window w:
                w.Title = L.T(w.Title ?? "");
                if (L.RightToLeft) w.FlowDirection = FlowDirection.RightToLeft;
                break;
            case TextBlock t when t.Inlines == null || t.Inlines.Count == 0:
                if (!string.IsNullOrEmpty(t.Text)) t.Text = L.T(t.Text);
                break;
            case HeaderedSelectingItemsControl h when h.Header is string hs:
                h.Header = L.T(hs);
                break;
            case HeaderedContentControl h when h.Header is string hs:
                h.Header = L.T(hs);
                break;
            case ContentControl c when c.Content is string cs:
                c.Content = L.T(cs);
                break;
            case TextBox box when box.Watermark is { } wm:
                box.Watermark = L.T(wm);
                break;
        }
        if (o is Control control)
        {
            if (ToolTip.GetTip(control) is string tip) ToolTip.SetTip(control, L.T(tip));
            if (control.ContextMenu != null) Apply(control.ContextMenu);
        }
        foreach (var child in o.GetLogicalChildren().ToList()) Apply(child);
    }
}
