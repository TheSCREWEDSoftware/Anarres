using Anarres.Core.Dbc;
using Anarres.Core.Spells;

namespace Anarres.Core.Tests;

public static class SpellTests
{
    private static SpellRecord NewSpell(out SpellLookups lookups)
    {
        DbcTable spells = new DbcTable(DbcDefinitions.Default.Find(SpellRecord.FILE)!);
        int row = spells.AddRow(100);
        SpellRecord spell = new SpellRecord(spells, row);
        spells.SetInt(row, spell.Column("EffectBasePoints_1"), 9);
        spells.SetInt(row, spell.Column("EffectDieSides_1"), 1);
        spells.SetInt(row, spell.Column("EffectBasePoints_2"), 19);
        spells.SetInt(row, spell.Column("EffectDieSides_2"), 11);
        spells.SetUInt(row, spell.Column("EffectAuraPeriod_1"), 3000);
        spells.SetUInt(row, spell.Column("DurationIndex"), 5);

        DbcTable durations = new DbcTable(DbcDefinitions.Default.Find("SpellDuration.dbc")!);
        int duration = durations.AddRow(5);
        durations.SetInt(duration, 1, 12000);
        lookups = new SpellLookups { Spells = spells, Durations = durations };
        return spell;
    }

    public static void DescribesColumns()
    {
        Assert.True(ColumnDocs.Describe(SpellRecord.FILE, "StartRecoveryCategory")!.Contains("global cooldown"),
                    "global cooldown group");
        Assert.Equal(ColumnDocs.Describe("Spell", "Effect"), ColumnDocs.Describe(SpellRecord.FILE, "Effect_2"),
                     "numbered columns share their base");
        Assert.Equal(ColumnDocs.Describe(SpellRecord.FILE, "Name"),
                     ColumnDocs.Describe(SpellRecord.FILE, "Name_Lang_deDE"), "locales share their base");
        Assert.True(ColumnDocs.Describe(SpellRecord.FILE, "AttributesEx3") != null, "attribute columns");
        Assert.True(ColumnDocs.Describe("SpellRange.dbc", "RangeMax_2") != null, "other spell tables");
        Assert.Equal(null, ColumnDocs.Describe("Item.dbc", "ID"), "unknown file");
        Assert.True(ColumnDocs.Tooltip(SpellRecord.FILE, "Mechanic", "spell_dbc").EndsWith("spell_dbc: Mechanic"),
                    "tooltip names the column");

        DbcDefinition definition = DbcDefinitions.Default.Find(SpellRecord.FILE)!;
        string[] missing = definition.Columns.Select(column => column.Name)
                                     .Where(name => ColumnDocs.Describe(SpellRecord.FILE, name) == null).ToArray();
        Assert.Equal(string.Empty, string.Join(", ", missing), "every Spell.dbc column is described");
    }

    public static void RendersTooltipTokens()
    {
        SpellRecord spell = NewSpell(out SpellLookups lookups);
        Assert.Equal("Deals 10 damage", SpellText.Render(spell, "Deals $s1 damage", lookups), "$s1");
        Assert.Equal("Heals 20 to 30", SpellText.Render(spell, "Heals $s2", lookups), "$s2 with dice");
        Assert.Equal("40 over 12 sec", SpellText.Render(spell, "$o1 over $d", lookups), "$o1 and $d");
        Assert.Equal("every 3 sec", SpellText.Render(spell, "every $t1 sec", lookups), "$t1");
        Assert.Equal("15 damage", SpellText.Render(spell, "${$s1*1.5} damage", lookups), "expression");
        Assert.Equal("10 points", SpellText.Render(spell, "$s1 $lpoint:points;", lookups), "plural");
        Assert.Equal("costs $5", SpellText.Render(spell, "costs $$5", lookups), "escaped dollar");
        Assert.Equal("10", SpellText.Render(spell, "$100s1", lookups), "another spell by ID");
        Assert.Equal("plain", SpellText.Render(spell, "$?s1234[talented][plain]", lookups), "condition");
        Assert.Equal("21 damage", SpellText.Render(spell, "${$s1*$?s5[${3}][${2.1}]} damage", lookups),
                     "condition inside a sum");
    }

    public static void EnumsLoad()
    {
        Assert.Equal("Dummy (3)", SpellEnums.Get(SpellEnums.EFFECT).Describe(3), "effect");
        Assert.True(SpellEnums.Get(SpellEnums.AURA).Values.Count > 300, "auras");
        Assert.True(SpellEnums.Get("Attributes").Flags, "attributes are flags");
        Assert.Equal("Fire, Frost", SpellEnums.Get(SpellEnums.SCHOOL).Describe(4 | 16), "schools");
        Assert.True(SpellEnums.StanceMask().Values.Any(bit => bit.Value == 1), "cat form is bit 0");
    }

    public static void ExpandsDescriptionVariables()
    {
        SpellRecord spell = NewSpell(out SpellLookups lookups);
        DbcTable variables = new DbcTable(DbcDefinitions.Default.Find("SpellDescriptionVariables.dbc")!);
        int row = variables.AddRow(7);
        variables.SetString(row, 1, "$mult=${2*3}\r\n$bonus=$<mult>");
        lookups.Variables = variables;
        spell.Table.SetUInt(spell.Row, spell.Column("SpellDescriptionVariableID"), 7);
        Assert.Equal("60 damage", SpellText.Render(spell, "${$s1*$<mult>} damage", lookups), "variable in a sum");
        Assert.Equal("6", SpellText.Render(spell, "$<bonus>", lookups), "variable naming another");
    }

    public static void SearchesSpells()
    {
        SpellRecord spell = NewSpell(out SpellLookups _);
        DbcTable table = spell.Table;
        table.SetString(spell.Row, spell.Column("Name_Lang_enUS"), "Frost Nova");
        table.SetUInt(spell.Row, spell.Column("Effect_2"), 6);
        Assert.True(SpellSearch.Build(table, "100")(spell.Row), "by ID");
        Assert.True(SpellSearch.Build(table, "nova")(spell.Row), "by part of the name");
        Assert.True(SpellSearch.Build(table, "effect:6")(spell.Row), "by effect in any slot");
        Assert.True(!SpellSearch.Build(table, "effect:2")(spell.Row), "an effect it doesn't have");
        Assert.True(SpellSearch.Build(table, "DurationIndex:5")(spell.Row), "by any column name");
    }

    private static long Value(string table, string name)
    {
        return SpellEnums.Get(table).Values.First(entry => entry.Name == name).Value;
    }

    public static void ExplainsMiscValues()
    {
        (MiscMeaning stat, MiscMeaning _) = SpellMiscValues.For(Value(SpellEnums.EFFECT, "SPELL_EFFECT_APPLY_AURA"),
                                                               Value(SpellEnums.AURA, "SPELL_AURA_MOD_STAT"));
        Assert.Equal("Stat", stat.Label, "stat aura");
        Assert.Equal("Stamina (2)", stat.Values!.Describe(2), "stamina");

        (MiscMeaning creature, MiscMeaning properties) =
            SpellMiscValues.For(Value(SpellEnums.EFFECT, "SPELL_EFFECT_SUMMON"), 0);
        Assert.Equal("Creature entry", creature.Label, "summon");
        Assert.True(properties.Label.StartsWith("Summon properties", StringComparison.Ordinal), "summon B");

        (MiscMeaning school, MiscMeaning _) = SpellMiscValues.For(Value(SpellEnums.EFFECT, "SPELL_EFFECT_APPLY_AURA"),
                                                                 Value(SpellEnums.AURA, "SPELL_AURA_SCHOOL_ABSORB"));
        Assert.Equal("Fire, Frost", school.Values!.Describe(4 | 16), "school mask");
        Assert.Equal(string.Empty, SpellMiscValues.For(Value(SpellEnums.EFFECT, "SPELL_EFFECT_SCHOOL_DAMAGE"), 0)
                                                  .A.Label, "nothing special");
    }

    public static void FallsBackToEnglish()
    {
        SpellRecord spell = NewSpell(out SpellLookups _);
        spell.Table.SetString(spell.Row, spell.Column("Name_Lang_enUS"), "Fireball");
        // The client keeps German in the 4th text column, which AzerothCore names Name_Lang_frFR
        spell.Table.SetString(spell.Row, spell.Column("Name_Lang_frFR"), "Feuerball");
        Assert.Equal("Feuerball", spell.Text("Name", "deDE"), "German");
        Assert.Equal("Fireball", spell.Text("Name", "frFR"), "French falls back");
    }

    // Talent conditions, chained variables, $M, signs and caster level, as the Frostbolt and Blizzard tooltips use
    public static void RendersConditionsAndLevels()
    {
        SpellRecord spell = NewSpell(out SpellLookups lookups);
        DbcTable spells = lookups.Spells!;
        int row = spells.FindRow(100);
        Assert.Equal("10", SpellText.Render(spell, "$?s500[${$m1*2}][$m1]", lookups), "no talent: the last branch");
        TooltipContext talent = new TooltipContext { Known = new HashSet<uint> { 500 } };
        Assert.Equal("20", SpellText.Render(spell, "$?s500[${$m1*2}][$m1]", lookups, talent), "with the talent");
        Assert.Equal("c", SpellText.Render(spell, "$?s1[a]?s2[b][c]", lookups), "a chain falls through");
        Assert.Equal("b", SpellText.Render(spell, "$?s1[a]?s500[b][c]", lookups, talent), "the chain's match");
        Assert.Equal("20 to 30", SpellText.Render(spell, "${$m2} to ${$M2}", lookups), "$M is the largest");

        spells.SetInt(row, spell.Column("EffectBasePoints_1"), -41);
        Assert.Equal("40%", SpellText.Render(spell, "$s1%", lookups), "shown without its sign");

        spells.SetInt(row, spell.Column("EffectBasePoints_1"), 9);
        spells.SetFloat(row, spell.Column("EffectRealPointsPerLevel_1"), 0.5f);
        spells.SetInt(row, spell.Column("BaseLevel"), 4);
        spells.SetInt(row, spell.Column("SpellLevel"), 4);
        spells.SetInt(row, spell.Column("MaxLevel"), 8);
        Assert.Equal("12", SpellText.Render(spell, "$s1", lookups, new TooltipContext { Level = 80 }),
                     "levels above the spell's, up to its maximum (8 - 4) x 0.5 = 2 more");
        Assert.Equal("10", SpellText.Render(spell, "$s1", lookups), "the spell's own level adds nothing");
        Assert.Equal("500", string.Join(",", SpellText.AskedSpells(spell, "$?s500[x][y]", lookups)), "asked talents");
    }
}
