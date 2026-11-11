using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Anarres.Core.Mpq;

// Writes a new MPQ archive (format 1) with zlib-compressed files and a (listfile), as WoW 3.3.5a reads patches
public static class MpqWriter
{
    public const int SECTOR_SIZE_SHIFT = 3;
    public const int HEADER_SIZE = 32;

    private const int MIN_HASH_ENTRIES = 16;

    // name is the path inside the archive, such as DBFilesClient\Spell.dbc
    public static void Write(string path, IReadOnlyList<(string Name, byte[] Data)> files)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        // Written beside the target first, so a failure never leaves a half-written patch in WoW's folder
        string temporary = path + ".tmp";
        using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            Write(stream, files);
        File.Move(temporary, path, overwrite: true);
    }

    public static void Write(Stream output, IReadOnlyList<(string Name, byte[] Data)> files)
    {
        List<(string Name, byte[] Data)> all = files
            .Select(file => (file.Name.Replace('/', '\\'), file.Data))
            .Where(file => !string.Equals(file.Item1, MpqArchive.LIST_FILE, StringComparison.OrdinalIgnoreCase))
            .GroupBy(file => file.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        string list = string.Join("\r\n", all.Select(file => file.Item1)) + "\r\n";
        all.Add((MpqArchive.LIST_FILE, Encoding.UTF8.GetBytes(list)));

        int sectorSize = MpqArchive.SECTOR_SIZE_BASE << SECTOR_SIZE_SHIFT;
        int hashCount = MIN_HASH_ENTRIES;
        while (hashCount < all.Count * 2)
            hashCount *= 2;

        MemoryStream body = new MemoryStream();
        uint[] blocks = new uint[all.Count * 4];
        for (int index = 0; index < all.Count; ++index)
        {
            byte[] stored = CompressFile(all[index].Data, sectorSize);
            uint position = (uint)(HEADER_SIZE + body.Length);
            body.Write(stored);
            uint flags = MpqArchive.FLAG_EXISTS | (all[index].Data.Length > 0 ? MpqArchive.FLAG_COMPRESS : 0);
            blocks[index * 4] = position;
            blocks[index * 4 + 1] = (uint)stored.Length;
            blocks[index * 4 + 2] = (uint)all[index].Data.Length;
            blocks[index * 4 + 3] = flags;
        }

        uint[] hashes = new uint[hashCount * 4];
        Array.Fill(hashes, MpqArchive.HASH_EMPTY);
        for (int index = 0; index < all.Count; ++index)
        {
            string name = all[index].Item1;
            uint slot = MpqCrypto.HashString(name, MpqCrypto.HASH_TABLE_OFFSET) & (uint)(hashCount - 1);
            while (hashes[slot * 4 + 3] != MpqArchive.HASH_EMPTY)
                slot = (slot + 1) % (uint)hashCount;
            hashes[slot * 4] = MpqCrypto.HashString(name, MpqCrypto.HASH_NAME_A);
            hashes[slot * 4 + 1] = MpqCrypto.HashString(name, MpqCrypto.HASH_NAME_B);
            hashes[slot * 4 + 2] = 0; // Neutral locale and platform
            hashes[slot * 4 + 3] = (uint)index;
        }

        MpqCrypto.Encrypt(hashes, MpqCrypto.HashString(MpqArchive.HASH_TABLE_KEY, MpqCrypto.HASH_FILE_KEY));
        MpqCrypto.Encrypt(blocks, MpqCrypto.HashString(MpqArchive.BLOCK_TABLE_KEY, MpqCrypto.HASH_FILE_KEY));

        uint hashPosition = (uint)(HEADER_SIZE + body.Length);
        uint blockPosition = hashPosition + (uint)(hashCount * MpqArchive.ENTRY_SIZE);
        uint archiveSize = blockPosition + (uint)(all.Count * MpqArchive.ENTRY_SIZE);

        byte[] header = new byte[HEADER_SIZE];
        BinaryPrimitives.WriteUInt32LittleEndian(header, MpqArchive.SIGNATURE);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), HEADER_SIZE);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), archiveSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), SECTOR_SIZE_SHIFT);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), hashPosition);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), blockPosition);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), hashCount);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), all.Count);

        output.Write(header);
        body.Position = 0;
        body.CopyTo(output);
        output.Write(ToBytes(hashes));
        output.Write(ToBytes(blocks));
    }

    // A sector offset table, then each sector zlib-compressed, or stored as is when that's no smaller
    private static byte[] CompressFile(byte[] data, int sectorSize)
    {
        if (data.Length == 0)
            return Array.Empty<byte>();

        int sectors = (data.Length + sectorSize - 1) / sectorSize;
        List<byte[]> parts = new List<byte[]>(sectors);
        for (int sector = 0; sector < sectors; ++sector)
        {
            ReadOnlySpan<byte> raw = data.AsSpan(sector * sectorSize,
                                                 Math.Min(sectorSize, data.Length - sector * sectorSize));
            byte[] packed = Zlib(raw);
            parts.Add(packed.Length + 1 < raw.Length ? Prefixed(packed) : raw.ToArray());
        }

        int tableSize = (sectors + 1) * 4;
        byte[] file = new byte[tableSize + parts.Sum(part => part.Length)];
        int position = tableSize;
        for (int sector = 0; sector < sectors; ++sector)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(sector * 4), (uint)position);
            parts[sector].CopyTo(file, position);
            position += parts[sector].Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(sectors * 4), (uint)position);
        return file;
    }

    private static byte[] Zlib(ReadOnlySpan<byte> raw)
    {
        using MemoryStream result = new MemoryStream();
        using (ZLibStream zlib = new ZLibStream(result, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw);
        return result.ToArray();
    }

    private static byte[] Prefixed(byte[] packed)
    {
        byte[] sector = new byte[packed.Length + 1];
        sector[0] = MpqArchive.COMPRESSION_ZLIB;
        packed.CopyTo(sector, 1);
        return sector;
    }

    private static byte[] ToBytes(uint[] words)
    {
        byte[] bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; ++i)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        return bytes;
    }
}
