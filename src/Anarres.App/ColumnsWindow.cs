using Anarres.Core.Dbc;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Anarres.App;

// A table's column layout; names and types can be changed and saved as a definition file that replaces the built-in
public sealed class ColumnsWindow : Window
{
    private const double WINDOW_WIDTH = 620;
    private const double WINDOW_HEIGHT = 680;

    private readonly DbcDefinition _definition;
    private readonly string _folder;
    private readonly List<(TextBox Name, ComboBox Type)> _rows = new List<(TextBox, ComboBox)>();
    private readonly TextBlock _status = new TextBlock { Classes = { "hint" } };

    public ColumnsWindow(DbcDefinition definition, string folder)
    {
        _definition = definition;
        _folder = folder;
        Title = "Columns of " + definition.File;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        string[] types = Enum.GetNames<DbcColumnType>();
        StackPanel list = new StackPanel { Spacing = 2 };
        for (int index = 0; index < definition.Columns.Count; ++index)
        {
            DbcColumn column = definition.Columns[index];
            TextBox name = new TextBox { Text = column.Name, Width = 300 };
            ComboBox type = new ComboBox { ItemsSource = types, SelectedItem = column.Type.ToString(), Width = 120 };
            _rows.Add((name, type));
            TextBlock number = new TextBlock { Text = index.ToString(), Width = 40, Classes = { "label" } };
            list.Children.Add(Ui.Horizontal(number, name, type));
        }

        Button save = Ui.Button("Save layout", Save, accent: true);
        Button close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close(false);
        StackPanel buttons = Ui.Horizontal(save, close);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;

        string origin = definition.IsInferred ? "guessed from the file's data" : "from AzerothCore";
        StackPanel top = new StackPanel { Margin = new Thickness(20, 16, 20, 8), Spacing = 6 };
        top.Children.Add(Ui.Hint("This layout is " + origin + ". Types must keep each record the same size: " +
                                 "Byte and SByte take 1 byte, Int16 and UInt16 take 2, the rest take 4. The layout " +
                                 "is saved to " + folder + "."));
        StackPanel bottom = new StackPanel { Margin = new Thickness(20, 8, 20, 16), Spacing = 8 };
        bottom.Children.Add(_status);
        bottom.Children.Add(buttons);

        DockPanel root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = list, Margin = new Thickness(20, 0) });
        Content = root;
    }

    private void Save()
    {
        DbcDefinition changed = _definition.Copy();
        changed.IsInferred = false;
        changed.Columns = _rows.Select(row => new DbcColumn((row.Name.Text ?? string.Empty).Trim(),
                                                            Enum.Parse<DbcColumnType>(row.Type.SelectedItem as string ??
                                                                                      nameof(DbcColumnType.UInt32))))
                               .ToList();
        if (changed.Columns.Any(column => column.Name.Length == 0) ||
            changed.Columns.Select(column => column.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            changed.Columns.Count)
        {
            _status.Text = "Every column needs a name of its own.";
            return;
        }

        if (changed.RecordSize != _definition.RecordSize)
        {
            _status.Text = "These types make a record of " + changed.RecordSize + " bytes; the file's records are " +
                           _definition.RecordSize + " bytes.";
            return;
        }

        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, _definition.Name + ".json"), DbcDefinitions.ToJson(changed));
            Close(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _status.Text = "The layout couldn't be saved: " + exception.Message;
        }
    }
}
