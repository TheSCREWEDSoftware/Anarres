using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// What differs between two copies of a table, row by row, with a report to save
public sealed class DiffWindow : Window
{
    public DiffWindow(string oldName, DbcTable old, string newName, DbcTable current)
    {
        Title = "Compare " + Path.GetFileName(oldName) + " with " + Path.GetFileName(newName);
        Width = 960;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        List<RowChange> changes = RowDiff.Tables(old, current);
        List<string> lines = new List<string>();
        foreach (RowChange change in changes)
        {
            if (change.Added || change.Removed)
            {
                lines.Add((change.Added ? "Added   " : "Removed ") + change.Key);
                continue;
            }

            lines.Add("Changed " + change.Key + ":");
            lines.AddRange(change.Cells.Select(cell => "        " + cell.Name + ": " + cell.Old + "  →  " + cell.New));
        }

        TextBlock summary = new TextBlock
        {
            Text = oldName + "  →  " + newName + "\n" + ChangeReport.Summary(changes),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 16, 20, 8)
        };
        ListBox list = new ListBox { ItemsSource = lines, FontFamily = new FontFamily("Consolas, Menlo, monospace") };
        Button save = Ui.AsyncButton("Save report...", async () =>
        {
            string? path = await Ui.SaveFileAsync(this, "Save the report", "dbc-changes.md", "Markdown", "md");
            if (path != null)
                await File.WriteAllTextAsync(path, ChangeReport.Tables(Title, old, current));
        });
        Button close = new Button { Content = "Close", IsCancel = true, IsDefault = true };
        close.Click += (_, _) => Close();
        StackPanel buttons = Ui.Horizontal(save, close);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10);
        DockPanel root = new DockPanel();
        DockPanel.SetDock(summary, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(summary);
        root.Children.Add(buttons);
        root.Children.Add(new Border { Margin = new Thickness(20, 0), Child = list });
        Content = root;
    }
}
