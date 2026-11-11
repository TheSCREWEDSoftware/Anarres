namespace Anarres.Core.Dbc;

// How one field of a record is stored in a .dbc file
public enum DbcColumnType
{
    Int32,
    UInt32,
    Float,
    String,
    Byte,
    SByte,
    Int16,
    UInt16
}

public static class DbcColumnTypes
{
    public static int Size(DbcColumnType type)
    {
        switch (type)
        {
            case DbcColumnType.Byte:
            case DbcColumnType.SByte:
                return 1;
            case DbcColumnType.Int16:
            case DbcColumnType.UInt16:
                return 2;
            default:
                return 4;
        }
    }

    public static bool IsInteger(DbcColumnType type)
    {
        return type is not (DbcColumnType.Float or DbcColumnType.String);
    }

    public static bool IsSigned(DbcColumnType type)
    {
        return type is DbcColumnType.Int32 or DbcColumnType.SByte or DbcColumnType.Int16;
    }
}
