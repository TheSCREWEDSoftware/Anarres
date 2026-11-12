using System.Globalization;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Anarres.App;

// How Anarres looks and behaves; Save closes with the new options, Cancel with null
public sealed class OptionsWindow : Window
{
    public const string APPEARANCE = "Appearance";
    public const string INTEGRATIONS = "Integrations";
    public const string SPELLS = "Spell Editor";
    public const string TABLES = "DBC Editor";
    public const string GENERAL = "General";

    private const double WINDOW_WIDTH = 860;
    private const double WINDOW_HEIGHT = 640;
    private const double SIDEBAR_WIDTH = 170;

    private static readonly string[] Sections = { APPEARANCE, INTEGRATIONS, SPELLS, TABLES, GENERAL };
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
    private readonly ComboBox _font = new ComboBox { Width = 260 };
    private readonly ComboBox _themeSize = new ComboBox { Width = 200 };
    private readonly ComboBox _themeFont = new ComboBox { Width = 260 };
    private readonly TextBlock _themeTextTitle = new TextBlock { FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private Dictionary<string, ThemeText> _themeText = new Dictionary<string, ThemeText>();
    private List<string> _fonts = new List<string>();
    private string? _textTheme;
    private readonly Dictionary<string, ScrollViewer> _scrollers = new Dictionary<string, ScrollViewer>();
    private readonly ComboBox _gridFontSize = new ComboBox { Width = 120 };
    private readonly ComboBox _locale = new ComboBox { ItemsSource = SpellRecord.ClientLocales, Width = 120 };
    private readonly CheckBox _rank = new CheckBox { Content = "Show the rank beside each name in the spell list" };
    private readonly ComboBox _listLayout = new ComboBox
    {
        ItemsSource = new[] { "Normal: icons, and a card on hover", "Compact: one line per spell" },
        Width = 300
    };
    private readonly CheckBox _preview2 = new CheckBox { Content = "Show the tooltip and buff preview" };
    private readonly CheckBox _reopen = new CheckBox { Content = "Open the last spell again on start" };
    private readonly CheckBox _hideEmpty = new CheckBox
    {
        Content = "Hide columns that are zero or empty in every row"
    };
    private readonly CheckBox _hex = new CheckBox { Content = "Show Flags, Mask and Attributes columns in hex" };
    private readonly CheckBox _namesInCells = new CheckBox
    {
        Content = "Write names beside values in cells, such as 6 (Orc, Dwarf); hovering a cell always lists them"
    };
    private readonly ComboBox _start = new ComboBox { ItemsSource = StartNames, Width = 240 };
    private readonly CheckBox _confirm = new CheckBox { Content = "Ask before deleting spells and rows" };
    private static readonly string[] CloseNames =
    {
        "Exit Anarres completely", "Minimise Anarres to the system tray"
    };

    private readonly ComboBox _onClose = new ComboBox { ItemsSource = CloseNames, Width = 300 };
    private readonly CheckBox _confirmExit = new CheckBox { Content = "Ask before exiting" };
    private readonly CheckBox _showSetup = new CheckBox { Content = "Show the setup questions when Anarres starts" };
    private readonly CheckBox _confirmApply = new CheckBox { Content = "Show what Apply will do and ask first" };
    private readonly ComboBox _undo = new ComboBox { Width = 120 };
    private readonly CheckBox _window = new CheckBox { Content = "Remember the window's size and position" };
    private readonly ProgramPicker _editor = new ProgramPicker(ExternalTools.EditorNames);
    private readonly ProgramPicker _sqlTool = new ProgramPicker(ExternalTools.SqlToolNames);
    private readonly CheckBox _openSql = new CheckBox
    {
        Content = "Open .sql files in the text editor once they're saved"
    };
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
        _themeText = _options.ThemeText.ToDictionary(entry => entry.Key,
                                                     entry => new ThemeText
                                                     {
                                                         FontSize = entry.Value.FontSize,
                                                         FontFamily = entry.Value.FontFamily
                                                     }, StringComparer.OrdinalIgnoreCase);
        _fonts = SystemFonts();
        _font.ItemsSource = new[] { SYSTEM_FONT }.Concat(_fonts).ToList();
        _font.SelectedIndex = Math.Max(0, _fonts.IndexOf(_options.FontFamily) + 1);
        _themeSize.ItemsSource = new[] { SAME_FOR_ALL }.Concat(AppOptions.FontSizes.Select(Size)).ToList();
        _themeFont.ItemsSource = new[] { SAME_FOR_ALL }.Concat(_fonts).ToList();
        _gridFontSize.ItemsSource = AppOptions.GridFontSizes.Select(Size).ToList();
        _gridFontSize.SelectedIndex = Math.Max(0, Array.IndexOf(AppOptions.GridFontSizes, _options.GridFontSize));
        _locale.SelectedItem = SpellRecord.ClientLocales.Contains(_options.DisplayLocale) ? _options.DisplayLocale
                                                                                    : SpellRecord.ENGLISH;
        _rank.IsChecked = _options.ShowRankInList;
        _listLayout.SelectedIndex = (int)_options.ListLayout;
        _preview2.IsChecked = _options.ShowTooltipPreview;
        _reopen.IsChecked = _options.ReopenLastSpell;
        _hideEmpty.IsChecked = _options.HideEmptyColumns;
        _hex.IsChecked = _options.FlagsInHex;
        _namesInCells.IsChecked = _options.NamesInCells;
        _start.SelectedIndex = (int)_options.StartIn;
        _confirm.IsChecked = _options.ConfirmDeletes;
        _confirmApply.IsChecked = _options.ConfirmApply;
        _onClose.SelectedIndex = (int)_options.OnClose;
        _confirmExit.IsChecked = _options.ConfirmExit;
        _showSetup.IsChecked = _options.ShowSetup;
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
                              Ui.Row("Spell list", _listLayout), _rank, _preview2, _reopen);
        _pages[TABLES] = Page(Ui.Title("DBC Editor"),
                              Ui.Row("Grid text size", _gridFontSize),
                              _hideEmpty, _hex, _namesInCells,
                              Ui.Hint("Hex is only how the grid shows the numbers; type them either way."));
        _editor.Use(_options.TextEditor);
        _sqlTool.Use(_options.SqlTool);
        _openSql.IsChecked = _options.OpenSqlAfterSaving;
        _pages[INTEGRATIONS] = Page(Ui.Title("Text editor"),
                                    _editor,
                                    _openSql,
                                    Ui.Hint("For the .sql and .csv files Anarres writes. System default uses the " +
                                            "program Windows or your desktop opens them with."),
                                    Ui.Title("SQL tool"),
                                    _sqlTool,
                                    Ui.Hint("Tools > Open the project in the SQL tool opens the project's database, " +
                                            "signed in where the tool allows it: HeidiSQL, DBeaver and MySQL " +
                                            "Workbench for MySQL, DBeaver and DB Browser for SQLite for a project " +
                                            "file. In custom arguments, %TARGET_PATH% is the project file, and " +
                                            "%HOST%, %PORT%, %USER%, %PASSWORD% and %DATABASE% the MySQL connection."));
        _pages[GENERAL] = Page(Ui.Title("General"),
                               Ui.Row("Start in", _start),
                               _confirm,
                               _confirmApply,
                               Ui.Row("Undo steps per table", _undo),
                               _window,
                               Ui.Row("When clicking X", _onClose),
                               _confirmExit,
                               _showSetup,
                               Ui.Title("Loading"),
                               _cache,
                               Ui.Hint("A copy of each big table is kept as a .dbc file and used while the " +
                                       "project's table is unchanged; edits made anywhere, even in another " +
                                       "database tool, make Anarres read the database again."),
                               Ui.Horizontal(Ui.Button("Clear the copies", ClearCache), _cacheSize));

        _sections.ItemsSource = Sections;
        _sections.SelectionChanged += (_, _) =>
        {
            if (_sections.SelectedItem is not string name)
                return;
            // One scroll area per page, made once; a new one each time found the page still in the old one (blank)
            if (!_scrollers.TryGetValue(name, out ScrollViewer? scroller))
            {
                scroller = new ScrollViewer
                {
                    Content = _pages[name],
                    Padding = new Thickness(16, 0, 16, 0),
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                };
                _scrollers[name] = scroller;
            }

            _page.Content = scroller;
        };
        // A page that fails while it's laid out or drawn says why, instead of staying blank
        DispatcherUnhandledExceptionEventHandler failed = (_, e) => ShowFailure(e.Exception);
        Dispatcher.UIThread.UnhandledException += failed;
        Closed += (_, _) => Dispatcher.UIThread.UnhandledException -= failed;
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
        _colours.SelectionChanged += (_, _) =>
        {
            ShowPreview();
            ShowThemeText();
        };
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
        ToolTip.SetTip(edit, "Change the colours of the theme chosen under Colours");
        Button folder = Ui.Button("Open themes folder", () => SystemShell.Open(_themeStore.Folder));
        ShowPreview();
        ShowThemeText();
        return Page(Ui.Title("Theme"),
                    Ui.Row("Look", _look),
                    Ui.Row("Colours", _colours),
                    _preview,
                    Ui.Horizontal(create, edit, folder),
                    Ui.Hint("Colours offers the built-in colours, ready-made themes and your own. A theme sets " +
                            "every colour on top of a dark or light base, and its base wins over the look above. " +
                            "Edit changes the theme chosen under Colours, built-in and ready-made ones included; " +
                            "Restore defaults in the editor brings back their original colours. Themes are .json " +
                            "files in the themes folder, in the same format as Urras's, so they can be shared."),
                    Ui.Title("Text, every theme"),
                    Ui.Row("Text size", _fontSize),
                    Ui.Row("Font", _font),
                    _themeTextTitle,
                    Ui.Row("Text size", _themeSize),
                    Ui.Row("Font", _themeFont),
                    Ui.Horizontal(Ui.Button("Use this theme's text for every theme", UseThemeTextEverywhere)),
                    Ui.Hint("A theme can have its own text size and font; \"" + SAME_FOR_ALL + "\" follows " +
                            "the settings above. The button makes this theme's choice the one for every theme " +
                            "and clears the other themes' own choices. Fonts are the ones installed on this " +
                            "computer."));
    }

    private const string SYSTEM_FONT = "System default";
    private const string SAME_FOR_ALL = "Same as every theme";

    // Fonts installed on this computer, by name
    private static List<string> SystemFonts()
    {
        try
        {
            return Avalonia.Media.FontManager.Current.SystemFonts.Select(family => family.Name)
                           .Where(name => name.Length > 0)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .Order(StringComparer.OrdinalIgnoreCase)
                           .ToList();
        }
        catch (InvalidOperationException)
        {
            return new List<string>();
        }
    }

    private string ThemeKey => _colours.SelectedItem as string ?? AppOptions.BUILT_IN_COLOURS;

    // Keeps the shown theme's own text choices, then shows the newly chosen theme's
    private void ShowThemeText()
    {
        KeepThemeText();
        _textTheme = ThemeKey;
        _themeTextTitle.Text = "Text, this theme only: " + _textTheme;
        ThemeText? text = _themeText.GetValueOrDefault(_textTheme);
        _themeSize.SelectedIndex = text?.FontSize is { } size
                                       ? Math.Max(0, Array.IndexOf(AppOptions.FontSizes, size) + 1)
                                       : 0;
        _themeFont.SelectedIndex = !string.IsNullOrEmpty(text?.FontFamily)
                                       ? Math.Max(0, _fonts.IndexOf(text.FontFamily) + 1)
                                       : 0;
    }

    private void KeepThemeText()
    {
        if (_textTheme == null)
            return;
        _themeText[_textTheme] = new ThemeText
        {
            FontSize = _themeSize.SelectedIndex > 0 ? AppOptions.FontSizes[_themeSize.SelectedIndex - 1] : null,
            FontFamily = _themeFont.SelectedIndex > 0 ? _fonts[_themeFont.SelectedIndex - 1] : null
        };
    }

    private void UseThemeTextEverywhere()
    {
        if (_themeSize.SelectedIndex > 0)
            _fontSize.SelectedIndex = _themeSize.SelectedIndex - 1;
        if (_themeFont.SelectedIndex > 0)
            _font.SelectedIndex = _themeFont.SelectedIndex;
        _themeText.Clear();
        _textTheme = null;
        ShowThemeText();
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
        _listedThemes = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase);
        foreach (string preset in ThemePresets.Names)
            _listedThemes[preset] = ThemePresets.Find(preset)!;
        // A file named after a preset is your version of it; Dark and Light files are the built-in colours
        foreach (Theme theme in _themeStore.LoadAll())
        {
            if (theme.Name is not (Anarres.Core.Settings.Theme.DARK or Anarres.Core.Settings.Theme.LIGHT))
                _listedThemes[theme.Name] = theme;
        }
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
        _preview.Show(SelectedTheme() ?? _themeStore.BuiltIn(ChosenBase()));
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

    // Edits the chosen theme in place; built-in and ready-made ones are saved as your version of them
    private async Task EditThemeAsync()
    {
        string chosen = _colours.SelectedItem as string ?? AppOptions.BUILT_IN_COLOURS;
        Theme theme;
        string about;
        if (chosen == AppOptions.BUILT_IN_COLOURS)
        {
            theme = _themeStore.BuiltIn(ChosenBase()).Copy(ChosenBase() == ThemeBase.Light
                                                               ? Anarres.Core.Settings.Theme.LIGHT
                                                               : Anarres.Core.Settings.Theme.DARK);
            about = "the built-in " + theme.Name + " colours (Look above picks Dark or Light)";
        }
        else if (ThemePresets.IsPreset(chosen))
        {
            theme = (_themeStore.Find(chosen) ?? ThemePresets.Find(chosen)!).Copy(chosen);
            about = chosen + ", a ready-made theme";
        }
        else if (_themeStore.Find(chosen) is { } custom)
        {
            theme = custom;
            about = chosen + ", your theme";
        }
        else
        {
            return;
        }

        ThemeEditorWindow editor = new ThemeEditorWindow(theme, _themeStore, about);
        await editor.ShowDialog<bool>(this);
        ListColours(chosen);
        ShowPreview();
    }

    private void ShowFailure(Exception exception)
    {
        string? log = CrashLog.Write(exception, "Options page failed");
        _page.Content = new SelectableTextBlock
        {
            Text = "This page couldn't be shown: " + exception.Message +
                   (log != null ? "\n\nThe details are in " + log + "." : string.Empty),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(16, 0)
        };
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
        options.TextEditor = _editor.Choice();
        options.SqlTool = _sqlTool.Choice();
        options.OpenSqlAfterSaving = _openSql.IsChecked == true;
        options.Look = (ThemeLook)Math.Max(0, _look.SelectedIndex);
        options.Colours = _colours.SelectedItem as string ?? AppOptions.BUILT_IN_COLOURS;
        options.FontSize = AppOptions.FontSizes[Math.Max(0, _fontSize.SelectedIndex)];
        options.FontFamily = _font.SelectedIndex > 0 ? _fonts[_font.SelectedIndex - 1] : string.Empty;
        KeepThemeText();
        options.ThemeText = _themeText.Where(entry => entry.Value.FontSize != null ||
                                                      !string.IsNullOrEmpty(entry.Value.FontFamily))
                                      .ToDictionary(entry => entry.Key, entry => entry.Value,
                                                    StringComparer.OrdinalIgnoreCase);
        options.GridFontSize = AppOptions.GridFontSizes[Math.Max(0, _gridFontSize.SelectedIndex)];
        options.DisplayLocale = _locale.SelectedItem as string ?? SpellRecord.ENGLISH;
        options.ShowRankInList = _rank.IsChecked == true;
        options.ListLayout = (SpellListLayout)Math.Max(0, _listLayout.SelectedIndex);
        options.ShowTooltipPreview = _preview2.IsChecked == true;
        options.ReopenLastSpell = _reopen.IsChecked == true;
        options.HideEmptyColumns = _hideEmpty.IsChecked == true;
        options.FlagsInHex = _hex.IsChecked == true;
        options.NamesInCells = _namesInCells.IsChecked == true;
        options.StartIn = (StartEditor)Math.Max(0, _start.SelectedIndex);
        options.ConfirmDeletes = _confirm.IsChecked == true;
        options.ConfirmApply = _confirmApply.IsChecked == true;
        options.OnClose = (CloseAction)Math.Max(0, _onClose.SelectedIndex);
        options.ConfirmExit = _confirmExit.IsChecked == true;
        options.ShowSetup = _showSetup.IsChecked == true;
        options.MaxUndoSteps = AppOptions.UndoSteps[Math.Max(0, _undo.SelectedIndex)];
        options.RememberWindow = _window.IsChecked == true;
        options.KeepTableCache = _cache.IsChecked == true;
        return options;
    }
}
