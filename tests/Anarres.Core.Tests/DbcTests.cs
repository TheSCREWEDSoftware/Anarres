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
}
