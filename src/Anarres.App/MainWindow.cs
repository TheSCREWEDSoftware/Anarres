using Anarres.Core.Settings;
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
    private bool _closingConfirmed;

    public MainWindow(ProjectSettings settings)
    {
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
            if (e.Key == Key.S && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            {
                e.Handled = true;
                await SaveCurrentAsync();
            }
        };
        Opened += async (_, _) => await ShowSpellsAsync();
        Closing += OnClosing;
        UpdateStatus();
    }

    public Session Session => _session;

    public void SetStatus(string text)
    {
        _status.Text = text;
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
        file.Items.Add(new Separator());
        file.Items.Add(Item("_Save", SaveCurrentAsync, "Ctrl+S"));
        file.Items.Add(Item("Save _all", SaveAllAsync));
        file.Items.Add(new Separator());
        file.Items.Add(Item("E_xit", () =>
        {
            Close();
            return Task.CompletedTask;
        }));
        MenuItem tools = new MenuItem { Header = "_Tools" };
        tools.Items.Add(Item("Build _patch MPQ from .dbc files...", BuildPatchFromFilesAsync));
        tools.Items.Add(Item("_Open the export folder", () =>
        {
            SystemShell.Open(_session.Settings.ExportFolder);
            return Task.CompletedTask;
        }));
        MenuItem view = new MenuItem { Header = "_View" };
        view.Items.Add(Item("_Dark", () => SetThemeAsync(nameof(ThemeBase.Dark))));
        view.Items.Add(Item("_Light", () => SetThemeAsync(nameof(ThemeBase.Light))));
        MenuItem help = new MenuItem { Header = "_Help" };
        help.Items.Add(Item("_About Anarres", () => Dialogs.MessageAsync(this, "About Anarres",
            "Anarres 0.1.0\nA spell and DBC editor for AzerothCore (WoW 3.3.5a), on .NET and Avalonia.\n\n" +
            "Layouts and names come from AzerothCore. The workflow follows Stoneharry's WoW Spell Editor and " +
            "WDBX Editor.\n\nLicensed under the AGPL-3.0-or-later.")));
        menu.Items.Add(file);
        menu.Items.Add(view);
        menu.Items.Add(tools);
        menu.Items.Add(help);

        Button import = Ui.AsyncButton("Import", OpenImportAsync);
        Button export = Ui.AsyncButton("Export", OpenExportAsync, accent: true);
        ToolTip.SetTip(import, "Bring .dbc files into the project from a folder or the client's MPQs");
        ToolTip.SetTip(export, "Write .dbc files, copy them to the server and build the client patch MPQ");
        StackPanel tabs = Ui.Horizontal(_spellTab, _tableTab);
        tabs.Spacing = 2;
        StackPanel actions = Ui.Horizontal(import, export);
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
        _store.Foreground = modified.Count > 0 ? ThemeBrushes.Warning : ThemeBrushes.MutedText;
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

    private async Task SaveCurrentAsync()
    {
        if (_content.Content == _spells)
            await _spells.SaveAsync();
        else
            await _tables.SaveAsync();
    }

    private async Task SaveAllAsync()
    {
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
        List<string> modified = _session.ModifiedTables.Select(table => table.Definition.File).ToList();
        if (modified.Count == 0)
            return true;
        switch (await Dialogs.UnsavedAsync(this, modified))
        {
            case Dialogs.UnsavedChoice.Save:
                await SaveAllAsync();
                return !_session.ModifiedTables.Any();
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
        _session.MySqlPassword = dialog.TypedPassword;
        App.UseTheme(settings.Theme);
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

    private Task SetThemeAsync(string theme)
    {
        _session.Settings.Theme = theme;
        _session.Settings.Save();
        App.UseTheme(theme);
        // Code-drawn parts pick the new colours up when they're drawn again
        InvalidateVisual();
        _tables.InvalidateVisual();
        return Task.CompletedTask;
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
        if (_closingConfirmed || !_session.ModifiedTables.Any())
        {
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
