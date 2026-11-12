using System.Globalization;
using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

public enum BulkOperation
{
    Set,
    Add,
    Multiply,
    TurnBitsOn,
    TurnBitsOff,
    ReplaceText
}

// One cell a bulk edit changes, before and after
public sealed record BulkChange(int Row, uint Key, string Old, string New);

// Changes one column of many rows at once, such as every spell of a family's global cooldown
public static class BulkEdit
{
    // What the edit would do, without doing it; replacement is only for ReplaceText
    public static List<BulkChange> Preview(DbcTable table, IEnumerable<int> rows, int column, BulkOperation operation,
                                           string value, string replacement = "")
    {
        List<BulkChange> changes = new List<BulkChange>();
        DbcColumnType type = table.Definition.Columns[column].Type;
        foreach (int row in rows)
        {
            string old = table.GetText(row, column);
            string? changed = Compute(type, table, row, column, operation, value, replacement);
            if (changed != null && changed != old)
                changes.Add(new BulkChange(row, table.Key(row), old, changed));
        }

        return changes;
    }

    // Writes the changes as one undo step; returns how many cells took the new value
    public static int Apply(DbcTable table, int column, IEnumerable<BulkChange> changes)
    {
        table.BreakUndoStep();
        int done = 0;
        foreach (BulkChange change in changes)
        {
            if (table.TrySetText(change.Row, column, change.New, out _))
                ++done;
        }

        table.BreakUndoStep();
        return done;
    }

    // The new text for a cell, or null when the operation doesn't fit the column or the value
    private static string? Compute(DbcColumnType type, DbcTable table, int row, int column, BulkOperation operation,
                                   string value, string replacement)
    {
        CultureInfo invariant = CultureInfo.InvariantCulture;
        if (type == DbcColumnType.String)
        {
            string text = table.GetString(row, column);
            return operation switch
            {
                BulkOperation.Set => value,
                BulkOperation.ReplaceText when value.Length > 0 => text.Replace(value, replacement,
                                                                                StringComparison.Ordinal),
                _ => null
            };
        }

        if (type == DbcColumnType.Float)
        {
            if (!double.TryParse(value, NumberStyles.Float, invariant, out double number))
                return null;
            double current = table.GetFloat(row, column);
            return operation switch
            {
                BulkOperation.Set => number.ToString(invariant),
                BulkOperation.Add => (current + number).ToString(invariant),
                BulkOperation.Multiply => (current * number).ToString(invariant),
                _ => null
            };
        }

        if (!TryInteger(value, out long amount))
            return null;
        long now = DbcColumnTypes.IsSigned(type) ? table.GetInt(row, column) : table.GetRaw(row, column);
        long result = operation switch
        {
            BulkOperation.Set => amount,
            BulkOperation.Add => now + amount,
            BulkOperation.Multiply => now * amount,
            BulkOperation.TurnBitsOn => now | amount,
            BulkOperation.TurnBitsOff => now & ~amount,
            _ => long.MinValue
        };
        return result == long.MinValue ? null : result.ToString(invariant);
    }

    // Decimal or 0x hex
    private static bool TryInteger(string text, out long value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
