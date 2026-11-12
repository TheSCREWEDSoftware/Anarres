using System.ComponentModel;
using System.Diagnostics;
using Anarres.Core.Settings;

namespace Anarres.App;

// Opens files in a text editor and the project's database in a SQL tool, chosen in Options > Integrations
public static class ExternalTools
{
    // Editors as Urras knows them: name, program on Windows and elsewhere, and whether it's a .cmd script on Windows
    private static readonly (string Name, string Windows, string Unix, bool Script)[] Editors =
    {
        ("Visual Studio Code", "code", "code", true),
        ("Notepad++", "notepad++", "", false),
        ("Sublime Text", "subl", "subl", false),
        ("Zed", "zed", "zed", false),
        ("JetBrains Rider", "rider64", "rider", false)
    };

    // SQL tools: where they're usually installed on Windows, the program elsewhere, and what each opens
    private sealed record SqlTool(string Name, string[] WindowsPaths, string Unix, bool MySql, bool Sqlite,
                                  Func<ProjectSettings, string, List<string>> Arguments);

    private static readonly SqlTool[] SqlTools =
    {
        new SqlTool("HeidiSQL", new[] { @"HeidiSQL\heidisql.exe" }, "", true, false,
                    (settings, password) => new List<string>
                    {
                        "-h=" + settings.MySqlHost, "-P=" + settings.MySqlPort, "-u=" + settings.MySqlUser,
                        "-p=" + password, "-db=" + settings.MySqlDatabase
                    }),
        new SqlTool("DBeaver", new[] { @"DBeaver\dbeaver.exe" }, "dbeaver", true, true,
                    (settings, password) => new List<string>
                    {
                        "-con",
                        settings.Store == StoreKind.Sqlite
                            ? "driver=sqlite|name=Anarres|connect=true|database=" + settings.SqlitePath
                            : "driver=mysql|name=Anarres|connect=true|host=" + settings.MySqlHost + "|port=" +
                              settings.MySqlPort + "|user=" + settings.MySqlUser + "|password=" + password +
                              "|database=" + settings.MySqlDatabase
                    }),
        new SqlTool("MySQL Workbench", new[] { @"MySQL\MySQL Workbench*\MySQLWorkbench.exe" }, "mysql-workbench", true,
                    false, (settings, _) => new List<string>
                    {
                        "--query", settings.MySqlUser + "@" + settings.MySqlHost + ":" + settings.MySqlPort
                    }),
        new SqlTool("DB Browser for SQLite", new[] { @"DB Browser for SQLite\DB Browser for SQLite.exe" },
                    "sqlitebrowser", false, true, (settings, _) => new List<string> { settings.SqlitePath })
    };

    public static IReadOnlyList<string> EditorNames => new[] { ProgramChoice.SYSTEM }
        .Concat(Editors.Where(editor => (OperatingSystem.IsWindows() ? editor.Windows : editor.Unix).Length > 0)
                       .Select(editor => editor.Name))
        .Append(ProgramChoice.CUSTOM).ToList();

    public static IReadOnlyList<string> SqlToolNames => new[] { ProgramChoice.NONE }
        .Concat(SqlTools.Where(tool => OperatingSystem.IsWindows() || tool.Unix.Length > 0).Select(tool => tool.Name))
        .Append(ProgramChoice.CUSTOM).ToList();

    // Opens a file in the chosen editor; returns null when it started, or what went wrong
    public static string? OpenInEditor(ProgramChoice editor, string path)
    {
        if (editor.IsCustom)
            return StartCustom(editor, path, null);
        (string Name, string Windows, string Unix, bool Script) known =
            Editors.FirstOrDefault(item => item.Name == editor.Name);
        if (known.Name == null)
            return Start(new ProcessStartInfo(path) { UseShellExecute = true });
        string program = OperatingSystem.IsWindows() ? known.Windows : known.Unix;
        if (OperatingSystem.IsWindows() && known.Script)
            return Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", program, path },
                CreateNoWindow = true,
                UseShellExecute = false
            });
        return Start(Info(program, path));
    }

    // Opens the project's database in the chosen SQL tool, signed in where the tool allows it
    public static string? OpenProject(ProgramChoice choice, ProjectSettings settings, string password)
    {
        Dictionary<string, string> values = new Dictionary<string, string>
        {
            ["HOST"] = settings.MySqlHost,
            ["PORT"] = settings.MySqlPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["USER"] = settings.MySqlUser,
            ["PASSWORD"] = password,
            ["DATABASE"] = settings.MySqlDatabase
        };
        string target = settings.Store == StoreKind.Sqlite ? settings.SqlitePath : string.Empty;
        if (choice.IsCustom)
            return StartCustom(choice, target, values);
        SqlTool? tool = SqlTools.FirstOrDefault(item => item.Name == choice.Name);
        if (tool == null)
            return "Choose a SQL tool in View > Options > Integrations first.";
        bool sqlite = settings.Store == StoreKind.Sqlite;
        if (sqlite ? !tool.Sqlite : !tool.MySql)
            return tool.Name + " doesn't open " + (sqlite ? "SQLite project files" : "MySQL databases") +
                   "; choose another SQL tool in View > Options > Integrations.";
        ProcessStartInfo start = Info(Find(tool));
        foreach (string argument in tool.Arguments(settings, password))
            start.ArgumentList.Add(argument);
        return Start(start);
    }

    // The installed program when it's in a usual place, otherwise its name for the system to find
    private static string Find(SqlTool tool)
    {
        if (!OperatingSystem.IsWindows())
            return tool.Unix;
        foreach (string root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                 }.Where(root => root.Length > 0))
        {
            foreach (string relative in tool.WindowsPaths)
            {
                string? found = Expand(root, relative);
                if (found != null)
                    return found;
            }
        }

        return Path.GetFileName(tool.WindowsPaths[0]);
    }

    // A path under root whose folders may hold a * (such as a version number)
    private static string? Expand(string root, string relative)
    {
        string[] parts = relative.Split('\\');
        IEnumerable<string> candidates = new[] { root };
        for (int i = 0; i < parts.Length - 1; ++i)
        {
            string part = parts[i];
            candidates = candidates.SelectMany(folder => Directory.Exists(folder)
                                                             ? Directory.EnumerateDirectories(folder, part)
                                                             : Enumerable.Empty<string>()).ToList();
        }

        return candidates.Select(folder => Path.Combine(folder, parts[^1])).FirstOrDefault(File.Exists);
    }

    private static string? StartCustom(ProgramChoice program, string target,
                                       IReadOnlyDictionary<string, string>? values)
    {
        if (string.IsNullOrWhiteSpace(program.Path))
            return "Choose the program's path in View > Options > Integrations first.";
        ProcessStartInfo start = Info(program.Path);
        foreach (string argument in program.ArgumentsFor(target, values))
            start.ArgumentList.Add(argument);
        return Start(start);
    }

    private static ProcessStartInfo Info(string program, params string[] arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    private static string? Start(ProcessStartInfo start)
    {
        try
        {
            using Process? process = Process.Start(start);
            return process == null && !start.UseShellExecute ? "Couldn't start " + start.FileName + "." : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return "Couldn't start " + start.FileName + ": " + exception.Message;
        }
    }
}
