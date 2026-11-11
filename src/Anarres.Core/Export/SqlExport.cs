using System.Globalization;
using System.Text;
using Anarres.Core.Dbc;
using Anarres.Core.Storage;

namespace Anarres.Core.Export;

// A .sql file of rows for AzerothCore's world database, such as custom spells for spell_dbc
public static class SqlExport
{
    private const int ROWS_PER_INSERT = 200;

    // tableName defaults to the AzerothCore table, such as spell_dbc
    public static string Build(DbcTable table, IReadOnlyList<int> rows, string? tableName = null)
    {
        DbcDefinition definition = table.Definition;
        string name = tableName ?? (definition.Table.Length > 0 ? definition.Table : definition.Name);
        List<(string Name, DbcColumnType Type, int Cell)> columns = SqlDbcStore.SqlColumns(definition);
        string key = Quote(columns[0].Name);

        StringBuilder sql = new StringBuilder();
        sql.Append("-- ").Append(rows.Count).Append(rows.Count == 1 ? " row" : " rows").Append(" from ")
           .Append(definition.File).Append(" for ").Append(name).Append(", written by Anarres\n");
        if (rows.Count == 0)
            return sql.ToString();
        // The text below escapes with backslashes, which servers set to NO_BACKSLASH_ESCAPES would read wrongly
        sql.Append("SET @@SESSION.sql_mode = REPLACE(@@SESSION.sql_mode, 'NO_BACKSLASH_ESCAPES', '');\n");

        List<string> keys = rows.Select(row => Literal(table, row, columns[0])).ToList();
        for (int start = 0; start < keys.Count; start += ROWS_PER_INSERT)
            sql.Append("DELETE FROM ").Append(Quote(name)).Append(" WHERE ").Append(key).Append(" IN (")
               .Append(string.Join(", ", keys.Skip(start).Take(ROWS_PER_INSERT))).Append(");\n");

        string header = "INSERT INTO " + Quote(name) + " (" + string.Join(", ", columns.Select(c => Quote(c.Name))) +
                        ") VALUES\n";
        for (int start = 0; start < rows.Count; start += ROWS_PER_INSERT)
        {
            sql.Append(header);
            int end = Math.Min(rows.Count, start + ROWS_PER_INSERT);
            for (int i = start; i < end; ++i)
            {
                sql.Append('(').Append(string.Join(", ", columns.Select(column => Literal(table, rows[i], column))))
                   .Append(i == end - 1 ? ");\n" : "),\n");
            }
        }

        return sql.ToString();
    }

    public static void Write(string path, DbcTable table, IReadOnlyList<int> rows, string? tableName = null)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        File.WriteAllText(path, Build(table, rows, tableName), new UTF8Encoding(false));
    }

    // A MySQL string literal with the characters that need it escaped
    public static string Text(string value)
    {
        StringBuilder literal = new StringBuilder(value.Length + 2);
        literal.Append('\'');
        foreach (char character in value)
        {
            switch (character)
            {
                case '\'':
                    literal.Append("\\'");
                    break;
                case '\\':
                    literal.Append("\\\\");
                    break;
                case '\0':
                    literal.Append("\\0");
                    break;
                case '\n':
                    literal.Append("\\n");
                    break;
                case '\r':
                    literal.Append("\\r");
                    break;
                case '\u001A':
                    literal.Append("\\Z");
                    break;
                default:
                    literal.Append(character);
                    break;
            }
        }

        return literal.Append('\'').ToString();
    }

    private static string Literal(DbcTable table, int row, (string Name, DbcColumnType Type, int Cell) column)
    {
        if (column.Cell < 0)
            return row.ToString(CultureInfo.InvariantCulture);
        if (column.Type == DbcColumnType.String)
            return Text(table.GetString(row, column.Cell));
        // MySQL has no literal for NaN or infinity
        if (column.Type == DbcColumnType.Float && !float.IsFinite(table.GetFloat(row, column.Cell)))
            return "0";
        return table.GetText(row, column.Cell);
    }

    private static string Quote(string identifier)
    {
        return "`" + identifier.Replace("`", "``") + "`";
    }
}
