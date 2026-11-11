namespace Anarres.Core.Tests;

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
            throw new Exception($"{what}: expected '{Visible(expected)}', got '{Visible(actual)}'");
    }

    public static void Throws<TException>(Action action, string what) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception(what + ": expected " + typeof(TException).Name);
    }

    // Line endings and tabs written out, so a difference in them shows and the message stays on one line
    public static string Visible(object? value)
    {
        return (value?.ToString() ?? "null").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }
}

// A throwaway folder in the temp folder, deleted on dispose
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "anarres-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name)
    {
        return System.IO.Path.Combine(Path, name);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still open on Windows; the temp folder is cleaned up later
        }
    }
}

// Real client files for the tests that use them: ANARRES_TEST_DBC is a folder of 3.3.5a .dbc files
public static class TestData
{
    public const string DBC_VARIABLE = "ANARRES_TEST_DBC";

    public static string? DbcFolder()
    {
        string? folder = Environment.GetEnvironmentVariable(DBC_VARIABLE);
        return folder != null && Directory.Exists(folder) ? folder : null;
    }
}
