using System.Text;
using Anarres.Core.Dbc;

namespace Anarres.Core.Export;

// Tables as CSV: a header of column names, then one line per row, quoted where a value needs it
public static class CsvFile
{
    public static void Write(string path, DbcTable table)
    {
        using StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(string.Join(",", table.Definition.Columns.Select(column => Quote(column.Name))));
        for (int row = 0; row < table.Rows.Count; ++row)
            writer.WriteLine(string.Join(",", Enumerable.Range(0, table.ColumnCount)
                                                        .Select(column => Quote(table.GetText(row, column)))));
    }

    // A new table from a CSV file; columns are matched by name, and columns the file lacks stay zero or empty
    public static DbcTable Read(string path, DbcDefinition definition)
    {
        List<List<string>> lines = Parse(File.ReadAllText(path, Encoding.UTF8));
        if (lines.Count == 0)
            throw new InvalidDataException("The CSV file is empty.");

        List<string> header = lines[0];
        int[] map = definition.Columns.Select(column => header.FindIndex(name =>
            string.Equals(name.Trim(), column.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (map.All(index => index < 0))
            throw new InvalidDataException("None of the CSV file's columns match " + definition.File + ".");
        if (definition.HasKey && map[0] < 0)
            throw new InvalidDataException("The CSV file has no " + definition.Columns[0].Name + " column.");

        DbcTable table = new DbcTable(definition);
        for (int line = 1; line < lines.Count; ++line)
        {
            List<string> values = lines[line];
            if (values.Count == 1 && values[0].Length == 0)
                continue;
            uint[] cells = new uint[definition.Columns.Count];
            for (int column = 0; column < cells.Length; ++column)
            {
                if (map[column] < 0 || map[column] >= values.Count)
                    continue;
                string text = values[map[column]];
                DbcColumnType type = definition.Columns[column].Type;
                if (!DbcTable.TryParse(type, text, out uint raw, out string error))
                    throw new InvalidDataException("Line " + (line + 1) + ", " + definition.Columns[column].Name +
                                                   ": " + error);
                cells[column] = type == DbcColumnType.String ? table.Intern(text) : raw;
            }

            table.Load(cells);
        }

        if (definition.HasKey && table.Rows.Select(row => row[0]).Distinct().Count() != table.Rows.Count)
            throw new InvalidDataException("The CSV file has rows with the same " + definition.Columns[0].Name + ".");
        return table;
    }

    public static string Quote(string value)
    {
        bool quote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    // Splits CSV text into lines of values; quoted values may hold commas, quotes and line breaks
    public static List<List<string>> Parse(string text)
    {
        List<List<string>> lines = new List<List<string>>();
        List<string> current = new List<string>();
        StringBuilder value = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; ++i)
        {
            char character = text[i];
            if (quoted)
            {
                if (character == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    value.Append('"');
                    ++i;
                }
                else if (character == '"')
                    quoted = false;
                else
                    value.Append(character);
                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                {
                    current.Add(value.ToString());
                    value.Clear();
                    break;
                }
                case '\r':
                    break;
                case '\n':
                {
                    current.Add(value.ToString());
                    value.Clear();
                    lines.Add(current);
                    current = new List<string>();
                    break;
                }
                default:
                    value.Append(character);
                    break;
            }
        }

        if (value.Length > 0 || current.Count > 0)
        {
            current.Add(value.ToString());
            lines.Add(current);
        }

        return lines;
    }
}
