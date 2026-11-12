using Anarres.Core.Dbc;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Storage;

namespace Anarres.Core.Export;

// Skipped: not imported, because the world database has no table for it or the file couldn't be read (Problem)
public sealed record ImportResult(string File, int Rows, bool Inferred, bool Skipped = false, string? Problem = null);

// BackedUp counts the server files copied aside before they were first replaced
public sealed record ExportResult(List<string> DbcFiles, string? ServerFolder, string? PatchPath, int BackedUp = 0);

// What Apply did, one line a step, and what it couldn't do
public sealed record ApplyResult(List<string> Done, List<string> Problems);

// Moves data between .dbc files, MPQs and the store: the import, export and deploy steps of the workflow
public static class Deployment
{
    // Every .dbc in a folder, or the ones named
    public static async Task<List<ImportResult>> ImportFolderAsync(IDbcStore store, string folder,
                                                                   DbcDefinitions definitions,
                                                                   IReadOnlyCollection<string>? only = null,
                                                                   IProgress<(string File, double Done)>? progress =
                                                                       null,
                                                                   CancellationToken cancellation = default)
    {
        List<string> files = Directory.EnumerateFiles(folder, "*.dbc", Files.AnyCase)
                                      .Where(path => only == null ||
                                                     only.Contains(Path.GetFileName(path),
                                                                   StringComparer.OrdinalIgnoreCase))
                                      .Order(StringComparer.OrdinalIgnoreCase).ToList();
        List<ImportResult> results = new List<ImportResult>();
        for (int i = 0; i < files.Count; ++i)
        {
            string file = Path.GetFileName(files[i]);
            string path = files[i];
            results.Add(await ImportTableAsync(store, () => DbcFile.Read(path, definitions), file, i, files.Count,
                                               progress, cancellation));
        }

        return results;
    }

    // DBFilesClient files straight out of the client's MPQs, the newest copy of each
    public static async Task<List<ImportResult>> ImportClientAsync(IDbcStore store, string dataFolder, string locale,
                                                                   DbcDefinitions definitions,
                                                                   IReadOnlyCollection<string>? only = null,
                                                                   IProgress<(string File, double Done)>? progress =
                                                                       null,
                                                                   CancellationToken cancellation = default)
    {
        using ClientArchives archives = ClientArchives.Open(dataFolder, locale);
        List<string> names = archives.ListDbcFiles()
                                     .Where(name => only == null ||
                                                    only.Contains(Path.GetFileName(name),
                                                                  StringComparer.OrdinalIgnoreCase)).ToList();
        List<ImportResult> results = new List<ImportResult>();
        for (int i = 0; i < names.Count; ++i)
        {
            byte[]? data = archives.TryRead(names[i]);
            if (data == null)
                continue;
            string file = Path.GetFileName(names[i]);
            results.Add(await ImportTableAsync(store, () => DbcFile.Read(data, file, definitions), file, i,
                                               names.Count, progress, cancellation));
        }

        return results;
    }

    // Writes the tables to the export folder, then to the server's dbc folder and a client patch when set
    public static async Task<ExportResult> ExportAsync(IDbcStore store, ProjectSettings settings,
                                                       IReadOnlyCollection<string> files, DbcDefinitions definitions,
                                                       bool copyToServer, bool buildPatch,
                                                       IProgress<(string File, double Done)>? progress = null,
                                                       CancellationToken cancellation = default)
    {
        string dbcFolder = Path.Combine(settings.ExportFolder, ClientArchives.DBC_FOLDER);
        Directory.CreateDirectory(dbcFolder);
        List<string> written = new List<string>();
        List<(string Name, byte[] Data)> patch = new List<(string, byte[])>();
        int index = 0;
        foreach (string file in files)
        {
            progress?.Report((file, (double)index++ / files.Count));
            DbcTable table = await store.LoadAsync(file, definitions, null, cancellation);
            // AzerothCore's world tables hold only the rows that differ, so they're laid over the clean file
            if (store.Naming == TableNaming.AzerothCore)
                table = DbcMerge.Overlay(LoadSourceOrFail(settings, file, definitions), table);
            if (settings.ExportAllLanguages)
                FillLanguages(table, settings, definitions);
            byte[] data = DbcFile.Write(table);
            string path = Path.Combine(dbcFolder, table.Definition.File);
            await WriteSafelyAsync(path, data, cancellation);
            written.Add(path);
            patch.Add((ClientArchives.DBC_FOLDER + "\\" + table.Definition.File, data));
        }

        string? server = null;
        int backedUp = 0;
        string serverDbc = settings.ServerDbc();
        if (copyToServer && serverDbc.Length > 0)
        {
            Directory.CreateDirectory(serverDbc);
            // Export and Apply alike keep the server's own files before replacing them the first time
            if (settings.ApplyBackupServer)
                backedUp = BackUpServerFiles(settings, written.Select(Path.GetFileName).OfType<string>(), serverDbc);
            foreach (string path in written)
                ReplaceSafely(path, Path.Combine(serverDbc, Path.GetFileName(path)));
            server = serverDbc;
        }

        string? patchPath = null;
        if (buildPatch && settings.ClientData().Length > 0)
        {
            patchPath = settings.PatchPath();
            MpqWriter.Write(patchPath, patch);
        }

        progress?.Report((string.Empty, 1));
        return new ExportResult(written, server, patchPath, backedUp);
    }

    // Written beside the target and then swapped in, so a crash or a full disk never leaves half a file
    private static async Task WriteSafelyAsync(string path, byte[] data, CancellationToken cancellation)
    {
        string temporary = path + TEMPORARY_EXTENSION;
        await File.WriteAllBytesAsync(temporary, data, cancellation);
        File.Move(temporary, path, overwrite: true);
    }

    private static void ReplaceSafely(string source, string target)
    {
        string temporary = target + TEMPORARY_EXTENSION;
        File.Copy(source, temporary, overwrite: true);
        File.Move(temporary, target, overwrite: true);
    }


    // Empty language columns filled from each locale folder the client has, so one file carries every language
    public static int FillLanguages(DbcTable table, ProjectSettings settings, DbcDefinitions definitions)
    {
        string data = settings.ClientData();
        if (data.Length == 0 || !table.Definition.Columns.Any(column => column.Name.EndsWith(
                "_Lang_" + Spells.SpellRecord.ENGLISH, StringComparison.OrdinalIgnoreCase)))
            return 0;
        int filled = 0;
        foreach (string locale in Spells.LocaleMerge.Order(ClientArchives.FindLocales(data)))
        {
            if (Spells.LocaleMerge.Read(data, locale, table.Definition.File, settings.PatchPath(), definitions) is
                { } source)
                filled += Spells.LocaleMerge.Apply(table, source, onlyEmpty: true);
        }

        return filled;
    }

    // Tables edited since import plus unsaved ones; untracked older imports are compared with clean files once
    public static async Task<List<string>> EditedFilesAsync(IDbcStore store, ProjectSettings settings,
                                                            DbcDefinitions definitions,
                                                            IEnumerable<string>? unsaved = null,
                                                            IProgress<(string File, double Done)>? progress = null,
                                                            CancellationToken cancellation = default)
    {
        List<EditedTable> edited = await store.EditedTablesAsync(cancellation);
        List<string> unknown = edited.Where(table => !table.Known).Select(table => table.File).ToList();
        List<string> files = edited.Where(table => table.Known).Select(table => table.File).ToList();
        if (unknown.Count > 0)
            files.AddRange(await CheckEditedAsync(store, settings, unknown, definitions, progress, cancellation));
        files.AddRange(unsaved ?? Enumerable.Empty<string>());
        return files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Compares tables with their clean files and records which differ; returns those that do
    public static async Task<List<string>> CheckEditedAsync(IDbcStore store, ProjectSettings settings,
                                                            IReadOnlyCollection<string> files,
                                                            DbcDefinitions definitions,
                                                            IProgress<(string File, double Done)>? progress = null,
                                                            CancellationToken cancellation = default)
    {
        List<string> edited = new List<string>();
        int index = 0;
        foreach (string file in files)
        {
            progress?.Report((file, (double)index++ / files.Count));
            DbcTable table = await store.LoadAsync(file, definitions, null, cancellation);
            DbcTable? clean = DbcMerge.LoadSource(settings, file, definitions);
            bool differs = clean == null || !DbcMerge.Same(clean, table);
            await store.MarkEditedAsync(file, differs, cancellation);
            if (differs)
                edited.Add(file);
        }

        progress?.Report((string.Empty, 1));
        return edited;
    }

    private static DbcTable LoadSourceOrFail(ProjectSettings settings, string file, DbcDefinitions definitions)
    {
        string table = definitions.Find(file)?.Table ?? file;
        return DbcMerge.LoadSource(settings, file, definitions) ??
               throw new InvalidOperationException(
                   "AzerothCore's " + table + " table holds only the rows that differ from the client's file, so " +
                   "exporting needs the clean file to lay them over. Set the Clean DBC folder or the WoW Data " +
                   "folder in Project settings.");
    }

    public const string BACKUP_FOLDER = "server-backup";
    // Files are written under this name beside their target, then swapped in
    private const string TEMPORARY_EXTENSION = ".anarres-new";

    // The one-click deploy: tables written, server copies backed up and replaced, patch built, and the chosen extras
    public static async Task<ApplyResult> ApplyAsync(IDbcStore store, ProjectSettings settings,
                                                     IReadOnlyCollection<string> files, DbcDefinitions definitions,
                                                     IProgress<(string File, double Done)>? progress = null,
                                                     CancellationToken cancellation = default)
    {
        List<string> done = new List<string>();
        List<string> problems = new List<string>();
        string serverDbc = settings.ServerDbc();
        bool toServer = settings.ApplyToServer && serverDbc.Length > 0;
        bool patch = settings.ApplyPatch && settings.ClientData().Length > 0;
        if (settings.ApplyToServer && serverDbc.Length == 0)
            problems.Add("No server data folder is set, so the server's files weren't replaced.");
        if (settings.ApplyPatch && settings.ClientData().Length == 0)
            problems.Add("No client folder is set, so no patch was built.");

        ExportResult result = await ExportAsync(store, settings, files, definitions, toServer, patch, progress,
                                                cancellation);
        if (result.BackedUp > 0)
            done.Add("Kept the server's own copy of " + result.BackedUp + (result.BackedUp == 1 ? " file" : " files") +
                     " in " + Path.Combine(settings.ExportFolder, BACKUP_FOLDER) + ".");
        done.Add("Wrote " + result.DbcFiles.Count + " .dbc " + (result.DbcFiles.Count == 1 ? "file" : "files") +
                 " to " + Path.Combine(settings.ExportFolder, ClientArchives.DBC_FOLDER) + ".");
        if (result.ServerFolder != null)
            done.Add("Copied them to the server's " + result.ServerFolder + ". Restart the worldserver.");
        if (result.PatchPath != null)
            done.Add("Built " + result.PatchPath + ". Restart the client.");

        if (settings.ApplyWorldDatabase)
            await ApplyWorldAsync(store, settings, files, definitions, done, problems, cancellation);

        if (settings.ApplyClearClientCache)
        {
            string cache = settings.ClientCache();
            if (cache.Length > 0 && Directory.Exists(cache))
            {
                try
                {
                    Directory.Delete(cache, recursive: true);
                    done.Add("Emptied the client's cache (" + cache + ").");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    problems.Add("The client's cache couldn't be emptied; is WoW still open? " + exception.Message);
                }
            }
        }

        return new ApplyResult(done, problems);
    }

    // Copies the server's file aside the first time it would be replaced; later runs keep that first copy
    private static int BackUpServerFiles(ProjectSettings settings, IEnumerable<string> files, string serverDbc)
    {
        string backup = Path.Combine(settings.ExportFolder, BACKUP_FOLDER);
        int kept = 0;
        foreach (string file in files)
        {
            string name = file.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase) ? file : file + ".dbc";
            string source = Path.Combine(serverDbc, name);
            string target = Path.Combine(backup, name);
            if (!File.Exists(source) || File.Exists(target))
                continue;
            Directory.CreateDirectory(backup);
            File.Copy(source, target);
            ++kept;
        }

        return kept;
    }

    // Rows that differ from the clean files go into acore_world's matching _dbc tables
    private static async Task ApplyWorldAsync(IDbcStore store, ProjectSettings settings, IEnumerable<string> files,
                                              DbcDefinitions definitions, List<string> done, List<string> problems,
                                              CancellationToken cancellation)
    {
        try
        {
            using MySqlDbcStore world = await MySqlDbcStore.OpenAsync(settings.World(), TableNaming.AzerothCore,
                                                                      cancellation: cancellation);
            foreach (string file in files)
            {
                DbcTable table = await store.LoadAsync(file, definitions, null, cancellation);
                if (table.Definition.Table.Length == 0)
                    continue;
                DbcTable? clean = DbcMerge.LoadSource(settings, file, definitions);
                if (clean == null)
                {
                    problems.Add("There's no clean " + table.Definition.File + " to compare with, so its rows " +
                                 "weren't put in " + table.Definition.Table + ".");
                    continue;
                }

                List<int> rows = DbcMerge.Changed(clean, table);
                if (rows.Count == 0)
                    continue;
                await world.UpsertRowsAsync(table, rows, cancellation);
                done.Add("Put " + rows.Count + " changed " + (rows.Count == 1 ? "row" : "rows") + " in " +
                         settings.WorldDatabase + "." + table.Definition.Table + ".");
            }
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or InvalidOperationException)
        {
            problems.Add("The world database couldn't be updated: " + exception.Message);
        }
    }

    // A patch MPQ from .dbc files already on disk, such as the export folder's
    public static void BuildPatch(string patchPath, IEnumerable<string> dbcFiles)
    {
        List<(string Name, byte[] Data)> files = dbcFiles
            .Select(path => (ClientArchives.DBC_FOLDER + "\\" + Path.GetFileName(path), File.ReadAllBytes(path)))
            .ToList();
        MpqWriter.Write(patchPath, files);
    }

    // A file that can't be read, such as the empty ones the client ships, is skipped and the import goes on
    private static async Task<ImportResult> ImportTableAsync(IDbcStore store, Func<DbcTable> read, string file,
                                                             int index, int count,
                                                             IProgress<(string File, double Done)>? progress,
                                                             CancellationToken cancellation)
    {
        progress?.Report((file, (double)index / count));
        DbcTable table;
        try
        {
            table = read();
        }
        catch (InvalidDataException exception)
        {
            return new ImportResult(file, 0, Inferred: false, Skipped: true, Problem: exception.Message);
        }

        if (store.Naming == TableNaming.AzerothCore && table.Definition.Table.Length == 0)
            return new ImportResult(file, 0, table.Definition.IsInferred, Skipped: true);
        IProgress<double> part = new Relay(done => progress?.Report((file, (index + done) / count)));
        await store.ImportAsync(table, part, cancellation);
        return new ImportResult(file, table.Rows.Count, table.Definition.IsInferred);
    }
}

// Passes progress on straight away on the same thread; the caller's own Progress moves it to the interface
internal sealed class Relay : IProgress<double>
{
    private readonly Action<double> _report;

    public Relay(Action<double> report)
    {
        _report = report;
    }

    public void Report(double value)
    {
        _report(value);
    }
}
