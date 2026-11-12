using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Spells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// A spell's fields that differ from the clean Spell.dbc, side by side, each with a way back
public sealed class CompareWindow : Window
{
    private readonly DbcTable _clean;
    private readonly DbcTable _spells;
    private readonly int _row;
    private readonly Func<string, DbcTable?> _tables;
    private readonly StackPanel _rows = new StackPanel { Spacing = 2 };
    private readonly TextBlock _summary = new TextBlock { TextWrapping = TextWrapping.Wrap };

    public CompareWindow(DbcTable clean, DbcTable spells, int row, Func<string, DbcTable?> tables)
    {
        _clean = clean;
        _spells = spells;
        _row = row;
        _tables = tables;
        Title = "Compare with clean";
        Width = 980;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button revertAll = Ui.Button("Revert the whole spell", RevertAll);
        ToolTip.SetTip(revertAll, "Put every field back as the clean Spell.dbc has it (Undo takes it back)");
        Button close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        close.Click += (_, _) => Close(Reverted);
        StackPanel buttons = Ui.Horizontal(revertAll, close);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10);

        StackPanel top = new StackPanel { Margin = new Thickness(20, 16, 20, 8), Spacing = 6 };
        top.Children.Add(_summary);
        top.Children.Add(Line("Field", "Clean", "Now", null, header: true));

        DockPanel root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = _rows, Padding = new Thickness(20, 0) });
        Content = root;
        Fill();
    }

    // Whether anything was put back, so the editor shows it
    public bool Reverted { get; private set; }

    private void Fill()
    {
        List<CellDiff> cells = RowDiff.Row(_clean, _spells, _row);
        bool added = _clean.FindRow(_spells.Key(_row)) < 0;
        SpellRecord spell = new SpellRecord(_spells, _row);
        _summary.Text = added
                            ? "Spell " + spell.Id + " (" + spell.Name + ") isn't in the clean Spell.dbc: it's a new " +
                              "spell. Its filled fields are listed."
                            : "Spell " + spell.Id + " (" + spell.Name + "): " + cells.Count +
                              (cells.Count == 1 ? " field differs" : " fields differ") + " from the clean Spell.dbc.";
        _rows.Children.Clear();
        foreach (CellDiff cell in cells)
            _rows.Children.Add(Line(cell.Name, Named(_clean, cell, cell.Old), Named(_spells, cell, cell.New),
                                    added ? null : cell, header: false));
    }

    // A value with its names, such as "16 (Frost)"
    private string Named(DbcTable table, CellDiff cell, string text)
    {
        if (table == _clean && _clean.FindRow(_spells.Key(_row)) < 0)
            return text;
        int row = table == _spells ? _row : _clean.FindRow(_spells.Key(_row));
        int column = table.Definition.IndexOf(cell.Name);
        if (row < 0 || column < 0 || !DbcColumnTypes.IsInteger(table.Definition.Columns[column].Type))
            return text;
        string names = ColumnValues.For(table, column, row, _tables)?.Names(table.GetInt(row, column)) ?? string.Empty;
        return names.Length == 0 ? text : text + " (" + names + ")";
    }

    private Control Line(string field, string clean, string now, CellDiff? cell, bool header)
    {
        Grid line = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*,*,90"), Margin = new Thickness(0, 1) };
        FontWeight weight = header ? FontWeight.SemiBold : FontWeight.Normal;
        TextBlock name = new TextBlock
        {
            Text = field, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (!header && ColumnDocs.Describe(_spells.Definition.File, field) is { } note)
            ToolTip.SetTip(name, note);
        line.Children.Add(name);
        AddCell(line, 1, clean, weight);
        AddCell(line, 2, now, weight);
        if (cell != null)
        {
            Button revert = Ui.Button("Revert", () =>
            {
                RowDiff.Revert(_clean, _spells, _row, cell.Column);
                _spells.BreakUndoStep();
                Reverted = true;
                Fill();
            });
            revert.Padding = new Thickness(8, 1);
            Grid.SetColumn(revert, 3);
            line.Children.Add(revert);
        }

        return line;
    }

    private static void AddCell(Grid line, int column, string text, FontWeight weight)
    {
        SelectableTextBlock cell = new SelectableTextBlock
        {
            Text = text,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 0)
        };
        Grid.SetColumn(cell, column);
        line.Children.Add(cell);
    }

    private void RevertAll()
    {
        foreach (CellDiff cell in RowDiff.Row(_clean, _spells, _row))
            RowDiff.Revert(_clean, _spells, _row, cell.Column);
        _spells.BreakUndoStep();
        Reverted = true;
        Fill();
    }
}
