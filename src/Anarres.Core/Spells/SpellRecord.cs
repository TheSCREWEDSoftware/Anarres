using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// One row of Spell.dbc read and written by column name; names are AzerothCore's spell_dbc columns
public readonly struct SpellRecord
{
    public const string FILE = "Spell.dbc";
    public const int EFFECT_COUNT = 3;
    public const int REAGENT_COUNT = 8;
    public const string ENGLISH = "enUS";

    // AzerothCore's names for the 16 text columns of each field; they don't match what the client keeps in them
    public static readonly string[] Locales =
    {
        "enUS", "enGB", "koKR", "frFR", "deDE", "enCN", "zhCN", "enTW", "zhTW", "esES", "esMX", "ruRU", "ptPT",
        "ptBR", "itIT", "Unk"
    };

    // The language the 3.3.5a client keeps in each text column, by position (AzerothCore's LocaleConstant)
    public static readonly string[] ClientLocales =
    {
        "enUS", "koKR", "frFR", "deDE", "zhCN", "zhTW", "esES", "esMX", "ruRU"
    };

    // Client locales, including those that share a column (enGB with enUS, enCN with zhCN, enTW with zhTW)
    public static readonly string[] AllClientLocales =
    {
        "enUS", "enGB", "koKR", "frFR", "deDE", "zhCN", "enCN", "zhTW", "enTW", "esES", "esMX", "ruRU"
    };

    public const int TEXT_COLUMNS = 16;

    // Which of the 16 text columns holds a locale's text; -1 for none
    public static int Slot(string locale)
    {
        return locale switch
        {
            "enGB" => 0,
            "enCN" => 4,
            "enTW" => 5,
            _ => Array.IndexOf(ClientLocales, locale)
        };
    }

    // The language of a text column by position, as the client reads it
    public static string SlotName(int slot)
    {
        return slot >= 0 && slot < ClientLocales.Length ? ClientLocales[slot] : "unused";
    }

    // The column of a field's text in a locale, such as Name in deDE (AzerothCore calls it Name_Lang_enCN)
    public static int TextColumn(DbcDefinition definition, string field, string locale)
    {
        int first = definition.IndexOf(field + "_Lang_" + ENGLISH);
        int slot = Slot(locale);
        return first < 0 || slot < 0 ? -1 : first + slot;
    }

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

    // Name, NameSubtext, Description or AuraDescription in a language; enUS when that language's text is empty
    public string Text(string field, string locale)
    {
        int column = TextColumn(Table.Definition, field, locale);
        string text = column >= 0 ? Table.GetString(Row, column) : string.Empty;
        return text.Length > 0 || locale == ENGLISH ? text : S(field + "_Lang_" + ENGLISH);
    }

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
