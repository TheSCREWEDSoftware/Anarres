using System.Text;
using Anarres.Core.Dbc;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Anarres.Core.Storage;

namespace Anarres.Core.Export;

// A readable record of what changed: each edited table against its clean file, row by row, field by field
public static class ChangeReport
{
    // Markdown, so it reads well as a file, in a pull request or pasted into a chat
    public static async Task<string> BuildAsync(IDbcStore store, ProjectSettings settings, DbcDefinitions definitions,
                                                IReadOnlyCollection<string> files,
                                                IProgress<(string File, double Done)>? progress = null,
                                                CancellationToken cancellation = default)
    {
        StringBuilder text = new StringBuilder();
        text.Append("# Changes from the clean files\n\n");
        int index = 0;
        foreach (string file in files)
        {
            progress?.Report((file, (double)index++ / files.Count));
            DbcTable table = await store.LoadAsync(file, definitions, null, cancellation);
            DbcTable? clean = DbcMerge.LoadSource(settings, file, definitions);
            text.Append("## ").Append(table.Definition.File).Append("\n\n");
            if (clean == null)
            {
                text.Append("No clean copy to compare with; every row (").Append(table.Rows.Count)
                    .Append(") counts as new.\n\n");
                continue;
            }

            List<RowChange> changes = RowDiff.Tables(clean, table);
            text.Append(Summary(changes)).Append("\n\n");
            foreach (RowChange change in changes)
                Row(text, change, table);
        }

        progress?.Report((string.Empty, 1));
        return text.ToString();
    }

    // Two copies of a table, such as two .dbc files, as Markdown
    public static string Tables(string title, DbcTable old, DbcTable current)
    {
        StringBuilder text = new StringBuilder();
        text.Append("# ").Append(title).Append("\n\n");
        List<RowChange> changes = RowDiff.Tables(old, current);
        text.Append(Summary(changes)).Append("\n\n");
        foreach (RowChange change in changes)
            Row(text, change, change.Removed ? old : current);
        return text.ToString();
    }

    public static string Summary(List<RowChange> changes)
    {
        int added = changes.Count(change => change.Added);
        int removed = changes.Count(change => change.Removed);
        int changed = changes.Count - added - removed;
        return changes.Count == 0 ? "No changes."
                                  : changed + " changed, " + added + " added, " + removed + " removed.";
    }

    private static void Row(StringBuilder text, RowChange change, DbcTable table)
    {
        int row = table.FindRow(change.Key);
        string name = row >= 0 ? Name(table, row) : string.Empty;
        string title = change.Key + (name.Length > 0 ? " " + name : string.Empty);
        if (change.Added)
        {
            text.Append("- **Added** ").Append(title).Append('\n');
            return;
        }

        if (change.Removed)
        {
            text.Append("- **Removed** ").Append(change.Key).Append('\n');
            return;
        }

        text.Append("- **").Append(title).Append("**\n");
        foreach (CellDiff cell in change.Cells)
            text.Append("  - ").Append(cell.Name).Append(": `").Append(Escape(cell.Old)).Append("` → `")
                .Append(Escape(cell.New)).Append("`\n");
    }

    // A row's English name, when the table has one, so changes read as "133 Fireball"
    private static string Name(DbcTable table, int row)
    {
        int column = SpellRecord.TextColumn(table.Definition, "Name", SpellRecord.ENGLISH);
        return column < 0 ? string.Empty : table.GetString(row, column);
    }

    private static string Escape(string value)
    {
        return value.Replace("`", "'").Replace("\r", string.Empty).Replace("\n", "\\n");
    }
}
