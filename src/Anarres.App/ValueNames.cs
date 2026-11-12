using Anarres.Core.Dbc;
using Anarres.Core.Spells;

namespace Anarres.App;

// Named lists for cells, kept per column (and per item class for subclasses) so drawing a grid stays quick
public sealed class ValueNames
{
    private readonly Session _session;
    private readonly Dictionary<(string File, int Column, int Depends), EnumTable?> _lists =
        new Dictionary<(string, int, int), EnumTable?>();

    public ValueNames(Session session)
    {
        _session = session;
    }

    // The tables that name values were loaded or changed, so lists are made again
    public void Forget()
    {
        _lists.Clear();
    }

    public EnumTable? For(DbcTable table, int column, int row)
    {
        if (!ColumnValues.Has(table.Definition, column))
            return null;
        // Only an item subclass mask depends on another column of its row
        int depends = 0;
        if (table.Definition.Columns[column].Name == "EquippedItemSubclass" && row >= 0)
        {
            int classColumn = table.Definition.IndexOf("EquippedItemClass");
            depends = classColumn < 0 ? 0 : table.GetInt(row, classColumn);
        }

        (string, int, int) key = (table.Definition.File, column, depends);
        if (!_lists.TryGetValue(key, out EnumTable? list))
        {
            list = ColumnValues.For(table, column, row, _session.NamingTable);
            _lists[key] = list;
        }

        return list;
    }
}
