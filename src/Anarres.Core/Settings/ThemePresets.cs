namespace Anarres.Core.Settings;

// Ready-made themes; only what differs from the dark or light base is listed
public static class ThemePresets
{
    public const string URRAS = "Urras";
    public const string FROST = "Frost";
    public const string FEL = "Fel";
    public const string HORDE = "Horde";
    public const string ALLIANCE = "Alliance";
    public const string PARCHMENT = "Parchment";

    private sealed record Preset(string Name, ThemeBase Base, Dictionary<string, string> Colours);

    private static readonly Preset[] Presets =
    {
        // Urras's own accent, for both apps to look alike
        Dark(URRAS, "#14161B", "#1B1E24", "#252930", "#2E333B", "#383E47", "#30353D", "#8B7CF6", "#E6EDF3",
             "#8B949E"),
        Dark(FROST, "#0F1620", "#152030", "#1D2B3E", "#253750", "#2E4360", "#26384F", "#5AB7FF", "#E3F1FF",
             "#88A2BD"),
        Dark(FEL, "#0F1712", "#151F18", "#1D2B21", "#26382B", "#2F4535", "#27392C", "#7CDB3A", "#E6F5E1",
             "#8FA996"),
        Dark(HORDE, "#1A1212", "#221717", "#2E1F1F", "#3A2828", "#463131", "#3B2A2A", "#E5484D", "#F5E6E6",
             "#B09595"),
        Dark(ALLIANCE, "#10141F", "#161C2B", "#1F273A", "#28324A", "#323D5A", "#2A3550", "#4C8DFF", "#E8EEF8",
             "#909CB4"),
        new Preset(PARCHMENT, ThemeBase.Light, Colours("#EFE6D2", "#FAF5E8", "#FFFBF0", "#EBE0C8", "#E0D3B6",
                                                       "#D6C7A4", "#8C4A1E", "#2B2216", "#6E604A"))
    };

    public static IReadOnlyList<string> Names => Presets.Select(preset => preset.Name).ToList();

    public static bool IsPreset(string? name)
    {
        return Presets.Any(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // A fresh copy each time, with every colour filled in, so changing it never changes the preset
    public static Theme? Find(string? name)
    {
        Preset? preset = Presets.FirstOrDefault(candidate => string.Equals(candidate.Name, name,
                                                                           StringComparison.OrdinalIgnoreCase));
        if (preset == null)
            return null;

        Theme theme = new Theme
        {
            Name = preset.Name,
            Base = preset.Base,
            Colours = new Dictionary<string, string>(preset.Colours)
        };
        theme.FillMissing();
        return theme;
    }

    private static Preset Dark(string name, string background, string surface, string raised, string hover,
                               string pressed, string border, string accent, string text, string muted)
    {
        return new Preset(name, ThemeBase.Dark, Colours(background, surface, raised, hover, pressed, border, accent,
                                                        text, muted));
    }

    private static Dictionary<string, string> Colours(string background, string surface, string raised, string hover,
                                                      string pressed, string border, string accent, string text,
                                                      string muted)
    {
        return new Dictionary<string, string>
        {
            [ThemeColours.BACKGROUND] = background,
            [ThemeColours.SURFACE] = surface,
            [ThemeColours.RAISED] = raised,
            [ThemeColours.HOVER] = hover,
            [ThemeColours.PRESSED] = pressed,
            [ThemeColours.BORDER] = border,
            [ThemeColours.ACCENT] = accent,
            [ThemeColours.TEXT] = text,
            [ThemeColours.MUTED_TEXT] = muted,
            [ThemeColours.GRID_HEADER] = raised
        };
    }
}
