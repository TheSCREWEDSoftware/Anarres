using System.Numerics;

namespace Anarres.Core.Models;

// A model posed at a moment of one of its animations, with its textures and live particles
public sealed class ModelInstance
{
    private const float MS_PER_SECOND = 1000f;

    private readonly ParticleSystem[] _particles;
    private double _time;
    private double _globalTime;

    public ModelInstance(M2Model model, M2Skin skin, ModelTexture[] textures)
    {
        Model = model;
        Skin = skin;
        Textures = textures;
        BoneMatrices = new Matrix4x4[model.Bones.Length];
        Array.Fill(BoneMatrices, Matrix4x4.Identity);
        _particles = model.Particles.Select((emitter, index) => new ParticleSystem(emitter, index)).ToArray();
        Sequence = model.DefaultSequence();
        Pose(Vector3.UnitX * 10f, Vector3.UnitZ);
    }

    public M2Model Model { get; }

    public M2Skin Skin { get; }

    // One per model texture; ModelTexture.White where the file couldn't be found
    public ModelTexture[] Textures { get; }

    public Matrix4x4[] BoneMatrices { get; }

    public IReadOnlyList<ParticleSystem> Particles => _particles;

    public int Sequence { get; private set; }

    // Milliseconds into the current animation
    public uint Time => (uint)_time;

    public bool ShowParticles { get; set; } = true;

    // Character models hold every hair style and outfit piece at once; only the defaults are drawn
    public bool DefaultGeosetsOnly { get; set; }

    private const int GEOSET_GROUP = 100;

    public bool ShowsGeoset(int id)
    {
        return !DefaultGeosetsOnly || id == 0 || id % GEOSET_GROUP == 1;
    }

    public void SetSequence(int sequence)
    {
        Sequence = sequence >= 0 && sequence < Model.Sequences.Length ? sequence : Model.DefaultSequence();
        _time = 0;
        foreach (ParticleSystem system in _particles)
            system.Clear();
    }

    // Moves time on, looping the animation, poses the bones and moves the particles
    public void Advance(float seconds, Vector3 cameraPosition, Vector3 cameraUp)
    {
        _time += seconds * MS_PER_SECOND;
        _globalTime += seconds * MS_PER_SECOND;
        uint duration = Sequence >= 0 ? Model.Sequences[Sequence].Duration : 0;
        _time = duration > 0 ? _time % duration : 0;
        Pose(cameraPosition, cameraUp);
        if (!ShowParticles)
            return;
        foreach (ParticleSystem system in _particles)
            system.Update(this, seconds);
    }

    // The value of a track now, following its global loop when it has one
    public T Value<T>(M2Track<T> track, T fallback, Func<T, T, float, T> lerp)
    {
        if (track.GlobalSequence >= 0 && track.GlobalSequence < Model.GlobalLoops.Length)
        {
            uint loop = Model.GlobalLoops[track.GlobalSequence];
            return track.Value(0, loop > 0 ? (uint)(_globalTime % loop) : 0, fallback, lerp);
        }

        return track.Value(Sequence, Time, fallback, lerp);
    }

    public float Value(M2Track<float> track, float fallback)
    {
        return Value(track, fallback, Lerp);
    }

    public static float Lerp(float a, float b, float amount)
    {
        return a + (b - a) * amount;
    }

    public static Vector3 Lerp(Vector3 a, Vector3 b, float amount)
    {
        return Vector3.Lerp(a, b, amount);
    }

    public static Quaternion Slerp(Quaternion a, Quaternion b, float amount)
    {
        return Quaternion.Slerp(a, b, amount);
    }

    // The colour and opacity a batch is drawn with now: its colour track times its texture weight
    public Vector4 BatchColour(M2Batch batch)
    {
        Vector4 colour = Vector4.One;
        if (batch.ColorIndex >= 0 && batch.ColorIndex < Model.Colors.Length)
        {
            M2Color track = Model.Colors[batch.ColorIndex];
            Vector3 rgb = Value(track.Colour, Vector3.One, Lerp);
            colour = new Vector4(rgb, Value(track.Alpha, 1f));
        }

        if (batch.WeightCombo < Model.TransparencyLookup.Length)
        {
            int weight = Model.TransparencyLookup[batch.WeightCombo];
            if (weight < Model.TextureWeights.Length)
                colour.W *= Value(Model.TextureWeights[weight], 1f);
        }

        return colour;
    }

    // The texture coordinate animation of a batch now (scrolling, turning or scaling), or identity
    public Matrix3x2 TextureMatrix(M2Batch batch)
    {
        if (batch.TransformCombo >= Model.TextureTransformLookup.Length)
            return Matrix3x2.Identity;
        int index = Model.TextureTransformLookup[batch.TransformCombo];
        if (index >= Model.TextureTransforms.Length)
            return Matrix3x2.Identity;
        M2TextureTransform transform = Model.TextureTransforms[index];
        Vector3 translation = Value(transform.Translation, Vector3.Zero, Lerp);
        Quaternion rotation = Value(transform.Rotation, Quaternion.Identity, Slerp);
        Vector3 scale = Value(transform.Scale, Vector3.One, Lerp);
        float angle = 2f * MathF.Atan2(rotation.Z, rotation.W);
        Vector2 centre = new Vector2(0.5f, 0.5f);
        return Matrix3x2.CreateTranslation(-centre) * Matrix3x2.CreateScale(scale.X, scale.Y) *
               Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(centre) *
               Matrix3x2.CreateTranslation(translation.X, translation.Y);
    }

    // The box around the drawn mesh in its current pose; the header's box also covers every animation's reach
    public (Vector3 Min, Vector3 Max) PosedBounds()
    {
        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);
        foreach (int index in Skin.Indices)
        {
            M2Vertex vertex = Model.Vertices[index];
            Vector3 position = Vector3.Zero;
            float total = 0f;
            Span<int> bones = stackalloc int[] { vertex.Bone0, vertex.Bone1, vertex.Bone2, vertex.Bone3 };
            Span<float> weights = stackalloc float[]
            {
                vertex.Weights.X, vertex.Weights.Y, vertex.Weights.Z, vertex.Weights.W
            };
            for (int i = 0; i < bones.Length; ++i)
            {
                if (weights[i] <= 0 || bones[i] >= BoneMatrices.Length)
                    continue;
                position += Vector3.Transform(vertex.Position, BoneMatrices[bones[i]]) * weights[i];
                total += weights[i];
            }

            position = total > 0 ? position / total : vertex.Position;
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return min.X <= max.X ? (min, max) : (Model.BoundsMin, Model.BoundsMax);
    }

    private void Pose(Vector3 cameraPosition, Vector3 cameraUp)
    {
        M2Bone[] bones = Model.Bones;
        for (int i = 0; i < bones.Length; ++i)
        {
            M2Bone bone = bones[i];
            Matrix4x4 parent = bone.Parent >= 0 && bone.Parent < i ? BoneMatrices[bone.Parent] : Matrix4x4.Identity;
            Vector3 translation = Value(bone.Translation, Vector3.Zero, Lerp);
            Quaternion rotation = Quaternion.Normalize(Value(bone.Rotation, Quaternion.Identity, Slerp));
            Vector3 scale = Value(bone.Scale, Vector3.One, Lerp);
            if ((bone.Flags & M2Bone.FLAG_BILLBOARD) != 0)
            {
                // Billboards point their X axis at the camera, from wherever the parent put the pivot
                Vector3 anchor = Vector3.Transform(bone.Pivot + translation, parent);
                Vector3 back = cameraPosition - anchor;
                back = back.LengthSquared() > 0 ? Vector3.Normalize(back) : Vector3.UnitX;
                Vector3 side = Vector3.Cross(cameraUp, back);
                side = side.LengthSquared() > 0 ? Vector3.Normalize(side) : Vector3.UnitY;
                Vector3 up = Vector3.Cross(back, side);
                Matrix4x4 facing = new Matrix4x4(back.X, back.Y, back.Z, 0, side.X, side.Y, side.Z, 0,
                                                 up.X, up.Y, up.Z, 0, 0, 0, 0, 1);
                BoneMatrices[i] = Matrix4x4.CreateTranslation(-bone.Pivot) * Matrix4x4.CreateScale(scale) * facing *
                                  Matrix4x4.CreateTranslation(anchor);
                continue;
            }

            BoneMatrices[i] = Matrix4x4.CreateTranslation(-bone.Pivot) * Matrix4x4.CreateScale(scale) *
                              Matrix4x4.CreateFromQuaternion(rotation) *
                              Matrix4x4.CreateTranslation(bone.Pivot + translation) * parent;
        }
    }
}
