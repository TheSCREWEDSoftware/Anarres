using System.Diagnostics;
using System.Numerics;

namespace Anarres.Core.Models;

// Draws a posed model and its particles into BGRA pixels in software: the same on every system and in tests
public sealed class ModelRenderer
{
    private const float AMBIENT = 0.45f;
    private const float DIFFUSE = 0.55f;
    private const float ALPHA_KEY = 0.5f;
    private const float MIN_W = 0.0001f;
    private const float INVISIBLE = 0.004f;
    private const float MOD2X = 2f;
    private const ushort ENVIRONMENT_UV = 0xFFFF;
    private const uint OPAQUE_ALPHA = 0xFF000000;
    private const int RED_SHIFT = 16;
    private const int GREEN_SHIFT = 8;
    private const float CHANNEL_MAX = 255f;
    private const int QUAD_CORNERS = 4;
    private const int MAX_SUPERSAMPLE = 3;
    // How many times over particles may cover the picture per frame; beyond it the furthest are left out
    private const float PARTICLE_FILL_BUDGET = 12f;
    // A single particle bigger than this many pictures is the camera sitting inside it; it isn't drawn
    private const float MAX_PARTICLE_FILL = 4f;

    private Vector3[] _colour = Array.Empty<Vector3>();
    private float[] _depth = Array.Empty<float>();
    private Vector3[] _positions = Array.Empty<Vector3>();
    private Vector3[] _normals = Array.Empty<Vector3>();
    private int _width;
    private int _height;

    public Vector3 BackgroundTop { get; set; } = new Vector3(0.16f, 0.17f, 0.2f);

    public Vector3 BackgroundBottom { get; set; } = new Vector3(0.07f, 0.07f, 0.09f);

    // Triangles drawn last frame, for the status line and tests
    public int TrianglesDrawn { get; private set; }

    public int ParticlesDrawn { get; private set; }

    // Draws at this many times the size each way and averages down, smoothing jagged edges (2 costs 4 times as much)
    public int Supersample { get; set; } = 1;

    // Where the last frame's time went, in milliseconds
    public double SkinMs { get; private set; }
    public double MeshMs { get; private set; }
    public double ParticlesMs { get; private set; }
    public double FinishMs { get; private set; }

    public void Render(ModelInstance instance, ModelCamera camera, int width, int height, uint[] bgra)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height)
            throw new ArgumentException("The pixel buffer is smaller than the picture.");
        int scale = Math.Clamp(Supersample, 1, MAX_SUPERSAMPLE);
        int outputWidth = width;
        int outputHeight = height;
        width *= scale;
        height *= scale;
        long started = Stopwatch.GetTimestamp();
        Prepare(width, height);
        TrianglesDrawn = 0;
        ParticlesDrawn = 0;

        Matrix4x4 view = camera.View;
        Matrix4x4 projection = camera.Projection(width / (float)height);
        Matrix4x4 viewProjection = view * projection;
        Vector3 light = Vector3.Normalize(camera.Position - camera.Target + Vector3.UnitZ * camera.Distance);
        Skin(instance);
        long skinned = Stopwatch.GetTimestamp();

        M2Model model = instance.Model;
        M2Skin skin = instance.Skin;
        List<int> order = Enumerable.Range(0, skin.Batches.Length).ToList();
        // Opaque batches first; the rest in their priority order, so glows and smoke land on what's behind them
        order = order.OrderBy(index => IsOpaque(model, skin.Batches[index]) ? 0 : 1)
                     .ThenBy(index => IsOpaque(model, skin.Batches[index]) ? 0 : skin.Batches[index].PriorityPlane)
                     .ToList();
        foreach (int index in order)
            DrawBatch(instance, skin.Batches[index], viewProjection, view, light);
        long meshed = Stopwatch.GetTimestamp();
        if (instance.ShowParticles)
            DrawParticles(instance, view, projection);
        long particled = Stopwatch.GetTimestamp();

        float average = 1f / (scale * scale);
        for (int y = 0; y < outputHeight; ++y)
        {
            for (int x = 0; x < outputWidth; ++x)
            {
                Vector3 sum = Vector3.Zero;
                for (int dy = 0; dy < scale; ++dy)
                {
                    int row = (y * scale + dy) * width + x * scale;
                    for (int dx = 0; dx < scale; ++dx)
                        sum += Vector3.Clamp(_colour[row + dx], Vector3.Zero, Vector3.One);
                }

                Vector3 c = sum * (average * CHANNEL_MAX);
                bgra[y * outputWidth + x] = OPAQUE_ALPHA | ((uint)c.X << RED_SHIFT) | ((uint)c.Y << GREEN_SHIFT) |
                                            (uint)c.Z;
            }
        }

        long finished = Stopwatch.GetTimestamp();
        SkinMs = Stopwatch.GetElapsedTime(started, skinned).TotalMilliseconds;
        MeshMs = Stopwatch.GetElapsedTime(skinned, meshed).TotalMilliseconds;
        ParticlesMs = Stopwatch.GetElapsedTime(meshed, particled).TotalMilliseconds;
        FinishMs = Stopwatch.GetElapsedTime(particled, finished).TotalMilliseconds;
    }

    private static bool IsOpaque(M2Model model, M2Batch batch)
    {
        M2Blend blend = batch.Material < model.Materials.Length ? model.Materials[batch.Material].Blend
                                                                : M2Blend.Opaque;
        return blend is M2Blend.Opaque or M2Blend.AlphaKey;
    }

    private void Prepare(int width, int height)
    {
        if (_width != width || _height != height)
        {
            _width = width;
            _height = height;
            _colour = new Vector3[width * height];
            _depth = new float[width * height];
        }

        for (int y = 0; y < height; ++y)
        {
            Vector3 row = Vector3.Lerp(BackgroundTop, BackgroundBottom, y / (float)Math.Max(1, height - 1));
            Array.Fill(_colour, row, y * width, width);
        }

        Array.Fill(_depth, 1f);
    }

    // Moves each vertex with the bones it's weighted to
    private void Skin(ModelInstance instance)
    {
        M2Vertex[] vertices = instance.Model.Vertices;
        if (_positions.Length != vertices.Length)
        {
            _positions = new Vector3[vertices.Length];
            _normals = new Vector3[vertices.Length];
        }

        Matrix4x4[] bones = instance.BoneMatrices;
        for (int i = 0; i < vertices.Length; ++i)
        {
            M2Vertex vertex = vertices[i];
            float total = vertex.Weights.X + vertex.Weights.Y + vertex.Weights.Z + vertex.Weights.W;
            if (bones.Length == 0 || total <= 0)
            {
                _positions[i] = vertex.Position;
                _normals[i] = vertex.Normal;
                continue;
            }

            Vector3 position = Vector3.Zero;
            Vector3 normal = Vector3.Zero;
            Add(bones, vertex.Bone0, vertex.Weights.X, vertex, ref position, ref normal);
            Add(bones, vertex.Bone1, vertex.Weights.Y, vertex, ref position, ref normal);
            Add(bones, vertex.Bone2, vertex.Weights.Z, vertex, ref position, ref normal);
            Add(bones, vertex.Bone3, vertex.Weights.W, vertex, ref position, ref normal);
            _positions[i] = position / total;
            _normals[i] = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : vertex.Normal;
        }
    }

    private static void Add(Matrix4x4[] bones, int bone, float weight, M2Vertex vertex, ref Vector3 position,
                            ref Vector3 normal)
    {
        if (weight <= 0 || bone >= bones.Length)
            return;
        position += Vector3.Transform(vertex.Position, bones[bone]) * weight;
        normal += Vector3.TransformNormal(vertex.Normal, bones[bone]) * weight;
    }

    private void DrawBatch(ModelInstance instance, M2Batch batch, Matrix4x4 viewProjection, Matrix4x4 view,
                           Vector3 light)
    {
        M2Model model = instance.Model;
        M2Skin skin = instance.Skin;
        if (batch.Submesh >= skin.Submeshes.Length)
            return;
        M2Material material = batch.Material < model.Materials.Length ? model.Materials[batch.Material]
                                                                      : new M2Material(0, M2Blend.Opaque);
        Vector4 colour = instance.BatchColour(batch);
        if (colour.W < INVISIBLE)
            return;

        ModelTexture texture = ModelTexture.White;
        if (batch.TextureCombo < model.TextureLookup.Length)
        {
            int index = model.TextureLookup[batch.TextureCombo];
            if (index < instance.Textures.Length)
                texture = instance.Textures[index];
        }

        ushort uvSet = batch.TexCoordCombo < model.TexUnitLookup.Length ? model.TexUnitLookup[batch.TexCoordCombo]
                                                                        : (ushort)0;
        Matrix3x2 uvMatrix = instance.TextureMatrix(batch);
        bool lit = (material.Flags & M2Material.FLAG_UNLIT) == 0;
        bool cull = (material.Flags & M2Material.FLAG_TWO_SIDED) == 0;
        bool depthTest = (material.Flags & M2Material.FLAG_NO_DEPTH_TEST) == 0;
        bool depthWrite = (material.Flags & M2Material.FLAG_NO_DEPTH_WRITE) == 0 &&
                          material.Blend is M2Blend.Opaque or M2Blend.AlphaKey;

        M2Submesh submesh = skin.Submeshes[batch.Submesh];
        if (!instance.ShowsGeoset(submesh.Id))
            return;
        int end = Math.Min(skin.Indices.Length, submesh.IndexStart + submesh.IndexCount);
        Span<ScreenVertex> corners = stackalloc ScreenVertex[3];
        for (int i = submesh.IndexStart; i + 2 < end; i += 3)
        {
            bool visible = true;
            for (int corner = 0; corner < 3 && visible; ++corner)
            {
                int vertex = skin.Indices[i + corner];
                Vector2 uv = uvSet switch
                {
                    1 => model.Vertices[vertex].Uv1,
                    ENVIRONMENT_UV => EnvironmentUv(_normals[vertex], view),
                    _ => model.Vertices[vertex].Uv0
                };
                float shade = lit ? AMBIENT + DIFFUSE * MathF.Max(0f, Vector3.Dot(_normals[vertex], light)) : 1f;
                visible = Project(_positions[vertex], viewProjection, Vector2.Transform(uv, uvMatrix),
                                  new Vector4(colour.X * shade, colour.Y * shade, colour.Z * shade, colour.W),
                                  out corners[corner]);
            }

            if (visible)
                DrawTriangle(corners[0], corners[1], corners[2], texture, material.Blend, cull, depthTest, depthWrite);
        }
    }

    // A shiny surface's reflection: texture coordinates from where its normal points on screen
    private static Vector2 EnvironmentUv(Vector3 normal, Matrix4x4 view)
    {
        Vector3 facing = Vector3.TransformNormal(normal, view);
        return new Vector2(facing.X * 0.5f + 0.5f, 0.5f - facing.Y * 0.5f);
    }

    private bool Project(Vector3 position, Matrix4x4 viewProjection, Vector2 uv, Vector4 colour,
                         out ScreenVertex result)
    {
        Vector4 clip = Vector4.Transform(new Vector4(position, 1f), viewProjection);
        return ToScreen(clip, uv, colour, out result);
    }

    private bool ToScreen(Vector4 clip, Vector2 uv, Vector4 colour, out ScreenVertex result)
    {
        if (clip.W < MIN_W)
        {
            result = default;
            return false;
        }

        float inverse = 1f / clip.W;
        result = new ScreenVertex((clip.X * inverse * 0.5f + 0.5f) * _width,
                                  (0.5f - clip.Y * inverse * 0.5f) * _height, clip.Z * inverse, inverse, uv, colour);
        return true;
    }

    private void DrawParticles(ModelInstance instance, Matrix4x4 view, Matrix4x4 projection)
    {
        List<(float Depth, ParticleSystem System, Particle Particle)> all =
            new List<(float, ParticleSystem, Particle)>();
        foreach (ParticleSystem system in instance.Particles)
        {
            foreach (Particle particle in system.Live)
                all.Add((Vector3.Transform(particle.Position, view).Z, system, particle));
        }

        // Furthest first, so nearer smoke covers what's behind it
        all.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        float picture = (float)_width * _height;
        float budget = picture * PARTICLE_FILL_BUDGET;
        float filled = 0f;
        // Nearest particles matter most, so the budget is spent from the near end
        List<bool> draw = Enumerable.Repeat(false, all.Count).ToList();
        Span<Vector3> probe = stackalloc Vector3[QUAD_CORNERS];
        for (int i = all.Count - 1; i >= 0 && filled < budget; --i)
        {
            (float _, ParticleSystem system, Particle particle) = all[i];
            float life = particle.Age / particle.Life;
            Vector2 size = system.Emitter.Scale.Value(life, Vector2.One, Vector2.Lerp) * particle.Size;
            Corners(system.Emitter, particle, size, view, probe);
            float area = ScreenArea(probe, projection);
            if (area > picture * MAX_PARTICLE_FILL)
                continue;
            filled += area;
            draw[i] = true;
        }

        Span<Vector3> quad = stackalloc Vector3[QUAD_CORNERS];
        Span<ScreenVertex> screen = stackalloc ScreenVertex[QUAD_CORNERS];
        for (int index = 0; index < all.Count; ++index)
        {
            if (!draw[index])
                continue;
            (float _, ParticleSystem system, Particle particle) = all[index];
            M2ParticleEmitter emitter = system.Emitter;
            float life = particle.Age / particle.Life;
            Vector3 rgb = emitter.Colour.Value(life, new Vector3(CHANNEL_MAX), ModelInstance.Lerp) / CHANNEL_MAX;
            float alpha = emitter.Alpha.Value(life, 1f, ModelInstance.Lerp);
            if (alpha < INVISIBLE)
                continue;
            Vector2 size = emitter.Scale.Value(life, Vector2.One, Vector2.Lerp) * particle.Size;
            int cell = (emitter.Flags & M2ParticleEmitter.FLAG_RANDOM_CELL) != 0
                           ? particle.Cell
                           : emitter.HeadCell.Value(life, (ushort)0, (a, _, _) => a);
            ModelTexture texture = emitter.Texture < instance.Textures.Length ? instance.Textures[emitter.Texture]
                                                                              : ModelTexture.White;
            Corners(emitter, particle, size, view, quad);
            int columns = emitter.Columns;
            int rows = emitter.Rows;
            cell = (int)((uint)cell % (uint)Math.Max(1L, Math.Min(int.MaxValue, (long)rows * columns)));
            float u0 = cell % columns / (float)columns;
            float v0 = cell / columns / (float)rows;
            float u1 = u0 + 1f / columns;
            float v1 = v0 + 1f / rows;
            Vector4 colour = new Vector4(rgb, alpha);
            bool visible = ToScreen(Vector4.Transform(new Vector4(quad[0], 1f), projection), new Vector2(u0, v0),
                                    colour, out screen[0]) &
                           ToScreen(Vector4.Transform(new Vector4(quad[1], 1f), projection), new Vector2(u1, v0),
                                    colour, out screen[1]) &
                           ToScreen(Vector4.Transform(new Vector4(quad[2], 1f), projection), new Vector2(u1, v1),
                                    colour, out screen[2]) &
                           ToScreen(Vector4.Transform(new Vector4(quad[3], 1f), projection), new Vector2(u0, v1),
                                    colour, out screen[3]);
            if (!visible)
                continue;
            DrawTriangle(screen[0], screen[1], screen[2], texture, emitter.Blend, false, true, false);
            DrawTriangle(screen[0], screen[2], screen[3], texture, emitter.Blend, false, true, false);
            ++ParticlesDrawn;
        }
    }

    // About how many pixels a quad covers on screen; behind the camera counts as nothing
    private float ScreenArea(ReadOnlySpan<Vector3> quad, Matrix4x4 projection)
    {
        Span<Vector2> points = stackalloc Vector2[QUAD_CORNERS];
        for (int i = 0; i < QUAD_CORNERS; ++i)
        {
            Vector4 clip = Vector4.Transform(new Vector4(quad[i], 1f), projection);
            if (clip.W < MIN_W)
                return 0f;
            points[i] = new Vector2(clip.X / clip.W * _width * 0.5f, clip.Y / clip.W * _height * 0.5f);
        }

        float twice = 0f;
        for (int i = 0; i < QUAD_CORNERS; ++i)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % QUAD_CORNERS];
            twice += a.X * b.Y - b.X * a.Y;
        }

        float area = MathF.Abs(twice) * 0.5f;
        return float.IsFinite(area) ? area : float.MaxValue;
    }

    // A particle's four corners in camera space: a square facing the camera, flat on the ground, or a streak
    private static void Corners(M2ParticleEmitter emitter, Particle particle, Vector2 size, Matrix4x4 view,
                                Span<Vector3> quad)
    {
        Vector3 centre = Vector3.Transform(particle.Position, view);
        float half = 0.5f;
        if (emitter.Parts == M2ParticleParts.Tail && particle.Velocity.LengthSquared() > 0)
        {
            Vector3 trail = Vector3.TransformNormal(particle.Velocity, view) * emitter.TailLength;
            Vector3 side = Vector3.Cross(trail, Vector3.UnitZ);
            side = side.LengthSquared() > 0 ? Vector3.Normalize(side) * size.X * half : Vector3.UnitX * size.X * half;
            quad[0] = centre - side;
            quad[1] = centre + side;
            quad[2] = centre + side - trail;
            quad[3] = centre - side - trail;
            return;
        }

        float angle = particle.Spin + particle.SpinRate * particle.Age;
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        Vector3 right;
        Vector3 up;
        if ((emitter.Flags & M2ParticleEmitter.FLAG_NO_BILLBOARD) != 0)
        {
            right = Vector3.TransformNormal(new Vector3(cos, sin, 0f), view) * size.X * half;
            up = Vector3.TransformNormal(new Vector3(-sin, cos, 0f), view) * size.Y * half;
        }
        else
        {
            right = new Vector3(cos, sin, 0f) * size.X * half;
            up = new Vector3(-sin, cos, 0f) * size.Y * half;
        }

        quad[0] = centre - right + up;
        quad[1] = centre + right + up;
        quad[2] = centre + right - up;
        quad[3] = centre - right - up;
    }

    private readonly record struct ScreenVertex(float X, float Y, float Z, float InverseW, Vector2 Uv,
                                                Vector4 Colour);

    private void DrawTriangle(ScreenVertex a, ScreenVertex b, ScreenVertex c, ModelTexture texture, M2Blend blend,
                              bool cull, bool depthTest, bool depthWrite)
    {
        float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(area) < float.Epsilon || (cull && area > 0))
            return;
        if ((a.Z > 1f && b.Z > 1f && c.Z > 1f) || (a.Z < 0f && b.Z < 0f && c.Z < 0f))
            return;

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int maxX = Math.Min(_width - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int maxY = Math.Min(_height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        if (minX > maxX || minY > maxY)
            return;
        ++TrianglesDrawn;

        float inverseArea = 1f / area;
        // The smaller copy of the texture that matches how many texels land on each pixel of this triangle
        float uvArea = MathF.Abs((b.Uv.X - a.Uv.X) * (c.Uv.Y - a.Uv.Y) - (b.Uv.Y - a.Uv.Y) * (c.Uv.X - a.Uv.X));
        texture = texture.Level(uvArea * texture.Width * texture.Height, MathF.Abs(area));
        for (int y = minY; y <= maxY; ++y)
        {
            float py = y + 0.5f;
            int row = y * _width;
            for (int x = minX; x <= maxX; ++x)
            {
                float px = x + 0.5f;
                float wa = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) * inverseArea;
                float wb = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) * inverseArea;
                float wc = 1f - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0)
                    continue;
                float z = wa * a.Z + wb * b.Z + wc * c.Z;
                if (z < 0f || z > 1f)
                    continue;
                int at = row + x;
                if (depthTest && z > _depth[at])
                    continue;

                // Perspective-correct: texture coordinates and colours are spread evenly in 3D, not on screen
                float pa = wa * a.InverseW;
                float pb = wb * b.InverseW;
                float pc = wc * c.InverseW;
                float scale = 1f / (pa + pb + pc);
                Vector2 uv = (a.Uv * pa + b.Uv * pb + c.Uv * pc) * scale;
                Vector4 shade = (a.Colour * pa + b.Colour * pb + c.Colour * pc) * scale;
                Vector4 source = texture.Sample(uv.X, uv.Y) * shade;
                if (!Blend(ref _colour[at], source, blend))
                    continue;
                if (depthWrite)
                    _depth[at] = z;
            }
        }
    }

    // Mixes a pixel into what's behind it the way the material says; false when the pixel is cut out
    private static bool Blend(ref Vector3 target, Vector4 source, M2Blend blend)
    {
        Vector3 rgb = new Vector3(source.X, source.Y, source.Z);
        switch (blend)
        {
            case M2Blend.Opaque:
                target = rgb;
                return true;
            case M2Blend.AlphaKey:
            {
                if (source.W < ALPHA_KEY)
                    return false;
                target = rgb;
                return true;
            }
            case M2Blend.NoAlphaAdd:
                target += rgb;
                return true;
            case M2Blend.Add:
                target += rgb * source.W;
                return true;
            case M2Blend.Mod:
                target *= rgb;
                return true;
            case M2Blend.Mod2x:
                target *= rgb * MOD2X;
                return true;
            case M2Blend.BlendAdd:
                target = rgb + target * (1f - source.W);
                return true;
            default:
                target = Vector3.Lerp(target, rgb, Math.Clamp(source.W, 0f, 1f));
                return true;
        }
    }
}
