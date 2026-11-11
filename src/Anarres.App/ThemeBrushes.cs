using Anarres.Core.Settings;
using Avalonia.Controls;
using Avalonia.Media;

namespace Anarres.App;

// The theme's colours as brushes for code; XAML reads the same ones as Anarres.<Key> resources
public static class ThemeBrushes
{
    public const string RESOURCE_PREFIX = "Anarres.";

    private static Dictionary<string, IBrush> _brushes = Build(ThemeBase.Dark);

    public static ThemeBase Current { get; private set; } = ThemeBase.Dark;

    public static IBrush Text => Get(ThemeColours.TEXT);
    public static IBrush MutedText => Get(ThemeColours.MUTED_TEXT);
    public static IBrush Error => Get(ThemeColours.ERROR);
    public static IBrush Warning => Get(ThemeColours.WARNING);
    public static IBrush Added => Get(ThemeColours.ADDED);
    public static IBrush Accent => Get(ThemeColours.ACCENT);
    public static IBrush Divider => Get(ThemeColours.DIVIDER);
    public static IBrush Surface => Get(ThemeColours.SURFACE);
    public static IBrush Border => Get(ThemeColours.BORDER);

    public static void Use(ThemeBase theme, IResourceDictionary? resources)
    {
        Current = theme;
        _brushes = Build(theme);
        if (resources == null)
            return;
        foreach ((string key, IBrush brush) in _brushes)
            resources[RESOURCE_PREFIX + key] = brush;
        ControlLook.Apply(theme, resources);
    }

    public static IBrush Get(string key)
    {
        return _brushes.TryGetValue(key, out IBrush? brush) ? brush : Brushes.Magenta;
    }

    public static Color ColourOf(string key)
    {
        return Color.Parse(ThemeColours.Colour(Current, key));
    }

    private static Dictionary<string, IBrush> Build(ThemeBase theme)
    {
        Dictionary<string, IBrush> brushes = new Dictionary<string, IBrush>(StringComparer.Ordinal);
        foreach (ThemeColour colour in ThemeColours.All)
            brushes[colour.Key] = new SolidColorBrush(Color.Parse(ThemeColours.Colour(theme, colour.Key)));
        return brushes;
    }
}
