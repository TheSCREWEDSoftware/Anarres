using System.Numerics;
using System.Text;
using Anarres.Core.Models;

namespace Anarres.Core.Tests;

// Writes small 3.3.5a test models: a square facing +X, a bone turning half a circle, a texture, maybe particles
public sealed class M2Builder
{
    private const int SEQUENCE_DURATION = 1000;

    private readonly MemoryStream _data = new MemoryStream();
    private readonly BinaryWriter _out;
    private readonly Dictionary<string, int> _fields = new Dictionary<string, int>();

    public M2Builder()
    {
        _out = new BinaryWriter(_data);
    }

    public string Texture { get; set; } = "test.blp";

    public bool Particles { get; set; }

    public M2Blend Blend { get; set; } = M2Blend.Opaque;

    public byte[] Model()
    {
        _out.Write(M2Model.MAGIC);
        _out.Write(M2Model.VERSION_WOTLK);
        Field("name");
        _out.Write(0u); // flags
        foreach (string name in new[] { "globalLoops", "sequences", "sequenceLookup", "bones", "keyBones",
                                        "vertices" })
            Field(name);
        _out.Write(1); // one skin
        foreach (string name in new[] { "colors", "textures", "weights", "transforms", "replaceable", "materials",
                                        "boneLookup", "textureLookup", "texUnitLookup", "transparencyLookup",
                                        "transformLookup" })
            Field(name);
        WriteVector(new Vector3(-1, -1, -1));
        WriteVector(new Vector3(1, 1, 1));
        _out.Write(1.7f);
        WriteVector(Vector3.Zero);
        WriteVector(Vector3.Zero);
        _out.Write(0f);
        foreach (string name in new[] { "collisionTriangles", "collisionVertices", "collisionNormals",
                                        "attachments", "attachmentLookup", "events", "lights", "cameras",
                                        "cameraLookup", "ribbons", "particles" })
            Field(name);

        Block("name", 5, Encoding.ASCII.GetBytes("Test\0"));
        Block("sequences", 1, Bytes(writer =>
        {
            writer.Write((ushort)0); // Stand
            writer.Write((ushort)0);
            writer.Write((uint)SEQUENCE_DURATION);
            writer.Write(0f);
            writer.Write(M2Sequence.FLAG_INLINE);
            writer.Write((short)0);
            writer.Write((ushort)0);
            writer.Write(new byte[40]);
            writer.Write((short)-1);
            writer.Write((ushort)0);
        }));

        // The bone's rotation: identity at 0 ms, half a turn about Z at 500 ms
        int times = Append(Bytes(writer =>
        {
            writer.Write(0u);
            writer.Write(500u);
        }));
        int values = Append(Bytes(writer =>
        {
            WriteQuat(writer, Quaternion.Identity);
            WriteQuat(writer, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI));
        }));
        int timeLists = Append(Bytes(writer => WriteArray(writer, 2, times)));
        int valueLists = Append(Bytes(writer => WriteArray(writer, 2, values)));
        Block("bones", 1, Bytes(writer =>
        {
            writer.Write(-1);
            writer.Write(0u);
            writer.Write((short)-1);
            writer.Write(new byte[6]);
            EmptyTrack(writer);
            writer.Write((ushort)M2Interpolation.Linear);
            writer.Write((short)-1);
            WriteArray(writer, 1, timeLists);
            WriteArray(writer, 1, valueLists);
            EmptyTrack(writer);
            WriteVector(writer, Vector3.Zero);
        }));
        Block("vertices", 4, Bytes(writer =>
        {
            Vector3[] corners = { new(0, -1, -1), new(0, 1, -1), new(0, 1, 1), new(0, -1, 1) };
            Vector2[] uvs = { new(0, 1), new(1, 1), new(1, 0), new(0, 0) };
            for (int i = 0; i < corners.Length; ++i)
            {
                WriteVector(writer, corners[i]);
                writer.Write(new byte[] { 255, 0, 0, 0 });
                writer.Write(new byte[] { 0, 0, 0, 0 });
                WriteVector(writer, Vector3.UnitX);
                writer.Write(uvs[i].X);
                writer.Write(uvs[i].Y);
                writer.Write(0f);
                writer.Write(0f);
            }
        }));
        int textureName = Append(Encoding.ASCII.GetBytes(Texture + "\0"));
        Block("textures", 1, Bytes(writer =>
        {
            writer.Write(M2Texture.HARDCODED);
            writer.Write(M2Texture.FLAG_WRAP_X | M2Texture.FLAG_WRAP_Y);
            WriteArray(writer, Texture.Length + 1, textureName);
        }));
        Block("materials", 1, Bytes(writer =>
        {
            writer.Write((ushort)(M2Material.FLAG_TWO_SIDED | M2Material.FLAG_UNLIT));
            writer.Write((ushort)Blend);
        }));
        Block("textureLookup", 1, Bytes(writer => writer.Write((ushort)0)));
        Block("texUnitLookup", 1, Bytes(writer => writer.Write((ushort)0)));
        if (Particles)
            Block("particles", 1, Emitter());
        return _data.ToArray();
    }

    public static byte[] Skin()
    {
        MemoryStream data = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(data);
        const int HEADER = 48;
        writer.Write(M2Skin.MAGIC);
        WriteArray(writer, 4, HEADER);
        WriteArray(writer, 6, HEADER + 8);
        WriteArray(writer, 0, 0);
        WriteArray(writer, 1, HEADER + 20);
        WriteArray(writer, 1, HEADER + 20 + M2Submesh.SIZE);
        writer.Write(0u);
        foreach (ushort vertex in new ushort[] { 0, 1, 2, 3 })
            writer.Write(vertex);
        foreach (ushort index in new ushort[] { 0, 1, 2, 0, 2, 3 })
            writer.Write(index);
        // One submesh of two triangles
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)4);
        writer.Write((ushort)0);
        writer.Write((ushort)6);
        writer.Write(new byte[M2Submesh.SIZE - 12]);
        // One batch drawing it with material 0 and texture 0, no colour, weight or texture animation
        writer.Write((byte)0);
        writer.Write((sbyte)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((short)-1);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0xFFFF);
        writer.Write((ushort)0xFFFF);
        return data.ToArray();
    }

    // An emitter shooting up 100 particles a second from the origin, living one second
    private byte[] Emitter()
    {
        int rateTimes = Append(Bytes(writer => writer.Write(0u)));
        int rateValues = Append(Bytes(writer => writer.Write(100f)));
        int lifeValues = Append(Bytes(writer => writer.Write(1f)));
        int speedValues = Append(Bytes(writer => writer.Write(1f)));
        int timeList = Append(Bytes(writer => WriteArray(writer, 1, rateTimes)));
        int rateList = Append(Bytes(writer => WriteArray(writer, 1, rateValues)));
        int lifeList = Append(Bytes(writer => WriteArray(writer, 1, lifeValues)));
        int speedList = Append(Bytes(writer => WriteArray(writer, 1, speedValues)));
        return Bytes(writer =>
        {
            writer.Write(-1);
            writer.Write(0u);
            WriteVector(writer, Vector3.Zero);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            WriteArray(writer, 0, 0);
            WriteArray(writer, 0, 0);
            writer.Write((byte)M2Blend.Add);
            writer.Write((byte)M2EmitterShape.Plane);
            writer.Write((ushort)0);
            writer.Write((byte)0);
            writer.Write((byte)M2ParticleParts.Head);
            writer.Write((short)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            Track(writer, timeList, speedList); // speed
            EmptyTrack(writer); // speed variation
            EmptyTrack(writer); // vertical range
            EmptyTrack(writer); // horizontal range
            EmptyTrack(writer); // gravity
            Track(writer, timeList, lifeList); // lifespan
            Track(writer, timeList, rateList); // emission rate
            for (int i = 0; i < 3; ++i)
                EmptyTrack(writer); // area length and width, z source
            for (int i = 0; i < 3; ++i)
                WriteArray(writer, 0, 0, 2); // colour, alpha and scale over life: none
            writer.Write(0f);
            writer.Write(0f);
            for (int i = 0; i < 2; ++i)
                WriteArray(writer, 0, 0, 2); // head and tail cells
            writer.Write(new byte[M2ParticleEmitter.SIZE - (int)writer.BaseStream.Position - M2Track<byte>.SIZE]);
            EmptyTrack(writer); // enabled
        });
    }

    private void Field(string name)
    {
        _fields[name] = (int)_data.Position;
        WriteArray(_out, 0, 0);
    }

    private void Block(string field, int count, byte[] bytes)
    {
        int offset = Append(bytes);
        long saved = _data.Position;
        _data.Position = _fields[field];
        WriteArray(_out, count, offset);
        _data.Position = saved;
    }

    private int Append(byte[] bytes)
    {
        _data.Position = _data.Length;
        int offset = (int)_data.Position;
        _out.Write(bytes);
        return offset;
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        MemoryStream data = new MemoryStream();
        write(new BinaryWriter(data));
        return data.ToArray();
    }

    private static void WriteArray(BinaryWriter writer, int count, int offset, int repeat = 1)
    {
        for (int i = 0; i < repeat; ++i)
        {
            writer.Write(count);
            writer.Write(offset);
        }
    }

    private static void EmptyTrack(BinaryWriter writer)
    {
        writer.Write((ushort)0);
        writer.Write((short)-1);
        WriteArray(writer, 0, 0, 2);
    }

    private static void Track(BinaryWriter writer, int timeList, int valueList)
    {
        writer.Write((ushort)M2Interpolation.Linear);
        writer.Write((short)-1);
        WriteArray(writer, 1, timeList);
        WriteArray(writer, 1, valueList);
    }

    private void WriteVector(Vector3 vector)
    {
        WriteVector(_out, vector);
    }

    private static void WriteVector(BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
    }

    // The reverse of the reader's unpacking: -1..1 back to the offset shorts
    private static void WriteQuat(BinaryWriter writer, Quaternion value)
    {
        foreach (float part in new[] { value.X, value.Y, value.Z, value.W })
        {
            int packed = (int)MathF.Round(part * 32767f);
            writer.Write((short)(packed >= 0 ? packed - 32768 : packed + 32767));
        }
    }
}
