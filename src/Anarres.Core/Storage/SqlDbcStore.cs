using System.Data.Common;
using Anarres.Core.Dbc;

namespace Anarres.Core.Storage;

// The SQL shared by the SQLite and MySQL stores; each subclass supplies its connection and dialect
public abstract class SqlDbcStore : IDbcStore
{
    public const string META_TABLE = "anarres_definitions";
    public const string ROW_COLUMN = "_row";
    public const string ID_COLUMN = "ID";

    private const int PROGRESS_STEP = 500;

    protected SqlDbcStore(DbConnection connection, TableNaming naming)
    {
        Connection = connection;
        Naming = naming;
    }

    public abstract string Description { get; }

    public TableNaming Naming { get; }

    protected DbConnection Connection { get; }

    // How many rows go into one INSERT; SQLite runs one prepared row at a time instead
    protected abstract int RowsPerInsert(int columns);

    protected abstract string Quote(string identifier);

    // How a column is read back; MySQL widens floats so its text protocol doesn't round them
    protected virtual string SelectExpression(string column, DbcColumnType type)
    {
        return Quote(column);
    }

    protected abstract string SqlType(DbcColumnType type);

    protected abstract string TextType { get; }

    protected abstract Task<bool> TableExistsAsync(string table, CancellationToken cancellation);

    protected abstract Task<List<string>> ColumnsAsync(string table, CancellationToken cancellation);

    protected abstract Task<List<string>> TablesAsync(CancellationToken cancellation);

    // The value to bind for a cell: unsigned columns as long, since SQLite has no unsigned integers
    protected abstract object Parameter(DbcColumnType type, uint raw);

    public string TableName(DbcDefinition definition)
    {
        if (Naming == TableNaming.AzerothCore)
        {
            if (definition.Table.Length == 0)
                throw new InvalidOperationException(definition.File + " has no AzerothCore table; use DBC names " +
                                                    "for this database to keep it there.");
            return definition.Table;
        }

        return definition.Name;
    }

    public async Task<List<StoredTable>> ListTablesAsync(CancellationToken cancellation = default)
    {
        HashSet<string> tables = new HashSet<string>(await TablesAsync(cancellation),
                                                     StringComparer.OrdinalIgnoreCase);
        List<StoredTable> result = new List<StoredTable>();
        List<DbcDefinition> candidates = Naming == TableNaming.AzerothCore
                                             ? DbcDefinitions.Default.All.Where(entry => entry.Table.Length > 0)
                                                             .ToList()
                                             : await StoredDefinitionsAsync(cancellation);
        foreach (DbcDefinition definition in candidates)
        {
            string table = TableName(definition);
            if (!tables.Contains(table))
                continue;
            long rows = Convert.ToInt64(await ScalarAsync("SELECT COUNT(*) FROM " + Quote(table), cancellation));
            result.Add(new StoredTable(definition.File, table, rows));
        }

        return result.OrderBy(entry => entry.File, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> ContainsAsync(string file, CancellationToken cancellation = default)
    {
        DbcDefinition? definition = await StoredDefinitionAsync(file, cancellation) ??
                                    DbcDefinitions.Default.Find(file);
        return definition != null && await TableExistsAsync(TableName(definition), cancellation);
    }

    public async Task ImportAsync(DbcTable table, IProgress<double>? progress = null,
                                  CancellationToken cancellation = default)
    {
        DbcDefinition definition = table.Definition;
        string name = TableName(definition);
        bool uniqueKeys = !definition.HasKey ||
                          table.Rows.Select(row => row[0]).Distinct().Count() == table.Rows.Count;
        bool rowColumn = definition.HasKey && !uniqueKeys;
        if (rowColumn && Naming == TableNaming.AzerothCore)
            throw new InvalidOperationException(definition.File + " has rows with the same ID, which " +
                                                definition.Table + " can't hold.");

        // AzerothCore's own tables keep their structure; only their rows are replaced
        bool keepTable = Naming == TableNaming.AzerothCore && await TableExistsAsync(name, cancellation);
        if (keepTable)
            await CheckColumnsAsync(definition, name, cancellation);

        await using DbTransaction transaction = await Connection.BeginTransactionAsync(cancellation);
        if (keepTable)
            await ExecuteAsync("DELETE FROM " + Quote(name), transaction, cancellation);
        else
        {
            await ExecuteAsync("DROP TABLE IF EXISTS " + Quote(name), transaction, cancellation);
            await ExecuteAsync(CreateTableSql(definition, name, rowColumn, uniqueKeys), transaction, cancellation);
        }

        await InsertRowsAsync(table, Enumerable.Range(0, table.Rows.Count).ToList(), name, rowColumn, replace: false,
                              transaction, progress, cancellation);
        if (Naming == TableNaming.DbcName)
            await SaveDefinitionAsync(definition, name, transaction, cancellation);
        await transaction.CommitAsync(cancellation);
        table.AcceptChanges();
        progress?.Report(1);
    }

    public async Task<DbcTable> LoadAsync(string file, DbcDefinitions definitions, IProgress<double>? progress = null,
                                          CancellationToken cancellation = default)
    {
        DbcDefinition definition = await StoredDefinitionAsync(file, cancellation) ?? definitions.Find(file) ??
                                   throw new InvalidOperationException("There's no definition for " + file + ".");
        string name = TableName(definition);
        if (!await TableExistsAsync(name, cancellation))
            throw new InvalidOperationException("The database has no table " + name + " yet. Import " +
                                                definition.File + " first.");

        List<string> existing = await ColumnsAsync(name, cancellation);
        bool rowColumn = existing.Contains(ROW_COLUMN, StringComparer.OrdinalIgnoreCase);
        await CheckColumnsAsync(definition, name, cancellation);
        long total = Convert.ToInt64(await ScalarAsync("SELECT COUNT(*) FROM " + Quote(name), cancellation));

        List<(string Name, DbcColumnType Type, int Cell)> columns = SqlColumns(definition);
        string order = rowColumn ? Quote(ROW_COLUMN) : Quote(columns[0].Name);
        string sql = "SELECT " + string.Join(", ", columns.Select(column => SelectExpression(column.Name,
                                                                                          column.Type))) + " FROM " +
                     Quote(name) + " ORDER BY " + order;
        DbcTable table = new DbcTable(definition);
        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellation);
        long read = 0;
        while (await reader.ReadAsync(cancellation))
        {
            uint[] cells = new uint[definition.Columns.Count];
            for (int i = 0; i < columns.Count; ++i)
            {
                if (columns[i].Cell < 0)
                    continue;
                cells[columns[i].Cell] = ReadCell(table, reader, i, columns[i].Type);
            }

            table.Load(cells);
            if (++read % PROGRESS_STEP == 0 && total > 0)
                progress?.Report((double)read / total);
        }

        progress?.Report(1);
        return table;
    }

    public async Task SaveChangesAsync(DbcTable table, CancellationToken cancellation = default)
    {
        DbcDefinition definition = table.Definition;
        string name = TableName(definition);
        bool rowColumn = (await ColumnsAsync(name, cancellation)).Contains(ROW_COLUMN,
                                                                             StringComparer.OrdinalIgnoreCase);
        // Without a usable key, the table is written again in full
        if (!definition.HasKey || rowColumn)
        {
            await using DbTransaction rewrite = await Connection.BeginTransactionAsync(cancellation);
            await ExecuteAsync("DELETE FROM " + Quote(name), rewrite, cancellation);
            await InsertRowsAsync(table, Enumerable.Range(0, table.Rows.Count).ToList(), name, rowColumn,
                                  replace: false, rewrite, null, cancellation);
            await rewrite.CommitAsync(cancellation);
            table.AcceptChanges();
            return;
        }

        await using DbTransaction transaction = await Connection.BeginTransactionAsync(cancellation);
        await DeleteKeysAsync(name, definition, table.RemovedKeys.ToList(), transaction, cancellation);
        List<int> rows = table.ChangedKeys.Select(table.FindRow).Where(row => row >= 0).ToList();
        await InsertRowsAsync(table, rows, name, rowColumn: false, replace: true, transaction, null, cancellation);
        await transaction.CommitAsync(cancellation);
        table.AcceptChanges();
    }

    public async Task UpsertRowsAsync(DbcTable table, IReadOnlyList<int> rows,
                                      CancellationToken cancellation = default)
    {
        string name = TableName(table.Definition);
        if (!await TableExistsAsync(name, cancellation))
        {
            await using DbTransaction create = await Connection.BeginTransactionAsync(cancellation);
            await ExecuteAsync(CreateTableSql(table.Definition, name, rowColumn: false, uniqueKeys: true), create,
                               cancellation);
            await create.CommitAsync(cancellation);
        }

        await CheckColumnsAsync(table.Definition, name, cancellation);
        await using DbTransaction transaction = await Connection.BeginTransactionAsync(cancellation);
        await InsertRowsAsync(table, rows, name, rowColumn: false, replace: true, transaction, null, cancellation);
        await transaction.CommitAsync(cancellation);
    }

    public void Dispose()
    {
        Connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // The columns as the database has them: row-number tables get their ID back as the first column
    public static List<(string Name, DbcColumnType Type, int Cell)> SqlColumns(DbcDefinition definition)
    {
        List<(string, DbcColumnType, int)> columns = new List<(string, DbcColumnType, int)>();
        if (definition.IndexIsRowNumber)
            columns.Add((ID_COLUMN, DbcColumnType.UInt32, -1));
        for (int i = 0; i < definition.Columns.Count; ++i)
            columns.Add((definition.Columns[i].Name, definition.Columns[i].Type, i));
        return columns;
    }

    protected async Task<object?> ScalarAsync(string sql, CancellationToken cancellation,
                                              params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string parameterName, object value) in parameters)
            AddParameter(command, parameterName, value);
        return await command.ExecuteScalarAsync(cancellation);
    }

    protected async Task ExecuteAsync(string sql, DbTransaction? transaction, CancellationToken cancellation)
    {
        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellation);
    }

    protected static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private string CreateTableSql(DbcDefinition definition, string name, bool rowColumn, bool uniqueKeys)
    {
        List<string> parts = new List<string>();
        if (rowColumn)
            parts.Add(Quote(ROW_COLUMN) + " " + SqlType(DbcColumnType.UInt32) + " NOT NULL");
        foreach ((string column, DbcColumnType type, int _) in SqlColumns(definition))
        {
            string defaultValue = type == DbcColumnType.String ? string.Empty : " NOT NULL DEFAULT 0";
            parts.Add(Quote(column) + " " + SqlType(type) + defaultValue);
        }

        if (rowColumn)
            parts.Add("PRIMARY KEY (" + Quote(ROW_COLUMN) + ")");
        else if ((definition.HasKey && uniqueKeys) || definition.IndexIsRowNumber)
            parts.Add("PRIMARY KEY (" + Quote(SqlColumns(definition)[0].Name) + ")");
        return "CREATE TABLE " + Quote(name) + " (" + string.Join(", ", parts) + ")";
    }

    private async Task CheckColumnsAsync(DbcDefinition definition, string name, CancellationToken cancellation)
    {
        List<string> existing = (await ColumnsAsync(name, cancellation))
                                .Where(column => !string.Equals(column, ROW_COLUMN,
                                                                StringComparison.OrdinalIgnoreCase)).ToList();
        List<string> expected = SqlColumns(definition).Select(column => column.Name).ToList();
        List<string> missing = expected.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException("The table " + name + " doesn't match " + definition.File +
                                                ": it has no column " + string.Join(", ", missing.Take(5)) +
                                                (missing.Count > 5 ? " and " + (missing.Count - 5) + " more" : "") +
                                                ". Import the file again to rebuild the table.");
    }

    private async Task InsertRowsAsync(DbcTable table, IReadOnlyList<int> rows, string name, bool rowColumn,
                                       bool replace, DbTransaction transaction, IProgress<double>? progress,
                                       CancellationToken cancellation)
    {
        if (rows.Count == 0)
            return;

        List<(string Name, DbcColumnType Type, int Cell)> columns = SqlColumns(table.Definition);
        List<string> names = columns.Select(column => Quote(column.Name)).ToList();
        if (rowColumn)
            names.Insert(0, Quote(ROW_COLUMN));
        int perRow = names.Count;
        int batch = Math.Max(1, RowsPerInsert(perRow));
        string verb = replace ? "REPLACE INTO " : "INSERT INTO ";
        string prefix = verb + Quote(name) + " (" + string.Join(", ", names) + ") VALUES ";

        if (batch == 1)
        {
            await InsertOneByOneAsync(table, rows, prefix, columns, rowColumn, transaction, progress, cancellation);
            return;
        }

        for (int start = 0; start < rows.Count; start += batch)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = Math.Min(batch, rows.Count - start);
            await using DbCommand command = Connection.CreateCommand();
            command.Transaction = transaction;
            List<string> groups = new List<string>(count);
            int parameter = 0;
            for (int i = 0; i < count; ++i)
            {
                int row = rows[start + i];
                List<string> placeholders = new List<string>(perRow);
                if (rowColumn)
                {
                    string placeholder = "@p" + parameter++;
                    placeholders.Add(placeholder);
                    AddParameter(command, placeholder, (long)row);
                }

                foreach ((string _, DbcColumnType type, int cell) in columns)
                {
                    string placeholder = "@p" + parameter++;
                    placeholders.Add(placeholder);
                    object value = cell < 0 ? (long)row
                                   : type == DbcColumnType.String ? table.GetString(row, cell)
                                   : Parameter(type, table.Rows[row][cell]);
                    AddParameter(command, placeholder, value);
                }

                groups.Add("(" + string.Join(", ", placeholders) + ")");
            }

            command.CommandText = prefix + string.Join(", ", groups);
            await command.ExecuteNonQueryAsync(cancellation);
            progress?.Report((double)(start + count) / rows.Count);
        }
    }

    // One prepared statement reused for every row, the fast way for SQLite
    private async Task InsertOneByOneAsync(DbcTable table, IReadOnlyList<int> rows, string prefix,
                                           List<(string Name, DbcColumnType Type, int Cell)> columns, bool rowColumn,
                                           DbTransaction transaction, IProgress<double>? progress,
                                           CancellationToken cancellation)
    {
        await using DbCommand command = Connection.CreateCommand();
        command.Transaction = transaction;
        int count = columns.Count + (rowColumn ? 1 : 0);
        List<string> placeholders = new List<string>(count);
        for (int i = 0; i < count; ++i)
        {
            placeholders.Add("@p" + i);
            AddParameter(command, "@p" + i, 0L);
        }

        command.CommandText = prefix + "(" + string.Join(", ", placeholders) + ")";
        command.Prepare();
        for (int index = 0; index < rows.Count; ++index)
        {
            int row = rows[index];
            int parameter = 0;
            if (rowColumn)
                command.Parameters[parameter++].Value = (long)row;
            foreach ((string _, DbcColumnType type, int cell) in columns)
            {
                command.Parameters[parameter++].Value = cell < 0 ? (long)row
                                                        : type == DbcColumnType.String ? table.GetString(row, cell)
                                                        : Parameter(type, table.Rows[row][cell]);
            }

            await command.ExecuteNonQueryAsync(cancellation);
            if (index % PROGRESS_STEP == 0)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report((double)index / rows.Count);
            }
        }
    }

    private async Task DeleteKeysAsync(string name, DbcDefinition definition, List<uint> keys,
                                       DbTransaction transaction, CancellationToken cancellation)
    {
        string key = Quote(SqlColumns(definition)[0].Name);
        int batch = Math.Max(1, RowsPerInsert(1));
        for (int start = 0; start < keys.Count; start += batch)
        {
            List<uint> part = keys.Skip(start).Take(batch).ToList();
            await using DbCommand command = Connection.CreateCommand();
            command.Transaction = transaction;
            List<string> placeholders = new List<string>();
            for (int i = 0; i < part.Count; ++i)
            {
                placeholders.Add("@k" + i);
                AddParameter(command, "@k" + i, Parameter(definition.Columns[0].Type, part[i]));
            }

            command.CommandText = "DELETE FROM " + Quote(name) + " WHERE " + key + " IN (" +
                                  string.Join(", ", placeholders) + ")";
            await command.ExecuteNonQueryAsync(cancellation);
        }
    }

    private static uint ReadCell(DbcTable table, DbDataReader reader, int ordinal, DbcColumnType type)
    {
        if (reader.IsDBNull(ordinal))
            return 0;
        object value = reader.GetValue(ordinal);
        switch (type)
        {
            case DbcColumnType.String:
                return table.Intern(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ??
                                    string.Empty);
            case DbcColumnType.Float:
                return BitConverter.SingleToUInt32Bits(Convert.ToSingle(value,
                                                                        System.Globalization.CultureInfo
                                                                              .InvariantCulture));
            default:
            {
                // Values bigger than the column allows (AzerothCore's bigint masks) keep their low 32 bits
                decimal number = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
                long whole = number > long.MaxValue ? unchecked((long)(ulong)number) : (long)number;
                return unchecked((uint)whole);
            }
        }
    }

    private async Task<List<DbcDefinition>> StoredDefinitionsAsync(CancellationToken cancellation)
    {
        List<DbcDefinition> definitions = new List<DbcDefinition>();
        if (Naming != TableNaming.DbcName || !await TableExistsAsync(META_TABLE, cancellation))
            return definitions;

        await using DbCommand command = Connection.CreateCommand();
        command.CommandText = "SELECT " + Quote("definition") + " FROM " + Quote(META_TABLE);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
        {
            if (DbcDefinitions.FromJson(reader.GetString(0)) is { } definition)
                definitions.Add(definition);
        }

        return definitions;
    }

    private async Task<DbcDefinition?> StoredDefinitionAsync(string file, CancellationToken cancellation)
    {
        string normalised = Path.GetFileName(file);
        if (!normalised.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase))
            normalised += ".dbc";
        return (await StoredDefinitionsAsync(cancellation))
            .FirstOrDefault(entry => string.Equals(entry.File, normalised, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SaveDefinitionAsync(DbcDefinition definition, string name, DbTransaction transaction,
                                           CancellationToken cancellation)
    {
        await ExecuteAsync("CREATE TABLE IF NOT EXISTS " + Quote(META_TABLE) + " (" + Quote("file") +
                           " VARCHAR(128) NOT NULL PRIMARY KEY, " + Quote("table_name") + " VARCHAR(128) NOT NULL, " +
                           Quote("definition") + " " + TextType + " NOT NULL)", transaction, cancellation);
        await using DbCommand command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "REPLACE INTO " + Quote(META_TABLE) + " (" + Quote("file") + ", " +
                              Quote("table_name") + ", " + Quote("definition") + ") VALUES (@file, @table, @json)";
        AddParameter(command, "@file", definition.File);
        AddParameter(command, "@table", name);
        AddParameter(command, "@json", DbcDefinitions.ToJson(definition));
        await command.ExecuteNonQueryAsync(cancellation);
    }
}
