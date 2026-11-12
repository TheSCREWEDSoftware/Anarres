using Anarres.Core.Dbc;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;

namespace Anarres.Core.Spells;

// Text is the locale's column filled in; Rows says how many rows had it, out of Total
public sealed record DetectedLocale(string Locale, string File, int Rows, int Total);

// Tells which language a set of .dbc files is in: each text field has 16 columns, and the client's own is filled
public static class LocaleDetector
{
    // Small files with names in them first, so a folder is read quickly
    private static readonly string[] Probes = { "ChrRaces.dbc", "ChrClasses.dbc", "Spell.dbc" };

    // The column filled in most often, among the first text field's 16
    public static DetectedLocale? Detect(DbcTable table)
    {
        List<DbcColumn> columns = table.Definition.Columns;
        int first = columns.FindIndex(column => column.Type == DbcColumnType.String &&
                                                column.Name.EndsWith("_Lang_" + SpellRecord.ENGLISH,
                                                                     StringComparison.OrdinalIgnoreCase));
        if (first < 0 || first + SpellRecord.TEXT_COLUMNS > columns.Count || table.Rows.Count == 0)
            return null;
        int[] filled = new int[SpellRecord.TEXT_COLUMNS];
        for (int row = 0; row < table.Rows.Count; ++row)
        {
            for (int slot = 0; slot < SpellRecord.TEXT_COLUMNS; ++slot)
            {
                if (table.GetString(row, first + slot).Length > 0)
                    ++filled[slot];
            }
        }

        int best = Array.IndexOf(filled, filled.Max());
        return filled[best] == 0
                   ? null
                   : new DetectedLocale(SpellRecord.SlotName(best), table.Definition.File, filled[best],
                                        table.Rows.Count);
    }

    // A folder of .dbc files, such as the clean files or a server's dbc folder
    public static DetectedLocale? DetectFolder(string folder, DbcDefinitions? definitions = null)
    {
        folder = ProjectSettings.ResolveServerDbc(folder);
        if (!Directory.Exists(folder))
            return null;
        foreach (string probe in Probes)
        {
            string? path = Directory.EnumerateFiles(folder, probe, Files.AnyCase).FirstOrDefault();
            if (path == null || new FileInfo(path).Length == 0)
                continue;
            try
            {
                if (Detect(DbcFile.Read(path, definitions)) is { } found)
                    return found;
            }
            catch (InvalidDataException)
            {
                // Try the next file
            }
        }

        return null;
    }

    // The client's MPQs for one locale folder, without the patch Anarres builds
    public static DetectedLocale? DetectClient(string dataFolder, string locale, string? skip = null,
                                               DbcDefinitions? definitions = null)
    {
        if (!Directory.Exists(dataFolder))
            return null;
        using ClientArchives archives = ClientArchives.Open(dataFolder, locale, skip);
        foreach (string probe in Probes)
        {
            byte[]? data = archives.TryRead(ClientArchives.DBC_FOLDER + "\\" + probe);
            if (data is not { Length: > 0 })
                continue;
            try
            {
                if (Detect(DbcFile.Read(data, probe, definitions)) is { } found)
                    return found;
            }
            catch (InvalidDataException)
            {
                // Try the next file
            }
        }

        return null;
    }
}
