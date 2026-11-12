using Anarres.Core.Spells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// Picks the kind of spell to start from; returns the template, or null
public sealed class TemplateWindow : Window
{
    private readonly ListBox _list = new ListBox();

    public TemplateWindow()
    {
        Title = "New spell from a template";
        Width = 620;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _list.ItemsSource = SpellTemplates.All;
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SpellTemplate>((template, _) =>
            Ui.Vertical(2,
                new TextBlock { Text = template?.Name, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = template?.Summary, Classes = { "hint" }, TextWrapping = TextWrapping.Wrap }));
        _list.SelectedIndex = 0;
        _list.DoubleTapped += (_, _) => Close(_list.SelectedItem as SpellTemplate);

        Button create = new Button { Content = "Create", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        create.Click += (_, _) => Close(_list.SelectedItem as SpellTemplate);
        cancel.Click += (_, _) => Close(null);
        StackPanel buttons = Ui.Horizontal(create, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10);

        TextBlock hint = new TextBlock
        {
            Text = "Each one is modelled on a real spell and gets a new ID. Change its name, values and icon after.",
            Classes = { "hint" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 16, 20, 8)
        };
        DockPanel root = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(hint);
        root.Children.Add(buttons);
        root.Children.Add(new Border { Margin = new Thickness(20, 0), Child = _list });
        Content = root;
    }
}
