using System.Globalization;
using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Storage;

// Anarres without a window, for scripts and build servers: import, export, patch and SQL from the command line
const string HELP = """
Usage: anarres-cli <command> [options]

Commands:
  list                         Tables in the store and their row counts
  import <folder>              Import every .dbc in a folder (or --only some)
  import-client <Data folder>  Import DBFilesClient files from the client's MPQs (--locale enUS)
  export                       Write .dbc files to the export folder (--only some, or every table)
                               --server copies them to the server's dbc folder, --patch builds the client MPQ
  patch <out.MPQ> <files...>   Pack .dbc files into a patch MPQ under DBFilesClient
  sql <file> --ids 1,2,...     Write AzerothCore SQL for those rows (--out file.sql, --table name)
  sql <file> --changed         ... or for every row that differs from the clean file (the Clean DBC folder)

Paths (otherwise from the settings): --export, --server-dbc, --client-data, --patch-name, --source

Store options (otherwise the app's settings are used):
  --settings <settings.json>
  --sqlite <project.anarres>
  --mysql <host[:port]/database> --user <name> --password <password> [--naming dbc|ac]
""";

Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
List<string> positional = new List<string>();
HashSet<string> switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "--server", "--patch", "--changed"
};
for (int i = 0; i < args.Length; ++i)
{
    if (args[i].StartsWith("--", StringComparison.Ordinal))
    {
        if (switches.Contains(args[i]) || i + 1 >= args.Length)
            options[args[i]] = "true";
        else
            options[args[i]] = args[++i];
    }
    else
        positional.Add(args[i]);
}

if (positional.Count == 0 || positional[0] is "help" or "-h")
{
    Console.WriteLine(HELP);
    return positional.Count == 0 ? 2 : 0;
}

ProjectSettings settings = ProjectSettings.Load(options.GetValueOrDefault("--settings"));
ApplyStoreOptions(settings, options);
DbcDefinitions definitions = DbcDefinitions.Default.WithFolder(settings.DefinitionsFolder);
List<string>? only = options.TryGetValue("--only", out string? list)
                         ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .Select(name => name.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase)
                                                   ? name
                                                   : name + ".dbc").ToList()
                         : null;
ConsoleProgress progress = new ConsoleProgress();

try
{
    switch (positional[0])
    {
        case "patch":
        {
            if (positional.Count < 3)
                return Fail("Give the MPQ to write and at least one .dbc file.");
            Deployment.BuildPatch(positional[1], positional.Skip(2));
            Console.WriteLine("Wrote " + positional[1] + ".");
            return 0;
        }
        case "list":
        {
            using IDbcStore store = await settings.OpenStoreAsync();
            Console.WriteLine(store.Description);
            foreach (StoredTable table in await store.ListTablesAsync())
                Console.WriteLine("  " + table.File.PadRight(40) + table.Rows.ToString(CultureInfo.InvariantCulture)
                                                                                   .PadLeft(8) + " rows");
            return 0;
        }
        case "import":
        case "import-client":
        {
            if (positional.Count < 2)
                return Fail("Give the folder to import from.");
            using IDbcStore store = await settings.OpenStoreAsync(createDatabase: true);
            List<ImportResult> results = positional[0] == "import"
                                             ? await Deployment.ImportFolderAsync(store, positional[1], definitions,
                                                                                  only, progress)
                                             : await Deployment.ImportClientAsync(store, positional[1],
                                                                                  options.GetValueOrDefault("--locale",
                                                                                      settings.Locale),
                                                                                  definitions, only, progress);
            Console.WriteLine();
            foreach (ImportResult result in results)
            {
                string rows = result.Skipped ? "  skipped: AzerothCore has no table for it"
                                             : result.Rows.ToString(CultureInfo.InvariantCulture).PadLeft(8) + " rows";
                Console.WriteLine("  " + result.File.PadRight(40) + rows +
                                  (result.Inferred && !result.Skipped ? "  (layout guessed)" : string.Empty));
            }

            Console.WriteLine("Imported " + results.Count(result => !result.Skipped) + " files into " +
                              store.Description + ".");
            return 0;
        }
        case "export":
        {
            using IDbcStore store = await settings.OpenStoreAsync();
            List<string> files = only ?? (await store.ListTablesAsync()).Select(table => table.File).ToList();
            ExportResult result = await Deployment.ExportAsync(store, settings, files, definitions,
                                                               options.ContainsKey("--server"),
                                                               options.ContainsKey("--patch"), progress);
            Console.WriteLine();
            Console.WriteLine("Wrote " + result.DbcFiles.Count + " files to " +
                              Path.Combine(settings.ExportFolder, ClientArchives.DBC_FOLDER) + ".");
            if (result.ServerFolder != null)
                Console.WriteLine("Copied them to " + result.ServerFolder + ". Restart the worldserver to load them.");
            if (result.PatchPath != null)
                Console.WriteLine("Built " + result.PatchPath + ".");
            return 0;
        }
        case "sql":
        {
            bool changed = options.ContainsKey("--changed");
            if (positional.Count < 2 || (!options.TryGetValue("--ids", out string? ids) && !changed))
                return Fail("Give the table and --ids or --changed.");
            using IDbcStore store = await settings.OpenStoreAsync();
            DbcTable table = await store.LoadAsync(positional[1], definitions);
            List<int> rows = new List<int>();
            if (changed)
            {
                DbcTable? clean = DbcMerge.LoadSource(settings, table.Definition.File, definitions);
                if (clean == null)
                    return Fail("There's no clean " + table.Definition.File + "; set --source or the Clean DBC " +
                                "folder in the settings.");
                rows = DbcMerge.Changed(clean, table);
            }

            StringSplitOptions split = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
            foreach (string id in (ids ?? string.Empty).Split(',', split))
            {
                int row = uint.TryParse(id, CultureInfo.InvariantCulture, out uint key) ? table.FindRow(key) : -1;
                if (row < 0)
                    return Fail(table.Definition.File + " has no row " + id + ".");
                rows.Add(row);
            }

            string sql = SqlExport.Build(table, rows, options.GetValueOrDefault("--table"));
            if (options.TryGetValue("--out", out string? output))
            {
                File.WriteAllText(output, sql);
                Console.WriteLine("Wrote " + output + ".");
            }
            else
                Console.Write(sql);
            return 0;
        }
        default:
            return Fail("Unknown command " + positional[0] + ". Run anarres-cli help for the list.");
    }
}
catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or
                                      NotSupportedException or System.Data.Common.DbException or
                                      UnauthorizedAccessException)
{
    Console.WriteLine();
    return Fail(exception.Message);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void ApplyStoreOptions(ProjectSettings settings, Dictionary<string, string> options)
{
    if (options.TryGetValue("--sqlite", out string? sqlite))
    {
        settings.Store = StoreKind.Sqlite;
        settings.SqlitePath = sqlite;
    }

    if (options.TryGetValue("--mysql", out string? mysql))
    {
        settings.Store = StoreKind.MySql;
        string[] parts = mysql.Split('/', 2);
        string[] host = parts[0].Split(':', 2);
        settings.MySqlHost = host[0];
        if (host.Length > 1 && int.TryParse(host[1], CultureInfo.InvariantCulture, out int port))
            settings.MySqlPort = port;
        if (parts.Length > 1)
            settings.MySqlDatabase = parts[1];
    }

    if (options.TryGetValue("--user", out string? user))
        settings.MySqlUser = user;
    if (options.TryGetValue("--password", out string? password))
        settings.MySqlPassword = password;
    if (options.TryGetValue("--naming", out string? naming))
        settings.MySqlNaming = naming.Equals("ac", StringComparison.OrdinalIgnoreCase) ? TableNaming.AzerothCore
                                                                                       : TableNaming.DbcName;
    foreach ((string key, string value) in options)
    {
        switch (key.ToLowerInvariant())
        {
            case "--export":
                settings.ExportFolder = value;
                break;
            case "--server-dbc":
                settings.ServerDbcFolder = value;
                break;
            case "--client-data":
                settings.ClientDataFolder = value;
                break;
            case "--patch-name":
                settings.PatchName = value;
                break;
            case "--source":
                settings.SourceDbcFolder = value;
                break;
            default:
                break;
        }
    }
}

// Reports on the calling thread, so the last line is written before the summary
internal sealed class ConsoleProgress : IProgress<(string File, double Done)>
{
    private string _last = string.Empty;

    public void Report((string File, double Done) update)
    {
        if (update.File.Length == 0)
            return;
        string line = (update.Done * 100).ToString("0", CultureInfo.InvariantCulture).PadLeft(3) + "%  " +
                      update.File.PadRight(40);
        if (line == _last)
            return;
        _last = line;
        Console.Write("\r" + line);
    }
}
