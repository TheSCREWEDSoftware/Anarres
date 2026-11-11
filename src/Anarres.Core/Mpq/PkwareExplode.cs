namespace Anarres.Core.Mpq;

// Decompresses PKWARE DCL ("implode") data, written from the format Mark Adler's blast.c documents
public static class PkwareExplode
{
    private const int MAX_BITS = 13;
    private const int END_OF_STREAM = 519;
    private const int MIN_DICTIONARY = 4;
    private const int MAX_DICTIONARY = 6;
    private const int LITERAL_SYMBOLS = 256;
    private const int LENGTH_SYMBOLS = 16;
    private const int DISTANCE_SYMBOLS = 64;
    private const int BYTE_BITS = 8;
    private const int SHORT_DISTANCE_BITS = 2;

    // Code lengths in the compact form: high nibble is repeats minus one, low nibble the length
    private static readonly byte[] LiteralLengths =
    {
        11, 124, 8, 7, 28, 7, 188, 13, 76, 4, 10, 8, 12, 10, 12, 10, 8, 23, 8, 9, 7, 6, 7, 8, 7, 6, 55, 8, 23, 24,
        12, 11, 7, 9, 11, 12, 6, 7, 22, 5, 7, 24, 6, 11, 9, 6, 7, 22, 7, 11, 38, 7, 9, 8, 25, 11, 8, 11, 9, 12,
        8, 12, 5, 38, 5, 38, 5, 11, 7, 5, 6, 21, 6, 10, 53, 8, 7, 24, 10, 27, 44, 253, 253, 253, 252, 252, 252,
        13, 12, 45, 12, 45, 12, 61, 12, 45, 44, 173
    };

    private static readonly byte[] LengthLengths = { 2, 35, 36, 53, 38, 23 };

    private static readonly byte[] DistanceLengths = { 2, 20, 53, 230, 247, 151, 248 };

    private static readonly int[] LengthBase = { 3, 2, 4, 5, 6, 7, 8, 9, 10, 12, 16, 24, 40, 72, 136, 264 };

    private static readonly int[] LengthExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    private static readonly Huffman LiteralCode = new Huffman(LiteralLengths, LITERAL_SYMBOLS);
    private static readonly Huffman LengthCode = new Huffman(LengthLengths, LENGTH_SYMBOLS);
    private static readonly Huffman DistanceCode = new Huffman(DistanceLengths, DISTANCE_SYMBOLS);

    public static byte[] Decompress(ReadOnlySpan<byte> input, int expectedSize)
    {
        BitReader reader = new BitReader(input.ToArray());
        int codedLiterals = reader.Bits(BYTE_BITS);
        int dictionary = reader.Bits(BYTE_BITS);
        if (codedLiterals > 1 || dictionary < MIN_DICTIONARY || dictionary > MAX_DICTIONARY)
            throw new InvalidDataException("The PKWARE-compressed data has a header that isn't valid.");

        List<byte> output = new List<byte>(expectedSize);
        while (true)
        {
            if (reader.Bits(1) == 1)
            {
                int symbol = LengthCode.Decode(reader);
                int length = LengthBase[symbol] + reader.Bits(LengthExtra[symbol]);
                if (length == END_OF_STREAM)
                    break;

                int shift = length == 2 ? SHORT_DISTANCE_BITS : dictionary;
                int distance = (DistanceCode.Decode(reader) << shift) + reader.Bits(shift) + 1;
                if (distance > output.Count)
                    throw new InvalidDataException("The PKWARE-compressed data refers back past its start.");
                int from = output.Count - distance;
                for (int i = 0; i < length; ++i)
                    output.Add(output[from + i]);
            }
            else
                output.Add((byte)(codedLiterals == 1 ? LiteralCode.Decode(reader) : reader.Bits(BYTE_BITS)));

            if (output.Count > expectedSize)
                throw new InvalidDataException("The PKWARE-compressed data is longer than expected.");
        }

        return output.ToArray();
    }

    // Reads bits starting from the lowest bit of each byte
    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _position;
        private int _buffer;
        private int _count;

        public BitReader(byte[] data)
        {
            _data = data;
        }

        public int Bits(int need)
        {
            while (_count < need)
            {
                if (_position >= _data.Length)
                    throw new InvalidDataException("The PKWARE-compressed data ended early.");
                _buffer |= _data[_position++] << _count;
                _count += BYTE_BITS;
            }

            int value = _buffer & ((1 << need) - 1);
            _buffer >>= need;
            _count -= need;
            return value;
        }
    }

    // A canonical Huffman code whose bits are stored inverted
    private sealed class Huffman
    {
        private readonly int[] _count = new int[MAX_BITS + 1];
        private readonly int[] _symbol;

        public Huffman(byte[] compact, int symbols)
        {
            int[] lengths = new int[symbols];
            int next = 0;
            foreach (byte entry in compact)
            {
                int repeat = (entry >> 4) + 1;
                for (int i = 0; i < repeat; ++i)
                    lengths[next++] = entry & 0x0F;
            }

            foreach (int length in lengths)
                ++_count[length];

            int[] offsets = new int[MAX_BITS + 1];
            for (int length = 1; length < MAX_BITS; ++length)
                offsets[length + 1] = offsets[length] + _count[length];

            _symbol = new int[symbols];
            for (int symbol = 0; symbol < symbols; ++symbol)
            {
                if (lengths[symbol] != 0)
                    _symbol[offsets[lengths[symbol]]++] = symbol;
            }
        }

        public int Decode(BitReader reader)
        {
            int code = 0;
            int first = 0;
            int index = 0;
            for (int length = 1; length <= MAX_BITS; ++length)
            {
                code |= reader.Bits(1) ^ 1;
                int count = _count[length];
                if (code < first + count)
                    return _symbol[index + (code - first)];
                index += count;
                first += count;
                first <<= 1;
                code <<= 1;
            }

            throw new InvalidDataException("The PKWARE-compressed data has a code that isn't valid.");
        }
    }
}
