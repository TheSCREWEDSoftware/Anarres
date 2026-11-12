using System.Numerics;

namespace Anarres.Core.Models;

// A camera turning around a point: drag turns it, the wheel moves it closer or further
public sealed class ModelCamera
{
    private const float FIELD_OF_VIEW = MathF.PI / 4f;
    private const float NEAR_FRACTION = 0.01f;
    private const float FAR_FRACTION = 20f;
    private const float MIN_PITCH = -1.5f;
    private const float MAX_PITCH = 1.5f;
    private const float START_YAW = 0.6f;
    private const float START_PITCH = 0.25f;
    private const float FIT_MARGIN = 1.15f;
    private const float MIN_RADIUS = 0.1f;
    private const float MIN_DISTANCE_FRACTION = 0.05f;
    private const float MAX_DISTANCE_FRACTION = 50f;

    public Vector3 Target { get; set; }

    // Turn around the up axis and up or down, in radians
    public float Yaw { get; set; } = START_YAW;

    public float Pitch { get; set; } = START_PITCH;

    public float Distance { get; set; } = 10f;

    public float Radius { get; private set; } = 1f;

    public Vector3 Position
    {
        get
        {
            float cos = MathF.Cos(Pitch);
            return Target + new Vector3(MathF.Cos(Yaw) * cos, MathF.Sin(Yaw) * cos, MathF.Sin(Pitch)) * Distance;
        }
    }

    // Frames a box so it fills the view
    public void Fit(Vector3 min, Vector3 max)
    {
        Target = (min + max) / 2f;
        Radius = MathF.Max(MIN_RADIUS, (max - min).Length() / 2f);
        Distance = Radius / MathF.Sin(FIELD_OF_VIEW / 2f) * FIT_MARGIN;
    }

    public void Turn(float yaw, float pitch)
    {
        Yaw += yaw;
        Pitch = Math.Clamp(Pitch + pitch, MIN_PITCH, MAX_PITCH);
    }

    public void Zoom(float factor)
    {
        Distance = Math.Clamp(Distance * factor, Radius * MIN_DISTANCE_FRACTION, Radius * MAX_DISTANCE_FRACTION);
    }

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitZ);

    public Matrix4x4 Projection(float aspect)
    {
        float near = MathF.Max(Distance * NEAR_FRACTION, 0.001f);
        return Matrix4x4.CreatePerspectiveFieldOfView(FIELD_OF_VIEW, aspect, near,
                                                      Distance + Radius * FAR_FRACTION);
    }
}
