using Anarres.Core.Dbc;
using Anarres.Core.Settings;
using Anarres.Core.Spells;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Anarres.App.Tests;

public static class WindowTests
{
    public static async Task MainWindowLoadsSpells()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings) { IsTest = true };
        window.Show();
        await Pump.WaitForAsync(() => window.SpellEditor.Spells != null, "the spells to load");
        Assert.Equal(3, window.SpellEditor.Spells!.Rows.Count, "spells");
        window.Session.Close();
        window.Close();
    }

    public static async Task SpellEditsUndoAndSave()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings) { IsTest = true };
        window.Show();
        await Pump.WaitForAsync(() => window.SpellEditor.Spells != null, "the spells to load");
        DbcTable spells = window.SpellEditor.Spells!;
        int name = spells.Definition.IndexOf("Name_Lang_enUS");
        int row = spells.FindRow(133);
        spells.SetString(row, name, "Pyroblast");
        window.SpellEditor.Undo(redo: false);
        Assert.Equal("Fireball", spells.GetString(row, name), "undone");
        window.SpellEditor.Undo(redo: true);
        await window.SpellEditor.SaveAsync();
        Assert.True(!spells.IsModified, "saved");

        window.Session.Forget(SpellRecord.FILE);
        DbcTable reloaded = await window.Session.LoadAsync(SpellRecord.FILE);
        Assert.Equal("Pyroblast", reloaded.GetString(reloaded.FindRow(133), name), "saved name");
        window.Session.Close();
        window.Close();
    }

    // Fields carry a "?" badge explaining the column, as Keira3 does
    public static async Task FieldsExplainColumns()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings) { IsTest = true };
        window.Show();
        await Pump.WaitForAsync(() => window.SpellEditor.Spells != null, "the spells to load");
        List<string> tips = window.SpellEditor.GetLogicalDescendants().OfType<Border>()
                                  .Where(border => border.Classes.Contains("info"))
                                  .Select(border => ToolTip.GetTip(border) as string ?? string.Empty).ToList();
        Assert.True(tips.Count > 50, "badges on the fields (" + tips.Count + ")");
        Assert.True(tips.Any(tip => tip.Contains("global cooldown") && tip.Contains("StartRecoveryCategory")),
                    "the global cooldown field is explained");
        window.Session.Close();
        window.Close();
    }

    // Ctrl+F finds fields and flag bits by label, column, notes or AzerothCore name; toggles change the bits
    public static async Task FindsFieldsAndTogglesFlags()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings) { IsTest = true };
        window.Show();
        await Pump.WaitForAsync(() => window.SpellEditor.Spells != null, "the spells to load");
        FieldFinder finder = window.SpellEditor.Finder;
        List<FoundField> gcd = finder.Find("global cooldown");
        Assert.True(gcd.Any(field => field.Words.Contains("StartRecoveryCategory")),
                    "a field by its label: " + string.Join(" | ", gcd.Take(5).Select(field => field.Label)));
        Assert.True(finder.Find("school").Count > 0, "school");
        Assert.True(finder.Find("range").Count > 0, "range");
        FoundField passive = finder.Find("SPELL_ATTR0_PASSIVE").First();
        Assert.Equal("Passive spell", passive.Label, "a flag by its AzerothCore name");
        Assert.True(passive.Where.StartsWith("Attributes"), "found on the Attributes tab: " + passive.Where);

        DbcTable spells = window.SpellEditor.Spells!;
        int column = spells.Definition.IndexOf("Attributes");
        int row = spells.FindRow(window.SpellEditor.CurrentSpellId!.Value);
        uint before = spells.GetRaw(row, column);
        ToggleSwitch toggle = passive.Target.GetLogicalDescendants().OfType<ToggleSwitch>().First();
        toggle.IsChecked = toggle.IsChecked != true;
        Assert.Equal(before ^ 0x40u, spells.GetRaw(row, column), "bit 6 flipped");
        window.Session.Close();
        window.Close();
    }

    public static async Task GridSortsAndHides()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings) { IsTest = true };
        window.Show();
        await window.ShowTablesForTestAsync();
        await window.TableEditor.OpenForTestAsync(SpellRecord.FILE);
        DbcGrid grid = window.TableEditor.Grid;
        Assert.Equal(3, grid.ShownRows.Count, "rows shown");

        grid.SortBy(0, descending: false);
        DbcTable table = grid.Table!;
        Assert.Equal("10,116,133", string.Join(",", grid.ShownRows.Select(row => table.Key(row))), "sorted by ID");
        grid.SortBy(0, descending: true);
        Assert.Equal("133,116,10", string.Join(",", grid.ShownRows.Select(row => table.Key(row))), "descending");

        grid.HideEmptyColumns();
        Assert.True(grid.ShownColumns.Count < table.ColumnCount, "empty columns hidden");
        grid.ShowAllColumns();
        Assert.Equal(table.ColumnCount, grid.ShownColumns.Count, "all columns back");
        window.Session.Close();
        window.Close();
    }

    // Every dialog builds and opens without an error
    public static async Task DialogsOpen()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings) { IsTest = true };
        window.Show();
        await Pump.WaitForAsync(() => window.SpellEditor.Spells != null, "the spells to load");
        DbcDefinition spell = DbcDefinitions.Default.Find(SpellRecord.FILE)!;
        DbcTable spells = window.SpellEditor.Spells!;
        int row = Math.Max(0, spells.FindRow(133));
        List<Window> dialogs = new List<Window>
        {
            new SettingsWindow(project.Settings),
            new ImportWindow(window.Session),
            new ExportWindow(window.Session),
            new ColumnsWindow(spell, project.Settings.DefinitionsFolder),
            new WorldDatabaseWindow(project.Settings, "Spell 133 (Fireball)"),
            new SetupWindow(project.Settings, showAgain: true),
            new FlagsDialog("Attributes", SpellEnums.Get("Attributes"), 0x10000),
            new OptionsWindow(new AppOptions(), OptionsWindow.GENERAL),
            new ThemeEditorWindow(Theme.BuiltIn(ThemeBase.Light), new ThemeStore(project.Folder)),
            new CompareWindow(spells, spells, row, window.Session.Loaded),
            new TemplateWindow(),
            new FindReplaceWindow(spells, "school:fire"),
            new UsedByWindow(133, "Fireball", SpellReferences.Find(spells, row)),
            new DiffWindow("old.dbc", spells, "new.dbc", spells),
            new PatchesWindow(project.Settings),
            new ModelWindow(window.Session)
        };
        foreach (Window dialog in dialogs)
        {
            dialog.Show(window);
            await Task.Delay(50);
            dialog.Close();
        }

        window.Session.Close();
        window.Close();
    }

    public static async Task OptionsApply()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings) { IsTest = true };
        window.Show();
        await Pump.WaitForAsync(() => window.SpellEditor.Spells != null, "the spells to load");
        AppOptions options = new AppOptions
        {
            Look = ThemeLook.Light,
            Colours = ThemePresets.PARCHMENT,
            GridFontSize = 16,
            FlagsInHex = false
        };
        App.UseOptions(options);
        window.SpellEditor.UseOptions(options);
        window.TableEditor.UseOptions(options);
        Assert.Equal(ThemePresets.PARCHMENT, ThemeBrushes.Current.Name, "theme in use");
        Assert.Equal(16.0, window.TableEditor.Grid.CellFontSize, "grid text size");
        App.UseOptions(new AppOptions());
        window.Session.Close();
        window.Close();
    }
}
