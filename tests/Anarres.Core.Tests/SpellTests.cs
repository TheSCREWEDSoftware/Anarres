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
}
