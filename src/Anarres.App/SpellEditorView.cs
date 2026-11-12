using System.Globalization;
using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Anarres.Core.Storage;
using Avalonia;
using Avalonia.Controls;
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
    private readonly TextBlock _unsaved = new TextBlock { Text = "Unsaved changes", IsVisible = false,
                                                          VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _editor = new ContentControl();
    private readonly SelectableTextBlock _preview = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock _auraPreview = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _searchTimer;
    private readonly Image _headerIcon = new Image { Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Image _lookIcon = new Image { Width = 32, Height = 32 };
    private readonly SpellVisualView _visual;
    private DbcTable? _spells;
    private TabControl? _tabs;
    private IconImages? _images;
    private Border? _previewBox;
    private string _locale = SpellRecord.ENGLISH;
    private bool _showRank = true;
    private bool _showPreview = true;
    private bool _confirmDeletes = true;
    // The spell to show when Spell.dbc first loads, from the last run
    private uint? _reopen;

    public SpellEditorView(MainWindow window, Session session)
    {
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

    public void UseOptions(AppOptions options)
    {
        bool listChanged = _locale != options.DisplayLocale || _showRank != options.ShowRankInList;
        _locale = options.DisplayLocale;
        _showRank = options.ShowRankInList;
        _showPreview = options.ShowTooltipPreview;
        _confirmDeletes = options.ConfirmDeletes;
        if (_spells == null)
            _reopen = options.ReopenLastSpell && options.LastSpellId > 0 ? options.LastSpellId : null;
        if (_previewBox != null)
            _previewBox.IsVisible = _showPreview;
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

        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpellItem>((_, _) =>
        {
            TextBlock id = Ui.Themed(new TextBlock { Width = 58, FontSize = 12,
                                                     VerticalAlignment = VerticalAlignment.Center },
                                     TextBlock.ForegroundProperty, ThemeColours.MUTED_TEXT);
            TextBlock name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            TextBlock rank = Ui.Themed(new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                                       TextBlock.ForegroundProperty, ThemeColours.MUTED_TEXT);
            id.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(SpellItem.Id)));
            name.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(SpellItem.DisplayName)));
            rank.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(SpellItem.Rank)));
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { id, name, rank } };
        });

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

        StackPanel heading = Ui.Horizontal(_headerIcon, Ui.Vertical(2, _title, _subtitle));
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
        StackPanel actions = Ui.Horizontal(_finder.Box, _unsaved, tools, sql, world, revert, save);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.VerticalAlignment = VerticalAlignment.Center;
        Grid header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 12) };
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
        _tabs = new TabControl { Margin = new Thickness(12, 0, 12, 12) };
        _tabs.Items.Add(Tab("Base", BaseTab()));
        _tabs.Items.Add(Tab("Attributes", AttributesTab()));
        _tabs.Items.Add(Tab("Effects", EffectsTab()));
        _tabs.Items.Add(Tab("Targets and Requirements", TargetsTab()));
        _tabs.Items.Add(Tab("Procs and Interrupts", ProcsTab()));
        _tabs.Items.Add(Tab("Items and Reagents", ItemsTab()));
        _tabs.Items.Add(Tab("Skill", new SkillView(_binding, _session, _window).View));
        _tabs.Items.Add(Tab("Visual", _visual));
        _tabs.Items.Add(Tab("Localisation", LocalesTab()));
        _tabs.Items.Add(new TabItem { Header = "All fields", Content = new AllFieldsView(_binding, definition).View });
        _tabs.SelectedIndex = Math.Clamp(selected, 0, _tabs.ItemCount - 1);
        _editor.Content = _tabs;
        _finder.Forget();
    }

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

        Border previewBox = Ui.Themed(new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Child = Ui.Vertical(6, Ui.Hint("Tooltip preview"), _preview, Ui.Hint("Buff preview"), _auraPreview)
        }, Border.BackgroundProperty, ThemeColours.RAISED);
        previewBox.IsVisible = _showPreview;
        _previewBox = previewBox;

        return Ui.Vertical(0,
            Ui.Group("Name and text",
                     Named(Ui.Row("Name", name.Box), "Name_Lang_enUS"),
                     Named(Ui.Row("Rank", rank.Box), "NameSubtext_Lang_enUS"),
                     Named(Ui.Label("Description ($s1, $d, $o1 and the rest are filled in below)", 500),
                           "Description_Lang_enUS"),
                     description.Box,
                     Named(Ui.Label("Aura description (shown on the buff)", 500), "AuraDescription_Lang_enUS"),
                     aura.Box, previewBox),
            Ui.Group("Spell",
                     Cell("ID", "ID"),
                     Flags("School", "SchoolMask", SpellEnums.Get(SpellEnums.SCHOOL)),
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
        return Named(Ui.Row("Icon", _lookIcon, icon.View, pick), "SpellIconID");
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

    private Control Flags(string label, string column, EnumTable table)
    {
        return Flags(label, column, () => table);
    }

    private Control Flags(string label, string column, Func<EnumTable?> table)
    {
        StackPanel row = Ui.Row(label, new FlagsField(_binding, column, table).View);
        row.Children[0].VerticalAlignment = VerticalAlignment.Top;
        row.Children[0].Margin = new Thickness(0, 6, 0, 0);
        return Named(row, column);
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
                     Lookup("Item created", Column("EffectItemType"), ItemName),
                     Named(Ui.Row("Affected family mask",
                                  new CellField(_binding, Column("EffectSpellClassMaskA"), Ui.FIELD_WIDTH).Box,
                                  new CellField(_binding, Column("EffectSpellClassMaskB"), Ui.FIELD_WIDTH).Box,
                                  new CellField(_binding, Column("EffectSpellClassMaskC"), Ui.FIELD_WIDTH).Box),
                           Column("EffectSpellClassMaskA"), Column("EffectSpellClassMaskB"),
                           Column("EffectSpellClassMaskC"))));
    }

    private Control TargetsTab()
    {
        return Ui.Vertical(0,
            Ui.Group("Target flags (Targets)",
                     new FlagsField(_binding, "Targets", SpellEnums.Get(SpellEnums.CAST_TARGET_FLAGS)).View),
            Ui.Group("Creature types (TargetCreatureType)",
                     new FlagsField(_binding, "TargetCreatureType", SpellEnums.CreatureTypeMask()).View),
            Ui.Group("Shapeshift forms that can cast (ShapeshiftMask)",
                     new FlagsField(_binding, "ShapeshiftMask", SpellEnums.StanceMask()).View),
            Ui.Group("Shapeshift forms that can't cast (ShapeshiftExclude)",
                     new FlagsField(_binding, "ShapeshiftExclude", SpellEnums.StanceMask()).View),
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
                     Cell("Charges", "ProcCharges"),
                     new FlagsField(_binding, "ProcTypeMask", SpellEnums.Get(SpellEnums.PROC_FLAGS)).View),
            Ui.Group("Interrupted by (InterruptFlags)",
                     new FlagsField(_binding, "InterruptFlags", SpellEnums.Get(SpellEnums.INTERRUPT_FLAGS)).View),
            Ui.Group("Aura removed by (AuraInterruptFlags)",
                     new FlagsField(_binding, "AuraInterruptFlags",
                                    SpellEnums.Get(SpellEnums.AURA_INTERRUPT_FLAGS)).View),
            Ui.Group("Channel interrupted by (ChannelInterruptFlags)",
                     new FlagsField(_binding, "ChannelInterruptFlags",
                                    SpellEnums.Get(SpellEnums.CHANNEL_INTERRUPT_FLAGS)).View));
    }

    private Control ItemsTab()
    {
        StackPanel reagents = new StackPanel();
        for (int reagent = 1; reagent <= SpellRecord.REAGENT_COUNT; ++reagent)
        {
            LookupField item = new LookupField(_binding, "Reagent_" + reagent, ItemName);
            item.View.Children.Insert(1, new TextBlock { Text = "x", Classes = { "label" },
                                                         VerticalAlignment = VerticalAlignment.Center });
            item.View.Children.Insert(2, new CellField(_binding, "ReagentCount_" + reagent, Ui.FIELD_WIDTH).Box);
            reagents.Children.Add(Named(Ui.Row("Reagent " + reagent, item.View),
                                        "Reagent_" + reagent, "ReagentCount_" + reagent));
        }

        return Ui.Vertical(0,
            Ui.Group("Reagents (item ID x count)", reagents),
            Ui.Group("Tools",
                     Lookup("Totem 1", "Totem_1", ItemName),
                     Lookup("Totem 2", "Totem_2", ItemName),
                     Lookup("Tool category 1", "RequiredTotemCategoryID_1", ToolCategory),
                     Lookup("Tool category 2", "RequiredTotemCategoryID_2", ToolCategory),
                     Ui.Hint("Item names come from item_template in the world database (Project settings > " +
                             "Apply), when it can be reached.")),
            Ui.Group("Equipped item",
                     Lookup("Item class", "EquippedItemClass",
                            value => ColumnValues.ItemClasses(_session.NamingTable)?.Names((int)value) ?? string.Empty),
                     Flags("Item subclass mask", "EquippedItemSubclass",
                           () => _binding.IsBound
                                     ? ColumnValues.ItemSubclasses(_session.NamingTable,
                                                                   new SpellRecord(_binding.Table!, _binding.Row)
                                                                       .I("EquippedItemClass"))
                                     : null),
                     Flags("Inventory type mask", "EquippedItemInvTypes", ColumnValues.InventoryTypeMask()),
                     Ui.Hint("-1 in Item class means no item is needed. The subclasses listed are the class's, from " +
                             "ItemSubClass.dbc, so custom ones show too.")));
    }

    private Control LocalesTab()
    {
        StackPanel panel = new StackPanel();
        CheckBox onlyEmpty = new CheckBox { Content = "Only fill text that's empty here", IsChecked = true };
        Button fill = Ui.AsyncButton("Fill in every language from the client",
                                     () => FillLanguagesAsync(onlyEmpty.IsChecked == true));
        ToolTip.SetTip(fill, "Reads Spell.dbc from each locale folder in the WoW client's Data folder and copies its " +
                             "language into these columns, for every spell. Save afterwards to keep it; Undo takes " +
                             "it back.");
        panel.Children.Add(Ui.Group("Languages",
                                    Ui.Hint("The 3.3.5a client reads 9 of the 16 columns, one per language, in the " +
                                            "order below. AzerothCore names the columns after a later list, so the " +
                                            "!'s column names differ: German is Name_Lang_frFR, and the 10th " +
                                            "column, unused, is Name_Lang_esES."),
                                    Ui.Hint("Each locale's client only has its own language, so a project " +
                                            "imported from one has the others empty."),
                                    Ui.Horizontal(fill, onlyEmpty)));
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
                rows.Children.Add(Named(Ui.Row(label, new CellField(_binding, column, longText ? 640 : 360).Box),
                                        column));
            }

            rows.Children.Add(Named(Ui.Row("Mask", new CellField(_binding, prefix + "_Lang_Mask").Box),
                                    prefix + "_Lang_Mask"));
            panel.Children.Add(Ui.Group(prefix, rows));
        }

        return panel;
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
        string query = (_search.Text ?? string.Empty).Trim();
        Func<int, bool> matches = SpellSearch.Build(table, query);
        List<SpellItem> items = new List<SpellItem>();
        for (int row = 0; row < table.Rows.Count; ++row)
        {
            if (!matches(row))
                continue;
            string name = Localised(table, row, nameColumn, englishName);
            string rank = _showRank ? Localised(table, row, rankColumn, englishRank) : string.Empty;
            items.Add(new SpellItem(table.Key(row), name, rank));
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
                         (rank.Length > 0 ? "  -  " + rank : string.Empty) + "  -  " +
                         SpellEnums.Get(SpellEnums.SCHOOL).Describe(spell.U("SchoolMask"));
        _unsaved.IsVisible = _session.ModifiedTables.Any();
        _headerIcon.Source = _images?.Get(spell.U("SpellIconID"));
        _lookIcon.Source = _headerIcon.Source;
    }

    private void UpdatePreview()
    {
        if (!_binding.IsBound)
            return;
        SpellRecord spell = new SpellRecord(_spells!, _binding.Row);
        _preview.Text = SpellText.Render(spell, spell.Text("Description", _locale), _lookups);
        _auraPreview.Text = SpellText.Render(spell, spell.Text("AuraDescription", _locale), _lookups);
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
