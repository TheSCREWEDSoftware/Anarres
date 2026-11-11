using Anarres.Core.Dbc;

namespace Anarres.Core.Storage;

// How tables are named in a database: Spell (like Stoneharry's editor) or spell_dbc (AzerothCore's world tables)
public enum TableNaming
{
    DbcName,
    AzerothCore
}

public sealed record StoredTable(string File, string TableName, long Rows);

// Where imported DBC data lives while it's edited: a local SQLite project file or a MySQL/MariaDB database
public interface IDbcStore : IDisposable
{
    // "SQLite: C:\mods\spells.anarres" or "MySQL: dbc on localhost"
    string Description { get; }

    TableNaming Naming { get; }

    Task<List<StoredTable>> ListTablesAsync(CancellationToken cancellation = default);

    Task<bool> ContainsAsync(string file, CancellationToken cancellation = default);

    // Replaces the whole table with the given rows
    Task ImportAsync(DbcTable table, IProgress<double>? progress = null, CancellationToken cancellation = default);

    Task<DbcTable> LoadAsync(string file, DbcDefinitions definitions, IProgress<double>? progress = null,
                             CancellationToken cancellation = default);

    // Writes only the rows changed or removed since the table was loaded, then marks the table saved
    Task SaveChangesAsync(DbcTable table, CancellationToken cancellation = default);

    // Writes just these rows, for sending a few spells to AzerothCore's world database
    Task UpsertRowsAsync(DbcTable table, IReadOnlyList<int> rows, CancellationToken cancellation = default);
}
