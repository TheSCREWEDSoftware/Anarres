using System.Globalization;
using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// One thing that looks wrong with a spell
public sealed record SpellProblem(uint SpellId, string SpellName, string Text);

// Checks spells for what the client or server trips over: missing indexes, missing triggers, broken auras
public static class SpellValidator
{
    private const int APPLY_AURA = 6;
    private const int PROC_TRIGGER_SPELL = 42;

    // Effects that apply their EffectAura (apply aura, area auras, persistent area aura and the rest)
    private static readonly HashSet<uint> AuraEffects = new HashSet<uint> { 6, 27, 35, 65, 119, 128, 129, 143 };

    // Auras that tick and need a period
    private static readonly HashSet<uint> PeriodicAuras = new HashSet<uint> { 3, 8, 20, 21, 23, 24, 53, 64, 226 };

    // Lookups by file; a table that isn't there isn't checked against
    public static List<SpellProblem> Check(DbcTable spells, IEnumerable<int> rows, Func<string, DbcTable?> tables)
    {
        List<SpellProblem> problems = new List<SpellProblem>();
        (string Column, string File, bool ZeroOk)[] references =
        {
            ("CastingTimeIndex", "SpellCastTimes.dbc", false),
            ("DurationIndex", "SpellDuration.dbc", true),
            ("RangeIndex", "SpellRange.dbc", false),
            ("SpellIconID", "SpellIcon.dbc", true),
            ("ActiveIconID", "SpellIcon.dbc", true),
            ("SpellVisualID_1", "SpellVisual.dbc", true),
            ("SpellVisualID_2", "SpellVisual.dbc", true),
            ("RuneCostID", "SpellRuneCost.dbc", true),
            ("SpellDifficultyID", "SpellDifficulty.dbc", true),
            ("SpellDescriptionVariableID", "SpellDescriptionVariables.dbc", true),
            ("Category", "SpellCategory.dbc", true),
            ("EffectRadiusIndex_1", "SpellRadius.dbc", true),
            ("EffectRadiusIndex_2", "SpellRadius.dbc", true),
            ("EffectRadiusIndex_3", "SpellRadius.dbc", true)
        };
        foreach (int row in rows)
        {
            SpellRecord spell = new SpellRecord(spells, row);
            void Add(string text) => problems.Add(new SpellProblem(spell.Id, spell.Name, text));

            if (spell.Name.Length == 0)
                Add("has no name in enUS, so the client shows it blank.");
            foreach ((string column, string file, bool zeroOk) in references)
            {
                int index = spells.Definition.IndexOf(column);
                if (index < 0 || tables(file) is not { } table)
                    continue;
                uint value = spells.GetRaw(row, index);
                if ((value == 0 && zeroOk) || table.FindRow(value) >= 0)
                    continue;
                Add(column + " is " + value.ToString(CultureInfo.InvariantCulture) + ", which " + file +
                    " doesn't have.");
            }

            for (int effect = 1; effect <= SpellRecord.EFFECT_COUNT; ++effect)
                CheckEffect(spells, row, effect, Add);
        }

        return problems;
    }

    private static void CheckEffect(DbcTable spells, int row, int effect, Action<string> add)
    {
        uint type = Raw(spells, row, "Effect_" + effect);
        uint aura = Raw(spells, row, "EffectAura_" + effect);
        uint trigger = Raw(spells, row, "EffectTriggerSpell_" + effect);
        uint period = Raw(spells, row, "EffectAuraPeriod_" + effect);
        string name = "Effect " + effect;
        if (type == APPLY_AURA && aura == 0)
            add(name + " applies an aura but its Aura is 0.");
        if (aura != 0 && !AuraEffects.Contains(type))
            add(name + " has Aura " + aura + " but its Effect (" + type + ") doesn't apply auras, so it's ignored.");
        if (AuraEffects.Contains(type) && PeriodicAuras.Contains(aura) && period == 0)
            add(name + " has a periodic aura with no Aura period, so it never ticks.");
        if (AuraEffects.Contains(type) && aura == PROC_TRIGGER_SPELL && trigger == 0)
            add(name + " is a proc trigger with no Trigger spell.");
        if (trigger != 0 && spells.FindRow(trigger) < 0)
            add(name + "'s Trigger spell " + trigger + " isn't in Spell.dbc; that's fine only if it's a server-side " +
                "spell in spell_dbc.");
        if (type == 0 && (aura != 0 || trigger != 0))
            add(name + " has no Effect but other fields set; the client ignores them.");
    }

    private static uint Raw(DbcTable spells, int row, string column)
    {
        int index = spells.Definition.IndexOf(column);
        return index < 0 ? 0 : spells.GetRaw(row, index);
    }
}
