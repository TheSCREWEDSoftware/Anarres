using System.Text.RegularExpressions;

namespace Anarres.Core.Spells;

// What a column is for, shown as a tooltip beside fields and on grid headers
public static partial class ColumnDocs
{
    private const string SPELL = "Spell.dbc";

    private static readonly Dictionary<string, Dictionary<string, string>> Files =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [SPELL] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = "The spell's unique ID. New spells need an ID no other spell uses; the client's own " +
                         "spells go up to about 80864.",
                ["Category"] = "SpellCategory.dbc ID. Spells in the same category share a cooldown: casting one " +
                               "starts CategoryRecoveryTime on all of them (potions, trinkets, shocks).",
                ["DispelType"] = "Which dispels remove the aura: Magic, Curse, Disease, Poison and so on. None " +
                                 "means it can't be dispelled by type.",
                ["Mechanic"] = "The spell's mechanic (stun, root, fear, polymorph...). Immunities, diminishing " +
                               "returns and \"break on damage\" checks use it.",
                ["Attributes"] = "Behaviour flags. Each bit changes how the spell works; press ... to see them " +
                                 "with AzerothCore's notes.",
                ["ShapeshiftMask"] = "Shapeshift forms the spell can be cast in; bit N is form N + 1. 0 means " +
                                     "any form.",
                ["ShapeshiftExclude"] = "Shapeshift forms the spell can't be cast in; bit N is form N + 1.",
                ["unk_320_2"] = "Not used in 3.3.5a; leave at 0.",
                ["unk_320_3"] = "Not used in 3.3.5a; leave at 0.",
                ["Targets"] = "What the cast asks the player to target: a unit, an item, a ground point, a game " +
                              "object and so on. Most spells take their targets from the effects instead.",
                ["TargetCreatureType"] = "Creature types the target must be (beast, undead, demon...); bit N is " +
                                         "type N + 1. 0 means any type.",
                ["RequiresSpellFocus"] = "SpellFocusObject.dbc ID: an object that must be near the caster, " +
                                         "such as an anvil, a forge or a cooking fire.",
                ["FacingCasterFlags"] = "1 makes the caster face the target to cast, as melee abilities need.",
                ["CasterAuraState"] = "An aura state the caster must have, such as \"just dodged\" for " +
                                      "Overpower or \"Health below 20%\" for Execute.",
                ["TargetAuraState"] = "An aura state the target must have, such as \"Health below 20%\" for " +
                                      "Execute or \"Frozen\" for Shatter.",
                ["ExcludeCasterAuraState"] = "An aura state the caster must not have.",
                ["ExcludeTargetAuraState"] = "An aura state the target must not have.",
                ["CasterAuraSpell"] = "A spell whose aura the caster must have to cast this one.",
                ["TargetAuraSpell"] = "A spell whose aura the target must have.",
                ["ExcludeCasterAuraSpell"] = "A spell whose aura the caster must not have, such as Forbearance " +
                                             "for Divine Shield.",
                ["ExcludeTargetAuraSpell"] = "A spell whose aura the target must not have, such as Weakened " +
                                             "Soul for Power Word: Shield.",
                ["CastingTimeIndex"] = "SpellCastTimes.dbc ID. 1 is instant; the value beside it is the cast " +
                                       "time in seconds.",
                ["RecoveryTime"] = "The spell's own cooldown, in milliseconds.",
                ["CategoryRecoveryTime"] = "The cooldown, in milliseconds, shared by every spell in the same " +
                                           "Category.",
                ["InterruptFlags"] = "What interrupts or delays the cast: moving, pushback from damage, an " +
                                     "interrupt ability...",
                ["AuraInterruptFlags"] = "What removes the aura: taking damage, moving, casting, mounting, " +
                                         "attacking...",
                ["ChannelInterruptFlags"] = "What interrupts a channelled spell.",
                ["ProcTypeMask"] = "Events that can trigger the aura's proc: dealing or taking melee or spell " +
                                   "hits, crits, kills and so on. AzerothCore's spell_proc table can refine it.",
                ["ProcChance"] = "Chance, in percent, that a matching event triggers the proc.",
                ["ProcCharges"] = "How many procs before the aura is used up. 0 means it never runs out.",
                ["MaxLevel"] = "The level where per-level growth stops (points, cost and so on).",
                ["BaseLevel"] = "The level the spell's values are for; per-level growth counts from here.",
                ["SpellLevel"] = "The level the spell is learned at. Also used for resists and level checks.",
                ["DurationIndex"] = "SpellDuration.dbc ID for how long the aura or effect lasts. 0 means no " +
                                    "duration.",
                ["PowerType"] = "The power the spell costs: mana, rage, focus, energy, runes, runic power, or " +
                                "health (-2).",
                ["ManaCost"] = "The flat cost. Rage and runic power are in tenths: 100 costs 10.",
                ["ManaCostPerLevel"] = "Cost added per level above BaseLevel.",
                ["ManaPerSecond"] = "Cost each second while channelling.",
                ["ManaPerSecondPerLevel"] = "Per-second cost added per level.",
                ["RangeIndex"] = "SpellRange.dbc ID: the spell's minimum and maximum range.",
                ["Speed"] = "Missile speed in yards per second. 0 hits at once.",
                ["ModalNextSpell"] = "The next spell of a modal chain; rarely used in 3.3.5a.",
                ["CumulativeAura"] = "How many times the aura stacks. 0 or 1 means it doesn't stack.",
                ["Totem"] = "An item the caster must carry but doesn't use up, such as a totem or a tool.",
                ["Reagent"] = "An item used up by the cast.",
                ["ReagentCount"] = "How many of the reagent the cast uses up.",
                ["EquippedItemClass"] = "Item class that must be equipped: 2 is weapons, 4 is armour. -1 means " +
                                        "nothing needs to be equipped.",
                ["EquippedItemSubclass"] = "Item subclasses allowed, as a mask (bit N is subclass N), such as " +
                                           "which weapon types.",
                ["EquippedItemInvTypes"] = "Inventory slots allowed, as a mask (bit N is inventory type N).",
                ["Effect"] = "What this effect does: damage, heal, apply an aura, summon, teleport...",
                ["EffectDieSides"] = "Random extra value: the effect is BasePoints + 1 up to BasePoints + " +
                                     "DieSides. 1 means no randomness.",
                ["EffectRealPointsPerLevel"] = "Value added per caster level above BaseLevel, up to MaxLevel.",
                ["EffectBasePoints"] = "The effect's value minus one: 9 with 1 die side gives 10. Tooltips show " +
                                       "it as $s1, $s2, $s3.",
                ["EffectMechanic"] = "The mechanic of this effect alone, such as a stun on one effect of a " +
                                     "spell that also deals damage.",
                ["ImplicitTargetA"] = "Who the effect lands on: the caster, the target, enemies around a point " +
                                      "and so on.",
                ["ImplicitTargetB"] = "A second target rule that refines Target A, such as \"enemies in an " +
                                      "area around Target A\".",
                ["EffectRadiusIndex"] = "SpellRadius.dbc ID for the size of an area effect.",
                ["EffectAura"] = "The aura an Apply Aura effect puts on its targets.",
                ["EffectAuraPeriod"] = "Milliseconds between ticks of a periodic aura (damage, heal, trigger).",
                ["EffectMultipleValue"] = "A multiplier whose meaning depends on the effect, such as how much " +
                                          "health a drain gives back per point taken.",
                ["EffectChainTargets"] = "How many targets a chain effect jumps to.",
                ["EffectItemType"] = "The item a Create Item effect makes, or the item other item effects use.",
                ["EffectMiscValue"] = "A value whose meaning depends on the effect or aura; the text beside it " +
                                      "says what it is here.",
                ["EffectMiscValueB"] = "A second value whose meaning depends on the effect, such as summon " +
                                       "properties for Summon.",
                ["EffectTriggerSpell"] = "The spell cast by a Trigger Spell effect, or by a proc or periodic " +
                                         "trigger aura.",
                ["EffectPointsPerCombo"] = "Value added per combo point.",
                ["EffectSpellClassMaskA"] = "Family mask (first 32 bits) of the spells this effect changes, " +
                                            "with SpellClassSet; how talents find the spells they improve.",
                ["EffectSpellClassMaskB"] = "Family mask, bits 32 to 63, of the spells this effect changes.",
                ["EffectSpellClassMaskC"] = "Family mask, bits 64 to 95, of the spells this effect changes.",
                ["SpellVisualID"] = "SpellVisual.dbc ID: the animations, particles and sounds. Edit them on " +
                                    "the Visual tab.",
                ["SpellIconID"] = "SpellIcon.dbc ID of the icon.",
                ["ActiveIconID"] = "SpellIcon.dbc ID of the icon shown while the spell is active, such as an " +
                                   "aura or auto-attack.",
                ["SpellPriority"] = "Client priority used to sort some spells; rarely matters.",
                ["Name"] = "The spell's name in this language.",
                ["NameSubtext"] = "The text under the name, such as \"Rank 1\", \"Racial\" or \"Passive\".",
                ["Description"] = "The tooltip. $s1, $d, $o1 and the other $ codes are filled in by the client.",
                ["AuraDescription"] = "The tooltip of the buff or debuff the spell leaves.",
                ["Name_Lang_Mask"] = "Which languages have a name; the client shows a language only when its " +
                                     "bit is set.",
                ["NameSubtext_Lang_Mask"] = "Which languages have rank text.",
                ["Description_Lang_Mask"] = "Which languages have a tooltip.",
                ["AuraDescription_Lang_Mask"] = "Which languages have a buff tooltip.",
                ["ManaCostPct"] = "Cost as a percentage of the caster's base mana (or base health).",
                ["StartRecoveryCategory"] = "The global cooldown group. 133 is the normal global cooldown; 0 " +
                                            "means the spell doesn't trigger or wait for one. Spells in the " +
                                            "same group share it.",
                ["StartRecoveryTime"] = "Length of the global cooldown it triggers, in milliseconds: usually " +
                                        "1500, or 1000 for energy users.",
                ["MaxTargetLevel"] = "The highest target level the spell works on, such as for crowd control.",
                ["SpellClassSet"] = "The spell family (Mage, Warrior, Priest...). With the family masks, it lets " +
                                    "talents and auras find this spell.",
                ["SpellClassMask"] = "This spell's family bits. Auras whose EffectSpellClassMask matches them " +
                                     "affect this spell.",
                ["MaxTargets"] = "The most targets an area spell hits. 0 means no limit.",
                ["DefenseType"] = "Damage class: none, magic, melee or ranged. Decides how hit, crit and " +
                                  "resist work.",
                ["PreventionType"] = "What stops the cast: 1 is silence, 2 is pacify.",
                ["StanceBarOrder"] = "Position on the stance bar for forms and stances.",
                ["EffectChainAmplitude"] = "Value multiplier at each chain jump: 0.5 halves it every jump.",
                ["MinFactionID"] = "The faction whose reputation the caster needs.",
                ["MinReputation"] = "The reputation rank needed with MinFactionID.",
                ["RequiredAuraVision"] = "Aura vision needed to cast; rarely used.",
                ["RequiredTotemCategoryID"] = "TotemCategory.dbc ID of a tool kind needed, such as any mining " +
                                              "pick or blacksmith hammer.",
                ["RequiredAreasID"] = "AreaGroup.dbc ID of the zones the spell can be cast in. 0 means anywhere.",
                ["SchoolMask"] = "The spell's school: physical, holy, fire, nature, frost, shadow, arcane, or " +
                                 "several at once.",
                ["RuneCostID"] = "SpellRuneCost.dbc ID: the blood, frost and unholy runes it costs and the " +
                                 "runic power it gives.",
                ["SpellMissileID"] = "SpellMissile.dbc ID: how the missile flies (arc, gravity, speed range).",
                ["PowerDisplayID"] = "PowerDisplay.dbc ID: how the power bar looks, for vehicles and special " +
                                     "powers.",
                ["EffectBonusMultiplier"] = "Spell power coefficient of this effect, such as 0.857 for a " +
                                            "3-second cast.",
                ["SpellDescriptionVariableID"] = "SpellDescriptionVariables.dbc ID: the $<name> values the " +
                                                 "tooltip uses.",
                ["SpellDifficultyID"] = "SpellDifficulty.dbc ID: the versions of this spell used in 10, 25 and " +
                                        "heroic modes."
            },
            ["SpellCastTimes.dbc"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Base"] = "Cast time in milliseconds.",
                ["PerLevel"] = "Milliseconds added per level.",
                ["Minimum"] = "The shortest the cast can become, in milliseconds."
            },
            ["SpellDuration.dbc"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Duration"] = "Duration in milliseconds; -1 lasts until cancelled.",
                ["DurationPerLevel"] = "Milliseconds added per level.",
                ["MaxDuration"] = "The longest it can last, in milliseconds."
            },
            ["SpellRadius.dbc"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Radius"] = "Radius in yards.",
                ["RadiusPerLevel"] = "Yards added per level.",
                ["RadiusMax"] = "The largest radius, in yards."
            },
            ["SpellRange.dbc"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RangeMin"] = "Minimum range in yards: 1 against enemies, 2 for friends.",
                ["RangeMax"] = "Maximum range in yards: 1 against enemies, 2 for friends.",
                ["Flags"] = "1 makes it a melee range; 2 a ranged-weapon range.",
                ["DisplayName"] = "The range's name in this language, such as \"Long Range\".",
                ["DisplayNameShort"] = "The short name in this language."
            },
            ["SpellIcon.dbc"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TextureFilename"] = "Path of the icon in the client's MPQs, without .blp, such as " +
                                      "Interface\\Icons\\Spell_Frost_FrostBolt02."
            },
            ["SpellVisual.dbc"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PrecastKit"] = "SpellVisualKit.dbc ID played while the cast is being prepared.",
                ["CastKit"] = "SpellVisualKit.dbc ID played while casting.",
                ["ImpactKit"] = "SpellVisualKit.dbc ID played on the target when the spell lands.",
                ["StateKit"] = "SpellVisualKit.dbc ID played on the target for as long as the aura lasts.",
                ["StateDoneKit"] = "SpellVisualKit.dbc ID played when the aura ends.",
                ["ChannelKit"] = "SpellVisualKit.dbc ID played while channelling.",
                ["HasMissile"] = "1 when the spell fires a missile.",
                ["MissileModel"] = "SpellVisualEffectName.dbc ID of the missile's model.",
                ["CasterImpactKit"] = "SpellVisualKit.dbc ID played on the caster when the spell lands.",
                ["TargetImpactKit"] = "SpellVisualKit.dbc ID played on each target hit.",
                ["MissileTargetingKit"] = "SpellVisualKit.dbc ID shown where a ground-targeted missile lands.",
                ["InstantAreaKit"] = "SpellVisualKit.dbc ID for an area effect that happens at once.",
                ["ImpactAreaKit"] = "SpellVisualKit.dbc ID for the area where the spell lands.",
                ["PersistentAreaKit"] = "SpellVisualKit.dbc ID for a lasting ground effect, such as Blizzard."
            }
        };

    private const string SUFFIX_PATTERN =
        @"(_Lang_(enUS|enGB|koKR|frFR|deDE|enCN|zhCN|enTW|zhTW|esES|esMX|ruRU|ptPT|ptBR|itIT|Unk))$" + @"|_\d+$";

    [GeneratedRegex(SUFFIX_PATTERN)]
    private static partial Regex SuffixRegex();

    // The description of a column, or null when there's none; Effect_2 and Name_Lang_deDE share their base's
    public static string? Describe(string file, string column)
    {
        string name = Path.GetFileName(file);
        if (!name.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase))
            name += ".dbc";
        if (!Files.TryGetValue(name, out Dictionary<string, string>? docs))
            return null;
        if (docs.TryGetValue(column, out string? exact))
            return exact;
        if (column.StartsWith("Attributes", StringComparison.OrdinalIgnoreCase) &&
            docs.TryGetValue("Attributes", out string? attributes))
            return attributes;
        return docs.TryGetValue(SuffixRegex().Replace(column, string.Empty), out string? shared) ? shared : null;
    }

    // Description, then the column's own name, for a tooltip
    public static string Tooltip(string file, string column, string? table = null)
    {
        string? description = Describe(file, column);
        string where = (table is { Length: > 0 } ? table : Path.GetFileNameWithoutExtension(file)) + ": " + column;
        return description == null ? where : description + "\n\n" + where;
    }
}
