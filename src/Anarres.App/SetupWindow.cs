using System.Globalization;
using Anarres.Core.Settings;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Anarres.App;

// First-start questions like Stoneharry's editor asks; shown at every start until "Don't show this again"
public sealed class SetupWindow : Window
{
    private const double WINDOW_WIDTH = 720;
    private const double LOGO_SIZE = 96;

    private readonly ProjectSettings _settings;
    private readonly RadioButton _sqlite = new RadioButton
    {
        Content = "SQLite project file: one local file, nothing to install",
        GroupName = "setup-store"
    };
    private readonly RadioButton _mysql = new RadioButton
    {
        Content = "MySQL or MariaDB: query and bulk-edit the tables with any SQL tool",
        GroupName = "setup-store"
    };
    private readonly TextBox _sqlitePath = new TextBox { Width = 420 };
    private readonly TextBox _host = new TextBox { Width = 200 };
    private readonly TextBox _port = new TextBox { Width = 80 };
    private readonly TextBox _user = new TextBox { Width = 200 };
    private readonly TextBox _password = new TextBox { Width = 200, PasswordChar = '*' };
    private readonly TextBox _database = new TextBox { Width = 200 };
    private readonly TextBox _source = new TextBox();
    private readonly TextBox _client = new TextBox();
    private readonly TextBox _server = new TextBox();
    private readonly CheckBox _world = new CheckBox
    {
        Content = "Apply also updates spell__dbc (and the other __dbc tables) in the world database"
    };
    private readonly TextBox _worldHost = new TextBox { Width = 200 };
    private readonly TextBox _worldPort = new TextBox { Width = 80 };
    private readonly TextBox _worldUser = new TextBox { Width = 200 };
    private readonly TextBox _worldPassword = new TextBox { Width = 200, PasswordChar = '*' };
    private readonly TextBox _worldDatabase = new TextBox { Width = 200 };
    private readonly CheckBox _import = new CheckBox { Content = "Import the clean files when I press Start" };
    private readonly CheckBox _dontShow = new CheckBox { Content = "Don't show this again (Tools > Setup opens it)" };
    private readonly StackPanel _mysqlPanel;
    private readonly StackPanel _worldPanel;

    public SetupWindow(ProjectSettings settings, bool showAgain)
    {
        _settings = settings.Clone();
        Title = "Set up Anarres";
        Width = WINDOW_WIDTH;
        Height = 760;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _sqlite.IsChecked = settings.Store == StoreKind.Sqlite;
        _mysql.IsChecked = settings.Store == StoreKind.MySql;
        _sqlitePath.Text = settings.SqlitePath;
        _host.Text = settings.MySqlHost;
        _port.Text = settings.MySqlPort.ToString(CultureInfo.InvariantCulture);
        _user.Text = settings.MySqlUser;
        _password.Text = settings.MySqlPassword;
        _database.Text = settings.MySqlDatabase;
        _source.Text = settings.SourceDbcFolder;
        _client.Text = settings.ClientDataFolder;
        _server.Text = settings.ServerDbcFolder;
        _world.IsChecked = settings.ApplyWorldDatabase;
        _worldHost.Text = settings.WorldHost;
        _worldPort.Text = settings.WorldPort.ToString(CultureInfo.InvariantCulture);
        _worldUser.Text = settings.WorldUser;
        _worldPassword.Text = settings.WorldPassword;
        _worldDatabase.Text = settings.WorldDatabase;
        _import.IsChecked = true;
        _dontShow.IsChecked = !showAgain;

        Image logo = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://Anarres/Assets/anarres.png"))),
            Width = LOGO_SIZE,
            Height = LOGO_SIZE,
            Margin = new Thickness(0, 0, 16, 0)
        };
        TextBlock welcome = new TextBlock
        {
            Text = "Welcome to Anarres",
            FontSize = 22,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        StackPanel heading = Ui.Horizontal(logo, Ui.Vertical(4, welcome, Ui.Hint(
            "A few questions to start. Everything here can be changed later in File > Project settings.")));

        _mysqlPanel = Ui.Vertical(2,
            Ui.Row("Host and port", _host, _port),
            Ui.Row("User", _user),
            Ui.Row("Password", _password),
            Ui.Row("Database", _database),
            Ui.Hint("Created on first use. AzerothCore's defaults are filled in."));
        _worldPanel = Ui.Vertical(2,
            Ui.Row("Host and port", _worldHost, _worldPort),
            Ui.Row("User", _worldUser),
            Ui.Row("Password", _worldPassword),
            Ui.Row("World database", _worldDatabase));
        _sqlite.IsCheckedChanged += (_, _) => UpdateEnabled();
        _world.IsCheckedChanged += (_, _) => UpdateEnabled();
        UpdateEnabled();

        Button browse = new Button { Content = "Browse..." };
        browse.Click += async (_, _) =>
        {
            string? path = await Ui.SaveFileAsync(this, "Project file", Path.GetFileName(_sqlitePath.Text ?? ""),
                                                  "Anarres project", SqliteDbcStore.EXTENSION.TrimStart('.'));
            if (path != null)
                _sqlitePath.Text = path;
        };

        StackPanel body = new StackPanel { Margin = new Thickness(20), Spacing = 4 };
        body.Children.Add(heading);
        body.Children.Add(Ui.Group("1. Where your edits are kept",
                                   _sqlite, Ui.Row("Project file", _sqlitePath, browse), _mysql, _mysqlPanel));
        body.Children.Add(Ui.Group("2. Clean files to start from",
                                   Ui.Row("Clean DBC folder", Ui.FolderPicker(this, _source, "Clean DBC folder")),
                                   Ui.Hint(SettingsWindow.CLEAN_FOLDER_HINT),
                                   Ui.Row("WoW client folder", Ui.FolderPicker(this, _client, "WoW client folder")),
                                   Ui.Hint("The folder with Wow.exe, or its Data folder; Apply builds the patch MPQ " +
                                           "there.")));
        body.Children.Add(Ui.Group("3. Your AzerothCore server",
                                   Ui.Row("Server data folder", Ui.FolderPicker(this, _server, "Server data folder")),
                                   Ui.Hint("The worldserver's DataDir (with dbc, maps and vmaps); Apply copies the " +
                                           "edited .dbc files there."),
                                   _world, _worldPanel,
                                   Ui.Hint("spell_dbc holds server-side spells and changes to existing ones; the " +
                                           "worldserver loads it over its Spell.dbc. Apply writes the rows that " +
                                           "differ from the clean files.")));
        body.Children.Add(Ui.Group("4. Finish", _import, _dontShow));

        Button start = new Button { Content = "Start", IsDefault = true, Classes = { "accent" } };
        Button skip = new Button { Content = "Skip", IsCancel = true };
        start.Click += (_, _) => Close(Build());
        skip.Click += (_, _) => Close(null);
        StackPanel buttons = Ui.Horizontal(start, skip);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10);

        DockPanel root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
    }

    // Read after the window closes, whether it was started or skipped
    public bool DontShowAgain => _dontShow.IsChecked == true;

    public bool ImportNow => _import.IsChecked == true;

    private void UpdateEnabled()
    {
        _sqlitePath.IsEnabled = _sqlite.IsChecked == true;
        _mysqlPanel.IsEnabled = _sqlite.IsChecked != true;
        _worldPanel.IsEnabled = _world.IsChecked == true;
    }

    private ProjectSettings Build()
    {
        ProjectSettings settings = _settings;
        settings.Store = _sqlite.IsChecked == true ? StoreKind.Sqlite : StoreKind.MySql;
        settings.SqlitePath = (_sqlitePath.Text ?? string.Empty).Trim();
        settings.MySqlHost = (_host.Text ?? string.Empty).Trim();
        settings.MySqlPort = Port(_port, settings.MySqlPort);
        settings.MySqlUser = (_user.Text ?? string.Empty).Trim();
        settings.MySqlPassword = _password.Text ?? string.Empty;
        settings.MySqlDatabase = (_database.Text ?? string.Empty).Trim();
        settings.SourceDbcFolder = (_source.Text ?? string.Empty).Trim();
        settings.ClientDataFolder = (_client.Text ?? string.Empty).Trim();
        settings.ServerDbcFolder = (_server.Text ?? string.Empty).Trim();
        settings.ApplyWorldDatabase = _world.IsChecked == true;
        settings.WorldHost = (_worldHost.Text ?? string.Empty).Trim();
        settings.WorldPort = Port(_worldPort, settings.WorldPort);
        settings.WorldUser = (_worldUser.Text ?? string.Empty).Trim();
        settings.WorldPassword = _worldPassword.Text ?? string.Empty;
        settings.WorldDatabase = (_worldDatabase.Text ?? ProjectSettings.DEFAULT_WORLD_DATABASE).Trim();
        return settings;
    }

    private static int Port(TextBox box, int fallback)
    {
        return int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                   ? port
                   : fallback;
    }
}
