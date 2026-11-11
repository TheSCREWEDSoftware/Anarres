using System.Text.Json;
using System.Text.Json.Serialization;
using Anarres.Core.Storage;

namespace Anarres.Core.Settings;

public enum StoreKind
{
    Sqlite,
    MySql
}

// Where data is kept and where exports go; saved as settings.json in the user's application data folder
public sealed class ProjectSettings
{
    public const string APP_FOLDER = "Anarres";
    public const string FILE_NAME = "settings.json";
    public const string DEFAULT_PATCH_NAME = "patch-A.MPQ";
    public const string DEFAULT_LOCALE = "enUS";

    // AzerothCore's own defaults, so a fresh install connects without changes
    public const string DEFAULT_MYSQL_HOST = "localhost";
    public const string DEFAULT_MYSQL_USER = "acore";
    public const string DEFAULT_MYSQL_PASSWORD = "acore";
    public const string DEFAULT_MYSQL_DATABASE = "acore_dbc_extracted";
    public const string DEFAULT_WORLD_DATABASE = "acore_world";

    private const string DBC_FOLDER = "dbc";
    private const string DATA_FOLDER = "Data";
    private const string CACHE_FOLDER = "Cache";

    private static readonly string[] DataDirFolders = { "maps", "vmaps", "mmaps", "Cameras" };

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), APP_FOLDER);

    public static string DefaultPath => Path.Combine(DefaultFolder, FILE_NAME);

    public StoreKind Store { get; set; } = StoreKind.Sqlite;

    public string SqlitePath { get; set; } = Path.Combine(DefaultFolder, "project" + SqliteDbcStore.EXTENSION);

    public string MySqlHost { get; set; } = DEFAULT_MYSQL_HOST;

    public int MySqlPort { get; set; } = MySqlSettings.DEFAULT_PORT;

    public string MySqlUser { get; set; } = DEFAULT_MYSQL_USER;

    // Kept only when the user ticks Remember password
    public string MySqlPassword { get; set; } = DEFAULT_MYSQL_PASSWORD;

    public bool RememberMySqlPassword { get; set; } = true;

    // Created on first use; a database of its own keeps the extracted DBC data apart from acore_world
    public string MySqlDatabase { get; set; } = DEFAULT_MYSQL_DATABASE;

    public TableNaming MySqlNaming { get; set; } = TableNaming.DbcName;

    // Clean .dbc files to import from, such as an extracted DBFilesClient folder
    public string SourceDbcFolder { get; set; } = string.Empty;

    // The WoW folder or its Data folder, to read DBCs from its MPQs and to put the patch in
    public string ClientDataFolder { get; set; } = string.Empty;

    public string Locale { get; set; } = DEFAULT_LOCALE;

    // Exported .dbc files are written here, under DBFilesClient
    public string ExportFolder { get; set; } = Path.Combine(DefaultFolder, "Export");

    // The worldserver's DataDir or its dbc folder; exported files are copied to the dbc folder when set
    public string ServerDbcFolder { get; set; } = string.Empty;

    public string PatchName { get; set; } = DEFAULT_PATCH_NAME;

    // Where to keep the patch: in Data itself, or in Data's locale folder
    public bool PatchInLocaleFolder { get; set; }

    // Extra definition .json files that replace or add to the built-in ones
    public string DefinitionsFolder { get; set; } = Path.Combine(DefaultFolder, "definitions");

    // Tables the last export used, ticked again next time, and the ones Apply writes
    public List<string> ExportTables { get; set; } = new List<string> { "Spell.dbc" };

    // What Apply does, in one go after saving
    public bool ApplyToServer { get; set; } = true;

    public bool ApplyPatch { get; set; } = true;

    // The server's own copy of each file is kept once, the first time Apply replaces it
    public bool ApplyBackupServer { get; set; } = true;

    // Rows changed from the clean files also go into acore_world's _dbc tables
    public bool ApplyWorldDatabase { get; set; }

    // The client's Cache folder is emptied, so it doesn't show old spell text
    public bool ApplyClearClientCache { get; set; }

    public string WorldHost { get; set; } = DEFAULT_MYSQL_HOST;

    public int WorldPort { get; set; } = MySqlSettings.DEFAULT_PORT;

    public string WorldUser { get; set; } = DEFAULT_MYSQL_USER;

    public string WorldPassword { get; set; } = DEFAULT_MYSQL_PASSWORD;

    public string WorldDatabase { get; set; } = DEFAULT_WORLD_DATABASE;

    public MySqlSettings MySql(string? password = null)
    {
        return new MySqlSettings(MySqlHost, MySqlPort, MySqlUser, password ?? MySqlPassword, MySqlDatabase);
    }

    public MySqlSettings World()
    {
        return new MySqlSettings(WorldHost, WorldPort, WorldUser, WorldPassword, WorldDatabase);
    }

    public string PatchPath()
    {
        string data = ClientData();
        string folder = PatchInLocaleFolder ? Path.Combine(data, Locale) : data;
        return Path.Combine(folder, PatchName);
    }

    // The server's dbc folder, whether DataDir or dbc itself was chosen; empty when none is set
    public string ServerDbc()
    {
        return ResolveServerDbc(ServerDbcFolder);
    }

    // The client's Data folder, whether the WoW folder or Data itself was chosen; empty when none is set
    public string ClientData()
    {
        return ResolveClientData(ClientDataFolder);
    }

    // The client's Cache folder, beside Data
    public string ClientCache()
    {
        string data = ClientData();
        return data.Length == 0 ? string.Empty
                                : Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(data)) ?? data,
                                               CACHE_FOLDER);
    }

    public static string ResolveServerDbc(string folder)
    {
        folder = folder.Trim();
        if (folder.Length == 0)
            return string.Empty;
        string trimmed = Path.TrimEndingDirectorySeparator(folder);
        if (string.Equals(Path.GetFileName(trimmed), DBC_FOLDER, StringComparison.OrdinalIgnoreCase))
            return trimmed;
        string inside = Path.Combine(trimmed, DBC_FOLDER);
        // A DataDir has dbc, maps and vmaps folders; any other folder is taken as the dbc folder itself
        bool dataDir = Directory.Exists(inside) || DataDirFolders.Any(name => Directory.Exists(Path.Combine(trimmed,
                                                                                                           name)));
        return dataDir ? inside : trimmed;
    }

    public static string ResolveClientData(string folder)
    {
        folder = folder.Trim();
        if (folder.Length == 0)
            return string.Empty;
        string trimmed = Path.TrimEndingDirectorySeparator(folder);
        string inside = Path.Combine(trimmed, DATA_FOLDER);
        bool hasArchives = Directory.Exists(trimmed) && Directory.EnumerateFiles(trimmed, "*.mpq", Files.AnyCase).Any();
        return !hasArchives && Directory.Exists(inside) ? inside : trimmed;
    }

    public static ProjectSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(path), JsonOptions) ??
                       new ProjectSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings start again from the defaults
        }

        return new ProjectSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string password = MySqlPassword;
        if (!RememberMySqlPassword)
            MySqlPassword = string.Empty;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        finally
        {
            MySqlPassword = password;
        }
    }

    public async Task<IDbcStore> OpenStoreAsync(string? password = null, bool createDatabase = false,
                                                CancellationToken cancellation = default)
    {
        if (Store == StoreKind.MySql)
            return await MySqlDbcStore.OpenAsync(MySql(password), MySqlNaming, createDatabase, cancellation);
        return await SqliteDbcStore.OpenAsync(SqlitePath, cancellation);
    }
}
