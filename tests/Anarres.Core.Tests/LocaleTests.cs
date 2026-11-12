using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Mpq;
using Anarres.Core.Spells;
using Anarres.Core.Storage;

namespace Anarres.Core.Tests;

public static class LocaleTests
{
    // A table with a Name field of 16 text columns named as AzerothCore names them
    private static DbcTable Names(int slot, params string[] names)
    {
        DbcDefinition definition = new DbcDefinition
        {
            File = "ChrRaces.dbc",
            Columns = new List<DbcColumn> { new DbcColumn("ID", DbcColumnType.UInt32) }
        };
        foreach (string locale in SpellRecord.Locales)
            definition.Columns.Add(new DbcColumn("Name_Lang_" + locale, DbcColumnType.String));
        definition.Columns.Add(new DbcColumn("Name_Lang_Mask", DbcColumnType.UInt32));
        DbcTable table = new DbcTable(definition);
        for (int i = 0; i < names.Length; ++i)
            table.SetString(table.AddRow((uint)i + 1), 1 + slot, names[i]);
        return table;
    }

    public static void TellsTheLanguageByColumn()
    {
        Assert.Equal("enUS", LocaleDetector.Detect(Names(0, "Human", "Orc"))!.Locale, "first column");
        // The client keeps German in the 4th column, which AzerothCore calls Name_Lang_deDE's neighbour enCN
        DetectedLocale german = LocaleDetector.Detect(Names(3, "Mensch", "Orc", "Zwerg"))!;
        Assert.Equal("deDE", german.Locale, "4th column");
        Assert.Equal(3, german.Rows, "rows with text");
        Assert.True(LocaleDetector.Detect(Names(0)) == null, "no rows");

        Assert.Equal(0, SpellRecord.Slot("enGB"), "enGB shares enUS's column");
        Assert.Equal(4, SpellRecord.Slot("enCN"), "enCN shares zhCN's column");
        Assert.Equal(-1, SpellRecord.Slot("ptBR"), "not a 3.3.5a client locale");
        DbcTable table = Names(3, "Mensch");
        Assert.Equal("Name_Lang_frFR", table.Definition.Columns[SpellRecord.TextColumn(table.Definition, "Name",
                                                                                       "deDE")].Name,
                     "deDE text sits in AzerothCore's frFR-named column");
    }

    public static void TellsARealFolder()
    {
        string? folder = TestData.DbcFolder();
        if (folder == null)
            return;
        DetectedLocale? found = LocaleDetector.DetectFolder(folder);
        Assert.True(found != null, "found a language");
        Assert.Equal("enUS", found!.Locale, "the test files are enUS");
    }

    // An empty file, like the client's CharVariations.dbc, is skipped and the rest still imported
    public static async Task ImportGoesOnPastEmptyFiles()
    {
        using TempFolder temp = new TempFolder();
        string folder = Directory.CreateDirectory(temp.File("dbc")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "CharVariations.dbc"), Array.Empty<byte>());
        DbcFile.Write(DbcTests.SampleTable(), Path.Combine(folder, "Sample.dbc"));
        DbcDefinitions definitions = new DbcDefinitions();
        definitions.Add(DbcTests.SampleDefinition());

        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("p.anarres"));
        List<ImportResult> results = await Deployment.ImportFolderAsync(store, folder, definitions);
        Assert.Equal(2, results.Count, "both files reported");
        Assert.True(results[0].Skipped && results[0].Problem != null, "the empty file skipped with a reason");
        Assert.Equal("Sample.dbc", string.Join(",", (await store.ListTablesAsync()).Select(table => table.File)),
                     "the other file imported");
    }

    // All available: each locale's MPQ fills its own language column of the one imported table
    public static async Task ImportFillsEveryLocale()
    {
        using TempFolder temp = new TempFolder();
        string data = Directory.CreateDirectory(temp.File("Data")).FullName;
        (string Locale, int Slot, string Name)[] clients = { ("enUS", 0, "Human"), ("deDE", 3, "Mensch") };
        foreach ((string locale, int slot, string name) in clients)
        {
            Directory.CreateDirectory(Path.Combine(data, locale));
            MpqWriter.Write(Path.Combine(data, locale, "locale-" + locale + ".MPQ"),
                            new List<(string, byte[])>
                            {
                                ("DBFilesClient\\ChrRaces.dbc", DbcFile.Write(Names(slot, name)))
                            });
        }

        DbcDefinitions definitions = new DbcDefinitions();
        definitions.Add(Names(0).Definition);
        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("p.anarres"));
        await Deployment.ImportClientAsync(store, data, "enUS", definitions,
                                           otherLocales: new[] { "deDE" });
        DbcTable table = await store.LoadAsync("ChrRaces.dbc", definitions);
        Assert.Equal("Human", table.GetString(0, 1), "enUS");
        Assert.Equal("Mensch", table.GetString(0, 1 + 3), "deDE from its own folder");
    }
}
