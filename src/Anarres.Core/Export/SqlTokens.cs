namespace Anarres.Core.Export;

public enum SqlTokenKind
{
    Plain,
    Keyword,
    Identifier,
    Text,
    Number,
    Comment
}

public readonly record struct SqlToken(SqlTokenKind Kind, string Text);

// Splits SQL into words, `names`, 'text', numbers and comments, for colouring it on screen
public static class SqlTokens
{
    private const char BACKTICK = '`';
    private const char QUOTE = '\'';
    private const char ESCAPE = '\\';

    private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "IS", "NULL", "INSERT", "INTO", "VALUES", "UPDATE",
        "SET", "DELETE", "REPLACE", "CREATE", "TABLE", "DROP", "ALTER", "ADD", "PRIMARY", "KEY", "IF", "EXISTS",
        "ORDER", "BY", "GROUP", "LIMIT", "AS", "ON", "JOIN", "LEFT", "INNER", "BETWEEN", "LIKE", "DEFAULT",
        "UNSIGNED", "INT", "TINYINT", "SMALLINT", "MEDIUMINT", "BIGINT", "FLOAT", "VARCHAR", "TEXT", "START",
        "TRANSACTION", "COMMIT", "ROLLBACK", "USE", "DUPLICATE"
    };

    public static List<SqlToken> Split(string sql)
    {
        List<SqlToken> tokens = new List<SqlToken>();
        int at = 0;
        while (at < sql.Length)
        {
            char character = sql[at];
            int start = at;
            SqlTokenKind kind;
            if (character == '-' && at + 1 < sql.Length && sql[at + 1] == '-')
            {
                at = sql.IndexOf('\n', at) is var end and >= 0 ? end : sql.Length;
                kind = SqlTokenKind.Comment;
            }
            else if (character is BACKTICK or QUOTE)
            {
                at = Closing(sql, at, character);
                kind = character == BACKTICK ? SqlTokenKind.Identifier : SqlTokenKind.Text;
            }
            else if (char.IsDigit(character) ||
                     (character == '-' && at + 1 < sql.Length && char.IsDigit(sql[at + 1]) && StartsValue(tokens)))
            {
                ++at;
                while (at < sql.Length && (char.IsLetterOrDigit(sql[at]) || sql[at] == '.'))
                    ++at;
                kind = SqlTokenKind.Number;
            }
            else if (char.IsLetter(character) || character == '_')
            {
                while (at < sql.Length && (char.IsLetterOrDigit(sql[at]) || sql[at] == '_'))
                    ++at;
                kind = Keywords.Contains(sql[start..at]) ? SqlTokenKind.Keyword : SqlTokenKind.Plain;
            }
            else
            {
                ++at;
                kind = SqlTokenKind.Plain;
            }

            string text = sql[start..at];
            // Plain runs are joined, so punctuation and spaces don't make a run each
            if (kind == SqlTokenKind.Plain && tokens.Count > 0 && tokens[^1].Kind == SqlTokenKind.Plain)
                tokens[^1] = tokens[^1] with { Text = tokens[^1].Text + text };
            else
                tokens.Add(new SqlToken(kind, text));
        }

        return tokens;
    }

    // The end of a `name` or 'text', past its closing mark; doubled or backslashed marks stay inside
    private static int Closing(string sql, int open, char mark)
    {
        int at = open + 1;
        while (at < sql.Length)
        {
            if (sql[at] == ESCAPE && mark == QUOTE)
            {
                at += 2;
                continue;
            }

            if (sql[at] == mark)
            {
                if (at + 1 < sql.Length && sql[at + 1] == mark)
                {
                    at += 2;
                    continue;
                }

                return at + 1;
            }

            ++at;
        }

        return sql.Length;
    }

    // A minus starts a negative number after an operator, comma or bracket, not after a name or number
    private static bool StartsValue(List<SqlToken> tokens)
    {
        for (int i = tokens.Count - 1; i >= 0; --i)
        {
            string trimmed = tokens[i].Text.TrimEnd();
            if (trimmed.Length == 0)
                continue;
            return tokens[i].Kind is SqlTokenKind.Plain or SqlTokenKind.Keyword &&
                   (tokens[i].Kind == SqlTokenKind.Keyword || "(,=<>".Contains(trimmed[^1]));
        }

        return true;
    }
}
