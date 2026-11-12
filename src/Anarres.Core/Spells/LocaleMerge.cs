using Anarres.Core.Dbc;
using Anarres.Core.Mpq;

namespace Anarres.Core.Spells;

// One locale's copy of a table, read from that locale's MPQs, with the text column it fills
public sealed record LocaleSource(string Locale, int Slot, DbcTable Table);

// Gathers each locale's own language column into one table, so a single Spell.dbc carries every language
public static class LocaleMerge
{
    // Locales with a column of their own first; enGB, enCN and enTW share one and only fill it when it's still empty
    public static List<string> Order(IEnumerable<string> locales)
    {
        List<string> list = locales.Distinct().ToList();
        return list.Where(locale => SpellRecord.ClientLocales.Contains(locale))
                   .Concat(list.Where(locale => !SpellRecord.ClientLocales.Contains(locale) &&
                                                SpellRecord.Slot(locale) >= 0))
                   .ToList();
    }

    // The file from one locale folder's MPQs, without skip (the patch Anarres builds, which holds the edits)
    public static LocaleSource? Read(string dataFolder, string locale, string file, string? skip,
                                     DbcDefinitions? definitions = null)
    {
        using ClientArchives archives = ClientArchives.Open(dataFolder, locale, skip);
        byte[]? data = archives.TryRead(ClientArchives.DBC_FOLDER + "\\" + Path.GetFileName(file));
        if (data is not { Length: > 0 })
            return null;
        DbcTable table = DbcFile.Read(data, Path.GetFileName(file), definitions);
        // The filled column is trusted over the folder's name, in case a folder holds another locale's files
        DetectedLocale? found = LocaleDetector.Detect(table);
        int slot = found != null ? Array.IndexOf(SpellRecord.ClientLocales, found.Locale) : SpellRecord.Slot(locale);
        return slot < 0 ? null : new LocaleSource(locale, slot, table);
    }

    // Copies the source's language column into target's rows with the same IDs; onlyEmpty keeps text already there
    public static int Apply(DbcTable target, LocaleSource source, bool onlyEmpty)
    {
        List<int> fields = TextFields(target.Definition);
        List<int> sourceFields = TextFields(source.Table.Definition);
        if (fields.Count == 0 || fields.Count != sourceFields.Count)
            return 0;
        int changed = 0;
        for (int row = 0; row < source.Table.Rows.Count; ++row)
        {
            int into = target.FindRow(source.Table.Key(row));
            if (into < 0)
                continue;
            bool rowChanged = false;
            for (int field = 0; field < fields.Count; ++field)
            {
                string text = source.Table.GetString(row, sourceFields[field] + source.Slot);
                int column = fields[field] + source.Slot;
                string current = target.GetString(into, column);
                if (text.Length == 0 || text == current || (onlyEmpty && current.Length > 0))
                    continue;
                target.SetString(into, column, text);
                rowChanged = true;
            }

            if (rowChanged)
                ++changed;
        }

        return changed;
    }

    // The first column of each text field (Name_Lang_enUS, Description_Lang_enUS...)
    private static List<int> TextFields(DbcDefinition definition)
    {
        List<int> fields = new List<int>();
        for (int column = 0; column < definition.Columns.Count; ++column)
        {
            DbcColumn info = definition.Columns[column];
            if (info.Type == DbcColumnType.String &&
                info.Name.EndsWith("_Lang_" + SpellRecord.ENGLISH, StringComparison.OrdinalIgnoreCase) &&
                column + SpellRecord.TEXT_COLUMNS <= definition.Columns.Count)
                fields.Add(column);
        }

        return fields;
    }
}
