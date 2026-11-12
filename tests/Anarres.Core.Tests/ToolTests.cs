using Anarres.Core.Dbc;
using Anarres.Core.Export;
using Anarres.Core.Mpq;
using Anarres.Core.Spells;

namespace Anarres.Core.Tests;

public static class ToolTests
{
    private static DbcTable Spells()
    {
        return new DbcTable(DbcDefinitions.Default.Find(SpellRecord.FILE)!);
    }

    private static void Set(DbcTable table, int row, string column, string value)
    {
        Assert.True(table.TrySetText(row, table.Definition.IndexOf(column), value, out string error),
                    column + ": " + error);
    }

    public static void ComparesRowsAndReverts()
    {
        DbcTable clean = Spells();
        Set(clean, clean.AddRow(133), "Name_Lang_enUS", "Fireball");
        DbcTable edited = Spells();
        int row = edited.AddRow(133);
        Set(edited, row, "Name_Lang_enUS", "Fireball");
        Set(edited, row, "SchoolMask", "16");
        Set(edited, edited.AddRow(90000), "Name_Lang_enUS", "Custom");

        List<CellDiff> cells = RowDiff.Row(clean, edited, row);
        Assert.Equal("SchoolMask", string.Join(",", cells.Select(cell => cell.Name)), "one field differs");
        List<RowChange> changes = RowDiff.Tables(clean, edited);
        Assert.True(changes.Any(change => change.Added && change.Key == 90000), "a new spell");
        Assert.Equal("1 changed, 1 added, 0 removed.", ChangeReport.Summary(changes), "summary");
        string report = ChangeReport.Tables("Test", clean, edited);
        Assert.True(report.Contains("SchoolMask: `0` → `16`"), "the report says old and new");

        RowDiff.Revert(clean, edited, row, cells[0].Column);
        Assert.Equal(0, RowDiff.Row(clean, edited, row).Count, "reverted");
    }

    public static void TemplatesMakeWorkingSpells()
    {
        DbcTable spells = Spells();
        foreach (SpellTemplate template in SpellTemplates.All)
        {
            int row = spells.AddRow(spells.NextFreeKey());
            List<string> skipped = SpellTemplates.Apply(template, spells, row);
            Assert.Equal(string.Empty, string.Join(",", skipped), template.Name + ": every value fits");
        }

        SpellRecord bolt = new SpellRecord(spells, 0);
        Assert.Equal(2u, bolt.U("Effect_1"), "school damage");
        Assert.Equal(133u, bolt.U("StartRecoveryCategory"), "global cooldown");
    }

    public static void ChecksSpells()
    {
        DbcTable spells = Spells();
        int row = spells.AddRow(1);
        Set(spells, row, "Name_Lang_enUS", "Broken");
        Set(spells, row, "Effect_1", "6");
        Set(spells, row, "EffectTriggerSpell_2", "424242");
        Set(spells, row, "Effect_2", "64");
        Set(spells, row, "CastingTimeIndex", "999");
        DbcTable casts = new DbcTable(DbcDefinitions.Default.Find("SpellCastTimes.dbc")!);
        casts.AddRow(1);
        List<SpellProblem> problems = SpellValidator.Check(spells, new[] { row },
                                                           file => file == "SpellCastTimes.dbc" ? casts : null);
        string all = string.Join(" | ", problems.Select(problem => problem.Text));
        Assert.True(all.Contains("Aura is 0"), "aura effect without an aura: " + all);
        Assert.True(all.Contains("424242"), "missing trigger");
        Assert.True(all.Contains("CastingTimeIndex is 999"), "missing cast time");
    }

    public static void EditsManySpells()
    {
        DbcTable spells = Spells();
        for (uint id = 1; id <= 5; ++id)
            Set(spells, spells.AddRow(id), "StartRecoveryTime", "1500");
        int column = spells.Definition.IndexOf("StartRecoveryTime");
        List<BulkChange> changes = BulkEdit.Preview(spells, new[] { 0, 1, 2 }, column, BulkOperation.Set, "1000");
        Assert.Equal(3, changes.Count, "three spells");
        Assert.Equal(3, BulkEdit.Apply(spells, column, changes), "applied");
        Assert.Equal(1000u, spells.GetRaw(2, column), "changed");
        Assert.Equal(1500u, spells.GetRaw(3, column), "the rest left alone");

        int attributes = spells.Definition.IndexOf("Attributes");
        List<BulkChange> bits = BulkEdit.Preview(spells, new[] { 0 }, attributes, BulkOperation.TurnBitsOn, "0x40");
        BulkEdit.Apply(spells, attributes, bits);
        Assert.Equal(0x40u, spells.GetRaw(0, attributes), "bits on");
    }

    public static void FindsReferences()
    {
        DbcTable spells = Spells();
        int fireball = spells.AddRow(133);
        Set(spells, fireball, "SpellClassSet", "3");
        Set(spells, fireball, "SpellClassMask_1", "1");
        int talent = spells.AddRow(11069);
        Set(spells, talent, "SpellClassSet", "3");
        Set(spells, talent, "EffectAura_1", "107");
        Set(spells, talent, "EffectSpellClassMaskA_1", "1");
        int trigger = spells.AddRow(200);
        Set(spells, trigger, "EffectTriggerSpell_1", "133");

        List<SpellReference> found = SpellReferences.Find(spells, fireball);
        Assert.True(found.Any(reference => reference.Group == SpellReferences.MODIFIED_BY &&
                                           reference.SpellId == 11069),
                    "the talent changes it");
        Assert.True(found.Any(reference => reference.Group == SpellReferences.TRIGGERED_BY && reference.SpellId == 200),
                    "triggered by");
    }

    public static void PastesRows()
    {
        DbcTable table = new DbcTable(DbcTests.SampleDefinition());
        table.AddRow(10);
        string text = "ID\tValue\tScale\tName\tRed\tGreen\tBlue\tAlpha\n10\t5\t1.5\tCopied\t1\t2\t3\t4\n" +
                      "77\t6\t2\tFresh\t0\t0\t0\t0\n";
        PasteResult result = RowPaste.Paste(table, text);
        Assert.Equal(2, result.Added, "two rows");
        Assert.Equal(1, result.Renumbered, "ID 10 was taken");
        Assert.Equal("Fresh", table.GetString(table.FindRow(77), 3), "kept its free ID");
    }

    public static void ListsClientPatches()
    {
        using TempFolder temp = new TempFolder();
        string data = Directory.CreateDirectory(temp.File("Data")).FullName;
        byte[] file = DbcFile.Write(DbcTests.SampleTable());
        MpqWriter.Write(Path.Combine(data, "patch-A.MPQ"), new[] { ("DBFilesClient\\Sample.dbc", file) });
        MpqWriter.Write(Path.Combine(data, "patch-Z.MPQ"), new[] { ("DBFilesClient\\Sample.dbc", file) });
        List<PatchArchive> archives = ClientPatches.List(data, "enUS", Path.Combine(data, "patch-A.MPQ"));
        Assert.Equal(2, archives.Count, "both patches");
        Assert.True(archives[0].IsOurs, "ours loads first");
        Assert.Equal("Sample.dbc", string.Join(",", ClientPatches.Hidden(archives)), "patch-Z hides ours");
    }
}
