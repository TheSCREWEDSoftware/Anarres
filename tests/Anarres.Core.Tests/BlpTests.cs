using System.Buffers.Binary;
using Anarres.Core.Mpq;

namespace Anarres.Core.Tests;

public static class BlpTests
{
    private const int HEADER = 148 + 1024;

    private static byte[] Header(byte compression, byte alphaDepth, byte alphaType, int width, int height,
                                 byte[] pixels)
    {
        byte[] data = new byte[HEADER + pixels.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, BlpImage.SIGNATURE);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1);
        data[8] = compression;
        data[9] = alphaDepth;
        data[10] = alphaType;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), height);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), HEADER);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(84), pixels.Length);
        pixels.CopyTo(data, HEADER);
        return data;
    }

    public static void DecodesPalettedTextures()
    {
        // Two pixels: palette entries 1 and 2, then an 8-bit alpha for each
        byte[] data = Header(1, 8, 0, 2, 1, new byte[] { 1, 2, 255, 128 });
        data[148 + 4] = 10; // entry 1 is blue 10, green 20, red 30
        data[148 + 5] = 20;
        data[148 + 6] = 30;
        data[148 + 8 + 2] = 200; // entry 2 is red 200
        BlpImage image = BlpImage.Decode(data);
        Assert.Equal(2, image.Width, "width");
        Assert.Equal("30,20,10,255,200,0,0,128", string.Join(",", image.Rgba), "pixels as RGBA");
    }

    public static void DecodesDxt1()
    {
        // One block: colour 0 pure red, colour 1 pure blue, every pixel index 1
        byte[] block = { 0x00, 0xF8, 0x1F, 0x00, 0x55, 0x55, 0x55, 0x55 };
        BlpImage image = BlpImage.Decode(Header(2, 0, 0, 4, 4, block));
        Assert.Equal("0,0,255,255", string.Join(",", image.Rgba.Take(4)), "blue pixel");
        Assert.Equal(64, image.Rgba.Length, "16 pixels");
    }

    public static void RefusesOtherFiles()
    {
        Assert.Throws<InvalidDataException>(() => BlpImage.Decode(new byte[10]), "too short");
    }
}
