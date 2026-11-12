using System.Globalization;
using System.Text;
using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Settings;
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
    private readonly ValueNames _names;
    private readonly TextBox _filter = Ui.Clearable(new TextBox
    {
        Watermark = "Filter: an ID, text, or Column=value",
        Width = 300
    });
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
    private bool _refreshing;

    public DbcEditorView(MainWindow window, Session session)
    {
        _window = window;
        _session = session;
        _names = new ValueNames(session);
        _grid.Names = _names.For;
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
            if (!_refreshing && _tables.SelectedItem is StoredTable stored)
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
                    Ui.Themed(new TextBlock
                    {
                        Text = table?.Rows.ToString("N0", CultureInfo.InvariantCulture) ?? string.Empty,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    }, TextBlock.ForegroundProperty, ThemeColours.MUTED_TEXT)
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

    private bool ConfirmDeletes { get; set; } = true;

    public void UseOptions(AppOptions options)
    {
        ConfirmDeletes = options.ConfirmDeletes;
        _grid.FlagsInHex = options.FlagsInHex;
        _grid.NamesInCells = options.NamesInCells;
        _grid.HideEmptyByDefault = options.HideEmptyColumns;
        _grid.UseFontSize(options.GridFontSize);
    }

    internal DbcGrid Grid => _grid;

    public void FocusFilter()
    {
        _filter.Focus();
        _filter.SelectAll();
    }

    internal Task OpenForTestAsync(string file)
    {
        return OpenStoredAsync(file);
    }

    // A file opened from disk with changes not written back; the project's tables are tracked by the session
    public string? UnsavedFile => _filePath != null && _table is { IsModified: true } ? _filePath : null;

    public async Task SaveFileAsync()
    {
        if (UnsavedFile != null)
            await SaveAsync();
    }

    // After an import the project's copy is stale; a file from disk stays open
    public void ForgetStoredTable()
    {
        if (_filePath != null)
            return;
        _table = null;
        _grid.Show(null);
        _info.Text = string.Empty;
    }

    // Asks before a file's unsaved changes would be dropped; false when the user cancels
    private async Task<bool> LeaveFileAsync()
    {
        if (UnsavedFile is not { } file)
            return true;
        switch (await Dialogs.UnsavedAsync(_window, new[] { Path.GetFileName(file) }))
        {
            case Dialogs.UnsavedChoice.Save:
                await SaveAsync();
                return UnsavedFile == null;
            case Dialogs.UnsavedChoice.Discard:
                return true;
            default:
                return false;
        }
    }

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

    // Row counts in the table list after rows were added or removed; the selection stays
    private async Task RefreshCountsAsync()
    {
        string? selected = (_tables.SelectedItem as StoredTable)?.File;
        IDbcStore store = await _session.StoreAsync();
        List<StoredTable> tables = await store.ListTablesAsync();
        _refreshing = true;
        _tables.ItemsSource = tables;
        _tables.SelectedItem = tables.FirstOrDefault(table => table.File == selected);
        _refreshing = false;
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
        Button importCsv = Ui.AsyncButton("Import CSV...", ImportCsvAsync);
        Button sql = new Button { Content = "SQL..." };
        sql.Flyout = new MenuFlyout
        {
            Items =
            {
                MenuItem("Rows shown (after the filter)", () => ExportSqlAsync(changedOnly: false)),
                MenuItem("Rows changed from the clean file", () => ExportSqlAsync(changedOnly: true))
            }
        };
        ToolTip.SetTip(importCsv, "Replace the table's rows with a CSV file's; columns are matched by name");
        Button columns = Ui.AsyncButton("Columns...", ShowColumnsAsync);
        ToolTip.SetTip(save, "Save changes to the project, or to the file when one was opened from disk (Ctrl+S)");
        ToolTip.SetTip(sql, "SQL for AzerothCore's matching _dbc table");
        ToolTip.SetTip(columns, "The column layout, and renaming the columns of a guessed layout");

        WrapPanel toolbar = new WrapPanel { Margin = new Thickness(12, 10, 12, 6) };
        foreach (Control control in new Control[]
                 {
                     save, add, copy, remove, _filter, export, csv, importCsv, sql, columns
                 })
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
        if (!await LeaveFileAsync())
            return;
        try
        {
            _window.SetStatus("Loading " + file + "...");
            _table = await _session.LoadAsync(file);
            await _session.LoadNamingTablesAsync();
            _names.Forget();
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
        if (!await LeaveFileAsync())
            return;
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
        Ui.Themed(_info, TextBlock.ForegroundProperty,
                  _table.IsModified ? ThemeColours.WARNING : ThemeColours.MUTED_TEXT);
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
        string hidden = _grid.HiddenColumnCount > 0 ? "  -  " + _grid.HiddenColumnCount + " columns hidden" : "";
        _cell.Text = column.Name + " (" + column.Type + "), row key " + _table.Key(_grid.SelectedRow) + bits +
                     hidden + "  -  Enter or double-click to edit, Ctrl+C to copy, click a header to sort, " +
                     "right-click it to hide";
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
                // Saved races, classes or item classes rename the values shown for them
                _names.Forget();
                _window.SetStatus("Saved " + _table.Definition.File + " to the project.");
                await RefreshCountsAsync();
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
        if (ConfirmDeletes &&
            !await Dialogs.ConfirmAsync(_window, "Delete row", "Delete the row with key " + key + "? Undo brings " +
                                                               "it back until you close the table.", "Delete"))
            return;
        _table.RemoveRow(_grid.SelectedRow);
        ApplyFilter();
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
        await Task.Run(() => CsvFile.Write(path, table));
        _window.SetStatus("Wrote " + path + ".");
    }

    private async Task ImportCsvAsync()
    {
        if (_table == null)
            return;
        List<string> files = await Ui.PickFilesAsync(_window, "Import a CSV file", "CSV files", new[] { "*.csv" },
                                                     many: false);
        if (files.Count == 0)
            return;
        DbcDefinition definition = _table.Definition;
        if (!await Dialogs.ConfirmAsync(_window, "Import CSV", "Replace every row of " + definition.File +
                                                               " with the rows of " + Path.GetFileName(files[0]) +
                                                               "? Columns are matched by name.", "Replace"))
            return;
        try
        {
            string path = files[0];
            DbcTable imported = await Task.Run(() => CsvFile.Read(path, definition));
            if (_filePath != null)
            {
                // A file from disk: the rows are replaced here and written when it's saved
                _table = imported;
                imported.MarkModified();
            }
            else
            {
                IDbcStore store = await _session.StoreAsync();
                await Task.Run(() => store.ImportAsync(imported));
                _session.Forget(definition.File);
                _table = await _session.LoadAsync(definition.File);
            }

            _filter.Text = string.Empty;
            _grid.Show(_table);
            ShowInfo();
            _window.SetStatus("Replaced " + definition.File + " with " + imported.Rows.Count + " rows from the CSV.");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or
                                              InvalidOperationException or System.Data.Common.DbException)
        {
            await Dialogs.MessageAsync(_window, "Not imported", "The CSV file couldn't be imported: " +
                                                                exception.Message);
        }
    }

    private async Task ExportSqlAsync(bool changedOnly)
    {
        if (_table == null)
            return;
        DbcTable table = _table;
        List<int> rows;
        if (changedOnly)
        {
            DbcTable? clean = await Task.Run(() => DbcMerge.LoadSource(_session.Settings, table.Definition.File,
                                                                       _session.Definitions));
            if (clean == null)
            {
                await Dialogs.MessageAsync(_window, "No clean file", "There's no clean " + table.Definition.File +
                                                                     " to compare with. Set the Clean DBC folder " +
                                                                     "or the WoW Data folder in Project settings.");
                return;
            }

            rows = DbcMerge.Changed(clean, table);
        }
        else
        {
            string filterText = (_filter.Text ?? string.Empty).Trim();
            rows = filterText.Length == 0 ? Enumerable.Range(0, table.Rows.Count).ToList()
                                          : Enumerable.Range(0, table.Rows.Count).Where(Matcher(table, filterText))
                                                      .ToList();
        }

        string target = table.Definition.Table.Length > 0 ? table.Definition.Table : table.Definition.Name;
        string? path = await Ui.SaveFileAsync(_window, "Save SQL", target + ".sql", "SQL file", "sql");
        if (path == null)
            return;
        await Task.Run(() => SqlExport.Write(path, table, rows, target));
        _window.OpenWritten(path);
        _window.SetStatus("Wrote " + rows.Count + " rows to " + path + ".");
    }

    // Undoes or redoes the last change to the open table
    public void Undo(bool redo)
    {
        if (_table == null)
            return;
        int row = redo ? _table.Redo() : _table.Undo();
        if (row < 0)
        {
            _window.SetStatus(redo ? "Nothing to redo." : "Nothing to undo.");
            return;
        }

        ApplyFilter();
        _grid.SelectRow(row);
        _session.NotifyChanged();
        ShowInfo();
    }

    private static MenuItem MenuItem(string header, Func<Task> action)
    {
        MenuItem item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private async Task ShowColumnsAsync()
    {
        if (_table == null)
            return;
        ColumnsWindow dialog = new ColumnsWindow(_table.Definition, _session.Settings.DefinitionsFolder);
        if (await dialog.ShowDialog<bool>(_window))
        {
            _session.ReloadDefinitions();
            _window.SetStatus("Saved the layout. Import " + _table.Definition.File + " again to use the new names " +
                              "in the project.");
        }
    }
}
