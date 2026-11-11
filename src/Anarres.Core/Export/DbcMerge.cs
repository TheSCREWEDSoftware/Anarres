using Anarres.Core.Dbc;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;

namespace Anarres.Core.Export;

// Compares and combines a table with the clean file it started from
public static class DbcMerge
{
    // The clean copy of a file: from the clean DBC folder, else the client's MPQs; null when neither has it
    public static DbcTable? LoadSource(ProjectSettings settings, string file, DbcDefinitions definitions)
    {
        string name = Path.GetFileName(file);
        if (!name.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase))
            name += ".dbc";
        if (settings.SourceDbcFolder.Length > 0 && Directory.Exists(settings.SourceDbcFolder))
        {
            string? path = Directory.EnumerateFiles(settings.SourceDbcFolder, name, Files.AnyCase).FirstOrDefault();
            if (path != null && new FileInfo(path).Length > 0)
                return DbcFile.Read(path, definitions);
        }

        string client = settings.ClientData();
        if (client.Length > 0 && Directory.Exists(client))
        {
            using ClientArchives archives = ClientArchives.Open(client, settings.Locale);
            byte[]? data = archives.TryRead(ClientArchives.DBC_FOLDER + "\\" + name);
            if (data is { Length: > 0 })
                return DbcFile.Read(data, name, definitions);
        }

        return null;
    }

    // The clean rows with the override rows laid over them by ID, new IDs added after them
    public static DbcTable Overlay(DbcTable clean, DbcTable overrides)
    {
        DbcTable result = new DbcTable(clean.Definition);
        for (int row = 0; row < clean.Rows.Count; ++row)
            result.Load(Copy(clean, row, result));

        HashSet<uint> added = new HashSet<uint>();
        List<uint[]> extra = new List<uint[]>();
        for (int row = 0; row < overrides.Rows.Count; ++row)
        {
            uint key = overrides.Key(row);
            int target = result.FindRow(key);
            if (target >= 0)
                result.Rows[target] = Copy(overrides, row, result);
            else if (added.Add(key))
                extra.Add(Copy(overrides, row, result));
        }

        foreach (uint[] cells in extra.OrderBy(cells => cells[0]))
            result.Load(cells);
        return result;
    }

    // Rows of the edited table that are new or differ from the clean file
    public static List<int> Changed(DbcTable clean, DbcTable edited)
    {
        List<int> changed = new List<int>();
        int columns = Math.Min(clean.ColumnCount, edited.ColumnCount);
        for (int row = 0; row < edited.Rows.Count; ++row)
        {
            int source = clean.FindRow(edited.Key(row));
            if (source < 0)
            {
                changed.Add(row);
                continue;
            }

            for (int column = 0; column < columns; ++column)
            {
                bool same = edited.Definition.Columns[column].Type == DbcColumnType.String
                                ? clean.GetString(source, column) == edited.GetString(row, column)
                                : clean.GetRaw(source, column) == edited.GetRaw(row, column);
                if (!same)
                {
                    changed.Add(row);
                    break;
                }
            }
        }

        return changed;
    }

    // A row's cells for another table: strings are moved into that table's pool
    private static uint[] Copy(DbcTable from, int row, DbcTable to)
    {
        uint[] cells = (uint[])from.Rows[row].Clone();
        for (int column = 0; column < cells.Length && column < to.ColumnCount; ++column)
        {
            if (to.Definition.Columns[column].Type == DbcColumnType.String)
                cells[column] = to.Intern(from.StringAt(cells[column]));
        }

        return cells;
    }
}
