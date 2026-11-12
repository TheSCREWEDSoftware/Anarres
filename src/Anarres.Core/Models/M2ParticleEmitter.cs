using System.Numerics;

namespace Anarres.Core.Models;

// Where an emitter's particles start
public enum M2EmitterShape
{
    Plane = 1,
    Sphere = 2,
    Spline = 3
}

// Which part of a particle is drawn: its head (facing the camera), its tail (a streak), or both
public enum M2ParticleParts
{
    Head = 0,
    Tail = 1,
    Both = 2
}

// One particle emitter of an M2 model: sparks, smoke, glows and most of what makes a spell effect visible
public sealed class M2ParticleEmitter
{
    public const int SIZE = 476;

    // The texture field holds up to three indexes, five bits each
    public const uint FLAG_MULTI_TEXTURE = 0x10000000;
    // Particles keep moving with the emitter instead of staying where they were born
    public const uint FLAG_FOLLOW_EMITTER = 0x8;
    // Particles lie flat instead of facing the camera
    public const uint FLAG_NO_BILLBOARD = 0x1000;
    // Each particle picks a random cell of its texture
    public const uint FLAG_RANDOM_CELL = 0x10000;

    private const int TEXTURE_INDEX_BITS = 5;
    private const int TEXTURE_INDEX_MASK = (1 << TEXTURE_INDEX_BITS) - 1;
    private const int VECTOR2_SIZE = 8;
    private const int VECTOR3_SIZE = 12;
    private const int BOX_SIZE = 24;

    public uint Flags { get; private init; }
    public Vector3 Position { get; private init; }
    public int Bone { get; private init; }
    public int Texture { get; private init; }
    public M2Blend Blend { get; private init; }
    public M2EmitterShape Shape { get; private init; }
    public M2ParticleParts Parts { get; private init; }
    public int Rows { get; private init; } = 1;
    public int Columns { get; private init; } = 1;
    public required M2Track<float> Speed { get; init; }
    public required M2Track<float> SpeedVariation { get; init; }
    public required M2Track<float> VerticalRange { get; init; }
    public required M2Track<float> HorizontalRange { get; init; }
    public required M2Track<float> Gravity { get; init; }
    public required M2Track<float> Lifespan { get; init; }
    public required M2Track<float> Rate { get; init; }
    public required M2Track<float> AreaLength { get; init; }
    public required M2Track<float> AreaWidth { get; init; }
    public required M2Track<float> ZSource { get; init; }
    public required M2FakeTrack<Vector3> Colour { get; init; }
    public required M2FakeTrack<float> Alpha { get; init; }
    public required M2FakeTrack<Vector2> Scale { get; init; }
    public Vector2 ScaleVariation { get; private init; }
    public required M2FakeTrack<ushort> HeadCell { get; init; }
    public required M2FakeTrack<ushort> TailCell { get; init; }
    public float TailLength { get; private init; }
    public float Drag { get; private init; }
    public float BaseSpin { get; private init; }
    public float BaseSpinVariation { get; private init; }
    public float Spin { get; private init; }
    public float SpinVariation { get; private init; }
    public required M2Track<byte> Enabled { get; init; }

    public static M2ParticleEmitter Read(ByteCursor cursor, Func<int, byte[]?> source)
    {
        cursor.I32(); // particle id
        uint flags = cursor.U32();
        Vector3 position = cursor.Vec3();
        int bone = cursor.U16();
        int texture = cursor.U16();
        cursor.Array(); // geometry model: a model drawn per particle
        cursor.Array(); // recursion model
        M2Blend blend = (M2Blend)cursor.U8();
        M2EmitterShape shape = (M2EmitterShape)cursor.U8();
        cursor.U16(); // colour index, for the game's own particle colours
        cursor.U8(); // particle type
        M2ParticleParts parts = (M2ParticleParts)cursor.U8();
        cursor.I16(); // texture tile rotation
        int rows = cursor.U16();
        int columns = cursor.U16();
        M2Track<float> speed = M2Model.FloatTrack(cursor, source);
        M2Track<float> speedVariation = M2Model.FloatTrack(cursor, source);
        M2Track<float> vertical = M2Model.FloatTrack(cursor, source);
        M2Track<float> horizontal = M2Model.FloatTrack(cursor, source);
        M2Track<float> gravity = M2Model.FloatTrack(cursor, source);
        M2Track<float> lifespan = M2Model.FloatTrack(cursor, source);
        M2Track<float> rate = M2Model.FloatTrack(cursor, source);
        M2Track<float> length = M2Model.FloatTrack(cursor, source);
        M2Track<float> width = M2Model.FloatTrack(cursor, source);
        M2Track<float> zSource = M2Model.FloatTrack(cursor, source);
        M2FakeTrack<Vector3> colour = M2FakeTrack<Vector3>.Read(cursor, VECTOR3_SIZE, value => value.Vec3());
        M2FakeTrack<float> alpha = M2FakeTrack<float>.Read(cursor, sizeof(short), value => value.Fixed16());
        M2FakeTrack<Vector2> scale = M2FakeTrack<Vector2>.Read(cursor, VECTOR2_SIZE, value => value.Vec2());
        Vector2 scaleVariation = cursor.Vec2();
        M2FakeTrack<ushort> headCell = M2FakeTrack<ushort>.Read(cursor, sizeof(ushort), value => value.U16());
        M2FakeTrack<ushort> tailCell = M2FakeTrack<ushort>.Read(cursor, sizeof(ushort), value => value.U16());
        float tailLength = cursor.F32();
        cursor.F32(); // twinkle speed, percent and scale range
        cursor.F32();
        cursor.Vec2();
        cursor.F32(); // burst multiplier
        float drag = cursor.F32();
        float baseSpin = cursor.F32();
        float baseSpinVariation = cursor.F32();
        float spin = cursor.F32();
        float spinVariation = cursor.F32();
        cursor.Skip(BOX_SIZE); // tumble
        cursor.Vec3(); // wind vector and time
        cursor.F32();
        cursor.Vec2(); // follow speeds and scales
        cursor.Vec2();
        cursor.Array(); // spline points
        M2Track<byte> enabled = M2Track<byte>.Read(cursor, sizeof(byte), value => value.U8(), source);
        return new M2ParticleEmitter
        {
            Flags = flags,
            Position = position,
            Bone = bone,
            Texture = (flags & FLAG_MULTI_TEXTURE) != 0 ? texture & TEXTURE_INDEX_MASK : texture,
            Blend = blend,
            Shape = shape,
            Parts = parts,
            Rows = Math.Max(1, rows),
            Columns = Math.Max(1, columns),
            Speed = speed,
            SpeedVariation = speedVariation,
            VerticalRange = vertical,
            HorizontalRange = horizontal,
            Gravity = gravity,
            Lifespan = lifespan,
            Rate = rate,
            AreaLength = length,
            AreaWidth = width,
            ZSource = zSource,
            Colour = colour,
            Alpha = alpha,
            Scale = scale,
            ScaleVariation = scaleVariation,
            HeadCell = headCell,
            TailCell = tailCell,
            TailLength = tailLength,
            Drag = drag,
            BaseSpin = baseSpin,
            BaseSpinVariation = baseSpinVariation,
            Spin = spin,
            SpinVariation = spinVariation,
            Enabled = enabled
        };
    }
}
