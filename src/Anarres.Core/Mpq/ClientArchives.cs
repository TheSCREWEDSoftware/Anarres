namespace Anarres.Core.Mpq;

// A WoW 3.3.5a client's Data folder: its MPQ archives in the order the client reads them, last one winning
public sealed class ClientArchives : IDisposable
{
    public const string DBC_FOLDER = "DBFilesClient";

    // Base archives, then the locale's, then patches in Data, then the locale's patches
    private const int BASE_PRIORITY = 0;
    private const int LOCALE_PRIORITY = 100;
    private const int PATCH_PRIORITY = 200;
    private const int LOCALE_PATCH_PRIORITY = 300;
    // patch-2 to patch-9, then patch-a to patch-z
    private const int LETTER_RANK_OFFSET = 10;

    private static readonly string[] BaseArchives = { "common", "common-2", "expansion", "lichking" };
    private static readonly string[] LocaleArchives = { "locale", "speech", "expansion-locale", "expansion-speech",
                                                        "lichking-locale", "lichking-speech" };

    private readonly List<MpqArchive> _archives;

    private ClientArchives(List<MpqArchive> archives)
    {
        _archives = archives;
    }

    // Read from first to last; a later archive's copy of a file replaces an earlier one
    public IReadOnlyList<MpqArchive> Archives => _archives;

    // dataFolder is WoW's Data folder; locale such as enUS, or null for the first locale folder found
    public static ClientArchives Open(string dataFolder, string? locale = null)
    {
        if (!Directory.Exists(dataFolder))
            throw new DirectoryNotFoundException("The folder " + dataFolder + " doesn't exist.");

        locale ??= Directory.EnumerateDirectories(dataFolder).Select(Path.GetFileName)
                            .FirstOrDefault(name => name is { Length: 4 } && char.IsLower(name[0]) &&
                                                    char.IsUpper(name[2]));
        List<(int Priority, string Path)> found = new List<(int, string)>();
        foreach (string file in Directory.EnumerateFiles(dataFolder, "*.mpq", Files.AnyCase))
            found.Add((Priority(Path.GetFileNameWithoutExtension(file), null), file));
        if (locale != null && Directory.Exists(Path.Combine(dataFolder, locale)))
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(dataFolder, locale), "*.mpq", Files.AnyCase))
                found.Add((Priority(Path.GetFileNameWithoutExtension(file), locale), file));
        }

        List<MpqArchive> archives = new List<MpqArchive>();
        try
        {
            foreach ((int _, string path) in found.Where(entry => entry.Priority >= 0)
                                                  .OrderBy(entry => entry.Priority)
                                                  .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
                archives.Add(MpqArchive.Open(path));
        }
        catch
        {
            archives.ForEach(archive => archive.Dispose());
            throw;
        }

        return new ClientArchives(archives);
    }

    // The client's load order; -1 for archives it never reads
    public static int Priority(string name, string? locale)
    {
        string lower = name.ToLowerInvariant();
        if (locale == null)
        {
            int index = Array.IndexOf(BaseArchives, lower);
            if (index >= 0)
                return BASE_PRIORITY + index;
            if (lower == "patch")
                return PATCH_PRIORITY;
            if (lower.StartsWith("patch-", StringComparison.Ordinal))
                return PatchRank(lower["patch-".Length..]) is { } rank ? PATCH_PRIORITY + rank : -1;
            return -1;
        }

        string localeLower = locale.ToLowerInvariant();
        string suffix = "-" + localeLower;
        if (lower.EndsWith(suffix, StringComparison.Ordinal))
        {
            int index = Array.IndexOf(LocaleArchives, lower[..^suffix.Length]);
            if (index >= 0)
                return LOCALE_PRIORITY + index;
        }

        string patch = "patch-" + localeLower;
        if (lower == patch)
            return LOCALE_PATCH_PRIORITY;
        if (lower.StartsWith(patch + "-", StringComparison.Ordinal))
            return PatchRank(lower[(patch.Length + 1)..]) is { } rank ? LOCALE_PATCH_PRIORITY + rank : -1;
        return -1;
    }

    // The newest copy of a file across the archives, or null when none has it or the newest deletes it
    public byte[]? TryRead(string name)
    {
        for (int i = _archives.Count - 1; i >= 0; --i)
        {
            if (_archives[i].IsDeleted(name))
                return null;
            if (_archives[i].Contains(name))
                return _archives[i].TryRead(name);
        }

        return null;
    }

    // Every DBFilesClient\*.dbc any archive lists, plus known files found in archives without a (listfile)
    public List<string> ListDbcFiles(IEnumerable<string>? knownFiles = null)
    {
        IEnumerable<string> listed = _archives.SelectMany(archive => archive.ListFiles())
                                              .Where(name => name.StartsWith(DBC_FOLDER + "\\",
                                                                             StringComparison.OrdinalIgnoreCase) &&
                                                             name.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase));
        IEnumerable<string> probed = (knownFiles ?? Dbc.DbcDefinitions.Default.All.Select(entry => entry.File))
                                     .Select(file => DBC_FOLDER + "\\" + file)
                                     .Where(name => _archives.Any(archive => archive.Contains(name)));
        return listed.Concat(probed)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }

    public void Dispose()
    {
        _archives.ForEach(archive => archive.Dispose());
    }

    private static int? PatchRank(string tail)
    {
        if (tail.Length != 1)
            return null;
        char character = tail[0];
        if (character is >= '2' and <= '9')
            return character - '0';
        if (character is >= 'a' and <= 'z')
            return LETTER_RANK_OFFSET + (character - 'a');
        return null;
    }
}
