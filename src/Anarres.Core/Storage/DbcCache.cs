using System.Security.Cryptography;
using System.Text;
using Anarres.Core.Dbc;
using Anarres.Core.Settings;

namespace Anarres.Core.Storage;

// Big tables kept on this PC as .dbc files, used only while the database's stamp for the table still matches
public sealed class DbcCache
{
    public const string FOLDER_NAME = "cache";

    // Smaller tables load from the database quickly enough on their own
    public const long MIN_CELLS = 100_000;

    private const string DATA_EXTENSION = ".dbc";
    private const string STAMP_EXTENSION = ".stamp";
    private const string TEMP_EXTENSION = ".tmp";

    public DbcCache(string folder)
    {
        Folder = folder;
    }

    public static string DefaultFolder => Path.Combine(ProjectSettings.DefaultFolder, FOLDER_NAME);

    public string Folder { get; }

    public static bool IsWorthKeeping(DbcTable table)
    {
        return (long)table.Rows.Count * table.Definition.Columns.Count >= MIN_CELLS;
    }

    // The kept copy, or null when there's none, it's out of date or it can't be read
    public DbcTable? TryRead(string store, DbcDefinition definition, string stamp)
    {
        (string data, string stampPath) = Paths(store, definition.File);
        try
        {
            if (!File.Exists(data) || !File.Exists(stampPath) ||
                File.ReadAllText(stampPath) != StampText(definition, stamp))
                return null;
            return DbcFile.Read(File.ReadAllBytes(data), definition);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    // Keeps a copy; a failure only means the next load reads the database again
    public void Write(string store, DbcTable table, string stamp)
    {
        (string data, string stampPath) = Paths(store, table.Definition.File);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(data)!);
            // The old stamp goes first, so a copy left half-written by a crash is never taken as current
            File.Delete(stampPath);
            File.WriteAllBytes(data + TEMP_EXTENSION, DbcFile.Write(table));
            File.Move(data + TEMP_EXTENSION, data, overwrite: true);
            File.WriteAllText(stampPath, StampText(table.Definition, stamp));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Forget(store, table.Definition.File);
        }
    }

    public void Forget(string store, string file)
    {
        (string data, string stampPath) = Paths(store, file);
        try
        {
            File.Delete(stampPath);
            File.Delete(data);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without its stamp file the copy is never used, so a locked .dbc can wait for the next clear
        }
    }

    public long SizeBytes()
    {
        if (!Directory.Exists(Folder))
            return 0;
        return new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
    }

    public void Clear()
    {
        if (Directory.Exists(Folder))
            Directory.Delete(Folder, recursive: true);
    }

    // One folder per database, named by a hash of where it is
    private (string Data, string Stamp) Paths(string store, string file)
    {
        string folder = Path.Combine(Folder, Hash(store)[..16]);
        string name = Path.GetFileNameWithoutExtension(file);
        return (Path.Combine(folder, name + DATA_EXTENSION), Path.Combine(folder, name + STAMP_EXTENSION));
    }

    // The stamp plus the layout, so a changed definition isn't read with an old copy
    private static string StampText(DbcDefinition definition, string stamp)
    {
        string layout = string.Join(";", definition.Columns.Select(column => column.Name + ":" + column.Type)) +
                        (definition.IndexIsRowNumber ? ";row-index" : string.Empty);
        return stamp + "\n" + Hash(layout);
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
