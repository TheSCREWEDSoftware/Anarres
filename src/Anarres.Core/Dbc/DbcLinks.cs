using System.Text;
using System.Text.RegularExpressions;

namespace Anarres.Core.Dbc;

// A column whose values are IDs of another table's rows
public sealed record DbcLink(string From, string Column, string To);

// How tables refer to each other: from column names (SpellIconID, MapID) and a list for the rest (CastingTimeIndex)
public static partial class DbcLinks
{
    private const string EXTENSION = ".dbc";

    // Table.Column (without its _1, _2 number) to the table it points at, where the name alone doesn't tell
    private static readonly Dictionary<string, string> Known =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Spell.CastingTimeIndex"] = "SpellCastTimes",
        ["Spell.DurationIndex"] = "SpellDuration",
        ["Spell.RangeIndex"] = "SpellRange",
        ["Spell.EffectRadiusIndex"] = "SpellRadius",
        ["Spell.ActiveIconID"] = "SpellIcon",
        ["Spell.SpellDescriptionVariableID"] = "SpellDescriptionVariables",
        ["Spell.RuneCostID"] = "SpellRuneCost",
        ["Spell.SpellMissileID"] = "SpellMissile",
        ["Spell.Category"] = "SpellCategory",
        ["Spell.DispelType"] = "SpellDispelType",
        ["Spell.Mechanic"] = "SpellMechanic",
        ["Spell.EffectMechanic"] = "SpellMechanic",
        ["Spell.PowerDisplayID"] = "PowerDisplay",
        ["Spell.EffectTriggerSpell"] = "Spell",
        ["Spell.CasterAuraSpell"] = "Spell",
        ["Spell.TargetAuraSpell"] = "Spell",
        ["Spell.ExcludeCasterAuraSpell"] = "Spell",
        ["Spell.ExcludeTargetAuraSpell"] = "Spell",
        ["Spell.EffectChainAmplitude"] = "",
        ["Spell.RequiredTotemCategoryID"] = "TotemCategory",
        ["Spell.RequiresSpellFocus"] = "SpellFocusObject",
        ["Spell.RequiredAreasID"] = "AreaGroup",
        ["Spell.MinFactionID"] = "Faction",
        ["Spell.SpellClassSet"] = "",
        ["Spell.StartRecoveryCategory"] = "",
        ["Spell.SpellVisualID"] = "SpellVisual",
        ["Spell.SpellIconID"] = "SpellIcon",
        ["Spell.SpellDifficultyID"] = "SpellDifficulty",
        ["Spell.EquippedItemClass"] = "ItemClass",
        ["SpellVisual.PrecastKit"] = "SpellVisualKit",
        ["SpellVisual.CastKit"] = "SpellVisualKit",
        ["SpellVisual.ImpactKit"] = "SpellVisualKit",
        ["SpellVisual.StateKit"] = "SpellVisualKit",
        ["SpellVisual.StateDoneKit"] = "SpellVisualKit",
        ["SpellVisual.ChannelKit"] = "SpellVisualKit",
        ["SpellVisual.CasterImpactKit"] = "SpellVisualKit",
        ["SpellVisual.TargetImpactKit"] = "SpellVisualKit",
        ["SpellVisual.InstantAreaKit"] = "SpellVisualKit",
        ["SpellVisual.ImpactAreaKit"] = "SpellVisualKit",
        ["SpellVisual.PersistentAreaKit"] = "SpellVisualKit",
        ["SpellVisual.MissileTargetingKit"] = "SpellVisualKit",
        ["SpellVisual.MissileModel"] = "SpellVisualEffectName",
        ["SpellVisual.MissileSound"] = "SoundEntries",
        ["SpellVisual.AnimEventSoundID"] = "SoundEntries",
        ["SpellVisualKit.HeadEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.ChestEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.BaseEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.LeftHandEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.RightHandEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.BreathEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.LeftWeaponEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.RightWeaponEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.SpecialEffect"] = "SpellVisualEffectName",
        ["SpellVisualKit.SoundID"] = "SoundEntries",
        ["SpellVisualKit.ShakeID"] = "SpellEffectCameraShakes",
        ["SpellVisualKit.StartAnimID"] = "AnimationData",
        ["SpellVisualKit.AnimID"] = "AnimationData",
        ["SkillLineAbility.Spell"] = "Spell",
        ["SkillLineAbility.SupercededBySpell"] = "Spell",
        ["SkillLineAbility.SkillLine"] = "SkillLine",
        ["Talent.SpellRank"] = "Spell",
        ["Talent.TabID"] = "TalentTab",
        ["Talent.PrereqTalent"] = "Talent",
        ["Talent.RequiredSpellID"] = "Spell",
        ["CreatureDisplayInfo.ModelID"] = "CreatureModelData",
        ["CreatureDisplayInfo.ExtendedDisplayInfoID"] = "CreatureDisplayInfoExtra",
        ["CreatureDisplayInfo.BloodID"] = "UnitBlood",
        ["CreatureDisplayInfo.NPCSoundID"] = "NPCSounds",
        ["CreatureDisplayInfo.ParticleColorID"] = "ParticleColor",
        ["SpellShapeshiftForm.CreatureDisplayID"] = "CreatureDisplayInfo",
        ["SpellShapeshiftForm.PresetSpellID"] = "Spell",
        ["SpellShapeshiftForm.AttackIconID"] = "SpellIcon",
        ["ItemDisplayInfo.SpellVisualID"] = "SpellVisual",
        ["ItemDisplayInfo.ItemVisual"] = "ItemVisuals",
        ["SpellItemEnchantment.EffectArg"] = "",
        ["SpellItemEnchantment.ItemVisual"] = "ItemVisuals",
        ["SpellItemEnchantment.ConditionID"] = "SpellItemEnchantmentCondition",
        ["AreaTable.ContinentID"] = "Map",
        ["AreaTable.ParentAreaID"] = "AreaTable",
        ["AreaTable.ZoneMusic"] = "ZoneMusic",
        ["AreaTable.IntroSound"] = "ZoneIntroMusicTable",
        ["AreaTable.LiquidTypeID"] = "LiquidType",
        ["AreaTable.LightID"] = "Light",
        ["TaxiPath.FromTaxiNode"] = "TaxiNodes",
        ["TaxiPath.ToTaxiNode"] = "TaxiNodes",
        ["TaxiPathNode.PathID"] = "TaxiPath",
        ["TaxiNodes.ContinentID"] = "Map",
        ["Achievement.Instance_ID"] = "Map",
        ["Achievement.Category"] = "Achievement_Category",
        ["Achievement.IconID"] = "SpellIcon",
        ["Achievement_Criteria.Achievement_ID"] = "Achievement",
        ["ChrRaces.FactionID"] = "FactionTemplate",
        ["ChrRaces.MaleDisplayID"] = "CreatureDisplayInfo",
        ["ChrRaces.FemaleDisplayID"] = "CreatureDisplayInfo",
        ["ChrRaces.CinematicSequenceID"] = "CinematicSequences",
        ["ChrClasses.CinematicSequenceID"] = "CinematicSequences",
        ["SkillRaceClassInfo.SkillID"] = "SkillLine",
        ["SkillRaceClassInfo.SkillTierID"] = "SkillTiers",
        ["SkillLine.CategoryID"] = "SkillLineCategory",
        ["SkillLine.SkillCostsID"] = "SkillCostsData",
        ["SkillLine.SpellIconID"] = "SpellIcon",
        ["FactionTemplate.Faction"] = "Faction",
        ["ItemSubClass.ClassID"] = "ItemClass",
        ["ItemSubClassMask.ClassID"] = "ItemClass",
        ["Item.ClassID"] = "ItemClass",
        ["ItemClass.ClassID"] = "",
        ["MapDifficulty.MapID"] = "Map",
        ["LFGDungeons.MapID"] = "Map",
        ["Map.AreaTableID"] = "AreaTable",
        ["Map.LoadingScreenID"] = "LoadingScreens",
        ["Map.CorpseMapID"] = "Map",
        ["Map.ParentMapID"] = "Map"
    };

    // Columns that end in ID but count something or name a kind, never a row of another table
    private static readonly HashSet<string> NotLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ID", "Flags", "Type", "Value", "Count", "Min", "Max"
    };

    [GeneratedRegex(@"_\d+$")]
    private static partial Regex NumberSuffix();

    // Every link between the given layouts; a target must be one of them
    public static List<DbcLink> Find(IEnumerable<DbcDefinition> definitions)
    {
        List<DbcDefinition> all = definitions.ToList();
        Dictionary<string, string> tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DbcDefinition definition in all)
            tables.TryAdd(Name(definition.File), definition.File);

        List<DbcLink> links = new List<DbcLink>();
        foreach (DbcDefinition definition in all)
        {
            string from = Name(definition.File);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < definition.Columns.Count; ++index)
            {
                DbcColumn column = definition.Columns[index];
                if (index == 0 && definition.HasKey || !DbcColumnTypes.IsInteger(column.Type))
                    continue;
                string bare = NumberSuffix().Replace(column.Name, string.Empty);
                if (!seen.Add(bare))
                    continue;
                string? target = Target(from, bare, tables);
                if (target != null)
                    links.Add(new DbcLink(definition.File, bare, target));
            }
        }

        return links;
    }

    // The table a column points at, or null
    private static string? Target(string table, string column, Dictionary<string, string> tables)
    {
        if (Known.TryGetValue(table + "." + column, out string? known))
            return known.Length > 0 && tables.TryGetValue(known, out string? file) ? file : null;
        if (NotLinks.Contains(column))
            return null;
        foreach (string suffix in new[] { "ID", "Id", "Index", "IndexID" })
        {
            if (column.Length <= suffix.Length || !column.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            string stem = column[..^suffix.Length].TrimEnd('_');
            // MapID is a Map row; SpellVisualKitID a SpellVisualKit row; RaceID a ChrRaces row
            foreach (string candidate in new[] { stem, stem + "s", "Chr" + stem + "s", "Chr" + stem + "es" })
            {
                if (tables.TryGetValue(candidate, out string? file))
                    return file;
            }

            // A longer name may end in the table's: MaleDisplayID has no table, but a TargetSpellID is a Spell
            string? ending = tables.Keys.Where(name => name.Length >= 4 &&
                                                       stem.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                                   .OrderByDescending(name => name.Length).FirstOrDefault();
            if (ending != null)
                return tables[ending];
        }

        return null;
    }

    public static string Name(string file)
    {
        string name = Path.GetFileName(file);
        return name.EndsWith(EXTENSION, StringComparison.OrdinalIgnoreCase) ? name[..^EXTENSION.Length] : name;
    }

    // The whole map as a Mermaid flowchart, for drawing elsewhere (GitHub, mermaid.live, editors)
    public static string Mermaid(IEnumerable<DbcLink> links)
    {
        StringBuilder text = new StringBuilder("flowchart LR\n");
        foreach (DbcLink link in links.OrderBy(link => link.From, StringComparer.OrdinalIgnoreCase))
        {
            text.Append("    ").Append(Node(link.From)).Append(" -->|").Append(link.Column).Append("| ")
                .Append(Node(link.To)).Append('\n');
        }

        return text.ToString();
    }

    private static string Node(string file)
    {
        string name = Name(file);
        return name.Replace("-", "_").Replace(" ", "_") + "[\"" + name + "\"]";
    }
}
