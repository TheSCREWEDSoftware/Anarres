using System.Runtime.InteropServices;
using Anarres.Core.Dbc;
using Anarres.Core.Mpq;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Anarres.App;

// Spell icons as bitmaps: SpellIcon.dbc gives the texture, the client's MPQs the picture
public sealed class IconImages
{
    private const double DPI = 96;

    private readonly Dictionary<uint, Bitmap?> _byIcon = new Dictionary<uint, Bitmap?>();

    public IconImages(IconLibrary library, DbcTable? icons)
    {
        Library = library;
        Icons = icons;
    }

    public IconLibrary Library { get; }

    public DbcTable? Icons { get; }

    public bool IsAvailable => Library.IsAvailable && Icons != null;

    public string Texture(uint icon)
    {
        int row = Icons?.FindRow(icon) ?? -1;
        if (Icons == null || row < 0)
            return string.Empty;
        int column = Icons.Definition.Columns.FindIndex(entry => entry.Type == DbcColumnType.String);
        return column >= 0 ? Icons.GetString(row, column) : string.Empty;
    }

    public Bitmap? Get(uint icon)
    {
        if (!IsAvailable || icon == 0)
            return null;
        if (_byIcon.TryGetValue(icon, out Bitmap? cached))
            return cached;
        BlpImage? image = Library.Get(Texture(icon));
        Bitmap? bitmap = image == null ? null : ToBitmap(image);
        _byIcon[icon] = bitmap;
        return bitmap;
    }

    public static Bitmap ToBitmap(BlpImage image)
    {
        WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(DPI, DPI),
                                                     PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer frame = bitmap.Lock();
        int rowBytes = image.Width * 4;
        for (int y = 0; y < image.Height; ++y)
            Marshal.Copy(image.Rgba, y * rowBytes, frame.Address + y * frame.RowBytes, rowBytes);
        return bitmap;
    }
}
