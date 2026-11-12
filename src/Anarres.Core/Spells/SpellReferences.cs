using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// Another spell linked to the one looked at, and how
public sealed record SpellReference(string Group, uint SpellId, string SpellName, string How);

// What links to a spell: triggers, family-mask talents, spells it triggers, and shared visuals or icons
public static class SpellReferences
{
    public const string TRIGGERED_BY = "Triggered or taught by";
    public const string MODIFIED_BY = "Changed by (family masks)";
    public const string TRIGGERS = "Triggers";
    public const string SAME_VISUAL = "Same visual";
    public const string SAME_ICON = "Same icon";

    public static List<SpellReference> Find(DbcTable spells, int row, int mostShared = 200)
    {
        List<SpellReference> found = new List<SpellReference>();
        SpellRecord spell = new SpellRecord(spells, row);
        uint id = spell.Id;
        uint family = spell.U("SpellClassSet");
        uint[] mask = { spell.U("SpellClassMask_1"), spell.U("SpellClassMask_2"), spell.U("SpellClassMask_3") };
        bool hasMask = mask.Any(part => part != 0);
        uint visual = spell.U("SpellVisualID_1");
        uint icon = spell.U("SpellIconID");
        int visuals = 0;
        int icons = 0;

        for (int effect = 1; effect <= SpellRecord.EFFECT_COUNT; ++effect)
        {
            uint trigger = spell.U("EffectTriggerSpell_" + effect);
            int target = trigger == 0 ? -1 : spells.FindRow(trigger);
            if (trigger != 0)
                found.Add(new SpellReference(TRIGGERS, trigger,
                                             target >= 0 ? new SpellRecord(spells, target).Name : "(not in Spell.dbc)",
                                             "effect " + effect));
        }

        // Columns looked up once; the loop runs over every spell
        int[] triggers = Columns(spells, "EffectTriggerSpell_");
        int[] auras = Columns(spells, "EffectAura_");
        int[] masksA = Columns(spells, "EffectSpellClassMaskA_");
        int[] masksB = Columns(spells, "EffectSpellClassMaskB_");
        int[] masksC = Columns(spells, "EffectSpellClassMaskC_");
        int familyColumn = spells.Definition.IndexOf("SpellClassSet");
        int visualColumn = spells.Definition.IndexOf("SpellVisualID_1");
        int iconColumn = spells.Definition.IndexOf("SpellIconID");
        for (int other = 0; other < spells.Rows.Count; ++other)
        {
            if (other == row)
                continue;
            bool sameFamily = hasMask && family != 0 && familyColumn >= 0 &&
                              spells.GetRaw(other, familyColumn) == family;
            for (int effect = 0; effect < SpellRecord.EFFECT_COUNT; ++effect)
            {
                if (triggers[effect] >= 0 && spells.GetRaw(other, triggers[effect]) == id)
                    found.Add(Reference(spells, other, TRIGGERED_BY, "effect " + (effect + 1)));
                uint aura = auras[effect] >= 0 ? spells.GetRaw(other, auras[effect]) : 0;
                if (sameFamily && aura != 0 &&
                    ((Raw(spells, other, masksA[effect]) & mask[0]) != 0 ||
                     (Raw(spells, other, masksB[effect]) & mask[1]) != 0 ||
                     (Raw(spells, other, masksC[effect]) & mask[2]) != 0))
                    found.Add(Reference(spells, other, MODIFIED_BY, "effect " + (effect + 1) + ", aura " + aura));
            }

            if (visual != 0 && visualColumn >= 0 && spells.GetRaw(other, visualColumn) == visual &&
                visuals++ < mostShared)
                found.Add(Reference(spells, other, SAME_VISUAL, "SpellVisual " + visual));
            if (icon != 0 && iconColumn >= 0 && spells.GetRaw(other, iconColumn) == icon && icons++ < mostShared)
                found.Add(Reference(spells, other, SAME_ICON, "SpellIcon " + icon));
        }

        return found;
    }

    // The spells one effect changes: same family, and a SpellClassMask bit in common with the effect's masks
    public static List<int> Affected(DbcTable spells, int row, int effect)
    {
        List<int> found = new List<int>();
        SpellRecord spell = new SpellRecord(spells, row);
        uint family = spell.U("SpellClassSet");
        uint[] mask =
        {
            spell.U("EffectSpellClassMaskA_" + effect), spell.U("EffectSpellClassMaskB_" + effect),
            spell.U("EffectSpellClassMaskC_" + effect)
        };
        int familyColumn = spells.Definition.IndexOf("SpellClassSet");
        int[] classMasks = Enumerable.Range(1, mask.Length)
                                     .Select(part => spells.Definition.IndexOf("SpellClassMask_" + part)).ToArray();
        if (family == 0 || familyColumn < 0 || mask.All(part => part == 0) || classMasks.Any(column => column < 0))
            return found;
        for (int other = 0; other < spells.Rows.Count; ++other)
        {
            if (other == row || spells.GetRaw(other, familyColumn) != family)
                continue;
            for (int part = 0; part < mask.Length; ++part)
            {
                if ((spells.GetRaw(other, classMasks[part]) & mask[part]) != 0)
                {
                    found.Add(other);
                    break;
                }
            }
        }

        return found;
    }

    // How many spells change this one through family masks, without the rest of Find's work
    public static List<uint> ModifiedBy(DbcTable spells, int row)
    {
        List<uint> found = new List<uint>();
        SpellRecord spell = new SpellRecord(spells, row);
        uint family = spell.U("SpellClassSet");
        uint[] mask = { spell.U("SpellClassMask_1"), spell.U("SpellClassMask_2"), spell.U("SpellClassMask_3") };
        int familyColumn = spells.Definition.IndexOf("SpellClassSet");
        if (family == 0 || familyColumn < 0 || mask.All(part => part == 0))
            return found;
        int[] auras = Columns(spells, "EffectAura_");
        int[] masksA = Columns(spells, "EffectSpellClassMaskA_");
        int[] masksB = Columns(spells, "EffectSpellClassMaskB_");
        int[] masksC = Columns(spells, "EffectSpellClassMaskC_");
        for (int other = 0; other < spells.Rows.Count; ++other)
        {
            if (other == row || spells.GetRaw(other, familyColumn) != family)
                continue;
            for (int effect = 0; effect < SpellRecord.EFFECT_COUNT; ++effect)
            {
                if (Raw(spells, other, auras[effect]) != 0 &&
                    ((Raw(spells, other, masksA[effect]) & mask[0]) != 0 ||
                     (Raw(spells, other, masksB[effect]) & mask[1]) != 0 ||
                     (Raw(spells, other, masksC[effect]) & mask[2]) != 0))
                {
                    found.Add(spells.Key(other));
                    break;
                }
            }
        }

        return found;
    }

    // Spells that are talents, from Talent.dbc's ranks
    public static HashSet<uint> TalentSpells(DbcTable? talents)
    {
        HashSet<uint> spells = new HashSet<uint>();
        if (talents == null)
            return spells;
        int[] ranks = talents.Definition.Columns.Select((column, index) => (column, index))
                             .Where(entry => entry.column.Name.StartsWith("SpellRank", StringComparison.Ordinal))
                             .Select(entry => entry.index).ToArray();
        for (int row = 0; row < talents.Rows.Count; ++row)
        {
            foreach (int column in ranks)
            {
                uint spell = talents.GetRaw(row, column);
                if (spell != 0)
                    spells.Add(spell);
            }
        }

        return spells;
    }

    private static SpellReference Reference(DbcTable spells, int row, string group, string how)
    {
        SpellRecord spell = new SpellRecord(spells, row);
        return new SpellReference(group, spell.Id, spell.Name, how);
    }

    private static int[] Columns(DbcTable spells, string prefix)
    {
        return Enumerable.Range(1, SpellRecord.EFFECT_COUNT)
                         .Select(effect => spells.Definition.IndexOf(prefix + effect)).ToArray();
    }

    private static uint Raw(DbcTable spells, int row, int column)
    {
        return column < 0 ? 0 : spells.GetRaw(row, column);
    }
}
