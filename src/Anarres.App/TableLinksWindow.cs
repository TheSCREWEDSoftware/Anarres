using Anarres.Core.Dbc;
using Anarres.Core.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// Tools > Table links: a table in the middle, what points at it on the left, what it points at on the right
public sealed class TableLinksWindow : Window
{
    private const double NODE_WIDTH = 200;
    private const double NODE_HEIGHT = 30;
    private const double ROW_GAP = 10;
    private const double COLUMN_GAP = 150;
    private const double MARGIN = 20;
    private const double LABEL_WIDTH = 135;
    private const double ARROW_LENGTH = 9;
    private const double ARROW_HALF_WIDTH = 4;

    private readonly MainWindow _owner;
    private readonly List<DbcLink> _links;
    private readonly ListBox _list = new ListBox();
    private readonly TextBox _search = Ui.Clearable(new TextBox { Watermark = "Search tables" });
    private readonly Canvas _canvas = new Canvas();
    private readonly TextBlock _summary = new TextBlock { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap };
    private readonly List<string> _tables;
    private readonly HashSet<string> _linked;
    private List<TableEntry> _entries = new List<TableEntry>();
    private readonly AppOptions _options;
    private readonly CheckBox _linkedOnly = new CheckBox { Content = "Only tables with links" };
    private string _current = "Spell.dbc";

    public TableLinksWindow(MainWindow owner, DbcDefinitions definitions, AppOptions? options = null)
    {
        _owner = owner;
        _options = options ?? new AppOptions();
        Title = "Table links";
        Width = 1240;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _links = DbcLinks.Find(definitions.All);
        _tables = definitions.All.Select(definition => definition.File).Distinct(StringComparer.OrdinalIgnoreCase)
                             .Order(StringComparer.OrdinalIgnoreCase).ToList();

        _linked = new HashSet<string>(_links.SelectMany(link => new[] { link.From, link.To }),
                                      StringComparer.OrdinalIgnoreCase);
        _linkedOnly.IsChecked = _options.LinkedTablesOnly;
        ToolTip.SetTip(_linkedOnly, "Leave out tables that no column points at and that point at nothing; the " +
                                    "default is in Options > DBC Editor");
        _linkedOnly.IsCheckedChanged += (_, _) =>
        {
            _options.LinkedTablesOnly = _linkedOnly.IsChecked == true;
            if (!_owner.IsTest)
                _options.Save();
            ShowList();
        };
        _search.TextChanged += (_, _) => ShowList();
        _list.ItemTemplate = new FuncDataTemplate<TableEntry>((entry, _) => EntryView(entry));
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is TableEntry entry && entry.File != _current)
                Show(entry.File);
        };
        Button open = Ui.AsyncButton("Open in the DBC editor", () => _owner.OpenTableAsync(_current));
        Button mermaid = Ui.AsyncButton("Save every link as Mermaid...", SaveMermaidAsync);
        ToolTip.SetTip(mermaid, "A flowchart of all the links, for GitHub, mermaid.live or a Markdown editor");

        DockPanel left = new DockPanel { Width = 330, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_linkedOnly, Dock.Top);
        _search.Margin = new Thickness(0, 0, 0, 6);
        left.Children.Add(_search);
        left.Children.Add(_linkedOnly);
        left.Children.Add(_list);
        DockPanel right = new DockPanel();
        StackPanel top = Ui.Vertical(6, Ui.Horizontal(open, mermaid), _summary);
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);
        right.Children.Add(new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
        DockPanel root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);
        Content = root;
        ShowList();
        Show(_current);
    }

    private void ShowList()
    {
        string text = _search.Text?.Trim() ?? string.Empty;
        bool linkedOnly = _linkedOnly.IsChecked == true;
        _entries = _tables.Where(file => file.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                                         (!linkedOnly || _linked.Contains(file) || Same(file, _current)))
                          .Select(file => new TableEntry(file, Ins(file), Outs(file)))
                          .ToList();
        _list.ItemsSource = _entries;
        SelectCurrent();
    }

    private void Show(string file)
    {
        _current = file;
        SelectCurrent();
        // Several columns between the same two tables share one line
        List<DbcLink> incoming = Grouped(_links.Where(link => Same(link.To, file) && !Same(link.From, file)),
                                         link => link.From);
        List<DbcLink> outgoing = Grouped(_links.Where(link => Same(link.From, file)), link => link.To);
        _summary.Text = DbcLinks.Name(file) + ": " + Count(incoming.Count, "link") + " from other tables, and " +
                        Count(outgoing.Count, "link") + " to other tables (or itself). Click a table to move to " +
                        "it. Links are worked out from the column names, so a few may be missing.";

        _canvas.Children.Clear();
        double leftX = MARGIN;
        double middleX = leftX + NODE_WIDTH + COLUMN_GAP;
        double rightX = middleX + NODE_WIDTH + COLUMN_GAP;
        int rows = Math.Max(1, Math.Max(incoming.Count, outgoing.Count));
        double height = MARGIN * 2 + rows * (NODE_HEIGHT + ROW_GAP);
        double middleY = height / 2 - NODE_HEIGHT / 2;
        _canvas.Width = rightX + NODE_WIDTH + MARGIN;
        _canvas.Height = height;

        for (int i = 0; i < incoming.Count; ++i)
        {
            double y = MARGIN + i * (NODE_HEIGHT + ROW_GAP);
            Line(leftX + NODE_WIDTH, y + NODE_HEIGHT / 2, middleX, middleY + NODE_HEIGHT / 2, incoming[i].Column,
                 labelNearStart: true);
            Node(incoming[i].From, leftX, y, false);
        }

        for (int i = 0; i < outgoing.Count; ++i)
        {
            double y = MARGIN + i * (NODE_HEIGHT + ROW_GAP);
            Line(middleX + NODE_WIDTH, middleY + NODE_HEIGHT / 2, rightX, y + NODE_HEIGHT / 2, outgoing[i].Column,
                 labelNearStart: false);
            Node(outgoing[i].To, rightX, y, false);
        }

        Node(file, middleX, middleY, true);
    }

    private void Node(string file, double x, double y, bool centre)
    {
        Button node = new Button
        {
            Content = DbcLinks.Name(file),
            Width = NODE_WIDTH,
            Height = NODE_HEIGHT,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (centre)
            node.Classes.Add("accent");
        ToolTip.SetTip(node, centre ? file : "Move to " + file);
        node.Click += (_, _) => Show(file);
        Canvas.SetLeft(node, x);
        Canvas.SetTop(node, y);
        _canvas.Children.Add(node);
    }

    // A line from one table to another, named after the column, near the table that has the column
    private void Line(double x1, double y1, double x2, double y2, string column, bool labelNearStart)
    {
        _canvas.Children.Add(Ui.Themed(new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            StrokeThickness = 1
        }, Shape.StrokeProperty, ThemeColours.MUTED_TEXT));
        _canvas.Children.Add(Arrow(x1, y1, x2, y2));
        TextBlock label = new TextBlock
        {
            Text = column,
            Width = LABEL_WIDTH,
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = labelNearStart ? TextAlignment.Left : TextAlignment.Right,
            Classes = { "hint" }
        };
        ToolTip.SetTip(label, column);
        double y = (labelNearStart ? y1 : y2) - NODE_HEIGHT / 2;
        Canvas.SetLeft(label, labelNearStart ? x1 + 6 : x2 - LABEL_WIDTH - 6);
        Canvas.SetTop(label, y);
        _canvas.Children.Add(label);
    }

    // An arrowhead at the line's end: the table with the column points at the table it names
    private static Polygon Arrow(double x1, double y1, double x2, double y2)
    {
        double length = Math.Max(Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)), 1);
        double dx = (x2 - x1) / length;
        double dy = (y2 - y1) / length;
        Point tip = new Point(x2, y2);
        Point back = new Point(x2 - dx * ARROW_LENGTH, y2 - dy * ARROW_LENGTH);
        Polygon arrow = new Polygon
        {
            Points = new List<Point>
            {
                tip,
                new Point(back.X - dy * ARROW_HALF_WIDTH, back.Y + dx * ARROW_HALF_WIDTH),
                new Point(back.X + dy * ARROW_HALF_WIDTH, back.Y - dx * ARROW_HALF_WIDTH)
            }
        };
        return Ui.Themed(arrow, Shape.FillProperty, ThemeColours.MUTED_TEXT);
    }

    private static List<DbcLink> Grouped(IEnumerable<DbcLink> links, Func<DbcLink, string> other)
    {
        return links.GroupBy(other, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First() with
                    {
                        Column = string.Join(", ", group.Select(link => link.Column))
                    })
                    .OrderBy(other, StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    private void SelectCurrent()
    {
        _list.SelectedItem = _entries.FirstOrDefault(entry => Same(entry.File, _current));
    }

    // The other tables pointing at this one, and the tables it points at, as the diagram draws them
    private int Ins(string file)
    {
        return _links.Where(link => Same(link.To, file) && !Same(link.From, file))
                     .Select(link => link.From).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    private int Outs(string file)
    {
        return _links.Where(link => Same(link.From, file))
                     .Select(link => link.To).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    private static Control EntryView(TableEntry? entry)
    {
        // A recycled row can be asked for with no entry
        if (entry == null)
            return new TextBlock();
        TextBlock counts = new TextBlock
        {
            Text = entry.Ins + " in  ·  " + entry.Outs + " out",
            Classes = { "hint" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        ToolTip.SetTip(counts, entry.Ins + (entry.Ins == 1 ? " table points" : " tables point") +
                               " at it; it points at " + entry.Outs + (entry.Outs == 1 ? " table" : " tables"));
        DockPanel row = new DockPanel();
        DockPanel.SetDock(counts, Dock.Right);
        row.Children.Add(counts);
        row.Children.Add(new TextBlock
        {
            Text = entry.File,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    private static string Count(int count, string noun)
    {
        return count + " " + noun + (count == 1 ? string.Empty : "s");
    }

    private sealed record TableEntry(string File, int Ins, int Outs);

    private static bool Same(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SaveMermaidAsync()
    {
        string? path = await Ui.SaveFileAsync(this, "Save the table links", "dbc-links.mmd", "Mermaid", "mmd");
        if (path == null)
            return;
        await File.WriteAllTextAsync(path, DbcLinks.Mermaid(_links));
        _summary.Text = "Saved " + _links.Count + " links to " + path + ".";
    }
}
