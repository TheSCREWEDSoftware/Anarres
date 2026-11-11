using Anarres.Core.Dbc;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Anarres.Core.Storage;
using Avalonia.Threading;

namespace Anarres.App.Tests;

public static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(what + ": expected '" + expected + "', got '" + actual + "'");
    }
}

// Runs the interface's queued work until a task finishes, as the app's own loop would
public static class Pump
{
    private const int TIMEOUT_SECONDS = 60;

    public static void Until(Task task)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(TIMEOUT_SECONDS);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow > end)
                throw new TimeoutException("The interface didn't finish in time.");
            Thread.Sleep(1);
        }

        Dispatcher.UIThread.RunJobs();
    }

    // Lets background work and queued interface work run until the condition holds
    public static async Task WaitForAsync(Func<bool> condition, string what)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(TIMEOUT_SECONDS);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
                throw new TimeoutException("Timed out waiting for " + what + ".");
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}

// A project with three spells in a temporary SQLite file
public sealed class TestProject : IDisposable
{
    private TestProject(string folder)
    {
        Folder = folder;
        Settings = new ProjectSettings
        {
            SqlitePath = Path.Combine(folder, "test.anarres"),
            ExportFolder = Path.Combine(folder, "Export"),
            DefinitionsFolder = Path.Combine(folder, "definitions")
        };
    }

    public string Folder { get; }

    public ProjectSettings Settings { get; }

    public static async Task<TestProject> CreateAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "anarres-ui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        TestProject project = new TestProject(folder);
        DbcTable spells = new DbcTable(DbcDefinitions.Default.Find(SpellRecord.FILE)!);
        int name = spells.Definition.IndexOf("Name_Lang_enUS");
        foreach ((uint id, string text) in new[] { (133u, "Fireball"), (116u, "Frostbolt"), (10u, "Blizzard") })
            spells.SetString(spells.AddRow(id), name, text);
        using SqliteDbcStore store = await SqliteDbcStore.OpenAsync(project.Settings.SqlitePath);
        await store.ImportAsync(spells);
        return project;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // The project file may still be open; the temp folder is cleaned up later
        }
    }
}
