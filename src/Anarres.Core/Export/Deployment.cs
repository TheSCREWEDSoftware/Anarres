using Anarres.Core.Dbc;
using Anarres.Core.Mpq;
using Anarres.Core.Settings;
using Anarres.Core.Storage;

namespace Anarres.Core.Export;

// Skipped: AzerothCore's world database has no table for the file, so it wasn't imported there
public sealed record ImportResult(string File, int Rows, bool Inferred, bool Skipped = false);

public sealed record ExportResult(List<string> DbcFiles, string? ServerFolder, string? PatchPath);

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
            DbcTable table = DbcFile.Read(files[i], definitions);
            results.Add(await ImportTableAsync(store, table, file, i, files.Count, progress, cancellation));
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
            DbcTable table = DbcFile.Read(data, file, definitions);
            results.Add(await ImportTableAsync(store, table, file, i, names.Count, progress, cancellation));
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
            byte[] data = DbcFile.Write(table);
            string path = Path.Combine(dbcFolder, table.Definition.File);
            await File.WriteAllBytesAsync(path, data, cancellation);
            written.Add(path);
            patch.Add((ClientArchives.DBC_FOLDER + "\\" + table.Definition.File, data));
        }

        string? server = null;
        if (copyToServer && settings.ServerDbcFolder.Length > 0)
        {
            Directory.CreateDirectory(settings.ServerDbcFolder);
            foreach (string path in written)
                File.Copy(path, Path.Combine(settings.ServerDbcFolder, Path.GetFileName(path)), overwrite: true);
            server = settings.ServerDbcFolder;
        }

        string? patchPath = null;
        if (buildPatch && settings.ClientDataFolder.Length > 0)
        {
            patchPath = settings.PatchPath();
            MpqWriter.Write(patchPath, patch);
        }

        progress?.Report((string.Empty, 1));
        return new ExportResult(written, server, patchPath);
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

    // A patch MPQ from .dbc files already on disk, such as the export folder's
    public static void BuildPatch(string patchPath, IEnumerable<string> dbcFiles)
    {
        List<(string Name, byte[] Data)> files = dbcFiles
            .Select(path => (ClientArchives.DBC_FOLDER + "\\" + Path.GetFileName(path), File.ReadAllBytes(path)))
            .ToList();
        MpqWriter.Write(patchPath, files);
    }

    private static async Task<ImportResult> ImportTableAsync(IDbcStore store, DbcTable table, string file, int index,
                                                             int count, IProgress<(string File, double Done)>? progress,
                                                             CancellationToken cancellation)
    {
        if (store.Naming == TableNaming.AzerothCore && table.Definition.Table.Length == 0)
            return new ImportResult(file, 0, table.Definition.IsInferred, Skipped: true);
        IProgress<double> part = new Relay(done => progress?.Report((file, (index + done) / count)));
        progress?.Report((file, (double)index / count));
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
