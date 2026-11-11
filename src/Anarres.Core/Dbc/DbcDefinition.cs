using System.Text.Json.Serialization;

namespace Anarres.Core.Dbc;

public sealed record DbcColumn(string Name, DbcColumnType Type);

// The layout of one .dbc file: its columns in order, and the AzerothCore world table that mirrors it
public sealed class DbcDefinition
{
    // Spell.dbc
    public string File { get; set; } = string.Empty;

    // spell_dbc in acore_world; empty when AzerothCore has no table for the file
    public string Table { get; set; } = string.Empty;

    // gt*.dbc files have no ID field: the ID is the record's position, starting at 0
    public bool IndexIsRowNumber { get; set; }

    // Worked out from the data because no definition was known; names are generic
    public bool IsInferred { get; set; }

    public List<DbcColumn> Columns { get; set; } = new List<DbcColumn>();

    [JsonIgnore]
    public string Name => Path.GetFileNameWithoutExtension(File);

    [JsonIgnore]
    public int RecordSize => Columns.Sum(column => DbcColumnTypes.Size(column.Type));

    // The column rows are found by: the ID, or nothing for row-number tables
    [JsonIgnore]
    public bool HasKey => !IndexIsRowNumber && Columns.Count > 0 && DbcColumnTypes.IsInteger(Columns[0].Type);

    public int IndexOf(string column)
    {
        return Columns.FindIndex(entry => string.Equals(entry.Name, column, StringComparison.OrdinalIgnoreCase));
    }

    public DbcDefinition Copy()
    {
        return new DbcDefinition
        {
            File = File,
            Table = Table,
            IndexIsRowNumber = IndexIsRowNumber,
            IsInferred = IsInferred,
            Columns = new List<DbcColumn>(Columns)
        };
    }
}
