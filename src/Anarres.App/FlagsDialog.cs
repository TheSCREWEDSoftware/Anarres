using System.Globalization;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// Picks the bits of a flags value from a list: a switch, the bit, its value and what it does, with the total on top
public sealed class FlagsDialog : Window
{
    private const double WINDOW_WIDTH = 860;
    private const double WINDOW_HEIGHT = 720;
    private const int BITS = 32;
    private const double BIT_WIDTH = 50;
    private const double VALUE_WIDTH = 110;

    private readonly TextBlock _total = new TextBlock { FontWeight = FontWeight.SemiBold };
    private readonly List<(ToggleSwitch Switch, TextBlock Value, uint Mask)> _rows =
        new List<(ToggleSwitch, TextBlock, uint)>();
    private readonly TextBox _filter = Ui.Clearable(new TextBox { Watermark = "Filter", Width = 240 });
    private readonly CheckBox _onlySet = new CheckBox { Content = "Only set bits" };
    private readonly List<(Control Row, string Text, uint Mask)> _filterable = new List<(Control, string, uint)>();
    private uint _value;

    public FlagsDialog(string column, EnumTable table, uint value)
    {
        _value = value;
        Title = "Flags selection: " + column;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Dictionary<uint, EnumValue> named = table.Values.Where(entry => entry.Value is > 0 and <= uint.MaxValue)
                                                 .GroupBy(entry => (uint)entry.Value)
                                                 .ToDictionary(group => group.Key, group => group.First());
        StackPanel list = new StackPanel();
        list.Children.Add(Header());
        for (int bit = 0; bit < BITS; ++bit)
        {
            uint mask = 1u << bit;
            named.TryGetValue(mask, out EnumValue? entry);
            list.Children.Add(Row(bit, mask, entry));
        }

        _filter.TextChanged += (_, _) => ApplyFilter();
        _onlySet.IsCheckedChanged += (_, _) => ApplyFilter();
        Button clear = Ui.Button("Clear all", () => _rows.ForEach(row => row.Switch.IsChecked = false));
        Button select = new Button { Content = "Select", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        select.Click += (_, _) => Close((uint?)_value);
        cancel.Click += (_, _) => Close(null);

        TextBlock title = new TextBlock { Text = column, FontSize = 18, FontWeight = FontWeight.SemiBold };
        StackPanel top = new StackPanel { Margin = new Thickness(20, 16, 20, 8), Spacing = 8 };
        top.Children.Add(title);
        top.Children.Add(Ui.Horizontal(_total, new TextBlock { Text = "  " }, _filter, _onlySet, clear));
        StackPanel buttons = Ui.Horizontal(cancel, select);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10, 20, 16);

        DockPanel root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(new Border
        {
            Classes = { "group" },
            Margin = new Thickness(20, 0),
            Padding = new Thickness(0),
            Child = new ScrollViewer { Content = list }
        });
        Content = root;
        UpdateTotal();
    }

    private static Control Header()
    {
        Grid header = Columns();
        Ui.Themed(header, Panel.BackgroundProperty, ThemeColours.GRID_HEADER);
        AddCell(header, 1, new TextBlock { Text = "Bit", FontWeight = FontWeight.SemiBold });
        AddCell(header, 2, new TextBlock { Text = "Value (2^Bit)", FontWeight = FontWeight.SemiBold });
        AddCell(header, 3, new TextBlock { Text = "Description", FontWeight = FontWeight.SemiBold });
        return header;
    }

    private Control Row(int bit, uint mask, EnumValue? entry)
    {
        ToggleSwitch toggle = new ToggleSwitch
        {
            IsChecked = (_value & mask) != 0,
            OnContent = null,
            OffContent = null,
            MinWidth = 0
        };
        TextBlock value = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 16, 0)
        };
        string label = entry?.Label ?? "Unknown bit " + bit;
        TextBlock description = new TextBlock { TextWrapping = TextWrapping.Wrap };
        description.Inlines!.Add(new Avalonia.Controls.Documents.Run(label) { FontWeight = FontWeight.SemiBold });
        string details = entry == null ? string.Empty
                         : entry.Name + (entry.Description.Length > 0 ? " - " + entry.Description : string.Empty);
        if (details.Length > 0)
        {
            description.Inlines.Add(new Avalonia.Controls.Documents.LineBreak());
            description.Inlines.Add(new Avalonia.Controls.Documents.Run(details)
            {
                Foreground = ThemeBrushes.MutedText,
                FontSize = 12
            });
        }

        Grid row = Columns();
        if (bit % 2 == 1)
            Ui.Themed(row, Panel.BackgroundProperty, ThemeColours.GRID_ALTERNATE);
        AddCell(row, 0, toggle);
        AddCell(row, 1, new TextBlock { Text = bit.ToString(CultureInfo.InvariantCulture) });
        AddCell(row, 2, value);
        AddCell(row, 3, description);
        toggle.IsCheckedChanged += (_, _) =>
        {
            _value = toggle.IsChecked == true ? _value | mask : _value & ~mask;
            UpdateTotal();
        };
        _rows.Add((toggle, value, mask));
        _filterable.Add((row, (label + " " + details + " " + bit).ToLowerInvariant(), mask));
        return row;
    }

    private static Grid Columns()
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("70," + BIT_WIDTH.ToString(CultureInfo.InvariantCulture) + "," +
                                                      VALUE_WIDTH.ToString(CultureInfo.InvariantCulture) + ",*"),
            MinHeight = 40
        };
    }

    private static void AddCell(Grid row, int column, Control content)
    {
        content.VerticalAlignment = VerticalAlignment.Center;
        content.Margin = new Thickness(column == 0 ? 12 : 4, 4, content.Margin.Right, 4);
        Grid.SetColumn(content, column);
        row.Children.Add(content);
    }

    private void UpdateTotal()
    {
        _total.Text = "Total value: " + _value.ToString(CultureInfo.InvariantCulture) + "  (0x" +
                      _value.ToString("X8", CultureInfo.InvariantCulture) + ")";
        foreach ((ToggleSwitch _, TextBlock value, uint mask) in _rows)
            value.Text = (_value & mask) != 0 ? "+" + mask.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void ApplyFilter()
    {
        string text = (_filter.Text ?? string.Empty).Trim().ToLowerInvariant();
        bool onlySet = _onlySet.IsChecked == true;
        foreach ((Control row, string searchable, uint mask) in _filterable)
            row.IsVisible = searchable.Contains(text, StringComparison.Ordinal) && (!onlySet || (_value & mask) != 0);
    }
}
