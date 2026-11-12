using System.Globalization;
using Anarres.Core.Dbc;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// A spell's look: its SpellVisual row and the SpellVisualKit rows that row points to, all editable
public sealed class SpellVisualView : StackPanel
{
    private const string VISUAL_FILE = "SpellVisual.dbc";
    private const string KIT_FILE = "SpellVisualKit.dbc";
    private const string EFFECT_FILE = "SpellVisualEffectName.dbc";
    private const string KIT_SUFFIX = "Kit";
    private const string EFFECT_SUFFIX = "Effect";

    private static readonly string[] VisualColumns = { "SpellVisualID_1", "SpellVisualID_2" };

    private readonly Session _session;
    private readonly Action _changed;
    private DbcTable? _spells;
    private int _spellRow = -1;
    private int _slot;
    private DbcTable? _visuals;
    private DbcTable? _kits;
    private DbcTable? _effects;

    public SpellVisualView(Session session, Action changed)
    {
        _session = session;
        _changed = changed;
        Spacing = 4;
    }

    public async Task LoadAsync(DbcTable spells)
    {
        _spells = spells;
        _visuals = await _session.TryLoadAsync(VISUAL_FILE);
        _kits = await _session.TryLoadAsync(KIT_FILE);
        _effects = await _session.TryLoadAsync(EFFECT_FILE);
    }

    public void Show(int spellRow)
    {
        _spellRow = spellRow;
        Children.Clear();
        if (_spells == null || spellRow < 0)
            return;
        if (_visuals == null)
        {
            Children.Add(Ui.Hint("Import SpellVisual.dbc and SpellVisualKit.dbc (and SpellVisualEffectName.dbc " +
                                 "for effect names) to edit how spells look."));
            return;
        }

        ComboBox slot = new ComboBox { ItemsSource = new[] { "Visual 1 (SpellVisualID_1)",
                                                            "Visual 2 (SpellVisualID_2)" },
                                       SelectedIndex = _slot, Width = 260 };
        slot.SelectionChanged += (_, _) =>
        {
            _slot = Math.Max(0, slot.SelectedIndex);
            Show(_spellRow);
        };
        int spellColumn = _spells.Definition.IndexOf(VisualColumns[_slot]);
        uint visualId = _spells.GetUInt(spellRow, spellColumn);
        int visualRow = visualId == 0 ? -1 : _visuals.FindRow(visualId);
        Button copy = Ui.Button(visualRow >= 0 ? "Make an own copy for this spell" : "New visual",
                                () => CopyVisual(spellColumn, visualRow));
        ToolTip.SetTip(copy, "Other spells may share this visual; a copy with a new ID changes only this spell");
        Children.Add(Ui.Horizontal(slot, copy));
        Children.Add(Ui.Hint(UsedBy(spellColumn, visualId)));

        if (visualRow < 0)
        {
            Children.Add(Ui.Hint(visualId == 0 ? "This spell has no visual in this slot."
                                               : "Visual " + visualId + " isn't in SpellVisual.dbc."));
            return;
        }

        RecordBinding visual = new RecordBinding();
        visual.Edited += column =>
        {
            _changed();
            if (column.EndsWith(KIT_SUFFIX, StringComparison.Ordinal))
                Show(_spellRow);
        };
        StackPanel fields = new StackPanel { Spacing = 2 };
        StackPanel kits = new StackPanel { Spacing = 6 };
        foreach (DbcColumn column in _visuals.Definition.Columns)
        {
            CellField field = new CellField(visual, column.Name, Ui.FIELD_WIDTH);
            if (!column.Name.EndsWith(KIT_SUFFIX, StringComparison.Ordinal))
            {
                fields.Children.Add(Ui.Documented(Ui.Row(column.Name, field.Box), _visuals.Definition.File,
                                                  column.Name));
                continue;
            }

            uint kitId = _visuals.GetUInt(visualRow, _visuals.Definition.IndexOf(column.Name));
            kits.Children.Add(KitPanel(column.Name, field, visualRow, kitId));
        }

        visual.Bind(_visuals, visualRow);
        Children.Add(Ui.Group("Kits (what plays at each stage of the cast)", kits));
        Children.Add(Ui.Group("SpellVisual " + visualId.ToString(CultureInfo.InvariantCulture), fields));
    }

    private Control KitPanel(string column, CellField field, int visualRow, uint kitId)
    {
        int kitRow = kitId == 0 || _kits == null ? -1 : _kits.FindRow(kitId);
        string state = kitId == 0 ? "none" : kitRow < 0 ? "not in SpellVisualKit.dbc" : "kit " + kitId;
        Button copy = Ui.Button("Own copy", () => CopyKit(column, visualRow, kitRow));
        copy.IsEnabled = kitRow >= 0;
        ToolTip.SetTip(copy, "Copy the kit to a new ID and use the copy here, so other visuals keep theirs");
        StackPanel header = Ui.Row(column, field.Box, copy, new TextBlock { Text = state, Classes = { "label" } });
        Ui.Documented(header, _visuals.Definition.File, column);
        if (kitRow < 0)
            return header;

        RecordBinding kit = new RecordBinding();
        kit.Edited += _ => _changed();
        StackPanel kitFields = new StackPanel { Spacing = 2 };
        foreach (DbcColumn kitColumn in _kits!.Definition.Columns.Skip(1))
        {
            Control editor = kitColumn.Name.Contains(EFFECT_SUFFIX, StringComparison.Ordinal) && _effects != null
                                 ? new LookupField(kit, kitColumn.Name, EffectName).View
                                 : new CellField(kit, kitColumn.Name, Ui.FIELD_WIDTH).Box;
            kitFields.Children.Add(Ui.Row(kitColumn.Name, editor));
        }

        kit.Bind(_kits, kitRow);
        Expander expander = new Expander
        {
            Header = header,
            Content = kitFields,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        return expander;
    }

    private string EffectName(uint id)
    {
        if (id == 0)
            return "None";
        int row = _effects?.FindRow(id) ?? -1;
        if (row < 0)
            return "Not in SpellVisualEffectName.dbc";
        int file = _effects!.Definition.IndexOf("FileName");
        int name = _effects.Definition.IndexOf("Name");
        return (name >= 0 ? _effects.GetString(row, name) + "  " : string.Empty) +
               (file >= 0 ? _effects.GetString(row, file) : string.Empty);
    }

    private string UsedBy(int spellColumn, uint visualId)
    {
        if (visualId == 0 || _spells == null)
            return string.Empty;
        int count = 0;
        for (int row = 0; row < _spells.Rows.Count; ++row)
        {
            if (_spells.Rows[row][spellColumn] == visualId)
                ++count;
        }

        return count <= 1 ? "Only this spell uses this visual."
                          : count + " spells use this visual; changes here show on all of them.";
    }

    private void CopyVisual(int spellColumn, int visualRow)
    {
        if (_visuals == null || _spells == null)
            return;
        uint id = _visuals.NextFreeKey();
        if (visualRow >= 0)
            _visuals.CopyRow(visualRow, id);
        else
            _visuals.AddRow(id);
        _spells.SetUInt(_spellRow, spellColumn, id);
        _changed();
        Show(_spellRow);
    }

    private void CopyKit(string column, int visualRow, int kitRow)
    {
        if (_kits == null || _visuals == null || kitRow < 0)
            return;
        uint id = _kits.NextFreeKey();
        _kits.CopyRow(kitRow, id);
        _visuals.SetUInt(visualRow, _visuals.Definition.IndexOf(column), id);
        _changed();
        Show(_spellRow);
    }

    // True when the spell's visual columns changed, so the tab is drawn again
    public static bool IsVisualColumn(string column)
    {
        return VisualColumns.Contains(column);
    }
}
