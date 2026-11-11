using Anarres.Core.Dbc;
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
        Changed?.Invoke();
        return _store;
    }

    // Switches to new settings: the store is closed and tables are loaded again from the new one
    public void Use(ProjectSettings settings)
    {
        Close();
        Settings = settings;
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
