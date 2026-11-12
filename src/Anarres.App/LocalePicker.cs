using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// The client locale, chosen from the locale folders found in the WoW client's Data folder
public sealed class LocalePicker : StackPanel
{
    private readonly ComboBox _box = new ComboBox { Width = 120 };
    // Shown with All available: every locale's archives are read as well
    private readonly TextBlock _slower = new TextBlock
    {
        Text = "Importing every locale takes longer",
        // Lined up under the picker in a Ui.Row, so a long client path can't push it out of sight
        Margin = new Avalonia.Thickness(Ui.LABEL_WIDTH + Ui.GAP, 0, 0, 0),
        IsVisible = false
    };
    private readonly TextBlock _found = new TextBlock
    {
        Classes = { "hint" },
        VerticalAlignment = VerticalAlignment.Center
    };

    public const string ALL = "All available";

    // The red note for All available; the window puts it on its own line under the picker's row
    public Control Note => _slower;

    private List<string> _foundLocales = new List<string>();

    public LocalePicker(string locale, bool all = false)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        _box.Width = 150;
        Children.Add(_box);
        Children.Add(_found);
        Ui.Themed(_slower, TextBlock.ForegroundProperty, ThemeColours.ERROR);
        Fill(new List<string>(), locale, all);
    }

    // With All available, the first locale the client has (the chosen one when it's among them) is read first
    public string Locale => _box.SelectedItem as string is { } chosen && chosen != ALL
                                ? chosen
                                : _foundLocales.FirstOrDefault(found => found == _primary) ??
                                  _foundLocales.FirstOrDefault() ?? ProjectSettings.DEFAULT_LOCALE;

    public bool All => _box.SelectedItem as string == ALL;

    // The other locales All available brings in beside Locale
    public List<string> OtherLocales => All ? LocaleMerge.Order(_foundLocales).Where(found => found != Locale).ToList()
                                            : new List<string>();

    private string _primary = ProjectSettings.DEFAULT_LOCALE;

    public event EventHandler? Changed;

    // Looks in the client folder again; keeps the chosen locale when the client has it
    public void Detect(string clientFolder)
    {
        string current = Locale;
        bool all = All;
        List<string> found = ClientArchives.FindLocales(ProjectSettings.ResolveClientData(clientFolder.Trim()));
        Fill(found, found.Contains(current) || found.Count == 0 ? current : found[0], all);
    }

    private void Fill(List<string> found, string locale, bool all)
    {
        _foundLocales = found;
        _primary = locale;
        // Without a client to look at, every 3.3.5a locale is offered
        List<string> items = found.Count > 0 ? found.ToList() : SpellRecord.AllClientLocales.ToList();
        if (!items.Contains(locale) && locale.Length > 0)
            items.Add(locale);
        if (found.Count > 1)
            items.Insert(0, ALL);
        _box.SelectionChanged -= OnChanged;
        _box.ItemsSource = items;
        _box.SelectedItem = all && found.Count > 1 ? ALL : locale.Length > 0 ? locale : items[0];
        _box.SelectionChanged += OnChanged;
        _slower.IsVisible = All;
        _found.Text = found.Count switch
        {
            0 => "No locale folder found in the client; showing all",
            1 => "1 found in the client: " + found[0],
            _ => found.Count + " found in the client (hover for the list)"
        };
        // One line per locale, with its language
        ToolTip.SetTip(_found, found.Count == 0
                                   ? null
                                   : found.Count + " found:\n" +
                                     string.Join("\n", found.Select(entry => "- " + entry + "  " + Language(entry))));
        ToolTip.SetTip(_box, found.Count > 1
                                 ? "All available reads " + Locale + " first and fills each other language from " +
                                   "its own folder"
                                 : null);
    }

    private static readonly Dictionary<string, string> Languages = new Dictionary<string, string>
    {
        ["enUS"] = "English (US)", ["enGB"] = "English (UK)", ["koKR"] = "Korean", ["frFR"] = "French",
        ["deDE"] = "German", ["enCN"] = "English (China)", ["zhCN"] = "Chinese (simplified)",
        ["enTW"] = "English (Taiwan)", ["zhTW"] = "Chinese (traditional)", ["esES"] = "Spanish (Spain)",
        ["esMX"] = "Spanish (Mexico)", ["ruRU"] = "Russian", ["ptPT"] = "Portuguese", ["ptBR"] = "Portuguese (Brazil)",
        ["itIT"] = "Italian"
    };

    public static string Language(string locale)
    {
        return Languages.GetValueOrDefault(locale, string.Empty);
    }

    private void OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        _slower.IsVisible = All;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
