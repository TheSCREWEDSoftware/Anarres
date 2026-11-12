using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        return known == null ? Theme.UNKNOWN_COLOUR : themeBase == ThemeBase.Light ? known.Light : known.Dark;
    }

    // #RGB, #RRGGBB or #AARRGGBB
    public static bool IsColour(string? value)
    {
        if (value is not { Length: > 1 } || value[0] != '#')
            return false;
        string digits = value[1..];
        return digits.Length is 3 or 6 or 8 && digits.All(Uri.IsHexDigit);
    }

    // The same colour at another opacity, such as a faint banner background from its border colour
    public static string WithAlpha(string colour, byte alpha)
    {
        string digits = colour.TrimStart('#');
        if (digits.Length == 3)
            digits = string.Concat(digits.Select(digit => new string(digit, 2)));
        if (digits.Length == 8)
            digits = digits[2..];
        return "#" + alpha.ToString("X2", CultureInfo.InvariantCulture) + digits.ToUpperInvariant();
    }
}

// Colours on top of a dark or light base, built in, preset or a file; the same scheme and format as Urras's themes
public sealed class Theme
{
    public const string DARK = "Dark";
    public const string LIGHT = "Light";

    // Bright pink for a key no build knows, so it stands out instead of hiding
    public const string UNKNOWN_COLOUR = "#FF00FF";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public string Name { get; set; } = DARK;

    [JsonConverter(typeof(JsonStringEnumConverter<ThemeBase>))]
    public ThemeBase Base { get; set; } = ThemeBase.Dark;

    public Dictionary<string, string> Colours { get; set; } = new Dictionary<string, string>();

    [JsonIgnore]
    public bool IsBuiltIn => Name is DARK or LIGHT || ThemePresets.IsPreset(Name);

    public static Theme BuiltIn(ThemeBase themeBase)
    {
        Theme theme = new Theme { Name = themeBase == ThemeBase.Light ? LIGHT : DARK, Base = themeBase };
        theme.FillMissing();
        return theme;
    }

    // A colour by key; a missing or broken one falls back to the base's default
    public string Colour(string key)
    {
        if (Colours.TryGetValue(key, out string? value) && ThemeColours.IsColour(value))
            return value;
        return ThemeColours.Colour(Base, key);
    }

    // Keys this build knows that the theme doesn't have yet get the base's default; true when any were added
    public bool FillMissing()
    {
        bool added = false;
        foreach (ThemeColour colour in ThemeColours.All)
        {
            if (Colours.TryGetValue(colour.Key, out string? value) && ThemeColours.IsColour(value))
                continue;
            Colours[colour.Key] = Base == ThemeBase.Light ? colour.Light : colour.Dark;
            added = true;
        }

        return added;
    }

    public Theme Copy(string name)
    {
        return new Theme { Name = name, Base = Base, Colours = new Dictionary<string, string>(Colours) };
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static Theme? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Theme>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Theme files in the themes folder beside the settings, one .json each
public sealed class ThemeStore
{
    public const string FOLDER_NAME = "themes";
    private const string EXTENSION = ".json";
    private const string UNSAFE_CHARACTERS = "\\/:*?\"<>|";

    public ThemeStore(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public static ThemeStore Default => new ThemeStore(Path.Combine(ProjectSettings.DefaultFolder, FOLDER_NAME));

    // The custom themes, by name; files that can't be read are left out
    public List<Theme> LoadAll()
    {
        if (!Directory.Exists(Folder))
            return new List<Theme>();

        List<Theme> themes = new List<Theme>();
        foreach (string file in Directory.EnumerateFiles(Folder, "*" + EXTENSION, Files.AnyCase)
                                         .Order(StringComparer.Ordinal))
        {
            if (Load(file) is { } theme)
                themes.Add(theme);
        }

        return themes;
    }

    // Reads a theme and, if this build knows colours it lacks, writes them into the file
    public Theme? Load(string file)
    {
        try
        {
            Theme? theme = Theme.FromJson(File.ReadAllText(file));
            if (theme == null)
                return null;
            if (string.IsNullOrWhiteSpace(theme.Name) || theme.IsBuiltIn)
                theme.Name = Path.GetFileNameWithoutExtension(file);
            if (theme.FillMissing())
                File.WriteAllText(file, theme.ToJson());
            return theme;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Theme? Find(string name)
    {
        return LoadAll().FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public string FileFor(string name)
    {
        // Characters Windows refuses in file names, on every system, so theme files can be shared
        string safe = string.Concat(name.Select(character => UNSAFE_CHARACTERS.Contains(character) ||
                                                             char.IsControl(character) ? '-' : character));
        return Path.Combine(Folder, safe + EXTENSION);
    }

    public void Save(Theme theme)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FileFor(theme.Name), theme.ToJson());
    }

    // The theme for a name: a preset or the built-in colours (your edited version if any), or a theme file
    public Theme Resolve(string? name, ThemeBase fallback)
    {
        if (ThemePresets.Find(name) is { } preset)
            return Find(preset.Name) ?? preset;
        if (!string.IsNullOrEmpty(name) && name is not (Theme.DARK or Theme.LIGHT) && Find(name) is { } custom)
            return custom;
        return BuiltIn(fallback);
    }

    // The built-in Dark or Light colours, or your edited version of them
    public Theme BuiltIn(ThemeBase themeBase)
    {
        Theme original = Theme.BuiltIn(themeBase);
        return Find(original.Name) ?? original;
    }

    // What a theme looks like as shipped: a preset or the built-in colours; null for your own themes
    public static Theme? Original(string name)
    {
        if (ThemePresets.Find(name) is { } preset)
            return preset;
        return name switch
        {
            Theme.DARK => Theme.BuiltIn(ThemeBase.Dark),
            Theme.LIGHT => Theme.BuiltIn(ThemeBase.Light),
            _ => null
        };
    }

    // Deletes your version of a preset or the built-in colours, so the shipped one is used again
    public void RemoveEdits(string name)
    {
        if (Original(name) != null && File.Exists(FileFor(name)))
            File.Delete(FileFor(name));
    }
}
