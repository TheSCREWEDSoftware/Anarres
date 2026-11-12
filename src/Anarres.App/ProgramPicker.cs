using Anarres.Core.Settings;
using Avalonia.Controls;

namespace Anarres.App;

// A known program from a list, or Custom with its path and arguments, as in Urras's Integrations
public sealed class ProgramPicker : StackPanel
{
    private readonly ComboBox _name = new ComboBox { Width = 380 };
    private readonly TextBox _path = new TextBox { Width = 380, Watermark = "Program" };
    private readonly TextBox _arguments = new TextBox { Width = 380, Watermark = "Arguments" };
    private readonly StackPanel _custom;

    public ProgramPicker(IReadOnlyList<string> names)
    {
        Spacing = 6;
        _name.ItemsSource = names;
        _custom = Ui.Vertical(4, Ui.Row("Program", _path), Ui.Row("Arguments", _arguments));
        _name.SelectionChanged += (_, _) => _custom.IsVisible = _name.SelectedItem as string == ProgramChoice.CUSTOM;
        Children.Add(_name);
        Children.Add(_custom);
    }

    public void Use(ProgramChoice choice)
    {
        IReadOnlyList<string> names = (IReadOnlyList<string>)_name.ItemsSource!;
        _name.SelectedItem = names.Contains(choice.Name) ? choice.Name : names[0];
        _path.Text = choice.Path;
        _arguments.Text = choice.Arguments;
        _custom.IsVisible = _name.SelectedItem as string == ProgramChoice.CUSTOM;
    }

    public ProgramChoice Choice()
    {
        return new ProgramChoice
        {
            Name = _name.SelectedItem as string ?? string.Empty,
            Path = (_path.Text ?? string.Empty).Trim(),
            Arguments = string.IsNullOrWhiteSpace(_arguments.Text) ? ProgramChoice.TARGET : _arguments.Text.Trim()
        };
    }
}
