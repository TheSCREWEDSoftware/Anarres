using System.Buffers.Binary;

namespace Anarres.Core.Dbc;

// Works out a layout for a .dbc file no definition covers: strings, floats and integers told apart by their values
public static class DbcInference
{
    // A column counts as floats when this share of its non-zero values look like ordinary floats
    private const double FLOAT_SHARE = 0.9;
    // Floats with an exponent outside this range are too large or too small to be real data
    private const int MIN_FLOAT_EXPONENT = -20;
    private const int MAX_FLOAT_EXPONENT = 30;
    private const int FLOAT_EXPONENT_BIAS = 127;
    private const int FLOAT_EXPONENT_SHIFT = 23;
    private const uint FLOAT_EXPONENT_MASK = 0xFF;
    // Values under this are taken as small integers, never floats
    private const uint SMALL_INTEGER_LIMIT = 0x00800000;
    private const int CELL_SIZE = 4;

    public static DbcDefinition Infer(string fileName, DbcFile.Header header, ReadOnlySpan<byte> records,
                                      ReadOnlySpan<byte> strings)
    {
        DbcDefinition definition = new DbcDefinition { File = fileName, IsInferred = true };
        bool packed = header.RecordSize == header.FieldCount * CELL_SIZE;
        if (!packed)
        {
            // Fields of mixed sizes can't be told apart; show whole cells and the bytes left over
            int cells = header.RecordSize / CELL_SIZE;
            for (int column = 0; column < cells; ++column)
                definition.Columns.Add(new DbcColumn(Name(column), DbcColumnType.UInt32));
            for (int extra = cells * CELL_SIZE; extra < header.RecordSize; ++extra)
                definition.Columns.Add(new DbcColumn("Byte" + extra, DbcColumnType.Byte));
            return definition;
        }

        HashSet<int> stringStarts = StringStarts(strings);
        for (int column = 0; column < header.FieldCount; ++column)
        {
            int nonZero = 0;
            int stringLike = 0;
            int floatLike = 0;
            bool negative = false;
            for (int record = 0; record < header.RecordCount; ++record)
            {
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(
                    records[(record * header.RecordSize + column * CELL_SIZE)..]);
                if (value == 0)
                    continue;
                ++nonZero;
                if (value < strings.Length && stringStarts.Contains((int)value))
                    ++stringLike;
                if (LooksLikeFloat(value))
                    ++floatLike;
                if ((int)value < 0)
                    negative = true;
            }

            DbcColumnType type = DbcColumnType.UInt32;
            if (column > 0 && nonZero > 0 && stringLike == nonZero && strings.Length > 1)
                type = DbcColumnType.String;
            else if (column > 0 && nonZero > 0 && floatLike >= nonZero * FLOAT_SHARE)
                type = DbcColumnType.Float;
            else if (negative)
                type = DbcColumnType.Int32;
            definition.Columns.Add(new DbcColumn(Name(column), type));
        }

        return definition;
    }

    private static string Name(int column)
    {
        return column == 0 ? "ID" : "Field" + column;
    }

    // Offsets where a string begins: just after a zero byte
    private static HashSet<int> StringStarts(ReadOnlySpan<byte> strings)
    {
        HashSet<int> starts = new HashSet<int>();
        for (int offset = 1; offset < strings.Length; ++offset)
        {
            if (strings[offset - 1] == 0)
                starts.Add(offset);
        }

        return starts;
    }

    private static bool LooksLikeFloat(uint value)
    {
        if ((value & 0x7FFFFFFF) < SMALL_INTEGER_LIMIT)
            return false;
        int exponent = (int)((value >> FLOAT_EXPONENT_SHIFT) & FLOAT_EXPONENT_MASK) - FLOAT_EXPONENT_BIAS;
        return exponent >= MIN_FLOAT_EXPONENT && exponent <= MAX_FLOAT_EXPONENT;
    }
}
