using System.Buffers.Binary;
using System.Text;

namespace Anarres.Core.Dbc;

// Reads and writes the WDBC format of WoW 3.3.5a: a 20-byte header, fixed-size records, then a string block
public static class DbcFile
{
    public const uint SIGNATURE = 0x43424457; // "WDBC" read as a little-endian number
    public const int HEADER_SIZE = 20;

    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    public readonly record struct Header(int RecordCount, int FieldCount, int RecordSize, int StringBlockSize);

    public static DbcTable Read(string path, DbcDefinitions? definitions = null)
    {
        return Read(File.ReadAllBytes(path), Path.GetFileName(path), definitions);
    }

    // The definition is found by file name; when none fits the file's layout, one is worked out from the data
    public static DbcTable Read(byte[] data, string fileName, DbcDefinitions? definitions = null)
    {
        Header header = ReadHeader(data, fileName);
        int recordsStart = HEADER_SIZE;
        int stringsStart = recordsStart + header.RecordCount * header.RecordSize;
        if (stringsStart + header.StringBlockSize > data.Length)
            throw new InvalidDataException(fileName + " is shorter than its header says; it may be cut off.");

        ReadOnlySpan<byte> records = data.AsSpan(recordsStart, header.RecordCount * header.RecordSize);
        ReadOnlySpan<byte> strings = data.AsSpan(stringsStart, header.StringBlockSize);
        DbcDefinition? known = (definitions ?? DbcDefinitions.Default).Find(fileName);
        DbcDefinition definition = known != null && Fits(known, header)
                                       ? known
                                       : DbcInference.Infer(fileName, header, records, strings);
        return ReadRecords(definition, header, records, strings);
    }

    // Reads with exactly this layout, as the table cache does; a file that doesn't fit it is refused
    public static DbcTable Read(byte[] data, DbcDefinition definition)
    {
        Header header = ReadHeader(data, definition.File);
        int stringsStart = HEADER_SIZE + header.RecordCount * header.RecordSize;
        if (stringsStart + header.StringBlockSize > data.Length || !Fits(definition, header))
            throw new InvalidDataException(definition.File + " doesn't match its layout.");
        return ReadRecords(definition, header, data.AsSpan(HEADER_SIZE, header.RecordCount * header.RecordSize),
                           data.AsSpan(stringsStart, header.StringBlockSize));
    }

    private static DbcTable ReadRecords(DbcDefinition definition, Header header, ReadOnlySpan<byte> records,
                                        ReadOnlySpan<byte> strings)
    {
        DbcTable table = new DbcTable(definition);
        Dictionary<int, uint> stringsByOffset = new Dictionary<int, uint> { [0] = 0 };
        List<DbcColumn> columns = definition.Columns;
        for (int record = 0; record < header.RecordCount; ++record)
        {
            ReadOnlySpan<byte> bytes = records.Slice(record * header.RecordSize, header.RecordSize);
            uint[] cells = new uint[columns.Count];
            int position = 0;
            for (int column = 0; column < columns.Count; ++column)
            {
                DbcColumnType type = columns[column].Type;
                cells[column] = ReadCell(bytes, position, type);
                position += DbcColumnTypes.Size(type);
                if (type == DbcColumnType.String)
                    cells[column] = StringCell(table, strings, (int)cells[column], stringsByOffset);
            }

            table.Load(cells);
        }

        return table;
    }

    // name, when given, goes in the messages so the user knows which file is broken
    public static Header ReadHeader(ReadOnlySpan<byte> data, string? name = null)
    {
        if (data.Length == 0)
            throw new InvalidDataException("The file is empty; the client ships a few .dbc files like that.");
        if (data.Length < HEADER_SIZE || BinaryPrimitives.ReadUInt32LittleEndian(data) != SIGNATURE)
            throw new InvalidDataException("This is not a WDBC file (WoW 3.3.5a .dbc).");

        Header header = new Header(BinaryPrimitives.ReadInt32LittleEndian(data[4..]),
                                   BinaryPrimitives.ReadInt32LittleEndian(data[8..]),
                                   BinaryPrimitives.ReadInt32LittleEndian(data[12..]),
                                   BinaryPrimitives.ReadInt32LittleEndian(data[16..]));
        // Summed in 64 bits, so a damaged header's negative or huge counts are refused instead of overflowing
        if (header.RecordCount < 0 || header.FieldCount < 0 || header.RecordSize < 0 || header.StringBlockSize < 0 ||
            HEADER_SIZE + (long)header.RecordCount * header.RecordSize + header.StringBlockSize > data.Length)
            throw new InvalidDataException((name ?? "The file") + " is shorter than its header says; it may be cut " +
                                           "off or damaged.");
        return header;
    }

    public static void Write(DbcTable table, string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        File.WriteAllBytes(path, Write(table));
    }

    // Records in table order; equal strings are written once, and the block starts with the empty string
    public static byte[] Write(DbcTable table)
    {
        List<DbcColumn> columns = table.Definition.Columns;
        int recordSize = table.Definition.RecordSize;
        MemoryStream stringBlock = new MemoryStream();
        stringBlock.WriteByte(0);
        Dictionary<uint, uint> offsets = new Dictionary<uint, uint> { [0] = 0 };

        byte[] records = new byte[table.Rows.Count * recordSize];
        for (int row = 0; row < table.Rows.Count; ++row)
        {
            Span<byte> bytes = records.AsSpan(row * recordSize, recordSize);
            int position = 0;
            for (int column = 0; column < columns.Count; ++column)
            {
                DbcColumnType type = columns[column].Type;
                uint raw = table.Rows[row][column];
                if (type == DbcColumnType.String)
                    raw = StringOffset(table, raw, stringBlock, offsets);
                WriteCell(bytes, position, type, raw);
                position += DbcColumnTypes.Size(type);
            }
        }

        byte[] output = new byte[HEADER_SIZE + records.Length + stringBlock.Length];
        Span<byte> header = output.AsSpan(0, HEADER_SIZE);
        BinaryPrimitives.WriteUInt32LittleEndian(header, SIGNATURE);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], table.Rows.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], columns.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], recordSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], (int)stringBlock.Length);
        records.CopyTo(output, HEADER_SIZE);
        stringBlock.ToArray().CopyTo(output, HEADER_SIZE + records.Length);
        return output;
    }

    private static bool Fits(DbcDefinition definition, Header header)
    {
        return definition.Columns.Count == header.FieldCount && definition.RecordSize == header.RecordSize;
    }

    private static uint ReadCell(ReadOnlySpan<byte> bytes, int position, DbcColumnType type)
    {
        switch (type)
        {
            case DbcColumnType.Byte:
                return bytes[position];
            case DbcColumnType.SByte:
                return unchecked((uint)(sbyte)bytes[position]);
            case DbcColumnType.Int16:
                return unchecked((uint)BinaryPrimitives.ReadInt16LittleEndian(bytes[position..]));
            case DbcColumnType.UInt16:
                return BinaryPrimitives.ReadUInt16LittleEndian(bytes[position..]);
            default:
                return BinaryPrimitives.ReadUInt32LittleEndian(bytes[position..]);
        }
    }

    private static void WriteCell(Span<byte> bytes, int position, DbcColumnType type, uint raw)
    {
        switch (type)
        {
            case DbcColumnType.Byte:
            case DbcColumnType.SByte:
                bytes[position] = unchecked((byte)raw);
                break;
            case DbcColumnType.Int16:
            case DbcColumnType.UInt16:
                BinaryPrimitives.WriteUInt16LittleEndian(bytes[position..], unchecked((ushort)raw));
                break;
            default:
                BinaryPrimitives.WriteUInt32LittleEndian(bytes[position..], raw);
                break;
        }
    }

    private static uint StringCell(DbcTable table, ReadOnlySpan<byte> strings, int offset,
                                   Dictionary<int, uint> stringsByOffset)
    {
        if (stringsByOffset.TryGetValue(offset, out uint cached))
            return cached;
        string text = string.Empty;
        if (offset > 0 && offset < strings.Length)
        {
            ReadOnlySpan<byte> rest = strings[offset..];
            int end = rest.IndexOf((byte)0);
            text = Utf8.GetString(end < 0 ? rest : rest[..end]);
        }

        uint index = table.Intern(text);
        stringsByOffset[offset] = index;
        return index;
    }

    private static uint StringOffset(DbcTable table, uint index, MemoryStream block, Dictionary<uint, uint> offsets)
    {
        if (offsets.TryGetValue(index, out uint offset))
            return offset;
        string text = table.StringAt(index);
        if (text.Length == 0)
        {
            offsets[index] = 0;
            return 0;
        }

        offset = (uint)block.Length;
        block.Write(Utf8.GetBytes(text));
        block.WriteByte(0);
        offsets[index] = offset;
        return offset;
    }
}
