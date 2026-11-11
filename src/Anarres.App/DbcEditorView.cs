using System.Globalization;
using System.Text;
using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Anarres.App;

// The DBC editor: every table in the project, or a .dbc file opened straight from disk, as an editable grid
public sealed class DbcEditorView : Grid
{
    private const double LIST_WIDTH = 270;
    private const int FILTER_DELAY_MS = 300;

    private readonly MainWindow _window;
    private readonly Session _session;
    private readonly ListBox _tables = new ListBox();
    private readonly DbcGrid _grid = new DbcGrid();
    private readonly TextBox _filter = new TextBox
    {
        Watermark = "Filter: an ID, text, or Column=value",
        Width = 300
    };
    private readonly TextBlock _info = new TextBlock
    {
        Classes = { "label" },
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _cell = new TextBlock { Classes = { "hint" }, Margin = new Thickness(12, 0, 12, 8) };
    private readonly DispatcherTimer _filterTimer;
    private DbcTable? _table;
    // Set when the table came from a file on disk instead of the project
    private string? _filePath;

    public DbcEditorView(MainWindow window, Session session)
    {
        _window = window;
        _session = session;
        ColumnDefinitions = new ColumnDefinitions("Auto,8,*");
        _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FILTER_DELAY_MS) };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ApplyFilter();
        };
        _filter.TextChanged += (_, _) =>
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        };
        _tables.SelectionChanged += async (_, _) =>
        {
            if (_tables.SelectedItem is StoredTable stored)
                await OpenStoredAsync(stored.File);
        };
        _tables.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<StoredTable>((table, _) =>
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = table?.File ?? string.Empty, Width = 170,
                                    TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = table?.Rows.ToString("N0", CultureInfo.InvariantCulture) ?? string.Empty,
                                    Foreground = ThemeBrushes.MutedText, FontSize = 12,
                                    VerticalAlignment = VerticalAlignment.Center }
                }
            });
        _grid.SelectionChanged += ShowCell;
        _grid.CellEdited += (_, _) =>
        {
            _session.NotifyChanged();
            ShowInfo();
        };
        _grid.EditRejected += message => _window.SetStatus(message);

        Children.Add(BuildList());
        Border right = Ui.Card(BuildEditor());
        SetColumn(right, 2);
        Children.Add(right);
    }

    public DbcTable? Current => _table;

    public async Task RefreshListAsync()
    {
        if (!_session.IsOpen && !await _window.EnsureStoreAsync())
            return;
        try
        {
            IDbcStore store = await _session.StoreAsync();
            List<StoredTable> tables = await store.ListTablesAsync();
            _tables.ItemsSource = tables;
            if (tables.Count == 0)
                _info.Text = "The project has no tables yet. Use Import to bring .dbc files in.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              System.Data.Common.DbException)
        {
            _window.SetStatus("The tables couldn't be listed: " + exception.Message);
        }
    }

    private Control BuildList()
    {
        Button open = Ui.AsyncButton("Open .dbc file...", OpenFileAsync);
        Button refresh = Ui.AsyncButton("Refresh", RefreshListAsync);
        ToolTip.SetTip(open, "Edit a .dbc file straight from disk, without importing it, like WDBX Editor");
        StackPanel buttons = Ui.Horizontal(open, refresh);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        TextBlock title = Ui.Title("Tables in the project");
        DockPanel panel = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(title);
        panel.Children.Add(buttons);
        panel.Children.Add(_tables);
        return new Border { Classes = { "card" }, Width = LIST_WIDTH, Child = panel };
    }

    private Control BuildEditor()
    {
        Button save = Ui.AsyncButton("Save", SaveAsync, accent: true);
        Button add = Ui.Button("Add row", AddRow);
        Button copy = Ui.Button("Copy row", CopyRow);
        Button remove = Ui.AsyncButton("Delete row", DeleteRowAsync);
        Button export = Ui.AsyncButton("Save as .dbc...", SaveAsDbcAsync);
        Button csv = Ui.AsyncButton("CSV...", ExportCsvAsync);
        Button sql = Ui.AsyncButton("SQL...", ExportSqlAsync);
        Button columns = Ui.AsyncButton("Columns...", ShowColumnsAsync);
        ToolTip.SetTip(save, "Save changes to the project, or to the file when one was opened from disk (Ctrl+S)");
        ToolTip.SetTip(sql, "SQL for the filtered rows, for AzerothCore's matching _dbc table");
        ToolTip.SetTip(columns, "The column layout, and renaming the columns of a guessed layout");

        WrapPanel toolbar = new WrapPanel { Margin = new Thickness(12, 10, 12, 6) };
        foreach (Control control in new Control[] { save, add, copy, remove, _filter, export, csv, sql, columns })
        {
            control.Margin = new Thickness(0, 0, 8, 4);
            toolbar.Children.Add(control);
        }

        _info.Margin = new Thickness(12, 0, 12, 6);
        DockPanel panel = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_info, Dock.Top);
        DockPanel.SetDock(_cell, Dock.Bottom);
        panel.Children.Add(toolbar);
        panel.Children.Add(_info);
        panel.Children.Add(_cell);
        panel.Children.Add(new Border { Margin = new Thickness(12, 0, 12, 4), Child = _grid, ClipToBounds = true });
        return panel;
    }

    private async Task OpenStoredAsync(string file)
    {
        try
        {
            _window.SetStatus("Loading " + file + "...");
            _table = await _session.LoadAsync(file);
            _filePath = null;
            _filter.Text = string.Empty;
            _grid.Show(_table);
            ShowInfo();
            _window.SetStatus(string.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              System.Data.Common.DbException)
        {
            _window.SetStatus(file + " couldn't be loaded: " + exception.Message);
        }
    }

    private async Task OpenFileAsync()
    {
        List<string> files = await Ui.PickFilesAsync(_window, "Open a .dbc file", "DBC files", new[] { "*.dbc" },
                                                     many: false);
        if (files.Count == 0)
            return;
        try
        {
            string path = files[0];
            _table = await Task.Run(() => DbcFile.Read(path, _session.Definitions));
            _filePath = path;
            _tables.SelectedItem = null;
            _filter.Text = string.Empty;
            _grid.Show(_table);
            ShowInfo();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or
                                              UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(_window, "Not opened", "The file couldn't be read: " + exception.Message);
        }
    }

    private void ShowInfo()
    {
        if (_table == null)
            return;
        DbcDefinition definition = _table.Definition;
        string source = _filePath ?? "project";
        string layout = definition.IsInferred ? "layout guessed from the data" : "layout from AzerothCore";
        string changes = _table.IsModified ? ", unsaved changes" : string.Empty;
        _info.Text = definition.File + "  -  " + _table.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) +
                     " rows, " + definition.Columns.Count + " columns, " + layout + changes + "  -  " + source;
        _info.Foreground = _table.IsModified ? ThemeBrushes.Warning : ThemeBrushes.MutedText;
    }

    private void ShowCell()
    {
        if (_table == null || _grid.SelectedRow < 0)
        {
            _cell.Text = string.Empty;
            return;
        }

        DbcColumn column = _table.Definition.Columns[_grid.SelectedColumn];
        uint raw = _table.GetRaw(_grid.SelectedRow, _grid.SelectedColumn);
        string hex = "  -  0x" + raw.ToString("X8", CultureInfo.InvariantCulture);
        string bits = column.Type == DbcColumnType.String ? string.Empty : hex;
        _cell.Text = column.Name + " (" + column.Type + "), row key " + _table.Key(_grid.SelectedRow) + bits +
                     "  -  Enter or double-click to edit, Ctrl+C to copy";
    }

    private void ApplyFilter()
    {
        if (_table == null)
            return;
        DbcTable table = _table;
        string text = (_filter.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            _grid.Show(table);
            return;
        }

        Func<int, bool> test = Matcher(table, text);
        List<int> rows = new List<int>();
        for (int row = 0; row < table.Rows.Count; ++row)
        {
            if (test(row))
                rows.Add(row);
        }

        _grid.Show(table, rows);
        _window.SetStatus(rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " rows match.");
    }

    // Column=value, Column~part of the text, an ID, or text found in any string column
    private static Func<int, bool> Matcher(DbcTable table, string text)
    {
        int separator = text.IndexOfAny(new[] { '=', '~' });
        if (separator > 0)
        {
            int column = table.Definition.IndexOf(text[..separator].Trim());
            string value = text[(separator + 1)..].Trim();
            bool contains = text[separator] == '~';
            if (column >= 0)
                return row => contains ? table.GetText(row, column).Contains(value, StringComparison.OrdinalIgnoreCase)
                                       : string.Equals(table.GetText(row, column), value,
                                                       StringComparison.OrdinalIgnoreCase);
        }

        bool isNumber = uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint key);
        List<int> strings = Enumerable.Range(0, table.ColumnCount)
                                      .Where(column => table.Definition.Columns[column].Type == DbcColumnType.String)
                                      .ToList();
        return row => (isNumber && table.Key(row) == key) ||
                      strings.Any(column => table.GetString(row, column)
                                                 .Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveAsync()
    {
        if (_table == null)
            return;
        try
        {
            if (_filePath != null)
            {
                await Task.Run(() => DbcFile.Write(_table, _filePath));
                _table.AcceptChanges();
                _window.SetStatus("Saved " + _filePath + ".");
            }
            else
            {
                await _session.SaveAsync(_table);
                _window.SetStatus("Saved " + _table.Definition.File + " to the project.");
            }

            _grid.Refresh();
            ShowInfo();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              UnauthorizedAccessException or System.Data.Common.DbException)
        {
            await Dialogs.MessageAsync(_window, "Not saved", "The table couldn't be saved: " + exception.Message);
        }
    }

    private void AddRow()
    {
        if (_table == null)
            return;
        int row = _table.AddRow(_table.NextFreeKey());
        AfterRowChange(row);
    }

    private void CopyRow()
    {
        if (_table == null || _grid.SelectedRow < 0)
            return;
        int row = _table.CopyRow(_grid.SelectedRow, _table.NextFreeKey());
        AfterRowChange(row);
    }

    private async Task DeleteRowAsync()
    {
        if (_table == null || _grid.SelectedRow < 0)
            return;
        uint key = _table.Key(_grid.SelectedRow);
        if (!await Dialogs.ConfirmAsync(_window, "Delete row", "Delete the row with key " + key + "?", "Delete"))
            return;
        _table.RemoveRow(_grid.SelectedRow);
        ApplyFilter();
        _grid.Show(_table);
        _session.NotifyChanged();
        ShowInfo();
    }

    private void AfterRowChange(int row)
    {
        _filter.Text = string.Empty;
        _grid.Show(_table);
        _grid.SelectRow(row);
        _grid.FocusGrid();
        _session.NotifyChanged();
        ShowInfo();
    }

    private async Task SaveAsDbcAsync()
    {
        if (_table == null)
            return;
        string? path = await Ui.SaveFileAsync(_window, "Save as .dbc", _table.Definition.File, "DBC file", "dbc");
        if (path == null)
            return;
        await Task.Run(() => DbcFile.Write(_table, path));
        _window.SetStatus("Wrote " + path + ".");
    }

    private async Task ExportCsvAsync()
    {
        if (_table == null)
            return;
        string? path = await Ui.SaveFileAsync(_window, "Save as CSV", _table.Definition.Name + ".csv", "CSV file",
                                              "csv");
        if (path == null)
            return;
        DbcTable table = _table;
        await Task.Run(() =>
        {
            using StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine(string.Join(",", table.Definition.Columns.Select(column => Csv(column.Name))));
            for (int row = 0; row < table.Rows.Count; ++row)
                writer.WriteLine(string.Join(",", Enumerable.Range(0, table.ColumnCount)
                                                            .Select(column => Csv(table.GetText(row, column)))));
        });
        _window.SetStatus("Wrote " + path + ".");
    }

    private async Task ExportSqlAsync()
    {
        if (_table == null)
            return;
        string target = _table.Definition.Table.Length > 0 ? _table.Definition.Table : _table.Definition.Name;
        string? path = await Ui.SaveFileAsync(_window, "Save SQL", target + ".sql", "SQL file", "sql");
        if (path == null)
            return;
        string filterText = (_filter.Text ?? string.Empty).Trim();
        List<int> tableRows = filterText.Length == 0
                                  ? Enumerable.Range(0, _table.Rows.Count).ToList()
                                  : Enumerable.Range(0, _table.Rows.Count).Where(Matcher(_table, filterText)).ToList();
        DbcTable table = _table;
        await Task.Run(() => SqlExport.Write(path, table, tableRows, target));
        _window.SetStatus("Wrote " + tableRows.Count + " rows to " + path + ".");
    }

    private async Task ShowColumnsAsync()
    {
        if (_table == null)
            return;
        ColumnsWindow dialog = new ColumnsWindow(_table.Definition, _session.Settings.DefinitionsFolder);
        if (await dialog.ShowDialog<bool>(_window))
        {
            _session.Use(_session.Settings);
            _window.SetStatus("Saved the layout. Import " + _table.Definition.File + " again to use the new names " +
                              "in the project.");
        }
    }

    private static string Csv(string value)
    {
        bool quote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
