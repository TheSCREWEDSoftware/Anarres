using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Anarres.App;

// The version, what Anarres builds on, with links to each project, and the licence
public sealed class AboutWindow : Window
{
    public const string REPOSITORY = "https://github.com/TheSCREWEDSoftware/Anarres";
    private const double LOGO_SIZE = 72;

    public AboutWindow()
    {
        Title = "About Anarres";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Image logo = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://Anarres/Assets/anarres.png"))),
            Width = LOGO_SIZE,
            Height = LOGO_SIZE,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        TextBlock name = new TextBlock { Text = "Anarres " + MainWindow.AppVersion, FontSize = 20,
                                         FontWeight = FontWeight.SemiBold };
        StackPanel heading = Ui.Horizontal(logo, Ui.Vertical(4, name,
            Ui.Hint("A spell and DBC editor for AzerothCore (WoW 3.3.5a), on .NET and Avalonia."),
            Link("Anarres on GitHub", REPOSITORY)));

        Button ok = new Button { Content = "OK", IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => Close();
        StackPanel buttons = Ui.Horizontal(ok);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 6,
            Children =
            {
                heading,
                Ui.Title("Built on"),
                Line("Layouts, column and spell names: ", "AzerothCore",
                     "https://github.com/azerothcore/azerothcore-wotlk"),
                Line("Layout and workflow: Stoneharry's ", "WoW Spell Editor",
                     "https://github.com/stoneharry/WoW-Spell-Editor"),
                Line("and ", "DBC Editing Workflow", "https://github.com/stoneharry/DBC-Editing-Workflow"),
                Line("Table editing in the spirit of ", "WDBX Editor", "https://github.com/WowDevTools/WDBXEditor"),
                Line("Notes on every field, after ", "Keira3", "https://github.com/azerothcore/Keira3"),
                Line("Look and themes from its twin, ", "Urras", "https://github.com/TheSCREWEDSoftware/Urras"),
                Line("Interface: ", "Avalonia", "https://github.com/AvaloniaUI/Avalonia"),
                Ui.Hint("No code was taken from the editors above; Anarres follows their layout and workflow."),
                Ui.Title("Licence"),
                Line("Free software under the ", "GNU AGPL 3.0 or later", REPOSITORY + "/blob/main/LICENSE"),
                buttons
            }
        };
    }

    private static Control Line(string text, string linkText, string url)
    {
        return Ui.Horizontal(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                             Link(linkText, url));
    }

    // Looks like a link, opens the page in the browser
    private static Button Link(string text, string url)
    {
        Button link = new Button
        {
            Content = text,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center
        };
        Ui.Themed(link, ForegroundProperty, Anarres.Core.Settings.ThemeColours.ACCENT);
        ToolTip.SetTip(link, url);
        link.Click += (_, _) => SystemShell.OpenUrl(url);
        return link;
    }
}
