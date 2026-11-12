using System.Data.Common;
using Anarres.Core.Dbc;
using Microsoft.Data.Sqlite;

namespace Anarres.Core.Storage;

// A project kept in one local SQLite file; nothing else needs to be running
public sealed class SqliteDbcStore : SqlDbcStore
{
    public const string EXTENSION = ".anarres";

    private readonly string _path;

    private SqliteDbcStore(SqliteConnection connection, string path) : base(connection, TableNaming.DbcName)
    {
        _path = path;
    }

    public override string Description => "SQLite: " + _path;

    protected override string TextType => "TEXT";

    protected override string CacheKey => "sqlite:" + Path.GetFullPath(_path);

    public static async Task<SqliteDbcStore> OpenAsync(string path, CancellationToken cancellation = default)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder { DataSource = path };
        SqliteConnection connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellation);
        SqliteDbcStore store = new SqliteDbcStore(connection, path);
        // Write-ahead logging keeps big imports fast and the file whole if Anarres or the computer stops mid-save
        await store.ExecuteAsync("PRAGMA journal_mode = WAL", null, cancellation);
        await store.ExecuteAsync("PRAGMA synchronous = NORMAL", null, cancellation);
        await store.TrackOlderTablesAsync(cancellation);
        return store;
    }

    protected override async Task<string?> CurrentStampAsync(string file, string table,
                                                             CancellationToken cancellation)
    {
        if (!await TableExistsAsync(STAMP_TABLE, cancellation))
            return null;
        object? value = await ScalarAsync("SELECT " + Quote("current") + " FROM " + Quote(STAMP_TABLE) + " WHERE " +
                                          Quote("file") + " = @file", cancellation, ("@file", file));
        return value as string;
    }

    protected override async Task<string> ImportStampAsync(string file, string table, CancellationToken cancellation)
    {
        await CreateStampTableAsync(null, cancellation);
        await TrackAsync(file, table, cancellation);
        return Guid.NewGuid().ToString("N");
    }

    // Triggers give the table a new token on every change, including edits made with other SQLite tools
    private async Task TrackAsync(string file, string table, CancellationToken cancellation)
    {
        string update = "UPDATE " + Quote(STAMP_TABLE) + " SET " + Quote("current") + " = lower(hex(randomblob(8))) " +
                        "WHERE " + Quote("file") + " = '" + file.Replace("'", "''") + "';";
        foreach (string action in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            string trigger = Quote("anarres_" + table + "_" + action.ToLowerInvariant());
            await ExecuteAsync("CREATE TRIGGER IF NOT EXISTS " + trigger + " AFTER " + action + " ON " +
                               Quote(table) + " BEGIN " + update + " END", null, cancellation);
        }
    }

    // Projects made before tables were tracked: their tables start being tracked, but count as edited
    private async Task TrackOlderTablesAsync(CancellationToken cancellation)
    {
        if (!await TableExistsAsync(META_TABLE, cancellation))
            return;
        await CreateStampTableAsync(null, cancellation);
        foreach (DbcDefinition definition in await StoredDefinitionsAsync(cancellation))
        {
            object? tracked = await ScalarAsync("SELECT COUNT(*) FROM " + Quote(STAMP_TABLE) + " WHERE " +
                                                Quote("file") + " = @file", cancellation, ("@file", definition.File));
            string table = TableName(definition);
            if (Convert.ToInt64(tracked) > 0 || !await TableExistsAsync(table, cancellation))
                continue;
            await TrackAsync(definition.File, table, cancellation);
            await SaveStampAsync(definition.File, null, null, cancellation);
        }
    }

    protected override int RowsPerInsert(int columns)
    {
        return 1;
    }

    protected override string Quote(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    protected override string SqlType(DbcColumnType type)
    {
        switch (type)
        {
            case DbcColumnType.Float:
                return "REAL";
            case DbcColumnType.String:
                return "TEXT";
            default:
                return "INTEGER";
        }
    }

    protected override object Parameter(DbcColumnType type, uint raw)
    {
        switch (type)
        {
            case DbcColumnType.Float:
                return (double)BitConverter.UInt32BitsToSingle(raw);
            case DbcColumnType.Int32:
            case DbcColumnType.SByte:
            case DbcColumnType.Int16:
                return (long)unchecked((int)raw);
            default:
                return (long)raw;
        }
    }

    protected override async Task SwapInAsync(string staging, string name, bool replacing,
                                              CancellationToken cancellation)
    {
        await using DbTransaction transaction = await Connection.BeginTransactionAsync(cancellation);
        await ExecuteAsync("DROP TABLE IF EXISTS " + Quote(name), transaction, cancellation);
        await ExecuteAsync("ALTER TABLE " + Quote(staging) + " RENAME TO " + Quote(name), transaction, cancellation);
        await transaction.CommitAsync(cancellation);
    }

    protected override async Task<bool> TableExistsAsync(string table, CancellationToken cancellation)
    {
        object? found = await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND " +
                                          "name = @name COLLATE NOCASE", cancellation, ("@name", table));
        return Convert.ToInt64(found) > 0;
    }

    protected override async Task<List<string>> ColumnsAsync(string table, CancellationToken cancellation)
    {
        List<string> columns = new List<string>();
        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(" + Quote(table) + ")";
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellation);
        int nameOrdinal = reader.GetOrdinal("name");
        while (await reader.ReadAsync(cancellation))
            columns.Add(reader.GetString(nameOrdinal));
        return columns;
    }

    protected override async Task<List<string>> TablesAsync(CancellationToken cancellation)
    {
        List<string> tables = new List<string>();
        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
            tables.Add(reader.GetString(0));
        return tables;
    }
}
