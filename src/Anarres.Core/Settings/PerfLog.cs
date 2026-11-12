using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Anarres.Core.Settings;

// View > Write a performance log: the system, then every 2 s frames and their parts, and anything over 50 ms
public static class PerfLog
{
    public const string FILE_NAME = "perf.log";
    public const double SLOW_MS = 50;
    public const double FLUSH_SECONDS = 2;

    private static readonly object Lock = new object();
    private static readonly Dictionary<string, (double Ms, int Count)> Totals =
        new Dictionary<string, (double, int)>(StringComparer.Ordinal);
    private static readonly List<string> Pending = new List<string>();
    private static string? _path;
    private static DateTime _lastFlush = DateTime.UtcNow;
    private static int _frames;
    private static double _slowest;

    public static string DefaultPath => Path.Combine(ProjectSettings.DefaultFolder, FILE_NAME);

    public static bool IsOn => _path != null;

    // Starts a new log with what it needs to be read; facts are name and value pairs such as the model shown
    public static void Start(string path, string version, IEnumerable<(string Name, string Value)>? facts = null)
    {
        StringBuilder head = new StringBuilder();
        head.AppendLine("Anarres " + version + " performance log, started " +
                        DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        head.AppendLine("System: " + RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture +
                        "), " + Environment.ProcessorCount + " processors, " + RuntimeInformation.FrameworkDescription);
        foreach ((string name, string value) in facts ?? Enumerable.Empty<(string, string)>())
            head.AppendLine(name + ": " + value);
        head.AppendLine();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, head.ToString());
        lock (Lock)
        {
            _path = path;
            Totals.Clear();
            Pending.Clear();
            _frames = 0;
            _slowest = 0;
            _lastFlush = DateTime.UtcNow;
        }
    }

    public static void Stop()
    {
        Flush(force: true);
        lock (Lock)
            _path = null;
    }

    // Something worth a line of its own, such as a model being opened
    public static void Note(string text)
    {
        lock (Lock)
        {
            if (_path != null)
                Pending.Add(Time() + "  " + text);
        }
    }

    // Time spent on one kind of work; 50 ms or more is also written down on its own
    public static void Add(string label, double ms)
    {
        lock (Lock)
        {
            if (_path == null)
                return;
            (double total, int count) = Totals.GetValueOrDefault(label);
            Totals[label] = (total + ms, count + 1);
            if (ms >= SLOW_MS)
                Pending.Add(Time() + "  slow: " + label + " " + ms.ToString("0", CultureInfo.InvariantCulture) + " ms");
        }
    }

    // One frame shown, and how long it took from start to screen
    public static void Frame(double ms)
    {
        lock (Lock)
        {
            if (_path == null)
                return;
            ++_frames;
            _slowest = Math.Max(_slowest, ms);
        }

        Flush(force: false);
    }

    // Writes what gathered since the last time, every 2 seconds
    public static void Flush(bool force)
    {
        string? path;
        string text;
        lock (Lock)
        {
            double seconds = (DateTime.UtcNow - _lastFlush).TotalSeconds;
            if (_path == null || (!force && seconds < FLUSH_SECONDS))
                return;
            StringBuilder lines = new StringBuilder();
            foreach (string line in Pending)
                lines.AppendLine(line);
            if (_frames > 0 || Totals.Count > 0)
            {
                lines.Append(Time() + "  " + _frames + " frames (" +
                             (_frames / Math.Max(seconds, 0.001)).ToString("0.0", CultureInfo.InvariantCulture) +
                             "/s, slowest " + _slowest.ToString("0", CultureInfo.InvariantCulture) + " ms)");
                foreach ((string label, (double ms, int count)) in Totals.OrderByDescending(entry => entry.Value.Ms))
                {
                    lines.Append("; " + label + " " +
                                 (ms / Math.Max(1, count)).ToString("0.0", CultureInfo.InvariantCulture) + " ms avg");
                }

                lines.AppendLine();
            }

            Pending.Clear();
            Totals.Clear();
            _frames = 0;
            _slowest = 0;
            _lastFlush = DateTime.UtcNow;
            path = _path;
            text = lines.ToString();
        }

        try
        {
            if (text.Length > 0)
                File.AppendAllText(path, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A log that can't be written stops quietly rather than getting in the way
            lock (Lock)
                _path = null;
        }
    }

    private static string Time()
    {
        return DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }
}
