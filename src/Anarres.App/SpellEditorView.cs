using System.Globalization;
using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Anarres.App;

// The spell editor: a searchable list of spells on the left, the chosen spell's fields in tabs on the right
public sealed class SpellEditorView : Grid
{
    private const double LIST_WIDTH = 330;
    private const int SEARCH_DELAY_MS = 250;
    private const double MILLISECONDS_PER_SECOND = 1000.0;
    private const int RANGE_MAX_COLUMN = 3;
    private const int RANGE_NAME_COLUMN = 6;

    private readonly MainWindow _window;
    private readonly Session _session;
    private readonly RecordBinding _binding = new RecordBinding();
    private readonly SpellLookups _lookups = new SpellLookups();
    private readonly TextBox _search = Ui.Clearable(new TextBox
    {
        Watermark = "Search by ID or name, or effect:6, aura:3"
    });
    private readonly ListBox _list = new ListBox();
    private readonly FieldFinder _finder;
    private readonly TextBlock _count = new TextBlock { Classes = { "hint" }, Margin = new Thickness(4, 4, 0, 0) };
    private readonly TextBlock _title = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _subtitle = new TextBlock { Classes = { "label" } };
    // The school in its combat text colour, as on the list's strip, and how many talents change the spell
    private readonly Border _schoolBadge = new Border
    {
        CornerRadius = new CornerRadius(BADGE_RADIUS),
        Padding = new Thickness(8, 1),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold }
    };
    private readonly Button _talentBadge = new Button
    {
        CornerRadius = new CornerRadius(BADGE_RADIUS),
        Padding = new Thickness(8, 1),
        FontSize = 12,
        MinHeight = 0,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false
    };
    private const double BADGE_RADIUS = 9;
    private const byte BADGE_FILL_ALPHA = 0x33;
    private readonly TextBlock _unsaved = new TextBlock { Text = "Unsaved changes", IsVisible = false,
                                                          VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _editor = new ContentControl();
    private readonly SelectableTextBlock _preview = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock _auraPreview = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _searchTimer;
    private readonly Image _headerIcon = new Image { Width = 48, Height = 48 };
    // A debuff-style border in the dispel type's colour (Magic blue, Curse purple, Disease brown, Poison green)
    private readonly Border _iconFrame = new Border
    {
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(4),
        Margin = new Thickness(0, 0, 12, 0),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Image _lookIcon = new Image { Width = 32, Height = 32 };
    private readonly SpellVisualView _visual;
    private DbcTable? _spells;
    private TabControl? _tabs;
    private IconImages? _images;
    private Grid? _previewBox;
    private ColumnDefinition? _previewColumn;
    private string _locale = SpellRecord.ENGLISH;
    // The Localisation tab's red previews of the client's own text, and that text once read
    private const double PREVIEW_INSET = 11;
    private readonly CheckBox _clientPreview = new CheckBox { Content = "Preview the client's text", IsChecked = true };
    private readonly List<(TextBox Box, TextBlock Preview, string Field, int Slot)> _localeBoxes =
        new List<(TextBox, TextBlock, string, int)>();
    private List<LocaleTexts>? _clientTexts;
    private string? _clientTextsFolder;
    private bool _loadingClientTexts;
    private bool _showRank = true;
    private SpellListLayout _layout = SpellListLayout.Normal;
    private bool _showPreview = true;
    private bool _confirmDeletes = true;
    // The spell to show when Spell.dbc first loads, from the last run
    private uint? _reopen;

    public SpellEditorView(MainWindow window, Session session)
    {
        _iconFrame.Child = _headerIcon;
        _window = window;
        _session = session;
        _finder = new FieldFinder(() => _tabs);
        ColumnDefinitions = new ColumnDefinitions("Auto,8,*");
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SEARCH_DELAY_MS) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            Filter();
        };
        _search.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _list.SelectionChanged += (_, _) => ShowSelected();
        _visual = new SpellVisualView(session, () => session.NotifyChanged());
        _binding.Edited += column =>
        {
            Edited();
            if (SpellVisualView.IsVisualColumn(column))
                _visual.Show(_binding.Row);
            if (column is "SpellIconID")
                UpdateHeading();
        };
        Ui.Themed(_unsaved, TextBlock.ForegroundProperty, ThemeColours.WARNING);

        Children.Add(BuildListPanel());
        Border editor = Ui.Card(BuildEditorPanel());
        SetColumn(editor, 2);
        Children.Add(editor);
        ShowEmpty("Open a project and import Spell.dbc to start. Use Import in the toolbar.");
    }

    public DbcTable? Spells => _spells;

    public uint? CurrentSpellId => (_list.SelectedItem as SpellItem)?.Id;

    public void FocusSearch()
    {
        _search.Focus();
        _search.SelectAll();
    }

    // Ctrl+F: the field finder while a spell is open and the list isn't in use; pressed again, the spell search
    public void FocusFind()
    {
        bool inList = _search.IsKeyboardFocusWithin || _list.IsKeyboardFocusWithin;
        if (!_binding.IsBound || inList || _finder.Box.IsKeyboardFocusWithin)
        {
            FocusSearch();
            return;
        }

        _finder.Box.Focus();
    }

    internal FieldFinder Finder => _finder;

    // False when loading failed and the editor shows the error instead of the tabs
    internal bool ShowsTabs => _editor.Content is TabControl;

    public void UseOptions(AppOptions options)
    {
        bool listChanged = _locale != options.DisplayLocale || _showRank != options.ShowRankInList ||
                           _layout != options.ListLayout;
        if (_layout != options.ListLayout)
        {
            _layout = options.ListLayout;
            _list.ItemTemplate = ListTemplate();
        }

        _locale = options.DisplayLocale;
        _showRank = options.ShowRankInList;
        _showPreview = options.ShowTooltipPreview;
        _confirmDeletes = options.ConfirmDeletes;
        if (_spells == null)
            _reopen = options.ReopenLastSpell && options.LastSpellId > 0 ? options.LastSpellId : null;
        ShowPreviewColumn();
        if (listChanged && _spells != null)
            Filter();
        UpdateHeading();
        UpdatePreview();
    }

    public async Task LoadAsync()
    {
        _spells = null;
        _binding.Bind(null, -1);
        if (!_session.IsOpen && !await _window.EnsureStoreAsync())
            return;
        try
        {
            IDbcStore store = await _session.StoreAsync();
            if (!await store.ContainsAsync(SpellRecord.FILE))
            {
                ShowEmpty("The project has no Spell.dbc yet. Use Import to bring it in from a DBC folder or the " +
                          "client's MPQs.");
                _list.ItemsSource = null;
                return;
            }

            _window.SetStatus("Loading Spell.dbc...");
            // Spells is set only once the editor is ready, so nothing sees a half-loaded spell editor
            DbcTable spells = await _session.LoadAsync(SpellRecord.FILE);
            _lookups.Spells = spells;
            await _session.LoadNamingTablesAsync();
            _ = ShowWorldNamesAsync();
            _lookups.Durations = await _session.TryLoadAsync("SpellDuration.dbc");
            _lookups.Radii = await _session.TryLoadAsync("SpellRadius.dbc");
            _lookups.Ranges = await _session.TryLoadAsync("SpellRange.dbc");
            _lookups.Variables = await _session.TryLoadAsync("SpellDescriptionVariables.dbc");
            CastTimes = await _session.TryLoadAsync("SpellCastTimes.dbc");
            Icons = await _session.TryLoadAsync("SpellIcon.dbc");
            _images = new IconImages(await _session.IconLibraryAsync(), Icons);
            await _visual.LoadAsync(spells);
            _spells = spells;
            BuildEditor(_spells.Definition);
            Filter();
            if (_reopen is { } last && _spells.FindRow(last) >= 0)
                Select(last);
            _reopen = null;
            if (_list.SelectedIndex < 0 && _list.ItemCount > 0)
                _list.SelectedIndex = 0;
            _window.SetStatus("Loaded " + _spells.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) +
                              " spells.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              System.Data.Common.DbException)
        {
            ShowEmpty("Spell.dbc couldn't be loaded: " + exception.Message);
            _window.SetStatus(exception.Message);
        }
    }

    private DbcTable? CastTimes { get; set; }

    private DbcTable? Icons { get; set; }

    // Selects a spell in the list; the search is cleared first when it hides the spell
    public void Select(uint id)
    {
        if (_spells == null)
            return;
        int index = IndexInList(id);
        if (index < 0)
        {
            // A new spell isn't listed yet, and the search may hide an existing one
            if ((_search.Text ?? string.Empty).Length > 0)
            {
                _search.Text = string.Empty;
                _searchTimer.Stop();
            }

            Filter();
            index = IndexInList(id);
        }

        if (index < 0)
            return;
        _list.SelectedIndex = index;
        _list.ScrollIntoView(index);
    }

    private int IndexInList(uint id)
    {
        return _list.ItemsSource is List<SpellItem> items ? items.FindIndex(item => item.Id == id) : -1;
    }

    private Control BuildListPanel()
    {
        Button add = Ui.Button("New", NewSpell);
        Button copy = Ui.Button("Copy", CopySpell);
        Button remove = Ui.AsyncButton("Delete", DeleteSpellAsync);
        ToolTip.SetTip(add, "A new empty spell with the next free ID");
        ToolTip.SetTip(copy, "A copy of the selected spell with the next free ID");
        StackPanel buttons = Ui.Horizontal(add, copy, remove);
        buttons.Margin = new Thickness(0, 8, 0, 0);

        _list.ItemTemplate = ListTemplate();

        DockPanel panel = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_count, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(_search);
        panel.Children.Add(_count);
        panel.Children.Add(buttons);
        panel.Children.Add(_list);
        return new Border { Classes = { "card" }, Width = LIST_WIDTH, Child = panel };
    }

    private Control BuildEditorPanel()
    {
        Button save = Ui.AsyncButton("Save", SaveAsync, accent: true);
        Button revert = Ui.AsyncButton("Revert", RevertAsync);
        Button sql = new Button { Content = "Save as SQL..." };
        sql.Flyout = new MenuFlyout
        {
            Items =
            {
                Item("This spell", () => ExportSqlAsync(changedOnly: false)),
                Item("Every spell changed from the clean Spell.dbc", () => ExportSqlAsync(changedOnly: true))
            }
        };
        Button world = new Button { Content = "Update spell__dbc..." };
        world.Flyout = new MenuFlyout
        {
            Items =
            {
                Item("This spell", () => SendToWorldAsync(changedOnly: false)),
                Item("Every spell changed from the clean Spell.dbc", () => SendToWorldAsync(changedOnly: true))
            }
        };
        ToolTip.SetTip(save, "Save every changed spell to the project (Ctrl+S)");
        ToolTip.SetTip(revert, "Drop changes not saved yet and load the saved spells again");
        ToolTip.SetTip(sql, "Save a .sql file of spell_dbc rows, to run on acore_world yourself or keep with your " +
                            "server's SQL updates.\n\nspell_dbc holds server-side spells and changes to existing " +
                            "ones; the worldserver loads it over its Spell.dbc at start-up.");
        ToolTip.SetTip(world, "Write spells into spell_dbc in your acore_world database now, then restart the " +
                              "worldserver.\n\nThe client still needs the patch MPQ (Apply, F6) to show new or " +
                              "changed spells.");

        _talentBadge.Click += (_, _) =>
        {
            if (_tabs?.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Header as string == "Talents") is { } tab)
                _tabs.SelectedItem = tab;
        };
        StackPanel badges = Ui.Horizontal(_subtitle, _schoolBadge, _talentBadge);
        StackPanel heading = Ui.Horizontal(_iconFrame, Ui.Vertical(2, _title, badges));
        Button tools = new Button { Content = "Spell..." };
        tools.Flyout = new MenuFlyout
        {
            Items =
            {
                Item("Compare with clean", CompareAsync),
                Item("Used by...", UsedByAsync),
                Item("New from a template...", NewFromTemplateAsync),
                Item("Find and replace across spells...", FindReplaceAsync),
                Item("Check edited spells", CheckAsync)
            }
        };
        ToolTip.SetTip(tools, "Compare with the clean Spell.dbc, see what uses this spell, start from a template, " +
                              "change many spells at once, or check edited spells for mistakes");
        // The heading keeps its width; the buttons take the rest and wrap onto a second line when it runs out
        WrapPanel actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        foreach (Control action in new Control[] { _finder.Box, _unsaved, tools, sql, world, revert, save })
        {
            action.Margin = new Thickness(8, 2, 0, 2);
            action.VerticalAlignment = VerticalAlignment.Center;
            actions.Children.Add(action);
        }

        Grid header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(16, 12) };
        header.Children.Add(heading);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        DockPanel panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(_editor);
        return panel;
    }

    private void ShowEmpty(string message)
    {
        _title.Text = "Spell Editor";
        _subtitle.Text = string.Empty;
        _editor.Content = new TextBlock
        {
            Text = message,
            Classes = { "hint" },
            FontSize = 14,
            Margin = new Thickness(16),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _count.Text = string.Empty;
    }

    private void BuildEditor(DbcDefinition definition)
    {
        int selected = _tabs?.SelectedIndex ?? 0;
        // Revert and imports rebuild the tabs: the old ones free shared controls and stop following the spell
        foreach (Control kept in new Control[] { _preview, _auraPreview, _lookIcon, _visual, _level, _levelText,
                                                 _levelBox, _levelRange, _talentBoxes, _clientPreview })
            Detach(kept);
        _fixedFields ??= _binding.Count;
        _binding.TrimTo(_fixedFields.Value);
        _tabs = new TabControl { Margin = new Thickness(12, 0, 12, 12) };
        _tabs.Items.Add(Tab("Base", BaseTab()));
        _tabs.Items.Add(Tab("Attributes", AttributesTab()));
        _tabs.Items.Add(Tab("Effects", EffectsTab()));
        _tabs.Items.Add(Tab("Targets and Requirements", TargetsTab()));
        _tabs.Items.Add(Tab("Procs and Interrupts", ProcsTab()));
        TabItem talentsItem = Tab("Talents", TalentsTab(out Action<bool> activateTalents));
        _tabs.Items.Add(talentsItem);
        _tabs.SelectionChanged += (_, _) => activateTalents(_tabs.SelectedItem == talentsItem);
        _tabs.Items.Add(Tab("Items and Reagents", ItemsTab()));
        _tabs.Items.Add(Tab("Skill", new SkillView(_binding, _session, _window).View));
        _tabs.Items.Add(Tab("Visual", _visual));
        SpellModelsView models = new SpellModelsView(_binding, _session);
        TabItem modelsTab = Tab("3D", models.View);
        _tabs.Items.Add(modelsTab);
        _tabs.SelectionChanged += (_, _) => models.Active = _tabs.SelectedItem == modelsTab;
        TabItem localesTab = Tab("Localisation", LocalesTab());
        _tabs.Items.Add(localesTab);
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedItem == localesTab && _clientPreview.IsChecked == true)
                _ = LoadClientTextsAsync();
        };
        _tabs.Items.Add(new TabItem { Header = "All fields", Content = new AllFieldsView(_binding, definition).View });
        _tabs.SelectedIndex = Math.Clamp(selected, 0, _tabs.ItemCount - 1);
        models.Active = _tabs.SelectedItem == modelsTab;
        _editor.Content = _tabs;
        _finder.Forget();
    }

    private int? _fixedFields;

    // Takes a control out of the old tabs, so a rebuild can put it into the new ones
    private static void Detach(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case ContentControl content:
                content.Content = null;
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
            default:
                break;
        }
    }

    // Like Wowhead's tooltip: a caster level and the talents the text asks about, ticked to count as learned
    private readonly Slider _level = new Slider
    {
        Minimum = 1,
        Maximum = MAX_PLAYER_LEVEL,
        Value = MAX_PLAYER_LEVEL,
        IsSnapToTickEnabled = true,
        TickFrequency = 1,
        Width = 220
    };
    // The level in a fixed-width box, so the slider doesn't move between 1 and 80
    private readonly TextBlock _levelText = new TextBlock
    {
        VerticalAlignment = VerticalAlignment.Center,
        Width = 64,
        TextAlignment = TextAlignment.Center,
        FontFeatures = new FontFeatureCollection { new FontFeature { Tag = "tnum" } }
    };
    private readonly NumericUpDown _levelBox = new NumericUpDown
    {
        Minimum = 1,
        Maximum = MAX_PLAYER_LEVEL,
        Value = MAX_PLAYER_LEVEL,
        Increment = 1,
        FormatString = "0",
        Width = 104,
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
    private readonly TextBlock _levelRange = new TextBlock
    {
        Classes = { "hint" },
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly WrapPanel _talentBoxes = new WrapPanel();
    private readonly HashSet<uint> _knownTalents = new HashSet<uint>();
    private List<uint> _askedShown = new List<uint>();
    private bool _levelHooked;
    private const int MAX_PLAYER_LEVEL = 80;
    private const double TALENT_BOX_WIDTH = 230;

    private Control CasterPanel()
    {
        _levelText.Text = (int)_level.Value + " / " + MAX_PLAYER_LEVEL;
        // The slider and box outlive rebuilds of the tabs, so they're hooked up once, and follow each other
        if (!_levelHooked)
        {
            _levelHooked = true;
            ToolTip.SetTip(_levelBox, "Type a level from 1 to " + MAX_PLAYER_LEVEL);
            _level.PropertyChanged += (_, change) =>
            {
                if (change.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                    return;
                _levelText.Text = (int)_level.Value + " / " + MAX_PLAYER_LEVEL;
                if (_levelBox.Value != (decimal)_level.Value)
                    _levelBox.Value = (decimal)_level.Value;
                UpdatePreview();
            };
            _levelBox.ValueChanged += (_, _) =>
            {
                if (_levelBox.Value is { } typed && (double)typed != _level.Value)
                    _level.Value = Math.Clamp((double)Math.Round(typed), _level.Minimum, _level.Maximum);
            };
        }

        _askedShown = new List<uint>();
        TextBlock label = new TextBlock { Text = "Level", VerticalAlignment = VerticalAlignment.Center };
        return Ui.Vertical(2, Ui.Horizontal(label, _levelText, _level, _levelBox), _levelRange, _talentBoxes);
    }

    // One box per spell the text's $?s conditions ask about, kept ticked across spells
    private void ShowAskedTalents(List<uint> asked)
    {
        if (asked.SequenceEqual(_askedShown))
            return;
        _askedShown = asked;
        _talentBoxes.Children.Clear();
        foreach (uint id in asked)
        {
            CheckBox box = new CheckBox
            {
                Content = ChipLook(id).Name,
                IsChecked = _knownTalents.Contains(id),
                Width = TALENT_BOX_WIDTH
            };
            ToolTip.SetTip(box, "Spell " + id + ": count it as learned in the preview");
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true)
                    _knownTalents.Add(id);
                else
                    _knownTalents.Remove(id);
                UpdatePreview();
            };
            _talentBoxes.Children.Add(box);
        }
    }

    // With the preview off, the text boxes take the whole width
    private void ShowPreviewColumn()
    {
        if (_previewBox == null || _previewColumn == null)
            return;
        _previewColumn.Width = _showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        foreach (Control child in _previewBox.Children)
        {
            if (Grid.GetColumn(child) == 2)
                child.IsVisible = _showPreview;
        }
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    // A preview text on the raised colour, the height of the box beside it
    private static Border PreviewPanel(Control text)
    {
        return Ui.Themed(new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            MinHeight = PREVIEW_HEIGHT,
            Margin = new Thickness(0, 0, 0, 8),
            Child = text
        }, Border.BackgroundProperty, ThemeColours.RAISED);
    }

    private const double PREVIEW_HEIGHT = 90;

    private static TabItem Tab(string header, Control content)
    {
        return new TabItem
        {
            Header = header,
            Content = new ScrollViewer
            {
                Content = new StackPanel { Margin = new Thickness(4, 8, 16, 8), Children = { content } },
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            }
        };
    }

    private Control Cell(string label, string column, double width = Ui.FIELD_WIDTH)
    {
        return Named(Ui.Row(label, new CellField(_binding, column, width).Box), column);
    }

    private Control Enum(string label, string column, string key)
    {
        return Named(Ui.Row(label, new EnumField(_binding, column, SpellEnums.Get(key)).View), column);
    }

    // An item's name from the world database, when it's reachable
    private string ItemName(uint id)
    {
        return id == 0 ? string.Empty : _session.WorldName(WorldNames.ITEMS, id) ?? string.Empty;
    }

    // An item's name in its quality colour, with its inventory icon, as the game shows it
    private (IBrush? Colour, IImage? Icon) ItemLook(uint id)
    {
        if (id == 0 || _session.ItemDetails(id) is not { } item)
            return (null, null);
        IBrush? colour = GameColours.ItemQuality(item.Quality) is { } hex ? GameBrush(hex) : null;
        string? icon = _itemSources?.ItemIcon(item.Display);
        return (colour, icon != null ? _images?.GetFile(icon) : null);
    }

    private Anarres.Core.Models.ModelSources? _itemSources;

    private string? WorldEntry(string table, uint id)
    {
        return id == 0 ? null : _session.WorldName(table, id);
    }

    private string ToolCategory(uint id)
    {
        DbcTable? table = _session.NamingTable("TotemCategory.dbc");
        int row = id == 0 || table == null ? -1 : table.FindRow(id);
        int name = table == null ? -1 : SpellRecord.TextColumn(table.Definition, "Name", SpellRecord.ENGLISH);
        return row < 0 || name < 0 ? string.Empty : table!.GetString(row, name);
    }

    private Control ItemLookup(string label, string column)
    {
        return Named(Ui.Row(label, new LookupField(_binding, column, ItemName, ItemLook).View), column);
    }

    private Control Lookup(string label, string column, Func<uint, string> describe)
    {
        return Named(Ui.Row(label, new LookupField(_binding, column, describe).View), column);
    }

    // Fields show readable labels; the tooltip says what the column does and gives its name in spell_dbc
    private static Control Named(Control row, params string[] columns)
    {
        List<string> notes = columns.Select(column => ColumnDocs.Describe(SpellRecord.FILE, column))
                                    .OfType<string>().Distinct().ToList();
        string tip = "spell_dbc: " + string.Join(", ", columns);
        if (notes.Count > 0)
            tip = string.Join("\n\n", notes) + "\n\n" + tip;
        return Ui.Explained(row, tip, notes.Count > 0);
    }

    private Control BaseTab()
    {
        CellField name = new CellField(_binding, "Name_Lang_enUS", 360);
        CellField rank = new CellField(_binding, "NameSubtext_Lang_enUS", 200);
        CellField description = new CellField(_binding, "Description_Lang_enUS", multiline: true);
        CellField aura = new CellField(_binding, "AuraDescription_Lang_enUS", multiline: true);
        description.ValueChanged += UpdatePreview;
        aura.ValueChanged += UpdatePreview;
        _binding.Edited += _ => UpdatePreview();

        // Each text beside what the game shows for it, updated as it's typed
        Grid texts = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto")
        };
        Place(texts, Named(Ui.Label("Description ($s1, $d, $o1 and the rest are filled in on the right)", 500),
                           "Description_Lang_enUS"), 0, 0);
        Place(texts, description.Box, 1, 0);
        Place(texts, Ui.Hint("Tooltip preview"), 0, 2);
        Place(texts, PreviewPanel(_preview), 1, 2);
        Place(texts, Named(Ui.Label("Aura description (shown on the buff)", 500), "AuraDescription_Lang_enUS"),
              2, 0);
        Place(texts, aura.Box, 3, 0);
        Place(texts, Ui.Hint("Buff preview"), 2, 2);
        Place(texts, PreviewPanel(_auraPreview), 3, 2);
        Place(texts, CasterPanel(), 4, 2);
        _previewColumn = texts.ColumnDefinitions[2];
        _previewBox = texts;
        ShowPreviewColumn();

        return Ui.Vertical(0,
            Ui.Group("Name and text",
                     Named(Ui.Row("Name", name.Box), "Name_Lang_enUS"),
                     Named(Ui.Row("Rank", rank.Box), "NameSubtext_Lang_enUS"),
                     texts),
            Ui.Group("Spell",
                     Cell("ID", "ID"),
                     Named(Ui.Row("School", new MaskListField(_binding, "SchoolMask",
                                                              SpellEnums.Get(SpellEnums.SCHOOL)).Combo),
                           "SchoolMask"),
                     Enum("Dispel type", "DispelType", SpellEnums.DISPEL),
                     Enum("Mechanic", "Mechanic", SpellEnums.MECHANIC),
                     Enum("Damage class", "DefenseType", SpellEnums.DAMAGE_CLASS),
                     Enum("Prevention type", "PreventionType", SpellEnums.PREVENTION_TYPE),
                     Cell("Category", "Category"),
                     Enum("Spell family", "SpellClassSet", SpellEnums.SPELL_FAMILY),
                     Named(Ui.Row("Family mask", new CellField(_binding, "SpellClassMask_1", Ui.FIELD_WIDTH).Box,
                                  new CellField(_binding, "SpellClassMask_2", Ui.FIELD_WIDTH).Box,
                                  new CellField(_binding, "SpellClassMask_3", Ui.FIELD_WIDTH).Box),
                           "SpellClassMask_1", "SpellClassMask_2", "SpellClassMask_3"),
                     Cell("Priority", "SpellPriority"),
                     Cell("Description variables", "SpellDescriptionVariableID"),
                     Cell("Difficulty", "SpellDifficultyID")),
            Ui.Group("Casting",
                     Lookup("Cast time", "CastingTimeIndex", CastTime),
                     Lookup("Duration", "DurationIndex", Duration),
                     Lookup("Range", "RangeIndex", Range),
                     Cell("Missile speed", "Speed"),
                     Cell("Cooldown (ms)", "RecoveryTime"),
                     Cell("Category cooldown (ms)", "CategoryRecoveryTime"),
                     Cell("Global cooldown (ms)", "StartRecoveryTime"),
                     Cell("Global cooldown category", "StartRecoveryCategory"),
                     Cell("Stack amount", "CumulativeAura"),
                     Cell("Max targets", "MaxTargets"),
                     Cell("Max target level", "MaxTargetLevel")),
            Ui.Group("Cost",
                     Enum("Power type", "PowerType", SpellEnums.POWER),
                     Cell("Cost", "ManaCost"),
                     Cell("Cost per level", "ManaCostPerLevel"),
                     Cell("Cost per second", "ManaPerSecond"),
                     Cell("Per second, per level", "ManaPerSecondPerLevel"),
                     Cell("Cost (% of base)", "ManaCostPct"),
                     Cell("Rune cost", "RuneCostID"),
                     Cell("Power display", "PowerDisplayID")),
            Ui.Group("Levels",
                     Cell("Base level", "BaseLevel"),
                     Cell("Spell level", "SpellLevel"),
                     Cell("Max level", "MaxLevel")),
            Ui.Group("Look",
                     IconRow(),
                     Lookup("Active icon", "ActiveIconID", Icon),
                     Cell("Visual 1", "SpellVisualID_1"),
                     Cell("Visual 2", "SpellVisualID_2"),
                     Cell("Missile", "SpellMissileID"),
                     Cell("Stance bar order", "StanceBarOrder")));
    }

    private Control IconRow()
    {
        LookupField icon = new LookupField(_binding, "SpellIconID", Icon);
        icon.Value.ValueChanged += () => _lookIcon.Source = _images?.Get(icon.Value.Raw());
        Button pick = Ui.AsyncButton("Pick...", PickIconAsync);
        ToolTip.SetTip(pick, "Choose from every icon in SpellIcon.dbc");
        // The icon sits after the box, so the box lines up with the fields below it
        icon.View.Children.Insert(1, _lookIcon);
        return Named(Ui.Row("Icon", icon.View, pick), "SpellIconID");
    }

    private async Task PickIconAsync()
    {
        if (_spells == null || !_binding.IsBound || _images?.Icons == null)
        {
            _window.SetStatus("Import SpellIcon.dbc to pick icons.");
            return;
        }

        int column = _spells.Definition.IndexOf("SpellIconID");
        IconPickerWindow picker = new IconPickerWindow(_images, _spells.GetUInt(_binding.Row, column));
        uint? chosen = await picker.ShowDialog<uint?>(_window);
        if (chosen is not { } icon)
            return;
        _spells.SetUInt(_binding.Row, column, icon);
        _binding.Refresh();
        Edited();
    }

    private Control AttributesTab()
    {
        StackPanel groups = new StackPanel { Spacing = 8 };
        for (int index = 0; index < SpellEnums.AttributeColumns.Length; ++index)
        {
            string column = SpellEnums.AttributeColumns[index];
            FlagToggles toggles = new FlagToggles(_binding, column, SpellEnums.Get(column));
            Button clear = Ui.Button("Clear", toggles.Clear);
            ToolTip.SetTip(clear, "Turn every bit of " + column + " off");
            groups.Children.Add(Ui.Group("Attributes " + index,
                                         Named(Ui.Row("Value", toggles.Value.Box, toggles.Hex, clear), column),
                                         toggles.Bits));
        }

        return groups;
    }

    private Control Flags(string title, string column, EnumTable table)
    {
        return Flags(title, column, () => table);
    }

    // A bit mask as a group of switches, like Attributes: the value, its hex, Clear, then each named bit
    private Control Flags(string title, string column, Func<EnumTable?> table)
    {
        FlagToggles toggles = new FlagToggles(_binding, column, table, knownOnly: true);
        Button clear = Ui.Button("Clear", toggles.Clear);
        ToolTip.SetTip(clear, "Turn every bit of " + column + " off");
        return Ui.Group(title, Named(Ui.Row("Value", toggles.Value.Box, toggles.Hex, clear), column), toggles.Bits);
    }

    private Control EffectsTab()
    {
        TabControl effects = new TabControl();
        for (int effect = 0; effect < SpellRecord.EFFECT_COUNT; ++effect)
            effects.Items.Add(new TabItem { Header = "Effect " + (effect + 1), Content = EffectPanel(effect) });
        return effects;
    }

    private Control EffectPanel(int effect)
    {
        string Column(string name) => SpellRecord.EffectColumn(name, effect);
        EnumField type = new EnumField(_binding, Column("Effect"), SpellEnums.Get(SpellEnums.EFFECT));
        EnumField aura = new EnumField(_binding, Column("EffectAura"), SpellEnums.Get(SpellEnums.AURA));
        return Ui.Vertical(0,
            Ui.Group("Effect",
                     Named(Ui.Row("Effect", type.View), Column("Effect")),
                     Named(Ui.Row("Aura", aura.View), Column("EffectAura")),
                     Cell("Base points", Column("EffectBasePoints")),
                     Cell("Die sides", Column("EffectDieSides")),
                     Cell("Points per level", Column("EffectRealPointsPerLevel")),
                     Cell("Points per combo point", Column("EffectPointsPerCombo")),
                     Cell("Value multiplier", Column("EffectMultipleValue")),
                     Cell("Bonus multiplier", Column("EffectBonusMultiplier")),
                     Ui.Hint("The tooltip shows base points + 1; with more than one die side it shows a range.")),
            Ui.Group("Targets",
                     Enum("Target A", Column("ImplicitTargetA"), SpellEnums.TARGET),
                     Enum("Target B", Column("ImplicitTargetB"), SpellEnums.TARGET),
                     Lookup("Radius", Column("EffectRadiusIndex"), Radius),
                     Cell("Chain targets", Column("EffectChainTargets")),
                     Cell("Chain amplitude", Column("EffectChainAmplitude"))),
            Ui.Group("Details",
                     Enum("Mechanic", Column("EffectMechanic"), SpellEnums.MECHANIC),
                     Cell("Aura period (ms)", Column("EffectAuraPeriod")),
                     new MiscField(_binding, Column("EffectMiscValue"), Column("Effect"), Column("EffectAura"),
                                   second: false, "Misc value", WorldEntry).View,
                     new MiscField(_binding, Column("EffectMiscValueB"), Column("Effect"), Column("EffectAura"),
                                   second: true, "Misc value B", WorldEntry).View,
                     Lookup("Trigger spell", Column("EffectTriggerSpell"), SpellName),
                     ItemLookup("Item created", Column("EffectItemType")),
                     Named(Ui.Row("Affected family mask",
                                  new CellField(_binding, Column("EffectSpellClassMaskA"), Ui.FIELD_WIDTH).Box,
                                  new CellField(_binding, Column("EffectSpellClassMaskB"), Ui.FIELD_WIDTH).Box,
                                  new CellField(_binding, Column("EffectSpellClassMaskC"), Ui.FIELD_WIDTH).Box),
                           Column("EffectSpellClassMaskA"), Column("EffectSpellClassMaskB"),
                           Column("EffectSpellClassMaskC"))),
            AffectedGroup(effect));
    }

    // Like Wowhead's "Affected spells": the spells of the same family this effect's masks pick out
    private Control AffectedGroup(int effect)
    {
        SpellChips chips = new SpellChips(ChipLook, Open);
        TextBlock title = new TextBlock { Classes = { "groupTitle" } };
        Border group = new Border { Classes = { "group" }, Child = Ui.Vertical(0, title, chips.View) };
        _binding.Add(new RefreshField(() =>
        {
            List<int> rows = _binding.IsBound ? SpellReferences.Affected(_binding.Table!, _binding.Row, effect + 1)
                                              : new List<int>();
            group.IsVisible = rows.Count > 0;
            title.Text = "Affected spells (" + rows.Count + ")";
            chips.Show(rows.Select(row => (_binding.Table!.Key(row), string.Empty)).ToList(), string.Empty);
        }));
        return group;
    }

    private SpellLook ChipLook(uint id)
    {
        int row = Find(_spells, id);
        if (row < 0)
            return new SpellLook("Spell " + id + " (not in Spell.dbc)", null, string.Empty);
        SpellRecord spell = new SpellRecord(_spells!, row);
        string name = spell.Text("Name", _locale);
        string rank = spell.Text("NameSubtext", _locale);
        return new SpellLook((name.Length == 0 ? "(no name)" : name) + (rank.Length > 0 ? " (" + rank + ")" : ""),
                             ListIcon(id), SpellCard(id));
    }

    private void Open(uint id)
    {
        Select(id);
    }

    // Talents, then other auras, changing this spell by family mask; only worked out while the tab is shown
    private Control TalentsTab(out Action<bool> activate)
    {
        SpellChips talents = new SpellChips(ChipLook, Open);
        SpellChips others = new SpellChips(ChipLook, Open);
        TextBlock talentTitle = new TextBlock { Classes = { "groupTitle" } };
        TextBlock otherTitle = new TextBlock { Classes = { "groupTitle" } };
        bool active = false;
        void Load()
        {
            if (!active || !_binding.IsBound)
                return;
            HashSet<uint> talentSpells = SpellReferences.TalentSpells(_session.NamingTable("Talent.dbc"));
            AuraNames auraNames = new AuraNames();
            List<(uint Id, string Note)> modifiers = SpellReferences.Find(_binding.Table!, _binding.Row)
                .Where(reference => reference.Group == SpellReferences.MODIFIED_BY)
                .Select(reference => (reference.SpellId, auraNames.Describe(reference.How)))
                .ToList();
            List<(uint Id, string Note)> fromTalents = modifiers.Where(entry => talentSpells.Contains(entry.Id))
                                                                .ToList();
            List<(uint Id, string Note)> fromOthers = modifiers.Where(entry => !talentSpells.Contains(entry.Id))
                                                               .ToList();
            talentTitle.Text = "Talents that change this spell (" + fromTalents.Count + ")";
            otherTitle.Text = "Other auras that change it: glyphs, set bonuses, buffs (" + fromOthers.Count + ")";
            talents.Show(fromTalents, talentSpells.Count == 0
                                          ? "Talent.dbc isn't in the project or the clean files, so talents can't " +
                                            "be told apart; they're listed below."
                                          : "No talent changes this spell.");
            others.Show(fromOthers, "Nothing else changes this spell.");
        }

        _binding.Add(new RefreshField(Load));
        activate = value =>
        {
            active = value;
            Load();
        };
        return Ui.Vertical(0,
            Ui.Hint("Spells of the same family whose effects pick this spell out with their family masks " +
                    "(EffectSpellClassMask against this spell's SpellClassMask), as the game applies them."),
            new Border { Classes = { "group" }, Child = Ui.Vertical(0, talentTitle, talents.View) },
            new Border { Classes = { "group" }, Child = Ui.Vertical(0, otherTitle, others.View) });
    }

    // "effect 1, aura 108" as "effect 1: Add Pct Modifier (108)"
    private sealed class AuraNames
    {
        private readonly EnumTable _auras = SpellEnums.Get(SpellEnums.AURA);

        public string Describe(string how)
        {
            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(how, @"^(?<effect>effect \d+), aura (?<aura>\d+)$");
            if (!match.Success)
                return how;
            long aura = long.Parse(match.Groups["aura"].Value, CultureInfo.InvariantCulture);
            string name = _auras.Names(aura);
            return match.Groups["effect"].Value + ": " + (name.Length > 0 ? name + " (" + aura + ")" : "aura " + aura);
        }
    }

    private sealed class RefreshField : IField
    {
        private readonly Action _refresh;

        public RefreshField(Action refresh)
        {
            _refresh = refresh;
        }

        public void Refresh()
        {
            _refresh();
        }
    }

    private Control TargetsTab()
    {
        return Ui.Vertical(0,
            Flags("Target flags (Targets)", "Targets", SpellEnums.Get(SpellEnums.CAST_TARGET_FLAGS)),
            Flags("Creature types (TargetCreatureType)", "TargetCreatureType", SpellEnums.CreatureTypeMask()),
            Flags("Shapeshift forms that can cast (ShapeshiftMask)", "ShapeshiftMask", SpellEnums.StanceMask()),
            Flags("Shapeshift forms that can't cast (ShapeshiftExclude)", "ShapeshiftExclude",
                  SpellEnums.StanceMask()),
            Ui.Group("Aura states",
                     Enum("Caster needs", "CasterAuraState", SpellEnums.AURA_STATE),
                     Enum("Target needs", "TargetAuraState", SpellEnums.AURA_STATE),
                     Enum("Caster must not have", "ExcludeCasterAuraState", SpellEnums.AURA_STATE),
                     Enum("Target must not have", "ExcludeTargetAuraState", SpellEnums.AURA_STATE)),
            Ui.Group("Aura spells",
                     Lookup("Caster needs", "CasterAuraSpell", SpellName),
                     Lookup("Target needs", "TargetAuraSpell", SpellName),
                     Lookup("Caster must not have", "ExcludeCasterAuraSpell", SpellName),
                     Lookup("Target must not have", "ExcludeTargetAuraSpell", SpellName)),
            Ui.Group("Other requirements",
                     Cell("Facing flags", "FacingCasterFlags"),
                     Cell("Spell focus object", "RequiresSpellFocus"),
                     Cell("Area group", "RequiredAreasID"),
                     Cell("Aura vision", "RequiredAuraVision"),
                     Cell("Faction", "MinFactionID"),
                     Cell("Reputation", "MinReputation")));
    }

    private Control ProcsTab()
    {
        return Ui.Vertical(0,
            Ui.Group("Proc",
                     Cell("Chance (%)", "ProcChance"),
                     Cell("Charges", "ProcCharges")),
            Flags("Procs on (ProcTypeMask)", "ProcTypeMask", SpellEnums.Get(SpellEnums.PROC_FLAGS)),
            Flags("Interrupted by (InterruptFlags)", "InterruptFlags", SpellEnums.Get(SpellEnums.INTERRUPT_FLAGS)),
            Flags("Aura removed by (AuraInterruptFlags)", "AuraInterruptFlags",
                  SpellEnums.Get(SpellEnums.AURA_INTERRUPT_FLAGS)),
            Flags("Channel interrupted by (ChannelInterruptFlags)", "ChannelInterruptFlags",
                  SpellEnums.Get(SpellEnums.CHANNEL_INTERRUPT_FLAGS)));
    }

    private Control ItemsTab()
    {
        StackPanel reagents = new StackPanel();
        for (int reagent = 1; reagent <= SpellRecord.REAGENT_COUNT; ++reagent)
        {
            LookupField item = new LookupField(_binding, "Reagent_" + reagent, ItemName, ItemLook);
            item.View.Children.Insert(1, new TextBlock { Text = "x", Classes = { "label" },
                                                         VerticalAlignment = VerticalAlignment.Center });
            item.View.Children.Insert(2, new CellField(_binding, "ReagentCount_" + reagent, Ui.FIELD_WIDTH).Box);
            reagents.Children.Add(Named(Ui.Row("Reagent " + reagent, item.View),
                                        "Reagent_" + reagent, "ReagentCount_" + reagent));
        }

        return Ui.Vertical(0,
            Ui.Group("Reagents (item ID x count)", reagents),
            Ui.Group("Tools",
                     ItemLookup("Totem 1", "Totem_1"),
                     ItemLookup("Totem 2", "Totem_2"),
                     Lookup("Tool category 1", "RequiredTotemCategoryID_1", ToolCategory),
                     Lookup("Tool category 2", "RequiredTotemCategoryID_2", ToolCategory),
                     Ui.Hint("Item names come from item_template in the world database (Project settings > " +
                             "Apply), when it can be reached.")),
            Ui.Group("Equipped item",
                     Lookup("Item class", "EquippedItemClass",
                            value => ColumnValues.ItemClasses(_session.NamingTable)?.Names((int)value) ?? string.Empty),
                     Ui.Hint("-1 in Item class means no item is needed. The subclasses listed are the class's, from " +
                             "ItemSubClass.dbc, so custom ones show too.")),
            Flags("Item subclasses (EquippedItemSubclass)", "EquippedItemSubclass",
                  () => _binding.IsBound
                            ? ColumnValues.ItemSubclasses(_session.NamingTable,
                                                          new SpellRecord(_binding.Table!, _binding.Row)
                                                              .I("EquippedItemClass"))
                            : null),
            Flags("Inventory types (EquippedItemInvTypes)", "EquippedItemInvTypes", ColumnValues.InventoryTypeMask()));
    }

    private Control LocalesTab()
    {
        _localeBoxes.Clear();
        StackPanel panel = new StackPanel();
        CheckBox onlyEmpty = new CheckBox { Content = "Only fill text that's empty here", IsChecked = true };
        Button fill = Ui.AsyncButton("Import from the client", () => FillLanguagesAsync(onlyEmpty.IsChecked == true));
        ToolTip.SetTip(fill, "Reads Spell.dbc from each locale folder in the WoW client's Data folder and copies its " +
                             "language into these columns, for every spell. Save afterwards to keep it; Undo takes " +
                             "it back.");
        _clientPreview.IsCheckedChanged -= OnPreviewChanged;
        _clientPreview.IsCheckedChanged += OnPreviewChanged;
        ToolTip.SetTip(_clientPreview, "Shows in red, in each empty box, the text the client has for this spell. " +
                                       "Nothing changes until Import from the client.");
        panel.Children.Add(Ui.Group("Languages",
                                    Ui.Hint("The 3.3.5a client reads 9 of the 16 columns, one per language, in the " +
                                            "order below. AzerothCore names the columns after a later list, so the " +
                                            "!'s column names differ: German is Name_Lang_frFR, and the 10th " +
                                            "column, unused, is Name_Lang_esES."),
                                    Ui.Hint("Each locale's client only has its own language. Importing with All " +
                                            "available fills them all; otherwise the others are empty here."),
                                    Ui.Horizontal(fill, onlyEmpty, _clientPreview)));
        foreach (string prefix in new[] { "Name", "NameSubtext", "Description", "AuraDescription" })
        {
            StackPanel rows = new StackPanel();
            // Labelled by what the client keeps in each column; AzerothCore's column names differ
            for (int slot = 0; slot < SpellRecord.TEXT_COLUMNS; ++slot)
            {
                bool longText = prefix is "Description" or "AuraDescription";
                string column = prefix + "_Lang_" + SpellRecord.Locales[slot];
                string label = slot < SpellRecord.ClientLocales.Length ? SpellRecord.ClientLocales[slot]
                                                                         : "Unused " + (slot + 1);
                TextBox box = new CellField(_binding, column, longText ? 640 : 360).Box;
                rows.Children.Add(Named(Ui.Row(label, PreviewOver(box, prefix, slot)), column));
            }

            rows.Children.Add(Named(Ui.Row("Mask", new CellField(_binding, prefix + "_Lang_Mask").Box),
                                    prefix + "_Lang_Mask"));
            panel.Children.Add(Ui.Group(prefix, rows));
        }

        return panel;
    }

    // The client's text drawn in red over an empty box; it isn't the box's text, so nothing is changed by it
    private Control PreviewOver(TextBox box, string field, int slot)
    {
        TextBlock preview = Ui.Themed(new TextBlock
        {
            Margin = new Thickness(PREVIEW_INSET, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
            IsVisible = false
        }, TextBlock.ForegroundProperty, ThemeColours.ERROR);
        box.TextChanged += (_, _) => preview.IsVisible = preview.Text is { Length: > 0 } &&
                                                         string.IsNullOrEmpty(box.Text);
        _localeBoxes.Add((box, preview, field, slot));
        Grid grid = new Grid { Width = box.Width, HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(box);
        grid.Children.Add(preview);
        return grid;
    }

    private void OnPreviewChanged(object? sender, RoutedEventArgs args)
    {
        if (_clientPreview.IsChecked == true)
            _ = LoadClientTextsAsync();
        ShowLocalePreview();
    }

    // Fills the red previews for the spell shown, from the client's text read once
    private void ShowLocalePreview()
    {
        if (!_binding.IsBound)
            return;
        uint id = new SpellRecord(_spells!, _binding.Row).Id;
        bool show = _clientPreview.IsChecked == true && _clientTexts != null;
        foreach ((TextBox box, TextBlock preview, string field, int slot) in _localeBoxes)
        {
            string text = show ? _clientTexts!.FirstOrDefault(texts => texts.Slot == slot)?.Get(id, field) ?? ""
                               : string.Empty;
            preview.Text = text;
            preview.IsVisible = text.Length > 0 && string.IsNullOrEmpty(box.Text);
        }
    }

    // Each locale folder's Spell.dbc, kept as text only; read when the Localisation tab first shows
    private async Task LoadClientTextsAsync()
    {
        string data = _session.Settings.ClientData();
        if (data.Length == 0 || (_clientTexts != null && _clientTextsFolder == data) || _loadingClientTexts)
            return;
        _loadingClientTexts = true;
        try
        {
            string patch = _session.Settings.PatchPath();
            DbcDefinitions definitions = _session.Definitions;
            _window.SetStatus("Reading the client's text for the preview...");
            _clientTexts = await Task.Run(() =>
            {
                List<LocaleTexts> found = new List<LocaleTexts>();
                foreach (string locale in LocaleMerge.Order(ClientArchives.FindLocales(data)))
                {
                    try
                    {
                        if (LocaleMerge.Read(data, locale, SpellRecord.FILE, patch, definitions) is { } source &&
                            found.All(texts => texts.Slot != source.Slot))
                            found.Add(LocaleMerge.Texts(source));
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or
                                                          UnauthorizedAccessException or NotSupportedException)
                    {
                        // A locale that can't be read just has no preview
                    }
                }

                return found;
            });
            _clientTextsFolder = data;
            _window.SetStatus("The client's text is shown in red where a box is empty (" + _clientTexts.Count +
                              (_clientTexts.Count == 1 ? " locale)." : " locales)."));
        }
        finally
        {
            _loadingClientTexts = false;
        }

        ShowLocalePreview();
    }

    private string CastTime(uint index)
    {
        if (index == 0)
            return "Instant";
        int row = Find(CastTimes, index);
        if (row < 0)
            return "Not in SpellCastTimes.dbc";
        int milliseconds = CastTimes!.GetInt(row, 1);
        return milliseconds == 0 ? "Instant" : Seconds(milliseconds);
    }

    private string Duration(uint index)
    {
        if (index == 0)
            return "No duration";
        int row = Find(_lookups.Durations, index);
        if (row < 0)
            return "Not in SpellDuration.dbc";
        DbcTable table = _lookups.Durations!;
        int milliseconds = table.GetInt(row, 1);
        return milliseconds < 0 ? "Until cancelled" : Seconds(milliseconds) + ", +" +
                                                      Seconds(table.GetInt(row, 2)) + " per level, at most " +
                                                      Seconds(table.GetInt(row, 3));
    }

    private string Range(uint index)
    {
        int row = Find(_lookups.Ranges, index);
        if (row < 0)
            return "Not in SpellRange.dbc";
        DbcTable table = _lookups.Ranges!;
        return table.GetFloat(row, 1).ToString("0.#", CultureInfo.InvariantCulture) + " to " +
               table.GetFloat(row, RANGE_MAX_COLUMN).ToString("0.#", CultureInfo.InvariantCulture) + " yards, " +
               table.GetString(row, RANGE_NAME_COLUMN);
    }

    private string Radius(uint index)
    {
        if (index == 0)
            return "None";
        return _lookups.Radius(index) is { } radius
                   ? radius.ToString("0.#", CultureInfo.InvariantCulture) + " yards"
                   : "Not in SpellRadius.dbc";
    }

    private string Icon(uint index)
    {
        int row = Find(Icons, index);
        if (row < 0)
            return index == 0 ? "None" : "Not in SpellIcon.dbc";
        int column = Icons!.Definition.Columns.FindIndex(entry => entry.Type == DbcColumnType.String);
        return column >= 0 ? Icons.GetString(row, column) : string.Empty;
    }

    private string SpellName(uint id)
    {
        if (id == 0)
            return "None";
        int row = Find(_spells, id);
        if (row < 0)
            return "No such spell";
        SpellRecord spell = new SpellRecord(_spells!, row);
        return spell.Rank.Length > 0 ? spell.Name + " (" + spell.Rank + ")" : spell.Name;
    }

    private static int Find(DbcTable? table, uint key)
    {
        return table?.FindRow(key) ?? -1;
    }

    private static string Seconds(int milliseconds)
    {
        return (milliseconds / MILLISECONDS_PER_SECOND).ToString("0.##", CultureInfo.InvariantCulture) + " sec";
    }

    private void Filter()
    {
        if (_spells == null)
            return;
        DbcTable table = _spells;
        int nameColumn = SpellRecord.TextColumn(table.Definition, "Name", _locale);
        int rankColumn = SpellRecord.TextColumn(table.Definition, "NameSubtext", _locale);
        int englishName = table.Definition.IndexOf("Name_Lang_" + SpellRecord.ENGLISH);
        int englishRank = table.Definition.IndexOf("NameSubtext_Lang_" + SpellRecord.ENGLISH);
        int schoolColumn = table.Definition.IndexOf("SchoolMask");
        string query = (_search.Text ?? string.Empty).Trim();
        Func<int, bool> matches = SpellSearch.Build(table, query);
        List<SpellItem> items = new List<SpellItem>();
        for (int row = 0; row < table.Rows.Count; ++row)
        {
            if (!matches(row))
                continue;
            string name = Localised(table, row, nameColumn, englishName);
            string rank = _showRank ? Localised(table, row, rankColumn, englishRank) : string.Empty;
            items.Add(new SpellItem(table.Key(row), name, rank)
            {
                SchoolBrush = SchoolBrush(schoolColumn >= 0 ? table.GetRaw(row, schoolColumn) : 0),
                IconFor = ListIcon,
                CardFor = SpellCard
            });
        }

        uint? selected = (_list.SelectedItem as SpellItem)?.Id;
        _list.ItemsSource = items;
        _count.Text = items.Count.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                      table.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " spells";
        if (selected is { } id && items.FindIndex(item => item.Id == id) is var index and >= 0)
            _list.SelectedIndex = index;
    }

    private static string Localised(DbcTable table, int row, int column, int english)
    {
        string text = column >= 0 ? table.GetString(row, column) : string.Empty;
        return text.Length > 0 || english < 0 ? text : table.GetString(row, english);
    }

    private void ShowSelected()
    {
        if (_spells == null || _list.SelectedItem is not SpellItem item)
        {
            _binding.Bind(_spells, -1);
            return;
        }

        int row = _spells.FindRow(item.Id);
        // Edits to one spell are a separate undo step from the next spell's
        _spells.BreakUndoStep();
        _binding.Bind(_spells, row);
        UpdateHeading();
        UpdatePreview();
        _visual.Show(row);
    }

    private void UpdateHeading()
    {
        if (!_binding.IsBound)
            return;
        SpellRecord spell = new SpellRecord(_spells!, _binding.Row);
        string name = spell.Text("Name", _locale);
        string rank = spell.Text("NameSubtext", _locale);
        _title.Text = name.Length == 0 ? "(no name)" : name;
        _subtitle.Text = "Spell " + spell.Id.ToString(CultureInfo.InvariantCulture) +
                         (rank.Length > 0 ? "  -  " + rank : string.Empty);
        ShowBadges(spell);
        ShowLocalePreview();
        _unsaved.IsVisible = _session.ModifiedTables.Any();
        _headerIcon.Source = _images?.Get(spell.U("SpellIconID"));
        _lookIcon.Source = _headerIcon.Source;
        uint dispel = spell.U("DispelType");
        _iconFrame.BorderBrush = GameColours.DispelType(dispel) is { } border ? GameBrush(border) : null;
        ToolTip.SetTip(_iconFrame, GameColours.DispelType(dispel) != null
                                       ? SpellEnums.Get(SpellEnums.DISPEL).Describe(dispel) +
                                         " (border in the game's debuff colour)"
                                       : null);
    }

    private void ShowBadges(SpellRecord spell)
    {
        uint school = spell.U("SchoolMask");
        string schools = SpellEnums.Get(SpellEnums.SCHOOL).Describe(school);
        ((TextBlock)_schoolBadge.Child!).Text = schools.Length > 0 ? schools : "No school";
        if (GameColours.School(school) is { } hex)
        {
            Color colour = Color.Parse(hex);
            _schoolBadge.BorderBrush = GameBrush(hex);
            _schoolBadge.Background = new SolidColorBrush(Color.FromArgb(BADGE_FILL_ALPHA, colour.R, colour.G,
                                                                         colour.B));
            ((TextBlock)_schoolBadge.Child!).Foreground = GameBrush(hex);
        }
        else
        {
            _schoolBadge.ClearValue(Border.BorderBrushProperty);
            _schoolBadge.ClearValue(Border.BackgroundProperty);
            ((TextBlock)_schoolBadge.Child!).ClearValue(TextBlock.ForegroundProperty);
        }

        HashSet<uint> talents = SpellReferences.TalentSpells(_session.NamingTable("Talent.dbc"));
        int count = SpellReferences.ModifiedBy(spell.Table, spell.Row).Count(id => talents.Contains(id));
        _talentBadge.Content = count == 1 ? "1 talent" : count + " talents";
        _talentBadge.IsVisible = count > 0;
        ToolTip.SetTip(_talentBadge, count + " talent ranks change this spell; open the Talents tab");
    }

    private void UpdatePreview()
    {
        if (!_binding.IsBound)
            return;
        SpellRecord spell = new SpellRecord(_spells!, _binding.Row);
        TooltipContext context = new TooltipContext { Level = (int)_level.Value, Known = _knownTalents };
        string description = spell.Text("Description", _locale);
        string aura = spell.Text("AuraDescription", _locale);
        _preview.Text = SpellText.Render(spell, description, _lookups, context);
        _auraPreview.Text = SpellText.Render(spell, aura, _lookups, context);
        ShowAskedTalents(SpellText.AskedSpells(spell, description + aura, _lookups));
        // From the spell's own BaseLevel and MaxLevel: where points per level stop changing anything
        int baseLevel = spell.I("BaseLevel");
        int maxLevel = spell.I("MaxLevel");
        _levelRange.Text = maxLevel > baseLevel
                               ? "Points per level count from level " + Math.Max(baseLevel, spell.I("SpellLevel")) +
                                 " up to " + maxLevel + " (BaseLevel, MaxLevel)."
                               : string.Empty;
        _levelRange.IsVisible = _levelRange.Text.Length > 0;
    }

    private void Edited()
    {
        UpdateHeading();
        if (_list.SelectedItem is SpellItem item && _binding.IsBound)
        {
            SpellRecord spell = new SpellRecord(_spells!, _binding.Row);
            string name = spell.Text("Name", _locale);
            string rank = _showRank ? spell.Text("NameSubtext", _locale) : string.Empty;
            if (item.Name != name || item.Rank != rank || item.Id != spell.Id)
            {
                item.Name = name;
                item.Rank = rank;
                item.Id = spell.Id;
            }
        }

        _session.NotifyChanged();
    }

    private DbcTable? _clean;

    // The clean Spell.dbc, read once; null with a message when there's none
    private async Task<DbcTable?> CleanAsync(bool quiet = false)
    {
        _clean ??= await Task.Run(() => DbcMerge.LoadSource(_session.Settings, SpellRecord.FILE,
                                                            _session.Definitions));
        if (_clean == null && !quiet)
            await Dialogs.MessageAsync(_window, "No clean Spell.dbc", "There's no clean Spell.dbc to compare with. " +
                                                                      "Set the Clean DBC folder, the server's data " +
                                                                      "folder or the WoW client in Project settings.");
        return _clean;
    }

    private async Task CompareAsync()
    {
        if (_spells == null || !_binding.IsBound || await CleanAsync() is not { } clean)
            return;
        CompareWindow dialog = new CompareWindow(clean, _spells, _binding.Row, _session.NamingTable);
        await dialog.ShowDialog(_window);
        if (!dialog.Reverted)
            return;
        _binding.Refresh();
        Edited();
    }

    private async Task NewFromTemplateAsync()
    {
        if (_spells == null || await new TemplateWindow().ShowDialog<SpellTemplate?>(_window) is not { } template)
            return;
        uint id = _spells.NextFreeKey();
        int row = _spells.AddRow(id);
        CopyLanguageMasks(row);
        SpellTemplates.Apply(template, _spells, row);
        _spells.BreakUndoStep();
        AfterAdd(id);
    }

    // The rows that differ from the clean Spell.dbc, new spells included, checked for mistakes
    public async Task<List<SpellProblem>?> CheckEditedAsync(bool quiet = false)
    {
        if (_spells == null || await CleanAsync(quiet) is not { } clean)
            return null;
        DbcTable spells = _spells;
        foreach (string file in new[] { "SpellCastTimes.dbc", "SpellDuration.dbc", "SpellRange.dbc", "SpellIcon.dbc",
                                        "SpellVisual.dbc", "SpellRuneCost.dbc", "SpellDifficulty.dbc",
                                        "SpellDescriptionVariables.dbc", "SpellCategory.dbc", "SpellRadius.dbc" })
            _checkTables[file] = await _session.TryLoadAsync(file) ??
                                 await Task.Run(() => DbcMerge.LoadSource(_session.Settings, file,
                                                                          _session.Definitions));
        List<int> rows = await Task.Run(() => DbcMerge.Changed(clean, spells));
        return SpellValidator.Check(spells, rows, file => _checkTables.TryGetValue(file, out DbcTable? table)
                                                              ? table
                                                              : null);
    }

    private readonly Dictionary<string, DbcTable?> _checkTables =
        new Dictionary<string, DbcTable?>(StringComparer.OrdinalIgnoreCase);

    private async Task CheckAsync()
    {
        _window.SetStatus("Checking the edited spells...");
        List<SpellProblem>? problems = await CheckEditedAsync();
        _window.SetStatus(string.Empty);
        if (problems == null)
            return;
        await Dialogs.MessageAsync(_window, "Check edited spells",
                                   problems.Count == 0
                                       ? "Nothing looks wrong with the spells that differ from the clean Spell.dbc."
                                       : Describe(problems));
    }

    // One line per problem, by spell
    public static string Describe(List<SpellProblem> problems, int most = 40)
    {
        IEnumerable<string> lines = problems.Take(most)
                                            .Select(problem => problem.SpellId + " " + problem.SpellName + ": " +
                                                               problem.Text);
        return problems.Count + (problems.Count == 1 ? " thing looks" : " things look") + " wrong:\n\n" +
               string.Join("\n", lines) + (problems.Count > most ? "\n..." : string.Empty);
    }

    private void CopyLanguageMasks(int row)
    {
        // The language masks say which translations the client may show; they're taken from an existing spell
        foreach (string mask in new[] { "Name_Lang_Mask", "NameSubtext_Lang_Mask", "Description_Lang_Mask",
                                        "AuraDescription_Lang_Mask" })
        {
            int column = _spells!.Definition.IndexOf(mask);
            if (column >= 0 && row > 0)
                _spells.SetRaw(row, column, _spells.GetRaw(0, column));
        }
    }

    private async Task UsedByAsync()
    {
        if (_spells == null || !_binding.IsBound)
            return;
        DbcTable spells = _spells;
        int row = _binding.Row;
        SpellRecord spell = new SpellRecord(spells, row);
        List<SpellReference> references = await Task.Run(() => SpellReferences.Find(spells, row));
        uint? open = await new UsedByWindow(spell.Id, spell.Name, references).ShowDialog<uint?>(_window);
        if (open is { } id)
            Select(id);
    }

    private async Task FindReplaceAsync()
    {
        if (_spells == null)
            return;
        FindReplaceWindow dialog = new FindReplaceWindow(_spells, _search.Text ?? string.Empty);
        int changed = await dialog.ShowDialog<int>(_window);
        if (changed == 0)
            return;
        _binding.Refresh();
        Filter();
        Edited();
        _window.SetStatus("Changed " + changed + (changed == 1 ? " spell" : " spells") +
                          ". Save to keep it; Undo takes it back.");
    }

    private void NewSpell()
    {
        if (_spells == null)
            return;
        uint id = _spells.NextFreeKey();
        int row = _spells.AddRow(id);
        _spells.SetString(row, _spells.Definition.IndexOf("Name_Lang_enUS"), "New spell");
        // The language masks say which translations the client may show; they're taken from an existing spell
        foreach (string mask in new[] { "Name_Lang_Mask", "NameSubtext_Lang_Mask", "Description_Lang_Mask",
                                        "AuraDescription_Lang_Mask" })
        {
            int column = _spells.Definition.IndexOf(mask);
            if (column >= 0 && row > 0)
                _spells.SetRaw(row, column, _spells.GetRaw(0, column));
        }

        AfterAdd(id);
    }

    private void CopySpell()
    {
        if (_spells == null || !_binding.IsBound)
            return;
        uint id = _spells.NextFreeKey();
        _spells.CopyRow(_binding.Row, id);
        AfterAdd(id);
    }

    private void AfterAdd(uint id)
    {
        _session.NotifyChanged();
        Select(id);
        _window.SetStatus("Spell " + id + " added. Save to keep it.");
    }

    private async Task DeleteSpellAsync()
    {
        if (_spells == null || !_binding.IsBound)
            return;
        SpellRecord spell = new SpellRecord(_spells, _binding.Row);
        if (_confirmDeletes &&
            !await Dialogs.ConfirmAsync(_window, "Delete spell", "Delete spell " + spell.Id + " (" + spell.Name +
                                                                 ")? It's removed from the project when you save; " +
                                                                 "Undo brings it back.", "Delete"))
            return;
        _spells.RemoveRow(_binding.Row);
        _binding.Bind(_spells, -1);
        Filter();
        _session.NotifyChanged();
    }

    // Saves Spell.dbc and the other tables the spell editor changed, such as visuals and kits
    public async Task SaveAsync()
    {
        if (_spells == null)
            return;
        try
        {
            int changed = _spells.ChangedKeys.Count + _spells.RemovedKeys.Count;
            List<string> others = _session.ModifiedTables.Where(table => table != _spells)
                                          .Select(table => table.Definition.File).ToList();
            await _session.SaveAllAsync();
            _window.SetStatus("Saved " + changed + (changed == 1 ? " spell" : " spells") +
                              (others.Count > 0 ? " and " + string.Join(", ", others) : string.Empty) +
                              " to the project.");
            UpdateHeading();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              System.Data.Common.DbException)
        {
            await Dialogs.MessageAsync(_window, "Not saved", "The spells couldn't be saved: " + exception.Message);
        }
    }

    private async Task RevertAsync()
    {
        if (_spells == null || !_session.ModifiedTables.Any())
            return;
        if (!await Dialogs.ConfirmAsync(_window, "Revert", "Drop every spell and visual change that isn't saved yet?",
                                        "Revert"))
            return;
        uint? selected = (_list.SelectedItem as SpellItem)?.Id;
        _session.Forget(SpellRecord.FILE);
        foreach (string file in new[] { "SpellVisual.dbc", "SpellVisualKit.dbc" })
            _session.Forget(file);
        await LoadAsync();
        if (selected is { } id)
            Select(id);
    }

    // The rows to write: the shown spell, or every spell that differs from the clean Spell.dbc
    private async Task<List<int>?> RowsToWriteAsync(DbcTable spells, bool changedOnly)
    {
        if (!changedOnly)
            return _binding.IsBound ? new List<int> { _binding.Row } : null;
        _window.SetStatus("Comparing with the clean Spell.dbc...");
        DbcTable? clean = await Task.Run(() => DbcMerge.LoadSource(_session.Settings, SpellRecord.FILE,
                                                                   _session.Definitions));
        if (clean == null)
        {
            await Dialogs.MessageAsync(_window, "No clean Spell.dbc", "There's no clean Spell.dbc to compare with. " +
                                                                      "Set the Clean DBC folder or the WoW client " +
                                                                      "folder in Project settings.");
            return null;
        }

        List<int> rows = await Task.Run(() => DbcMerge.Changed(clean, spells));
        _window.SetStatus(rows.Count == 0 ? "No spell differs from the clean Spell.dbc." : string.Empty);
        return rows.Count == 0 ? null : rows;
    }

    private async Task ExportSqlAsync(bool changedOnly)
    {
        if (_spells == null)
            return;
        DbcTable spells = _spells;
        List<int>? rows = await RowsToWriteAsync(spells, changedOnly);
        if (rows == null)
            return;
        string suggested = changedOnly ? "spell_dbc_changed.sql"
                                       : "spell_" + new SpellRecord(spells, rows[0]).Id + ".sql";
        string? path = await Ui.SaveFileAsync(_window, "Save SQL for spell_dbc", suggested, "SQL file", "sql");
        if (path == null)
            return;
        SqlExport.Write(path, spells, rows);
        _window.OpenWritten(path);
        _window.SetStatus("Wrote " + rows.Count + (rows.Count == 1 ? " spell" : " spells") + " to " + path +
                          ". Run it on acore_world and restart the worldserver.");
    }

    // Item and creature names arrive from the world database in the background, then the fields show them
    private async Task ShowWorldNamesAsync()
    {
        await _session.LoadWorldNamesAsync();
        _itemSources = await _session.ModelSourcesAsync();
        _binding.Refresh();
    }

    // Every locale the client has, one Spell.dbc at a time, gathered into the open spells as one undo step
    private async Task FillLanguagesAsync(bool onlyEmpty)
    {
        if (_spells == null)
            return;
        DbcTable spells = _spells;
        string data = _session.Settings.ClientData();
        List<string> locales = LocaleMerge.Order(ClientArchives.FindLocales(data));
        if (locales.Count == 0)
        {
            await Dialogs.MessageAsync(_window, "No locales", "No locale folders were found in the WoW client's " +
                                                              "Data folder. Set the WoW client folder in Project " +
                                                              "settings.");
            return;
        }

        string patch = _session.Settings.PatchPath();
        DbcDefinitions definitions = _session.Definitions;
        List<string> filled = new List<string>();
        spells.BreakUndoStep();
        for (int i = 0; i < locales.Count; ++i)
        {
            string locale = locales[i];
            _window.SetStatus("Reading " + locale + "'s Spell.dbc (" + (i + 1) + " of " + locales.Count + ")...");
            LocaleSource? source;
            try
            {
                source = await Task.Run(() => LocaleMerge.Read(data, locale, SpellRecord.FILE, patch, definitions));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                filled.Add(locale + ": couldn't be read (" + exception.Message + ")");
                continue;
            }

            if (source == null)
                continue;
            int rows = LocaleMerge.Apply(spells, source, onlyEmpty);
            filled.Add(locale + ": " + rows.ToString("N0", CultureInfo.InvariantCulture) + " spells");
        }

        spells.BreakUndoStep();
        _binding.Refresh();
        UpdateHeading();
        _window.SetStatus("Filled in " + string.Join(", ", filled) + ". Save to keep them.");
    }

    // Undoes or redoes the last spell change, and shows the spell it touched
    public void Undo(bool redo)
    {
        if (_spells == null)
            return;
        int row = redo ? _spells.Redo() : _spells.Undo();
        if (row < 0)
        {
            _window.SetStatus(redo ? "Nothing to redo." : "Nothing to undo.");
            return;
        }

        Filter();
        Select(_spells.Key(row));
        _binding.Refresh();
        UpdatePreview();
        _visual.Show(_binding.Row);
        _session.NotifyChanged();
    }

    private static MenuItem Item(string header, Func<Task> action)
    {
        MenuItem item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private async Task SendToWorldAsync(bool changedOnly)
    {
        if (_spells == null)
            return;
        DbcTable spells = _spells;
        List<int>? rows = await RowsToWriteAsync(spells, changedOnly);
        if (rows == null)
            return;
        string what = rows.Count == 1
                          ? "Spell " + new SpellRecord(spells, rows[0]).Id + " (" +
                            new SpellRecord(spells, rows[0]).Name + ")"
                          : rows.Count + " spells";
        WorldDatabaseWindow dialog = new WorldDatabaseWindow(_session.Settings, what);
        MySqlSettings? target = await dialog.ShowDialog<MySqlSettings?>(_window);
        if (target == null)
            return;
        _session.Settings.WorldHost = target.Host;
        _session.Settings.WorldPort = target.Port;
        _session.Settings.WorldUser = target.User;
        _session.Settings.WorldPassword = target.Password;
        _session.Settings.WorldDatabase = target.Database;
        if (!_window.IsTest)
            _session.Settings.Save();
        try
        {
            using MySqlDbcStore world = await MySqlDbcStore.OpenAsync(target, TableNaming.AzerothCore);
            await world.UpsertRowsAsync(spells, rows);
            _window.SetStatus(what + (rows.Count == 1 ? " is" : " are") + " in " + target.Database +
                              ".spell_dbc. Restart the worldserver to load " + (rows.Count == 1 ? "it." : "them."));
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                              System.Data.Common.DbException)
        {
            await Dialogs.MessageAsync(_window, "Not written", "spell_dbc couldn't be updated: " + exception.Message);
        }
    }

    private const double SCHOOL_STRIP_WIDTH = 4;
    private const double LIST_ICON_SIZE = 32;

    // One line per spell (compact), or its icon, name and rank on a taller row with a card on hover (normal)
    private Avalonia.Controls.Templates.FuncDataTemplate<SpellItem> ListTemplate()
    {
        bool compact = _layout == SpellListLayout.Compact;
        return new Avalonia.Controls.Templates.FuncDataTemplate<SpellItem>((_, _) =>
        {
            TextBlock id = Ui.Themed(new TextBlock { Width = 58, FontSize = 12,
                                                     VerticalAlignment = VerticalAlignment.Center },
                                     TextBlock.ForegroundProperty, ThemeColours.MUTED_TEXT);
            TextBlock name = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock rank = Ui.Themed(new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                                       TextBlock.ForegroundProperty, ThemeColours.MUTED_TEXT);
            // A strip in the school's combat text colour, as the game shows it
            Border school = new Border
            {
                Width = SCHOOL_STRIP_WIDTH,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 3, 4, 3)
            };
            school.Bind(Border.BackgroundProperty, new Avalonia.Data.Binding(nameof(SpellItem.SchoolBrush)));
            id.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(SpellItem.Id)));
            name.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(SpellItem.DisplayName)));
            rank.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(SpellItem.Rank)));
            if (compact)
            {
                return new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { school, id, name, rank }
                };
            }

            Image icon = new Image { Width = LIST_ICON_SIZE, Height = LIST_ICON_SIZE };
            icon.Bind(Image.SourceProperty, new Avalonia.Data.Binding(nameof(SpellItem.Icon)));
            Border iconFrame = new Border
            {
                Width = LIST_ICON_SIZE,
                Height = LIST_ICON_SIZE,
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Child = icon
            };
            StackPanel text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { name, Ui.Horizontal(id, rank) }
            };
            StackPanel row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 2),
                Children = { school, iconFrame, text }
            };
            row.Bind(ToolTip.TipProperty, new Avalonia.Data.Binding(nameof(SpellItem.Card)));
            return row;
        });
    }

    // The hover card of a spell in the list: what it is, and its tooltip as the game would show it
    private string SpellCard(uint id)
    {
        int row = Find(_spells, id);
        if (row < 0)
            return string.Empty;
        SpellRecord spell = new SpellRecord(_spells!, row);
        string name = spell.Text("Name", _locale);
        string rank = spell.Text("NameSubtext", _locale);
        string description = SpellText.Render(spell, spell.Text("Description", _locale), _lookups);
        string aura = SpellText.Render(spell, spell.Text("AuraDescription", _locale), _lookups);
        return (name.Length == 0 ? "(no name)" : name) + (rank.Length > 0 ? " (" + rank + ")" : string.Empty) +
               "\nSpell " + id + "  -  " + SpellEnums.Get(SpellEnums.SCHOOL).Describe(spell.U("SchoolMask")) +
               "  -  " + CastTime(spell.U("CastingTimeIndex")) +
               (description.Length > 0 ? "\n\n" + description : string.Empty) +
               (aura.Length > 0 ? "\n\nBuff: " + aura : string.Empty);
    }

    private IImage? ListIcon(uint id)
    {
        int row = Find(_spells, id);
        return row < 0 ? null : _images?.Get(new SpellRecord(_spells!, row).U("SpellIconID"));
    }

    private static readonly Dictionary<string, IBrush> GameBrushes = new Dictionary<string, IBrush>();

    private static IBrush? SchoolBrush(uint mask)
    {
        return GameColours.School(mask) is { } colour ? GameBrush(colour) : null;
    }

    private static IBrush GameBrush(string colour)
    {
        if (!GameBrushes.TryGetValue(colour, out IBrush? brush))
            GameBrushes[colour] = brush = new SolidColorBrush(Color.Parse(colour));
        return brush;
    }

    private sealed class SpellItem : System.ComponentModel.INotifyPropertyChanged
    {
        private uint _id;
        private string _name;
        private string _rank;

        public SpellItem(uint id, string name, string rank)
        {
            _id = id;
            _name = name;
            _rank = rank;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public uint Id
        {
            get => _id;
            set
            {
                _id = value;
                Notify(nameof(Id));
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                Notify(nameof(DisplayName));
            }
        }

        public string DisplayName => _name.Length == 0 ? "(no name)" : _name;

        public IBrush? SchoolBrush { get; set; }

        // Filled in when a row is first shown: the list only makes rows for what's on screen
        public Func<uint, IImage?>? IconFor { get; set; }

        public Func<uint, string>? CardFor { get; set; }

        public IImage? Icon => IconFor?.Invoke(_id);

        public string? Card => CardFor?.Invoke(_id);

        public string Rank
        {
            get => _rank;
            set
            {
                _rank = value;
                Notify(nameof(Rank));
            }
        }

        public override string ToString()
        {
            return Id + " " + Name;
        }

        private void Notify(string property)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(property));
        }
    }
}
