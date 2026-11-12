using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// What a spell chip shows: its name, icon and hover card
public readonly record struct SpellLook(string Name, IImage? Icon, string Card);

// Spells as a grid of icon-and-name chips, like Wowhead's affected spells; clicking one opens it
public sealed class SpellChips
{
    private const double CHIP_WIDTH = 300;
    private const double ICON_SIZE = 24;
    private const int MOST = 300;

    private readonly WrapPanel _panel = new WrapPanel();
    private readonly TextBlock _note = new TextBlock { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    private readonly Func<uint, SpellLook> _look;
    private readonly Action<uint> _open;

    public SpellChips(Func<uint, SpellLook> look, Action<uint> open)
    {
        _look = look;
        _open = open;
        View = Ui.Vertical(4, _panel, _note);
    }

    public Control View { get; }

    // spells with a short note each (such as which effect); emptyText when there are none
    public void Show(IReadOnlyList<(uint Id, string Note)> spells, string emptyText)
    {
        _panel.Children.Clear();
        foreach ((uint id, string note) in spells.Take(MOST))
            _panel.Children.Add(Chip(id, note));
        _note.Text = spells.Count == 0 ? emptyText
                     : spells.Count > MOST ? "And " + (spells.Count - MOST) + " more." : string.Empty;
        _note.IsVisible = _note.Text.Length > 0;
    }

    private Button Chip(uint id, string note)
    {
        SpellLook look = _look(id);
        StackPanel text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = look.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        string detail = id + (note.Length > 0 ? "  -  " + note : string.Empty);
        text.Children.Add(new TextBlock
        {
            Text = detail,
            Classes = { "hint" },
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new Border
        {
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Child = new Image { Source = look.Icon, Width = ICON_SIZE, Height = ICON_SIZE },
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(text);
        Button chip = new Button
        {
            Content = content,
            Width = CHIP_WIDTH,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Classes = { "subtle" },
            Margin = new Thickness(0, 0, 6, 4)
        };
        ToolTip.SetTip(chip, look.Card);
        chip.Click += (_, _) => _open(id);
        return chip;
    }
}
