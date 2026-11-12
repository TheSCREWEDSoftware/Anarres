namespace Anarres.Core.Mpq;

// One archive the client loads, in order, with the .dbc files it has and those it's the last to have (Wins)
public sealed record PatchArchive(string Path, List<string> DbcFiles, List<string> Wins, bool IsOurs);

// The client's archives in the order it reads them, so a later patch hiding an earlier one shows up
public static class ClientPatches
{
    public static List<PatchArchive> List(string dataFolder, string locale, string ourPatch)
    {
        using ClientArchives archives = ClientArchives.Open(dataFolder, locale);
        List<string> known = archives.ListDbcFiles();
        List<(string Path, List<string> Files)> found = archives.Archives
            .Select(archive => (archive.Path, known.Where(archive.Contains).ToList()))
            .ToList();
        // The last archive with a file is the one the client reads it from
        Dictionary<string, string> winner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, List<string> files) in found)
        {
            foreach (string file in files)
                winner[file] = path;
        }

        string ours = ourPatch.Length > 0 ? System.IO.Path.GetFullPath(ourPatch) : string.Empty;
        return found.Select(entry => new PatchArchive(
                        entry.Path, entry.Files.Select(FileName).ToList(),
                        entry.Files.Where(file => winner[file] == entry.Path).Select(FileName).ToList(),
                        string.Equals(System.IO.Path.GetFullPath(entry.Path), ours,
                                      StringComparison.OrdinalIgnoreCase)))
                    .ToList();
    }

    // MPQ names use backslashes, which Path.GetFileName doesn't split on Linux or macOS
    private static string FileName(string path)
    {
        return path[(path.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
    }

    // Files in our patch that a later archive replaces, so the client never sees our copy
    public static List<string> Hidden(List<PatchArchive> archives)
    {
        PatchArchive? ours = archives.FirstOrDefault(archive => archive.IsOurs);
        return ours == null ? new List<string>() : ours.DbcFiles.Except(ours.Wins).ToList();
    }
}
