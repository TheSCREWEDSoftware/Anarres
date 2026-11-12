using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Anarres.Core.Spells;

public sealed record EnumValue(long Value, string Name, string Label, string Description);

// One list of named values, such as spell effects, or of flag bits, such as the Attributes column
public sealed class EnumTable
{
    public EnumTable(string key, bool flags, List<EnumValue> values)
    {
        Key = key;
        Flags = flags;
        Values = values;
    }

    public string Key { get; }

    public bool Flags { get; }

    public List<EnumValue> Values { get; }

    // "School Damage (2)", or "Unknown (999)" for a value no list has
    public string Describe(long value)
    {
        if (Flags)
        {
            List<string> set = Values.Where(entry => (value & entry.Value) != 0).Select(entry => entry.Label)
                                     .ToList();
            long known = Values.Aggregate(0L, (mask, entry) => mask | entry.Value);
            if ((value & ~known) != 0)
                set.Add("0x" + (value & ~known).ToString("X", CultureInfo.InvariantCulture));
            return set.Count == 0 ? "None" : string.Join(", ", set);
        }

        EnumValue? found = Values.FirstOrDefault(entry => entry.Value == value);
        return (found?.Label ?? "Unknown") + " (" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    // The bit number of a one-bit value; a value of several bits is shown as itself
    private static string Bit(long value)
    {
        return value > 0 && (value & (value - 1)) == 0
                   ? System.Numerics.BitOperations.Log2((ulong)value).ToString(CultureInfo.InvariantCulture)
                   : value.ToString(CultureInfo.InvariantCulture);
    }

    // Just the names, for beside a value: "Orc, Dwarf", or the one name; empty for 0 or an unnamed value
    public string Names(long value)
    {
        if (Flags)
        {
            long known = Values.Aggregate(0L, (mask, entry) => mask | entry.Value);
            if (value != 0 && Values.Count > 1 && (value & known) == known)
                return "All";
            return value == 0 ? string.Empty : Describe(value);
        }
        return Values.FirstOrDefault(entry => entry.Value == value)?.Label ?? string.Empty;
    }

    // One line per set bit, "1 - Orc (2)": bit, name, then the bit's value; a plain value gives "2 - Orc"
    public string Lines(long value)
    {
        if (!Flags)
        {
            EnumValue? found = Values.FirstOrDefault(entry => entry.Value == value);
            return value.ToString(CultureInfo.InvariantCulture) + " - " + (found?.Label ?? "Unknown");
        }

        List<string> lines = Values.Where(entry => (value & entry.Value) != 0)
                                   .Select(entry => Bit(entry.Value) + " - " + entry.Label + " (" +
                                                    entry.Value.ToString(CultureInfo.InvariantCulture) + ")")
                                   .ToList();
        long known = Values.Aggregate(0L, (mask, entry) => mask | entry.Value);
        for (int bit = 0; bit < 64; ++bit)
        {
            long mask = 1L << bit;
            if ((value & ~known & mask) != 0)
                lines.Add(bit + " - Unknown bit (" + mask.ToString(CultureInfo.InvariantCulture) + ")");
        }

        return lines.Count == 0 ? "None" : string.Join("\n", lines);
    }
}

// The names AzerothCore gives spell effects, auras, targets, attribute bits and the rest
public static class SpellEnums
{
    public const string RESOURCE = "Anarres.Core.Definitions.spell-enums-3.3.5a.json";

    public const string EFFECT = "Effect";
    public const string AURA = "Aura";
    public const string TARGET = "Target";
    public const string MECHANIC = "Mechanic";
    public const string DISPEL = "Dispel";
    public const string POWER = "Power";
    public const string SCHOOL = "School";
    public const string AURA_STATE = "AuraState";
    public const string DAMAGE_CLASS = "DamageClass";
    public const string PREVENTION_TYPE = "PreventionType";
    public const string SPELL_FAMILY = "SpellFamily";
    public const string CAST_TARGET_FLAGS = "CastTargetFlags";
    public const string INTERRUPT_FLAGS = "InterruptFlags";
    public const string AURA_INTERRUPT_FLAGS = "AuraInterruptFlags";
    public const string CHANNEL_INTERRUPT_FLAGS = "ChannelInterruptFlags";
    public const string PROC_FLAGS = "ProcFlags";
    public const string SHAPESHIFT_FORM = "ShapeshiftForm";
    public const string CREATURE_TYPE = "CreatureType";

    // Attributes, AttributesEx ... AttributesEx7: the keys are the Spell.dbc column names
    public static readonly string[] AttributeColumns =
    {
        "Attributes", "AttributesEx", "AttributesEx2", "AttributesEx3", "AttributesEx4", "AttributesEx5",
        "AttributesEx6", "AttributesEx7"
    };

    private static readonly Lazy<Dictionary<string, EnumTable>> Tables =
        new Lazy<Dictionary<string, EnumTable>>(Load);

    public static IReadOnlyDictionary<string, EnumTable> All => Tables.Value;

    public static EnumTable Get(string key)
    {
        return Tables.Value.TryGetValue(key, out EnumTable? table)
                   ? table
                   : new EnumTable(key, false, new List<EnumValue>());
    }

    // Mask bits for shapeshift forms: form N is bit N - 1
    public static EnumTable StanceMask()
    {
        List<EnumValue> bits = Get(SHAPESHIFT_FORM).Values.Where(form => form.Value > 0 && form.Value <= 32)
                                                   .Select(form => form with { Value = 1L << (int)(form.Value - 1) })
                                                   .ToList();
        return new EnumTable("Stances", true, bits);
    }

    // Mask bits for creature types: type N is bit N - 1
    public static EnumTable CreatureTypeMask()
    {
        List<EnumValue> bits = Get(CREATURE_TYPE).Values.Where(type => type.Value > 0 && type.Value <= 32)
                                                 .Select(type => type with { Value = 1L << (int)(type.Value - 1) })
                                                 .ToList();
        return new EnumTable("CreatureTypes", true, bits);
    }

    private static Dictionary<string, EnumTable> Load()
    {
        Dictionary<string, EnumTable> tables = new Dictionary<string, EnumTable>(StringComparer.Ordinal);
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(RESOURCE);
        if (stream == null)
            return tables;

        using JsonDocument document = JsonDocument.Parse(stream);
        foreach (JsonProperty entry in document.RootElement.GetProperty("Enums").EnumerateObject())
        {
            bool flags = entry.Value.GetProperty("Flags").GetBoolean();
            List<EnumValue> values = entry.Value.GetProperty("Values").EnumerateArray()
                                          .Select(value => new EnumValue(value.GetProperty("Value").GetInt64(),
                                                                         value.GetProperty("Name").GetString() ?? "",
                                                                         Words(value.GetProperty("Label").GetString()),
                                                                         Words(value.GetProperty("Description")
                                                                                    .GetString())))
                                          .ToList();
            tables[entry.Name] = new EnumTable(entry.Name, flags, values);
        }

        return tables;
    }

    // AzerothCore's notes say "server & client"; the interface spells it out
    private static string Words(string? text)
    {
        return (text ?? string.Empty).Replace(" & ", " and ", StringComparison.Ordinal);
    }
}
