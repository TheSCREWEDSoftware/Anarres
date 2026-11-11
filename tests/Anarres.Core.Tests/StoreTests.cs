using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Settings;
using Anarres.Core.Storage;

namespace Anarres.Core.Tests;

public static class StoreTests
{
    private static DbcDefinitions Definitions()
    {
        DbcDefinitions definitions = new DbcDefinitions();
        definitions.Add(DbcTests.SampleDefinition());
        return definitions;
    }

    public static async Task ImportsLoadsAndSaves()
    {
        using TempFolder temp = new TempFolder();
        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("project.anarres"));
        await store.ImportAsync(DbcTests.SampleTable());

        List<StoredTable> tables = await store.ListTablesAsync();
        Assert.Equal(1, tables.Count, "one table");
        Assert.Equal(3L, tables[0].Rows, "rows");

        DbcTable table = await store.LoadAsync("Sample.dbc", Definitions());
        Assert.Equal(-5, table.GetInt(0, 1), "negative int");
        Assert.Equal(1.5f, table.GetFloat(0, 2), "float");
        Assert.Equal("Fireball", table.GetString(1, 3), "string");
        Assert.Equal(-1, table.GetInt(0, 7), "signed byte");

        table.SetString(2, 3, "Ice Lance");
        table.RemoveRow(0);
        table.CopyRow(0, 99);
        await store.SaveChangesAsync(table);
        Assert.True(!table.IsModified, "saved");

        DbcTable again = await store.LoadAsync("Sample", Definitions());
        Assert.Equal(3, again.Rows.Count, "one removed, one added");
        Assert.Equal(-1, again.FindRow(10), "removed row is gone");
        Assert.Equal("Ice Lance", again.GetString(again.FindRow(12), 3), "changed string");
        Assert.Equal("Fireball", again.GetString(again.FindRow(99), 3), "copied row");
    }

    public static async Task KeepsInferredDefinitions()
    {
        using TempFolder temp = new TempFolder();
        DbcDefinition definition = DbcTests.SampleDefinition();
        definition.File = "Custom.dbc";
        definition.IsInferred = true;
        DbcTable table = new DbcTable(definition);
        table.AddRow(1);
        using (SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("p.anarres")))
            await store.ImportAsync(table);

        using SqliteDbcStore reopened = await SqliteDbcStore.OpenAsync(temp.File("p.anarres"));
        DbcTable loaded = await reopened.LoadAsync("Custom.dbc", new DbcDefinitions());
        Assert.True(loaded.Definition.IsInferred, "the layout comes back from the project file");
        Assert.Equal(8, loaded.Definition.Columns.Count, "columns");
    }

    public static async Task HandlesRepeatedKeys()
    {
        using TempFolder temp = new TempFolder();
        DbcTable table = new DbcTable(DbcTests.SampleDefinition());
        table.AddRow(5);
        table.AddRow(5);
        table.SetString(1, 3, "second");
        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("p.anarres"));
        await store.ImportAsync(table);
        DbcTable loaded = await store.LoadAsync("Sample.dbc", Definitions());
        Assert.Equal(2, loaded.Rows.Count, "both rows kept");
        Assert.Equal("second", loaded.GetString(1, 3), "order kept");
    }

    public static async Task ExportsAndBuildsPatch()
    {
        using TempFolder temp = new TempFolder();
        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("p.anarres"));
        await store.ImportAsync(DbcTests.SampleTable());
        ProjectSettings settings = new ProjectSettings
        {
            ExportFolder = temp.File("Export"),
            ServerDbcFolder = temp.File("server-dbc"),
            ClientDataFolder = temp.File("Data"),
            PatchName = "patch-A.MPQ"
        };

        ExportResult result = await Deployment.ExportAsync(store, settings, new[] { "Sample.dbc" }, Definitions(),
                                                           copyToServer: true, buildPatch: true);
        Assert.True(File.Exists(Path.Combine(settings.ServerDbcFolder, "Sample.dbc")), "copied to the server");
        Assert.True(result.PatchPath != null && File.Exists(result.PatchPath), "patch written");
        using Anarres.Core.Mpq.MpqArchive patch = Anarres.Core.Mpq.MpqArchive.Open(result.PatchPath!);
        DbcTable fromPatch = DbcFile.Read(patch.Read("DBFilesClient\\Sample.dbc"), "Sample.dbc", Definitions());
        Assert.Equal("Frost Nova", fromPatch.GetString(2, 3), "the patch has the data");
    }

    public static void WritesSqlForAzerothCore()
    {
        DbcTable table = DbcTests.SampleTable();
        table.SetString(0, 3, "It's \\ new");
        string sql = SqlExport.Build(table, new[] { 0 }, "sample_dbc");
        Assert.True(sql.Contains("DELETE FROM `sample_dbc` WHERE `ID` IN (10);"), "delete first");
        Assert.True(sql.Contains("(10, -5, 1.5, 'It\\'s \\\\ new', 255, 0, 0, -1);"), "row values: " + sql);
    }
}
