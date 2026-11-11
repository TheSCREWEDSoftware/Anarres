using Anarres.Core.Settings;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Anarres.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeBrushes.Use(ThemeBase.Dark, Resources);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ProjectSettings settings = ProjectSettings.Load();
            UseTheme(settings.Theme);
            MainWindow window = new MainWindow(settings);
            desktop.MainWindow = window;

            // An error in a button or menu action is logged and shown, instead of closing the whole app
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                string? log = CrashLog.Write(e.Exception, "Error in an action; Anarres kept running");
                window.ShowError(e.Exception, log);
                e.Handled = true;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void UseTheme(string name)
    {
        if (Current is not App app)
            return;
        ThemeBase theme = name == nameof(ThemeBase.Light) ? ThemeBase.Light : ThemeBase.Dark;
        app.RequestedThemeVariant = theme == ThemeBase.Light ? Avalonia.Styling.ThemeVariant.Light
                                                             : Avalonia.Styling.ThemeVariant.Dark;
        ThemeBrushes.Use(theme, app.Resources);
    }
}
