namespace Anarres.Core.Dbc;

// Rows pasted into a table: how many went in, and how many got a new ID because theirs was taken
public sealed record PasteResult(int Added, int Renumbered, List<uint> Keys);

// Adds rows copied as tab-separated text; a first line of column names maps the cells, else they go in order
public static class RowPaste
{
    public static PasteResult Paste(DbcTable table, string text)
    {
        List<string[]> lines = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(line => line.Split('\t')).ToList();
        List<uint> keys = new List<uint>();
        if (lines.Count == 0)
            return new PasteResult(0, 0, keys);
        List<DbcColumn> columns = table.Definition.Columns;
        int[] map = Enumerable.Range(0, lines[0].Length).ToArray();
        if (lines[0].Any(cell => table.Definition.IndexOf(cell) >= 0))
        {
            map = lines[0].Select(cell => table.Definition.IndexOf(cell)).ToArray();
            lines.RemoveAt(0);
        }

        int renumbered = 0;
        table.BreakUndoStep();
        foreach (string[] cells in lines)
        {
            int keyCell = Array.IndexOf(map, 0);
            uint key = table.NextFreeKey();
            if (!table.Definition.IndexIsRowNumber && keyCell >= 0 && keyCell < cells.Length &&
                uint.TryParse(cells[keyCell], out uint wanted))
            {
                if (table.FindRow(wanted) < 0)
                    key = wanted;
                else
                    ++renumbered;
            }

            int row = table.AddRow(key);
            for (int cell = 0; cell < cells.Length && cell < map.Length; ++cell)
            {
                int column = map[cell];
                if (column <= 0 && !(column == 0 && table.Definition.IndexIsRowNumber) || column >= columns.Count)
                    continue;
                table.TrySetText(row, column, cells[cell].Replace("\\n", "\n"), out _);
            }

            keys.Add(key);
        }

        table.BreakUndoStep();
        return new PasteResult(keys.Count, renumbered, keys);
    }
}
