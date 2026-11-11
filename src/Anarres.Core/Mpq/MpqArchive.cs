using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Anarres.Core.Mpq;

// Reads files out of an MPQ archive (format 1 and 2, as WoW 3.3.5a ships them)
public sealed class MpqArchive : IDisposable
{
    public const uint SIGNATURE = 0x1A51504D;        // "MPQ\x1A"
    public const uint USER_DATA_SIGNATURE = 0x1B51504D; // "MPQ\x1B"
    public const string LIST_FILE = "(listfile)";
    public const string HASH_TABLE_KEY = "(hash table)";
    public const string BLOCK_TABLE_KEY = "(block table)";

    public const uint FLAG_IMPLODE = 0x00000100;
    public const uint FLAG_COMPRESS = 0x00000200;
    public const uint FLAG_ENCRYPTED = 0x00010000;
    public const uint FLAG_FIX_KEY = 0x00020000;
    public const uint FLAG_PATCH_FILE = 0x00100000;
    public const uint FLAG_SINGLE_UNIT = 0x01000000;
    public const uint FLAG_DELETE_MARKER = 0x02000000;
    public const uint FLAG_SECTOR_CRC = 0x04000000;
    public const uint FLAG_EXISTS = 0x80000000;

    public const byte COMPRESSION_HUFFMAN = 0x01;
    public const byte COMPRESSION_ZLIB = 0x02;
    public const byte COMPRESSION_PKWARE = 0x08;
    public const byte COMPRESSION_BZIP2 = 0x10;

    public const uint HASH_EMPTY = 0xFFFFFFFF;
    public const uint HASH_DELETED = 0xFFFFFFFE;
    public const int ENTRY_SIZE = 16;
    public const int SECTOR_SIZE_BASE = 512;

    private const int HEADER_SEARCH_STEP = 512;
    private const int HEADER_V1_SIZE = 32;
    private const int HEADER_V2_SIZE = 44;
    private const int FORMAT_V2 = 1;

    private readonly FileStream _stream;
    private readonly long _archiveStart;
    private readonly HashEntry[] _hashTable;
    private readonly BlockEntry[] _blockTable;
    private readonly int _sectorSize;

    private MpqArchive(string path)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            _archiveStart = FindHeader();
            byte[] header = ReadAt(_archiveStart, HEADER_V2_SIZE, allowShort: true);
            int formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12));
            _sectorSize = SECTOR_SIZE_BASE << BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14));
            long hashPosition = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
            long blockPosition = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
            int hashCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24));
            int blockCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(28));
            if (formatVersion >= FORMAT_V2 && header.Length >= HEADER_V2_SIZE)
            {
                hashPosition |= (long)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(40)) << 32;
                blockPosition |= (long)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(42)) << 32;
            }

            uint[] hashes = ReadTable(hashPosition, hashCount, MpqCrypto.HashString(HASH_TABLE_KEY,
                                                                                    MpqCrypto.HASH_FILE_KEY));
            uint[] blocks = ReadTable(blockPosition, blockCount, MpqCrypto.HashString(BLOCK_TABLE_KEY,
                                                                                      MpqCrypto.HASH_FILE_KEY));
            _hashTable = new HashEntry[hashCount];
            for (int i = 0; i < hashCount; ++i)
            {
                int at = i * 4;
                _hashTable[i] = new HashEntry(hashes[at], hashes[at + 1], (ushort)(hashes[at + 2] & 0xFFFF),
                                              hashes[at + 3]);
            }

            _blockTable = new BlockEntry[blockCount];
            for (int i = 0; i < blockCount; ++i)
            {
                int at = i * 4;
                _blockTable[i] = new BlockEntry(blocks[at], blocks[at + 1], blocks[at + 2], blocks[at + 3]);
            }
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public string Path { get; }

    public static MpqArchive Open(string path)
    {
        return new MpqArchive(path);
    }

    public bool Contains(string name)
    {
        return FindBlock(name) is { } block && (block.Flags & FLAG_DELETE_MARKER) == 0;
    }

    // True when the archive marks the file as removed, hiding copies in archives read before it
    public bool IsDeleted(string name)
    {
        return FindBlock(name) is { } block && (block.Flags & FLAG_DELETE_MARKER) != 0;
    }

    // Names from the archive's (listfile); archives without one give nothing
    public List<string> ListFiles()
    {
        byte[]? list = TryRead(LIST_FILE);
        if (list == null)
            return new List<string>();
        return Encoding.UTF8.GetString(list).Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(name => name.Trim()).Where(name => name.Length > 0).Distinct().ToList();
    }

    public byte[] Read(string name)
    {
        return TryRead(name) ?? throw new FileNotFoundException(name + " is not in " +
                                                                System.IO.Path.GetFileName(Path) + ".");
    }

    public byte[]? TryRead(string name)
    {
        if (FindBlock(name) is not { } block || (block.Flags & FLAG_DELETE_MARKER) != 0)
            return null;
        if ((block.Flags & FLAG_PATCH_FILE) != 0)
            throw new NotSupportedException(name + " is stored as a binary patch, which Anarres can't apply.");

        long start = _archiveStart + block.Position;
        bool encrypted = (block.Flags & FLAG_ENCRYPTED) != 0;
        uint key = encrypted ? MpqCrypto.FileKey(name, block.Position, block.FileSize,
                                                 (block.Flags & FLAG_FIX_KEY) != 0) : 0;
        bool compressed = (block.Flags & (FLAG_COMPRESS | FLAG_IMPLODE)) != 0;
        if (block.FileSize == 0)
            return Array.Empty<byte>();

        if ((block.Flags & FLAG_SINGLE_UNIT) != 0 || !compressed)
        {
            if (!compressed)
                return ReadUncompressed(start, block, key, encrypted);
            byte[] whole = ReadAt(start, (int)block.CompressedSize);
            if (encrypted)
                DecryptBytes(whole, key);
            return Decompress(whole, (int)block.FileSize, block.Flags);
        }

        int sectors = (int)((block.FileSize + _sectorSize - 1) / _sectorSize);
        int tableEntries = sectors + 1 + ((block.Flags & FLAG_SECTOR_CRC) != 0 ? 1 : 0);
        byte[] tableBytes = ReadAt(start, tableEntries * 4);
        uint[] offsets = new uint[tableEntries];
        for (int i = 0; i < tableEntries; ++i)
            offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(tableBytes.AsSpan(i * 4));
        if (encrypted)
            MpqCrypto.Decrypt(offsets, key - 1);

        byte[] output = new byte[block.FileSize];
        for (int sector = 0; sector < sectors; ++sector)
        {
            int size = (int)(offsets[sector + 1] - offsets[sector]);
            int expected = (int)Math.Min(_sectorSize, block.FileSize - (long)sector * _sectorSize);
            byte[] data = ReadAt(start + offsets[sector], size);
            if (encrypted)
                DecryptBytes(data, key + (uint)sector);
            byte[] plain = size < expected ? Decompress(data, expected, block.Flags) : data;
            if (plain.Length != expected)
                throw new InvalidDataException(name + " has a sector that doesn't decompress to its full size.");
            plain.CopyTo(output, sector * _sectorSize);
        }

        return output;
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    private byte[] ReadUncompressed(long start, BlockEntry block, uint key, bool encrypted)
    {
        byte[] data = ReadAt(start, (int)block.FileSize);
        if (!encrypted)
            return data;
        // Encrypted uncompressed files are still split into sectors, each with its own key
        for (int sector = 0; sector * _sectorSize < data.Length; ++sector)
        {
            int length = Math.Min(_sectorSize, data.Length - sector * _sectorSize);
            byte[] part = data.AsSpan(sector * _sectorSize, length).ToArray();
            DecryptBytes(part, key + (uint)sector);
            part.CopyTo(data, sector * _sectorSize);
        }

        return data;
    }

    private static byte[] Decompress(byte[] data, int expected, uint flags)
    {
        if ((flags & FLAG_IMPLODE) != 0)
            return PkwareExplode.Decompress(data, expected);

        byte mask = data[0];
        byte[] payload = data.AsSpan(1).ToArray();
        if ((mask & COMPRESSION_BZIP2) != 0 || (mask & COMPRESSION_HUFFMAN) != 0 || (mask & ~0x0A) != 0)
            throw new NotSupportedException("This MPQ file uses a compression Anarres doesn't read (" +
                                            "0x" + mask.ToString("X2") + "); only zlib and PKWARE are supported.");
        if ((mask & COMPRESSION_PKWARE) != 0)
            payload = PkwareExplode.Decompress(payload, expected);
        if ((mask & COMPRESSION_ZLIB) != 0)
        {
            using ZLibStream zlib = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress);
            using MemoryStream result = new MemoryStream(expected);
            zlib.CopyTo(result);
            payload = result.ToArray();
        }

        return payload;
    }

    private static void DecryptBytes(byte[] data, uint key)
    {
        int words = data.Length / 4;
        uint[] buffer = new uint[words];
        for (int i = 0; i < words; ++i)
            buffer[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i * 4));
        MpqCrypto.Decrypt(buffer, key);
        for (int i = 0; i < words; ++i)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), buffer[i]);
    }

    private BlockEntry? FindBlock(string name)
    {
        if (_hashTable.Length == 0)
            return null;
        uint start = MpqCrypto.HashString(name, MpqCrypto.HASH_TABLE_OFFSET) & (uint)(_hashTable.Length - 1);
        uint nameA = MpqCrypto.HashString(name, MpqCrypto.HASH_NAME_A);
        uint nameB = MpqCrypto.HashString(name, MpqCrypto.HASH_NAME_B);
        BlockEntry? found = null;
        for (int step = 0; step < _hashTable.Length; ++step)
        {
            HashEntry entry = _hashTable[(start + step) % _hashTable.Length];
            if (entry.BlockIndex == HASH_EMPTY)
                break;
            if (entry.NameA != nameA || entry.NameB != nameB || entry.BlockIndex >= _blockTable.Length)
                continue;
            BlockEntry block = _blockTable[entry.BlockIndex];
            if ((block.Flags & FLAG_EXISTS) == 0)
                continue;
            // The neutral locale wins; another one is used only if there's no neutral copy
            if (entry.Locale == 0)
                return block;
            found ??= block;
        }

        return found;
    }

    private long FindHeader()
    {
        byte[] signature = new byte[4];
        for (long offset = 0; offset + HEADER_V1_SIZE <= _stream.Length; offset += HEADER_SEARCH_STEP)
        {
            _stream.Position = offset;
            _stream.ReadExactly(signature);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(signature);
            if (value == SIGNATURE)
                return offset;
            if (value == USER_DATA_SIGNATURE)
            {
                byte[] userData = ReadAt(offset, HEADER_V1_SIZE / 2);
                return offset + BinaryPrimitives.ReadUInt32LittleEndian(userData.AsSpan(8));
            }
        }

        throw new InvalidDataException(System.IO.Path.GetFileName(Path) + " is not an MPQ archive.");
    }

    private uint[] ReadTable(long position, int entries, uint key)
    {
        byte[] bytes = ReadAt(_archiveStart + position, entries * ENTRY_SIZE);
        uint[] words = new uint[entries * 4];
        for (int i = 0; i < words.Length; ++i)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
        MpqCrypto.Decrypt(words, key);
        return words;
    }

    private byte[] ReadAt(long position, int count, bool allowShort = false)
    {
        if (allowShort)
            count = (int)Math.Min(count, _stream.Length - position);
        byte[] buffer = new byte[count];
        _stream.Position = position;
        _stream.ReadExactly(buffer);
        return buffer;
    }

    private readonly record struct HashEntry(uint NameA, uint NameB, ushort Locale, uint BlockIndex);

    private readonly record struct BlockEntry(uint Position, uint CompressedSize, uint FileSize, uint Flags);
}
