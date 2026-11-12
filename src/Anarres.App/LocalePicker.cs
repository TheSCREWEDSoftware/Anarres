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
    private readonly TextBlock _found = new TextBlock
    {
        Classes = { "hint" },
        VerticalAlignment = VerticalAlignment.Center
    };

    public LocalePicker(string locale)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        Children.Add(_box);
        Children.Add(_found);
        Fill(new List<string>(), locale);
    }

    public string Locale => _box.SelectedItem as string ?? ProjectSettings.DEFAULT_LOCALE;

    public event EventHandler? Changed;

    // Looks in the client folder again; keeps the chosen locale when the client has it
    public void Detect(string clientFolder)
    {
        string current = Locale;
        List<string> found = ClientArchives.FindLocales(ProjectSettings.ResolveClientData(clientFolder.Trim()));
        Fill(found, found.Contains(current) || found.Count == 0 ? current : found[0]);
    }

    private void Fill(List<string> found, string locale)
    {
        // Without a client to look at, every 3.3.5a locale is offered
        List<string> items = found.Count > 0 ? found : SpellRecord.AllClientLocales.ToList();
        if (!items.Contains(locale) && locale.Length > 0)
            items.Add(locale);
        _box.SelectionChanged -= OnChanged;
        _box.ItemsSource = items;
        _box.SelectedItem = locale.Length > 0 ? locale : items[0];
        _box.SelectionChanged += OnChanged;
        _found.Text = found.Count switch
        {
            0 => "No locale folder found in the client; showing all",
            1 => "Found in the client",
            _ => found.Count + " found in the client"
        };
    }

    private void OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
