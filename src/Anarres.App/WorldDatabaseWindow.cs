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
    private readonly TextBox _host = new TextBox { Width = 200 };
    private readonly TextBox _port = new TextBox { Width = 80 };
    private readonly TextBox _user = new TextBox { Width = 200 };
    private readonly TextBox _password = new TextBox { Width = 200, PasswordChar = '*' };
    private readonly TextBox _database = new TextBox { Width = 200 };

    // what names the spells, such as "Spell 133 (Fireball)" or "12 spells"
    public WorldDatabaseWindow(ProjectSettings settings, string what)
    {
        Title = "Update spell_dbc";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _host.Text = settings.WorldHost;
        _port.Text = settings.WorldPort.ToString(CultureInfo.InvariantCulture);
        _user.Text = settings.WorldUser;
        _password.Text = settings.WorldPassword;
        _database.Text = settings.WorldDatabase;

        Button send = new Button { Content = "Write to spell__dbc", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        send.Click += (_, _) => Close(new MySqlSettings(
            (_host.Text ?? string.Empty).Trim(),
            int.TryParse(_port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                ? port
                : MySqlSettings.DEFAULT_PORT,
            (_user.Text ?? string.Empty).Trim(), _password.Text ?? string.Empty,
            (_database.Text ?? ProjectSettings.DEFAULT_WORLD_DATABASE).Trim()));
        cancel.Click += (_, _) => Close(null);
        StackPanel buttons = Ui.Horizontal(send, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 6,
            Children =
            {
                Ui.Hint(what + " will replace any rows with the same IDs in spell_dbc. spell_dbc holds " +
                        "server-side spells and changes to existing ones; the worldserver loads it over its " +
                        "Spell.dbc at start-up, so restart it afterwards. The client still needs the patch MPQ " +
                        "(Apply, F6) to show new or changed spells."),
                Ui.Row("Host and port", _host, _port),
                Ui.Row("User", _user),
                Ui.Row("Password", _password),
                Ui.Row("World database", _database),
                buttons
            }
        };
    }
}
