using Anarres.Core.Dbc;
using Anarres.Core.Spells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// Changes one field on many spells at once, previewed first and applied as one undo step
public sealed class FindReplaceWindow : Window
{
    private const int PREVIEW_ROWS = 200;

    private static readonly (string Label, BulkOperation Operation)[] Operations =
    {
        ("Set to", BulkOperation.Set), ("Add", BulkOperation.Add), ("Multiply by", BulkOperation.Multiply),
        ("Turn bits on", BulkOperation.TurnBitsOn), ("Turn bits off", BulkOperation.TurnBitsOff),
        ("Replace text", BulkOperation.ReplaceText)
    };

    private readonly DbcTable _spells;
    private readonly TextBox _query = Ui.Clearable(new TextBox
    {
        Watermark = "Which spells, as in the spell list: a name, an ID, effect:6, family:3...",
        Width = 520
    });
    private readonly AutoCompleteBox _column = new AutoCompleteBox
    {
        Width = 320,
        FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
        Watermark = "Column, such as StartRecoveryTime"
    };
    private readonly ComboBox _operation = new ComboBox { Width = 180 };
    private readonly TextBox _value = new TextBox { Width = 200, Watermark = "Value (0x for hex)" };
    private readonly TextBox _replacement = new TextBox { Width = 200, Watermark = "Replace with" };
    private readonly TextBlock _status = new TextBlock { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _preview = new ListBox();
    private List<BulkChange> _changes = new List<BulkChange>();
    private int _columnIndex = -1;

    public FindReplaceWindow(DbcTable spells, string query)
    {
        _spells = spells;
        Title = "Find and replace across spells";
        Width = 860;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _query.Text = query;
        _column.ItemsSource = spells.Definition.Columns.Select(column => column.Name).ToList();
        _operation.ItemsSource = Operations.Select(operation => operation.Label).ToList();
        _operation.SelectedIndex = 0;
        _operation.SelectionChanged += (_, _) => _replacement.IsVisible = Chosen() == BulkOperation.ReplaceText;
        _replacement.IsVisible = false;

        Button preview = Ui.Button("Preview", Preview);
        Button apply = new Button { Content = "Apply", Classes = { "accent" }, IsDefault = true };
        apply.Click += (_, _) => ApplyChanges();
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(0);

        StackPanel top = new StackPanel { Margin = new Thickness(20, 16, 20, 8), Spacing = 6 };
        top.Children.Add(Ui.Row("Spells", _query));
        top.Children.Add(Ui.Row("Column", _column));
        top.Children.Add(Ui.Row("Change", _operation, _value, _replacement));
        top.Children.Add(Ui.Horizontal(preview, _status));
        StackPanel buttons = Ui.Horizontal(apply, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10);

        DockPanel root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(new Border { Margin = new Thickness(20, 0), Child = _preview });
        Content = root;
    }

    private BulkOperation Chosen()
    {
        return Operations[Math.Max(0, _operation.SelectedIndex)].Operation;
    }

    private void Preview()
    {
        _columnIndex = _spells.Definition.IndexOf((_column.Text ?? string.Empty).Trim());
        if (_columnIndex < 0)
        {
            _status.Text = "Choose a column from the list.";
            return;
        }

        Func<int, bool> matches = SpellSearch.Build(_spells, (_query.Text ?? string.Empty).Trim());
        List<int> rows = Enumerable.Range(0, _spells.Rows.Count).Where(matches).ToList();
        _changes = BulkEdit.Preview(_spells, rows, _columnIndex, Chosen(), _value.Text ?? string.Empty,
                                    _replacement.Text ?? string.Empty);
        int name = _spells.Definition.IndexOf("Name_Lang_enUS");
        _preview.ItemsSource = _changes.Take(PREVIEW_ROWS)
                                       .Select(change => change.Key + "  " +
                                                         (name >= 0 ? _spells.GetString(change.Row, name) : "") +
                                                         ":   " + change.Old + "  →  " + change.New)
                                       .ToList();
        _status.Text = rows.Count + (rows.Count == 1 ? " spell matches" : " spells match") + "; " + _changes.Count +
                       (_changes.Count == 1 ? " would change." : " would change.") +
                       (_changes.Count > PREVIEW_ROWS ? " The first " + PREVIEW_ROWS + " are listed." : string.Empty);
    }

    private void ApplyChanges()
    {
        Preview();
        if (_changes.Count == 0 || _columnIndex < 0)
            return;
        Close(BulkEdit.Apply(_spells, _columnIndex, _changes));
    }
}
