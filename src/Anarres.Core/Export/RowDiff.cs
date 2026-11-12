using Anarres.Core.Dbc;

namespace Anarres.Core.Export;

// One cell that differs between two copies of a row
public sealed record CellDiff(int Column, string Name, string Old, string New);

// A row whose cells differ, or one only in the new table (Added) or only in the old one (Removed)
public sealed record RowChange(uint Key, bool Added, bool Removed, List<CellDiff> Cells);

// Compares rows of two copies of a table by ID, such as a spell against the clean Spell.dbc, or two .dbc files
public static class RowDiff
{
    // The cells of one row that differ from the row with the same ID in old (every non-empty cell when there's none)
    public static List<CellDiff> Row(DbcTable old, DbcTable current, int row)
    {
        int oldRow = old.FindRow(current.Key(row));
        List<CellDiff> cells = new List<CellDiff>();
        List<DbcColumn> columns = current.Definition.Columns;
        for (int column = 0; column < columns.Count; ++column)
        {
            int oldColumn = old.Definition.IndexOf(columns[column].Name);
            string now = current.GetText(row, column);
            string before = oldRow >= 0 && oldColumn >= 0 ? old.GetText(oldRow, oldColumn) : string.Empty;
            bool same = oldRow >= 0 && oldColumn >= 0 &&
                        (columns[column].Type == DbcColumnType.String
                             ? old.GetString(oldRow, oldColumn) == current.GetString(row, column)
                             : old.GetRaw(oldRow, oldColumn) == current.GetRaw(row, column));
            if (!same && (oldRow >= 0 || (now.Length > 0 && now != "0")))
                cells.Add(new CellDiff(column, columns[column].Name, before, now));
        }

        return cells;
    }

    // Every row added, removed or changed between two tables with the same layout
    public static List<RowChange> Tables(DbcTable old, DbcTable current)
    {
        List<RowChange> changes = new List<RowChange>();
        HashSet<uint> seen = new HashSet<uint>();
        for (int row = 0; row < current.Rows.Count; ++row)
        {
            uint key = current.Key(row);
            seen.Add(key);
            bool added = old.FindRow(key) < 0;
            List<CellDiff> cells = Row(old, current, row);
            if (added || cells.Count > 0)
                changes.Add(new RowChange(key, added, Removed: false, added ? new List<CellDiff>() : cells));
        }

        for (int row = 0; row < old.Rows.Count; ++row)
        {
            if (!seen.Contains(old.Key(row)))
                changes.Add(new RowChange(old.Key(row), Added: false, Removed: true, new List<CellDiff>()));
        }

        return changes;
    }

    // Puts one cell back as old has it
    public static void Revert(DbcTable old, DbcTable current, int row, int column)
    {
        int oldRow = old.FindRow(current.Key(row));
        int oldColumn = old.Definition.IndexOf(current.Definition.Columns[column].Name);
        if (oldRow < 0 || oldColumn < 0)
            return;
        if (current.Definition.Columns[column].Type == DbcColumnType.String)
            current.SetString(row, column, old.GetString(oldRow, oldColumn));
        else
            current.SetRaw(row, column, old.GetRaw(oldRow, oldColumn));
    }
}
