namespace Anarres.Core.Settings;

// A known program, or a custom path and arguments with %TARGET_PATH% and the database placeholders (Urras's format)
public sealed class ProgramChoice
{
    public const string TARGET = "%TARGET_PATH%";
    public const string CUSTOM = "Custom";
    public const string NONE = "None";
    public const string SYSTEM = "System default";

    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = TARGET;

    public bool IsCustom => Name == CUSTOM;

    // The arguments with the placeholders filled in; without %TARGET_PATH%, the target goes at the end
    public List<string> ArgumentsFor(string target, IReadOnlyDictionary<string, string>? values = null)
    {
        List<string> arguments = Split(Arguments).Select(part => Fill(part, target, values)).ToList();
        if (!Arguments.Contains(TARGET) && target.Length > 0)
            arguments.Add(target);
        return arguments;
    }

    // Splits on spaces outside double quotes, as a command line does
    public static List<string> Split(string text)
    {
        List<string> parts = new List<string>();
        System.Text.StringBuilder current = new System.Text.StringBuilder();
        bool quoted = false;
        bool any = false;
        foreach (char character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (any)
                    parts.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(character);
                any = true;
            }
        }

        if (any)
            parts.Add(current.ToString());
        return parts;
    }

    private static string Fill(string part, string target, IReadOnlyDictionary<string, string>? values)
    {
        string filled = part.Replace(TARGET, target, StringComparison.Ordinal);
        foreach (KeyValuePair<string, string> value in values ?? new Dictionary<string, string>())
            filled = filled.Replace("%" + value.Key + "%", value.Value, StringComparison.Ordinal);
        return filled;
    }
}
