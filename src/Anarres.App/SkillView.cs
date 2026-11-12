using System.Globalization;
using Anarres.Core.Dbc;
using Anarres.Core.Spells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// The Skill tab: the spell's SkillLineAbility rows (its skill line) and trainer_spell SQL to teach it
public sealed class SkillView : IField
{
    public const string FILE = "SkillLineAbility.dbc";

    private readonly RecordBinding _binding;
    private readonly Session _session;
    private readonly MainWindow _window;
    private readonly DbcGrid _grid = new DbcGrid { Height = 180 };
    private readonly ValueNames _names;
    private readonly TextBlock _status = new TextBlock { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _trainer = new TextBox { Width = 120, Watermark = "Trainer ID" };
    private readonly MoneyBox _cost = new MoneyBox();
    private readonly TextBox _level = new TextBox { Width = 80, Text = "1" };
    private readonly SqlBox _sql = new SqlBox();
    private DbcTable? _skills;
    private bool _loading;

    public SkillView(RecordBinding binding, Session session, MainWindow window)
    {
        _binding = binding;
        _session = session;
        _window = window;
        _names = new ValueNames(session);
        _grid.Names = _names.For;
        binding.Add(this);
        _grid.CellEdited += (_, _) => _session.NotifyChanged();
        foreach (TextBox box in new[] { _trainer, _level })
            box.TextChanged += (_, _) => WriteSql();
        _cost.Changed += WriteSql;

        Button add = Ui.Button("Add to a skill line", AddRow);
        Button remove = Ui.Button("Remove the selected row", RemoveRow);
        Button copy = Ui.AsyncButton("Copy SQL", CopyAsync);
        View = Ui.Vertical(0,
            Ui.Group("Skill lines (SkillLineAbility.dbc)",
                     Ui.Hint("A row puts the spell in a skill line: a class's spells, a profession or a weapon " +
                             "skill. Set SkillLine (hover it for the name), the races and classes that get it, and " +
                             "AcquireMethod (1 learned with the skill, 2 learned on getting the skill)."),
                     _grid,
                     Ui.Horizontal(add, remove),
                     _status),
            Ui.Group("Trainer (acore_world.trainer_spell)",
                     Ui.Row("Trainer ID", _trainer),
                     Ui.Row("Cost", _cost.View),
                     Ui.Row("Required level", _level),
                     _sql.View,
                     Ui.Horizontal(copy),
                     Ui.Hint("The trainer is a trainer.Id; creatures teach it through creature_default_trainer. " +
                             "The skill line and rank come from the first row above.")));
    }

    public Control View { get; }

    public void Refresh()
    {
        if (_skills == null)
        {
            if (!_loading)
                _ = LoadAsync();
            return;
        }

        uint id = _binding.IsBound ? new SpellRecord(_binding.Table!, _binding.Row).Id : 0;
        int spell = _skills.Definition.IndexOf("Spell");
        List<int> rows = Enumerable.Range(0, _skills.Rows.Count)
                                   .Where(row => spell >= 0 && _skills.GetRaw(row, spell) == id).ToList();
        _grid.Show(_skills, rows);
        _status.Text = rows.Count == 0 ? "The spell isn't in any skill line." : string.Empty;
        WriteSql();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _skills = await _session.TryLoadAsync(FILE);
            await _session.LoadNamingTablesAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                              InvalidOperationException or System.Data.Common.DbException)
        {
            _status.Text = FILE + " couldn't be loaded: " + exception.Message;
        }

        _loading = false;
        if (_skills == null)
        {
            _status.Text = "The project has no " + FILE + "; import it to put spells in skill lines.";
            return;
        }

        Refresh();
    }

    private void AddRow()
    {
        if (_skills == null || !_binding.IsBound)
            return;
        uint id = new SpellRecord(_binding.Table!, _binding.Row).Id;
        int row = _skills.AddRow(_skills.NextFreeKey());
        int spell = _skills.Definition.IndexOf("Spell");
        if (spell >= 0)
            _skills.SetRaw(row, spell, id);
        _session.NotifyChanged();
        Refresh();
        _window.SetStatus("Added a SkillLineAbility row for spell " + id + ". Set its SkillLine, then save.");
    }

    private void RemoveRow()
    {
        if (_skills == null || _grid.SelectedRow < 0)
            return;
        _skills.RemoveRow(_grid.SelectedRow);
        _session.NotifyChanged();
        Refresh();
    }

    private void WriteSql()
    {
        if (!_binding.IsBound)
        {
            _sql.Sql = string.Empty;
            return;
        }

        // Without SkillLineAbility.dbc the skill line and rank are left at 0
        uint id = new SpellRecord(_binding.Table!, _binding.Row).Id;
        int spell = _skills?.Definition.IndexOf("Spell") ?? -1;
        int first = _skills == null
                        ? -1
                        : Enumerable.Range(0, _skills.Rows.Count)
                                    .FirstOrDefault(row => spell >= 0 && _skills.GetRaw(row, spell) == id, -1);
        uint skill = first >= 0 ? Raw("SkillLine", first) : 0;
        uint rank = first >= 0 ? Raw("MinSkillLineRank", first) : 0;
        string trainer = Number(_trainer.Text, "0");
        _sql.Sql = "DELETE FROM `trainer_spell` WHERE `TrainerId` = " + trainer + " AND `SpellId` = " + id + ";\n" +
                    "INSERT INTO `trainer_spell` (`TrainerId`, `SpellId`, `MoneyCost`, `ReqSkillLine`, " +
                    "`ReqSkillRank`, `ReqAbility1`, `ReqAbility2`, `ReqAbility3`, `ReqLevel`, `VerifiedBuild`) " +
                    "VALUES (" + trainer + ", " + id + ", " + _cost.Copper + ", " + skill + ", " + rank +
                    ", 0, 0, 0, " + Number(_level.Text, "1") + ", 0);";
    }

    private uint Raw(string column, int row)
    {
        int index = _skills!.Definition.IndexOf(column);
        return index < 0 ? 0 : _skills.GetRaw(row, index);
    }

    private static string Number(string? text, string fallback)
    {
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value)
                   ? value.ToString(CultureInfo.InvariantCulture)
                   : fallback;
    }

    private async Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(_window)?.Clipboard is { } clipboard && _sql.Sql is { Length: > 0 } text)
        {
            await clipboard.SetTextAsync(text);
            _window.SetStatus("Copied the trainer_spell SQL.");
        }
    }
}
