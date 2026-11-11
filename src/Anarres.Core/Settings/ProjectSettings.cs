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

    // WoW's Data folder, to read DBCs from its MPQs and to put the patch in
    public string ClientDataFolder { get; set; } = string.Empty;

    public string Locale { get; set; } = DEFAULT_LOCALE;

    // Exported .dbc files are written here, under DBFilesClient
    public string ExportFolder { get; set; } = Path.Combine(DefaultFolder, "Export");

    // The worldserver's dbc folder (DataDir/dbc); exported files are copied there when set
    public string ServerDbcFolder { get; set; } = string.Empty;

    public string PatchName { get; set; } = DEFAULT_PATCH_NAME;

    // Where to keep the patch: in Data itself, or in Data's locale folder
    public bool PatchInLocaleFolder { get; set; }

    // Extra definition .json files that replace or add to the built-in ones
    public string DefinitionsFolder { get; set; } = Path.Combine(DefaultFolder, "definitions");

    // Tables the last export used, ticked again next time
    public List<string> ExportTables { get; set; } = new List<string> { "Spell.dbc" };

    public string Theme { get; set; } = "Dark";

    public MySqlSettings MySql(string? password = null)
    {
        return new MySqlSettings(MySqlHost, MySqlPort, MySqlUser, password ?? MySqlPassword, MySqlDatabase);
    }

    public string PatchPath()
    {
        string folder = PatchInLocaleFolder ? Path.Combine(ClientDataFolder, Locale) : ClientDataFolder;
        return Path.Combine(folder, PatchName);
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
