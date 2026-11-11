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
        // Rows with the same ID come only from files; editing can't make them
        DbcTable table = new DbcTable(DbcTests.SampleDefinition());
        table.Load(new uint[8] { 5, 0, 0, 0, 0, 0, 0, 0 });
        table.Load(new uint[8] { 5, 0, 0, table.Intern("second"), 0, 0, 0, 0 });
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

    public static async Task KeepsFileOrder()
    {
        using TempFolder temp = new TempFolder();
        DbcTable table = new DbcTable(DbcTests.SampleDefinition());
        foreach (uint id in new uint[] { 30, 10, 20 })
            table.AddRow(id);
        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("p.anarres"));
        await store.ImportAsync(table);
        DbcTable loaded = await store.LoadAsync("Sample.dbc", Definitions());
        Assert.Equal("30,10,20", string.Join(",", loaded.Rows.Select(row => row[0])), "rows in the file's order");
        Assert.True(DbcFile.Write(table).AsSpan().SequenceEqual(DbcFile.Write(loaded)), "same bytes");

        loaded.SetString(1, 3, "changed");
        await store.SaveChangesAsync(loaded);
        DbcTable again = await store.LoadAsync("Sample.dbc", Definitions());
        Assert.Equal("changed", again.GetString(1, 3), "saved in an unsorted table");
    }

    public static async Task ImportReplacesWholeTable()
    {
        using TempFolder temp = new TempFolder();
        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("p.anarres"));
        await store.ImportAsync(DbcTests.SampleTable());
        DbcTable smaller = new DbcTable(DbcTests.SampleDefinition());
        smaller.AddRow(1);
        await store.ImportAsync(smaller);
        Assert.Equal(1, (await store.LoadAsync("Sample.dbc", Definitions())).Rows.Count, "old rows gone");
        Assert.Equal(1, (await store.ListTablesAsync()).Count, "no staging table left behind");
    }

    public static void OverlaysAndFindsChanges()
    {
        DbcTable clean = DbcTests.SampleTable();
        DbcTable overrides = new DbcTable(DbcTests.SampleDefinition());
        int changed = overrides.AddRow(11);
        overrides.SetString(changed, 3, "Override");
        overrides.AddRow(5);

        DbcTable merged = DbcMerge.Overlay(clean, overrides);
        Assert.Equal(4, merged.Rows.Count, "three clean rows and one new");
        Assert.Equal("Override", merged.GetString(merged.FindRow(11), 3), "override wins");
        Assert.Equal("Frost Nova", merged.GetString(merged.FindRow(12), 3), "clean row kept");

        List<int> rows = DbcMerge.Changed(clean, merged);
        Assert.Equal("11,5", string.Join(",", rows.Select(row => merged.Key(row))), "changed and new rows");
    }

    public static void FindsServerAndClientFolders()
    {
        using TempFolder temp = new TempFolder();
        string data = Directory.CreateDirectory(temp.File("server-data")).FullName;
        Directory.CreateDirectory(Path.Combine(data, "dbc"));
        Directory.CreateDirectory(Path.Combine(data, "maps"));
        Assert.Equal(Path.Combine(data, "dbc"), ProjectSettings.ResolveServerDbc(data), "DataDir to its dbc folder");
        Assert.Equal(Path.Combine(data, "dbc"), ProjectSettings.ResolveServerDbc(Path.Combine(data, "dbc")),
                     "the dbc folder itself");

        string wow = Directory.CreateDirectory(temp.File("WoW")).FullName;
        Directory.CreateDirectory(Path.Combine(wow, "Data"));
        File.WriteAllBytes(Path.Combine(wow, "Data", "common.MPQ"), Array.Empty<byte>());
        Assert.Equal(Path.Combine(wow, "Data"), ProjectSettings.ResolveClientData(wow), "WoW folder to Data");
        Assert.Equal(Path.Combine(wow, "Data"), ProjectSettings.ResolveClientData(Path.Combine(wow, "Data")),
                     "Data itself");
        ProjectSettings settings = new ProjectSettings { ClientDataFolder = wow };
        Assert.Equal(Path.Combine(wow, "Cache"), settings.ClientCache(), "Cache beside Data");
        Assert.Equal(string.Empty, ProjectSettings.ResolveServerDbc(" "), "unset");
    }

    public static async Task AppliesToServerAndClient()
    {
        using TempFolder temp = new TempFolder();
        string wow = Directory.CreateDirectory(temp.File("WoW")).FullName;
        Directory.CreateDirectory(Path.Combine(wow, "Data"));
        Directory.CreateDirectory(Path.Combine(wow, "Cache", "WDB"));
        string dbc = Directory.CreateDirectory(Path.Combine(temp.File("server"), "dbc")).FullName;
        File.WriteAllText(Path.Combine(dbc, "Sample.dbc"), "the server's own file");

        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(temp.File("p.anarres"));
        await store.ImportAsync(DbcTests.SampleTable());
        ProjectSettings settings = new ProjectSettings
        {
            ExportFolder = temp.File("Export"),
            ServerDbcFolder = temp.File("server"),
            ClientDataFolder = wow,
            ApplyClearClientCache = true
        };

        ApplyResult result = await Deployment.ApplyAsync(store, settings, new[] { "Sample.dbc" }, Definitions());
        Assert.Equal(0, result.Problems.Count, "no problems: " + string.Join(" ", result.Problems));
        Assert.Equal("the server's own file",
                     File.ReadAllText(Path.Combine(settings.ExportFolder, Deployment.BACKUP_FOLDER, "Sample.dbc")),
                     "the server's copy kept");
        Assert.Equal(3, DbcFile.Read(Path.Combine(dbc, "Sample.dbc"), Definitions()).Rows.Count, "server replaced");
        Assert.True(File.Exists(Path.Combine(wow, "Data", "patch-A.MPQ")), "patch in the client's Data");
        Assert.True(!Directory.Exists(Path.Combine(wow, "Cache")), "cache emptied");

        await Deployment.ApplyAsync(store, settings, new[] { "Sample.dbc" }, Definitions());
        Assert.Equal("the server's own file",
                     File.ReadAllText(Path.Combine(settings.ExportFolder, Deployment.BACKUP_FOLDER, "Sample.dbc")),
                     "the first copy stays the backup");
    }
}
