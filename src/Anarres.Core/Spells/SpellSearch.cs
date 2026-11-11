using System.Globalization;
using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// The spell list's search box: an ID, part of a name, or field:value such as effect:6, aura:3 or family:3
public static class SpellSearch
{
    private static readonly Dictionary<string, string[]> Fields = new Dictionary<string, string[]>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["effect"] = new[] { "Effect_1", "Effect_2", "Effect_3" },
        ["aura"] = new[] { "EffectAura_1", "EffectAura_2", "EffectAura_3" },
        ["target"] = new[] { "ImplicitTargetA_1", "ImplicitTargetA_2", "ImplicitTargetA_3", "ImplicitTargetB_1",
                             "ImplicitTargetB_2", "ImplicitTargetB_3" },
        ["trigger"] = new[] { "EffectTriggerSpell_1", "EffectTriggerSpell_2", "EffectTriggerSpell_3" },
        ["misc"] = new[] { "EffectMiscValue_1", "EffectMiscValue_2", "EffectMiscValue_3" },
        ["family"] = new[] { "SpellClassSet" },
        ["icon"] = new[] { "SpellIconID" },
        ["visual"] = new[] { "SpellVisualID_1", "SpellVisualID_2" },
        ["category"] = new[] { "Category" },
        ["mechanic"] = new[] { "Mechanic" },
        ["school"] = new[] { "SchoolMask" }
    };

    public static IEnumerable<string> FieldNames => Fields.Keys;

    // A test for each row; an empty query matches everything
    public static Func<int, bool> Build(DbcTable table, string query)
    {
        query = query.Trim();
        if (query.Length == 0)
            return _ => true;

        int colon = query.IndexOf(':');
        if (colon > 0)
        {
            string field = query[..colon].Trim();
            string value = query[(colon + 1)..].Trim();
            List<int> columns = Fields.TryGetValue(field, out string[]? names)
                                    ? names.Select(table.Definition.IndexOf).Where(index => index >= 0).ToList()
                                    : new List<int> { table.Definition.IndexOf(field) };
            columns.RemoveAll(index => index < 0);
            if (columns.Count > 0 && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                                   out long number))
            {
                uint raw = unchecked((uint)number);
                return row => columns.Any(column => table.Rows[row][column] == raw);
            }
        }

        int nameColumn = table.Definition.IndexOf("Name_Lang_enUS");
        bool isNumber = uint.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id);
        return row => (isNumber && table.Key(row) == id) ||
                      (nameColumn >= 0 && table.GetString(row, nameColumn)
                                               .Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
