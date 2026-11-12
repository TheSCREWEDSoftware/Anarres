namespace Anarres.Core.Mpq;

// Spell icons and other files from the client's MPQs; empty when no WoW Data folder is set
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

    // Any file from the client, for models and their textures; null when no archive has it
    public byte[]? ReadFile(string name)
    {
        if (_archives == null)
            return null;
        lock (_lock)
        {
            try
            {
                return _archives.TryRead(name);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or
                                                  IOException)
            {
                return null;
            }
        }
    }

    public bool Contains(string name)
    {
        if (_archives == null)
            return false;
        lock (_lock)
            return _archives.Archives.Any(archive => archive.Contains(name));
    }

    // Every file the archives list that ends with a suffix (such as .m2), sorted and without repeats
    public List<string> ListFiles(string suffix)
    {
        if (_archives == null)
            return new List<string>();
        lock (_lock)
        {
            return _archives.Archives.SelectMany(archive => archive.ListFiles())
                            .Where(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Order(StringComparer.OrdinalIgnoreCase)
                            .ToList();
        }
    }

    public void Dispose()
    {
        _archives?.Dispose();
    }
}
