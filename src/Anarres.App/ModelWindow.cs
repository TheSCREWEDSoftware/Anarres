using Anarres.Core.Models;
using Anarres.Core.Mpq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// Tools > Model viewer: any client .m2, or a display's or spell visual's model by ID; also the DBC preview
public sealed class ModelWindow : Window
{
    private const int MAX_LISTED = 2000;

    private static readonly string[] Kinds =
    {
        "Client files", "Creature display ID", "Spell visual ID", "Item display ID", "Game object display ID"
    };

    private readonly Session _session;
    private readonly ComboBox _kind = new ComboBox { ItemsSource = Kinds, SelectedIndex = 0, Width = 190 };
    private readonly TextBox _search = new TextBox { Watermark = "Part of a path, such as fireball or wolf" };
    private readonly ListBox _files = new ListBox();
    private readonly TextBlock _count = new TextBlock { Classes = { "hint" } };
    private readonly ModelView _view;
    private List<string> _all = new List<string>();
    private IconLibrary? _client;

    public ModelWindow(Session session, List<ModelChoice>? show = null, string? title = null)
    {
        _session = session;
        Title = title ?? "Model viewer";
        Width = 1180;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _view = new ModelView(name => _client?.ReadFile(name), 560);

        Grid layout = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*"), Margin = new Thickness(12) };
        DockPanel left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        StackPanel top = Ui.Vertical(6, _kind, Ui.Clearable(_search), _count);
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);
        left.Children.Add(_files);
        layout.Children.Add(left);
        ScrollViewer right = new ScrollViewer { Content = _view.View };
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);
        Content = layout;

        _kind.SelectionChanged += async (_, _) => await SearchAsync();
        _search.TextChanged += async (_, _) => await SearchAsync();
        _files.SelectionChanged += (_, _) =>
        {
            if (_kind.SelectedIndex == 0 && _files.SelectedItem is string path)
                _view.Show(new List<ModelChoice> { new ModelChoice(Path.GetFileName(path), path,
                                                                   ModelChoice.NoTextures) }, string.Empty);
        };
        Opened += async (_, _) =>
        {
            _client = await _session.IconLibraryAsync();
            if (!_client.IsAvailable)
            {
                _count.Text = "Set the WoW client folder in the project settings to see models.";
                return;
            }

            _count.Text = "Listing the client's models...";
            _all = await Task.Run(() => _client.ListFiles(".m2"));
            if (show != null)
            {
                left.IsVisible = false;
                layout.ColumnDefinitions = new ColumnDefinitions("0,*");
                _view.Show(show, "Nothing to show.");
            }
            else
            {
                await SearchAsync();
            }
        };
    }

    private async Task SearchAsync()
    {
        string text = _search.Text?.Trim() ?? string.Empty;
        if (_kind.SelectedIndex == 0)
        {
            _search.Watermark = "Part of a path, such as fireball or wolf";
            List<string> matches = _all.Where(path => path.Contains(text, StringComparison.OrdinalIgnoreCase))
                                       .ToList();
            _files.IsVisible = true;
            _files.ItemsSource = matches.Take(MAX_LISTED).ToList();
            _count.Text = matches.Count > MAX_LISTED
                              ? matches.Count.ToString("N0") + " models; the first " + MAX_LISTED.ToString("N0") +
                                " shown. Type more to narrow them down."
                              : matches.Count.ToString("N0") + " models.";
            return;
        }

        _search.Watermark = "ID";
        _files.IsVisible = false;
        if (!uint.TryParse(text, out uint id))
        {
            _count.Text = "Type an ID.";
            return;
        }

        ModelSources sources = await _session.ModelSourcesAsync();
        List<ModelChoice> found = _kind.SelectedIndex switch
        {
            1 => sources.ForCreatureDisplay(id) is { } creature ? new List<ModelChoice> { creature } : new(),
            2 => sources.ForSpellVisual(id),
            3 => sources.ForItemDisplay(id) is { } item ? new List<ModelChoice> { item } : new(),
            _ => sources.ForGameObjectDisplay(id) is { } gameObject ? new List<ModelChoice> { gameObject } : new()
        };
        _count.Text = found.Count == 0 ? "No model for " + Kinds[_kind.SelectedIndex].ToLowerInvariant() + " " + id +
                                         "." : found.Count + " model" + (found.Count == 1 ? "." : "s.");
        _view.Show(found, "No model found.");
    }
}
