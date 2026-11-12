using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// The named list behind a column in any table; lists read from tables follow custom races, classes and items
public static class ColumnValues
{
    private const string SPELL = "Spell.dbc";

    // Spell.dbc columns (without their _1, _2 numbers) and their list
    private static readonly Dictionary<string, Func<EnumTable>> SpellColumns =
        new Dictionary<string, Func<EnumTable>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SchoolMask"] = () => SpellEnums.Get(SpellEnums.SCHOOL),
            ["DispelType"] = () => SpellEnums.Get(SpellEnums.DISPEL),
            ["Mechanic"] = () => SpellEnums.Get(SpellEnums.MECHANIC),
            ["EffectMechanic"] = () => SpellEnums.Get(SpellEnums.MECHANIC),
            ["DefenseType"] = () => SpellEnums.Get(SpellEnums.DAMAGE_CLASS),
            ["PreventionType"] = () => SpellEnums.Get(SpellEnums.PREVENTION_TYPE),
            ["SpellClassSet"] = () => SpellEnums.Get(SpellEnums.SPELL_FAMILY),
            ["PowerType"] = () => SpellEnums.Get(SpellEnums.POWER),
            ["ImplicitTargetA"] = () => SpellEnums.Get(SpellEnums.TARGET),
            ["ImplicitTargetB"] = () => SpellEnums.Get(SpellEnums.TARGET),
            ["Effect"] = () => SpellEnums.Get(SpellEnums.EFFECT),
            ["EffectAura"] = () => SpellEnums.Get(SpellEnums.AURA),
            ["Targets"] = () => SpellEnums.Get(SpellEnums.CAST_TARGET_FLAGS),
            ["TargetCreatureType"] = SpellEnums.CreatureTypeMask,
            ["ShapeshiftMask"] = SpellEnums.StanceMask,
            ["ShapeshiftExclude"] = SpellEnums.StanceMask,
            ["CasterAuraState"] = () => SpellEnums.Get(SpellEnums.AURA_STATE),
            ["TargetAuraState"] = () => SpellEnums.Get(SpellEnums.AURA_STATE),
            ["ExcludeCasterAuraState"] = () => SpellEnums.Get(SpellEnums.AURA_STATE),
            ["ExcludeTargetAuraState"] = () => SpellEnums.Get(SpellEnums.AURA_STATE),
            ["ProcTypeMask"] = () => SpellEnums.Get(SpellEnums.PROC_FLAGS),
            ["InterruptFlags"] = () => SpellEnums.Get(SpellEnums.INTERRUPT_FLAGS),
            ["AuraInterruptFlags"] = () => SpellEnums.Get(SpellEnums.AURA_INTERRUPT_FLAGS),
            ["ChannelInterruptFlags"] = () => SpellEnums.Get(SpellEnums.CHANNEL_INTERRUPT_FLAGS),
            ["EquippedItemInvTypes"] = InventoryTypeMask
        };

    // The slots an item can go in (InventoryType); a spell's EquippedItemInvTypes has bit N for slot N
    private static readonly string[] InventoryTypes =
    {
        "Non-equippable", "Head", "Neck", "Shoulder", "Shirt", "Chest", "Waist", "Legs", "Feet", "Wrists", "Hands",
        "Finger", "Trinket", "One-hand weapon", "Shield", "Ranged (bow)", "Back", "Two-hand weapon", "Bag", "Tabard",
        "Robe", "Main hand", "Off hand", "Held in off hand", "Ammo", "Thrown", "Ranged (gun, wand)", "Quiver", "Relic"
    };

    public static EnumTable InventoryTypeMask()
    {
        return new EnumTable("InventoryTypes", true,
                             InventoryTypes.Select((name, slot) => new EnumValue(1L << slot, "INVTYPE_" + slot, name,
                                                                                 string.Empty)).ToList());
    }

    // The list for a column of a row; row matters where it depends on another column (item subclasses on the class)
    public static EnumTable? For(DbcTable table, int column, int row, Func<string, DbcTable?> tables)
    {
        DbcDefinition definition = table.Definition;
        string name = definition.Columns[column].Name;
        string bare = System.Text.RegularExpressions.Regex.Replace(name, @"_\d+$", string.Empty);
        bool spell = string.Equals(Path.GetFileName(definition.File), SPELL, StringComparison.OrdinalIgnoreCase);
        if (spell && bare.StartsWith("Attributes", StringComparison.OrdinalIgnoreCase) &&
            SpellEnums.AttributeColumns.Contains(bare, StringComparer.OrdinalIgnoreCase))
            return SpellEnums.Get(bare);
        if (spell && SpellColumns.TryGetValue(bare, out Func<EnumTable>? list))
            return list();
        if (spell && bare.Equals("EquippedItemClass", StringComparison.OrdinalIgnoreCase))
            return ItemClasses(tables);
        if (spell && bare.Equals("EquippedItemSubclass", StringComparison.OrdinalIgnoreCase))
        {
            int classColumn = definition.IndexOf("EquippedItemClass");
            return classColumn < 0 || row < 0 ? null : ItemSubclasses(tables, table.GetInt(row, classColumn));
        }

        if (name.Contains("RaceMask", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AllowableRace", StringComparison.OrdinalIgnoreCase))
            return MaskFrom(tables("ChrRaces.dbc"), "Races");
        if (name.Contains("ClassMask", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Spell", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AllowableClass", StringComparison.OrdinalIgnoreCase))
            return MaskFrom(tables("ChrClasses.dbc"), "Classes");
        if (name.EndsWith("SchoolMask", StringComparison.OrdinalIgnoreCase))
            return SpellEnums.Get(SpellEnums.SCHOOL);
        return null;
    }

    // Whether For can give a list for this column at all, so a grid knows which columns to annotate
    public static bool Has(DbcDefinition definition, int column)
    {
        string name = definition.Columns[column].Name;
        string bare = System.Text.RegularExpressions.Regex.Replace(name, @"_\d+$", string.Empty);
        bool spell = string.Equals(Path.GetFileName(definition.File), SPELL, StringComparison.OrdinalIgnoreCase);
        return spell && (SpellColumns.ContainsKey(bare) || SpellEnums.AttributeColumns.Contains(bare) ||
                         bare is "EquippedItemClass" or "EquippedItemSubclass") ||
               name.Contains("RaceMask", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AllowableRace", StringComparison.OrdinalIgnoreCase) ||
               (name.Contains("ClassMask", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Spell", StringComparison.OrdinalIgnoreCase)) ||
               name.Equals("AllowableClass", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("SchoolMask", StringComparison.OrdinalIgnoreCase);
    }

    // Item classes by ID from ItemClass.dbc
    public static EnumTable? ItemClasses(Func<string, DbcTable?> tables)
    {
        DbcTable? classes = tables("ItemClass.dbc");
        int id = classes?.Definition.IndexOf("ClassID") ?? -1;
        int name = classes == null ? -1 : SpellRecord.TextColumn(classes.Definition, "ClassName", SpellRecord.ENGLISH);
        if (classes == null || id < 0 || name < 0)
            return null;
        List<EnumValue> values = new List<EnumValue> { new EnumValue(-1, "NONE", "No item needed", string.Empty) };
        for (int row = 0; row < classes.Rows.Count; ++row)
            values.Add(new EnumValue(classes.GetInt(row, id), "ITEM_CLASS", classes.GetString(row, name),
                                     string.Empty));
        return new EnumTable("ItemClasses", false, values);
    }

    // One class's subclasses from ItemSubClass.dbc, as mask bits (bit N is subclass N)
    public static EnumTable? ItemSubclasses(Func<string, DbcTable?> tables, int itemClass)
    {
        DbcTable? subclasses = tables("ItemSubClass.dbc");
        if (subclasses == null || itemClass < 0)
            return null;
        DbcDefinition definition = subclasses.Definition;
        int classColumn = definition.IndexOf("ClassID");
        int subclassColumn = definition.IndexOf("SubClassID");
        int verbose = SpellRecord.TextColumn(definition, "VerboseName", SpellRecord.ENGLISH);
        int display = SpellRecord.TextColumn(definition, "DisplayName", SpellRecord.ENGLISH);
        if (classColumn < 0 || subclassColumn < 0 || display < 0)
            return null;
        List<EnumValue> values = new List<EnumValue>();
        for (int row = 0; row < subclasses.Rows.Count; ++row)
        {
            int subclass = subclasses.GetInt(row, subclassColumn);
            if (subclasses.GetInt(row, classColumn) != itemClass || subclass < 0 || subclass > 31)
                continue;
            string label = verbose >= 0 && subclasses.GetString(row, verbose).Length > 0
                               ? subclasses.GetString(row, verbose)
                               : subclasses.GetString(row, display);
            values.Add(new EnumValue(1L << subclass, "SUBCLASS_" + subclass, label, string.Empty));
        }

        return new EnumTable("ItemSubclasses", true, values.OrderBy(value => value.Value).ToList());
    }

    // A mask whose bit N is row ID N + 1 of a table with names, such as races and classes
    private static EnumTable? MaskFrom(DbcTable? table, string key)
    {
        if (table == null)
            return null;
        int name = SpellRecord.TextColumn(table.Definition, "Name", SpellRecord.ENGLISH);
        if (name < 0)
            return null;
        List<EnumValue> values = new List<EnumValue>();
        for (int row = 0; row < table.Rows.Count; ++row)
        {
            uint id = table.Key(row);
            if (id >= 1 && id <= 32)
                values.Add(new EnumValue(1L << (int)(id - 1), key.ToUpperInvariant() + "_" + id,
                                         table.GetString(row, name), string.Empty));
        }

        return new EnumTable(key, true, values.OrderBy(value => value.Value).ToList());
    }
}
