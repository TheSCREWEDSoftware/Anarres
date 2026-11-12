using System.Numerics;
using Anarres.Core.Mpq;

namespace Anarres.Core.Models;

// A decoded texture ready to sample: colours from 0 to 1, wrapping or clamped per axis
public sealed class ModelTexture
{
    private const float BYTE_SCALE = 1f / 255f;
    private const int CHANNELS = 4;

    private readonly Vector4[] _pixels;
    // Mipmaps: each level half the last, so a texture far smaller on screen is averaged instead of sparkling
    private readonly List<ModelTexture> _levels = new List<ModelTexture>();

    public ModelTexture(int width, int height, Vector4[] pixels, bool wrapX = true, bool wrapY = true)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
        WrapX = wrapX;
        WrapY = wrapY;
        _levels.Add(this);
        ModelTexture level = this;
        while (level.Width > 1 || level.Height > 1)
        {
            level = level.Half();
            _levels.Add(level);
        }
    }

    private ModelTexture(int width, int height, Vector4[] pixels, bool wrapX, bool wrapY, bool level)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
        WrapX = wrapX;
        WrapY = wrapY;
    }

    public int Levels => _levels.Count;

    // The level for a triangle whose texture area is texels and whose screen area is pixels
    public ModelTexture Level(float texels, float pixels)
    {
        if (_levels.Count <= 1 || pixels <= 0 || texels <= pixels)
            return this;
        int level = (int)(0.5f * MathF.Log2(texels / pixels));
        return _levels[Math.Clamp(level, 0, _levels.Count - 1)];
    }

    private ModelTexture Half()
    {
        int width = Math.Max(1, Width / 2);
        int height = Math.Max(1, Height / 2);
        Vector4[] pixels = new Vector4[width * height];
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                int x0 = Math.Min(Width - 1, x * 2);
                int y0 = Math.Min(Height - 1, y * 2);
                int x1 = Math.Min(Width - 1, x0 + 1);
                int y1 = Math.Min(Height - 1, y0 + 1);
                pixels[y * width + x] = (_pixels[y0 * Width + x0] + _pixels[y0 * Width + x1] +
                                         _pixels[y1 * Width + x0] + _pixels[y1 * Width + x1]) * 0.25f;
            }
        }

        return new ModelTexture(width, height, pixels, WrapX, WrapY, level: true);
    }

    public int Width { get; }

    public int Height { get; }

    public bool WrapX { get; }

    public bool WrapY { get; }

    // A plain white texture, for batches whose texture the game fills in and Anarres can't find
    public static ModelTexture White { get; } = new ModelTexture(1, 1, new[] { Vector4.One });

    public static ModelTexture FromBlp(byte[] data, uint flags)
    {
        BlpImage image = BlpImage.Decode(data);
        return FromRgba(image.Width, image.Height, image.Rgba, (flags & M2Texture.FLAG_WRAP_X) != 0,
                        (flags & M2Texture.FLAG_WRAP_Y) != 0);
    }

    public static ModelTexture FromRgba(int width, int height, byte[] rgba, bool wrapX = true, bool wrapY = true)
    {
        Vector4[] pixels = new Vector4[width * height];
        for (int i = 0; i < pixels.Length; ++i)
        {
            int at = i * CHANNELS;
            pixels[i] = new Vector4(rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) * BYTE_SCALE;
        }

        return new ModelTexture(width, height, pixels, wrapX, wrapY);
    }

    // Bilinear filtering, so small textures stretched over a big model don't look like tiles
    public Vector4 Sample(float u, float v)
    {
        float x = u * Width - 0.5f;
        float y = v * Height - 0.5f;
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        Vector4 top = Vector4.Lerp(Pixel(x0, y0), Pixel(x0 + 1, y0), fx);
        Vector4 bottom = Vector4.Lerp(Pixel(x0, y0 + 1), Pixel(x0 + 1, y0 + 1), fx);
        return Vector4.Lerp(top, bottom, fy);
    }

    private Vector4 Pixel(int x, int y)
    {
        x = WrapX ? ((x % Width) + Width) % Width : Math.Clamp(x, 0, Width - 1);
        y = WrapY ? ((y % Height) + Height) % Height : Math.Clamp(y, 0, Height - 1);
        return _pixels[y * Width + x];
    }
}
