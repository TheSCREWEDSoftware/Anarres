using System.Globalization;
using Anarres.Core.Settings;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// Project settings: where the data lives (SQLite file or MySQL) and the folders for import, export and deploy
public sealed class SettingsWindow : Window
{
    // What the Clean DBC folder is for, here and in the setup window
    public const string CLEAN_FOLDER_HINT =
        "Optional: unmodified .dbc files, used to tell what you changed (spell_dbc rows, changed-only SQL, edited " +
        "tables). Left empty, Anarres uses the server's own files, keeping a copy the first time Apply replaces " +
        "one, then the client's MPQs without your patch. Set it when those already hold earlier edits.";

    private const double WINDOW_WIDTH = 760;
    private const double WINDOW_HEIGHT = 720;

    private readonly ProjectSettings _original;
    private readonly RadioButton _sqlite = new RadioButton { Content = "SQLite project file (local, nothing to run)",
                                                             GroupName = "store" };
    private readonly RadioButton _mysql = new RadioButton
    {
        Content = "MySQL or MariaDB database",
        GroupName = "store"
    };
    private readonly TextBox _sqlitePath = new TextBox();
    private readonly TextBox _host = new TextBox { Width = 200 };
    private readonly TextBox _port = new TextBox { Width = 80 };
    private readonly TextBox _user = new TextBox { Width = 200 };
    private readonly TextBox _password = new TextBox { Width = 200, PasswordChar = '*' };
    private readonly CheckBox _remember = new CheckBox { Content = "Remember password (saved in settings.json)" };
    private readonly TextBox _database = new TextBox { Width = 200 };
    private readonly ComboBox _naming = new ComboBox
    {
        Width = 420,
        ItemsSource = new[]
        {
            "DBC names: Spell, SpellIcon... (a database just for DBC data)",
            "AzerothCore names: spell_dbc, ... (acore_world's own override tables)"
        }
    };
    private readonly TextBlock _test = new TextBlock
    {
        Classes = { "hint" },
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBox _source = new TextBox();
    private readonly TextBox _client = new TextBox();
    private readonly LocalePicker _locale;
    private readonly TextBox _export = new TextBox();
    private readonly TextBox _server = new TextBox();
    private readonly TextBox _patch = new TextBox { Width = 200 };
    private readonly CheckBox _patchInLocale = new CheckBox { Content = "Put the patch in Data's locale folder" };
    private readonly TextBox _definitions = new TextBox();
    private readonly TextBlock _serverResolved = new TextBlock { Classes = { "hint" } };
    private readonly TextBlock _clientResolved = new TextBlock { Classes = { "hint" } };
    private readonly CheckBox _applyServer = new CheckBox { Content = "Copy the .dbc files to the server" };
    private readonly CheckBox _applyBackup = new CheckBox
    {
        Content = "Keep the server's own copy of each file the first time it's replaced"
    };
    private readonly CheckBox _applyPatch = new CheckBox { Content = "Build the client patch MPQ" };
    private readonly CheckBox _allLanguages = new CheckBox
    {
        Content = "Export every language the client has (fills empty language columns from its locale folders)"
    };
    private readonly CheckBox _applyEdited = new CheckBox
    {
        Content = "Write only tables edited since import (otherwise the ones ticked in the last export)"
    };
    private readonly CheckBox _applyWorld = new CheckBox
    {
        Content = "Put rows changed from the clean files into acore_world's _dbc tables"
    };
    private readonly CheckBox _applyCache = new CheckBox
    {
        Content = "Empty the client's Cache folder, so it shows the new spell text"
    };
    private readonly TextBox _worldHost = new TextBox { Width = 200 };
    private readonly TextBox _worldPort = new TextBox { Width = 80 };
    private readonly TextBox _worldUser = new TextBox { Width = 200 };
    private readonly TextBox _worldPassword = new TextBox { Width = 200, PasswordChar = '*' };
    private readonly TextBox _worldDatabase = new TextBox { Width = 200 };
    private readonly StackPanel _mysqlPanel;

    public SettingsWindow(ProjectSettings settings)
    {
        _original = settings;
        Title = "Project settings";
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _sqlitePath.Text = settings.SqlitePath;
        _host.Text = settings.MySqlHost;
        _port.Text = settings.MySqlPort.ToString(CultureInfo.InvariantCulture);
        _user.Text = settings.MySqlUser;
        _password.Text = settings.MySqlPassword;
        _remember.IsChecked = settings.RememberMySqlPassword;
        _database.Text = settings.MySqlDatabase;
        _naming.SelectedIndex = settings.MySqlNaming == TableNaming.AzerothCore ? 1 : 0;
        _source.Text = settings.SourceDbcFolder;
        _client.Text = settings.ClientDataFolder;
        _locale = new LocalePicker(settings.Locale, settings.AllLocales);
        _locale.Detect(settings.ClientDataFolder);
        _export.Text = settings.ExportFolder;
        _server.Text = settings.ServerDbcFolder;
        _patch.Text = settings.PatchName;
        _patchInLocale.IsChecked = settings.PatchInLocaleFolder;
        _definitions.Text = settings.DefinitionsFolder;
        _applyEdited.IsChecked = settings.ExportEditedOnly;
        _allLanguages.IsChecked = settings.ExportAllLanguages;
        _applyServer.IsChecked = settings.ApplyToServer;
        _applyBackup.IsChecked = settings.ApplyBackupServer;
        _applyPatch.IsChecked = settings.ApplyPatch;
        _applyWorld.IsChecked = settings.ApplyWorldDatabase;
        _applyCache.IsChecked = settings.ApplyClearClientCache;
        _worldHost.Text = settings.WorldHost;
        _worldPort.Text = settings.WorldPort.ToString(CultureInfo.InvariantCulture);
        _worldUser.Text = settings.WorldUser;
        _worldPassword.Text = settings.WorldPassword;
        _worldDatabase.Text = settings.WorldDatabase;
        _server.TextChanged += (_, _) => ShowResolved();
        _client.TextChanged += (_, _) => ShowResolved();
        _locale.Changed += (_, _) => ShowResolved();
        _client.TextChanged += (_, _) => _locale.Detect(_client.Text ?? string.Empty);
        _patch.TextChanged += (_, _) => ShowResolved();
        _patchInLocale.IsCheckedChanged += (_, _) => ShowResolved();
        _sqlite.IsChecked = settings.Store == StoreKind.Sqlite;
        _mysql.IsChecked = settings.Store == StoreKind.MySql;

        Button browseSqlite = new Button { Content = "Browse..." };
        browseSqlite.Click += async (_, _) =>
        {
            string? path = await Ui.SaveFileAsync(this, "Project file", Path.GetFileName(_sqlitePath.Text ?? ""),
                                                  "Anarres project", SqliteDbcStore.EXTENSION.TrimStart('.'));
            if (path != null)
                _sqlitePath.Text = path;
        };
        _sqlitePath.Width = 420;
        Button test = Ui.AsyncButton("Test connection", TestAsync);
        _mysqlPanel = Ui.Vertical(2,
            Ui.Row("Host and port", _host, _port),
            Ui.Row("User", _user),
            Ui.Row("Password", _password),
            Ui.Row(string.Empty, _remember),
            Ui.Row("Database", _database),
            Ui.Row("Table names", _naming),
            Ui.Hint("A separate database (acore_dbc_extracted by default) keeps the editor's tables apart from the " +
                    "server's. With AzerothCore names, rows go into acore_world's *_dbc tables, which the " +
                    "worldserver loads on top of the .dbc files; import only the rows you change there."),
            Ui.Row(string.Empty, test, _test));
        _sqlite.IsCheckedChanged += (_, _) => UpdateEnabled();
        UpdateEnabled();

        StackPanel body = new StackPanel { Margin = new Thickness(20), Spacing = 4 };
        body.Children.Add(Ui.Group("Where the data is kept",
                                   _sqlite, Ui.Row("Project file", _sqlitePath, browseSqlite), _mysql, _mysqlPanel));
        body.Children.Add(Ui.Group("Import",
                                   Ui.Row("Clean DBC folder", Ui.FolderPicker(this, _source, "Clean DBC folder")),
                                   Ui.Hint(CLEAN_FOLDER_HINT),
                                   Ui.Row("Locale", _locale), _locale.Note));
        body.Children.Add(Ui.Group("Server and client",
                                   Ui.Row("Server data folder", Ui.FolderPicker(this, _server, "Server data folder")),
                                   Ui.Row(string.Empty, _serverResolved),
                                   Ui.Hint("The worldserver's DataDir (the folder with dbc, maps and vmaps), or its " +
                                           "dbc folder."),
                                   Ui.Row("WoW client folder", Ui.FolderPicker(this, _client, "WoW client folder")),
                                   Ui.Row(string.Empty, _clientResolved),
                                   Ui.Hint("The folder with Wow.exe, or its Data folder. Its MPQs give icons and " +
                                           "clean files, and the patch goes into it."),
                                   Ui.Row("Patch name", _patch),
                                   Ui.Row(string.Empty, _patchInLocale),
                                   Ui.Hint("Later letters win: patch-A.MPQ loads after patch-3.MPQ."),
                                   Ui.Row("Export folder", Ui.FolderPicker(this, _export, "Export folder"))));
        body.Children.Add(Ui.Group("Apply (one click: save, then put the changes in place)",
                                   _applyEdited, _allLanguages, _applyServer, _applyBackup, _applyPatch, _applyCache,
                                   _applyWorld,
                                   Ui.Row("World database", _worldHost, _worldPort),
                                   Ui.Row("User", _worldUser),
                                   Ui.Row("Password", _worldPassword),
                                   Ui.Row("Database", _worldDatabase),
                                   Ui.Hint("The client always needs whole .dbc files, so each edited table is " +
                                           "written in full. Rows for acore_world are only those that differ from " +
                                           "the clean files; the worldserver loads them over its .dbc files.")));
        ShowResolved();
        body.Children.Add(Ui.Group("Other",
                                   Ui.Row("Extra definitions", Ui.FolderPicker(this, _definitions,
                                                                               "Definitions folder")),
                                   Ui.Hint("Layouts saved from the DBC editor's Columns window go here and replace " +
                                           "the built-in ones."),
                                   Ui.Hint("Theme, text size and how the editors behave are under View > Options.")));

        Button save = new Button { Content = "Save", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => Close(Build());
        cancel.Click += (_, _) => Close(null);
        StackPanel buttons = Ui.Horizontal(save, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10);

        DockPanel root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
    }

    // The password typed this time, for this run even when it isn't remembered
    public string? TypedPassword => string.IsNullOrEmpty(_password.Text) ? null : _password.Text;

    private void UpdateEnabled()
    {
        bool sqlite = _sqlite.IsChecked == true;
        _sqlitePath.IsEnabled = sqlite;
        _mysqlPanel.IsEnabled = !sqlite;
    }

    private ProjectSettings Build()
    {
        ProjectSettings settings = new ProjectSettings
        {
            Store = _sqlite.IsChecked == true ? StoreKind.Sqlite : StoreKind.MySql,
            SqlitePath = (_sqlitePath.Text ?? string.Empty).Trim(),
            MySqlHost = (_host.Text ?? string.Empty).Trim(),
            MySqlPort = int.TryParse(_port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                            ? port
                            : MySqlSettings.DEFAULT_PORT,
            MySqlUser = (_user.Text ?? string.Empty).Trim(),
            MySqlPassword = _password.Text ?? string.Empty,
            RememberMySqlPassword = _remember.IsChecked == true,
            MySqlDatabase = (_database.Text ?? string.Empty).Trim(),
            MySqlNaming = _naming.SelectedIndex == 1 ? TableNaming.AzerothCore : TableNaming.DbcName,
            SourceDbcFolder = (_source.Text ?? string.Empty).Trim(),
            ClientDataFolder = (_client.Text ?? string.Empty).Trim(),
            Locale = _locale.Locale,
            AllLocales = _locale.All,
            ExportFolder = (_export.Text ?? string.Empty).Trim(),
            ServerDbcFolder = (_server.Text ?? string.Empty).Trim(),
            PatchName = string.IsNullOrWhiteSpace(_patch.Text) ? ProjectSettings.DEFAULT_PATCH_NAME
                                                               : _patch.Text.Trim(),
            PatchInLocaleFolder = _patchInLocale.IsChecked == true,
            DefinitionsFolder = (_definitions.Text ?? string.Empty).Trim(),
            ExportTables = _original.ExportTables,
            ExportEditedOnly = _applyEdited.IsChecked == true,
            ExportAllLanguages = _allLanguages.IsChecked == true,
            ApplyToServer = _applyServer.IsChecked == true,
            ApplyBackupServer = _applyBackup.IsChecked == true,
            ApplyPatch = _applyPatch.IsChecked == true,
            ApplyWorldDatabase = _applyWorld.IsChecked == true,
            ApplyClearClientCache = _applyCache.IsChecked == true,
            WorldHost = (_worldHost.Text ?? string.Empty).Trim(),
            WorldPort = int.TryParse(_worldPort.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                     out int worldPort) ? worldPort : MySqlSettings.DEFAULT_PORT,
            WorldUser = (_worldUser.Text ?? string.Empty).Trim(),
            WorldPassword = _worldPassword.Text ?? string.Empty,
            WorldDatabase = (_worldDatabase.Text ?? ProjectSettings.DEFAULT_WORLD_DATABASE).Trim()
        };
        return settings;
    }

    // Where files will actually go, so a DataDir or WoW folder can be checked before saving
    private void ShowResolved()
    {
        string server = ProjectSettings.ResolveServerDbc(_server.Text ?? string.Empty);
        _serverResolved.Text = server.Length == 0 ? "Not set." : "Files go to " + server;
        ProjectSettings probe = new ProjectSettings
        {
            ClientDataFolder = _client.Text ?? string.Empty,
            Locale = _locale.Locale,
            PatchName = string.IsNullOrWhiteSpace(_patch.Text) ? ProjectSettings.DEFAULT_PATCH_NAME
                                                               : _patch.Text.Trim(),
            PatchInLocaleFolder = _patchInLocale.IsChecked == true
        };
        _clientResolved.Text = probe.ClientData().Length == 0 ? "Not set." : "The patch goes to " + probe.PatchPath();
    }

    private async Task TestAsync()
    {
        _test.Text = "Connecting...";
        ProjectSettings settings = Build();
        try
        {
            using MySqlDbcStore store = await MySqlDbcStore.OpenAsync(settings.MySql(), settings.MySqlNaming);
            List<StoredTable> tables = await store.ListTablesAsync();
            _test.Text = "Connected. " + tables.Count + (tables.Count == 1 ? " DBC table" : " DBC tables") + " found.";
        }
        catch (MySqlConnector.MySqlException exception) when (exception.ErrorCode ==
                                                              MySqlConnector.MySqlErrorCode.UnknownDatabase)
        {
            _test.Text = "Connected. The database " + settings.MySqlDatabase + " doesn't exist yet; it's made " +
                         "the first time the project opens.";
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or InvalidOperationException)
        {
            _test.Text = "Couldn't connect: " + exception.Message;
        }
    }
}
