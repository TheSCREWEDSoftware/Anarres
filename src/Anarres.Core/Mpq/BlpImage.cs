using System.Buffers.Binary;

namespace Anarres.Core.Mpq;

// Decodes BLP2 textures, the format of WoW 3.3.5a's icons: paletted, DXT1/3/5 or plain BGRA, first mip level only
public sealed class BlpImage
{
    public const uint SIGNATURE = 0x32504C42; // "BLP2" read as a little-endian number

    private const int MIP_COUNT = 16;
    private const int PALETTE_OFFSET = 148;
    private const int PALETTE_SIZE = 256;
    private const byte COMPRESSION_PALETTE = 1;
    private const byte COMPRESSION_DXT = 2;
    private const byte COMPRESSION_BGRA = 3;
    private const byte ALPHA_TYPE_DXT5 = 7;
    private const int BLOCK = 4;
    private const int PIXELS_PER_BLOCK = 16;
    private const int CHANNELS = 4;
    private const int MAX_SIZE = 4096;

    private BlpImage(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    // Four bytes per pixel, red, green, blue then alpha, not premultiplied
    public byte[] Rgba { get; }

    public static BlpImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < PALETTE_OFFSET + PALETTE_SIZE * CHANNELS ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != SIGNATURE)
            throw new InvalidDataException("This is not a BLP2 texture.");

        byte compression = data[8];
        byte alphaDepth = data[9];
        byte alphaType = data[10];
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);
        int size = BinaryPrimitives.ReadInt32LittleEndian(data[(20 + MIP_COUNT * 4)..]);
        if (width <= 0 || height <= 0 || width > MAX_SIZE || height > MAX_SIZE || offset < 0 || size < 0 ||
            offset + size > data.Length)
            throw new InvalidDataException("The BLP texture's header isn't valid.");

        ReadOnlySpan<byte> pixels = data.Slice(offset, size);
        byte[] rgba = new byte[width * height * CHANNELS];
        switch (compression)
        {
            case COMPRESSION_PALETTE:
                DecodePalette(data.Slice(PALETTE_OFFSET, PALETTE_SIZE * CHANNELS), pixels, alphaDepth, width, height,
                              rgba);
                break;
            case COMPRESSION_DXT:
            {
                DxtFormat format = alphaType == ALPHA_TYPE_DXT5 ? DxtFormat.Dxt5
                                   : alphaDepth > 1 ? DxtFormat.Dxt3
                                   : DxtFormat.Dxt1;
                DecodeDxt(pixels, format, width, height, rgba);
                break;
            }
            case COMPRESSION_BGRA:
            {
                for (int i = 0; i + 3 < pixels.Length && i < rgba.Length; i += CHANNELS)
                {
                    rgba[i] = pixels[i + 2];
                    rgba[i + 1] = pixels[i + 1];
                    rgba[i + 2] = pixels[i];
                    rgba[i + 3] = pixels[i + 3];
                }

                break;
            }
            default:
                throw new NotSupportedException("This BLP texture uses compression " + compression +
                                                ", which Anarres doesn't read.");
        }

        return new BlpImage(width, height, rgba);
    }

    private enum DxtFormat
    {
        Dxt1,
        Dxt3,
        Dxt5
    }

    private static void DecodePalette(ReadOnlySpan<byte> palette, ReadOnlySpan<byte> pixels, byte alphaDepth,
                                      int width, int height, byte[] rgba)
    {
        int count = width * height;
        for (int i = 0; i < count && i < pixels.Length; ++i)
        {
            int entry = pixels[i] * CHANNELS;
            rgba[i * CHANNELS] = palette[entry + 2];
            rgba[i * CHANNELS + 1] = palette[entry + 1];
            rgba[i * CHANNELS + 2] = palette[entry];
            rgba[i * CHANNELS + 3] = Alpha(pixels, count, i, alphaDepth);
        }
    }

    // Alpha follows the colour indices: one byte, four bits or one bit a pixel
    private static byte Alpha(ReadOnlySpan<byte> pixels, int count, int pixel, byte depth)
    {
        const byte OPAQUE = 255;
        const int FOUR_BIT_SCALE = 17;
        switch (depth)
        {
            case 8:
                return count + pixel < pixels.Length ? pixels[count + pixel] : OPAQUE;
            case 4:
            {
                int index = count + pixel / 2;
                if (index >= pixels.Length)
                    return OPAQUE;
                int nibble = pixel % 2 == 0 ? pixels[index] & 0x0F : pixels[index] >> 4;
                return (byte)(nibble * FOUR_BIT_SCALE);
            }
            case 1:
            {
                int index = count + pixel / 8;
                if (index >= pixels.Length)
                    return OPAQUE;
                return (pixels[index] >> (pixel % 8) & 1) != 0 ? OPAQUE : (byte)0;
            }
            default:
                return OPAQUE;
        }
    }

    private static void DecodeDxt(ReadOnlySpan<byte> data, DxtFormat format, int width, int height, byte[] rgba)
    {
        int blockSize = format == DxtFormat.Dxt1 ? 8 : 16;
        int blocksWide = Math.Max(1, (width + BLOCK - 1) / BLOCK);
        int blocksHigh = Math.Max(1, (height + BLOCK - 1) / BLOCK);
        byte[] colours = new byte[PIXELS_PER_BLOCK * CHANNELS];
        for (int by = 0; by < blocksHigh; ++by)
        {
            for (int bx = 0; bx < blocksWide; ++bx)
            {
                int at = (by * blocksWide + bx) * blockSize;
                if (at + blockSize > data.Length)
                    return;
                ReadOnlySpan<byte> block = data.Slice(at, blockSize);
                ReadOnlySpan<byte> colourBlock = format == DxtFormat.Dxt1 ? block : block[8..];
                DecodeColours(colourBlock, format == DxtFormat.Dxt1, colours);
                if (format == DxtFormat.Dxt3)
                    DecodeExplicitAlpha(block, colours);
                else if (format == DxtFormat.Dxt5)
                    DecodeInterpolatedAlpha(block, colours);

                for (int py = 0; py < BLOCK; ++py)
                {
                    for (int px = 0; px < BLOCK; ++px)
                    {
                        int x = bx * BLOCK + px;
                        int y = by * BLOCK + py;
                        if (x >= width || y >= height)
                            continue;
                        Array.Copy(colours, (py * BLOCK + px) * CHANNELS, rgba, (y * width + x) * CHANNELS, CHANNELS);
                    }
                }
            }
        }
    }

    private static void DecodeColours(ReadOnlySpan<byte> block, bool punchThrough, byte[] output)
    {
        ushort first = BinaryPrimitives.ReadUInt16LittleEndian(block);
        ushort second = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        Span<byte> palette = stackalloc byte[4 * CHANNELS];
        Expand(first, palette[..CHANNELS]);
        Expand(second, palette.Slice(CHANNELS, CHANNELS));
        bool fourColours = first > second || !punchThrough;
        for (int channel = 0; channel < 3; ++channel)
        {
            int a = palette[channel];
            int b = palette[CHANNELS + channel];
            palette[2 * CHANNELS + channel] = (byte)(fourColours ? (2 * a + b) / 3 : (a + b) / 2);
            palette[3 * CHANNELS + channel] = (byte)(fourColours ? (a + 2 * b) / 3 : 0);
        }

        palette[2 * CHANNELS + 3] = 255;
        palette[3 * CHANNELS + 3] = fourColours ? (byte)255 : (byte)0;
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        for (int pixel = 0; pixel < PIXELS_PER_BLOCK; ++pixel)
        {
            int index = (int)(indices >> (pixel * 2) & 3);
            palette.Slice(index * CHANNELS, CHANNELS).CopyTo(output.AsSpan(pixel * CHANNELS, CHANNELS));
        }
    }

    // 5-6-5 bit colour to 8 bits a channel
    private static void Expand(ushort colour, Span<byte> output)
    {
        int red = colour >> 11 & 0x1F;
        int green = colour >> 5 & 0x3F;
        int blue = colour & 0x1F;
        output[0] = (byte)(red << 3 | red >> 2);
        output[1] = (byte)(green << 2 | green >> 4);
        output[2] = (byte)(blue << 3 | blue >> 2);
        output[3] = 255;
    }

    private static void DecodeExplicitAlpha(ReadOnlySpan<byte> block, byte[] output)
    {
        const int FOUR_BIT_SCALE = 17;
        for (int pixel = 0; pixel < PIXELS_PER_BLOCK; ++pixel)
        {
            int value = block[pixel / 2] >> (pixel % 2 * 4) & 0x0F;
            output[pixel * CHANNELS + 3] = (byte)(value * FOUR_BIT_SCALE);
        }
    }

    private static void DecodeInterpolatedAlpha(ReadOnlySpan<byte> block, byte[] output)
    {
        Span<int> alphas = stackalloc int[8];
        alphas[0] = block[0];
        alphas[1] = block[1];
        if (alphas[0] > alphas[1])
        {
            for (int i = 1; i < 7; ++i)
                alphas[i + 1] = ((7 - i) * alphas[0] + i * alphas[1]) / 7;
        }
        else
        {
            for (int i = 1; i < 5; ++i)
                alphas[i + 1] = ((5 - i) * alphas[0] + i * alphas[1]) / 5;
            alphas[6] = 0;
            alphas[7] = 255;
        }

        ulong bits = 0;
        for (int i = 0; i < 6; ++i)
            bits |= (ulong)block[2 + i] << (8 * i);
        for (int pixel = 0; pixel < PIXELS_PER_BLOCK; ++pixel)
            output[pixel * CHANNELS + 3] = (byte)alphas[(int)(bits >> (3 * pixel) & 7)];
    }
}
