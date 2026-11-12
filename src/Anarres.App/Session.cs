using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Models;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Storage;

namespace Anarres.App;

// What the window works on: the settings, the open store, and the tables loaded from it
public sealed class Session : IDisposable
{
    private readonly Dictionary<string, DbcTable> _tables =
        new Dictionary<string, DbcTable>(StringComparer.OrdinalIgnoreCase);
    private IDbcStore? _store;
    private DbcCache? _cache;
    private Task<IconLibrary>? _icons;

    public Session(ProjectSettings settings)
    {
        Settings = settings;
        Definitions = DbcDefinitions.Default.WithFolder(settings.DefinitionsFolder);
    }

    public ProjectSettings Settings { get; private set; }

    public DbcDefinitions Definitions { get; private set; }

    // Typed in when the password isn't remembered; kept only while Anarres runs
    public string? MySqlPassword { get; set; }

    public bool IsOpen => _store != null;

    // Where the store keeps big tables between runs; null reads them from the database every time
    public DbcCache? Cache
    {
        get => _cache;
        set
        {
            _cache = value;
            if (_store != null)
                _store.Cache = value;
        }
    }

    public string Description => _store?.Description ?? "No project open";

    public event Action? Changed;

    public IEnumerable<DbcTable> LoadedTables => _tables.Values;

    public IEnumerable<DbcTable> ModifiedTables => _tables.Values.Where(table => table.IsModified);

    public async Task<IDbcStore> StoreAsync(bool createDatabase = false)
    {
        if (_store != null)
            return _store;
        string? password = Settings.Store == StoreKind.MySql && !Settings.RememberMySqlPassword ? MySqlPassword : null;
        _store = await Settings.OpenStoreAsync(password, createDatabase);
        _store.Cache = _cache;
        Changed?.Invoke();
        return _store;
    }

    // Switches to new settings: the store is closed and tables are loaded again from the new one
    public void Use(ProjectSettings settings)
    {
        Close();
        Settings = settings;
        _naming.Clear();
        _worldLoad = null;
        lock (_world)
            _world.Clear();
        Definitions = DbcDefinitions.Default.WithFolder(settings.DefinitionsFolder);
        Changed?.Invoke();
    }

    // Picks up layouts saved to the definitions folder; loaded tables and their changes stay as they are
    public void ReloadDefinitions()
    {
        Definitions = DbcDefinitions.Default.WithFolder(Settings.DefinitionsFolder);
    }

    // The client's icons, opened once in the background; empty when no WoW Data folder is set
    public Task<IconLibrary> IconLibraryAsync()
    {
        ProjectSettings settings = Settings;
        _icons ??= Task.Run(() =>
        {
            try
            {
                return new IconLibrary(settings.ClientData(), settings.Locale);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                                  UnauthorizedAccessException)
            {
                return new IconLibrary(string.Empty, settings.Locale);
            }
        });
        return _icons;
    }

    public void Close()
    {
        if (_icons is { IsCompletedSuccessfully: true })
            _icons.Result.Dispose();
        _icons = null;
        _store?.Dispose();
        _store = null;
        _tables.Clear();
    }

    public DbcTable? Loaded(string file)
    {
        return _tables.TryGetValue(Normalise(file), out DbcTable? table) ? table : null;
    }

    public async Task<DbcTable> LoadAsync(string file, IProgress<double>? progress = null)
    {
        if (Loaded(file) is { } loaded)
            return loaded;
        IDbcStore store = await StoreAsync();
        DbcTable table = await Task.Run(() => store.LoadAsync(file, Definitions, progress));
        _tables[Normalise(table.Definition.File)] = table;
        return table;
    }

    // Tables that name values (races, classes, item classes...), from the project or else the clean files
    public static readonly string[] NamingTables =
    {
        "ChrRaces.dbc", "ChrClasses.dbc", "ItemClass.dbc", "ItemSubClass.dbc", "TotemCategory.dbc",
        "SkillLine.dbc"
    };

    private readonly Dictionary<string, DbcTable?> _naming =
        new Dictionary<string, DbcTable?>(StringComparer.OrdinalIgnoreCase);

    // Loads the naming tables once; a missing one stays null and its values show as plain numbers
    public async Task LoadNamingTablesAsync()
    {
        foreach (string file in NamingTables)
        {
            if (_naming.ContainsKey(file))
                continue;
            DbcTable? table = null;
            try
            {
                table = await TryLoadAsync(file) ??
                        await Task.Run(() => DbcMerge.LoadSource(Settings, file, Definitions));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                                  InvalidOperationException or System.Data.Common.DbException)
            {
                table = null;
            }

            _naming[file] = table;
        }
    }

    private readonly Dictionary<string, Dictionary<uint, string>> _world =
        new Dictionary<string, Dictionary<uint, string>>(StringComparer.Ordinal);
    private Task? _worldLoad;

    // Item, creature and game object names from the world database, tried once
    public Task LoadWorldNamesAsync()
    {
        return _worldLoad ??= Task.Run(async () =>
        {
            MySqlSettings world = Settings.World();
            foreach (string table in new[] { WorldNames.ITEMS, WorldNames.CREATURES, WorldNames.GAME_OBJECTS })
            {
                try
                {
                    Dictionary<uint, string> names = await WorldNames.LoadAsync(world, table);
                    lock (_world)
                        _world[table] = names;
                }
                catch (Exception exception) when (exception is System.Data.Common.DbException or
                                                      InvalidOperationException or TimeoutException)
                {
                    // No world database here; IDs show without names
                }
            }

            try
            {
                Dictionary<uint, (byte, uint)> items = await WorldNames.ItemDetailsAsync(world);
                lock (_world)
                    _items = items;
            }
            catch (Exception exception) when (exception is System.Data.Common.DbException or
                                                  InvalidOperationException or TimeoutException or
                                                  InvalidCastException or OverflowException)
            {
                // Names show without quality colours or icons
            }
        });
    }

    private Dictionary<uint, (byte Quality, uint Display)> _items = new Dictionary<uint, (byte, uint)>();

    // An item's quality and display from item_template, once the world names have loaded
    public (byte Quality, uint Display)? ItemDetails(uint entry)
    {
        lock (_world)
            return _items.TryGetValue(entry, out (byte, uint) details) ? details : null;
    }

    // The name of an entry of item_template, creature_template or gameobject_template, or null
    public string? WorldName(string table, uint entry)
    {
        lock (_world)
            return _world.TryGetValue(table, out Dictionary<uint, string>? names) &&
                   names.TryGetValue(entry, out string? name)
                       ? name
                       : null;
    }

    // The tables that lead from spells, creatures, objects and items to their models
    public static readonly string[] ModelTables =
    {
        ModelSources.SPELL_VISUAL, ModelSources.VISUAL_KIT, ModelSources.EFFECT_NAME, ModelSources.CREATURE_DISPLAY,
        ModelSources.CREATURE_MODEL, ModelSources.OBJECT_DISPLAY, ModelSources.ITEM_DISPLAY, ModelSources.SHAPESHIFT,
        AnimationNames.FILE
    };

    private readonly Dictionary<string, DbcTable?> _modelTables =
        new Dictionary<string, DbcTable?>(StringComparer.OrdinalIgnoreCase);

    // Finds models from the project's tables (with unsaved edits) or else the clean files and the client
    public async Task<ModelSources> ModelSourcesAsync()
    {
        IconLibrary client = await IconLibraryAsync();
        foreach (string file in ModelTables)
        {
            if (_modelTables.ContainsKey(file) || Loaded(file) != null)
                continue;
            DbcTable? table;
            try
            {
                table = await TryLoadAsync(file) ??
                        await Task.Run(() => DbcMerge.LoadSource(Settings, file, Definitions));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                                  InvalidOperationException or System.Data.Common.DbException)
            {
                table = null;
            }

            _modelTables[file] = table;
        }

        AnimationNames.Use(Loaded(AnimationNames.FILE) ?? _modelTables.GetValueOrDefault(AnimationNames.FILE));
        return new ModelSources(file => Loaded(file) ?? _modelTables.GetValueOrDefault(file), client.Contains);
    }

    // The display a creature, game object or item entry uses in the world database, or null without one
    public async Task<(uint Display, uint Slot)?> WorldDisplayAsync(string table, uint entry)
    {
        try
        {
            return await WorldNames.DisplayAsync(Settings.World(), table, entry);
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or
                                              InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    // A naming table already loaded, or null; for the value names drawn in grids and fields
    public DbcTable? NamingTable(string file)
    {
        if (Loaded(file) is { } open)
            return open;
        return _naming.TryGetValue(file, out DbcTable? table) ? table : null;
    }

    // A table if the store has it, or null; for lookups such as SpellDuration.dbc that may not be imported
    public async Task<DbcTable?> TryLoadAsync(string file)
    {
        if (Loaded(file) is { } loaded)
            return loaded;
        IDbcStore store = await StoreAsync();
        return await store.ContainsAsync(file) ? await LoadAsync(file) : null;
    }

    public async Task SaveAsync(DbcTable table)
    {
        IDbcStore store = await StoreAsync();
        await Task.Run(() => store.SaveChangesAsync(table));
        Changed?.Invoke();
    }

    public async Task SaveAllAsync()
    {
        foreach (DbcTable table in ModifiedTables.ToList())
            await SaveAsync(table);
    }

    // Drops a table's unsaved changes by forgetting it; the next load reads the saved copy
    public void Forget(string file)
    {
        _tables.Remove(Normalise(file));
    }

    // After an import the store has new data, so loaded copies are stale
    public void ForgetAll()
    {
        _tables.Clear();
        Changed?.Invoke();
    }

    public void NotifyChanged()
    {
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Close();
    }

    private static string Normalise(string file)
    {
        string name = Path.GetFileName(file);
        return name.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase) ? name : name + ".dbc";
    }
}
