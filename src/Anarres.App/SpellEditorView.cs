using System.Globalization;
using Anarres.Core.Dbc;
using Anarres.Core.Export;
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
    private readonly TextBox _search = new TextBox { Watermark = "Search by ID or name, or effect:6, aura:3" };
    private readonly ListBox _list = new ListBox();
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

    public SpellEditorView(MainWindow window, Session session)
    {
        _window = window;
        _session = session;
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
            _spells = await _session.LoadAsync(SpellRecord.FILE);
            _lookups.Spells = _spells;
            _lookups.Durations = await _session.TryLoadAsync("SpellDuration.dbc");
            _lookups.Radii = await _session.TryLoadAsync("SpellRadius.dbc");
            _lookups.Ranges = await _session.TryLoadAsync("SpellRange.dbc");
            _lookups.Variables = await _session.TryLoadAsync("SpellDescriptionVariables.dbc");
            CastTimes = await _session.TryLoadAsync("SpellCastTimes.dbc");
            Icons = await _session.TryLoadAsync("SpellIcon.dbc");
            _images = new IconImages(await _session.IconLibraryAsync(), Icons);
            await _visual.LoadAsync(_spells);
            BuildEditor(_spells.Definition);
            Filter();
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

    public void Select(uint id)
    {
        if (_spells == null)
            return;
        _search.Text = id.ToString(CultureInfo.InvariantCulture);
        Filter();
        if (_list.ItemCount > 0)
            _list.SelectedIndex = 0;
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
        Button sql = new Button { Content = "SQL..." };
        sql.Flyout = new MenuFlyout
        {
            Items =
            {
                Item("This spell", () => ExportSqlAsync(changedOnly: false)),
                Item("Every spell changed from the clean Spell.dbc", () => ExportSqlAsync(changedOnly: true))
            }
        };
        Button world = Ui.AsyncButton("Send to world DB...", SendToWorldAsync);
        ToolTip.SetTip(save, "Save every changed spell to the project (Ctrl+S)");
        ToolTip.SetTip(revert, "Drop changes not saved yet and load the saved spells again");
        ToolTip.SetTip(sql, "Write spells as SQL for AzerothCore's spell_dbc table");
        ToolTip.SetTip(world, "Write this spell straight into spell_dbc in your AzerothCore world database");

        StackPanel heading = Ui.Horizontal(_headerIcon, Ui.Vertical(2, _title, _subtitle));
        StackPanel actions = Ui.Horizontal(_unsaved, sql, world, revert, save);
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
        _tabs.Items.Add(Tab("Targets & Requirements", TargetsTab()));
        _tabs.Items.Add(Tab("Procs & Interrupts", ProcsTab()));
        _tabs.Items.Add(Tab("Items & Reagents", ItemsTab()));
        _tabs.Items.Add(Tab("Visual", _visual));
        _tabs.Items.Add(Tab("Localisation", LocalesTab()));
        _tabs.Items.Add(new TabItem { Header = "All fields", Content = new AllFieldsView(_binding, definition).View });
        _tabs.SelectedIndex = Math.Clamp(selected, 0, _tabs.ItemCount - 1);
        _editor.Content = _tabs;
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

    private Control Lookup(string label, string column, Func<uint, string> describe)
    {
        return Named(Ui.Row(label, new LookupField(_binding, column, describe).View), column);
    }

    // Fields show readable labels; the tooltip gives the column's name in spell_dbc and Spell.dbc
    private static Control Named(Control row, params string[] columns)
    {
        ToolTip.SetTip(row, "spell_dbc: " + string.Join(", ", columns));
        return row;
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
                     Named(Ui.Row("Family mask", new CellField(_binding, "SpellClassMask_1", 110).Box,
                                  new CellField(_binding, "SpellClassMask_2", 110).Box,
                                  new CellField(_binding, "SpellClassMask_3", 110).Box),
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
        StackPanel rows = new StackPanel { Spacing = 6 };
        for (int index = 0; index < SpellEnums.AttributeColumns.Length; ++index)
        {
            string column = SpellEnums.AttributeColumns[index];
            rows.Children.Add(Flags("Attributes " + index, column, SpellEnums.Get(column)));
        }

        return Ui.Group("Attributes (press ... to pick the bits)", rows);
    }

    private Control Flags(string label, string column, EnumTable table)
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
                     Cell("Misc value", Column("EffectMiscValue")),
                     Cell("Misc value B", Column("EffectMiscValueB")),
                     Lookup("Trigger spell", Column("EffectTriggerSpell"), SpellName),
                     Cell("Item created", Column("EffectItemType")),
                     Named(Ui.Row("Affected family mask",
                                  new CellField(_binding, Column("EffectSpellClassMaskA"), 110).Box,
                                  new CellField(_binding, Column("EffectSpellClassMaskB"), 110).Box,
                                  new CellField(_binding, Column("EffectSpellClassMaskC"), 110).Box),
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
            reagents.Children.Add(Named(Ui.Row("Reagent " + reagent,
                                               new CellField(_binding, "Reagent_" + reagent, 110).Box,
                                               new TextBlock { Text = "x", Classes = { "label" } },
                                               new CellField(_binding, "ReagentCount_" + reagent, 60).Box),
                                        "Reagent_" + reagent, "ReagentCount_" + reagent));
        }

        return Ui.Vertical(0,
            Ui.Group("Reagents (item ID x count)", reagents),
            Ui.Group("Tools",
                     Cell("Totem 1", "Totem_1"),
                     Cell("Totem 2", "Totem_2"),
                     Cell("Tool category 1", "RequiredTotemCategoryID_1"),
                     Cell("Tool category 2", "RequiredTotemCategoryID_2")),
            Ui.Group("Equipped item",
                     Cell("Item class", "EquippedItemClass"),
                     Cell("Item subclass mask", "EquippedItemSubclass"),
                     Cell("Inventory type mask", "EquippedItemInvTypes"),
                     Ui.Hint("-1 in Item class means no item is needed.")));
    }

    private Control LocalesTab()
    {
        StackPanel panel = new StackPanel();
        foreach (string prefix in new[] { "Name", "NameSubtext", "Description", "AuraDescription" })
        {
            StackPanel rows = new StackPanel();
            foreach (string locale in SpellRecord.Locales)
            {
                bool longText = prefix is "Description" or "AuraDescription";
                rows.Children.Add(Named(Ui.Row(locale, new CellField(_binding, prefix + "_Lang_" + locale,
                                                                     longText ? 640 : 360).Box),
                                        prefix + "_Lang_" + locale));
            }

            rows.Children.Add(Named(Ui.Row("Mask", new CellField(_binding, prefix + "_Lang_Mask", 160).Box),
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
        int nameColumn = table.Definition.IndexOf("Name_Lang_enUS");
        int rankColumn = table.Definition.IndexOf("NameSubtext_Lang_enUS");
        string query = (_search.Text ?? string.Empty).Trim();
        Func<int, bool> matches = SpellSearch.Build(table, query);
        List<SpellItem> items = new List<SpellItem>();
        for (int row = 0; row < table.Rows.Count; ++row)
        {
            if (matches(row))
                items.Add(new SpellItem(table.Key(row), table.GetString(row, nameColumn),
                                        table.GetString(row, rankColumn)));
        }

        uint? selected = (_list.SelectedItem as SpellItem)?.Id;
        _list.ItemsSource = items;
        _count.Text = items.Count.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                      table.Rows.Count.ToString("N0", CultureInfo.InvariantCulture) + " spells";
        if (selected is { } id && items.FindIndex(item => item.Id == id) is var index and >= 0)
            _list.SelectedIndex = index;
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
        _title.Text = spell.Name.Length == 0 ? "(no name)" : spell.Name;
        _subtitle.Text = "Spell " + spell.Id.ToString(CultureInfo.InvariantCulture) +
                         (spell.Rank.Length > 0 ? "  -  " + spell.Rank : string.Empty) + "  -  " +
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
        _preview.Text = SpellText.Render(spell, spell.Description, _lookups);
        _auraPreview.Text = SpellText.Render(spell, spell.AuraDescription, _lookups);
    }

    private void Edited()
    {
        UpdateHeading();
        if (_list.SelectedItem is SpellItem item && _binding.IsBound)
        {
            SpellRecord spell = new SpellRecord(_spells!, _binding.Row);
            if (item.Name != spell.Name || item.Rank != spell.Rank || item.Id != spell.Id)
            {
                item.Name = spell.Name;
                item.Rank = spell.Rank;
                item.Id = spell.Id;
            }
        }

        _session.NotifyChanged();
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
        if (!await Dialogs.ConfirmAsync(_window, "Delete spell", "Delete spell " + spell.Id + " (" + spell.Name +
                                                                 ")? It's removed from the project when you save.",
                                        "Delete"))
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

    private async Task ExportSqlAsync(bool changedOnly)
    {
        if (_spells == null || (!changedOnly && !_binding.IsBound))
            return;
        DbcTable spells = _spells;
        List<int> rows;
        string suggested;
        if (changedOnly)
        {
            _window.SetStatus("Comparing with the clean Spell.dbc...");
            DbcTable? clean = await Task.Run(() => DbcMerge.LoadSource(_session.Settings, SpellRecord.FILE,
                                                                       _session.Definitions));
            if (clean == null)
            {
                await Dialogs.MessageAsync(_window, "No clean Spell.dbc", "There's no clean Spell.dbc to compare " +
                                                                          "with. Set the Clean DBC folder or the " +
                                                                          "WoW Data folder in Project settings.");
                return;
            }

            rows = await Task.Run(() => DbcMerge.Changed(clean, spells));
            suggested = "spell_dbc_changed.sql";
        }
        else
        {
            rows = new List<int> { _binding.Row };
            suggested = "spell_" + new SpellRecord(spells, _binding.Row).Id + ".sql";
        }

        if (rows.Count == 0)
        {
            _window.SetStatus("No spell differs from the clean Spell.dbc.");
            return;
        }

        string? path = await Ui.SaveFileAsync(_window, "Save SQL for spell_dbc", suggested, "SQL file", "sql");
        if (path == null)
            return;
        SqlExport.Write(path, spells, rows);
        _window.SetStatus("Wrote " + rows.Count + (rows.Count == 1 ? " spell" : " spells") + " to " + path +
                          ". Run it on acore_world and restart the worldserver.");
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

    private async Task SendToWorldAsync()
    {
        if (_spells == null || !_binding.IsBound)
            return;
        SpellRecord spell = new SpellRecord(_spells, _binding.Row);
        WorldDatabaseWindow dialog = new WorldDatabaseWindow(_session.Settings, spell.Id, spell.Name);
        MySqlSettings? target = await dialog.ShowDialog<MySqlSettings?>(_window);
        if (target == null)
            return;
        try
        {
            using MySqlDbcStore world = await MySqlDbcStore.OpenAsync(target, TableNaming.AzerothCore);
            await world.UpsertRowsAsync(_spells, new[] { _binding.Row });
            _window.SetStatus("Spell " + spell.Id + " is in " + target.Database +
                              ".spell_dbc. Restart the worldserver to load it.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                              System.Data.Common.DbException)
        {
            await Dialogs.MessageAsync(_window, "Not sent", "The spell couldn't be written: " + exception.Message);
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
