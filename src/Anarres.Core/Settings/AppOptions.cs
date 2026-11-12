using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anarres.Core.Settings;

public enum ThemeLook
{
    System,
    Dark,
    Light
}

// How the spell list draws its rows
public enum SpellListLayout
{
    Normal,
    Compact
}

// What the window's close button does
public enum CloseAction
{
    Exit,
    Tray
}

public enum StartEditor
{
    Last,
    Spells,
    Tables
}

// A theme's own text size and font; null means the one set for every theme
public sealed class ThemeText
{
    public double? FontSize { get; set; }

    public string? FontFamily { get; set; }
}

// How Anarres looks and behaves, apart from any project; saved as options.json beside settings.json
public sealed class AppOptions
{
    public const string FILE_NAME = "options.json";
    public const string BUILT_IN_COLOURS = "Built-in colours";
    public const double DEFAULT_FONT_SIZE = 14;
    public const double DEFAULT_GRID_FONT_SIZE = 13;
    public const int DEFAULT_UNDO_STEPS = 1000;

    public static readonly double[] FontSizes = { 12, 13, 14, 15, 16, 18 };
    public static readonly double[] GridFontSizes = { 11, 12, 13, 14, 15, 16 };
    public static readonly int[] UndoSteps = { 100, 500, 1000, 5000, 20000 };

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath => Path.Combine(ProjectSettings.DefaultFolder, FILE_NAME);

    // Appearance
    public ThemeLook Look { get; set; } = ThemeLook.Dark;

    // A preset or a theme file's name; the built-in colours of the look otherwise
    public string Colours { get; set; } = BUILT_IN_COLOURS;

    // Text size and font for every theme; empty font means the system's own interface font
    public double FontSize { get; set; } = DEFAULT_FONT_SIZE;

    public string FontFamily { get; set; } = string.Empty;

    // Per theme (by the name under Colours), a text size or font that replaces the ones above
    public Dictionary<string, ThemeText> ThemeText { get; set; } =
        new Dictionary<string, ThemeText>(StringComparer.OrdinalIgnoreCase);

    public double FontSizeFor(string theme)
    {
        return ThemeText.TryGetValue(theme, out ThemeText? text) && text.FontSize is { } size ? size : FontSize;
    }

    public string FontFamilyFor(string theme)
    {
        return ThemeText.TryGetValue(theme, out ThemeText? text) && !string.IsNullOrEmpty(text.FontFamily)
                   ? text.FontFamily
                   : FontFamily;
    }

    public double GridFontSize { get; set; } = DEFAULT_GRID_FONT_SIZE;

    // Spell editor: names, ranks and tooltips in the list, heading and preview use this language's columns
    public string DisplayLocale { get; set; } = "enUS";

    public bool ShowRankInList { get; set; } = true;

    // Normal rows carry the spell's icon and a hover card; compact rows are one line of text
    public SpellListLayout ListLayout { get; set; } = SpellListLayout.Normal;

    public bool ShowTooltipPreview { get; set; } = true;

    public bool ReopenLastSpell { get; set; } = true;

    public uint LastSpellId { get; set; }

    // DBC editor
    public bool HideEmptyColumns { get; set; }

    // A table opened from the list replaces the last one's tab until it's edited or kept, like a file preview
    public bool PreviewTabs { get; set; } = true;

    // Columns named Flags, Mask or Attributes show as 0x... in the grid
    public bool FlagsInHex { get; set; } = true;

    // General
    public StartEditor StartIn { get; set; } = StartEditor.Last;

    public StartEditor LastEditor { get; set; } = StartEditor.Spells;

    public bool ConfirmDeletes { get; set; } = true;

    // Apply lists what it's about to do and waits for OK
    public bool ConfirmApply { get; set; } = true;

    public int MaxUndoSteps { get; set; } = DEFAULT_UNDO_STEPS;

    public bool RememberWindow { get; set; } = true;

    // The close button exits, or hides Anarres in the system tray
    public CloseAction OnClose { get; set; } = CloseAction.Exit;

    // Asks before Anarres exits, even with nothing unsaved
    public bool ConfirmExit { get; set; }

    // The program .sql and .csv files open in, and the one the project's database opens in (Tools menu)
    public ProgramChoice TextEditor { get; set; } = new ProgramChoice { Name = ProgramChoice.SYSTEM };

    public ProgramChoice SqlTool { get; set; } = new ProgramChoice { Name = ProgramChoice.NONE };

    // A .sql file opens in the text editor once it's saved
    public bool OpenSqlAfterSaving { get; set; }

    // The DBC editor writes names beside values in cells too; hovering a cell always lists them
    public bool NamesInCells { get; set; }

    // The setup window opens at start until "Don't show this again" is ticked
    public bool ShowSetup { get; set; } = true;

    // Big tables such as Spell.dbc are kept on this PC between runs, so they open in a fraction of the time
    public bool KeepTableCache { get; set; } = true;

    // Unset until the window has been closed once
    public double? WindowX { get; set; }

    public double? WindowY { get; set; }

    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    public static AppOptions Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(path), JsonOptions)?.Checked() ??
                       new AppOptions();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable options start again from the defaults
        }

        return new AppOptions();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Options that can't be written are used for this run only
        }
    }

    public AppOptions Copy()
    {
        return JsonSerializer.Deserialize<AppOptions>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
    }

    // Values a hand-edited file may have broken are put back in range
    private AppOptions Checked()
    {
        FontSize = Math.Clamp(FontSize, FontSizes[0], FontSizes[^1]);
        ThemeText = new Dictionary<string, ThemeText>(ThemeText ?? new Dictionary<string, ThemeText>(),
                                                      StringComparer.OrdinalIgnoreCase);
        foreach (ThemeText text in ThemeText.Values)
        {
            if (text.FontSize is { } size)
                text.FontSize = Math.Clamp(size, FontSizes[0], FontSizes[^1]);
        }

        FontFamily ??= string.Empty;
        GridFontSize = Math.Clamp(GridFontSize, GridFontSizes[0], GridFontSizes[^1]);
        MaxUndoSteps = Math.Clamp(MaxUndoSteps, UndoSteps[0], UndoSteps[^1]);
        if (string.IsNullOrWhiteSpace(Colours))
            Colours = BUILT_IN_COLOURS;
        if (string.IsNullOrWhiteSpace(DisplayLocale))
            DisplayLocale = "enUS";
        return this;
    }
}
