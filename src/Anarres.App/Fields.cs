using System.Globalization;
using Anarres.Core.Dbc;
using Anarres.Core.Spells;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// The row the field editors show; switching rows refreshes every field, editing one reports the change
public sealed class RecordBinding
{
    private readonly List<IField> _fields = new List<IField>();

    public DbcTable? Table { get; private set; }

    public int Row { get; private set; } = -1;

    public bool IsBound => Table != null && Row >= 0 && Row < Table.Rows.Count;

    // A field was edited by the user
    public event Action<string>? Edited;

    public int Count => _fields.Count;

    public void Add(IField field)
    {
        _fields.Add(field);
    }

    // Drops the fields added after the first count, when the editor that held them is rebuilt
    public void TrimTo(int count)
    {
        if (count < _fields.Count)
            _fields.RemoveRange(count, _fields.Count - count);
    }

    public void Bind(DbcTable? table, int row)
    {
        Table = table;
        Row = row;
        Refresh();
    }

    public void Refresh()
    {
        foreach (IField field in _fields)
            field.Refresh();
    }

    public int Column(string name)
    {
        return Table?.Definition.IndexOf(name) ?? -1;
    }

    public void ReportEdit(string column)
    {
        Edited?.Invoke(column);
    }
}

public interface IField
{
    void Refresh();
}

// A number or text cell in a box; it's written when the box loses focus or Enter is pressed
public sealed class CellField : IField
{
    private readonly RecordBinding _binding;
    private readonly string _column;
    private bool _refreshing;

    public CellField(RecordBinding binding, string column, double width = Ui.FIELD_WIDTH, bool multiline = false)
    {
        _binding = binding;
        _column = column;
        Box = new TextBox
        {
            Width = width,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap
        };
        if (multiline)
        {
            Box.Height = 90;
            Box.Width = double.NaN;
        }

        ToolTip.SetTip(Box, "Column: " + column);
        _binding.Add(this);
        Box.LostFocus += (_, _) =>
        {
            Commit();
            _binding.Table?.BreakUndoStep();
        };
        Box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !multiline)
                Commit();
        };
        // Text is written as it's typed, so previews such as the tooltip follow along
        Box.TextChanged += (_, _) =>
        {
            if (!_refreshing && IsText)
                Commit();
        };
    }

    public TextBox Box { get; }

    // Other controls, such as a lookup's description, listen for the value changing
    public event Action? ValueChanged;

    private bool IsText => _binding.Table is { } table && _binding.Column(_column) is var index and >= 0 &&
                           table.Definition.Columns[index].Type == DbcColumnType.String;

    public void Refresh()
    {
        _refreshing = true;
        int column = _binding.Column(_column);
        Box.IsEnabled = _binding.IsBound && column >= 0;
        Box.Text = Box.IsEnabled ? _binding.Table!.GetText(_binding.Row, column) : string.Empty;
        Box.Classes.Remove("invalid");
        _refreshing = false;
        ValueChanged?.Invoke();
    }

    private void Commit()
    {
        int column = _binding.Column(_column);
        if (!_binding.IsBound || column < 0)
            return;
        DbcTable table = _binding.Table!;
        string text = Box.Text ?? string.Empty;
        if (table.GetText(_binding.Row, column) == text)
        {
            Box.Classes.Remove("invalid");
            return;
        }

        if (table.TrySetText(_binding.Row, column, text, out string error))
        {
            Box.Classes.Remove("invalid");
            ToolTip.SetTip(Box, "Column: " + _column);
            _binding.ReportEdit(_column);
            ValueChanged?.Invoke();
        }
        else
        {
            Box.Classes.Add("invalid");
            ToolTip.SetTip(Box, error);
        }
    }

    public uint Raw()
    {
        int column = _binding.Column(_column);
        return _binding.IsBound && column >= 0 ? _binding.Table!.GetRaw(_binding.Row, column) : 0;
    }
}

// A number with a list of named values beside it, such as an effect type or an implicit target
public sealed class EnumField : IField
{
    private const double COMBO_WIDTH = 300;

    private readonly RecordBinding _binding;
    private readonly string _column;
    private readonly EnumTable _table;
    private readonly List<EnumValue> _items;
    private bool _refreshing;

    public EnumField(RecordBinding binding, string column, EnumTable table, bool withNone = true)
    {
        _binding = binding;
        _column = column;
        _table = table;
        _items = new List<EnumValue>();
        if (withNone && table.Values.All(value => value.Value != 0))
            _items.Add(new EnumValue(0, "NONE", "None", string.Empty));
        _items.AddRange(table.Values.OrderBy(value => value.Value));

        Combo = new ComboBox
        {
            Width = COMBO_WIDTH,
            ItemsSource = _items.Select(Text).Append(OTHER).ToList(),
            MaxDropDownHeight = 420
        };
        Value = new CellField(binding, column, Ui.FIELD_WIDTH);
        ToolTip.SetTip(Value.Box, "A value that isn't in the list");
        _binding.Add(this);
        Combo.SelectionChanged += (_, _) =>
        {
            if (_refreshing || Combo.SelectedIndex < 0 || !_binding.IsBound)
                return;
            // The last entry opens a box for a value that isn't in the list
            if (Combo.SelectedIndex >= _items.Count)
            {
                Value.Box.IsVisible = true;
                Value.Box.Focus();
                return;
            }

            int index = _binding.Column(_column);
            if (index < 0)
                return;
            long chosen = _items[Combo.SelectedIndex].Value;
            _binding.Table!.SetRaw(_binding.Row, index, unchecked((uint)chosen));
            _binding.ReportEdit(_column);
            Value.Refresh();
            Changed?.Invoke();
        };
        Value.ValueChanged += () =>
        {
            Select();
            Changed?.Invoke();
        };
        View = Ui.Horizontal(Combo, Value.Box);
    }

    public ComboBox Combo { get; }

    public CellField Value { get; }

    public StackPanel View { get; }

    public event Action? Changed;

    public long Current => unchecked((int)Value.Raw()) < 0 ? unchecked((int)Value.Raw()) : Value.Raw();

    public void Refresh()
    {
        Select();
    }

    private void Select()
    {
        _refreshing = true;
        Combo.IsEnabled = _binding.IsBound;
        long current = Current;
        int found = _items.FindIndex(item => item.Value == current);
        // The number box only shows for a value the list doesn't have
        Combo.SelectedIndex = found >= 0 || !_binding.IsBound ? found : _items.Count;
        Value.Box.IsVisible = found < 0 && _binding.IsBound;
        _refreshing = false;
    }

    private const string OTHER = "Other value...";

    // "13 - POTION": the value, then its name in capitals so it stands apart from the number
    private static string Text(EnumValue value)
    {
        return value.Value.ToString(CultureInfo.InvariantCulture) + " - " + value.Label.ToUpperInvariant();
    }
}

// A flags column as its number, a ... button that opens the bit list, and the names of the bits that are set
public sealed class FlagsField : IField
{
    private const double SUMMARY_WIDTH = 760;

    private readonly RecordBinding _binding;
    private readonly string _column;
    private readonly Func<EnumTable?> _table;
    private readonly CellField _value;
    private readonly TextBlock _summary = new TextBlock
    {
        Classes = { "hint" },
        MaxWidth = SUMMARY_WIDTH,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    public FlagsField(RecordBinding binding, string column, EnumTable table) : this(binding, column, () => table)
    {
    }

    // The list may change with the row, such as an item class's subclasses
    public FlagsField(RecordBinding binding, string column, Func<EnumTable?> table)
    {
        _binding = binding;
        _column = column;
        _table = table;
        _value = new CellField(binding, column, Ui.FIELD_WIDTH);
        _value.ValueChanged += Refresh;
        Button pick = new Button { Content = "...", Padding = new Thickness(10, 2) };
        ToolTip.SetTip(pick, "Choose the bits of " + column);
        pick.Click += async (_, _) => await PickAsync(pick);
        binding.Add(this);
        // A list that depends on another column, such as an item class's subclasses, follows its edits
        binding.Edited += edited =>
        {
            if (edited != _column)
                Refresh();
        };
        View = Ui.Vertical(4, Ui.Horizontal(_value.Box, pick), _summary);
    }

    public Control View { get; }

    public void Refresh()
    {
        if (!_binding.IsBound)
        {
            _summary.Text = string.Empty;
            return;
        }

        EnumTable? table = _table();
        if (table == null)
        {
            _summary.Text = string.Empty;
            ToolTip.SetTip(_summary, null);
            return;
        }

        _summary.Text = table.Describe(_value.Raw());
        ToolTip.SetTip(_summary, table.Lines(_value.Raw()));
    }

    private async Task PickAsync(Control anchor)
    {
        int column = _binding.Column(_column);
        if (!_binding.IsBound || column < 0 || TopLevel.GetTopLevel(anchor) is not Window owner ||
            _table() is not { } table)
            return;
        FlagsDialog dialog = new FlagsDialog(_column, table, _binding.Table!.GetRaw(_binding.Row, column));
        uint? chosen = await dialog.ShowDialog<uint?>(owner);
        if (chosen is not { } value || value == _binding.Table.GetRaw(_binding.Row, column))
            return;
        _binding.Table.SetRaw(_binding.Row, column, value);
        _binding.ReportEdit(_column);
        _value.Refresh();
    }
}

// An effect's misc value, labelled and explained by what the effect or its aura uses it for
public sealed class MiscField : IField
{
    private readonly RecordBinding _binding;
    private readonly string _effectColumn;
    private readonly string _auraColumn;
    private readonly bool _second;
    private readonly string _defaultLabel;
    private readonly TextBlock _label;
    private readonly TextBlock _meaning = new TextBlock
    {
        Classes = { "label" },
        MaxWidth = 420,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly Func<string, uint, string?>? _entries;
    private MiscMeaning _current = MiscMeaning.Plain;

    // entries names a creature or game object entry from the world database, when one is reachable
    public MiscField(RecordBinding binding, string column, string effectColumn, string auraColumn, bool second,
                     string label, Func<string, uint, string?>? entries = null)
    {
        _entries = entries;
        _binding = binding;
        _effectColumn = effectColumn;
        _auraColumn = auraColumn;
        _second = second;
        _defaultLabel = label;
        _label = Ui.Label(label);
        Value = new CellField(binding, column, Ui.FIELD_WIDTH);
        Value.ValueChanged += Describe;
        binding.Add(this);
        binding.Edited += edited =>
        {
            if (edited == _effectColumn || edited == _auraColumn)
                Refresh();
        };
        View = Ui.Horizontal(_label, Value.Box, _meaning);
        View.Margin = new Thickness(0, 2);
        Ui.Documented(View, SpellRecord.FILE, column, "spell_dbc");
    }

    public CellField Value { get; }

    public StackPanel View { get; }

    public void Refresh()
    {
        int effect = _binding.Column(_effectColumn);
        int aura = _binding.Column(_auraColumn);
        if (_binding.IsBound && effect >= 0 && aura >= 0)
        {
            (MiscMeaning a, MiscMeaning b) = SpellMiscValues.For(_binding.Table!.GetRaw(_binding.Row, effect),
                                                                 _binding.Table.GetRaw(_binding.Row, aura));
            _current = _second ? b : a;
        }
        else
            _current = MiscMeaning.Plain;

        _label.Text = _current.Label.Length > 0 ? _defaultLabel + ": " + _current.Label : _defaultLabel;
        Describe();
    }

    private void Describe()
    {
        uint raw = Value.Raw();
        string? table = _current.Label switch
        {
            "Creature entry" => WorldNames.CREATURES,
            "Game object entry" => WorldNames.GAME_OBJECTS,
            _ => null
        };
        if (table != null && _entries?.Invoke(table, raw) is { } name)
        {
            _meaning.Text = name;
            return;
        }

        _meaning.Text = _current.Values?.Describe(_current.Values.Flags ? raw : unchecked((int)raw)) ?? string.Empty;
    }
}

// An index into another table, with what it means shown beside it, such as "1.5 sec" for a cast time
public sealed class LookupField : IField
{
    private readonly Func<uint, string> _describe;
    private readonly TextBlock _meaning = new TextBlock { Classes = { "label" }, MaxWidth = 380,
                                                          TextTrimming = TextTrimming.CharacterEllipsis };

    public LookupField(RecordBinding binding, string column, Func<uint, string> describe)
    {
        _describe = describe;
        Value = new CellField(binding, column, Ui.FIELD_WIDTH);
        Value.ValueChanged += Refresh;
        binding.Add(this);
        View = Ui.Horizontal(Value.Box, _meaning);
    }

    public CellField Value { get; }

    public StackPanel View { get; }

    public void Refresh()
    {
        _meaning.Text = _describe(Value.Raw());
    }
}

// Every column of the row as a name and a box, for the fields no tab shows
public sealed class AllFieldsView
{
    private readonly StackPanel _rows = new StackPanel { Spacing = 2 };
    private readonly TextBox _filter = Ui.Clearable(new TextBox { Watermark = "Filter columns", Width = 300 });
    private readonly List<(string Name, Control Row)> _entries = new List<(string, Control)>();

    public AllFieldsView(RecordBinding binding, DbcDefinition definition)
    {
        foreach (DbcColumn column in definition.Columns)
        {
            CellField field = new CellField(binding, column.Name,
                                            column.Type == DbcColumnType.String ? 520 : Ui.FIELD_WIDTH);
            TextBlock type = new TextBlock { Text = column.Type.ToString(), Width = 60, Classes = { "label" },
                                             FontSize = 11 };
            StackPanel row = Ui.Horizontal(Ui.Label(column.Name, 240), type, field.Box);
            Ui.Documented(row, definition.File, column.Name, definition.Table);
            _entries.Add((column.Name, row));
            _rows.Children.Add(row);
        }

        _filter.TextChanged += (_, _) =>
        {
            string text = _filter.Text ?? string.Empty;
            foreach ((string name, Control row) in _entries)
                row.IsVisible = name.Contains(text, StringComparison.OrdinalIgnoreCase);
        };
        View = new DockPanel();
        DockPanel.SetDock(_filter, Dock.Top);
        _filter.Margin = new Thickness(0, 0, 0, 8);
        _filter.HorizontalAlignment = HorizontalAlignment.Left;
        View.Children.Add(_filter);
        View.Children.Add(new ScrollViewer { Content = _rows });
    }

    public DockPanel View { get; }
}

// Every bit of a flags column as a toggle on the page, like Stoneharry's attribute checkboxes
public sealed class FlagToggles : IField
{
    public const string BIT_CLASS = "flag-bit";
    private const double ITEM_WIDTH = 380;
    private const double VALUE_GAP = 14;
    private const int BITS = 32;

    private readonly RecordBinding _binding;
    private readonly string _column;
    private readonly ToggleSwitch[] _switches = new ToggleSwitch[BITS];
    private readonly TextBlock _hex = new TextBlock
    {
        Classes = { "hint" },
        VerticalAlignment = VerticalAlignment.Center
    };
    private bool _updating;

    public FlagToggles(RecordBinding binding, string column, EnumTable table)
    {
        _binding = binding;
        _column = column;
        Value = new CellField(binding, column);
        Value.ValueChanged += Refresh;
        WrapPanel bits = new WrapPanel();
        for (int bit = 0; bit < BITS; ++bit)
        {
            uint mask = 1u << bit;
            EnumValue? entry = table.Values.FirstOrDefault(value => value.Value == mask);
            ToggleSwitch toggle = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0 };
            int index = bit;
            toggle.IsCheckedChanged += (_, _) => Toggle(index, toggle.IsChecked == true);
            _switches[bit] = toggle;
            string label = entry?.Label ?? "Unknown bit " + bit;
            TextBlock text = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 0, 6, 0)
            };
            // The bit's value, so a total such as 587 can be worked out from the switches (1 + 2 + 8 + 64 + 512)
            TextBlock value = new TextBlock
            {
                Text = mask.ToString(CultureInfo.InvariantCulture),
                Classes = { "hint" },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, VALUE_GAP, 0)
            };
            DockPanel item = new DockPanel { Width = ITEM_WIDTH, Classes = { BIT_CLASS } };
            DockPanel.SetDock(toggle, Dock.Left);
            DockPanel.SetDock(value, Dock.Right);
            item.Children.Add(toggle);
            item.Children.Add(value);
            item.Children.Add(text);
            string details = entry == null ? string.Empty
                             : "\n\n" + entry.Name + (entry.Description.Length > 0 ? ": " + entry.Description : "");
            ToolTip.SetTip(item, label + details + "\n\nBit " + bit + " (" +
                                 mask.ToString(CultureInfo.InvariantCulture) + ", 0x" +
                                 mask.ToString("X", CultureInfo.InvariantCulture) + ") of " + column);
            bits.Children.Add(item);
        }

        Bits = bits;
        binding.Add(this);
    }

    public CellField Value { get; }

    // The value as hex, shown beside the value box
    public TextBlock Hex => _hex;

    public Control Bits { get; }

    public void Refresh()
    {
        _updating = true;
        uint raw = _binding.IsBound ? Value.Raw() : 0;
        for (int bit = 0; bit < BITS; ++bit)
        {
            _switches[bit].IsChecked = (raw & (1u << bit)) != 0;
            _switches[bit].IsEnabled = _binding.IsBound;
        }

        _hex.Text = _binding.IsBound ? "0x" + raw.ToString("X8", CultureInfo.InvariantCulture) : string.Empty;
        _updating = false;
    }

    public void Clear()
    {
        int column = _binding.Column(_column);
        if (!_binding.IsBound || column < 0 || _binding.Table!.GetRaw(_binding.Row, column) == 0)
            return;
        _binding.Table.SetRaw(_binding.Row, column, 0);
        _binding.ReportEdit(_column);
        Value.Refresh();
        Refresh();
    }

    private void Toggle(int bit, bool on)
    {
        int column = _binding.Column(_column);
        if (_updating || !_binding.IsBound || column < 0)
            return;
        uint raw = _binding.Table!.GetRaw(_binding.Row, column);
        uint changed = on ? raw | (1u << bit) : raw & ~(1u << bit);
        if (changed == raw)
            return;
        _binding.Table.SetRaw(_binding.Row, column, changed);
        _binding.ReportEdit(_column);
        Value.Refresh();
        Refresh();
    }
}
