using Anarres.Core.Spells;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// What links to a spell, grouped; double-click a spell to open it. Returns the ID to open, or null
public sealed class UsedByWindow : Window
{
    private readonly ListBox _list = new ListBox();

    public UsedByWindow(uint id, string name, List<SpellReference> references)
    {
        Title = "Used by: " + id + " " + name;
        Width = 760;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        List<Control> items = new List<Control>();
        string[] order =
        {
            SpellReferences.TRIGGERED_BY, SpellReferences.MODIFIED_BY, SpellReferences.TRIGGERS,
            SpellReferences.SAME_VISUAL, SpellReferences.SAME_ICON
        };
        foreach (string group in order)
        {
            List<SpellReference> entries = references.Where(reference => reference.Group == group).ToList();
            items.Add(new TextBlock
            {
                Text = group + " (" + entries.Count + ")",
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 2),
                Tag = null
            });
            foreach (SpellReference entry in entries)
                items.Add(new TextBlock
                {
                    Text = "    " + entry.SpellId + "  " + entry.SpellName + "   " + entry.How,
                    Tag = entry.SpellId
                });
        }

        _list.ItemsSource = items;
        _list.DoubleTapped += (_, _) => Open();
        Button open = new Button { Content = "Open spell", IsDefault = true, Classes = { "accent" } };
        open.Click += (_, _) => Open();
        Button close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close(null);
        StackPanel buttons = Ui.Horizontal(open, close);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(20, 10);
        TextBlock hint = new TextBlock
        {
            Text = "Family-mask matches are spells of the same family whose effect masks overlap this spell's " +
                   "SpellClassMask: talents and auras that change it. Shared visuals and icons list up to 200.",
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

    private void Open()
    {
        if (_list.SelectedItem is Control { Tag: uint id })
            Close(id);
    }
}
