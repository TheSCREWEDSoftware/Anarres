using Anarres.Core.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// A small picture of Anarres in a theme's colours, and a strip of its main colours, for choosing a theme
public sealed class ThemePreview : Border
{
    private const double PREVIEW_HEIGHT = 176;
    private const double SIDEBAR_WIDTH = 140;
    private const double SWATCH_SIZE = 14;
    private const double SMALL_TEXT = 11;
    private const double TEXT = 12;
    private const byte SELECTION_ALPHA = 0x40;

    private static readonly string[] SwatchKeys =
    {
        ThemeColours.BACKGROUND, ThemeColours.SURFACE, ThemeColours.RAISED, ThemeColours.ACCENT, ThemeColours.TEXT
    };

    public ThemePreview()
    {
        Height = PREVIEW_HEIGHT;
        CornerRadius = new CornerRadius(ControlLook.OVERLAY_RADIUS);
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
    }

    // Draws the window again in the theme's colours
    public void Show(Theme theme)
    {
        Background = Brush(theme, ThemeColours.BACKGROUND);
        BorderBrush = Brush(theme, ThemeColours.BORDER);

        Grid window = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(10) };
        Border list = new Border
        {
            Background = Brush(theme, ThemeColours.SURFACE),
            BorderBrush = Brush(theme, ThemeColours.BORDER),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(6),
            Width = SIDEBAR_WIDTH,
            Margin = new Thickness(0, 0, 10, 0)
        };
        StackPanel spells = new StackPanel { Spacing = 2 };
        spells.Children.Add(Spell(theme, "133", "Fireball", false));
        spells.Children.Add(Spell(theme, "116", "Frostbolt", true));
        spells.Children.Add(Spell(theme, "10", "Blizzard", false));
        list.Child = spells;
        window.Children.Add(list);

        Border card = new Border
        {
            Background = Brush(theme, ThemeColours.SURFACE),
            BorderBrush = Brush(theme, ThemeColours.BORDER),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(10, 6)
        };
        Grid.SetColumn(card, 1);
        DockPanel content = new DockPanel();
        StackPanel heading = new StackPanel { Spacing = 0 };
        heading.Children.Add(Text(theme, "Frostbolt", ThemeColours.TEXT, TEXT + 2, FontWeight.SemiBold));
        heading.Children.Add(Text(theme, "Spell 116  -  Rank 1  -  Frost", ThemeColours.MUTED_TEXT, SMALL_TEXT,
                                  FontWeight.Normal));
        StackPanel tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                                           Margin = new Thickness(0, 6, 0, 0) };
        tabs.Children.Add(new Border
        {
            BorderBrush = Brush(theme, ThemeColours.ACCENT),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Child = Text(theme, "Base", ThemeColours.TEXT, TEXT, FontWeight.SemiBold)
        });
        tabs.Children.Add(Text(theme, "Effects", ThemeColours.MUTED_TEXT, TEXT, FontWeight.Normal));
        tabs.Children.Add(Text(theme, "Visual", ThemeColours.MUTED_TEXT, TEXT, FontWeight.Normal));
        heading.Children.Add(tabs);
        DockPanel.SetDock(heading, Dock.Top);
        content.Children.Add(heading);

        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        buttons.Children.Add(Text(theme, "Unsaved changes", ThemeColours.WARNING, SMALL_TEXT, FontWeight.Normal));
        buttons.Children.Add(PretendButton(theme, "Revert", ThemeColours.RAISED, ThemeColours.TEXT, true));
        buttons.Children.Add(PretendButton(theme, "Save", ThemeColours.ACCENT, ThemeColours.ACCENT_TEXT, false));
        DockPanel.SetDock(buttons, Dock.Bottom);
        content.Children.Add(buttons);

        StackPanel fields = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Spacing = 3 };
        fields.Children.Add(Field(theme, "Name", "Frostbolt"));
        fields.Children.Add(Field(theme, "Cast time", "3 sec"));
        content.Children.Add(fields);
        card.Child = content;
        window.Children.Add(card);
        Child = window;
    }

    // Background, panel, buttons, accent and text side by side, for the theme list
    public static Control Swatches(Theme theme)
    {
        StackPanel strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (string key in SwatchKeys)
            strip.Children.Add(new Border
            {
                Width = SWATCH_SIZE,
                Height = SWATCH_SIZE,
                Background = Brush(theme, key),
                BorderBrush = Brush(theme, ThemeColours.BORDER),
                BorderThickness = new Thickness(1)
            });
        return new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = strip };
    }

    private static Control Spell(Theme theme, string id, string name, bool selected)
    {
        Color accent = Color.Parse(theme.Colour(ThemeColours.ACCENT));
        StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        line.Children.Add(Text(theme, id, ThemeColours.MUTED_TEXT, SMALL_TEXT, FontWeight.Normal));
        line.Children.Add(Text(theme, name, ThemeColours.TEXT, TEXT, FontWeight.Normal));
        return new Border
        {
            Background = selected
                ? new SolidColorBrush(Color.FromArgb(SELECTION_ALPHA, accent.R, accent.G, accent.B))
                : Brushes.Transparent,
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(6, 3),
            Child = line
        };
    }

    private static Control Field(Theme theme, string label, string value)
    {
        StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        TextBlock name = Text(theme, label, ThemeColours.MUTED_TEXT, TEXT, FontWeight.Normal);
        name.Width = 70;
        row.Children.Add(name);
        row.Children.Add(new Border
        {
            Background = Brush(theme, ThemeColours.RAISED),
            BorderBrush = Brush(theme, ThemeColours.BORDER),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(6, 1),
            Width = 120,
            Child = Text(theme, value, ThemeColours.TEXT, TEXT, FontWeight.Normal)
        });
        return row;
    }

    private static Control PretendButton(Theme theme, string label, string background, string foreground, bool edged)
    {
        return new Border
        {
            Background = Brush(theme, background),
            BorderBrush = Brush(theme, ThemeColours.BORDER),
            BorderThickness = new Thickness(edged ? 1 : 0),
            CornerRadius = new CornerRadius(ControlLook.CONTROL_RADIUS),
            Padding = new Thickness(10, 3),
            Child = Text(theme, label, foreground, TEXT, FontWeight.SemiBold)
        };
    }

    private static TextBlock Text(Theme theme, string text, string colour, double size, FontWeight weight)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush(theme, colour),
            FontSize = size,
            FontWeight = weight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static IBrush Brush(Theme theme, string key)
    {
        return new SolidColorBrush(Color.Parse(theme.Colour(key)));
    }
}
