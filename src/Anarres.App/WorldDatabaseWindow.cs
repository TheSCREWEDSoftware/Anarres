using System.Globalization;
using Anarres.Core.Settings;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// Asks where AzerothCore's world database is, before a spell is written into its spell_dbc table
public sealed class WorldDatabaseWindow : Window
{
    private const string DEFAULT_WORLD_DATABASE = "acore_world";

    private readonly TextBox _host = new TextBox { Width = 200 };
    private readonly TextBox _port = new TextBox { Width = 80 };
    private readonly TextBox _user = new TextBox { Width = 200 };
    private readonly TextBox _password = new TextBox { Width = 200, PasswordChar = '*' };
    private readonly TextBox _database = new TextBox { Width = 200, Text = DEFAULT_WORLD_DATABASE };

    public WorldDatabaseWindow(ProjectSettings settings, uint spellId, string spellName)
    {
        Title = "Send to the world database";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _host.Text = settings.MySqlHost;
        _port.Text = settings.MySqlPort.ToString(CultureInfo.InvariantCulture);
        _user.Text = settings.MySqlUser;
        _password.Text = settings.MySqlPassword;

        Button send = new Button { Content = "Send", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        send.Click += (_, _) => Close(new MySqlSettings(
            (_host.Text ?? string.Empty).Trim(),
            int.TryParse(_port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                ? port
                : MySqlSettings.DEFAULT_PORT,
            (_user.Text ?? string.Empty).Trim(), _password.Text ?? string.Empty,
            (_database.Text ?? DEFAULT_WORLD_DATABASE).Trim()));
        cancel.Click += (_, _) => Close(null);
        StackPanel buttons = Ui.Horizontal(send, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 6,
            Children =
            {
                Ui.Hint("Spell " + spellId + " (" + spellName + ") replaces any row with the same ID in spell_dbc. " +
                        "The worldserver loads spell_dbc on top of Spell.dbc at start-up. The client still needs " +
                        "the spell in its patch MPQ to show it."),
                Ui.Row("Host and port", _host, _port),
                Ui.Row("User", _user),
                Ui.Row("Password", _password),
                Ui.Row("World database", _database),
                buttons
            }
        };
    }
}
