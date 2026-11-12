using System.Numerics;

namespace Anarres.Core.Models;

public struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Age;
    public float Life;
    public float Spin;
    public float SpinRate;
    public float Size;
    public int Cell;
}

// The live particles of one emitter: born at its rate, moved by speed, gravity and drag, gone after their life
public sealed class ParticleSystem
{
    public const int MAX_PARTICLES = 1500;

    private const float FULL_CIRCLE = MathF.PI * 2f;
    private const float MIN_LIFE = 0.01f;
    private const float MIN_Z_SOURCE = 0.0001f;
    private const float MAX_STEP = 0.1f;

    private readonly List<Particle> _live = new List<Particle>();
    private readonly Random _random;
    private float _owed;

    public ParticleSystem(M2ParticleEmitter emitter, int seed)
    {
        Emitter = emitter;
        _random = new Random(seed);
    }

    public M2ParticleEmitter Emitter { get; }

    public IReadOnlyList<Particle> Live => _live;

    public void Clear()
    {
        _live.Clear();
        _owed = 0;
    }

    public void Update(ModelInstance instance, float seconds)
    {
        seconds = Math.Clamp(seconds, 0f, MAX_STEP);
        float gravity = instance.Value(Emitter.Gravity, 0f);
        float drag = Math.Clamp(Emitter.Drag * seconds, 0f, 1f);
        for (int i = _live.Count - 1; i >= 0; --i)
        {
            Particle particle = _live[i];
            particle.Age += seconds;
            if (particle.Age >= particle.Life)
            {
                _live.RemoveAt(i);
                continue;
            }

            particle.Velocity.Z -= gravity * seconds;
            particle.Velocity *= 1f - drag;
            particle.Position += particle.Velocity * seconds;
            _live[i] = particle;
        }

        bool enabled = instance.Value(Emitter.Enabled, (byte)1, (a, _, _) => a) != 0;
        if (!enabled)
            return;
        _owed += MathF.Max(0f, instance.Value(Emitter.Rate, 0f)) * seconds;
        Matrix4x4 bone = Emitter.Bone < instance.BoneMatrices.Length ? instance.BoneMatrices[Emitter.Bone]
                                                                     : Matrix4x4.Identity;
        while (_owed >= 1f)
        {
            _owed -= 1f;
            if (_live.Count < MAX_PARTICLES)
                _live.Add(Spawn(instance, bone));
        }
    }

    private Particle Spawn(ModelInstance instance, Matrix4x4 bone)
    {
        float length = instance.Value(Emitter.AreaLength, 0f);
        float width = instance.Value(Emitter.AreaWidth, 0f);
        float vertical = instance.Value(Emitter.VerticalRange, 0f);
        float horizontal = instance.Value(Emitter.HorizontalRange, 0f);
        float zSource = instance.Value(Emitter.ZSource, 0f);
        float speed = instance.Value(Emitter.Speed, 0f) *
                      (1f + Between(-1f, 1f) * instance.Value(Emitter.SpeedVariation, 0f));

        Vector3 offset;
        Vector3 direction;
        float polar = Between(-1f, 1f) * vertical;
        float azimuth = Between(-1f, 1f) * horizontal;
        Vector3 spread = new Vector3(MathF.Sin(polar) * MathF.Cos(azimuth), MathF.Sin(polar) * MathF.Sin(azimuth),
                                     MathF.Cos(polar));
        if (Emitter.Shape == M2EmitterShape.Sphere)
        {
            // A sphere emitter's area is the range of radiuses its particles start at
            offset = spread * (length + (width - length) * Between(0f, 1f));
            direction = spread;
        }
        else
        {
            offset = new Vector3(Between(-0.5f, 0.5f) * length, Between(-0.5f, 0.5f) * width, 0f);
            direction = spread;
        }

        if (zSource > MIN_Z_SOURCE)
        {
            Vector3 away = offset - new Vector3(0f, 0f, zSource);
            direction = away.LengthSquared() > 0 ? Vector3.Normalize(away) : Vector3.UnitZ;
        }

        float scale = 1f + Between(-1f, 1f) * Emitter.ScaleVariation.X;
        int cells = Emitter.Rows * Emitter.Columns;
        return new Particle
        {
            Position = Vector3.Transform(Emitter.Position + offset, bone),
            Velocity = Vector3.TransformNormal(direction, bone) * speed,
            Life = MathF.Max(MIN_LIFE, instance.Value(Emitter.Lifespan, 1f)),
            Spin = Emitter.BaseSpin + Between(-1f, 1f) * Emitter.BaseSpinVariation * FULL_CIRCLE,
            SpinRate = Emitter.Spin + Between(-1f, 1f) * Emitter.SpinVariation,
            Size = MathF.Max(0f, scale),
            Cell = (Emitter.Flags & M2ParticleEmitter.FLAG_RANDOM_CELL) != 0 ? _random.Next(cells) : 0
        };
    }

    private float Between(float low, float high)
    {
        return low + (float)_random.NextDouble() * (high - low);
    }
}
