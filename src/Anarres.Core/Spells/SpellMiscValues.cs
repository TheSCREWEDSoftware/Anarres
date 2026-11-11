namespace Anarres.Core.Spells;

// What an effect's misc values mean, by its effect and aura, such as Stat for a stat aura or Power for Energize
public sealed record MiscMeaning(string Label, EnumTable? Values)
{
    public static readonly MiscMeaning Plain = new MiscMeaning(string.Empty, null);
}

public static class SpellMiscValues
{
    private const string SCHOOL = "School";
    private const string POWER = "Power";
    private const string CREATURE = "Creature entry";
    private const string GAMEOBJECT = "Game object entry";
    private const string SKILL = "Skill (SkillLine.dbc)";
    private const string ENCHANT = "Enchantment (SpellItemEnchantment.dbc)";

    // Stat numbers as AzerothCore's Stats enum, with -1 for every stat
    private static readonly EnumTable Stats = new EnumTable("Stat", false, new List<EnumValue>
    {
        new EnumValue(-1, "ALL", "All stats", string.Empty),
        new EnumValue(0, "STAT_STRENGTH", "Strength", string.Empty),
        new EnumValue(1, "STAT_AGILITY", "Agility", string.Empty),
        new EnumValue(2, "STAT_STAMINA", "Stamina", string.Empty),
        new EnumValue(3, "STAT_INTELLECT", "Intellect", string.Empty),
        new EnumValue(4, "STAT_SPIRIT", "Spirit", string.Empty)
    });

    // What a spell modifier aura changes, as SpellModOp numbers them in 3.3.5a
    private static readonly EnumTable SpellModOps = Named("SpellMod", false, new[]
    {
        "Damage", "Duration", "Threat", "Effect 1 value", "Charges", "Range", "Radius", "Critical chance",
        "All effects", "Not lose casting time", "Casting time", "Cooldown", "Effect 2 value", "Ignore armor", "Cost",
        "Critical damage bonus", "Resist miss chance", "Jump targets", "Chance of success", "Activation time",
        "Damage multiplier", "Global cooldown", "Damage over time", "Effect 3 value", "Bonus multiplier", "",
        "Procs per minute", "Value multiplier", "Resist dispel chance", "Critical damage bonus 2",
        "Cost refund on fail"
    });

    // Combat rating bits, as CombatRating numbers them in 3.3.5a
    private static readonly EnumTable Ratings = Named("Rating", true, new[]
    {
        "Weapon skill", "Defense", "Dodge", "Parry", "Block", "Melee hit", "Ranged hit", "Spell hit", "Melee crit",
        "Ranged crit", "Spell crit", "Melee hit taken", "Ranged hit taken", "Spell hit taken", "Melee crit taken",
        "Ranged crit taken", "Spell crit taken", "Melee haste", "Ranged haste", "Spell haste", "Main-hand skill",
        "Off-hand skill", "Ranged skill", "Expertise", "Armor penetration"
    });

    private static readonly Dictionary<string, (string A, string B)> EffectLabels =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["SPELL_EFFECT_ENERGIZE"] = (POWER, string.Empty),
            ["SPELL_EFFECT_ENERGIZE_PCT"] = (POWER, string.Empty),
            ["SPELL_EFFECT_POWER_BURN"] = (POWER, string.Empty),
            ["SPELL_EFFECT_POWER_DRAIN"] = (POWER, string.Empty),
            ["SPELL_EFFECT_SUMMON"] = (CREATURE, "Summon properties (SummonProperties.dbc)"),
            ["SPELL_EFFECT_SUMMON_PET"] = (CREATURE, string.Empty),
            ["SPELL_EFFECT_KILL_CREDIT"] = (CREATURE, string.Empty),
            ["SPELL_EFFECT_KILL_CREDIT2"] = (CREATURE, string.Empty),
            ["SPELL_EFFECT_DISPEL"] = ("Dispel type", string.Empty),
            ["SPELL_EFFECT_DISPEL_MECHANIC"] = ("Mechanic", string.Empty),
            ["SPELL_EFFECT_SKILL"] = (SKILL, string.Empty),
            ["SPELL_EFFECT_SKILL_STEP"] = (SKILL, string.Empty),
            ["SPELL_EFFECT_OPEN_LOCK"] = ("Lock type (LockType.dbc)", string.Empty),
            ["SPELL_EFFECT_TRANS_DOOR"] = (GAMEOBJECT, string.Empty),
            ["SPELL_EFFECT_SUMMON_OBJECT_WILD"] = (GAMEOBJECT, string.Empty),
            ["SPELL_EFFECT_SUMMON_OBJECT_SLOT1"] = (GAMEOBJECT, string.Empty),
            ["SPELL_EFFECT_SUMMON_OBJECT_SLOT2"] = (GAMEOBJECT, string.Empty),
            ["SPELL_EFFECT_SUMMON_OBJECT_SLOT3"] = (GAMEOBJECT, string.Empty),
            ["SPELL_EFFECT_SUMMON_OBJECT_SLOT4"] = (GAMEOBJECT, string.Empty),
            ["SPELL_EFFECT_ACTIVATE_OBJECT"] = ("Game object action", string.Empty),
            ["SPELL_EFFECT_QUEST_COMPLETE"] = ("Quest ID", string.Empty),
            ["SPELL_EFFECT_ENCHANT_ITEM"] = (ENCHANT, string.Empty),
            ["SPELL_EFFECT_ENCHANT_ITEM_TEMPORARY"] = (ENCHANT, string.Empty),
            ["SPELL_EFFECT_ENCHANT_ITEM_PRISMATIC"] = (ENCHANT, string.Empty),
            ["SPELL_EFFECT_REPUTATION"] = ("Faction (Faction.dbc)", string.Empty),
            ["SPELL_EFFECT_SEND_EVENT"] = ("Event ID", string.Empty),
            ["SPELL_EFFECT_APPLY_GLYPH"] = ("Glyph (GlyphProperties.dbc)", string.Empty)
        };

    private static readonly Dictionary<string, string> AuraLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SPELL_AURA_MOD_STAT"] = "Stat",
        ["SPELL_AURA_MOD_PERCENT_STAT"] = "Stat",
        ["SPELL_AURA_MOD_TOTAL_STAT_PERCENTAGE"] = "Stat",
        ["SPELL_AURA_MOD_RATING_FROM_STAT"] = "Rating",
        ["SPELL_AURA_MOD_SPELL_DAMAGE_OF_STAT_PERCENT"] = SCHOOL,
        ["SPELL_AURA_MOD_SPELL_HEALING_OF_STAT_PERCENT"] = "Stat",
        ["SPELL_AURA_MOD_RESISTANCE"] = SCHOOL,
        ["SPELL_AURA_MOD_RESISTANCE_PCT"] = SCHOOL,
        ["SPELL_AURA_MOD_BASE_RESISTANCE"] = SCHOOL,
        ["SPELL_AURA_MOD_BASE_RESISTANCE_PCT"] = SCHOOL,
        ["SPELL_AURA_MOD_RESISTANCE_EXCLUSIVE"] = SCHOOL,
        ["SPELL_AURA_MOD_TARGET_RESISTANCE"] = SCHOOL,
        ["SPELL_AURA_MOD_DAMAGE_DONE"] = SCHOOL,
        ["SPELL_AURA_MOD_DAMAGE_PERCENT_DONE"] = SCHOOL,
        ["SPELL_AURA_MOD_DAMAGE_TAKEN"] = SCHOOL,
        ["SPELL_AURA_MOD_DAMAGE_PERCENT_TAKEN"] = SCHOOL,
        ["SPELL_AURA_MOD_HEALING_DONE"] = SCHOOL,
        ["SPELL_AURA_MOD_HEALING_DONE_PERCENT"] = SCHOOL,
        ["SPELL_AURA_MOD_HIT_CHANCE"] = SCHOOL,
        ["SPELL_AURA_SCHOOL_ABSORB"] = SCHOOL,
        ["SPELL_AURA_MANA_SHIELD"] = SCHOOL,
        ["SPELL_AURA_SCHOOL_IMMUNITY"] = SCHOOL,
        ["SPELL_AURA_DAMAGE_IMMUNITY"] = SCHOOL,
        ["SPELL_AURA_MOD_POWER_COST_SCHOOL"] = SCHOOL,
        ["SPELL_AURA_MOD_POWER_COST_SCHOOL_PCT"] = SCHOOL,
        ["SPELL_AURA_MECHANIC_IMMUNITY"] = "Mechanic",
        ["SPELL_AURA_DISPEL_IMMUNITY"] = "Dispel type",
        ["SPELL_AURA_MOD_POWER_REGEN"] = POWER,
        ["SPELL_AURA_MOD_POWER_REGEN_PERCENT"] = POWER,
        ["SPELL_AURA_PERIODIC_ENERGIZE"] = POWER,
        ["SPELL_AURA_OBS_MOD_POWER"] = POWER,
        ["SPELL_AURA_MOD_INCREASE_ENERGY"] = POWER,
        ["SPELL_AURA_MOD_INCREASE_ENERGY_PERCENT"] = POWER,
        ["SPELL_AURA_POWER_BURN"] = POWER,
        ["SPELL_AURA_MOD_SHAPESHIFT"] = "Shapeshift form",
        ["SPELL_AURA_MOD_SKILL"] = SKILL,
        ["SPELL_AURA_MOD_SKILL_TALENT"] = SKILL,
        ["SPELL_AURA_ADD_FLAT_MODIFIER"] = "Spell modifier",
        ["SPELL_AURA_ADD_PCT_MODIFIER"] = "Spell modifier",
        ["SPELL_AURA_MOD_RATING"] = "Rating",
        ["SPELL_AURA_TRANSFORM"] = CREATURE,
        ["SPELL_AURA_MOUNTED"] = CREATURE,
        ["SPELL_AURA_FORCE_REACTION"] = "Faction (Faction.dbc)",
        ["SPELL_AURA_MOD_FACTION_REPUTATION_GAIN"] = "Faction (Faction.dbc)",
        ["SPELL_AURA_TRACK_CREATURES"] = "Creature types",
        ["SPELL_AURA_MOD_DAMAGE_DONE_CREATURE"] = "Creature types",
        ["SPELL_AURA_MOD_STEALTH"] = "Stealth type",
        ["SPELL_AURA_MOD_INVISIBILITY"] = "Invisibility type"
    };

    // The meaning of misc value A and B for an effect, and for an aura when the effect applies one
    public static (MiscMeaning A, MiscMeaning B) For(long effect, long aura)
    {
        string effectName = NameOf(SpellEnums.EFFECT, effect);
        if (EffectLabels.TryGetValue(effectName, out (string A, string B) labels))
            return (Meaning(labels.A), Meaning(labels.B));

        string auraName = NameOf(SpellEnums.AURA, aura);
        if (aura != 0 && AuraLabels.TryGetValue(auraName, out string? label))
            return (Meaning(label), MiscMeaning.Plain);
        return (MiscMeaning.Plain, MiscMeaning.Plain);
    }

    private static MiscMeaning Meaning(string label)
    {
        switch (label)
        {
            case "":
                return MiscMeaning.Plain;
            case SCHOOL:
                return new MiscMeaning("School mask", SpellEnums.Get(SpellEnums.SCHOOL));
            case POWER:
                return new MiscMeaning(POWER, SpellEnums.Get(SpellEnums.POWER));
            case "Stat":
                return new MiscMeaning(label, Stats);
            case "Mechanic":
                return new MiscMeaning(label, SpellEnums.Get(SpellEnums.MECHANIC));
            case "Dispel type":
                return new MiscMeaning(label, SpellEnums.Get(SpellEnums.DISPEL));
            case "Shapeshift form":
                return new MiscMeaning(label, SpellEnums.Get(SpellEnums.SHAPESHIFT_FORM));
            case "Spell modifier":
                return new MiscMeaning(label, SpellModOps);
            case "Rating":
                return new MiscMeaning("Rating mask", Ratings);
            case "Creature types":
                return new MiscMeaning("Creature type mask", SpellEnums.CreatureTypeMask());
            default:
                return new MiscMeaning(label, null);
        }
    }

    private static string NameOf(string table, long value)
    {
        return SpellEnums.Get(table).Values.FirstOrDefault(entry => entry.Value == value)?.Name ?? string.Empty;
    }

    private static EnumTable Named(string key, bool flags, string[] labels)
    {
        List<EnumValue> values = new List<EnumValue>();
        for (int index = 0; index < labels.Length; ++index)
        {
            if (labels[index].Length > 0)
            {
                string name = labels[index].ToUpperInvariant().Replace(' ', '_');
                values.Add(new EnumValue(flags ? 1L << index : index, name, labels[index], string.Empty));
            }
        }

        return new EnumTable(key, flags, values);
    }
}
