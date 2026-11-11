using System.Globalization;
using Anarres.Core.Dbc;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Anarres.App;

// Picks a spell icon from SpellIcon.dbc, with the pictures from the client when its Data folder is set
public sealed class IconPickerWindow : Window
{
    private const double ICON_SIZE = 36;

    private readonly ListBox _list = new ListBox();
    private readonly TextBox _filter = new TextBox { Watermark = "Filter by ID or texture name" };
    private readonly List<IconItem> _all;

    public IconPickerWindow(IconImages images, uint current)
    {
        Title = "Pick an icon";
        Width = 620;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DbcTable icons = images.Icons!;
        _all = Enumerable.Range(0, icons.Rows.Count)
                         .Select(row => new IconItem(images, icons.Key(row), images.Texture(icons.Key(row))))
                         .ToList();
        _list.ItemsSource = _all;
        _list.ItemTemplate = new FuncDataTemplate<IconItem>((_, _) =>
        {
            Image picture = new Image { Width = ICON_SIZE, Height = ICON_SIZE };
            picture.Bind(Image.SourceProperty, new Binding(nameof(IconItem.Picture)));
            TextBlock id = new TextBlock { Width = 60, VerticalAlignment = VerticalAlignment.Center };
            id.Bind(TextBlock.TextProperty, new Binding(nameof(IconItem.Id)));
            TextBlock name = new TextBlock { VerticalAlignment = VerticalAlignment.Center,
                                             TextTrimming = TextTrimming.CharacterEllipsis };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(IconItem.Texture)));
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
                                    Children = { picture, id, name } };
        });
        _list.SelectedItem = _all.FirstOrDefault(item => item.Id == current);
        _filter.TextChanged += (_, _) =>
        {
            string text = (_filter.Text ?? string.Empty).Trim();
            _list.ItemsSource = text.Length == 0
                                    ? _all
                                    : _all.Where(item => item.Id.ToString(CultureInfo.InvariantCulture) == text ||
                                                         item.Texture.Contains(text,
                                                                               StringComparison.OrdinalIgnoreCase))
                                          .ToList();
        };
        _list.DoubleTapped += (_, _) => Choose();

        Button ok = new Button { Content = "Use icon", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => Choose();
        cancel.Click += (_, _) => Close(null);
        StackPanel buttons = Ui.Horizontal(cancel, ok);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(16, 10);
        _filter.Margin = new Thickness(16, 16, 16, 8);

        TextBlock hint = Ui.Hint(images.Library.IsAvailable
                                     ? "Double-click an icon to use it."
                                     : "Set the WoW Data folder in Project settings to see the pictures.");
        hint.Margin = new Thickness(16, 0, 16, 8);
        DockPanel root = new DockPanel();
        DockPanel.SetDock(_filter, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(_filter);
        root.Children.Add(hint);
        root.Children.Add(buttons);
        root.Children.Add(new Border { Classes = { "group" }, Margin = new Thickness(16, 0), Child = _list });
        Content = root;
        Opened += (_, _) =>
        {
            if (_list.SelectedItem != null)
                _list.ScrollIntoView(_list.SelectedItem);
            _filter.Focus();
        };
    }

    private void Choose()
    {
        if (_list.SelectedItem is IconItem item)
            Close((uint?)item.Id);
    }

    // The picture is decoded when the row first shows
    private sealed class IconItem
    {
        private readonly IconImages _images;

        public IconItem(IconImages images, uint id, string texture)
        {
            _images = images;
            Id = id;
            Texture = texture;
        }

        public uint Id { get; }

        public string Texture { get; }

        public Bitmap? Picture => _images.Get(Id);
    }
}
