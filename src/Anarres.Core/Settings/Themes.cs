using System.Globalization;

namespace Anarres.Core.Settings;

// One colour of the interface: its key, what it colours, and its Dark and Light values (the scheme Urras uses)
public sealed record ThemeColour(string Key, string Description, string Dark, string Light);

public enum ThemeBase
{
    Dark,
    Light
}

public static class ThemeColours
{
    public const string BACKGROUND = "Background";
    public const string SURFACE = "Surface";
    public const string RAISED = "Raised";
    public const string HOVER = "Hover";
    public const string PRESSED = "Pressed";
    public const string BORDER = "Border";
    public const string ACCENT_TEXT = "AccentText";
    public const string TEXT = "Text";
    public const string MUTED_TEXT = "MutedText";
    public const string ERROR = "Error";
    public const string ATTENTION = "Attention";
    public const string WARNING = "Warning";
    public const string ADDED = "Added";
    public const string ACCENT = "Accent";
    public const string DIVIDER = "Divider";
    public const string GRID_LINE = "GridLine";
    public const string GRID_HEADER = "GridHeader";
    public const string GRID_ALTERNATE = "GridAlternate";
    public const string CHANGED = "Changed";

    public static readonly IReadOnlyList<ThemeColour> All = new List<ThemeColour>
    {
        new ThemeColour(BACKGROUND, "Window and title bar", "#14161B", "#F2F3F5"),
        new ThemeColour(SURFACE, "The panels with lists and editors", "#1B1E24", "#FFFFFF"),
        new ThemeColour(RAISED, "Buttons, boxes, menus and tooltips", "#252930", "#FFFFFF"),
        new ThemeColour(HOVER, "Under the pointer", "#2E333B", "#ECEEF1"),
        new ThemeColour(PRESSED, "Pressed buttons", "#383E47", "#E1E4E8"),
        new ThemeColour(BORDER, "Edges of buttons, boxes and panels", "#30353D", "#D6DAE0"),
        new ThemeColour(ACCENT_TEXT, "Text on accent-coloured buttons", "#FFFFFF", "#FFFFFF"),
        new ThemeColour(TEXT, "Text", "#E6EDF3", "#1F2328"),
        new ThemeColour(MUTED_TEXT, "Dimmed text: hints, IDs, details", "#8B949E", "#59636E"),
        new ThemeColour(ERROR, "Errors and values that don't fit", "#FF7B72", "#CF222E"),
        new ThemeColour(ATTENTION, "Links and information", "#79C0FF", "#0969DA"),
        new ThemeColour(WARNING, "Warnings and unsaved changes", "#F2C14E", "#9A6700"),
        new ThemeColour(ADDED, "Saved and finished", "#57AB5A", "#1A7F37"),
        new ThemeColour(ACCENT, "Accent: selection, main buttons, checkboxes", "#A27BFF", "#7A3DF0"),
        new ThemeColour(DIVIDER, "Lines between areas", "#33FFFFFF", "#33000000"),
        new ThemeColour(GRID_LINE, "Lines between cells in the table", "#22FFFFFF", "#1A000000"),
        new ThemeColour(GRID_HEADER, "Column headers in the table", "#20242B", "#F6F8FA"),
        new ThemeColour(GRID_ALTERNATE, "Every other row in the table", "#08FFFFFF", "#05000000"),
        new ThemeColour(CHANGED, "Rows changed but not saved", "#33F2C14E", "#33F2C14E")
    };

    public static string Colour(ThemeBase themeBase, string key)
    {
        ThemeColour? known = All.FirstOrDefault(colour => colour.Key == key);
        return known == null ? "#FF00FF" : themeBase == ThemeBase.Light ? known.Light : known.Dark;
    }

    // The same colour at another opacity, such as a faint banner background from its border colour
    public static string WithAlpha(string colour, byte alpha)
    {
        string digits = colour.TrimStart('#');
        if (digits.Length == 8)
            digits = digits[2..];
        return "#" + alpha.ToString("X2", CultureInfo.InvariantCulture) + digits.ToUpperInvariant();
    }
}
