using Anarres.Core.Dbc;

namespace Anarres.Core.Tests;

public static class DbcTests
{
    public static DbcDefinition SampleDefinition()
    {
        return new DbcDefinition
        {
            File = "Sample.dbc",
            Columns = new List<DbcColumn>
            {
                new DbcColumn("ID", DbcColumnType.UInt32),
                new DbcColumn("Value", DbcColumnType.Int32),
                new DbcColumn("Scale", DbcColumnType.Float),
                new DbcColumn("Name", DbcColumnType.String),
                new DbcColumn("Red", DbcColumnType.Byte),
                new DbcColumn("Green", DbcColumnType.Byte),
                new DbcColumn("Blue", DbcColumnType.Byte),
                new DbcColumn("Alpha", DbcColumnType.SByte)
            }
        };
    }

    public static DbcTable SampleTable()
    {
        DbcTable table = new DbcTable(SampleDefinition());
        int first = table.AddRow(10);
        table.SetInt(first, 1, -5);
        table.SetFloat(first, 2, 1.5f);
        table.SetString(first, 3, "Fireball");
        table.SetUInt(first, 4, 255);
        table.SetInt(first, 7, -1);
        int second = table.AddRow(11);
        table.SetString(second, 3, "Fireball");
        int third = table.AddRow(12);
        table.SetString(third, 3, "Frost Nova");
        return table;
    }

    public static void WritesAndReadsBack()
    {
        DbcDefinitions definitions = new DbcDefinitions();
        definitions.Add(SampleDefinition());
        byte[] data = DbcFile.Write(SampleTable());

        DbcFile.Header header = DbcFile.ReadHeader(data);
        Assert.Equal(3, header.RecordCount, "records");
        Assert.Equal(8, header.FieldCount, "fields");
        Assert.Equal(20, header.RecordSize, "four 4-byte fields and four bytes");
        // "\0Fireball\0Frost Nova\0": equal strings are stored once
        Assert.Equal(1 + 9 + 11, header.StringBlockSize, "string block");

        DbcTable read = DbcFile.Read(data, "Sample.dbc", definitions);
        Assert.Equal(-5, read.GetInt(0, 1), "negative int");
        Assert.Equal(1.5f, read.GetFloat(0, 2), "float");
        Assert.Equal("Fireball", read.GetString(1, 3), "shared string");
        Assert.Equal("Frost Nova", read.GetString(2, 3), "string");
        Assert.Equal(255u, read.GetUInt(0, 4), "byte");
        Assert.Equal(-1, read.GetInt(0, 7), "signed byte");
        Assert.True(!read.IsModified, "a table just read has no changes");
    }

    // Counts that overflow an int or are negative are refused with a clear message, not a crash
    public static void RefusesDamagedHeaders()
    {
        byte[] data = DbcFile.Write(SampleTable());
        foreach (int count in new[] { int.MaxValue, -1 })
        {
            byte[] damaged = (byte[])data.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(damaged.AsSpan(4), count);
            try
            {
                DbcFile.Read(damaged, "Sample.dbc");
                Assert.True(false, "a record count of " + count + " is refused");
            }
            catch (InvalidDataException exception)
            {
                Assert.True(exception.Message.StartsWith("Sample.dbc", StringComparison.Ordinal), "names the file");
            }
        }
    }

    public static void TracksChangesByKey()
    {
        DbcTable table = SampleTable();
        table.AcceptChanges();
        table.SetString(1, 3, "Pyroblast");
        Assert.True(table.ChangedKeys.SetEquals(new uint[] { 11 }), "changed row");

        table.SetRaw(1, 0, 20);
        Assert.True(table.RemovedKeys.Contains(11) && table.ChangedKeys.Contains(20), "an ID change moves the row");
        Assert.Equal(1, table.FindRow(20), "found under its new ID");

        table.RemoveRow(0);
        Assert.True(table.RemovedKeys.Contains(10), "removed row");
        Assert.Equal(21u, table.NextFreeKey(), "next free ID");
    }

    public static void ParsesCellText()
    {
        DbcTable table = SampleTable();
        Assert.True(table.TrySetText(0, 1, "-42", out _), "int");
        Assert.True(!table.TrySetText(0, 4, "300", out string error) && error.Length > 0, "byte too big");
        Assert.True(table.TrySetText(0, 0, "0x20", out _), "hex");
        Assert.Equal(32u, table.GetUInt(0, 0), "hex value");
        Assert.True(table.TrySetText(0, 2, "0.25", out _), "float");
        Assert.Equal("0.25", table.GetText(0, 2), "float text");
        Assert.True(table.TrySetText(0, 1, "0xFFFFFFFF", out _), "hex bits in a signed column");
        Assert.Equal(-1, table.GetInt(0, 1), "all bits set");
    }

    public static void InfersUnknownFiles()
    {
        DbcDefinition definition = SampleDefinition();
        definition.Columns.RemoveRange(4, 4);
        DbcTable table = new DbcTable(definition);
        for (uint id = 1; id <= 20; ++id)
        {
            int row = table.AddRow(id);
            table.SetInt(row, 1, (int)id * 3);
            table.SetFloat(row, 2, id * 0.5f + 1);
            table.SetString(row, 3, "Name " + id);
        }

        DbcTable read = DbcFile.Read(DbcFile.Write(table), "Unknown.dbc", new DbcDefinitions());
        Assert.True(read.Definition.IsInferred, "no definition, so it's inferred");
        Assert.Equal(DbcColumnType.UInt32, read.Definition.Columns[1].Type, "integers");
        Assert.Equal(DbcColumnType.Float, read.Definition.Columns[2].Type, "floats");
        Assert.Equal(DbcColumnType.String, read.Definition.Columns[3].Type, "strings");
        Assert.Equal("Name 7", read.GetString(6, 3), "string read");
    }

    public static void BuiltInDefinitionsLoad()
    {
        DbcDefinition? spell = DbcDefinitions.Default.Find("Spell.dbc");
        Assert.True(spell != null, "Spell.dbc is defined");
        Assert.Equal(234, spell!.Columns.Count, "Spell.dbc columns");
        Assert.Equal(936, spell.RecordSize, "Spell.dbc record size");
        Assert.Equal("spell_dbc", spell.Table, "AzerothCore table");
        Assert.True(DbcDefinitions.Default.Find("spell_dbc") == spell, "found by table name");
        DbcDefinition? gt = DbcDefinitions.Default.Find("gtCombatRatings");
        Assert.True(gt is { IndexIsRowNumber: true, Columns.Count: 1 }, "gt files use the row number as ID");
    }

    // Every real file reads, writes and reads back with the same values
    public static void RealFilesRoundTrip()
    {
        string? folder = TestData.DbcFolder();
        if (folder == null)
            return;

        foreach (string path in Directory.EnumerateFiles(folder, "*.dbc"))
        {
            byte[] original = File.ReadAllBytes(path);
            // The client ships some files empty, such as CharVariations.dbc
            if (original.Length == 0)
                continue;
            DbcTable table = DbcFile.Read(original, Path.GetFileName(path));
            byte[] written = DbcFile.Write(table);
            DbcTable again = DbcFile.Read(written, Path.GetFileName(path));
            DbcFile.Header before = DbcFile.ReadHeader(original);
            DbcFile.Header after = DbcFile.ReadHeader(written);
            string name = Path.GetFileName(path);
            Assert.Equal(before.RecordCount, after.RecordCount, name + " records");
            Assert.Equal(before.RecordSize, after.RecordSize, name + " record size");
            for (int row = 0; row < table.Rows.Count; ++row)
            {
                for (int column = 0; column < table.ColumnCount; ++column)
                {
                    if (table.GetText(row, column) != again.GetText(row, column))
                        throw new Exception(name + " row " + row + " column " + column + " changed");
                }
            }

            // Files without strings come out byte for byte the same
            if (table.Definition.Columns.All(column => column.Type != DbcColumnType.String) &&
                before.StringBlockSize <= 1)
                Assert.True(original.AsSpan().SequenceEqual(written), name + " is identical");
        }
    }

    public static void RefusesTakenIds()
    {
        DbcTable table = SampleTable();
        Assert.True(!table.TrySetText(0, 0, "11", out string error) && error.Contains("11"), "ID in use");
        Assert.Equal(10u, table.GetUInt(0, 0), "ID unchanged");
        Assert.Throws<InvalidOperationException>(() => table.AddRow(12), "adding a taken ID");
        Assert.True(table.TrySetText(0, 0, "10", out _), "setting the row's own ID again");
    }

    public static void UndoesAndRedoes()
    {
        DbcTable table = SampleTable();
        table.AcceptChanges();
        table.ClearUndo();
        table.SetString(0, 3, "F");
        table.SetString(0, 3, "Fi");
        table.SetString(0, 3, "Fire");
        table.BreakUndoStep();
        table.SetInt(0, 1, 99);
        table.RemoveRow(2);
        table.CopyRow(0, 50);

        Assert.True(table.Undo() >= 0, "undo the copy");
        Assert.Equal(-1, table.FindRow(50), "copy gone");
        table.Undo();
        Assert.Equal("Frost Nova", table.GetString(2, 3), "removed row back in place");
        table.Undo();
        Assert.Equal(-5, table.GetInt(0, 1), "number back");
        table.Undo();
        Assert.Equal("Fireball", table.GetString(0, 3), "typing undone in one step");
        Assert.True(!table.CanUndo, "nothing left");

        table.Redo();
        Assert.Equal("Fire", table.GetString(0, 3), "redo the typing");
        table.Redo();
        table.Redo();
        Assert.Equal(2, table.Rows.Count, "redo the removal");
        table.SetInt(1, 1, 7);
        Assert.True(!table.CanRedo, "a new edit clears redo");
    }

    // Unsaved changes are what still differs from the saved table, not every edit ever made
    public static void ChangesSetBackAreNotUnsaved()
    {
        DbcTable table = SampleTable();
        table.AcceptChanges();
        int row = table.FindRow(10);
        table.SetInt(row, 1, 7);
        Assert.True(table.IsModified, "edited");
        table.SetInt(row, 1, -5);
        Assert.True(!table.IsModified && table.ChangedKeys.Count == 0, "set back by hand");

        table.SetInt(row, 1, 9);
        table.Undo();
        Assert.True(!table.IsModified, "undone");
        table.Redo();
        Assert.True(table.IsModified, "redone");
        table.Undo();

        table.AddRow(50);
        table.Undo();
        Assert.True(!table.IsModified, "new row undone");
        table.RemoveRow(table.FindRow(11));
        Assert.True(table.IsModified && table.RemovedKeys.Contains(11u), "row deleted");
        table.Undo();
        Assert.True(!table.IsModified && table.RemovedKeys.Count == 0, "delete undone");

        int added = table.AddRow(60);
        table.SetInt(added, 1, 3);
        table.RemoveRow(added);
        Assert.True(!table.IsModified && table.RemovedKeys.Count == 0, "a new row deleted leaves nothing");

        table.SetRaw(row, 0, 70);
        table.SetRaw(table.FindRow(70), 0, 10);
        Assert.True(!table.IsModified && table.RemovedKeys.Count == 0, "an ID changed and changed back");
    }

    // The example from Keira3 issue 3123: 193837 copper, then gold and silver changed
    public static void ConvertsMoney()
    {
        Assert.Equal((19u, 38u, 37u), Anarres.Core.Money.Split(193837), "19g 38s 37c");
        Assert.Equal(215937u, Anarres.Core.Money.ToCopper(21, 59, 37), "21g 59s 37c");
        Assert.Equal("19g 38s 37c", Anarres.Core.Money.Text(193837), "as text");
        Assert.Equal("0c", Anarres.Core.Money.Text(0), "nothing");
        Assert.Equal(150u, Anarres.Core.Money.ToCopper(0, 0, 150), "copper past 99 carries over");
        Assert.True(Anarres.Core.Money.IsColumn("TaxiPath.dbc", "Cost") &&
                    Anarres.Core.Money.IsColumn("SkillTiers.dbc", "Cost_3") &&
                    !Anarres.Core.Money.IsColumn("Spell.dbc", "ManaCost"), "money columns");
    }

    public static void ColoursSql()
    {
        List<Anarres.Core.Export.SqlToken> tokens =
            Anarres.Core.Export.SqlTokens.Split("DELETE FROM `spell_dbc` WHERE `ID` = -5; -- note\nSELECT 'it''s'");
        string kinds = string.Join(",", tokens.Where(token => token.Kind != Anarres.Core.Export.SqlTokenKind.Plain)
                                              .Select(token => token.Kind + ":" + token.Text));
        Assert.Equal("Keyword:DELETE,Keyword:FROM,Identifier:`spell_dbc`,Keyword:WHERE,Identifier:`ID`,Number:-5," +
                     "Comment:-- note,Keyword:SELECT,Text:'it''s'", kinds, "tokens");
        Assert.Equal("DELETE FROM `spell_dbc` WHERE `ID` = -5; -- note\nSELECT 'it''s'",
                     string.Concat(tokens.Select(token => token.Text)), "nothing lost");
    }
}
