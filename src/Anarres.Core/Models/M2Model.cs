using System.Numerics;

namespace Anarres.Core.Models;

// How a batch mixes with what's behind it
public enum M2Blend
{
    Opaque = 0,
    AlphaKey = 1,
    Alpha = 2,
    NoAlphaAdd = 3,
    Add = 4,
    Mod = 5,
    Mod2x = 6,
    BlendAdd = 7
}

public sealed record M2Sequence(ushort Id, ushort Variation, uint Duration, uint Flags, short Next, ushort Alias)
{
    public const int SIZE = 64;

    // The keys are in the model; otherwise in a ModelNameIIII-VV.anim file beside it
    public const uint FLAG_INLINE = 0x20;

    // This entry only points to another one (Alias)
    public const uint FLAG_ALIAS = 0x40;

    public bool IsInline => (Flags & FLAG_INLINE) != 0;
}

public sealed record M2Bone(int KeyBone, uint Flags, short Parent, M2Track<Vector3> Translation,
                            M2Track<Quaternion> Rotation, M2Track<Vector3> Scale, Vector3 Pivot)
{
    public const int SIZE = 88;

    // Turns to face the camera
    public const uint FLAG_BILLBOARD = 0x8;

    public bool IsAnimated => !Translation.IsEmpty || !Rotation.IsEmpty || !Scale.IsEmpty;
}

public readonly record struct M2Vertex(Vector3 Position, Vector4 Weights, int Bone0, int Bone1, int Bone2,
                                       int Bone3, Vector3 Normal, Vector2 Uv0, Vector2 Uv1)
{
    public const int SIZE = 48;
}

public sealed record M2Color(M2Track<Vector3> Colour, M2Track<float> Alpha)
{
    public const int SIZE = 40;
}

public sealed record M2Texture(uint Type, uint Flags, string FileName)
{
    public const int SIZE = 16;

    // Type 0 is a file named in the model; the others are filled in by the game
    public const uint HARDCODED = 0;
    public const uint ITEM_SKIN = 2;
    public const uint MONSTER_SKIN_1 = 11;
    public const uint MONSTER_SKIN_2 = 12;
    public const uint MONSTER_SKIN_3 = 13;
    public const uint FLAG_WRAP_X = 0x1;
    public const uint FLAG_WRAP_Y = 0x2;
}

public sealed record M2TextureTransform(M2Track<Vector3> Translation, M2Track<Quaternion> Rotation,
                                        M2Track<Vector3> Scale)
{
    public const int SIZE = 60;
}

public readonly record struct M2Material(ushort Flags, M2Blend Blend)
{
    public const int SIZE = 4;
    public const ushort FLAG_UNLIT = 0x1;
    public const ushort FLAG_TWO_SIDED = 0x4;
    public const ushort FLAG_NO_DEPTH_TEST = 0x8;
    public const ushort FLAG_NO_DEPTH_WRITE = 0x10;
}

// A WotLK (3.3.5a, version 264) .m2 model: its geometry, textures, bones, animations and particle emitters
public sealed class M2Model
{
    public const uint MAGIC = 0x3032444D; // "MD20" read as a little-endian number
    public const uint VERSION_WOTLK = 264;

    private const int COMPRESSED_QUAT_SIZE = 8;
    private const int VECTOR3_SIZE = 12;
    private const int QUATERNION_SIZE = 16;
    private const float QUAT_SCALE = 32767f;
    private const int QUAT_OFFSET = 32768;
    private const float BYTE_SCALE = 255f;
    private const int SEQUENCE_PADDING = 2;
    private const int SEQUENCE_TAIL = 40; // replay range, blend time and bounds, which the viewer doesn't use
    private const int BONE_UNION = 6; // submesh id and distance hints

    public string Name { get; private set; } = string.Empty;
    public uint Flags { get; private set; }
    public uint[] GlobalLoops { get; private set; } = Array.Empty<uint>();
    public M2Sequence[] Sequences { get; private set; } = Array.Empty<M2Sequence>();
    public M2Bone[] Bones { get; private set; } = Array.Empty<M2Bone>();
    public M2Vertex[] Vertices { get; private set; } = Array.Empty<M2Vertex>();
    public int SkinCount { get; private set; }
    public M2Color[] Colors { get; private set; } = Array.Empty<M2Color>();
    public M2Texture[] Textures { get; private set; } = Array.Empty<M2Texture>();
    public M2Track<float>[] TextureWeights { get; private set; } = Array.Empty<M2Track<float>>();
    public M2TextureTransform[] TextureTransforms { get; private set; } = Array.Empty<M2TextureTransform>();
    public M2Material[] Materials { get; private set; } = Array.Empty<M2Material>();
    public ushort[] TextureLookup { get; private set; } = Array.Empty<ushort>();
    public ushort[] TexUnitLookup { get; private set; } = Array.Empty<ushort>();
    public ushort[] TransparencyLookup { get; private set; } = Array.Empty<ushort>();
    public ushort[] TextureTransformLookup { get; private set; } = Array.Empty<ushort>();
    public Vector3 BoundsMin { get; private set; }
    public Vector3 BoundsMax { get; private set; }
    public float BoundsRadius { get; private set; }
    public int RibbonCount { get; private set; }
    public M2ParticleEmitter[] Particles { get; private set; } = Array.Empty<M2ParticleEmitter>();

    // animFile(id, variation) gives an animation's .anim file when its keys aren't in the model
    public static M2Model Read(byte[] data, Func<ushort, ushort, byte[]?>? animFile = null)
    {
        ByteCursor cursor = new ByteCursor(data);
        if (data.Length < sizeof(uint) * 2 || cursor.U32() != MAGIC)
            throw new InvalidDataException("This is not an M2 model.");
        uint version = cursor.U32();
        if (version != VERSION_WOTLK)
            throw new InvalidDataException("This model is version " + version + "; Anarres reads 3.3.5a models (" +
                                           VERSION_WOTLK + ").");

        M2Model model = new M2Model();
        M2Array name = cursor.Array();
        model.Flags = cursor.U32();
        M2Array globalLoops = cursor.Array();
        M2Array sequences = cursor.Array();
        cursor.Array(); // sequence lookups, by animation id
        M2Array bones = cursor.Array();
        cursor.Array(); // key bone lookup
        M2Array vertices = cursor.Array();
        model.SkinCount = cursor.I32();
        M2Array colors = cursor.Array();
        M2Array textures = cursor.Array();
        M2Array weights = cursor.Array();
        M2Array transforms = cursor.Array();
        cursor.Array(); // replaceable texture lookup
        M2Array materials = cursor.Array();
        cursor.Array(); // bone lookup, used by skins for hardware skinning
        M2Array textureLookup = cursor.Array();
        M2Array texUnitLookup = cursor.Array();
        M2Array transparencyLookup = cursor.Array();
        M2Array transformLookup = cursor.Array();
        model.BoundsMin = cursor.Vec3();
        model.BoundsMax = cursor.Vec3();
        model.BoundsRadius = cursor.F32();
        cursor.Vec3(); // collision box
        cursor.Vec3();
        cursor.F32();
        cursor.Array(); // collision triangles, vertices and normals
        cursor.Array();
        cursor.Array();
        cursor.Array(); // attachments and their lookup
        cursor.Array();
        cursor.Array(); // events
        cursor.Array(); // lights
        cursor.Array(); // cameras and their lookup
        cursor.Array();
        M2Array ribbons = cursor.Array();
        M2Array particles = cursor.Array();

        model.Name = cursor.Text(name);
        model.GlobalLoops = cursor.Items(globalLoops, sizeof(uint), item => item.U32());
        model.Sequences = cursor.Items(sequences, M2Sequence.SIZE, ReadSequence);
        Dictionary<int, byte[]?> anims = new Dictionary<int, byte[]?>();
        Func<int, byte[]?> source = animation =>
        {
            if (animation < 0 || animation >= model.Sequences.Length || model.Sequences[animation].IsInline)
                return data;
            if (anims.TryGetValue(animation, out byte[]? loaded))
                return loaded;
            M2Sequence sequence = model.Sequences[animation];
            byte[]? file = animFile?.Invoke(sequence.Id, sequence.Variation);
            anims[animation] = file;
            return file;
        };

        model.Bones = cursor.Items(bones, M2Bone.SIZE, item => ReadBone(item, source));
        model.Vertices = cursor.Items(vertices, M2Vertex.SIZE, ReadVertex);
        model.Colors = cursor.Items(colors, M2Color.SIZE, item =>
            new M2Color(Vec3Track(item, source), Fixed16Track(item, source)));
        model.Textures = cursor.Items(textures, M2Texture.SIZE, item =>
        {
            uint type = item.U32();
            uint flags = item.U32();
            return new M2Texture(type, flags, item.Text(item.Array()));
        });
        model.TextureWeights = cursor.Items(weights, M2Track<float>.SIZE, item => Fixed16Track(item, source));
        model.TextureTransforms = cursor.Items(transforms, M2TextureTransform.SIZE, item =>
            new M2TextureTransform(Vec3Track(item, source),
                                   M2Track<Quaternion>.Read(item, QUATERNION_SIZE, value => value.Quat(), source),
                                   Vec3Track(item, source)));
        model.Materials = cursor.Items(materials, M2Material.SIZE, item =>
        {
            ushort flags = item.U16();
            return new M2Material(flags, (M2Blend)item.U16());
        });
        model.TextureLookup = cursor.U16s(textureLookup);
        model.TexUnitLookup = cursor.U16s(texUnitLookup);
        model.TransparencyLookup = cursor.U16s(transparencyLookup);
        model.TextureTransformLookup = cursor.U16s(transformLookup);
        model.RibbonCount = Math.Max(0, ribbons.Count);
        model.Particles = cursor.Items(particles, M2ParticleEmitter.SIZE,
                                       item => M2ParticleEmitter.Read(item, source));
        return model;
    }

    // The .skin file of a model: Name.m2 has Name00.skin (the most detailed) to Name03.skin
    public static string SkinPath(string modelPath, int skin = 0)
    {
        return Path.ChangeExtension(modelPath, null) + skin.ToString("00") + ".skin";
    }

    // The .anim file holding an animation's keys when the model doesn't
    public static string AnimPath(string modelPath, ushort id, ushort variation)
    {
        return Path.ChangeExtension(modelPath, null) + id.ToString("0000") + "-" + variation.ToString("00") + ".anim";
    }

    // Spell and creature tables name models .mdx or .mdl, the formats before M2; the client looks for .m2
    public static string ModelFile(string name)
    {
        string trimmed = name.Trim().Replace('/', '\\');
        string extension = Path.GetExtension(trimmed);
        return extension.Equals(".mdx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mdl", StringComparison.OrdinalIgnoreCase)
                   ? trimmed[..^extension.Length] + ".m2"
                   : trimmed;
    }

    // The animation to show first: Stand when there is one, otherwise the first that isn't an alias
    public int DefaultSequence()
    {
        for (int i = 0; i < Sequences.Length; ++i)
        {
            if (Sequences[i].Id == 0 && (Sequences[i].Flags & M2Sequence.FLAG_ALIAS) == 0)
                return i;
        }

        for (int i = 0; i < Sequences.Length; ++i)
        {
            if ((Sequences[i].Flags & M2Sequence.FLAG_ALIAS) == 0)
                return i;
        }

        return Sequences.Length > 0 ? 0 : -1;
    }

    private static M2Sequence ReadSequence(ByteCursor cursor)
    {
        ushort id = cursor.U16();
        ushort variation = cursor.U16();
        uint duration = cursor.U32();
        cursor.F32(); // move speed
        uint flags = cursor.U32();
        cursor.I16(); // frequency
        cursor.Skip(SEQUENCE_PADDING);
        cursor.Skip(SEQUENCE_TAIL);
        short next = cursor.I16();
        return new M2Sequence(id, variation, duration, flags, next, cursor.U16());
    }

    private static M2Bone ReadBone(ByteCursor cursor, Func<int, byte[]?> source)
    {
        int keyBone = cursor.I32();
        uint flags = cursor.U32();
        short parent = cursor.I16();
        cursor.Skip(BONE_UNION);
        M2Track<Vector3> translation = Vec3Track(cursor, source);
        M2Track<Quaternion> rotation = M2Track<Quaternion>.Read(cursor, COMPRESSED_QUAT_SIZE, ReadCompressedQuat,
                                                                source);
        M2Track<Vector3> scale = Vec3Track(cursor, source);
        return new M2Bone(keyBone, flags, parent, translation, rotation, scale, cursor.Vec3());
    }

    private static M2Vertex ReadVertex(ByteCursor cursor)
    {
        Vector3 position = cursor.Vec3();
        Vector4 weights = new Vector4(cursor.U8(), cursor.U8(), cursor.U8(), cursor.U8()) / BYTE_SCALE;
        int bone0 = cursor.U8();
        int bone1 = cursor.U8();
        int bone2 = cursor.U8();
        int bone3 = cursor.U8();
        Vector3 normal = cursor.Vec3();
        Vector2 uv0 = cursor.Vec2();
        return new M2Vertex(position, weights, bone0, bone1, bone2, bone3, normal, uv0, cursor.Vec2());
    }

    // Bone rotations are stored as four signed shorts, offset so that 0 and the extremes map to -1 and 1
    private static Quaternion ReadCompressedQuat(ByteCursor cursor)
    {
        float x = Unpack(cursor.I16());
        float y = Unpack(cursor.I16());
        float z = Unpack(cursor.I16());
        return new Quaternion(x, y, z, Unpack(cursor.I16()));
    }

    private static float Unpack(short value)
    {
        return (value < 0 ? value + QUAT_OFFSET : value - (QUAT_OFFSET - 1)) / QUAT_SCALE;
    }

    internal static M2Track<Vector3> Vec3Track(ByteCursor cursor, Func<int, byte[]?> source)
    {
        return M2Track<Vector3>.Read(cursor, VECTOR3_SIZE, value => value.Vec3(), source);
    }

    internal static M2Track<float> Fixed16Track(ByteCursor cursor, Func<int, byte[]?> source)
    {
        return M2Track<float>.Read(cursor, sizeof(short), value => value.Fixed16(), source);
    }

    internal static M2Track<float> FloatTrack(ByteCursor cursor, Func<int, byte[]?> source)
    {
        return M2Track<float>.Read(cursor, sizeof(float), value => value.F32(), source);
    }
}
