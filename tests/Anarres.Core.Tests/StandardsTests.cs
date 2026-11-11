using System.Text;
using Anarres.Standards;

namespace Anarres.Core.Tests;

public static class StandardsTests
{
    private static List<string> Rules(string path, string text)
    {
        return FileRules.Check(path, Encoding.UTF8.GetBytes(text)).Select(violation => violation.Rule).ToList();
    }

    public static void ChecksFileFormat()
    {
        Assert.Equal(0, Rules("a.cs", "namespace A;\n").Count, "a clean file");
        Assert.True(Rules("a.cs", "namespace A;\r\n").Contains("line-endings"), "CRLF in a C# file");
        Assert.True(Rules("a.md", new string('x', 121) + "\n").Contains("line-length"), "too long");
        Assert.Equal(0, Rules("wotlk-3.3.5a.json", new string('x', 200)).Count, "generated definitions are skipped");
    }

    public static void ChecksCommitMessages()
    {
        Assert.Equal(0, CommitRules.Check("c", "feat(Dbc): Read WDBC files\n").Count, "a good title");
        Assert.True(CommitRules.Check("c", "feat(Git): Read\n").Count > 0, "a scope from another project");
        Assert.True(CommitRules.Check("c", "feat(UI): Add\n\nClaude-Session: x\n").Count > 0, "session links");
    }

    // Every file in the repository follows the standards
    public static void RepositoryFollowsStandards()
    {
        string? root = FindRoot();
        if (root == null)
            return;

        List<string> problems = new List<string>();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.StartsWith(".git/", StringComparison.Ordinal) || relative.StartsWith(".agents/",
                                                                                              StringComparison.Ordinal))
                continue;
            problems.AddRange(FileRules.Check(relative, File.ReadAllBytes(path)).Select(v => v.ToString()));
        }

        Assert.True(problems.Count == 0, string.Join(" | ", problems.Take(10)));
    }

    private static string? FindRoot()
    {
        DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "Anarres.sln")))
            folder = folder.Parent;
        return folder?.FullName;
    }
}
