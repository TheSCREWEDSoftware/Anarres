using System.Numerics;
using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// A set of spells to narrow the spell list to, such as a class's or a profession's
public sealed record SpellGroup(string Label, HashSet<uint> Spells);

// Spells by class, race and profession, worked out from the client's own tables
public static class SpellGroups
{
    // Skill line categories in SkillLine.dbc
    private const int PROFESSIONS = 11;
    private const int SECONDARY_SKILLS = 9;

    // First Aid, Cooking and Fishing: the secondary skills that are professions, among riding and racial lines
    private static readonly uint[] SecondaryProfessions = { 129, 185, 356 };

    public static List<SpellGroup> Build(DbcTable spells, DbcTable? classes, DbcTable? races, DbcTable? skillLines,
                                         DbcTable? abilities, DbcTable? skillRaceClass, DbcTable? talents)
    {
        List<SpellGroup> groups = new List<SpellGroup>();
        if (talents != null)
            groups.Add(new SpellGroup("Talents", SpellReferences.TalentSpells(talents)));
        if (classes != null)
            groups.AddRange(Classes(spells, classes, abilities));
        if (races != null)
            groups.AddRange(Races(races, abilities, skillRaceClass));
        if (skillLines != null && abilities != null)
            groups.AddRange(Professions(skillLines, abilities));
        return groups.Where(group => group.Spells.Count > 0).ToList();
    }

    // A class's spells: its spell family (SpellClassSet), and skill line rows for that class alone or a few
    private static IEnumerable<SpellGroup> Classes(DbcTable spells, DbcTable classes, DbcTable? abilities)
    {
        int family = spells.Definition.IndexOf("SpellClassSet");
        int classSet = classes.Definition.IndexOf("SpellClassSet");
        int classCount = classes.Rows.Count;
        foreach (int row in Enumerable.Range(0, classes.Rows.Count).OrderBy(row => Name(classes, row, "Name")))
        {
            uint id = classes.Key(row);
            uint set = classSet >= 0 ? classes.GetRaw(row, classSet) : 0;
            HashSet<uint> found = new HashSet<uint>();
            if (family >= 0 && set > 0)
            {
                for (int spell = 0; spell < spells.Rows.Count; ++spell)
                {
                    if (spells.GetRaw(spell, family) == set)
                        found.Add(spells.Key(spell));
                }
            }

            if (abilities != null && id is > 0 and <= 32)
                AddMasked(abilities, "ClassMask", 1u << (int)(id - 1), classCount, found);
            yield return new SpellGroup("Class: " + Name(classes, row, "Name"), found);
        }
    }

    // A race's spells: skill line rows for that race alone, and the lines only that race learns (its racials)
    private static IEnumerable<SpellGroup> Races(DbcTable races, DbcTable? abilities, DbcTable? skillRaceClass)
    {
        if (abilities == null)
            yield break;
        int skill = abilities.Definition.IndexOf("SkillLine");
        int spell = abilities.Definition.IndexOf("Spell");
        Dictionary<uint, uint> skillRaces = new Dictionary<uint, uint>();
        if (skillRaceClass != null)
        {
            int skillId = skillRaceClass.Definition.IndexOf("SkillID");
            int raceMask = skillRaceClass.Definition.IndexOf("RaceMask");
            for (int row = 0; row < skillRaceClass.Rows.Count && skillId >= 0 && raceMask >= 0; ++row)
            {
                uint id = skillRaceClass.GetRaw(row, skillId);
                skillRaces[id] = skillRaces.GetValueOrDefault(id) | skillRaceClass.GetRaw(row, raceMask);
            }
        }

        foreach (int row in Enumerable.Range(0, races.Rows.Count).OrderBy(row => Name(races, row, "Name")))
        {
            uint id = races.Key(row);
            if (id is 0 or > 32)
                continue;
            uint bit = 1u << (int)(id - 1);
            HashSet<uint> found = new HashSet<uint>();
            AddMasked(abilities, "RaceMask", bit, 1, found);
            for (int ability = 0; ability < abilities.Rows.Count && skill >= 0 && spell >= 0; ++ability)
            {
                if (skillRaces.TryGetValue(abilities.GetRaw(ability, skill), out uint mask) && mask == bit)
                    found.Add(abilities.GetRaw(ability, spell));
            }

            yield return new SpellGroup("Race: " + Name(races, row, "Name"), found);
        }
    }

    private static IEnumerable<SpellGroup> Professions(DbcTable skillLines, DbcTable abilities)
    {
        int category = skillLines.Definition.IndexOf("CategoryID");
        int skill = abilities.Definition.IndexOf("SkillLine");
        int spell = abilities.Definition.IndexOf("Spell");
        if (category < 0 || skill < 0 || spell < 0)
            yield break;
        Dictionary<uint, HashSet<uint>> bySkill = new Dictionary<uint, HashSet<uint>>();
        for (int row = 0; row < abilities.Rows.Count; ++row)
        {
            uint line = abilities.GetRaw(row, skill);
            if (!bySkill.TryGetValue(line, out HashSet<uint>? set))
                bySkill[line] = set = new HashSet<uint>();
            set.Add(abilities.GetRaw(row, spell));
        }

        foreach (int row in Enumerable.Range(0, skillLines.Rows.Count)
                                      .OrderBy(row => Name(skillLines, row, "DisplayName")))
        {
            uint id = skillLines.Key(row);
            uint kind = skillLines.GetRaw(row, category);
            bool profession = kind == PROFESSIONS || (kind == SECONDARY_SKILLS && SecondaryProfessions.Contains(id));
            if (profession && bySkill.TryGetValue(id, out HashSet<uint>? found))
                yield return new SpellGroup("Profession: " + Name(skillLines, row, "DisplayName"), found);
        }
    }

    // Skill line rows whose mask has the bit, leaving out masks shared by more than most of them (every class)
    private static void AddMasked(DbcTable abilities, string column, uint bit, int most, HashSet<uint> found)
    {
        int mask = abilities.Definition.IndexOf(column);
        int spell = abilities.Definition.IndexOf("Spell");
        if (mask < 0 || spell < 0)
            return;
        for (int row = 0; row < abilities.Rows.Count; ++row)
        {
            uint value = abilities.GetRaw(row, mask);
            if ((value & bit) != 0 && BitOperations.PopCount(value) <= Math.Max(1, most / 2))
                found.Add(abilities.GetRaw(row, spell));
        }
    }

    private static string Name(DbcTable table, int row, string field)
    {
        int column = table.Definition.IndexOf(field + "_Lang_" + SpellRecord.ENGLISH);
        string name = column >= 0 ? table.GetString(row, column) : string.Empty;
        return name.Length > 0 ? name : table.Key(row).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
