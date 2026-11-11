using Anarres.Core.Dbc;
using Anarres.Core.Spells;
using Avalonia.Controls;

namespace Anarres.App.Tests;

public static class WindowTests
{
    public static async Task MainWindowLoadsSpells()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings);
        window.Show();
        await Pump.WaitForAsync(() => window.SpellEditor.Spells != null, "the spells to load");
        Assert.Equal(3, window.SpellEditor.Spells!.Rows.Count, "spells");
        window.Session.Close();
        window.Close();
    }

    public static async Task SpellEditsUndoAndSave()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings);
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

    public static async Task GridSortsAndHides()
    {
        using TestProject project = await TestProject.CreateAsync();
        MainWindow window = new MainWindow(project.Settings);
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
        MainWindow window = new MainWindow(project.Settings);
        window.Show();
        await Pump.WaitForAsync(() => window.SpellEditor.Spells != null, "the spells to load");
        DbcDefinition spell = DbcDefinitions.Default.Find(SpellRecord.FILE)!;
        List<Window> dialogs = new List<Window>
        {
            new SettingsWindow(project.Settings),
            new ImportWindow(window.Session),
            new ExportWindow(window.Session),
            new ColumnsWindow(spell, project.Settings.DefinitionsFolder),
            new WorldDatabaseWindow(project.Settings, 133, "Fireball"),
            new FlagsDialog("Attributes", SpellEnums.Get("Attributes"), 0x10000)
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
}
