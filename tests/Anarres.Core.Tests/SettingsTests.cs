using Anarres.Core.Settings;

namespace Anarres.Core.Tests;

public static class SettingsTests
{
    public static void OptionsRoundTrip()
    {
        using TempFolder temp = new TempFolder();
        AppOptions options = new AppOptions { Look = ThemeLook.Light, DisplayLocale = "deDE", FontSize = 16 };
        options.Save(temp.File("options.json"));
        AppOptions loaded = AppOptions.Load(temp.File("options.json"));
        Assert.Equal(ThemeLook.Light, loaded.Look, "look");
        Assert.Equal("deDE", loaded.DisplayLocale, "language");

        File.WriteAllText(temp.File("broken.json"), "{ \"FontSize\": 400, \"MaxUndoSteps\": -3 }");
        AppOptions repaired = AppOptions.Load(temp.File("broken.json"));
        Assert.Equal(AppOptions.FontSizes[^1], repaired.FontSize, "font size put back in range");
        Assert.Equal(AppOptions.UndoSteps[0], repaired.MaxUndoSteps, "undo steps put back in range");
        Assert.Equal(AppOptions.DEFAULT_FONT_SIZE, AppOptions.Load(temp.File("missing.json")).FontSize, "defaults");
    }

    public static void ThemesSaveLoadAndResolve()
    {
        using TempFolder temp = new TempFolder();
        ThemeStore store = new ThemeStore(temp.Path);
        Theme theme = Theme.BuiltIn(ThemeBase.Dark).Copy("Mine");
        theme.Colours[ThemeColours.ACCENT] = "#123456";
        store.Save(theme);

        Assert.Equal("#123456", store.Resolve("Mine", ThemeBase.Light).Colour(ThemeColours.ACCENT), "own theme");
        Assert.Equal(ThemePresets.FROST, store.Resolve(ThemePresets.FROST, ThemeBase.Light).Name, "preset");
        Assert.Equal(ThemeBase.Light, store.Resolve("Missing", ThemeBase.Light).Base, "falls back to the look");

        // A theme file from an older build gets the colours it lacks written into it
        File.WriteAllText(temp.File("old.json"), "{ \"Name\": \"Old\", \"Base\": \"Light\", \"Colours\": {} }");
        Theme old = store.Find("Old")!;
        Assert.Equal(ThemeColours.Colour(ThemeBase.Light, ThemeColours.TEXT), old.Colour(ThemeColours.TEXT), "filled");
        Assert.True(File.ReadAllText(temp.File("old.json")).Contains(ThemeColours.TEXT), "written back");
        Assert.True(!ThemeColours.IsColour("#12"), "not a colour");
    }

    public static void ThemesHaveTheirOwnText()
    {
        AppOptions options = new AppOptions { FontSize = 14, FontFamily = "Inter" };
        options.ThemeText["Fel"] = new ThemeText { FontSize = 16 };
        Assert.Equal(16.0, options.FontSizeFor("fel"), "Fel's own size, any case");
        Assert.Equal("Inter", options.FontFamilyFor("Fel"), "Fel follows the font for every theme");
        Assert.Equal(14.0, options.FontSizeFor("Frost"), "other themes follow the size for every theme");
        AppOptions copy = options.Copy();
        Assert.Equal(16.0, copy.FontSizeFor("Fel"), "kept when saved and read back");
    }

    public static void EditsReadyMadeThemesAndRestoresThem()
    {
        using TempFolder temp = new TempFolder();
        ThemeStore store = new ThemeStore(temp.File("themes"));
        string preset = ThemePresets.Names[0];
        Theme edited = ThemePresets.Find(preset)!.Copy(preset);
        edited.Colours[ThemeColours.ACCENT] = "#123456";
        store.Save(edited);
        Assert.Equal("#123456", store.Resolve(preset, ThemeBase.Dark).Colour(ThemeColours.ACCENT), "your version");
        store.RemoveEdits(preset);
        Assert.Equal(ThemePresets.Find(preset)!.Colour(ThemeColours.ACCENT),
                     store.Resolve(preset, ThemeBase.Dark).Colour(ThemeColours.ACCENT), "the original again");

        Theme dark = Theme.BuiltIn(ThemeBase.Dark);
        dark.Colours[ThemeColours.TEXT] = "#ABCDEF";
        store.Save(dark);
        Assert.Equal("#ABCDEF", store.BuiltIn(ThemeBase.Dark).Colour(ThemeColours.TEXT), "edited built-in colours");
        Assert.True(ThemeStore.Original("Dark") != null && ThemeStore.Original("Mine") == null, "what has defaults");
    }
}
