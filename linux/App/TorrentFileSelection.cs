using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Nexonic.Core;
using Nexonic.Linux;

namespace Nexonic.App;

/// <summary>Virtualized file choices; editing does not mutate a live download before Save.</summary>
internal sealed class TorrentFileSelection : UserControl
{
    static readonly int[] Values = { 0, 1, 4, 7 };
    sealed class Choice(TorrentFile file, int priority)
    {
        public TorrentFile File { get; } = file;
        public int Priority { get; set; } = priority;
    }
    List<Choice> choices = new();
    readonly ListBox list = new() { Height = 190 };
    readonly TextBlock summary = Ui.Muted("");
    public int[] Priorities => choices.Select(c => c.Priority).ToArray();
    public long SelectedSize => choices.Where(c => c.Priority > 0).Sum(c => c.File.Size);
    public TorrentFileSelection()
    {
        list.ItemTemplate = new FuncDataTemplate<Choice>((choice, _) =>
        {
            if (choice == null) return null;
            var row = new DockPanel { LastChildFill = true };
            var priority = new ComboBox { ItemsSource = new[] { "Skip", "Low", "Normal", "High" },
                SelectedIndex = Array.IndexOf(Values, choice.Priority), Width = 106, Margin = new Thickness(8, 0, 0, 0) };
            priority.SelectionChanged += (_, _) =>
            {
                if (priority.SelectedIndex < 0) return;
                choice.Priority = Values[priority.SelectedIndex];
                UpdateSummary();
            };
            DockPanel.SetDock(priority, Dock.Right); row.Children.Add(priority);
            var name = new TextBlock { Text = $"{choice.File.Path} ({Row.Bytes(choice.File.Size)})", TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            ToolTip.SetTip(name, choice.File.Path);
            row.Children.Add(name);
            return row;
        });
        Content = new StackPanel { Children = { list, summary } };
    }
    public void Load(TorrentMetadata? metadata)
    {
        var priorities = metadata?.Priorities();
        choices = metadata?.Files.Select((file, i) => new Choice(file, priorities![i])).ToList() ?? new();
        list.ItemsSource = choices;
        UpdateSummary();
    }
    void UpdateSummary() => summary.Text = $"{choices.Count(c => c.Priority > 0)} of {choices.Count} files selected · {Row.Bytes(SelectedSize)}";
}
