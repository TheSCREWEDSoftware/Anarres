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

    public static async Task<SqliteDbcStore> OpenAsync(string path, CancellationToken cancellation = default)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder { DataSource = path };
        SqliteConnection connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellation);
        SqliteDbcStore store = new SqliteDbcStore(connection, path);
        // A rollback journal kept in memory makes big imports several times faster
        await store.ExecuteAsync("PRAGMA journal_mode = MEMORY", null, cancellation);
        await store.ExecuteAsync("PRAGMA synchronous = NORMAL", null, cancellation);
        return store;
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
