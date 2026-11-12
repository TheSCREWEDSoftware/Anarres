using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;

namespace Anarres.App;

// A field found by the finder: its label, where it is, and the words it's found by
public sealed record FoundField(string Label, string Where, string Words, Control Target)
{
    public override string ToString()
    {
        return Label + "   ·   " + Where;
    }
}

// Ctrl+F in a spell: finds a field or flag by label, column, notes or SPELL_ATTR name, and shows it
public sealed class FieldFinder
{
    private const int MAX_RESULTS = 40;
    private static readonly TimeSpan FlashTime = TimeSpan.FromSeconds(1.6);

    private readonly Func<Control?> _root;
    private List<FoundField>? _fields;

    public FieldFinder(Func<Control?> root)
    {
        _root = root;
        Box = new AutoCompleteBox
        {
            Watermark = "Find a field (Ctrl+F)",
            Width = 240,
            FilterMode = AutoCompleteFilterMode.Custom,
            ItemFilter = Matches,
            MinimumPrefixLength = 1,
            MaxDropDownHeight = 420,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        ToolTip.SetTip(Box, "Find a field or flag by name, column, notes or SPELL_ATTR name, such as school, " +
                            "cooldown, range, passive or SPELL_ATTR0_PASSIVE. Ctrl+F again goes to the spell search.");
        Box.GotFocus += (_, _) => Box.ItemsSource ??= Index();
        Box.SelectionChanged += (_, _) =>
        {
            if (Box.SelectedItem is not FoundField found)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                Box.Text = string.Empty;
                Show(found);
            });
        };
        Box.KeyDown += (_, e) =>
        {
            // Enter takes the best match when none is highlighted
            if (e.Key != Key.Enter || Box.SelectedItem != null || Box.Text is not { Length: > 0 } text)
                return;
            FoundField? best = Find(text).FirstOrDefault();
            if (best == null)
                return;
            e.Handled = true;
            Box.Text = string.Empty;
            Show(best);
        };
    }

    public AutoCompleteBox Box { get; }

    // The editor was rebuilt, so the fields are found afresh next time
    public void Forget()
    {
        _fields = null;
        Box.ItemsSource = null;
    }

    // Best first: the label itself matches, then the column or the tab, then only the notes
    public List<FoundField> Find(string text)
    {
        return Index().Where(field => Matches(text, field)).OrderBy(field => Rank(text, field)).Take(MAX_RESULTS)
                      .ToList();
    }

    private static int Rank(string text, FoundField field)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.All(word => field.Label.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return 0;
        return words.All(word => (field.Where + " " + field.Words[..Math.Min(field.Words.Length, 200)])
                              .Contains(word, StringComparison.OrdinalIgnoreCase)) ? 1 : 2;
    }

    // Every word typed must appear somewhere in the field's words
    private static bool Matches(string? text, object? item)
    {
        if (item is not FoundField field || string.IsNullOrWhiteSpace(text))
            return false;
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .All(word => field.Words.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    // Labelled rows with a tooltip and flag bits, through the whole editor including hidden tabs
    private List<FoundField> Index()
    {
        if (_fields != null)
            return _fields;
        List<FoundField> fields = new List<FoundField>();
        Control? root = _root();
        if (root == null)
            return fields;
        foreach (Control control in root.GetLogicalDescendants().OfType<Control>())
        {
            if (ToolTip.GetTip(control) is not string tip)
                continue;
            string? label = Label(control);
            if (string.IsNullOrWhiteSpace(label))
                continue;
            string where = Where(control);
            fields.Add(new FoundField(label, where, label + " " + tip + " " + where, control));
        }

        _fields = fields;
        return fields;
    }

    private static string? Label(Control control)
    {
        // A flag's row: its switch, its bit value, then its name
        if (control is Panel bit && bit.Classes.Contains(FlagToggles.BIT_CLASS))
            return bit.Children.OfType<TextBlock>().LastOrDefault()?.Text;
        if (control is not StackPanel { Orientation: Avalonia.Layout.Orientation.Horizontal } panel ||
            panel.Children.Count < 2)
            return null;
        return panel.Children[0] is TextBlock { Text.Length: > 0 } label ? label.Text : null;
    }

    // The tabs and the group a field sits in, outermost first
    private static string Where(Control control)
    {
        List<string> parts = new List<string>();
        bool groupFound = false;
        foreach (ILogical ancestor in control.GetLogicalAncestors())
        {
            if (ancestor is TabItem { Header: string header })
                parts.Insert(0, header);
            else if (!groupFound && ancestor is Border { Child: StackPanel panel } border &&
                     border.Classes.Contains("group") && panel.Children.Count > 0 &&
                     panel.Children[0] is TextBlock { Text: { } title })
            {
                parts.Insert(0, title);
                groupFound = true;
            }
        }

        return string.Join(" › ", parts);
    }

    // Opens every tab around the field, scrolls to it, puts the cursor in it and flashes it
    private static void Show(FoundField found)
    {
        foreach (ILogical ancestor in found.Target.GetLogicalAncestors())
        {
            if (ancestor is TabItem item && item.Parent is TabControl tabs)
                tabs.SelectedItem = item;
        }

        Dispatcher.UIThread.Post(() =>
        {
            found.Target.BringIntoView();
            Control? input = found.Target.GetLogicalDescendants().OfType<Control>()
                                  .FirstOrDefault(control => control is TextBox or ToggleSwitch or ComboBox or Button);
            input?.Focus();
            if (found.Target is Panel panel)
                Flash(panel);
        }, DispatcherPriority.Background);
    }

    private static void Flash(Panel panel)
    {
        IBrush? before = panel.Background;
        panel.Background = new SolidColorBrush(ThemeBrushes.Accent is ISolidColorBrush accent ? accent.Color
                                                                                               : Colors.MediumPurple,
                                               0.25);
        DispatcherTimer timer = new DispatcherTimer { Interval = FlashTime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            panel.Background = before;
        };
        timer.Start();
    }
}
