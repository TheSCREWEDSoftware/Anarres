namespace Anarres.Core.Models;

// How a track moves between its keys
public enum M2Interpolation
{
    None = 0,
    Linear = 1,
    Hermite = 2,
    Bezier = 3
}

// An animated value: key times (ms) and values per animation, read on first use since .anim files hold some
public sealed class M2Track<T>
{
    public const int SIZE = 20;

    private const int SPLINE_VALUES = 3; // a value then its in and out tangents

    private readonly M2Array[] _times;
    private readonly M2Array[] _values;
    private readonly int _stride;
    private readonly Func<ByteCursor, T> _read;
    private readonly Func<int, byte[]?> _source;
    private readonly (uint[] Times, T[] Values)?[] _keys;

    private M2Track(M2Interpolation interpolation, short globalSequence, M2Array[] times, M2Array[] values,
                    int stride, Func<ByteCursor, T> read, Func<int, byte[]?> source)
    {
        Interpolation = interpolation;
        GlobalSequence = globalSequence;
        _times = times;
        _values = values;
        _stride = stride;
        _read = read;
        _source = source;
        _keys = new (uint[], T[])?[Math.Min(times.Length, values.Length)];
    }

    public M2Interpolation Interpolation { get; }

    // The global loop this track follows instead of the animation, or -1
    public short GlobalSequence { get; }

    public int AnimationCount => _keys.Length;

    public bool IsEmpty => _times.All(array => array.Count == 0);

    // source(i) is the file holding animation i's keys: the model itself, its .anim file, or null when missing
    public static M2Track<T> Read(ByteCursor cursor, int stride, Func<ByteCursor, T> read,
                                  Func<int, byte[]?> source)
    {
        M2Interpolation interpolation = (M2Interpolation)cursor.U16();
        short global = cursor.I16();
        M2Array times = cursor.Array();
        M2Array values = cursor.Array();
        return new M2Track<T>(interpolation, global, cursor.Items(times, M2Array.SIZE, item => item.Array()),
                              cursor.Items(values, M2Array.SIZE, item => item.Array()), stride, read, source);
    }

    // The keys of one animation, or of the only list when the track follows a global loop
    public (uint[] Times, T[] Values) Keys(int animation)
    {
        int index = GlobalSequence >= 0 ? 0 : animation;
        if (index < 0 || index >= _keys.Length)
            return (Array.Empty<uint>(), Array.Empty<T>());
        if (_keys[index] is { } cached)
            return cached;

        (uint[] Times, T[] Values) keys = (Array.Empty<uint>(), Array.Empty<T>());
        byte[]? data = _source(GlobalSequence >= 0 ? -1 : index);
        if (data != null && _times[index].Count > 0)
        {
            try
            {
                ByteCursor cursor = new ByteCursor(data);
                bool spline = Interpolation is M2Interpolation.Hermite or M2Interpolation.Bezier;
                uint[] times = cursor.Items(_times[index], sizeof(uint), item => item.U32());
                T[] values = cursor.Items(_values[index], spline ? _stride * SPLINE_VALUES : _stride, _read);
                int count = Math.Min(times.Length, values.Length);
                keys = (times[..count], values[..count]);
            }
            catch (InvalidDataException)
            {
                // A truncated or mismatched .anim file: the track stays still rather than failing the model
            }
        }

        _keys[index] = keys;
        return keys;
    }

    // The value at a time, holding the first and last values outside the keys
    public T Value(int animation, uint time, T fallback, Func<T, T, float, T> lerp)
    {
        (uint[] times, T[] values) = Keys(animation);
        if (values.Length == 0)
            return fallback;
        if (values.Length == 1 || time <= times[0])
            return values[0];
        if (time >= times[^1])
            return values[^1];
        int next = Array.BinarySearch(times, time);
        if (next >= 0)
            return values[next];
        next = ~next;
        if (Interpolation == M2Interpolation.None)
            return values[next - 1];
        float span = times[next] - times[next - 1];
        float amount = span > 0 ? (time - times[next - 1]) / span : 0f;
        return lerp(values[next - 1], values[next], amount);
    }
}

// A value over a particle's life: key times as fractions of the life, values at them
public sealed class M2FakeTrack<T>
{
    public const int SIZE = 16;

    private M2FakeTrack(float[] times, T[] values)
    {
        Times = times;
        Values = values;
    }

    public float[] Times { get; }

    public T[] Values { get; }

    public static M2FakeTrack<T> Read(ByteCursor cursor, int stride, Func<ByteCursor, T> read)
    {
        M2Array times = cursor.Array();
        M2Array values = cursor.Array();
        float[] fractions = cursor.Items(times, sizeof(short), item => item.Fixed16());
        T[] items = cursor.Items(values, stride, read);
        int count = Math.Min(fractions.Length, items.Length);
        return new M2FakeTrack<T>(fractions[..count], items[..count]);
    }

    public T Value(float life, T fallback, Func<T, T, float, T> lerp)
    {
        if (Values.Length == 0)
            return fallback;
        if (Values.Length == 1 || life <= Times[0])
            return Values[0];
        for (int i = 1; i < Values.Length; ++i)
        {
            if (life <= Times[i])
            {
                float span = Times[i] - Times[i - 1];
                return lerp(Values[i - 1], Values[i], span > 0 ? (life - Times[i - 1]) / span : 0f);
            }
        }

        return Values[^1];
    }
}
