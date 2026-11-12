using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Anarres.Core.Dbc;

namespace Anarres.Core.Spells;

// Lookups the tooltip needs from other tables: durations, radii, ranges and other spells
public sealed class SpellLookups
{
    public DbcTable? Spells { get; set; }

    public DbcTable? Durations { get; set; }

    public DbcTable? Radii { get; set; }

    public DbcTable? Ranges { get; set; }

    // SpellDescriptionVariables.dbc: named values such as $<mult> that descriptions use
    public DbcTable? Variables { get; set; }

    // "$mult=${$m1*2}" lines of a SpellDescriptionVariables row, by name
    public Dictionary<string, string> VariablesOf(uint index)
    {
        Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DbcTable? table = Variables;
        int row = index == 0 || table == null ? -1 : table.FindRow(index);
        if (row < 0)
            return found;
        int column = table!.Definition.Columns.FindIndex(entry => entry.Type == DbcColumnType.String);
        if (column < 0)
            return found;
        foreach (string line in table.GetString(row, column).Split('\n', '\r'))
        {
            int equals = line.IndexOf('=');
            if (line.StartsWith('$') && equals > 1)
                found[line[1..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        return found;
    }

    // Seconds of a SpellDuration.dbc row, or null when unknown
    public int? DurationMilliseconds(uint index)
    {
        return Durations is { } table && table.FindRow(index) is var row and >= 0 ? table.GetInt(row, 1) : null;
    }

    public float? Radius(uint index)
    {
        return Radii is { } table && table.FindRow(index) is var row and >= 0 ? table.GetFloat(row, 1) : null;
    }

    public float? RangeMax(uint index)
    {
        // RangeMax_1 is the hostile range, the one tooltips show
        const int RANGE_MAX_COLUMN = 3;
        return Ranges is { } table && table.FindRow(index) is var row and >= 0
                   ? table.GetFloat(row, RANGE_MAX_COLUMN)
                   : null;
    }
}

// The caster the preview assumes: a level (0 for the spell's own) and the spells (ticked talents) $?s12345 asks
public sealed class TooltipContext
{
    public static TooltipContext Default { get; } = new TooltipContext();

    public int Level { get; init; }

    public IReadOnlySet<uint> Known { get; init; } = new HashSet<uint>();
}

// Fills in the $ tokens of a spell's description as the client shows them, for a preview while editing
public static partial class SpellText
{
    private const int EFFECTS = 3;
    private const double MILLISECONDS_PER_SECOND = 1000.0;
    private const int SECONDS_PER_MINUTE = 60;
    private const int SECONDS_PER_HOUR = 3600;

    [GeneratedRegex(@"\$(?<choice>[lLgG])(?<a>[^:;$]*):(?<b>[^;$]*);" +
                    @"|\$(?<spell>\d+)?(?<token>[a-zA-Z])(?<index>[1-3])?")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\$\{(?<expression>[^}]*)\}")]
    private static partial Regex ExpressionRegex();

    [GeneratedRegex(@"\$<\w+>")]
    private static partial Regex UnknownVariableRegex();

    public static string Render(SpellRecord spell, string text, SpellLookups lookups, TooltipContext? context = null)
    {
        context ??= TooltipContext.Default;
        text = ExpandVariables(text, lookups.VariablesOf(spell.U("SpellDescriptionVariableID")));
        text = Nest(ResolveConditions(text, context.Known));
        string expanded = ExpressionRegex().Replace(text, match =>
        {
            // A variable no SpellDescriptionVariables row defines counts as 1, so the rest still adds up
            string known = UnknownVariableRegex().Replace(match.Groups["expression"].Value, "1");
            string inner = TokenRegex().Replace(known, token => Value(spell, token, lookups, true, context));
            return Evaluate(inner) is { } value ? Number(value) : match.Value;
        });

        int lastNumber = 0;
        StringBuilder output = new StringBuilder();
        int position = 0;
        foreach (Match match in TokenRegex().Matches(expanded))
        {
            output.Append(expanded, position, match.Index - position);
            position = match.Index + match.Length;
            if (match.Groups["choice"].Success)
            {
                // $lsingular:plural; follows the last number; $gmale:female; is shown as both
                bool lower = match.Groups["choice"].Value is "l" or "L";
                output.Append(lower ? (lastNumber == 1 ? match.Groups["a"].Value : match.Groups["b"].Value)
                                    : match.Groups["a"].Value + "/" + match.Groups["b"].Value);
                continue;
            }

            string value = Value(spell, match, lookups, false, context);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                lastNumber = number;
            output.Append(value);
        }

        output.Append(expanded, position, expanded.Length - position);
        return output.ToString().Replace("$$", "$");
    }

    // The spells $?s12345 conditions ask about, so the preview can offer them as talents to tick
    public static List<uint> AskedSpells(SpellRecord spell, string text, SpellLookups lookups)
    {
        text = ExpandVariables(text, lookups.VariablesOf(spell.U("SpellDescriptionVariableID")));
        return ConditionRegex().Matches(text)
                               .SelectMany(match => SpellIdRegex().Matches(match.Groups["condition"].Value))
                               .Select(id => uint.Parse(id.Groups["id"].Value, CultureInfo.InvariantCulture))
                               .Distinct().ToList();
    }

    [GeneratedRegex(@"\$\?(?<condition>[^\[]*)\[")]
    private static partial Regex ConditionRegex();

    [GeneratedRegex(@"[sa](?<id>\d+)")]
    private static partial Regex SpellIdRegex();

    // $?s12345[with][without], with !, | and &, and ?s678[...] chains before the last branch; a12345 likewise
    private static string ResolveConditions(string text, IReadOnlySet<uint> known)
    {
        const int MAX_PASSES = 256;
        for (int pass = 0; pass < MAX_PASSES; ++pass)
        {
            int start = text.IndexOf("$?", StringComparison.Ordinal);
            if (start < 0)
                return text;
            int conditionStart = start + 2;
            string? chosen = null;
            int end = conditionStart;
            while (true)
            {
                int open = text.IndexOf('[', end);
                int close = open < 0 ? -1 : Closing(text, open);
                if (close < 0)
                    return text;
                string condition = text[end..open];
                string branch = text[(open + 1)..close];
                if (chosen == null && Holds(condition, known))
                    chosen = branch;
                end = close + 1;
                // ?s678[...] goes on to the next condition; [...] is the last branch, for none of them
                if (end < text.Length && text[end] == '?')
                {
                    ++end;
                    continue;
                }

                if (end < text.Length && text[end] == '[')
                {
                    int last = Closing(text, end);
                    if (last < 0)
                        return text;
                    chosen ??= text[(end + 1)..last];
                    end = last + 1;
                }

                break;
            }

            text = text[..start] + (chosen ?? string.Empty) + text[end..];
        }

        return text;
    }

    private static bool Holds(string condition, IReadOnlySet<uint> known)
    {
        bool any = condition.Contains('|');
        IEnumerable<bool> parts = condition.Split('|', '&').Select(part =>
        {
            string trimmed = part.Trim().Trim('(', ')');
            bool negated = trimmed.StartsWith('!');
            Match id = SpellIdRegex().Match(trimmed);
            bool has = id.Success && known.Contains(uint.Parse(id.Groups["id"].Value, CultureInfo.InvariantCulture));
            return negated ? !has : has;
        });
        return any ? parts.Any(part => part) : parts.All(part => part);
    }

    private static int Closing(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; ++i)
        {
            if (text[i] == '[')
                ++depth;
            else if (text[i] == ']' && --depth == 0)
                return i;
        }

        return -1;
    }

    // ${...} inside another ${...} becomes brackets, so the outer sum can be worked out in one go
    private static string Nest(string text)
    {
        StringBuilder output = new StringBuilder(text.Length);
        Stack<bool> braces = new Stack<bool>();
        for (int i = 0; i < text.Length; ++i)
        {
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                bool inner = braces.Count > 0;
                braces.Push(inner);
                output.Append(inner ? "(" : "${");
                ++i;
                continue;
            }

            if (text[i] == '}' && braces.Count > 0)
            {
                output.Append(braces.Pop() ? ")" : "}");
                continue;
            }

            output.Append(text[i]);
        }

        return output.ToString();
    }

    // $<name> becomes its value; inside ${...} a value's own ${...} becomes brackets so the sums nest
    private static string ExpandVariables(string text, Dictionary<string, string> variables)
    {
        const int MAX_DEPTH = 24;
        if (variables.Count == 0)
            return text;
        for (int depth = 0; depth < MAX_DEPTH && text.Contains("$<", StringComparison.Ordinal); ++depth)
        {
            StringBuilder output = new StringBuilder();
            int braces = 0;
            for (int i = 0; i < text.Length; ++i)
            {
                if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
                    ++braces;
                else if (text[i] == '}' && braces > 0)
                    --braces;
                int close = text[i] == '$' && i + 1 < text.Length && text[i + 1] == '<' ? text.IndexOf('>', i) : -1;
                if (close > 0 && variables.TryGetValue(text[(i + 2)..close], out string? value))
                {
                    bool wrapped = value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}');
                    output.Append(braces > 0 && wrapped ? "(" + value[2..^1] + ")" : value);
                    i = close;
                    continue;
                }

                output.Append(text[i]);
            }

            text = output.ToString();
        }

        return text;
    }

    private static string Value(SpellRecord spell, Match match, SpellLookups lookups, bool numeric,
                                TooltipContext context)
    {
        SpellRecord source = spell;
        if (match.Groups["spell"].Success && lookups.Spells is { } spells &&
            uint.TryParse(match.Groups["spell"].Value, CultureInfo.InvariantCulture, out uint other))
        {
            int row = spells.FindRow(other);
            if (row < 0)
                return match.Value;
            source = new SpellRecord(spells, row);
        }

        int effect = match.Groups["index"].Success ? int.Parse(match.Groups["index"].Value,
                                                                CultureInfo.InvariantCulture) - 1 : 0;
        effect = Math.Clamp(effect, 0, EFFECTS - 1);
        string token = match.Groups["token"].Value;
        switch (token.ToLowerInvariant())
        {
            case "s":
            case "m":
            {
                // Base points plus one die; with more sides the tooltip shows the whole range
                int scaled = source.EffectBasePoints(effect) + LevelBonus(source, effect, context);
                int minimum = scaled + 1;
                int maximum = scaled + source.EffectDieSides(effect);
                // Tooltips show amounts without their sign: a -40% slow reads "40%"
                if (!numeric && maximum <= 0)
                    (minimum, maximum) = (Math.Abs(maximum), Math.Abs(minimum));
                // $m is the smallest value and $M the largest; $s shows the range when there is one
                if (token == "M")
                    return Number(maximum);
                bool spread = token is "s" or "S" && maximum > minimum && !numeric;
                return spread ? Number(minimum) + " to " + Number(maximum) : Number(minimum);
            }
            case "o":
            {
                int? duration = lookups.DurationMilliseconds(source.DurationIndex);
                uint period = source.EffectAuraPeriod(effect);
                double ticks = duration is > 0 && period > 0 ? Math.Floor((double)duration.Value / period) : 0;
                return Number((source.EffectBasePoints(effect) + LevelBonus(source, effect, context) + 1) * ticks);
            }
            case "t":
                return Number(source.EffectAuraPeriod(effect) / MILLISECONDS_PER_SECOND);
            case "d":
            {
                int? duration = lookups.DurationMilliseconds(source.DurationIndex);
                if (duration == null)
                    return match.Value;
                return numeric ? Number(duration.Value / MILLISECONDS_PER_SECOND) : Duration(duration.Value);
            }
            case "a":
                return lookups.Radius(source.EffectRadiusIndex(effect)) is { } radius ? Number(radius) : match.Value;
            case "r":
                return lookups.RangeMax(source.RangeIndex) is { } range ? Number(range) : match.Value;
            case "n":
                return Number(source.ProcCharges);
            case "h":
                return Number(source.ProcChance);
            case "u":
                return Number(source.StackAmount);
            case "x":
                return Number(source.EffectChainTargets(effect));
            case "e":
                return Number(source.EffectMultipleValue(effect));
            case "q":
                return Number(source.EffectMiscValue(effect));
            case "b":
                return Number(source.EffectPointsPerCombo(effect));
            case "i":
                return Number(source.MaxTargets);
            default:
                return match.Value;
        }
    }

    private static string Duration(int milliseconds)
    {
        int seconds = (int)(milliseconds / MILLISECONDS_PER_SECOND);
        if (milliseconds < 0)
            return "until cancelled";
        if (seconds >= SECONDS_PER_HOUR && seconds % SECONDS_PER_HOUR == 0)
            return Number(seconds / SECONDS_PER_HOUR) + " hr";
        if (seconds >= SECONDS_PER_MINUTE && seconds % SECONDS_PER_MINUTE == 0)
            return Number(seconds / SECONDS_PER_MINUTE) + " min";
        return Number(milliseconds / MILLISECONDS_PER_SECOND) + " sec";
    }

    // As AzerothCore's SpellEffectInfo::CalcValue: per-level points times levels above max(base, spell level)
    private static int LevelBonus(SpellRecord spell, int effect, TooltipContext context)
    {
        float perLevel = spell.F(SpellRecord.EffectColumn("EffectRealPointsPerLevel", effect));
        if (perLevel == 0 || context.Level <= 0)
            return 0;
        int level = context.Level;
        int maxLevel = spell.I("MaxLevel");
        if (maxLevel > 0 && level > maxLevel)
            level = maxLevel;
        else if (level < spell.I("BaseLevel"))
            level = spell.I("BaseLevel");
        return (int)((level - Math.Max(spell.I("BaseLevel"), spell.I("SpellLevel"))) * perLevel);
    }

    private static string Number(double value)
    {
        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    // + - * / and brackets over plain numbers; null when the text is anything else
    private static double? Evaluate(string expression)
    {
        try
        {
            System.Data.DataTable table = new System.Data.DataTable { Locale = CultureInfo.InvariantCulture };
            object? result = table.Compute(expression.Replace(",", "."), null);
            return Convert.ToDouble(result, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is System.Data.EvaluateException or
                                              System.Data.SyntaxErrorException or InvalidCastException or
                                              FormatException or OverflowException)
        {
            return null;
        }
    }
}
