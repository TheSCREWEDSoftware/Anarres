namespace Anarres.Core.Mpq;

// The hashing and table encryption every MPQ archive uses
public static class MpqCrypto
{
    public const uint HASH_TABLE_OFFSET = 0;
    public const uint HASH_NAME_A = 1;
    public const uint HASH_NAME_B = 2;
    public const uint HASH_FILE_KEY = 3;

    private const int TABLE_SIZE = 0x500;
    private const int TABLE_ROW = 0x100;
    private const int KEY_ROW = 0x400;
    private const uint SEED = 0x00100001;
    private const uint SEED_MULTIPLIER = 125;
    private const uint SEED_ADDEND = 3;
    private const uint SEED_MODULUS = 0x2AAAAB;
    private const uint LOW_WORD = 0xFFFF;
    private const uint HASH_SEED_1 = 0x7FED7FED;
    private const uint HASH_SEED_2 = 0xEEEEEEEE;
    private const uint KEY_ADDEND = 0x11111111;
    private const int ROUNDS_PER_ENTRY = 5;

    private static readonly uint[] Table = BuildTable();

    public static uint HashString(string text, uint hashType)
    {
        uint seed1 = HASH_SEED_1;
        uint seed2 = HASH_SEED_2;
        foreach (char character in text)
        {
            uint upper = Upper(character);
            seed1 = Table[hashType * TABLE_ROW + upper] ^ (seed1 + seed2);
            seed2 = upper + seed1 + seed2 + (seed2 << 5) + SEED_ADDEND;
        }

        return seed1;
    }

    public static void Decrypt(Span<uint> data, uint key)
    {
        uint seed = HASH_SEED_2;
        for (int i = 0; i < data.Length; ++i)
        {
            seed += Table[KEY_ROW + (key & 0xFF)];
            uint plain = data[i] ^ (key + seed);
            key = ((~key << 0x15) + KEY_ADDEND) | (key >> 0x0B);
            seed = plain + seed + (seed << 5) + SEED_ADDEND;
            data[i] = plain;
        }
    }

    public static void Encrypt(Span<uint> data, uint key)
    {
        uint seed = HASH_SEED_2;
        for (int i = 0; i < data.Length; ++i)
        {
            seed += Table[KEY_ROW + (key & 0xFF)];
            uint plain = data[i];
            data[i] = plain ^ (key + seed);
            key = ((~key << 0x15) + KEY_ADDEND) | (key >> 0x0B);
            seed = plain + seed + (seed << 5) + SEED_ADDEND;
        }
    }

    // The key a file's sectors are encrypted with, from its name without folders
    public static uint FileKey(string name, uint position, uint size, bool adjusted)
    {
        int slash = name.LastIndexOfAny(new[] { '\\', '/' });
        uint key = HashString(slash < 0 ? name : name[(slash + 1)..], HASH_FILE_KEY);
        return adjusted ? (key + position) ^ size : key;
    }

    // MPQ names ignore case and use backslashes
    private static uint Upper(char character)
    {
        if (character == '/')
            return '\\';
        if (character >= 'a' && character <= 'z')
            return (uint)(character - ('a' - 'A'));
        return (uint)(character & 0xFF);
    }

    private static uint[] BuildTable()
    {
        uint[] table = new uint[TABLE_SIZE];
        uint seed = SEED;
        for (int first = 0; first < TABLE_ROW; ++first)
        {
            int index = first;
            for (int round = 0; round < ROUNDS_PER_ENTRY; ++round)
            {
                seed = (seed * SEED_MULTIPLIER + SEED_ADDEND) % SEED_MODULUS;
                uint high = (seed & LOW_WORD) << 16;
                seed = (seed * SEED_MULTIPLIER + SEED_ADDEND) % SEED_MODULUS;
                uint low = seed & LOW_WORD;
                table[index] = high | low;
                index += TABLE_ROW;
            }
        }

        return table;
    }
}
