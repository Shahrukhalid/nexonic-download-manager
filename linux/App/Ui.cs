using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>Small building blocks shared by the windows, so each window reads top to bottom.</summary>
public static class Ui
{
    public static readonly IBrush Accent = Brush(StatusColors.Accent);
    public static readonly IBrush Good = Brush(StatusColors.Good);
    public static readonly IBrush Bad = Brush(StatusColors.Bad);
    public static readonly IBrush Waiting = Brush(StatusColors.Waiting);
    public static readonly IBrush Idle = Brush(StatusColors.Idle);
    /// <summary>A light grey that works on light and dark backgrounds.</summary>
    public static readonly IBrush Shade = Brush("#1F808080");
    public static readonly IBrush Line = Brush("#33808080");

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    static Bitmap? icon;
    /// <summary>The app icon (256 px).</summary>
    public static Bitmap AppIcon => icon ??= Resource("icon.png");
    public static WindowIcon WindowIcon => new(AppIcon);

    public static Bitmap Resource(string name)
    {
        using var s = typeof(Ui).Assembly.GetManifestResourceStream(name)!;
        return new Bitmap(s);
    }

    public static TextBlock Muted(string text, double size = 0)
    {
        var t = new TextBlock { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        if (size > 0) t.FontSize = size;
        return t;
    }

    public static TextBlock Title(string text, double size = 18) =>
        new() { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };

    public static Button Button(string text, Action click, bool isDefault = false, bool isCancel = false)
    {
        var b = new Button { Content = text, MinWidth = 96, Margin = new Thickness(6, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (isDefault) b.Classes.Add("accent");
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A link that opens in the browser.</summary>
    public static Button Link(string text, string url) => LinkButton(text, () => LinuxSystem.Open(url));

    public static Button LinkButton(string text, Action click)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, Foreground = Accent, TextDecorations = TextDecorations.Underline },
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) => click();
        return b;
    }

    public static string Bytes(long n) => Row.Bytes(n);

    /// <summary>A coloured badge with the file's extension, in place of the system's file icons.</summary>
    public static Control FileBadge(string fileName, double size = 32)
    {
        var category = FileCategories.Of(fileName);
        var ext = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
        if (ext.Length is 0 or > 4) ext = category switch
        {
            FileCategory.Video => "VID", FileCategory.Music => "AUD", FileCategory.Images => "IMG",
            FileCategory.Documents => "DOC", FileCategory.Compressed => "ZIP", FileCategory.Programs => "APP", _ => "FILE",
        };
        var color = category switch
        {
            FileCategory.Video => "#8250DF", FileCategory.Music => "#BF3989", FileCategory.Images => "#1A7F37",
            FileCategory.Documents => "#0969DA", FileCategory.Compressed => "#9A6700", FileCategory.Programs => "#CF222E", _ => "#6E7781",
        };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 6), Background = Brush(color),
            Child = new TextBlock
            {
                Text = ext, Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = Math.Max(7, size / (ext.Length >= 4 ? 4.2 : 3.4)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>A simple line icon drawn from path data on a 24 × 24 grid.</summary>
    public static PathIcon Icon(string data, double size = 20) => new() { Data = Geometry.Parse(data), Width = size, Height = size };

    /// <summary>Icon paths (drawn for this app). Holes are drawn the other way round, so they show with the non-zero rule.</summary>
    public static class Icons
    {
        const string Ring = "M12,2 A10,10 0 1 1 11.99,2 Z M12,4 A8,8 0 1 0 12.01,4 Z ";
        public const string Add = "F1 M11,5 H13 V11 H19 V13 H13 V19 H11 V13 H5 V11 H11 Z";
        public const string Play = "F1 M8,5 L19,12 L8,19 Z";
        public const string Pause = "F1 M7,5 H10 V19 H7 Z M14,5 H17 V19 H14 Z";
        public const string StopAll = "F1 M6,6 H18 V18 H6 Z";
        public const string SelectAll = "F1 M4,4 H20 V20 H4 Z M6,6 V18 H18 V6 Z M7.5,12 L9,10.5 L11,12.5 L15.5,8 L17,9.5 L11,15.5 Z";
        public const string Delete = "F1 M9,3 H15 V5 H20 V7 H4 V5 H9 Z M6,8 H18 L17,21 H7 Z M9,10 V19 H10.5 V10 Z M13.5,10 V19 H15 V10 Z";
        public const string Clear = "F1 M3,12.5 L5,10.5 L9,14.5 L17,6.5 L19,8.5 L9,18.5 Z";
        public const string Clock = "F1 " + Ring + "M11,7 H13 V11.6 L16,14.6 L14.6,16 L11,12.4 Z";
        public const string StartQueue = "F1 M3,6 H11 V8 H3 Z M3,11 H11 V13 H3 Z M3,16 H11 V18 H3 Z M14,6 L21,12 L14,18 Z";
        public const string StopQueue = "F1 M3,6 H11 V8 H3 Z M3,11 H11 V13 H3 Z M3,16 H11 V18 H3 Z M14,7 H20 V17 H14 Z";
        public const string Globe = "F1 " + Ring + "M11,3 H13 V21 H11 Z M3,11 H21 V13 H3 Z";
        public const string Batch = "F1 M9,5 H21 V7 H9 Z M9,11 H21 V13 H9 Z M9,17 H21 V19 H9 Z M3,4 H6 V8 H3 Z M3,10 H6 V14 H3 Z M3,16 H6 V20 H3 Z";
        public const string Options = "F1 M3,6 H21 V8 H3 Z M3,11 H21 V13 H3 Z M3,16 H21 V18 H3 Z M7,4.5 H10 V9.5 H7 Z M14,9.5 H17 V14.5 H14 Z M9,14.5 H12 V19.5 H9 Z";
        public const string Help = "F1 " + Ring + "M9,7 H15 V9 H9 Z M13,9 H15 V12 H13 Z M11,11 H15 V13 H11 Z M11,13 H13 V14.5 H11 Z M11,16 H13 V18 H11 Z";
        public const string Info = "F1 " + Ring + "M11,10 H13 V17 H11 Z M11,6.5 H13 V8.5 H11 Z";
        public const string Search = "F1 M10,3 A7,7 0 1 1 9.99,3 Z M10,5 A5,5 0 1 0 10.01,5 Z M14.5,16 L16,14.5 L21,19.5 L19.5,21 Z";
        public const string Download = "F1 M11,3 H13 V13 L16.5,9.5 L18,11 L12,17 L6,11 L7.5,9.5 L11,13 Z M4,19 H20 V21 H4 Z";
    }

    /// <summary>Asks a question in a small window; true when the first (default) button is chosen.</summary>
    public static async Task<bool> Confirm(Window? owner, string title, string text, string yes = "OK", string no = "Cancel")
    {
        var result = false;
        var w = new Window
        {
            Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false, Icon = WindowIcon,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Button(no, w.Close, isCancel: true));
        buttons.Children.Add(Button(yes, () => { result = true; w.Close(); }, isDefault: true));
        w.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { Title(title, 15), new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }, buttons },
        };
        await ShowModal(w, owner);
        return result;
    }

    /// <summary>A message with one button.</summary>
    public static Task Message(Window? owner, string title, string text)
    {
        var w = new Window
        {
            Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false, Icon = WindowIcon,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Button("OK", w.Close, isDefault: true, isCancel: true));
        w.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { Title(title, 15), new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }, buttons },
        };
        return ShowModal(w, owner);
    }

    /// <summary>Modal over a visible owner; otherwise a normal window. Completes when the window closes.</summary>
    public static Task ShowModal(Window w, Window? owner)
    {
        if (owner is { IsVisible: true }) return w.ShowDialog(owner);
        var done = new TaskCompletionSource();
        w.Closed += (_, _) => done.TrySetResult();
        w.Show();
        w.Activate();
        return done.Task;
    }

    /// <summary>Lets the user pick a folder; null when cancelled.</summary>
    public static async Task<string?> PickFolder(Window owner, string current)
    {
        try
        {
            var sp = owner.StorageProvider;
            var start = Directory.Exists(current) ? await sp.TryGetFolderFromPathAsync(current) : null;
            var picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Save to", AllowMultiple = false, SuggestedStartLocation = start });
            return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        }
        catch { return null; }
    }

    /// <summary>The clipboard text when it is a download link, else "".</summary>
    public static async Task<string> ClipboardLink(TopLevel? top)
    {
        try
        {
            var t = (top?.Clipboard is { } c ? await c.GetTextAsync() : null)?.Trim() ?? "";
            return new DownloadJob(t).IsValid ? t : "";
        }
        catch { return ""; }
    }

    public static int Number(string? text, int fallback = 0) => int.TryParse((text ?? "").Trim(), out var n) && n >= 0 ? n : fallback;
}

/// <summary>
/// A small form window built in code: a title, fields one under the other and buttons at the bottom right.
/// </summary>
public sealed class Form
{
    public readonly Window Window;
    public readonly StackPanel Body = new() { Margin = new Thickness(20, 16, 20, 16) };
    readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
    readonly Window? owner;

    public Form(Window? owner, string title, double width = 520)
    {
        this.owner = owner;
        Body.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        Window = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false, Icon = Ui.WindowIcon,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 760 },
        };
    }

    public TextBlock Note(string text)
    {
        var t = Ui.Muted(text);
        t.Margin = new Thickness(0, 0, 0, 8);
        Body.Children.Add(t);
        return t;
    }

    public TextBlock Error()
    {
        var t = new TextBlock { Foreground = Ui.Bad, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        Body.Children.Add(t);
        return t;
    }

    public T Field<T>(string label, T control) where T : Control
    {
        Body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        Body.Children.Add(control);
        return control;
    }

    public TextBox Text(string label, string value = "", bool multi = false, bool password = false)
    {
        var box = new TextBox { Text = value };
        if (password) box.PasswordChar = '•';
        if (multi)
        {
            box.AcceptsReturn = true;
            box.Height = 110;
            box.TextWrapping = TextWrapping.NoWrap;
            box.FontFamily = new FontFamily("monospace");
        }
        return Field(label, box);
    }

    public CheckBox Check(string label, bool value)
    {
        var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 0) };
        Body.Children.Add(c);
        return c;
    }

    /// <summary>The queues by name; <see cref="QueueId"/> reads the choice.</summary>
    public ComboBox Queue(DownloadManager m, string selected) => Field("Queue", QueueBox(m, selected));

    public static ComboBox QueueBox(DownloadManager m, string selected)
    {
        var queues = m.Queues.ToList();
        var box = new ComboBox { ItemsSource = queues.Select(q => q.Name).ToList(), MinWidth = 220, Tag = queues };
        var i = queues.FindIndex(q => q.Id == selected);
        box.SelectedIndex = i >= 0 ? i : 0;
        return box;
    }

    public static string QueueId(ComboBox box) =>
        box.Tag is List<DownloadQueue> queues && box.SelectedIndex >= 0 && box.SelectedIndex < queues.Count ? queues[box.SelectedIndex].Id : DownloadQueue.MainId;

    /// <summary>A folder field with a Choose… button.</summary>
    public TextBox Folder(string value)
    {
        var (row, box) = FolderRow(Window, value);
        Field("Save to", row);
        return box;
    }

    public static (Control Row, TextBox Box) FolderRow(Window window, string value)
    {
        var box = new TextBox { Text = value };
        var pick = new Button { Content = "Choose…", Margin = new Thickness(6, 0, 0, 0) };
        pick.Click += async (_, _) => { if (await Ui.PickFolder(window, box.Text ?? "") is { } f) box.Text = f; };
        var row = new DockPanel();
        DockPanel.SetDock(pick, Dock.Right);
        row.Children.Add(pick);
        row.Children.Add(box);
        return (row, box);
    }

    public Button AddButton(string text, Action click, bool isDefault = false, bool isCancel = false)
    {
        var b = Ui.Button(text, click, isDefault, isCancel);
        buttons.Children.Add(b);
        return b;
    }

    public Task Show()
    {
        Body.Children.Add(buttons);
        return Ui.ShowModal(Window, owner);
    }

    public void Close() => Window.Close();
}
