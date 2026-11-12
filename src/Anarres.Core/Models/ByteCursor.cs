using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Anarres.Core.Models;

// A count and an offset into the file, the way M2 and skin files point at their lists
public readonly record struct M2Array(int Count, int Offset)
{
    public const int SIZE = 8;
}

// Reads little-endian fields one after another, the way the model formats lay them out
public sealed class ByteCursor
{
    private const float FIXED16_SCALE = 32767f;

    public ByteCursor(byte[] data, int position = 0)
    {
        Data = data;
        Position = position;
    }

    public byte[] Data { get; }

    public int Position { get; set; }

    public byte U8()
    {
        Need(sizeof(byte));
        return Data[Position++];
    }

    public sbyte I8()
    {
        return unchecked((sbyte)U8());
    }

    public ushort U16()
    {
        Need(sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(Position));
        Position += sizeof(ushort);
        return value;
    }

    public short I16()
    {
        return unchecked((short)U16());
    }

    public uint U32()
    {
        Need(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(Position));
        Position += sizeof(uint);
        return value;
    }

    public int I32()
    {
        return unchecked((int)U32());
    }

    public float F32()
    {
        return BitConverter.Int32BitsToSingle(I32());
    }

    // A signed 16-bit fraction of 1, used for alphas and texture weights
    public float Fixed16()
    {
        return I16() / FIXED16_SCALE;
    }

    public Vector2 Vec2()
    {
        float x = F32();
        return new Vector2(x, F32());
    }

    public Vector3 Vec3()
    {
        float x = F32();
        float y = F32();
        return new Vector3(x, y, F32());
    }

    public Quaternion Quat()
    {
        float x = F32();
        float y = F32();
        float z = F32();
        return new Quaternion(x, y, z, F32());
    }

    public M2Array Array()
    {
        int count = I32();
        return new M2Array(count, I32());
    }

    public void Skip(int bytes)
    {
        Need(bytes);
        Position += bytes;
    }

    // The items of a list, each read by the given reader starting at its stride
    public T[] Items<T>(M2Array array, int stride, Func<ByteCursor, T> read)
    {
        if (array.Count <= 0)
            return System.Array.Empty<T>();
        if (array.Offset < 0 || (long)array.Offset + (long)array.Count * stride > Data.Length)
            throw new InvalidDataException("A list points past the end of the file.");
        T[] items = new T[array.Count];
        int saved = Position;
        for (int i = 0; i < array.Count; ++i)
        {
            Position = array.Offset + i * stride;
            items[i] = read(this);
        }

        Position = saved;
        return items;
    }

    public ushort[] U16s(M2Array array)
    {
        return Items(array, sizeof(ushort), cursor => cursor.U16());
    }

    // A zero-terminated or counted string
    public string Text(M2Array array)
    {
        if (array.Count <= 0 || array.Offset < 0 || array.Offset + array.Count > Data.Length)
            return string.Empty;
        ReadOnlySpan<byte> bytes = Data.AsSpan(array.Offset, array.Count);
        int end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes);
    }

    private void Need(int bytes)
    {
        if (Position < 0 || Position + bytes > Data.Length)
            throw new InvalidDataException("The file ends too soon.");
    }
}
