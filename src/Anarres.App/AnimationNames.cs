using Anarres.Core.Dbc;

namespace Anarres.App;

// Animation names from the client's AnimationData.dbc (Stand, Walk, SpellCastOmni...), so custom ones show too
public static class AnimationNames
{
    public const string FILE = "AnimationData.dbc";

    private static Dictionary<uint, string> _names = new Dictionary<uint, string>();

    public static void Use(DbcTable? table)
    {
        Dictionary<uint, string> names = new Dictionary<uint, string>();
        int column = table?.Definition.IndexOf("Name") ?? -1;
        if (table != null && column >= 0)
        {
            for (int row = 0; row < table.Rows.Count; ++row)
                names[table.Key(row)] = table.GetString(row, column);
        }

        _names = names;
    }

    public static string Name(uint id)
    {
        return _names.TryGetValue(id, out string? name) && name.Length > 0 ? name + " (" + id + ")"
                                                                             : "Animation " + id;
    }
}
