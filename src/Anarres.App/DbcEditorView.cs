using System.Globalization;
using System.Text;
using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Models;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
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
    // Tables opened from the project this session, shown as tabs above the grid
    private readonly List<string> _open = new List<string>();
    private readonly WrapPanel _tabStrip = new WrapPanel { Margin = new Thickness(12, 8, 12, 0) };
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
    private readonly Button _preview;
    private readonly Button _spellEditor;
    // Set when the table came from a file on disk instead of the project
    private string? _filePath;
    private bool _refreshing;

    public DbcEditorView(MainWindow window, Session session)
    {
        _window = window;
        _session = session;
        _preview = Ui.AsyncButton("3D preview", PreviewAsync);
        _preview.IsVisible = false;
        _spellEditor = Ui.AsyncButton("Open in Spell Editor", async () =>
        {
            if (_table != null && _grid.SelectedRow >= 0)
                await _window.OpenInSpellEditorAsync(_table.Key(_grid.SelectedRow));
            else
                await _window.OpenInSpellEditorAsync(0);
        }, accent: true);
        ToolTip.SetTip(_spellEditor, "The selected spell in the spell editor, with its fields in tabs and named lists");
        _spellEditor.IsVisible = false;
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
                await OpenStoredAsync(stored.File, keep: !_previewTabs);
        };
        // Double-clicking a table in the list keeps its tab
        _tables.DoubleTapped += (_, _) =>
        {
            if (_tables.SelectedItem is StoredTable stored &&
                string.Equals(_transient, stored.File, StringComparison.OrdinalIgnoreCase))
            {
                _transient = null;
                ShowTabs();
            }
        };
        _tables.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<StoredTable>((table, _) =>
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    TableMark(table?.File),
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
            ShowTabs();
            RefreshMarks();
        };
        _grid.EditRejected += message => _window.SetStatus(message);
        _grid.PasteRequested += Paste;
        _grid.AddRowRequested += AddRow;
        _grid.CopyRowRequested += CopyRow;
        _grid.DeleteRowsRequested += () => _ = DeleteRowAsync();
        _grid.FilterRequested += text => _filter.Text = text;
        ToolTip.SetTip(_clientText, "Shows in red, in empty text cells, what the client's other locale folders have " +
                                    "there; it isn't saved. Import with All available to keep every language.");
        _clientText.IsCheckedChanged += async (_, _) =>
        {
            if (_table != null && _filePath == null)
                await ShowClientTextAsync(_table);
            else
                _grid.Placeholder = null;
            _grid.Refresh();
        };

        Children.Add(BuildList());
        Border right = Ui.Card(BuildEditor());
        SetColumn(right, 2);
        Children.Add(right);
    }

    public DbcTable? Current => _table;

    // Tables whose rows lead to a model get a 3D preview
    private static readonly string[] PreviewTables =
    {
        ModelSources.CREATURE_DISPLAY, ModelSources.CREATURE_MODEL, ModelSources.SPELL_VISUAL,
        ModelSources.EFFECT_NAME, ModelSources.ITEM_DISPLAY, ModelSources.OBJECT_DISPLAY, ModelSources.SHAPESHIFT
    };

    private async Task PreviewAsync()
    {
        if (_table == null || _grid.SelectedRow < 0)
        {
            _window.SetStatus("Select a row to preview its model.");
            return;
        }

        uint key = _table.Key(_grid.SelectedRow);
        ModelSources sources = await _session.ModelSourcesAsync();
        string file = _table.Definition.File;
        List<ModelChoice> found = new List<ModelChoice>();
        if (file.Equals(ModelSources.CREATURE_DISPLAY, StringComparison.OrdinalIgnoreCase) &&
            sources.ForCreatureDisplay(key) is { } creature)
            found.Add(creature);
        else if (file.Equals(ModelSources.CREATURE_MODEL, StringComparison.OrdinalIgnoreCase))
        {
            int column = _table.Definition.IndexOf("ModelName");
            string path = column >= 0 ? M2Model.ModelFile(_table.GetString(_grid.SelectedRow, column)) : "";
            if (path.Length > 0)
                found.Add(new ModelChoice("Creature model " + key, path, ModelChoice.NoTextures));
        }
        else if (file.Equals(ModelSources.SPELL_VISUAL, StringComparison.OrdinalIgnoreCase))
            found.AddRange(sources.ForSpellVisual(key));
        else if (file.Equals(ModelSources.EFFECT_NAME, StringComparison.OrdinalIgnoreCase) &&
                 sources.EffectFile(key) is { } effect)
            found.Add(new ModelChoice("Effect " + key, effect, ModelChoice.NoTextures));
        else if (file.Equals(ModelSources.ITEM_DISPLAY, StringComparison.OrdinalIgnoreCase) &&
                 sources.ForItemDisplay(key) is { } item)
            found.Add(item);
        else if (file.Equals(ModelSources.OBJECT_DISPLAY, StringComparison.OrdinalIgnoreCase) &&
                 sources.ForGameObjectDisplay(key) is { } gameObject)
            found.Add(gameObject);
        else if (file.Equals(ModelSources.SHAPESHIFT, StringComparison.OrdinalIgnoreCase))
            found.AddRange(sources.ForShapeshift(key));

        if (found.Count == 0)
        {
            _window.SetStatus("Row " + key + " has no model Anarres can show.");
            return;
        }

        await new ModelWindow(_session, found, file + " " + key).ShowDialog(_window);
    }

    // A tab per table opened this session; the shown one is marked, and each closes with its x
    private bool IsModified(string file)
    {
        return _session.ModifiedTables.Any(table => string.Equals(table.Definition.File, file,
                                                                  StringComparison.OrdinalIgnoreCase));
    }

    private async Task CloseAllTabsAsync()
    {
        _open.Clear();
        _transient = null;
        _table = null;
        _grid.Show(null);
        ShowInfo();
        ShowTabs();
        await Task.CompletedTask;
    }

    private void ShowTabs()
    {
        // An edited preview tab is kept
        if (_transient != null && IsModified(_transient))
            _transient = null;
        _spellEditor.IsVisible = _table != null && _filePath == null &&
                                 string.Equals(_table.Definition.File, "Spell.dbc", StringComparison.OrdinalIgnoreCase);
        _preview.IsVisible = _table != null &&
                             PreviewTables.Contains(_table.Definition.File, StringComparer.OrdinalIgnoreCase);
        _tabStrip.Children.Clear();
        foreach (string file in _open)
        {
            bool current = _table != null && _filePath == null &&
                           string.Equals(_table.Definition.File, file, StringComparison.OrdinalIgnoreCase);
            bool modified = _session.ModifiedTables.Any(table => string.Equals(table.Definition.File, file,
                                                                               StringComparison.OrdinalIgnoreCase));
            Button tab = new Button
            {
                Content = file.Replace("_", "__") + (modified ? " *" : string.Empty),
                Padding = new Thickness(10, 3),
                Margin = new Thickness(0, 0, 0, 4)
            };
            if (current)
                tab.Classes.Add("accent");
            if (string.Equals(file, _transient, StringComparison.OrdinalIgnoreCase))
            {
                tab.FontStyle = FontStyle.Italic;
                ToolTip.SetTip(tab, "A preview: the next table from the list replaces it until it's edited or " +
                                    "double-clicked");
                tab.DoubleTapped += (_, _) =>
                {
                    _transient = null;
                    ShowTabs();
                };
            }
            tab.Click += async (_, _) => await OpenStoredAsync(file);
            Button close = new Button
            {
                Content = "x",
                Padding = new Thickness(6, 3),
                Margin = new Thickness(0, 0, 8, 4)
            };
            ToolTip.SetTip(close, "Close the tab; unsaved changes stay until you save or revert them");
            close.Click += async (_, _) => await CloseTabAsync(file);
            _tabStrip.Children.Add(tab);
            _tabStrip.Children.Add(close);
        }

        if (_open.Count > 1)
        {
            Button closeAll = Ui.AsyncButton("Close all", CloseAllTabsAsync);
            closeAll.Margin = new Thickness(0, 0, 0, 4);
            closeAll.Padding = new Thickness(10, 3);
            ToolTip.SetTip(closeAll, "Close every tab; unsaved changes stay until you save or revert them");
            _tabStrip.Children.Add(closeAll);
        }
    }

    private async Task CloseTabAsync(string file)
    {
        _open.RemoveAll(open => string.Equals(open, file, StringComparison.OrdinalIgnoreCase));
        bool current = _table != null &&
                       string.Equals(_table.Definition.File, file, StringComparison.OrdinalIgnoreCase);
        if (current && _open.Count > 0)
        {
            await OpenStoredAsync(_open[^1]);
            return;
        }

        if (current)
        {
            _table = null;
            _grid.Show(null);
            ShowInfo();
        }

        ShowTabs();
    }

    // Ctrl+V: rows copied from this or another table go in as new rows
    private void Paste(string text)
    {
        if (_table == null)
            return;
        PasteResult result = RowPaste.Paste(_table, text);
        if (result.Added == 0)
        {
            _window.SetStatus("Nothing to paste: copy rows (Shift+click, then Ctrl+C) first.");
            return;
        }

        _filter.Text = string.Empty;
        _grid.Show(_table);
        // Pasted rows go at the bottom; they're selected and scrolled to
        _grid.SelectRows(Enumerable.Range(_table.Rows.Count - result.Added, result.Added).ToList());
        _session.NotifyChanged();
        ShowInfo();
        ShowTabs();
        RefreshMarks();
        _window.SetStatus("Pasted " + result.Added + (result.Added == 1 ? " row" : " rows") +
                          (result.Renumbered > 0 ? "; " + result.Renumbered + " got new IDs because theirs were taken"
                                                 : string.Empty) + ". Save to keep them.");
    }

    private bool ConfirmDeletes { get; set; } = true;

    public void UseOptions(AppOptions options)
    {
        ConfirmDeletes = options.ConfirmDeletes;
        _grid.FlagsInHex = options.FlagsInHex;
        _grid.NamesInCells = options.NamesInCells;
        _grid.HideEmptyByDefault = options.HideEmptyColumns;
        _grid.UseFontSize(options.GridFontSize);
        _previewTabs = options.PreviewTabs;
    }

    private bool _previewTabs = true;
    // The tab that the next table opened from the list replaces, while it's unedited
    private string? _transient;
    private List<StoredTable> _allTables = new List<StoredTable>();
    private readonly TextBox _tableSearch = Ui.Clearable(new TextBox { Watermark = "Search tables" });

    // Tables edited since import, from the project; tables with unsaved changes come from the session
    private HashSet<string> _editedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _unsavedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // A stripe beside a table in the list: orange with unsaved changes, pink when edited since import
    private Control TableMark(string? file)
    {
        Border stripe = new Border { Width = 4, Height = 18, CornerRadius = new CornerRadius(2) };
        if (file != null && (_unsavedFiles.Contains(file) || _editedFiles.Contains(file)))
        {
            bool unsaved = _unsavedFiles.Contains(file);
            Ui.Themed(stripe, Border.BackgroundProperty, unsaved ? ThemeColours.CHANGED : ThemeColours.EDITED);
            ToolTip.SetTip(stripe, unsaved ? "Changes not saved yet" : "Edited since import");
        }

        return stripe;
    }

    // Redraws the list's stripes when a table's unsaved state changes
    private void RefreshMarks()
    {
        HashSet<string> unsaved = new HashSet<string>(_session.ModifiedTables.Select(table => table.Definition.File),
                                                      StringComparer.OrdinalIgnoreCase);
        if (unsaved.SetEquals(_unsavedFiles))
            return;
        _unsavedFiles = unsaved;
        ShowTableList();
    }

    private async Task LoadEditedAsync(IDbcStore store)
    {
        try
        {
            _editedFiles = new HashSet<string>((await store.EditedTablesAsync()).Select(table => table.File),
                                               StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException)
        {
            _editedFiles.Clear();
        }

        _unsavedFiles = new HashSet<string>(_session.ModifiedTables.Select(table => table.Definition.File),
                                            StringComparer.OrdinalIgnoreCase);
    }

    private void ShowTableList()
    {
        string text = _tableSearch.Text?.Trim() ?? string.Empty;
        string? selected = (_tables.SelectedItem as StoredTable)?.File;
        _refreshing = true;
        List<StoredTable> shown = _allTables
                                  .Where(table => table.File.Contains(text, StringComparison.OrdinalIgnoreCase))
                                  .ToList();
        _tables.ItemsSource = shown;
        _tables.SelectedItem = shown.FirstOrDefault(table => table.File == selected);
        _refreshing = false;
    }

    internal DbcGrid Grid => _grid;

    public void FocusFilter()
    {
        _filter.Focus();
        _filter.SelectAll();
    }

    public Task OpenTableAsync(string file)
    {
        return OpenStoredAsync(file);
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
            _allTables = tables;
            await LoadEditedAsync(store);
            ShowTableList();
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
        _allTables = await store.ListTablesAsync();
        await LoadEditedAsync(store);
        ShowTableList();
        _tables.SelectedItem = (_tables.ItemsSource as List<StoredTable>)
                               ?.FirstOrDefault(table => table.File == selected);
    }

    private Control BuildList()
    {
        Button open = Ui.AsyncButton("Open .dbc file...", OpenFileAsync);
        Button refresh = Ui.AsyncButton("Refresh", RefreshListAsync);
        ToolTip.SetTip(open, "Edit a .dbc file straight from disk, without importing it, like WDBX Editor");
        StackPanel buttons = Ui.Horizontal(open, refresh);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        TextBlock title = Ui.Title("Tables in the project");
        _tableSearch.TextChanged += (_, _) => ShowTableList();
        _tableSearch.Margin = new Thickness(0, 0, 0, 6);
        DockPanel panel = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(_tableSearch, Dock.Top);
        panel.Children.Add(_tableSearch);
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
        ToolTip.SetTip(_preview, "The selected row's model in 3D, from the client's files");
        ToolTip.SetTip(save, "Save changes to the project, or to the file when one was opened from disk (Ctrl+S)");
        ToolTip.SetTip(sql, "SQL for AzerothCore's matching _dbc table");
        ToolTip.SetTip(columns, "The column layout, and renaming the columns of a guessed layout");

        WrapPanel toolbar = new WrapPanel { Margin = new Thickness(12, 10, 12, 6) };
        foreach (Control control in new Control[]
                 {
                     _spellEditor, save, add, copy, remove, _filter, export, csv, importCsv, sql, columns, _preview,
                     _clientText
                 })
        {
            control.Margin = new Thickness(0, 0, 8, 4);
            toolbar.Children.Add(control);
        }

        _info.Margin = new Thickness(12, 0, 12, 6);
        DockPanel infoRow = new DockPanel();
        Control legend = Legend();
        DockPanel.SetDock(legend, Dock.Right);
        infoRow.Children.Add(legend);
        infoRow.Children.Add(_info);
        DockPanel panel = new DockPanel();
        DockPanel.SetDock(_tabStrip, Dock.Top);
        panel.Children.Add(_tabStrip);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(infoRow, Dock.Top);
        DockPanel.SetDock(_cell, Dock.Bottom);
        panel.Children.Add(toolbar);
        panel.Children.Add(infoRow);
        panel.Children.Add(_cell);
        panel.Children.Add(new Border { Margin = new Thickness(12, 0, 12, 4), Child = _grid, ClipToBounds = true });
        return panel;
    }

    private async Task OpenStoredAsync(string file, bool keep = true)
    {
        if (!await LeaveFileAsync())
            return;
        try
        {
            _window.SetStatus("Loading " + file + "...");
            _table = await _session.LoadAsync(file);
            string opened = _table.Definition.File;
            if (!_open.Contains(opened, StringComparer.OrdinalIgnoreCase))
            {
                // A preview tab is replaced in place by the next table, unless it was edited
                int replace = _transient == null ? -1 : _open.FindIndex(name => string.Equals(name, _transient,
                                                                       StringComparison.OrdinalIgnoreCase));
                bool edited = _transient != null && IsModified(_transient);
                if (!keep && replace >= 0 && !edited)
                    _open[replace] = opened;
                else
                    _open.Add(opened);
                _transient = keep ? null : opened;
            }
            else if (keep && string.Equals(_transient, opened, StringComparison.OrdinalIgnoreCase))
            {
                _transient = null;
            }
            ShowTabs();
            await _session.LoadNamingTablesAsync();
            _names.Forget();
            _filePath = null;
            _filter.Text = string.Empty;
            _grid.Clean = null;
            _grid.Placeholder = null;
            _grid.Show(_table);
            ShowInfo();
            _window.SetStatus(string.Empty);
            await ShowCleanAsync(_table);
            await ShowClientTextAsync(_table);
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
            _grid.Clean = null;
            _grid.Placeholder = null;
            _grid.Show(_table);
            ShowInfo();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or
                                              UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(_window, "Not opened", "The file couldn't be read: " + exception.Message);
        }
    }

    // What the row colours mean, beside the table's details
    private static Control Legend()
    {
        StackPanel legend = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 12, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach ((RowMark mark, string text, string tip) in new[]
                 {
                     (RowMark.New, "New", "Added and not saved yet"),
                     (RowMark.Edited, "Changed", "Changed and not saved yet; the changed cells are brighter"),
                     (RowMark.SavedNew, "Added", "Saved, and not in the clean file: added since import"),
                     (RowMark.SavedEdited, "Edited", "Saved, and different from the clean file: changed since import")
                 })
        {
            Border swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2) };
            Ui.Themed(swatch, Border.BackgroundProperty, RowLook.ColourKey(mark));
            TextBlock label = new TextBlock
            {
                Text = text,
                Classes = { "hint" },
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel item = Ui.Horizontal(swatch, label);
            item.Spacing = 4;
            ToolTip.SetTip(item, tip);
            legend.Children.Add(item);
        }

        return legend;
    }

    // The clean copy of a project table, read once, to mark what changed since import
    private readonly Dictionary<string, DbcTable?> _cleanTables =
        new Dictionary<string, DbcTable?>(StringComparer.OrdinalIgnoreCase);

    // The client's other languages in red in a table's empty text cells, read once per table and session
    private readonly CheckBox _clientText = new CheckBox { Content = "Client text", IsChecked = true };

    private async Task ShowClientTextAsync(DbcTable table)
    {
        _grid.Placeholder = null;
        if (_clientText.IsChecked != true || !LocaleMerge.HasText(table.Definition))
            return;
        // Name_Lang_deDE is the Name field's 4th language: its base name and the language's place
        Dictionary<int, (string Field, int Slot)> languages = new Dictionary<int, (string, int)>();
        for (int column = 0; column < table.Definition.Columns.Count; ++column)
        {
            string name = table.Definition.Columns[column].Name;
            int at = name.LastIndexOf("_Lang_", StringComparison.OrdinalIgnoreCase);
            int slot = at < 0 ? -1 : Array.IndexOf(SpellRecord.Locales, name[(at + "_Lang_".Length)..]);
            if (slot >= 0 && table.Definition.Columns[column].Type == DbcColumnType.String)
                languages[column] = (name[..at], slot);
        }

        List<LocaleTexts> texts = await _session.ClientTextsAsync(table.Definition.File);
        if (!ReferenceEquals(_table, table) || texts.Count == 0)
            return;
        _grid.Placeholder = (row, column) =>
            languages.TryGetValue(column, out (string Field, int Slot) language) &&
            texts.FirstOrDefault(locale => locale.Slot == language.Slot) is { } found
                ? found.Get(table.Key(row), language.Field)
                : null;
        _grid.Refresh();
    }

    private async Task ShowCleanAsync(DbcTable table)
    {
        string file = table.Definition.File;
        if (!_cleanTables.TryGetValue(file, out DbcTable? clean))
        {
            try
            {
                clean = await Task.Run(() => DbcMerge.LoadSource(_session.Settings, file, _session.Definitions));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                                  UnauthorizedAccessException)
            {
                clean = null;
            }

            _cleanTables[file] = clean;
        }

        if (ReferenceEquals(_table, table))
        {
            _grid.Clean = clean;
            _grid.Refresh();
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
                ShowTabs();
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
        List<int> rows = _grid.SelectedRows;
        string what = rows.Count == 1 ? "the row with key " + _table.Key(rows[0]) : rows.Count + " rows";
        if (ConfirmDeletes &&
            !await Dialogs.ConfirmAsync(_window, "Delete rows", "Delete " + what + "? Undo brings them back " +
                                                                "until you close the table.", "Delete"))
            return;
        // From the last row up, so the indexes of the rest don't move; one undo brings them all back
        _table.BreakUndoStep();
        foreach (int row in rows.OrderByDescending(row => row))
            _table.RemoveRow(row);
        _table.BreakUndoStep();
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
