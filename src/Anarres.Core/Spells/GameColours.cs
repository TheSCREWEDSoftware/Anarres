namespace Anarres.Core.Spells;

// The colours the game's own interface uses: combat text by school, and debuff borders by dispel type
public static class GameColours
{
    private const int SCHOOLS = 7;

    // Physical, Holy, Fire, Nature, Frost, Shadow, Arcane: bit 0 to 6 of a school mask
    private static readonly string[] Schools =
    {
        "#FFFF00", "#FFE680", "#FF8000", "#4DFF4D", "#80FFFF", "#8080FF", "#FF80FF"
    };

    // Magic, Curse, Disease, Poison: dispel types 1 to 4
    private static readonly string[] DispelTypes = { "#3399FF", "#9900FF", "#996600", "#009900" };

    // Item names by quality: poor, common, uncommon, rare, epic, legendary, artifact, heirloom
    private static readonly string[] Qualities =
    {
        "#9D9D9D", "#FFFFFF", "#1EFF00", "#0070DD", "#A335EE", "#FF8000", "#E6CC80", "#E6CC80"
    };

    public static string? ItemQuality(int quality)
    {
        return quality >= 0 && quality < Qualities.Length ? Qualities[quality] : null;
    }

    // The colour of a mask's lowest school, or null for none
    public static string? School(uint mask)
    {
        for (int bit = 0; bit < SCHOOLS; ++bit)
        {
            if ((mask & (1u << bit)) != 0)
                return Schools[bit];
        }

        return null;
    }

    // The debuff border of a dispel type, or null for types the game doesn't colour
    public static string? DispelType(uint type)
    {
        return type >= 1 && type <= DispelTypes.Length ? DispelTypes[type - 1] : null;
    }
}
