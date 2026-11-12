using Anarres.Core.Dbc;
using Anarres.Core.Settings;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Anarres.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeBrushes.Use(Theme.BuiltIn(ThemeBase.Dark), Resources);
        Resources[FONT_SIZE_RESOURCE] = AppOptions.DEFAULT_FONT_SIZE;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ProjectSettings settings = ProjectSettings.Load();
            AppOptions options = AppOptions.Load();
            UseOptions(options);
            MainWindow window = new MainWindow(settings, options);
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

    public const string FONT_SIZE_RESOURCE = "Anarres.FontSize";
    public const string FONT_FAMILY_RESOURCE = "ContentControlThemeFontFamily";

    // Windows 11's interface font, then Windows 10's, then a common open font elsewhere
    public const string DEFAULT_FONTS = "Segoe UI Variable Text, Segoe UI, Noto Sans";

    // Applies the look, colours, text size and undo depth; windows follow through their resources
    public static void UseOptions(AppOptions options)
    {
        DbcTable.MaxUndo = options.MaxUndoSteps;
        if (Current is not App app)
            return;

        ThemeBase look = options.Look switch
        {
            ThemeLook.Light => ThemeBase.Light,
            ThemeLook.System => SystemIsLight(app) ? ThemeBase.Light : ThemeBase.Dark,
            _ => ThemeBase.Dark
        };
        // A preset or theme file brings its own base, which wins over the look
        Theme theme = options.Colours == AppOptions.BUILT_IN_COLOURS
                          ? ThemeStore.Default.BuiltIn(look)
                          : ThemeStore.Default.Resolve(options.Colours, look);
        app.RequestedThemeVariant = theme.Base == ThemeBase.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        ThemeBrushes.Use(theme, app.Resources);
        double size = options.FontSizeFor(options.Colours);
        string family = options.FontFamilyFor(options.Colours);
        app.Resources[FONT_SIZE_RESOURCE] = size;
        app.Resources["ControlContentThemeFontSize"] = size;
        app.Resources[FONT_FAMILY_RESOURCE] = new FontFamily(family.Length > 0 ? family : DEFAULT_FONTS);
    }

    private static bool SystemIsLight(App app)
    {
        return app.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light;
    }
}
