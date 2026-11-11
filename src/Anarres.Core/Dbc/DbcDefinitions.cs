using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anarres.Core.Dbc;

// The known layouts, by file name: the built-in ones from AzerothCore, then any the user saved over them
public sealed class DbcDefinitions
{
    // AzerothCore's layouts first; the client-only files fill in what AzerothCore has no table for
    public static readonly string[] BuiltInResources =
    {
        "Anarres.Core.Definitions.wotlk-3.3.5a.json", "Anarres.Core.Definitions.client-3.3.5a.json"
    };

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Lazy<DbcDefinitions> BuiltIn = new Lazy<DbcDefinitions>(LoadBuiltIn);

    private readonly Dictionary<string, DbcDefinition> _byFile =
        new Dictionary<string, DbcDefinition>(StringComparer.OrdinalIgnoreCase);

    public static DbcDefinitions Default => BuiltIn.Value;

    public string Build { get; private set; } = string.Empty;

    public IEnumerable<DbcDefinition> All => _byFile.Values.OrderBy(entry => entry.File, StringComparer.Ordinal);

    // Spell.dbc, Spell or spell_dbc
    public DbcDefinition? Find(string name)
    {
        string file = Path.GetFileName(name);
        if (!file.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase))
            file += ".dbc";
        if (_byFile.TryGetValue(file, out DbcDefinition? found))
            return found;
        return _byFile.Values.FirstOrDefault(entry => entry.Table.Length > 0 &&
                                                      string.Equals(entry.Table, name,
                                                                    StringComparison.OrdinalIgnoreCase));
    }

    public void Add(DbcDefinition definition)
    {
        _byFile[definition.File] = definition;
    }

    // A copy with the definitions in a folder of .json files laid over these
    public DbcDefinitions WithFolder(string folder)
    {
        DbcDefinitions combined = new DbcDefinitions { Build = Build };
        foreach (DbcDefinition definition in All)
            combined.Add(definition);
        if (!Directory.Exists(folder))
            return combined;
        foreach (string file in Directory.EnumerateFiles(folder, "*.json", Files.AnyCase))
        {
            try
            {
                DbcDefinition? definition = JsonSerializer.Deserialize<DbcDefinition>(File.ReadAllText(file),
                                                                                       JsonOptions);
                if (definition is { Columns.Count: > 0 } && definition.File.Length > 0)
                    combined.Add(definition);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                // A broken file is skipped; the built-in definition stays
            }
        }

        return combined;
    }

    public static string ToJson(DbcDefinition definition)
    {
        return JsonSerializer.Serialize(definition, JsonOptions);
    }

    public static DbcDefinition? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DbcDefinition>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static DbcDefinitions Parse(string json)
    {
        DefinitionFile? file = JsonSerializer.Deserialize<DefinitionFile>(json, JsonOptions);
        DbcDefinitions definitions = new DbcDefinitions { Build = file?.Build ?? string.Empty };
        foreach (DbcDefinition definition in file?.Definitions ?? new List<DbcDefinition>())
            definitions.Add(definition);
        return definitions;
    }

    private static DbcDefinitions LoadBuiltIn()
    {
        DbcDefinitions all = new DbcDefinitions();
        foreach (string resource in BuiltInResources)
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream == null)
                continue;
            using StreamReader reader = new StreamReader(stream);
            DbcDefinitions part = Parse(reader.ReadToEnd());
            all.Build = all.Build.Length > 0 ? all.Build : part.Build;
            foreach (DbcDefinition definition in part.All.Where(entry => all.Find(entry.File) == null))
                all.Add(definition);
        }

        return all;
    }

    private sealed class DefinitionFile
    {
        public string Build { get; set; } = string.Empty;

        public List<DbcDefinition> Definitions { get; set; } = new List<DbcDefinition>();
    }
}
