using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// A starting point for a new spell: the fields to set, by spell_dbc column, as text the editor would accept
public sealed record SpellTemplate(string Name, string Summary, IReadOnlyDictionary<string, string> Values);

// Ready-made shapes of common spells, each modelled on a real 3.3.5a spell so the client and server treat it alike
public static class SpellTemplates
{
    // What every template starts from: one rank, no randomness, the normal global cooldown
    private static readonly Dictionary<string, string> Common = new Dictionary<string, string>
    {
        ["ProcChance"] = "101",
        ["BaseLevel"] = "1",
        ["SpellLevel"] = "1",
        ["EquippedItemClass"] = "-1",
        ["EffectDieSides_1"] = "1",
        ["EffectDieSides_2"] = "1",
        ["EffectDieSides_3"] = "1",
        ["EffectChainAmplitude_1"] = "1",
        ["EffectChainAmplitude_2"] = "1",
        ["EffectChainAmplitude_3"] = "1",
        ["StartRecoveryCategory"] = "133",
        ["StartRecoveryTime"] = "1500",
        ["DefenseType"] = "1",
        ["PreventionType"] = "1"
    };

    public static readonly IReadOnlyList<SpellTemplate> All = new List<SpellTemplate>
    {
        Make("Direct damage", "A bolt that deals damage once, like Fireball: 1.5 sec cast, 30 yards.", new()
        {
            ["Name_Lang_enUS"] = "New Bolt",
            ["Description_Lang_enUS"] = "Hurls a bolt of fire at the enemy, causing $s1 Fire damage.",
            ["Attributes"] = "65536", ["FacingCasterFlags"] = "1", ["CastingTimeIndex"] = "16",
            ["InterruptFlags"] = "15", ["RangeIndex"] = "4", ["Speed"] = "24", ["Effect_1"] = "2",
            ["EffectBasePoints_1"] = "99", ["ImplicitTargetA_1"] = "6", ["SchoolMask"] = "4",
            ["SpellIconID"] = "185", ["SpellVisualID_1"] = "67", ["ManaCostPct"] = "8"
        }),
        Make("Damage over time", "Damage every 3 sec for 15 sec, like Moonfire's dot: instant, 30 yards.", new()
        {
            ["Name_Lang_enUS"] = "New Affliction",
            ["Description_Lang_enUS"] = "Afflicts the target with shadow, causing $o1 Shadow damage over $d.",
            ["AuraDescription_Lang_enUS"] = "$s1 Shadow damage every $t1 sec.",
            ["Attributes"] = "65536", ["DispelType"] = "1", ["CastingTimeIndex"] = "1", ["InterruptFlags"] = "8",
            ["DurationIndex"] = "8", ["RangeIndex"] = "4", ["Effect_1"] = "6", ["EffectAura_1"] = "3",
            ["EffectAuraPeriod_1"] = "3000", ["EffectBasePoints_1"] = "19", ["ImplicitTargetA_1"] = "6",
            ["SchoolMask"] = "32", ["SpellIconID"] = "225", ["SpellVisualID_1"] = "1263", ["ManaCostPct"] = "10"
        }),
        Make("Heal", "Heals a friendly target once, like Flash Heal: 1.5 sec cast, 40 yards.", new()
        {
            ["Name_Lang_enUS"] = "New Heal",
            ["Description_Lang_enUS"] = "Heals a friendly target for $s1.",
            ["Attributes"] = "65536", ["CastingTimeIndex"] = "16", ["InterruptFlags"] = "15", ["RangeIndex"] = "5",
            ["Effect_1"] = "10", ["EffectBasePoints_1"] = "199", ["ImplicitTargetA_1"] = "21",
            ["EffectMultipleValue_1"] = "1", ["SchoolMask"] = "2", ["SpellIconID"] = "242",
            ["SpellVisualID_1"] = "3077", ["ManaCostPct"] = "18"
        }),
        Make("Buff", "Raises a friendly target's Stamina for 30 min, like Power Word: Fortitude.", new()
        {
            ["Name_Lang_enUS"] = "New Blessing",
            ["Description_Lang_enUS"] = "Increases the target's Stamina by $s1 for $d.",
            ["AuraDescription_Lang_enUS"] = "Stamina increased by $s1.",
            ["Attributes"] = "65536", ["DispelType"] = "1", ["CastingTimeIndex"] = "1", ["InterruptFlags"] = "8",
            ["DurationIndex"] = "30", ["RangeIndex"] = "4", ["Effect_1"] = "6", ["EffectAura_1"] = "29",
            ["EffectMiscValue_1"] = "2", ["EffectBasePoints_1"] = "9", ["ImplicitTargetA_1"] = "21",
            ["SchoolMask"] = "2", ["SpellIconID"] = "685", ["SpellVisualID_1"] = "278", ["ManaCostPct"] = "20"
        }),
        Make("Proc on hit", "A passive aura that casts another spell on melee hits; set the spell to cast in " +
             "Effects > Trigger spell.", new()
        {
            ["Name_Lang_enUS"] = "New Proc",
            ["Description_Lang_enUS"] = "Your melee attacks have a $h% chance to strike the enemy with extra power.",
            ["Attributes"] = "64", ["CastingTimeIndex"] = "1", ["DurationIndex"] = "21", ["RangeIndex"] = "1",
            ["ProcTypeMask"] = "20", ["ProcChance"] = "10", ["Effect_1"] = "6", ["EffectAura_1"] = "42",
            ["ImplicitTargetA_1"] = "1", ["SchoolMask"] = "1", ["SpellIconID"] = "681",
            ["StartRecoveryCategory"] = "0", ["StartRecoveryTime"] = "0"
        }),
        Make("Summon pet", "Summons a creature as a pet, like Summon Imp; set its creature entry in Effects > " +
             "Misc value.", new()
        {
            ["Name_Lang_enUS"] = "New Summon",
            ["Description_Lang_enUS"] = "Summons a companion that fights for you.",
            ["Attributes"] = "65536", ["CastingTimeIndex"] = "7", ["InterruptFlags"] = "15", ["DurationIndex"] = "21",
            ["RangeIndex"] = "1", ["Effect_1"] = "56", ["EffectBasePoints_1"] = "-1", ["ImplicitTargetA_1"] = "32",
            ["EffectMiscValue_1"] = "416", ["SchoolMask"] = "32", ["SpellIconID"] = "215",
            ["SpellVisualID_1"] = "4043", ["ManaCostPct"] = "60"
        }),
        Make("Teleport", "Teleports the caster after a 10 sec cast, like Hearthstone; the place comes from " +
             "acore_world's spell_target_position row for the spell.", new()
        {
            ["Name_Lang_enUS"] = "New Teleport",
            ["Description_Lang_enUS"] = "Teleports you to a place of power.",
            ["Attributes"] = "65536", ["CastingTimeIndex"] = "7", ["InterruptFlags"] = "31", ["RangeIndex"] = "1",
            ["Effect_1"] = "5", ["EffectBasePoints_1"] = "-1", ["ImplicitTargetA_1"] = "1",
            ["ImplicitTargetB_1"] = "17", ["SchoolMask"] = "64", ["SpellIconID"] = "776", ["SpellVisualID_1"] = "220"
        }),
        Make("Passive", "An always-on aura on its owner that raises Stamina; change the aura for other effects.", new()
        {
            ["Name_Lang_enUS"] = "New Passive",
            ["NameSubtext_Lang_enUS"] = "Passive",
            ["Description_Lang_enUS"] = "Increases your Stamina by $s1.",
            ["Attributes"] = "64", ["CastingTimeIndex"] = "1", ["DurationIndex"] = "21", ["RangeIndex"] = "1",
            ["Effect_1"] = "6", ["EffectAura_1"] = "29", ["EffectMiscValue_1"] = "2", ["EffectBasePoints_1"] = "9",
            ["ImplicitTargetA_1"] = "1", ["SchoolMask"] = "1", ["SpellIconID"] = "1",
            ["StartRecoveryCategory"] = "0", ["StartRecoveryTime"] = "0"
        })
    };

    // Fills a new row from a template; columns the layout doesn't have are skipped, and so are bad values
    public static List<string> Apply(SpellTemplate template, DbcTable table, int row)
    {
        List<string> skipped = new List<string>();
        foreach ((string column, string value) in template.Values)
        {
            int index = table.Definition.IndexOf(column);
            if (index < 0 || !table.TrySetText(row, index, value, out _))
                skipped.Add(column);
        }

        return skipped;
    }

    private static SpellTemplate Make(string name, string summary, Dictionary<string, string> values)
    {
        Dictionary<string, string> all = new Dictionary<string, string>(Common);
        foreach ((string column, string value) in values)
            all[column] = value;
        return new SpellTemplate(name, summary, all);
    }
}
