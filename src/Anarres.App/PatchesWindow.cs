using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// The client's archives in load order, what .dbc files each has and which it wins, and whether ours is hidden
public sealed class PatchesWindow : Window
{
    private readonly ProjectSettings _settings;
    private readonly StackPanel _list = new StackPanel { Spacing = 6 };
    private readonly TextBlock _summary = new TextBlock { TextWrapping = TextWrapping.Wrap };

    public PatchesWindow(ProjectSettings settings)
    {
        _settings = settings;
        Title = "Client patches";
        Width = 900;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        close.Click += (_, _) => Close();
        StackPanel buttons = Ui.Horizontal(close);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10);
        _summary.Margin = new Thickness(20, 16, 20, 8);
        DockPanel root = new DockPanel();
        DockPanel.SetDock(_summary, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(_summary);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = _list, Padding = new Thickness(20, 0) });
        Content = root;
        Opened += async (_, _) => await FillAsync();
    }

    private async Task FillAsync()
    {
        string data = _settings.ClientData();
        if (data.Length == 0 || !Directory.Exists(data))
        {
            _summary.Text = "Set the WoW client folder in Project settings to see its patches.";
            return;
        }

        _summary.Text = "Reading the client's archives...";
        List<PatchArchive> archives;
        try
        {
            archives = await Task.Run(() => ClientPatches.List(data, _settings.Locale, _settings.PatchPath()));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            _summary.Text = "The client's archives couldn't be read: " + exception.Message;
            return;
        }

        List<string> hidden = ClientPatches.Hidden(archives);
        bool hasOurs = archives.Any(archive => archive.IsOurs);
        _summary.Text = "The client reads these in order, top to bottom; for each .dbc file, the last archive " +
                        "with it wins.\n" +
                        (!hasOurs ? "Your patch (" + Path.GetFileName(_settings.PatchPath()) + ") isn't there yet; " +
                                    "Apply builds it."
                         : hidden.Count == 0 ? "Nothing hides your patch."
                         : "A later archive hides " + hidden.Count + " of your patch's files: " +
                           string.Join(", ", hidden.Take(10)) + ". Pick a later patch name in Project settings.");
        _list.Children.Clear();
        foreach (PatchArchive archive in archives)
        {
            string title = Path.GetRelativePath(data, archive.Path) +
                           (archive.IsOurs ? "   (your patch)" : string.Empty);
            string files = archive.DbcFiles.Count == 0
                               ? "No .dbc files."
                               : archive.DbcFiles.Count + " .dbc files, " + archive.Wins.Count + " used from here" +
                                 (archive.Wins.Count > 0 ? ": " + string.Join(", ", archive.Wins.Take(12)) +
                                                           (archive.Wins.Count > 12 ? "..." : string.Empty)
                                                         : string.Empty);
            TextBlock heading = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold };
            if (archive.IsOurs)
                Ui.Themed(heading, TextBlock.ForegroundProperty, Anarres.Core.Settings.ThemeColours.ACCENT);
            _list.Children.Add(Ui.Vertical(2, heading,
                                           new TextBlock { Text = files, Classes = { "hint" },
                                                           TextWrapping = TextWrapping.Wrap }));
        }
    }
}
