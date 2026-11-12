using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Settings;
using Anarres.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Anarres.Core.Tests;

public static class CacheTests
{
    // Enough rows of the sample's 8 columns for the cache to keep the table
    private const int BIG_ROWS = 13_000;

    private static DbcDefinitions Definitions()
    {
        DbcDefinitions definitions = new DbcDefinitions();
        definitions.Add(DbcTests.SampleDefinition());
        return definitions;
    }

    private static DbcTable BigTable()
    {
        DbcTable table = new DbcTable(DbcTests.SampleDefinition());
        for (uint id = 1; id <= BIG_ROWS; ++id)
        {
            int row = table.AddRow(id);
            table.SetInt(row, 1, (int)id * 3);
            table.SetString(row, 3, "Spell " + id);
        }

        table.AcceptChanges();
        return table;
    }

    public static async Task KeepsBigTablesUntilTheDatabaseChanges()
    {
        using TempFolder temp = new TempFolder();
        DbcCache cache = new DbcCache(temp.File("cache"));
        string project = temp.File("p.anarres");
        using (SqliteDbcStore store = await SqliteDbcStore.OpenAsync(project))
        {
            store.Cache = cache;
            await store.ImportAsync(BigTable());
            Assert.True(cache.SizeBytes() > 0, "the import is kept");

            DbcTable loaded = await store.LoadAsync("Sample.dbc", Definitions());
            Assert.Equal(BIG_ROWS, loaded.Rows.Count, "rows from the copy");
            Assert.Equal("Spell 77", loaded.GetString(loaded.FindRow(77), 3), "strings from the copy");

            loaded.SetString(loaded.FindRow(77), 3, "Saved Name");
            await store.SaveChangesAsync(loaded);
            DbcTable afterSave = await store.LoadAsync("Sample.dbc", Definitions());
            Assert.Equal("Saved Name", afterSave.GetString(afterSave.FindRow(77), 3), "the copy follows saves");
        }

        // Another tool edits the project file; the copy no longer matches, so the database is read
        await using (SqliteConnection other = new SqliteConnection("Data Source=" + project))
        {
            await other.OpenAsync();
            await using SqliteCommand command = other.CreateCommand();
            command.CommandText = "UPDATE \"Sample\" SET \"Name\" = 'Edited Elsewhere' WHERE \"ID\" = 77";
            await command.ExecuteNonQueryAsync();
        }

        using (SqliteDbcStore reopened = await SqliteDbcStore.OpenAsync(project))
        {
            reopened.Cache = cache;
            DbcTable loaded = await reopened.LoadAsync("Sample.dbc", Definitions());
            Assert.Equal("Edited Elsewhere", loaded.GetString(loaded.FindRow(77), 3), "outside edits are seen");
        }
    }

    public static void IgnoresBrokenCopies()
    {
        using TempFolder temp = new TempFolder();
        DbcCache cache = new DbcCache(temp.File("cache"));
        DbcTable table = BigTable();
        cache.Write("store", table, "stamp-1");
        Assert.True(cache.TryRead("store", table.Definition, "stamp-1") != null, "read back");
        Assert.True(cache.TryRead("store", table.Definition, "stamp-2") == null, "another stamp");
        Assert.True(cache.TryRead("other", table.Definition, "stamp-1") == null, "another database");

        DbcDefinition changed = DbcTests.SampleDefinition();
        changed.Columns[1] = new DbcColumn("Value", DbcColumnType.Float);
        Assert.True(cache.TryRead("store", changed, "stamp-1") == null, "another layout");

        foreach (string file in Directory.EnumerateFiles(cache.Folder, "*.dbc", SearchOption.AllDirectories))
            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
        Assert.True(cache.TryRead("store", table.Definition, "stamp-1") == null, "a damaged copy");
        cache.Clear();
        Assert.Equal(0L, cache.SizeBytes(), "cleared");
    }

    public static async Task TracksEditedTables()
    {
        using TempFolder temp = new TempFolder();
        string project = temp.File("p.anarres");
        using (SqliteDbcStore store = await SqliteDbcStore.OpenAsync(project))
        {
            await store.ImportAsync(DbcTests.SampleTable());
            Assert.Equal(0, (await store.EditedTablesAsync()).Count, "nothing edited after import");

            DbcTable table = await store.LoadAsync("Sample.dbc", Definitions());
            table.SetString(table.FindRow(10), 3, "Changed");
            await store.SaveChangesAsync(table);
            List<EditedTable> edited = await store.EditedTablesAsync();
            Assert.Equal("Sample.dbc", string.Join(",", edited.Select(entry => entry.File)), "edited after save");
            Assert.True(edited[0].Known, "tracked since import");

            // Undoing the edit by hand and checking against the clean file clears the mark
            table.SetString(table.FindRow(10), 3, DbcTests.SampleTable().GetString(0, 3));
            await store.SaveChangesAsync(table);
            string clean = temp.File("clean");
            Directory.CreateDirectory(clean);
            DbcFile.Write(DbcTests.SampleTable(), Path.Combine(clean, "Sample.dbc"));
            ProjectSettings settings = new ProjectSettings { SourceDbcFolder = clean };
            List<string> still = await Deployment.CheckEditedAsync(store, settings, new[] { "Sample.dbc" },
                                                                   Definitions());
            Assert.Equal(0, still.Count, "matches the clean file again");
            Assert.Equal(0, (await store.EditedTablesAsync()).Count, "remembered");
        }

        // A project from before tracking: its tables are compared with the clean files once
        await using (SqliteConnection other = new SqliteConnection("Data Source=" + project))
        {
            await other.OpenAsync();
            await using SqliteCommand command = other.CreateCommand();
            command.CommandText = "DROP TABLE \"" + SqlDbcStore.STAMP_TABLE + "\"";
            await command.ExecuteNonQueryAsync();
        }

        using (SqliteDbcStore older = await SqliteDbcStore.OpenAsync(project))
        {
            List<EditedTable> unknown = await older.EditedTablesAsync();
            Assert.True(unknown.Count == 1 && !unknown[0].Known, "unknown before the check");
            ProjectSettings settings = new ProjectSettings { SourceDbcFolder = temp.File("clean") };
            List<string> files = await Deployment.EditedFilesAsync(older, settings, Definitions(),
                                                                   new[] { "Unsaved.dbc" });
            Assert.Equal("Unsaved.dbc", string.Join(",", files), "same as clean, so only the unsaved table");
            Assert.Equal(0, (await older.EditedTablesAsync()).Count, "the check is remembered");
        }
    }

    // Runs against a real server when ANARRES_TEST_MYSQL is host:port/database (user and password acore)
    public static async Task KeepsMySqlTablesByChecksum()
    {
        string? target = Environment.GetEnvironmentVariable("ANARRES_TEST_MYSQL");
        if (string.IsNullOrEmpty(target))
            return;
        string[] parts = target.Split('/', 2);
        string[] host = parts[0].Split(':');
        MySqlSettings settings = new MySqlSettings(host[0], host.Length > 1 ? int.Parse(host[1]) : 3306, "acore",
                                                   "acore", parts[1]);
        using TempFolder temp = new TempFolder();
        DbcCache cache = new DbcCache(temp.File("cache"));
        using MySqlDbcStore store = await MySqlDbcStore.OpenAsync(settings, TableNaming.DbcName, createDatabase: true);
        store.Cache = cache;
        await store.ImportAsync(BigTable());
        Assert.Equal(0, (await store.EditedTablesAsync()).Count, "nothing edited after import");

        DbcTable loaded = await store.LoadAsync("Sample.dbc", Definitions());
        loaded.SetString(loaded.FindRow(5), 3, "MySQL Edit");
        await store.SaveChangesAsync(loaded);
        DbcTable again = await store.LoadAsync("Sample.dbc", Definitions());
        Assert.Equal("MySQL Edit", again.GetString(again.FindRow(5), 3), "saved and kept");
        Assert.Equal("Sample.dbc", string.Join(",", (await store.EditedTablesAsync()).Select(entry => entry.File)),
                     "edited by checksum");
    }
}
