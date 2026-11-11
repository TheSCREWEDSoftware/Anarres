namespace Anarres.Core;

public static class Files
{
    // *.dbc also finds .DBC and *.mpq finds .MPQ on Linux, as on Windows
    public static readonly EnumerationOptions AnyCase = new EnumerationOptions
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false
    };
}
