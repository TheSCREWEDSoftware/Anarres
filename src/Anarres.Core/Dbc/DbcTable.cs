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
    private readonly List<Change> _undo = new List<Change>();
    private readonly List<Change> _redo = new List<Change>();
    private Dictionary<uint, int>? _rowsByKey;
    private bool _replaying;

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

    public bool CanUndo => _undo.Any(change => change is not Boundary);

    public bool CanRedo => _redo.Any(change => change is not Boundary);

    public const int DEFAULT_MAX_UNDO = 1000;

    // Steps kept for undo; older ones are dropped
    public static int MaxUndo { get; set; } = DEFAULT_MAX_UNDO;

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
            if (IsKeyTaken(value, row))
                throw new InvalidOperationException(KeyTakenMessage(value));
            // The old ID is gone from the table; the row lives on under the new one
            RemovedKeys.Add(Rows[row][0]);
            _rowsByKey = null;
        }

        Record(new CellChange(row, column, Rows[row][column], value));
        Rows[row][column] = value;
        MarkChanged(row);
    }

    // True when another row already uses the key, so a save would merge the two
    public bool IsKeyTaken(uint key, int exceptRow = -1)
    {
        if (!Definition.HasKey)
            return false;
        int found = FindRow(key);
        if (found < 0)
            return false;
        if (found != exceptRow)
            return true;
        // FindRow gives the first match; a later duplicate still counts
        for (int row = found + 1; row < Rows.Count; ++row)
        {
            if (Rows[row][0] == key)
                return true;
        }

        return false;
    }

    public static string KeyTakenMessage(uint key)
    {
        return "ID " + key.ToString(CultureInfo.InvariantCulture) + " is already used by another row.";
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
        if (column == 0 && Definition.HasKey && raw != Rows[row][0] && IsKeyTaken(raw, row))
        {
            error = KeyTakenMessage(raw);
            return false;
        }

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
        return InsertRow(Rows.Count, cells);
    }

    // A copy of a row under a new key; strings are shared through the pool
    public int CopyRow(int source, uint key)
    {
        uint[] cells = (uint[])Rows[source].Clone();
        if (Definition.HasKey)
            cells[0] = key;
        return InsertRow(Rows.Count, cells);
    }

    public void RemoveRow(int row)
    {
        uint key = Key(row);
        Record(new RowRemoved(row, Rows[row]));
        Rows.RemoveAt(row);
        _rowsByKey = null;
        ChangedKeys.Remove(key);
        RemovedKeys.Add(key);
        IsModified = true;
        // Rows after it in a row-number table get new IDs, so they count as changed too
        if (Definition.IndexIsRowNumber)
        {
            for (int later = row; later < Rows.Count; ++later)
                ChangedKeys.Add((uint)later);
        }
    }

    // Reverses the last edit; returns the row it touched, or -1 when there was nothing to undo
    public int Undo()
    {
        return Replay(_undo, _redo, undo: true);
    }

    public int Redo()
    {
        return Replay(_redo, _undo, undo: false);
    }

    private int InsertRow(int index, uint[] cells)
    {
        if (Definition.HasKey && IsKeyTaken(cells[0]))
            throw new InvalidOperationException(KeyTakenMessage(cells[0]));
        Rows.Insert(index, cells);
        _rowsByKey = null;
        Record(new RowAdded(index, cells));
        MarkChanged(index);
        return index;
    }

    private int Replay(List<Change> from, List<Change> to, bool undo)
    {
        // Step markers change nothing; they're dropped on the way
        while (from.Count > 0 && from[^1] is Boundary)
            from.RemoveAt(from.Count - 1);
        if (from.Count == 0)
            return -1;
        Change change = from[^1];
        from.RemoveAt(from.Count - 1);
        _replaying = true;
        try
        {
            switch (change)
            {
                case CellChange cell:
                {
                    SetRaw(cell.Row, cell.Column, undo ? cell.Old : cell.New);
                    break;
                }
                case RowAdded added when undo:
                {
                    RemoveRow(added.Row);
                    break;
                }
                case RowAdded added:
                {
                    InsertRow(added.Row, added.Cells);
                    break;
                }
                case RowRemoved removed when undo:
                {
                    InsertRow(removed.Row, removed.Cells);
                    break;
                }
                case RowRemoved removed:
                {
                    RemoveRow(removed.Row);
                    break;
                }
                default:
                    break;
            }
        }
        finally
        {
            _replaying = false;
        }

        to.Add(change);
        return Math.Min(change.Row, Rows.Count - 1);
    }

    private void Record(Change change)
    {
        if (_replaying)
            return;
        _redo.Clear();
        // Typing in one cell is one step, not one per key
        if (change is CellChange cell && _undo.Count > 0 && _undo[^1] is CellChange last &&
            last.Row == cell.Row && last.Column == cell.Column)
        {
            _undo[^1] = last with { New = cell.New };
            return;
        }

        _undo.Add(change);
        if (_undo.Count > MaxUndo)
            _undo.RemoveRange(0, _undo.Count - MaxUndo);
    }

    public void ClearUndo()
    {
        _undo.Clear();
        _redo.Clear();
    }

    // Ends the current step, so the next edit of the same cell is undone on its own
    public void BreakUndoStep()
    {
        if (_undo.Count > 0 && _undo[^1] is CellChange last)
            _undo.Add(new Boundary(last.Row));
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

    // The whole table counts as changed, such as after its rows were replaced from a CSV file
    public void MarkModified()
    {
        IsModified = true;
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

    private abstract record Change(int Row);

    private sealed record CellChange(int Row, int Column, uint Old, uint New) : Change(Row);

    private sealed record RowAdded(int Row, uint[] Cells) : Change(Row);

    private sealed record RowRemoved(int Row, uint[] Cells) : Change(Row);

    // Separates steps without changing anything; skipped when undoing
    private sealed record Boundary(int Row) : Change(Row);
}
