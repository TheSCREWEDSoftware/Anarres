using System.Globalization;

namespace Anarres.Core.Dbc;

// A .dbc file in memory: every record as raw 32-bit cells, with string cells pointing into a shared pool
public sealed class DbcTable
{
    private readonly List<string> _strings = new List<string> { string.Empty };
    private readonly Dictionary<string, uint> _stringIndex = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        [string.Empty] = 0
    };
    private Dictionary<uint, int>? _rowsByKey;

    public DbcTable(DbcDefinition definition)
    {
        Definition = definition;
    }

    public DbcDefinition Definition { get; }

    public List<uint[]> Rows { get; } = new List<uint[]>();

    // Keys of rows added or changed since the table was loaded or last saved
    public HashSet<uint> ChangedKeys { get; } = new HashSet<uint>();

    // Keys of rows removed since the table was loaded or last saved
    public HashSet<uint> RemovedKeys { get; } = new HashSet<uint>();

    // Any change at all, including in tables without a key
    public bool IsModified { get; private set; }

    public int ColumnCount => Definition.Columns.Count;

    public uint Key(int row)
    {
        return Definition.IndexIsRowNumber ? (uint)row : Rows[row][0];
    }

    public int FindRow(uint key)
    {
        if (Definition.IndexIsRowNumber)
            return key < Rows.Count ? (int)key : -1;

        _rowsByKey ??= BuildKeyIndex();
        return _rowsByKey.TryGetValue(key, out int row) ? row : -1;
    }

    public uint NextFreeKey()
    {
        if (Definition.IndexIsRowNumber)
            return (uint)Rows.Count;
        uint highest = 0;
        foreach (uint[] cells in Rows)
            highest = Math.Max(highest, cells[0]);
        return highest + 1;
    }

    public uint GetRaw(int row, int column)
    {
        return Rows[row][column];
    }

    public uint GetUInt(int row, int column)
    {
        return Rows[row][column];
    }

    public int GetInt(int row, int column)
    {
        return unchecked((int)Rows[row][column]);
    }

    public float GetFloat(int row, int column)
    {
        return BitConverter.UInt32BitsToSingle(Rows[row][column]);
    }

    public string GetString(int row, int column)
    {
        uint index = Rows[row][column];
        return index < _strings.Count ? _strings[(int)index] : string.Empty;
    }

    public void SetRaw(int row, int column, uint value)
    {
        if (Rows[row][column] == value)
            return;

        bool keyChanged = column == 0 && Definition.HasKey;
        if (keyChanged)
        {
            // The old ID is gone from the table; the row lives on under the new one
            RemovedKeys.Add(Rows[row][0]);
            _rowsByKey = null;
        }

        Rows[row][column] = value;
        MarkChanged(row);
    }

    public void SetUInt(int row, int column, uint value)
    {
        SetRaw(row, column, value);
    }

    public void SetInt(int row, int column, int value)
    {
        SetRaw(row, column, unchecked((uint)value));
    }

    public void SetFloat(int row, int column, float value)
    {
        SetRaw(row, column, BitConverter.SingleToUInt32Bits(value));
    }

    public void SetString(int row, int column, string? value)
    {
        SetRaw(row, column, Intern(value ?? string.Empty));
    }

    // Puts a string in the pool and gives the cell value that refers to it
    public uint Intern(string value)
    {
        if (_stringIndex.TryGetValue(value, out uint index))
            return index;
        index = (uint)_strings.Count;
        _strings.Add(value);
        _stringIndex[value] = index;
        return index;
    }

    public string StringAt(uint index)
    {
        return index < _strings.Count ? _strings[(int)index] : string.Empty;
    }

    // A cell as text: numbers in invariant culture, floats so they read back to the same value
    public string GetText(int row, int column)
    {
        uint raw = Rows[row][column];
        switch (Definition.Columns[column].Type)
        {
            case DbcColumnType.String:
                return StringAt(raw);
            case DbcColumnType.Float:
                return BitConverter.UInt32BitsToSingle(raw).ToString("R", CultureInfo.InvariantCulture);
            case DbcColumnType.Int32:
            case DbcColumnType.SByte:
            case DbcColumnType.Int16:
                return unchecked((int)raw).ToString(CultureInfo.InvariantCulture);
            default:
                return raw.ToString(CultureInfo.InvariantCulture);
        }
    }

    // Parses text for a cell; false with a reason when it doesn't fit the column's type
    public bool TrySetText(int row, int column, string text, out string error)
    {
        if (!TryParse(Definition.Columns[column].Type, text, out uint raw, out error))
            return false;
        if (Definition.Columns[column].Type == DbcColumnType.String)
            raw = Intern(text);
        SetRaw(row, column, raw);
        return true;
    }

    // Text to a cell value for any column type; strings are not interned here
    public static bool TryParse(DbcColumnType type, string text, out uint raw, out string error)
    {
        raw = 0;
        error = string.Empty;
        string trimmed = text.Trim();
        NumberStyles integer = NumberStyles.Integer;
        CultureInfo invariant = CultureInfo.InvariantCulture;
        bool hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        switch (type)
        {
            case DbcColumnType.String:
                return true;
            case DbcColumnType.Float:
            {
                if (float.TryParse(trimmed, NumberStyles.Float, invariant, out float number))
                {
                    raw = BitConverter.SingleToUInt32Bits(number);
                    return true;
                }

                error = "Enter a number, such as 1.5.";
                return false;
            }
            default:
            {
                long value;
                bool parsed = hex ? long.TryParse(trimmed[2..], NumberStyles.HexNumber, invariant, out value)
                                  : long.TryParse(trimmed, integer, invariant, out value);
                (long minimum, long maximum) = Range(type);
                // Hex is taken as the cell's bits, so 0xFFFFFFFF fits a signed column too
                if (parsed && hex && value <= uint.MaxValue)
                {
                    raw = (uint)value;
                    return true;
                }

                if (parsed && value >= minimum && value <= maximum)
                {
                    raw = unchecked((uint)value);
                    return true;
                }

                error = "Enter a whole number from " + minimum.ToString(invariant) + " to " +
                        maximum.ToString(invariant) + ".";
                return false;
            }
        }
    }

    private static (long Minimum, long Maximum) Range(DbcColumnType type)
    {
        switch (type)
        {
            case DbcColumnType.Int32:
                return (int.MinValue, int.MaxValue);
            case DbcColumnType.Byte:
                return (byte.MinValue, byte.MaxValue);
            case DbcColumnType.SByte:
                return (sbyte.MinValue, sbyte.MaxValue);
            case DbcColumnType.Int16:
                return (short.MinValue, short.MaxValue);
            case DbcColumnType.UInt16:
                return (ushort.MinValue, ushort.MaxValue);
            default:
                return (uint.MinValue, uint.MaxValue);
        }
    }

    // A new row of zeros and empty strings, with the given key
    public int AddRow(uint key)
    {
        uint[] cells = new uint[ColumnCount];
        if (Definition.HasKey)
            cells[0] = key;
        Rows.Add(cells);
        _rowsByKey = null;
        MarkChanged(Rows.Count - 1);
        return Rows.Count - 1;
    }

    // A copy of a row under a new key; strings are shared through the pool
    public int CopyRow(int source, uint key)
    {
        uint[] cells = (uint[])Rows[source].Clone();
        if (Definition.HasKey)
            cells[0] = key;
        Rows.Add(cells);
        _rowsByKey = null;
        MarkChanged(Rows.Count - 1);
        return Rows.Count - 1;
    }

    public void RemoveRow(int row)
    {
        uint key = Key(row);
        Rows.RemoveAt(row);
        _rowsByKey = null;
        ChangedKeys.Remove(key);
        RemovedKeys.Add(key);
        IsModified = true;
    }

    // Raw cells added straight after reading; no change is recorded
    public void Load(uint[] cells)
    {
        Rows.Add(cells);
        _rowsByKey = null;
    }

    public void SortByKey()
    {
        if (!Definition.HasKey)
            return;
        Rows.Sort((left, right) => left[0].CompareTo(right[0]));
        _rowsByKey = null;
    }

    public void AcceptChanges()
    {
        ChangedKeys.Clear();
        RemovedKeys.Clear();
        IsModified = false;
    }

    private void MarkChanged(int row)
    {
        uint key = Key(row);
        ChangedKeys.Add(key);
        RemovedKeys.Remove(key);
        IsModified = true;
    }

    private Dictionary<uint, int> BuildKeyIndex()
    {
        Dictionary<uint, int> index = new Dictionary<uint, int>(Rows.Count);
        for (int row = 0; row < Rows.Count; ++row)
            index.TryAdd(Rows[row][0], row);
        return index;
    }
}
