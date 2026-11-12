namespace Anarres.Core;

// WoW money: the server and the tables count copper; 100 copper make a silver, 100 silver a gold
public static class Money
{
    public const uint COPPER_PER_SILVER = 100;
    public const uint SILVER_PER_GOLD = 100;
    public const uint COPPER_PER_GOLD = COPPER_PER_SILVER * SILVER_PER_GOLD;

    // Silver and copper past 99 carry over, as typing 150 copper means 1 silver 50 copper
    public static uint ToCopper(uint gold, uint silver, uint copper)
    {
        ulong total = (ulong)gold * COPPER_PER_GOLD + (ulong)silver * COPPER_PER_SILVER + copper;
        return total > uint.MaxValue ? uint.MaxValue : (uint)total;
    }

    public static (uint Gold, uint Silver, uint Copper) Split(uint copper)
    {
        return (copper / COPPER_PER_GOLD, copper / COPPER_PER_SILVER % SILVER_PER_GOLD, copper % COPPER_PER_SILVER);
    }

    // Table columns that hold copper amounts
    private static readonly HashSet<string> Columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "BankBagSlotPrices.dbc:Cost", "StableSlotPrices.dbc:Cost", "TaxiPath.dbc:Cost", "Package.dbc:Cost",
        "SkillTiers.dbc:Cost"
    };

    public static bool IsColumn(string file, string column)
    {
        string bare = System.Text.RegularExpressions.Regex.Replace(column, @"_\d+$", string.Empty);
        return Columns.Contains(Path.GetFileName(file) + ":" + bare);
    }

    // "1g 2s 50c", leaving out what's zero; "0c" for nothing
    public static string Text(uint copper)
    {
        (uint gold, uint silver, uint rest) = Split(copper);
        List<string> parts = new List<string>();
        if (gold > 0)
            parts.Add(gold + "g");
        if (silver > 0)
            parts.Add(silver + "s");
        if (rest > 0 || parts.Count == 0)
            parts.Add(rest + "c");
        return string.Join(" ", parts);
    }
}
