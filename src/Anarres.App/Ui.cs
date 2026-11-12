using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Anarres.App;

// Small builders for the controls every window repeats, so the layout code stays readable
public static class Ui
{
    public const double LABEL_WIDTH = 170;
    public const double FIELD_WIDTH = 160;
    public const double GAP = 8;

    public static TextBlock Label(string text, double width = LABEL_WIDTH)
    {
        return new TextBlock
        {
            Text = text,
            Width = width,
            Classes = { "label" },
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    public static TextBlock Hint(string text)
    {
        return new TextBlock { Text = text, Classes = { "hint" } };
    }

    public static TextBlock Title(string text)
    {
        return new TextBlock { Text = text, Classes = { "groupTitle" } };
    }

    // A labelled row: the label on the left, then the controls
    public static StackPanel Row(string label, params Control[] controls)
    {
        StackPanel row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = GAP,
            Margin = new Thickness(0, 2)
        };
        row.Children.Add(Label(label));
        foreach (Control control in controls)
            row.Children.Add(control);
        return row;
    }

    public const double BADGE_SIZE = 16;

    // A small round "?" that explains a field when hovered, like Keira3's column notes
    public static Control InfoBadge(string tip)
    {
        Border badge = new Border
        {
            Width = BADGE_SIZE,
            Height = BADGE_SIZE,
            CornerRadius = new CornerRadius(BADGE_SIZE / 2),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Help),
            Background = Brushes.Transparent,
            Classes = { "info" },
            Child = Themed(new TextBlock
            {
                Text = "?",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, TextBlock.ForegroundProperty, ThemeColours.ACCENT)
        };
        Themed(badge, Border.BorderBrushProperty, ThemeColours.ACCENT);
        ToolTip.SetTip(badge, tip);
        ToolTip.SetShowDelay(badge, 150);
        return badge;
    }

    // Tooltip on the row, plus a badge after its label (or an equal gap, so fields stay lined up)
    public static Control Explained(Control row, string tip, bool described)
    {
        ToolTip.SetTip(row, tip);
        if (row is not StackPanel { Orientation: Orientation.Horizontal } panel || panel.Children.Count == 0 ||
            panel.Children[0] is not TextBlock label || double.IsNaN(label.Width))
            return row;
        label.Width = Math.Max(40, label.Width - BADGE_SIZE - panel.Spacing);
        panel.Children.Insert(1, described ? InfoBadge(tip) : new Control { Width = BADGE_SIZE });
        return row;
    }

    // A row for a column, explained by what ColumnDocs knows about it
    public static Control Documented(Control row, string file, string column, string? table = null)
    {
        return Explained(row, ColumnDocs.Tooltip(file, column, table), ColumnDocs.Describe(file, column) != null);
    }

    public static StackPanel Horizontal(params Control[] controls)
    {
        StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GAP };
        foreach (Control control in controls)
            panel.Children.Add(control);
        return panel;
    }

    public static StackPanel Vertical(double spacing, params Control[] controls)
    {
        StackPanel panel = new StackPanel { Spacing = spacing };
        foreach (Control control in controls)
            panel.Children.Add(control);
        return panel;
    }

    // A titled box of fields inside an editor tab
    public static Border Group(string title, params Control[] content)
    {
        StackPanel panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(Title(title));
        foreach (Control control in content)
            panel.Children.Add(control);
        return new Border { Classes = { "group" }, Child = panel };
    }

    // Follows a theme colour, so the control changes with the theme instead of keeping the colour it was made with
    public static T Themed<T>(T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(ThemeBrushes.RESOURCE_PREFIX + key));
        return control;
    }

    public static Border Card(Control child)
    {
        return new Border { Classes = { "card" }, Child = child };
    }

    public static Button Button(string text, Action onClick, bool accent = false)
    {
        Button button = new Button { Content = text };
        if (accent)
            button.Classes.Add("accent");
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Button AsyncButton(string text, Func<Task> onClick, bool accent = false)
    {
        Button button = new Button { Content = text };
        if (accent)
            button.Classes.Add("accent");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await onClick();
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    // A path box with a Browse button that picks a folder
    public static StackPanel FolderPicker(Window owner, TextBox box, string title)
    {
        box.Width = 420;
        Button browse = new Button { Content = "Browse..." };
        browse.Click += async (_, _) =>
        {
            if (await PickFolderAsync(owner, title) is { } folder)
                box.Text = folder;
        };
        return Horizontal(box, browse);
    }

    public static async Task<string?> PickFolderAsync(Window owner, string title)
    {
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public static async Task<List<string>> PickFilesAsync(Window owner, string title, string typeName,
                                                         string[] patterns, bool many)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = many,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = patterns } }
        });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
    }

    public static async Task<string?> SaveFileAsync(Window owner, string title, string suggested, string typeName,
                                                    string extension)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            DefaultExtension = extension,
            FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = new[] { "*." + extension } } }
        });
        return file?.TryGetLocalPath();
    }
}
