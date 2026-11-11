using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// One row of Spell.dbc read and written by column name; names are AzerothCore's spell_dbc columns
public readonly struct SpellRecord
{
    public const string FILE = "Spell.dbc";
    public const int EFFECT_COUNT = 3;
    public const int REAGENT_COUNT = 8;
    public const string ENGLISH = "enUS";

    public static readonly string[] Locales =
    {
        "enUS", "enGB", "koKR", "frFR", "deDE", "enCN", "zhCN", "enTW", "zhTW", "esES", "esMX", "ruRU", "ptPT",
        "ptBR", "itIT", "Unk"
    };

    public SpellRecord(DbcTable table, int row)
    {
        Table = table;
        Row = row;
    }

    public DbcTable Table { get; }

    public int Row { get; }

    public uint Id => U("ID");

    public string Name => S("Name_Lang_enUS");

    public string Rank => S("NameSubtext_Lang_enUS");

    public string Description => S("Description_Lang_enUS");

    public string AuraDescription => S("AuraDescription_Lang_enUS");

    public uint DurationIndex => U("DurationIndex");

    public uint RangeIndex => U("RangeIndex");

    public uint ProcCharges => U("ProcCharges");

    public uint ProcChance => U("ProcChance");

    public uint StackAmount => U("CumulativeAura");

    public uint MaxTargets => U("MaxTargets");

    public static string EffectColumn(string name, int effect)
    {
        return name + "_" + (effect + 1);
    }

    public uint Effect(int effect)
    {
        return U(EffectColumn("Effect", effect));
    }

    public int EffectBasePoints(int effect)
    {
        return I(EffectColumn("EffectBasePoints", effect));
    }

    public int EffectDieSides(int effect)
    {
        return I(EffectColumn("EffectDieSides", effect));
    }

    public uint EffectAuraPeriod(int effect)
    {
        return U(EffectColumn("EffectAuraPeriod", effect));
    }

    public uint EffectRadiusIndex(int effect)
    {
        return U(EffectColumn("EffectRadiusIndex", effect));
    }

    public uint EffectChainTargets(int effect)
    {
        return U(EffectColumn("EffectChainTargets", effect));
    }

    public float EffectMultipleValue(int effect)
    {
        return F(EffectColumn("EffectMultipleValue", effect));
    }

    public int EffectMiscValue(int effect)
    {
        return I(EffectColumn("EffectMiscValue", effect));
    }

    public float EffectPointsPerCombo(int effect)
    {
        return F(EffectColumn("EffectPointsPerCombo", effect));
    }

    public int Column(string name)
    {
        int index = Table.Definition.IndexOf(name);
        if (index < 0)
            throw new KeyNotFoundException("Spell.dbc has no column " + name + ".");
        return index;
    }

    public uint U(string name)
    {
        return Table.GetUInt(Row, Column(name));
    }

    public int I(string name)
    {
        return Table.GetInt(Row, Column(name));
    }

    public float F(string name)
    {
        return Table.GetFloat(Row, Column(name));
    }

    public string S(string name)
    {
        return Table.GetString(Row, Column(name));
    }
}
