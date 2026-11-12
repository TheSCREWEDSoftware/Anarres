using System.Globalization;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// How Anarres looks and behaves; Save closes with the new options, Cancel with null
public sealed class OptionsWindow : Window
{
    public const string APPEARANCE = "Appearance";
    public const string SPELLS = "Spell Editor";
    public const string TABLES = "DBC Editor";
    public const string GENERAL = "General";

    private const double WINDOW_WIDTH = 860;
    private const double WINDOW_HEIGHT = 640;
    private const double SIDEBAR_WIDTH = 170;

    private static readonly string[] Sections = { APPEARANCE, SPELLS, TABLES, GENERAL };
    private static readonly string[] LookNames = { "System", "Dark", "Light" };
    private static readonly string[] StartNames = { "The editor used last", "Spell Editor", "DBC Editor" };

    private readonly AppOptions _options;
    private readonly ThemeStore _themeStore = ThemeStore.Default;
    private readonly ListBox _sections = new ListBox { Width = SIDEBAR_WIDTH };
    private readonly ContentControl _page = new ContentControl();
    private readonly Dictionary<string, Control> _pages = new Dictionary<string, Control>();

    private readonly ComboBox _look = new ComboBox { ItemsSource = LookNames, Width = 180 };
    private readonly ComboBox _colours = new ComboBox { Width = 380 };
    private readonly ThemePreview _preview = new ThemePreview { Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox _fontSize = new ComboBox { Width = 120 };
    private readonly ComboBox _gridFontSize = new ComboBox { Width = 120 };
    private readonly ComboBox _locale = new ComboBox { ItemsSource = SpellRecord.Locales, Width = 120 };
    private readonly CheckBox _rank = new CheckBox { Content = "Show the rank beside each name in the spell list" };
    private readonly CheckBox _preview2 = new CheckBox { Content = "Show the tooltip and buff preview" };
    private readonly CheckBox _reopen = new CheckBox { Content = "Open the last spell again on start" };
    private readonly CheckBox _hideEmpty = new CheckBox
    {
        Content = "Hide columns that are zero or empty in every row"
    };
    private readonly CheckBox _hex = new CheckBox { Content = "Show Flags, Mask and Attributes columns in hex" };
    private readonly ComboBox _start = new ComboBox { ItemsSource = StartNames, Width = 240 };
    private readonly CheckBox _confirm = new CheckBox { Content = "Ask before deleting spells and rows" };
    private readonly CheckBox _confirmApply = new CheckBox { Content = "Show what Apply will do and ask first" };
    private readonly ComboBox _undo = new ComboBox { Width = 120 };
    private readonly CheckBox _window = new CheckBox { Content = "Remember the window's size and position" };
    private readonly CheckBox _cache = new CheckBox
    {
        Content = "Keep big tables such as Spell.dbc on this PC so they open quicker"
    };
    private readonly TextBlock _cacheSize = new TextBlock
    {
        Classes = { "hint" },
        VerticalAlignment = VerticalAlignment.Center
    };
    private Dictionary<string, Theme> _listedThemes = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase);

    public OptionsWindow(AppOptions options, string section = APPEARANCE)
    {
        _options = options.Copy();
        Title = "Options";
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _look.SelectedIndex = (int)_options.Look;
        _fontSize.ItemsSource = AppOptions.FontSizes.Select(Size).ToList();
        _fontSize.SelectedIndex = Math.Max(0, Array.IndexOf(AppOptions.FontSizes, _options.FontSize));
        _gridFontSize.ItemsSource = AppOptions.GridFontSizes.Select(Size).ToList();
        _gridFontSize.SelectedIndex = Math.Max(0, Array.IndexOf(AppOptions.GridFontSizes, _options.GridFontSize));
        _locale.SelectedItem = SpellRecord.Locales.Contains(_options.DisplayLocale) ? _options.DisplayLocale
                                                                                    : SpellRecord.ENGLISH;
        _rank.IsChecked = _options.ShowRankInList;
        _preview2.IsChecked = _options.ShowTooltipPreview;
        _reopen.IsChecked = _options.ReopenLastSpell;
        _hideEmpty.IsChecked = _options.HideEmptyColumns;
        _hex.IsChecked = _options.FlagsInHex;
        _start.SelectedIndex = (int)_options.StartIn;
        _confirm.IsChecked = _options.ConfirmDeletes;
        _confirmApply.IsChecked = _options.ConfirmApply;
        _undo.ItemsSource = AppOptions.UndoSteps.Select(steps => steps.ToString("N0", CultureInfo.InvariantCulture))
                                      .ToList();
        _undo.SelectedIndex = Math.Max(0, Array.IndexOf(AppOptions.UndoSteps, _options.MaxUndoSteps));
        _window.IsChecked = _options.RememberWindow;
        _cache.IsChecked = _options.KeepTableCache;
        ShowCacheSize();

        _pages[APPEARANCE] = AppearancePage();
        _pages[SPELLS] = Page(Ui.Title("Spell Editor"),
                              Ui.Row("Language", _locale),
                              Ui.Hint("Names, ranks, the heading and the tooltip preview use this language's " +
                                      "columns. A spell without text in it falls back to enUS."),
                              _rank, _preview2, _reopen);
        _pages[TABLES] = Page(Ui.Title("DBC Editor"),
                              Ui.Row("Grid text size", _gridFontSize),
                              _hideEmpty, _hex,
                              Ui.Hint("Hex is only how the grid shows the numbers; type them either way."));
        _pages[GENERAL] = Page(Ui.Title("General"),
                               Ui.Row("Start in", _start),
                               _confirm,
                               _confirmApply,
                               Ui.Row("Undo steps per table", _undo),
                               _window,
                               Ui.Title("Loading"),
                               _cache,
                               Ui.Hint("A copy of each big table is kept as a .dbc file and used while the " +
                                       "project's table is unchanged; edits made anywhere, even in another " +
                                       "database tool, make Anarres read the database again."),
                               Ui.Horizontal(Ui.Button("Clear the copies", ClearCache), _cacheSize));

        _sections.ItemsSource = Sections;
        _sections.SelectionChanged += (_, _) =>
        {
            if (_sections.SelectedItem is string name)
                _page.Content = new ScrollViewer
                {
                    Content = _pages[name],
                    Padding = new Thickness(16, 0, 16, 0),
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                };
        };
        _sections.SelectedItem = Sections.Contains(section) ? section : APPEARANCE;

        Button save = new Button { Content = "Save", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => Close(Build());
        cancel.Click += (_, _) => Close(null);
        StackPanel buttons = Ui.Horizontal(save, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(16, 10);

        Grid body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(16, 16, 0, 0)
        };
        body.Children.Add(_sections);
        Grid.SetColumn(_page, 1);
        body.Children.Add(_page);
        DockPanel root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        Content = root;
    }

    private Control AppearancePage()
    {
        ListColours(_options.Colours);
        _look.SelectionChanged += (_, _) => ShowPreview();
        _colours.SelectionChanged += (_, _) => ShowPreview();
        _colours.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            if (name != null && name != AppOptions.BUILT_IN_COLOURS &&
                _listedThemes.TryGetValue(name, out Theme? theme))
                row.Children.Add(ThemePreview.Swatches(theme));
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            return row;
        });

        Button create = Ui.AsyncButton("New theme...", NewThemeAsync);
        Button edit = Ui.AsyncButton("Edit theme...", EditThemeAsync);
        Button folder = Ui.Button("Open themes folder", () => SystemShell.Open(_themeStore.Folder));
        ShowPreview();
        return Page(Ui.Title("Theme"),
                    Ui.Row("Look", _look),
                    Ui.Row("Colours", _colours),
                    _preview,
                    Ui.Horizontal(create, edit, folder),
                    Ui.Hint("Colours offers ready-made themes and your own. A theme sets every colour on top of a " +
                            "dark or light base, and its base wins over the look above. Editing a ready-made " +
                            "theme makes a copy. Themes are .json files in the themes folder, in the same format " +
                            "as Urras's, so they can be shared between the two."),
                    Ui.Title("Text"),
                    Ui.Row("Text size", _fontSize));
    }

    private static StackPanel Page(params Control[] controls)
    {
        return Ui.Vertical(8, controls);
    }

    private static string Size(double size)
    {
        return size.ToString(CultureInfo.InvariantCulture) + " pt";
    }

    // Built-in colours first, then the presets, then each theme file, with the given one chosen
    private void ListColours(string? chosen)
    {
        List<Theme> listed = ThemePresets.Names.Select(name => ThemePresets.Find(name)!)
                                     .Concat(_themeStore.LoadAll()).ToList();
        _listedThemes = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase);
        foreach (Theme theme in listed)
            _listedThemes.TryAdd(theme.Name, theme);
        List<string> names = new List<string> { AppOptions.BUILT_IN_COLOURS };
        names.AddRange(_listedThemes.Keys);
        _colours.ItemsSource = names;
        _colours.SelectedItem = names.FirstOrDefault(name => string.Equals(name, chosen,
                                                                           StringComparison.OrdinalIgnoreCase)) ??
                                AppOptions.BUILT_IN_COLOURS;
    }

    private ThemeBase ChosenBase()
    {
        return _look.SelectedIndex == (int)ThemeLook.Light ? ThemeBase.Light : ThemeBase.Dark;
    }

    private Theme? SelectedTheme()
    {
        return _colours.SelectedItem is string name && _listedThemes.TryGetValue(name, out Theme? theme)
                   ? theme
                   : null;
    }

    private void ShowPreview()
    {
        _preview.Show(SelectedTheme() ?? Anarres.Core.Settings.Theme.BuiltIn(ChosenBase()));
    }

    // A new theme starts as a copy of the chosen colours
    private async Task NewThemeAsync()
    {
        string? name = (await Dialogs.PromptAsync(this, "New theme", "Name of the theme:"))?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        bool builtIn = name is Anarres.Core.Settings.Theme.DARK or Anarres.Core.Settings.Theme.LIGHT ||
                       name == AppOptions.BUILT_IN_COLOURS;
        if (builtIn || ThemePresets.IsPreset(name) ||
            _themeStore.Find(name) != null)
        {
            await Dialogs.MessageAsync(this, "New theme", "There's already a theme called " + name + ".");
            return;
        }

        Theme theme = (SelectedTheme() ?? Anarres.Core.Settings.Theme.BuiltIn(ChosenBase())).Copy(name);
        try
        {
            _themeStore.Save(theme);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(this, "New theme", "The theme couldn't be saved: " + exception.Message);
            return;
        }

        ListColours(name);
        await EditThemeAsync();
    }

    private async Task EditThemeAsync()
    {
        if (_colours.SelectedItem is not string name || name == AppOptions.BUILT_IN_COLOURS)
        {
            await Dialogs.MessageAsync(this, "Edit theme", "Choose a theme under Colours first, or make a new one.");
            return;
        }

        // A ready-made theme stays as it is; editing it makes a copy to change
        if (ThemePresets.Find(name) is { } preset)
        {
            string copyName = name + " (copy)";
            for (int number = 2; _themeStore.Find(copyName) != null; number++)
                copyName = name + " (copy " + number + ")";
            try
            {
                _themeStore.Save(preset.Copy(copyName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await Dialogs.MessageAsync(this, "Edit theme", "The copy couldn't be saved: " + exception.Message);
                return;
            }

            name = copyName;
        }

        if (_themeStore.Find(name) is not { } theme)
            return;
        if (await new ThemeEditorWindow(theme, _themeStore).ShowDialog<bool>(this))
            ListColours(name);
        else
            ListColours(_colours.SelectedItem as string);
    }

    private void ShowCacheSize()
    {
        long bytes = new DbcCache(DbcCache.DefaultFolder).SizeBytes();
        _cacheSize.Text = (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB in " +
                          DbcCache.DefaultFolder;
    }

    private void ClearCache()
    {
        try
        {
            new DbcCache(DbcCache.DefaultFolder).Clear();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _cacheSize.Text = "Some copies are in use and stayed: " + exception.Message;
            return;
        }

        ShowCacheSize();
    }

    private AppOptions Build()
    {
        AppOptions options = _options.Copy();
        options.Look = (ThemeLook)Math.Max(0, _look.SelectedIndex);
        options.Colours = _colours.SelectedItem as string ?? AppOptions.BUILT_IN_COLOURS;
        options.FontSize = AppOptions.FontSizes[Math.Max(0, _fontSize.SelectedIndex)];
        options.GridFontSize = AppOptions.GridFontSizes[Math.Max(0, _gridFontSize.SelectedIndex)];
        options.DisplayLocale = _locale.SelectedItem as string ?? SpellRecord.ENGLISH;
        options.ShowRankInList = _rank.IsChecked == true;
        options.ShowTooltipPreview = _preview2.IsChecked == true;
        options.ReopenLastSpell = _reopen.IsChecked == true;
        options.HideEmptyColumns = _hideEmpty.IsChecked == true;
        options.FlagsInHex = _hex.IsChecked == true;
        options.StartIn = (StartEditor)Math.Max(0, _start.SelectedIndex);
        options.ConfirmDeletes = _confirm.IsChecked == true;
        options.ConfirmApply = _confirmApply.IsChecked == true;
        options.MaxUndoSteps = AppOptions.UndoSteps[Math.Max(0, _undo.SelectedIndex)];
        options.RememberWindow = _window.IsChecked == true;
        options.KeepTableCache = _cache.IsChecked == true;
        return options;
    }
}
