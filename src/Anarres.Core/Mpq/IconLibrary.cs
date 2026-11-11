namespace Anarres.Core.Mpq;

// Spell icons from the client's MPQs, decoded once and kept; empty when no WoW Data folder is set
public sealed class IconLibrary : IDisposable
{
    private const string EXTENSION = ".blp";

    private readonly Dictionary<string, BlpImage?> _cache =
        new Dictionary<string, BlpImage?>(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new object();
    private readonly ClientArchives? _archives;

    public IconLibrary(string dataFolder, string locale)
    {
        if (dataFolder.Length > 0 && Directory.Exists(dataFolder))
            _archives = ClientArchives.Open(dataFolder, locale);
    }

    public bool IsAvailable => _archives != null;

    // texture is SpellIcon.dbc's path, such as Interface\Icons\Spell_Frost_FrostBolt02
    public BlpImage? Get(string texture)
    {
        if (_archives == null || texture.Length == 0)
            return null;
        string name = texture.EndsWith(EXTENSION, StringComparison.OrdinalIgnoreCase) ? texture : texture + EXTENSION;
        lock (_lock)
        {
            if (_cache.TryGetValue(name, out BlpImage? cached))
                return cached;
            BlpImage? image = null;
            try
            {
                byte[]? data = _archives.TryRead(name);
                if (data != null)
                    image = BlpImage.Decode(data);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or
                                                  IOException)
            {
                // A texture that can't be read shows no icon
            }

            _cache[name] = image;
            return image;
        }
    }

    public void Dispose()
    {
        _archives?.Dispose();
    }
}
