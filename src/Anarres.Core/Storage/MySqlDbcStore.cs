using System.Data.Common;
using Anarres.Core.Dbc;
using MySqlConnector;

namespace Anarres.Core.Storage;

public sealed record MySqlSettings(string Host, int Port, string User, string Password, string Database)
{
    public const int DEFAULT_PORT = 3306;

    public string ConnectionString(bool withDatabase = true)
    {
        MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)Port,
            UserID = User,
            Password = Password,
            CharacterSet = "utf8mb4",
            AllowUserVariables = true
        };
        if (withDatabase)
            builder.Database = Database;
        return builder.ToString();
    }
}

// Tables in a MySQL or MariaDB database, such as acore_dbc_extracted beside acore_world, or acore_world itself
public sealed class MySqlDbcStore : SqlDbcStore
{
    // MySQL takes up to 65535 placeholders in one statement; this stays well under it
    private const int MAX_PARAMETERS = 30000;

    private readonly MySqlSettings _settings;

    private MySqlDbcStore(MySqlConnection connection, MySqlSettings settings, TableNaming naming)
        : base(connection, naming)
    {
        _settings = settings;
    }

    public override string Description => "MySQL: " + _settings.Database + " on " + _settings.Host + ":" +
                                          _settings.Port + (Naming == TableNaming.AzerothCore ? " (AzerothCore tables)"
                                                                                                : string.Empty);

    protected override string TextType => "MEDIUMTEXT";

    protected override string CacheKey => "mysql:" + _settings.Host + ":" + _settings.Port + "/" +
                                          _settings.Database + "/" + Naming;

    // createDatabase makes the database first when it doesn't exist yet
    public static async Task<MySqlDbcStore> OpenAsync(MySqlSettings settings, TableNaming naming,
                                                      bool createDatabase = false,
                                                      CancellationToken cancellation = default)
    {
        if (createDatabase)
        {
            await using MySqlConnection server = new MySqlConnection(settings.ConnectionString(withDatabase: false));
            await server.OpenAsync(cancellation);
            await using MySqlCommand create = server.CreateCommand();
            create.CommandText = "CREATE DATABASE IF NOT EXISTS `" + settings.Database.Replace("`", "``") +
                                 "` CHARACTER SET utf8mb4";
            await create.ExecuteNonQueryAsync(cancellation);
        }

        MySqlConnection connection = new MySqlConnection(settings.ConnectionString());
        await connection.OpenAsync(cancellation);
        return new MySqlDbcStore(connection, settings, naming);
    }

    protected override int RowsPerInsert(int columns)
    {
        return Math.Max(1, MAX_PARAMETERS / Math.Max(1, columns));
    }

    protected override string Quote(string identifier)
    {
        return "`" + identifier.Replace("`", "``") + "`";
    }

    // FLOAT comes back as text with about six digits; adding a double zero sends every bit of it
    protected override string SelectExpression(string column, DbcColumnType type)
    {
        return type == DbcColumnType.Float ? "(" + Quote(column) + " + 0E0)" : Quote(column);
    }

    protected override string SqlType(DbcColumnType type)
    {
        switch (type)
        {
            case DbcColumnType.Int32:
                return "INT";
            case DbcColumnType.Float:
                return "FLOAT";
            case DbcColumnType.String:
                return "TEXT";
            case DbcColumnType.Byte:
                return "TINYINT UNSIGNED";
            case DbcColumnType.SByte:
                return "TINYINT";
            case DbcColumnType.Int16:
                return "SMALLINT";
            case DbcColumnType.UInt16:
                return "SMALLINT UNSIGNED";
            default:
                return "INT UNSIGNED";
        }
    }

    protected override object Parameter(DbcColumnType type, uint raw)
    {
        switch (type)
        {
            case DbcColumnType.Float:
                return BitConverter.UInt32BitsToSingle(raw);
            case DbcColumnType.Int32:
            case DbcColumnType.SByte:
            case DbcColumnType.Int16:
                return unchecked((int)raw);
            default:
                return raw;
        }
    }

    // CHECKSUM TABLE sums the rows on the server and sends back one number, so nothing big crosses the network
    protected override async Task<string?> CurrentStampAsync(string file, string table,
                                                             CancellationToken cancellation)
    {
        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = "CHECKSUM TABLE " + Quote(table);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellation);
        if (!await reader.ReadAsync(cancellation) || reader.IsDBNull(1))
            return null;
        return Convert.ToString(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override async Task<string> ImportStampAsync(string file, string table, CancellationToken cancellation)
    {
        return await CurrentStampAsync(file, table, cancellation) ?? Guid.NewGuid().ToString("N");
    }

    // RENAME TABLE swaps both names at once, so readers never see the table missing
    protected override async Task SwapInAsync(string staging, string name, bool replacing,
                                              CancellationToken cancellation)
    {
        if (!replacing)
        {
            await ExecuteAsync("RENAME TABLE " + Quote(staging) + " TO " + Quote(name), null, cancellation);
            return;
        }

        string old = name + "_anarres_old";
        await ExecuteAsync("DROP TABLE IF EXISTS " + Quote(old), null, cancellation);
        await ExecuteAsync("RENAME TABLE " + Quote(name) + " TO " + Quote(old) + ", " + Quote(staging) + " TO " +
                           Quote(name), null, cancellation);
        await ExecuteAsync("DROP TABLE " + Quote(old), null, cancellation);
    }

    protected override async Task<bool> TableExistsAsync(string table, CancellationToken cancellation)
    {
        object? found = await ScalarAsync("SELECT COUNT(*) FROM information_schema.tables WHERE " +
                                          "table_schema = DATABASE() AND table_name = @name", cancellation,
                                          ("@name", table));
        return Convert.ToInt64(found) > 0;
    }

    protected override async Task<List<string>> ColumnsAsync(string table, CancellationToken cancellation)
    {
        List<string> columns = new List<string>();
        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() " +
                              "AND table_name = @name ORDER BY ordinal_position";
        AddParameter(command, "@name", table);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
            columns.Add(reader.GetString(0));
        return columns;
    }

    protected override async Task<List<string>> TablesAsync(CancellationToken cancellation)
    {
        List<string> tables = new List<string>();
        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE()";
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
            tables.Add(reader.GetString(0));
        return tables;
    }
}
