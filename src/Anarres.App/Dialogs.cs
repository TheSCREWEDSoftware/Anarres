using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anarres.App;

// Small modal dialogs built in code, since Avalonia has no built-in message box
public static class Dialogs
{
    private const double DIALOG_WIDTH = 480;
    private const double BODY_MARGIN = 20;
    private const double BODY_SPACING = 14;

    public static Window CreateWindow(string title, double width = DIALOG_WIDTH)
    {
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
    }

    public static async Task MessageAsync(Window owner, string title, string message)
    {
        Window dialog = CreateWindow(title);
        Button ok = new Button { Content = "OK", IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = Layout(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, null,
                                ok);
        await dialog.ShowDialog(owner);
    }

    public static async Task<bool> ConfirmAsync(Window owner, string title, string message, string confirmText)
    {
        Window dialog = CreateWindow(title);
        Button confirm = new Button { Content = confirmText, IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = Layout(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, null, confirm,
                                cancel);
        return await dialog.ShowDialog<bool>(owner);
    }

    public enum UnsavedChoice
    {
        Cancel,
        Save,
        Discard
    }

    public static async Task<UnsavedChoice> UnsavedAsync(Window owner, IEnumerable<string> tables)
    {
        Window dialog = CreateWindow("Unsaved changes");
        Button save = new Button { Content = "Save", IsDefault = true, Classes = { "accent" } };
        Button discard = new Button { Content = "Discard" };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => dialog.Close(UnsavedChoice.Save);
        discard.Click += (_, _) => dialog.Close(UnsavedChoice.Discard);
        cancel.Click += (_, _) => dialog.Close(UnsavedChoice.Cancel);
        string message = "These tables have changes that aren't saved yet: " + string.Join(", ", tables) +
                         ". Save them first?";
        dialog.Content = Layout(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, null, save,
                                discard, cancel);
        return await dialog.ShowDialog<UnsavedChoice>(owner);
    }

    public static async Task<string?> PromptAsync(Window owner, string title, string label, string initialText = "",
                                                  bool password = false)
    {
        Window dialog = CreateWindow(title);
        TextBox input = new TextBox { Text = initialText };
        if (password)
            input.PasswordChar = '*';
        Button ok = new Button { Content = "OK", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(input.Text ?? string.Empty);
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        dialog.Content = Layout(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input, ok, cancel);
        return await dialog.ShowDialog<string?>(owner);
    }

    public static Control Layout(Control message, Control? input, params Button[] buttons)
    {
        StackPanel body = new StackPanel { Margin = new Thickness(BODY_MARGIN), Spacing = BODY_SPACING };
        body.Children.Add(message);
        if (input != null)
            body.Children.Add(input);
        StackPanel row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        foreach (Button button in buttons)
            row.Children.Add(button);
        body.Children.Add(row);
        return body;
    }
}
