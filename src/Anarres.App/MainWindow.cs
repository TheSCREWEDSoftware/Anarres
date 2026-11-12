using Anarres.Core.Export;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Anarres.App;

// The window: a title bar with the menu and the two editors, the editor itself, and a status line
public sealed class MainWindow : Window
{
    private const double TITLE_BAR_HEIGHT = 40;
    // Room on the right for Windows' minimise, maximise and close buttons
    private const double CAPTION_BUTTONS_WIDTH = 140;
    private const double ICON_SIZE = 20;

    private readonly Session _session;
    private readonly SpellEditorView _spells;
    private readonly DbcEditorView _tables;
    private readonly ContentControl _content = new ContentControl { Margin = new Thickness(10, 0, 10, 0) };
    private readonly TextBlock _status = new TextBlock
    {
        Classes = { "hint" },
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _store = new TextBlock
    {
        Classes = { "hint" },
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _windowTitle = new TextBlock { Text = "Anarres", Classes = { "windowTitle" },
                                                              VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _spellTab = new ToggleButton { Content = "Spell Editor", Classes = { "subtle" } };
    private readonly ToggleButton _tableTab = new ToggleButton { Content = "DBC Editor", Classes = { "subtle" } };
    private readonly Grid _root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
    private const int STATUS_SECONDS = 12;

    private readonly Avalonia.Threading.DispatcherTimer _statusTimer = new Avalonia.Threading.DispatcherTimer
    {
        Interval = TimeSpan.FromSeconds(STATUS_SECONDS)
    };
    private bool _closingConfirmed;
    private AppOptions _options;

    public MainWindow(ProjectSettings settings, AppOptions? options = null)
    {
        _options = options ?? new AppOptions();
        Title = "Anarres";
        Width = 1400;
        Height = 880;
        MinWidth = 1000;
        MinHeight = 620;
        _session = new Session(settings);
        _spells = new SpellEditorView(this, _session);
        _tables = new DbcEditorView(this, _session);
        _session.Changed += UpdateStatus;

        Control titleBar = BuildTitleBar();
        Grid.SetRow(_content, 1);
        Control statusBar = BuildStatusBar();
        Grid.SetRow(statusBar, 2);
        _root.Children.Add(titleBar);
        _root.Children.Add(_content);
        _root.Children.Add(statusBar);
        Content = _root;

        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            ExtendClientAreaTitleBarHeightHint = TITLE_BAR_HEIGHT;
            // Maximised, Windows puts the window's edges off-screen; the content moves in to stay visible
            PropertyChanged += (_, change) =>
            {
                if (change.Property == OffScreenMarginProperty || change.Property == WindowStateProperty)
                    _root.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : default;
            };
        }

        _spellTab.Click += async (_, _) => await ShowSpellsAsync();
        _tableTab.Click += async (_, _) => await ShowTablesAsync();
        KeyDown += async (_, e) =>
        {
            bool control = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            if (e.Key == Key.S && control)
            {
                e.Handled = true;
                await SaveCurrentAsync();
            }
            else if (e.Key == Key.F && control)
            {
                e.Handled = true;
                if (_content.Content == _spells)
                    _spells.FocusFind();
                else
                    _tables.FocusFilter();
            }
            // A text box handles its own undo first; this one undoes table edits
            else if ((e.Key == Key.Z || e.Key == Key.Y) && control)
            {
                e.Handled = true;
                await UndoAsync(redo: e.Key == Key.Y || (e.KeyModifiers & KeyModifiers.Shift) != 0);
            }
        };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _status.Text = string.Empty;
        };
        RestoreWindow();
        UseOptions(_options);
        Opened += async (_, _) =>
        {
            if (_options.ShowSetup && !IsTest)
                await RunSetupAsync(reload: false);
            StartEditor start = _options.StartIn == StartEditor.Last ? _options.LastEditor : _options.StartIn;
            if (start == StartEditor.Tables)
                await ShowTablesAsync();
            else
                await ShowSpellsAsync();
        };
        Closing += OnClosing;
        UpdateStatus();
    }

    public Session Session => _session;

    internal SpellEditorView SpellEditor => _spells;

    // Tests don't write options.json in the user's folder
    internal bool IsTest { get; set; }

    internal DbcEditorView TableEditor => _tables;

    internal Task ShowTablesForTestAsync()
    {
        return ShowTablesAsync();
    }

    // Messages fade from the status line after a while, so an old one isn't read as news
    public void SetStatus(string text)
    {
        _status.Text = text;
        _statusTimer.Stop();
        if (text.Length > 0)
            _statusTimer.Start();
    }

    public void ShowError(Exception exception, string? log)
    {
        SetStatus("Something went wrong: " + exception.Message + (log != null ? " (details in " + log + ")" : ""));
    }

    // Opens the store, asking for the MySQL password when it isn't remembered; false when it can't be opened
    public async Task<bool> EnsureStoreAsync()
    {
        if (_session.IsOpen)
            return true;
        ProjectSettings settings = _session.Settings;
        if (settings.Store == StoreKind.MySql && !settings.RememberMySqlPassword && _session.MySqlPassword == null)
        {
            string? password = await Dialogs.PromptAsync(this, "MySQL password", "Password for " + settings.MySqlUser +
                                                         " on " + settings.MySqlHost + ":", password: true);
            if (password == null)
                return false;
            _session.MySqlPassword = password;
        }

        try
        {
            await _session.StoreAsync(createDatabase: true);
            UpdateStatus();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              UnauthorizedAccessException or System.Data.Common.DbException)
        {
            _session.MySqlPassword = null;
            SetStatus("The project couldn't be opened: " + exception.Message);
            await Dialogs.MessageAsync(this, "Project not opened", "The project couldn't be opened: " +
                                                                   exception.Message + "\n\nCheck Project settings.");
            return false;
        }
    }

    private Control BuildTitleBar()
    {
        Image icon = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://Anarres/Assets/anarres.png"))),
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            Margin = new Thickness(12, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        Menu menu = new Menu { VerticalAlignment = VerticalAlignment.Center };
        MenuItem file = new MenuItem { Header = "_File" };
        file.Items.Add(Item("_Project settings...", OpenSettingsAsync));
        file.Items.Add(Item("_Import...", OpenImportAsync));
        file.Items.Add(Item("_Export and deploy...", OpenExportAsync));
        file.Items.Add(Item("_Apply to server and client", ApplyAsync, "F6"));
        file.Items.Add(new Separator());
        file.Items.Add(Item("_Save", SaveCurrentAsync, "Ctrl+S"));
        file.Items.Add(Item("Save _all", SaveAllAsync));
        file.Items.Add(new Separator());
        file.Items.Add(Item("E_xit", () =>
        {
            Close();
            return Task.CompletedTask;
        }));
        MenuItem edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(Item("_Undo", () => UndoAsync(redo: false), "Ctrl+Z"));
        edit.Items.Add(Item("_Redo", () => UndoAsync(redo: true), "Ctrl+Y"));
        MenuItem tools = new MenuItem { Header = "_Tools" };
        tools.Items.Add(Item("_Setup...", () => RunSetupAsync(reload: true)));
        tools.Items.Add(new Separator());
        tools.Items.Add(Item("Build _patch MPQ from .dbc files...", BuildPatchFromFilesAsync));
        tools.Items.Add(Item("_Open the export folder", () =>
        {
            SystemShell.Open(_session.Settings.ExportFolder);
            return Task.CompletedTask;
        }));
        MenuItem view = new MenuItem { Header = "_View" };
        view.Items.Add(Item("_Options...", () => OpenOptionsAsync(OptionsWindow.APPEARANCE), "Ctrl+OemComma"));
        view.Items.Add(new Separator());
        view.Items.Add(Item("_Dark", () => SetLookAsync(ThemeLook.Dark)));
        view.Items.Add(Item("_Light", () => SetLookAsync(ThemeLook.Light)));
        view.Items.Add(Item("_System", () => SetLookAsync(ThemeLook.System)));
        MenuItem help = new MenuItem { Header = "_Help" };
        help.Items.Add(Item("_About Anarres", () => Dialogs.MessageAsync(this, "About Anarres",
            "Anarres 0.1.0\nA spell and DBC editor for AzerothCore (WoW 3.3.5a), on .NET and Avalonia.\n\n" +
            "Layouts and names come from AzerothCore. The workflow follows Stoneharry's WoW Spell Editor and " +
            "WDBX Editor.\n\nLicensed under the AGPL-3.0-or-later.")));
        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(view);
        menu.Items.Add(tools);
        menu.Items.Add(help);

        Button import = Ui.AsyncButton("Import", OpenImportAsync);
        Button export = Ui.AsyncButton("Export", OpenExportAsync);
        Button apply = Ui.AsyncButton("Apply", ApplyAsync, accent: true);
        ToolTip.SetTip(apply, "Save, then put the changes in place: the server's dbc folder, the client patch and, " +
                              "as set in Project settings, acore_world");
        ToolTip.SetTip(import, "Bring .dbc files into the project from a folder or the client's MPQs");
        ToolTip.SetTip(export, "Write .dbc files, copy them to the server and build the client patch MPQ");
        StackPanel tabs = Ui.Horizontal(_spellTab, _tableTab);
        tabs.Spacing = 2;
        StackPanel actions = Ui.Horizontal(import, export, apply);
        actions.VerticalAlignment = VerticalAlignment.Center;

        Grid bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            Height = TITLE_BAR_HEIGHT,
            Margin = OperatingSystem.IsWindows() ? new Thickness(0, 0, CAPTION_BUTTONS_WIDTH, 0)
                                                 : new Thickness(0, 4, 8, 4)
        };
        bar.Children.Add(icon);
        Grid.SetColumn(menu, 1);
        bar.Children.Add(menu);
        tabs.Margin = new Thickness(16, 0, 0, 0);
        tabs.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tabs, 2);
        bar.Children.Add(tabs);
        Grid.SetColumn(_windowTitle, 3);
        _windowTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _windowTitle.IsHitTestVisible = false;
        bar.Children.Add(_windowTitle);
        Grid.SetColumn(actions, 4);
        bar.Children.Add(actions);
        return new Border { Margin = new Thickness(0, 0, 0, 8), Child = bar };
    }

    private Control BuildStatusBar()
    {
        Grid bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 6) };
        bar.Children.Add(_status);
        Grid.SetColumn(_store, 1);
        bar.Children.Add(_store);
        return bar;
    }

    private MenuItem Item(string header, Func<Task> action, string? gesture = null)
    {
        MenuItem item = new MenuItem { Header = header };
        if (gesture != null)
            item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += async (_, _) => await action();
        return item;
    }

    private void UpdateStatus()
    {
        List<string> modified = _session.ModifiedTables.Select(table => table.Definition.File).ToList();
        _store.Text = _session.Description + (modified.Count > 0 ? "  -  unsaved: " + string.Join(", ", modified)
                                                                 : string.Empty);
        Ui.Themed(_store, TextBlock.ForegroundProperty,
                  modified.Count > 0 ? ThemeColours.WARNING : ThemeColours.MUTED_TEXT);
        string title = modified.Count > 0 ? "Anarres *" : "Anarres";
        Title = title;
        _windowTitle.Text = title;
    }

    private async Task ShowSpellsAsync()
    {
        _spellTab.IsChecked = true;
        _tableTab.IsChecked = false;
        _content.Content = _spells;
        if (_spells.Spells == null)
            await _spells.LoadAsync();
    }

    private async Task ShowTablesAsync()
    {
        _spellTab.IsChecked = false;
        _tableTab.IsChecked = true;
        _content.Content = _tables;
        await _tables.RefreshListAsync();
    }

    // A number typed in a box is written when the box loses focus; Ctrl+S and the menus don't move focus
    private void CommitEdits()
    {
        FocusManager?.ClearFocus();
    }

    private async Task SaveCurrentAsync()
    {
        CommitEdits();
        if (_content.Content == _spells)
            await _spells.SaveAsync();
        else
            await _tables.SaveAsync();
    }

    private Task UndoAsync(bool redo)
    {
        CommitEdits();
        if (_content.Content == _spells)
            _spells.Undo(redo);
        else
            _tables.Undo(redo);
        return Task.CompletedTask;
    }

    private async Task SaveAllAsync()
    {
        CommitEdits();
        try
        {
            await _session.SaveAllAsync();
            SetStatus("Saved every changed table.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              System.Data.Common.DbException)
        {
            await Dialogs.MessageAsync(this, "Not saved", "Saving failed: " + exception.Message);
        }
    }

    // Unsaved tables are offered for saving before anything replaces them
    private async Task<bool> ResolveUnsavedAsync()
    {
        CommitEdits();
        List<string> modified = _session.ModifiedTables.Select(table => table.Definition.File).ToList();
        if (_tables.UnsavedFile is { } file)
            modified.Add(file);
        if (modified.Count == 0)
            return true;
        switch (await Dialogs.UnsavedAsync(this, modified))
        {
            case Dialogs.UnsavedChoice.Save:
            {
                await SaveAllAsync();
                await _tables.SaveFileAsync();
                return !_session.ModifiedTables.Any() && _tables.UnsavedFile == null;
            }
            case Dialogs.UnsavedChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    private async Task OpenSettingsAsync()
    {
        if (!await ResolveUnsavedAsync())
            return;
        SettingsWindow dialog = new SettingsWindow(_session.Settings);
        ProjectSettings? settings = await dialog.ShowDialog<ProjectSettings?>(this);
        if (settings == null)
            return;
        settings.Save();
        _session.Use(settings);
        _tables.ForgetStoredTable();
        _session.MySqlPassword = dialog.TypedPassword;
        await ReloadAsync();
    }

    private async Task OpenImportAsync()
    {
        if (!await EnsureStoreAsync() || !await ResolveUnsavedAsync())
            return;
        ImportWindow dialog = new ImportWindow(_session);
        await dialog.ShowDialog(this);
        if (!dialog.Imported)
            return;
        _session.Settings.Save();
        _session.ForgetAll();
        _tables.ForgetStoredTable();
        await ReloadAsync();
    }

    private async Task OpenExportAsync()
    {
        if (!await EnsureStoreAsync() || !await ResolveUnsavedAsync())
            return;
        ExportWindow dialog = new ExportWindow(_session);
        await dialog.ShowDialog(this);
        _session.Settings.Save();
    }

    // The one-click deploy: save, then write the files where the server and client read them
    private async Task ApplyAsync()
    {
        CommitEdits();
        if (!await EnsureStoreAsync())
            return;
        ProjectSettings settings = _session.Settings;
        List<string> plan = new List<string>();
        if (settings.ApplyToServer && settings.ServerDbc().Length > 0)
            plan.Add("copy them to " + settings.ServerDbc() + (settings.ApplyBackupServer
                                                                  ? ", keeping the server's own copies once"
                                                                  : string.Empty));
        if (settings.ApplyPatch && settings.ClientData().Length > 0)
            plan.Add("build " + settings.PatchPath());
        if (settings.ApplyClearClientCache && settings.ClientData().Length > 0)
            plan.Add("empty " + settings.ClientCache());
        if (settings.ApplyWorldDatabase)
            plan.Add("put changed rows in " + settings.WorldDatabase + " on " + settings.WorldHost);
        if (plan.Count == 0)
        {
            await Dialogs.MessageAsync(this, "Apply", "Apply has nothing to do yet. Set the server data folder " +
                                                      "and the WoW client folder, and choose the steps, in File > " +
                                                      "Project settings.");
            return;
        }

        List<string> unsaved = _session.ModifiedTables.Select(table => table.Definition.File).ToList();
        List<string> files;
        if (settings.ExportEditedOnly)
        {
            SetStatus("Finding the edited tables...");
            IDbcStore edited = await _session.StoreAsync();
            files = await Task.Run(() => Deployment.EditedFilesAsync(edited, settings, _session.Definitions,
                                                                     unsaved));
            SetStatus(string.Empty);
            if (files.Count == 0)
            {
                await Dialogs.MessageAsync(this, "Apply", "No table has been edited since it was imported, so " +
                                                          "there's nothing to apply.");
                return;
            }
        }
        else
            files = settings.ExportTables.Count > 0 ? settings.ExportTables : new List<string> { SpellRecord.FILE };
        string message = (unsaved.Count > 0 ? "Save " + string.Join(", ", unsaved) + ", then write " : "Write ") +
                         string.Join(", ", files) + ", and " + string.Join("; ", plan) + ".";
        if (_options.ConfirmApply && !await Dialogs.ConfirmAsync(this, "Apply", message, "Apply"))
            return;

        await SaveAllAsync();
        if (_session.ModifiedTables.Any())
            return;
        try
        {
            IDbcStore store = await _session.StoreAsync();
            Progress<(string File, double Done)> progress = new Progress<(string File, double Done)>(update =>
            {
                if (update.File.Length > 0)
                    SetStatus("Applying " + update.File + "...");
            });
            ApplyResult result = await Task.Run(() => Deployment.ApplyAsync(store, settings, files,
                                                                             _session.Definitions, progress));
            List<string> lines = new List<string>(result.Done);
            lines.AddRange(result.Problems);
            if (System.Diagnostics.Process.GetProcessesByName("worldserver").Length > 0)
                lines.Add("The worldserver is running: restart it to load the new files.");
            if (System.Diagnostics.Process.GetProcessesByName("Wow").Length > 0)
                lines.Add("WoW is open: the patch loads when it starts again.");
            SetStatus(result.Problems.Count == 0 ? "Applied." : "Applied, with problems.");
            await Dialogs.MessageAsync(this, result.Problems.Count == 0 ? "Applied" : "Applied, with problems",
                                       string.Join("\n", lines.Select(line => "- " + line)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or System.Data.Common.DbException)
        {
            await Dialogs.MessageAsync(this, "Not applied", "Apply stopped: " + exception.Message +
                                                            (exception is IOException
                                                                 ? " A file may be in use; close WoW and try again."
                                                                 : string.Empty));
        }
    }

    private async Task BuildPatchFromFilesAsync()
    {
        List<string> files = await Ui.PickFilesAsync(this, "Pick the .dbc files for the patch", "DBC files",
                                                     new[] { "*.dbc" }, many: true);
        if (files.Count == 0)
            return;
        string? path = await Ui.SaveFileAsync(this, "Save the patch MPQ", _session.Settings.PatchName, "MPQ archive",
                                              "MPQ");
        if (path == null)
            return;
        await Task.Run(() => Anarres.Core.Export.Deployment.BuildPatch(path, files));
        SetStatus("Built " + path + " with " + files.Count + " files under DBFilesClient.");
    }

    // The first-start questions; Start saves them as the project settings and can go straight to importing
    internal async Task RunSetupAsync(bool reload)
    {
        if (reload && !await ResolveUnsavedAsync())
            return;
        SetupWindow dialog = new SetupWindow(_session.Settings, _options.ShowSetup);
        ProjectSettings? settings = await dialog.ShowDialog<ProjectSettings?>(this);
        if (_options.ShowSetup == dialog.DontShowAgain)
        {
            _options.ShowSetup = !dialog.DontShowAgain;
            if (!IsTest)
                _options.Save();
        }

        if (settings == null)
            return;
        if (!IsTest)
            settings.Save();
        _session.Use(settings);
        _session.ForgetAll();
        _tables.ForgetStoredTable();
        if (settings.MySqlPassword.Length > 0)
            _session.MySqlPassword = settings.MySqlPassword;
        if (dialog.ImportNow && await EnsureStoreAsync())
        {
            ImportWindow import = new ImportWindow(_session);
            await import.ShowDialog(this);
            if (import.Imported && !IsTest)
                _session.Settings.Save();
            _session.ForgetAll();
        }

        if (reload)
            await ReloadAsync();
    }

    private async Task OpenOptionsAsync(string section)
    {
        AppOptions? options = await new OptionsWindow(_options, section).ShowDialog<AppOptions?>(this);
        if (options == null)
            return;
        UseOptions(options);
        options.Save();
        SetStatus("Options saved.");
    }

    // The look alone, from the View menu; the colours go back to the built-in ones of that look
    private Task SetLookAsync(ThemeLook look)
    {
        AppOptions options = _options.Copy();
        options.Look = look;
        options.Colours = AppOptions.BUILT_IN_COLOURS;
        UseOptions(options);
        options.Save();
        return Task.CompletedTask;
    }

    private void UseOptions(AppOptions options)
    {
        _options = options;
        _session.Cache = options.KeepTableCache ? new DbcCache(DbcCache.DefaultFolder) : null;
        App.UseOptions(options);
        _spells.UseOptions(options);
        _tables.UseOptions(options);
        // Code-drawn parts pick the new colours up when they're drawn again
        InvalidateVisual();
    }

    private void RestoreWindow()
    {
        const double MIN_VISIBLE = 100;
        if (!_options.RememberWindow || _options.WindowWidth < MinWidth || _options.WindowHeight < MinHeight)
            return;
        Width = _options.WindowWidth;
        Height = _options.WindowHeight;
        if (_options.WindowX is { } x && _options.WindowY is { } y && x > -_options.WindowWidth + MIN_VISIBLE &&
            y >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)x, (int)y);
        }

        if (_options.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    // Where the window was, which editor and which spell, for next time
    private void RememberState()
    {
        _options.LastEditor = _content.Content == _tables ? StartEditor.Tables : StartEditor.Spells;
        if (_spells.CurrentSpellId is { } spell)
            _options.LastSpellId = spell;
        if (_options.RememberWindow)
        {
            _options.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _options.WindowX = Position.X;
                _options.WindowY = Position.Y;
                _options.WindowWidth = Width;
                _options.WindowHeight = Height;
            }
        }

        _options.Save();
    }

    private async Task ReloadAsync()
    {
        UpdateStatus();
        if (_content.Content == _tables)
            await _tables.RefreshListAsync();
        else
            await _spells.LoadAsync();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        CommitEdits();
        if (_closingConfirmed || (!_session.ModifiedTables.Any() && _tables.UnsavedFile == null))
        {
            if (!IsTest)
                RememberState();
            _session.Dispose();
            return;
        }

        e.Cancel = true;
        if (await ResolveUnsavedAsync())
        {
            _closingConfirmed = true;
            Close();
        }
    }
}
