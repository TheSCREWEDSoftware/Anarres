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
    private readonly TextBox _locale = new TextBox { Width = 80 };
    private readonly TextBox _export = new TextBox();
    private readonly TextBox _server = new TextBox();
    private readonly TextBox _patch = new TextBox { Width = 200 };
    private readonly CheckBox _patchInLocale = new CheckBox { Content = "Put the patch in Data's locale folder" };
    private readonly TextBox _definitions = new TextBox();
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
        _locale.Text = settings.Locale;
        _export.Text = settings.ExportFolder;
        _server.Text = settings.ServerDbcFolder;
        _patch.Text = settings.PatchName;
        _patchInLocale.IsChecked = settings.PatchInLocaleFolder;
        _definitions.Text = settings.DefinitionsFolder;
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
                                   Ui.Hint("Unmodified 3.3.5a .dbc files, such as the dbc folder of AzerothCore's " +
                                           "client data, or an extracted DBFilesClient folder."),
                                   Ui.Row("WoW Data folder", Ui.FolderPicker(this, _client, "WoW Data folder")),
                                   Ui.Row("Locale", _locale)));
        body.Children.Add(Ui.Group("Export and deploy",
                                   Ui.Row("Export folder", Ui.FolderPicker(this, _export, "Export folder")),
                                   Ui.Row("Server dbc folder", Ui.FolderPicker(this, _server, "Server dbc folder")),
                                   Ui.Hint("The worldserver's DataDir dbc folder; exported files are copied there."),
                                   Ui.Row("Patch name", _patch),
                                   Ui.Row(string.Empty, _patchInLocale),
                                   Ui.Hint("The patch is written to the WoW Data folder above. Later letters win: " +
                                           "patch-A.MPQ loads after patch-3.MPQ.")));
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
            Locale = (_locale.Text ?? ProjectSettings.DEFAULT_LOCALE).Trim(),
            ExportFolder = (_export.Text ?? string.Empty).Trim(),
            ServerDbcFolder = (_server.Text ?? string.Empty).Trim(),
            PatchName = string.IsNullOrWhiteSpace(_patch.Text) ? ProjectSettings.DEFAULT_PATCH_NAME
                                                               : _patch.Text.Trim(),
            PatchInLocaleFolder = _patchInLocale.IsChecked == true,
            DefinitionsFolder = (_definitions.Text ?? string.Empty).Trim(),
            ExportTables = _original.ExportTables
        };
        return settings;
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
