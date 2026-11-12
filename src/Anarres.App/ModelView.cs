using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Anarres.Core.Models;
using Anarres.Core.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Anarres.App;

// A 3D view of client models, drawn in software from the client's own files: nothing comes from the internet
public sealed class ModelView
{
    private const int FRAME_MS = 33;
    private const double TURN_PER_PIXEL = 0.01;
    private const double ZOOM_STEP = 0.9;
    private const int MAX_PIXELS = 640 * 480;
    private const int MIN_SIDE = 16;
    private const int BYTES_PER_PIXEL = 4;
    private const double DEFAULT_HEIGHT = 360;
    private const float MAX_FRAME_SECONDS = 0.1f;
    private const int SMOOTH_SCALE = 2;
    private const double PERF_SECONDS = 0.5;

    private readonly Func<string, byte[]?> _read;
    private readonly Image _image = new Image { Stretch = Stretch.Fill };
    private readonly Border _canvas;
    private readonly ComboBox _models = new ComboBox { MinWidth = 260, MaxWidth = 520 };
    private readonly ComboBox _animations = new ComboBox { MinWidth = 160 };
    private readonly CheckBox _particles = new CheckBox { Content = "Particles", IsChecked = true };
    private readonly Button _play = new Button { Content = "Pause" };
    private readonly TextBlock _status = new TextBlock
    {
        Classes = { "hint" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 900
    };
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FRAME_MS) };
    private readonly Stopwatch _clock = new Stopwatch();
    private readonly ModelRenderer _renderer = new ModelRenderer();
    private readonly ModelCamera _camera = new ModelCamera();
    private List<ModelChoice> _choices = new List<ModelChoice>();
    private ModelInstance? _instance;
    // Two pictures in turn: one on screen while the next is drawn, so a frame is never shown half-written
    private readonly WriteableBitmap?[] _bitmaps = new WriteableBitmap?[2];
    private int _back;
    private uint[] _pixels = Array.Empty<uint>();
    private int[] _copy = Array.Empty<int>();
    private Point? _drag;
    private bool _playing = true;
    private bool _rendering;
    private bool _again;
    private float _owedSeconds;
    private readonly object _renderLock = new object();
    private readonly CheckBox _smooth = new CheckBox { Content = "Smooth edges" };
    private readonly CheckBox _showPerf = new CheckBox { Content = "Performance" };
    private readonly TextBlock _perf = new TextBlock { Classes = { "hint" }, IsVisible = false };
    private readonly Stopwatch _perfClock = Stopwatch.StartNew();
    private int _perfFrames;
    private double _perfTotal;
    private Vector4 _perfParts;
    private bool _changing;
    private ModelChoice? _loading;
    private int _loadVersion;
    private string _missing = string.Empty;

    public ModelView(Func<string, byte[]?> read, double height = DEFAULT_HEIGHT)
    {
        _read = read;
        _canvas = new Border
        {
            Height = height,
            ClipToBounds = true,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Black,
            Child = _image,
            Cursor = new Cursor(StandardCursorType.SizeAll)
        };
        _canvas.PointerPressed += (_, e) =>
        {
            _drag = e.GetPosition(_canvas);
            e.Pointer.Capture(_canvas);
            if (e.ClickCount == 2)
                ResetView();
        };
        _canvas.PointerMoved += (_, e) =>
        {
            if (_drag is not { } last)
                return;
            Point now = e.GetPosition(_canvas);
            _camera.Turn((float)(-(now.X - last.X) * TURN_PER_PIXEL), (float)((now.Y - last.Y) * TURN_PER_PIXEL));
            _drag = now;
            if (!_playing)
                Draw(0f);
        };
        _canvas.PointerReleased += (_, e) =>
        {
            _drag = null;
            e.Pointer.Capture(null);
        };
        _canvas.PointerWheelChanged += (_, e) =>
        {
            _camera.Zoom((float)(e.Delta.Y > 0 ? ZOOM_STEP : 1 / ZOOM_STEP));
            e.Handled = true;
            if (!_playing)
                Draw(0f);
        };
        _canvas.AttachedToVisualTree += (_, _) => UpdateTimer();
        _canvas.DetachedFromVisualTree += (_, _) => UpdateTimer();
        _canvas.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty || change.Property == Visual.BoundsProperty)
                UpdateTimer();
        };

        _models.SelectionChanged += (_, _) =>
        {
            if (!_changing)
                _ = LoadSelectedAsync();
        };
        _animations.SelectionChanged += (_, _) =>
        {
            if (_instance == null || _animations.SelectedIndex < 0)
                return;
            lock (_renderLock)
                _instance.SetSequence(_animations.SelectedIndex);
        };
        _particles.IsCheckedChanged += (_, _) =>
        {
            if (_instance == null)
                return;
            lock (_renderLock)
            {
                _instance.ShowParticles = _particles.IsChecked == true;
                foreach (ParticleSystem system in _instance.Particles)
                    system.Clear();
            }
        };
        _play.Click += (_, _) =>
        {
            _playing = !_playing;
            _play.Content = _playing ? "Pause" : "Play";
            UpdateTimer();
        };
        Button reset = Ui.Button("Reset view", ResetView);
        ToolTip.SetTip(_smooth, "Draws at twice the size and scales down: smoother edges, four times the work");
        ToolTip.SetTip(_showPerf, "Frames per second and where each frame's time goes");
        _showPerf.IsCheckedChanged += (_, _) => _perf.IsVisible = _showPerf.IsChecked == true;
        _smooth.IsCheckedChanged += (_, _) =>
        {
            if (!_playing)
                Draw(0f);
        };
        _timer.Tick += (_, _) => Tick();

        View = Ui.Vertical(6,
            Ui.Horizontal(Ui.Label("Model", 60), _models),
            _canvas,
            Ui.Horizontal(Ui.Label("Animation", 70), _animations, _play, _particles, _smooth, _showPerf, reset),
            _perf,
            _status);
    }

    public Control View { get; }

    public ModelChoice? Current => _models.SelectedIndex >= 0 && _models.SelectedIndex < _choices.Count
                                       ? _choices[_models.SelectedIndex]
                                       : null;

    // Shows these models, the first one loaded; empty shows the message instead
    public void Show(List<ModelChoice> choices, string emptyMessage)
    {
        // The list can't be swapped while it's in the middle of changing its selection; do it right after
        if (_changing)
        {
            Dispatcher.UIThread.Post(() => Show(choices, emptyMessage));
            return;
        }

        _changing = true;
        try
        {
            ShowChoices(choices, emptyMessage);
        }
        finally
        {
            _changing = false;
        }
    }

    private void ShowChoices(List<ModelChoice> choices, string emptyMessage)
    {
        _choices = choices;
        _models.ItemsSource = choices.Select(choice => choice.Label + "  (" + choice.Path + ")").ToList();
        _models.IsVisible = choices.Count > 1;
        if (choices.Count == 0)
        {
            _loading = null;
            _instance = null;
            _image.Source = null;
            _animations.ItemsSource = null;
            _status.Text = emptyMessage;
            UpdateTimer();
            return;
        }

        _models.SelectedIndex = 0;
        _ = LoadSelectedAsync();
    }

    private async Task LoadSelectedAsync()
    {
        ModelChoice? choice = Current;
        // The same model asked for again, while it's loading or shown, is left as it is
        if (choice != null && _loading != null && choice.Path == _loading.Path && choice.Label == _loading.Label &&
            choice.Textures.SequenceEqual(_loading.Textures))
            return;
        int version = ++_loadVersion;
        _loading = choice;
        if (choice == null)
            return;
        _status.Text = "Loading " + choice.Path + "...";
        List<string> missing = new List<string>();
        try
        {
            long loading = Stopwatch.GetTimestamp();
            ModelInstance instance = await Task.Run(() => ModelLoader.Load(_read, choice, missing));
            double loadMs = Stopwatch.GetElapsedTime(loading).TotalMilliseconds;
            PerfLog.Note("opened " + choice.Path + " in " + loadMs.ToString("0", CultureInfo.InvariantCulture) +
                         " ms: " + instance.Model.Vertices.Length + " vertices, " + instance.Skin.Indices.Length / 3 +
                         " triangles, " + instance.Model.Particles.Length + " emitters, " +
                         instance.Model.Textures.Length + " textures");
            if (version != _loadVersion)
                return;
            _instance = instance;
            _instance.ShowParticles = _particles.IsChecked == true;
            _animations.ItemsSource = instance.Model.Sequences
                                              .Select(sequence => AnimationNames.Name(sequence.Id) +
                                                                  (sequence.Variation > 0
                                                                       ? " (" + sequence.Variation + ")"
                                                                       : string.Empty))
                                              .ToList();
            _animations.SelectedIndex = instance.Sequence;
            _missing = missing.Count > 0 ? " Not in the client: " + string.Join(", ", missing.Distinct()) + "." : "";
            Describe();
            ResetView();
            UpdateTimer();
        }
        // A model this reader can't make sense of says so here rather than taking the window down
        catch (Exception exception)
        {
            if (version != _loadVersion)
                return;
            _instance = null;
            _image.Source = null;
            _animations.ItemsSource = null;
            _status.Text = exception.Message;
            UpdateTimer();
        }
    }

    private void ResetView()
    {
        if (_instance == null)
            return;
        M2Model model = _instance.Model;
        Vector3 min = model.BoundsMin;
        Vector3 max = model.BoundsMax;
        // Particles fly beyond the mesh, so effects keep the header's box; other models frame their posed mesh
        if (model.Particles.Length == 0 || (max - min).LengthSquared() < 0.0001f)
        {
            lock (_renderLock)
                (min, max) = _instance.PosedBounds();
        }

        if ((max - min).LengthSquared() < 0.0001f)
        {
            min = new Vector3(-1f);
            max = new Vector3(1f);
        }

        _camera.Yaw = 0.6f;
        _camera.Pitch = 0.25f;
        _camera.Fit(min, max);
        Draw(0f);
    }

    private void UpdateTimer()
    {
        bool shown = _instance != null && _canvas.IsEffectivelyVisible && _canvas.GetVisualRoot() != null &&
                     _canvas.Bounds.Width >= MIN_SIDE;
        if (shown && _playing)
        {
            if (!_timer.IsEnabled)
            {
                _clock.Restart();
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
            if (shown)
                Draw(0f);
        }
    }

    private void Tick()
    {
        float seconds = (float)Math.Min(_clock.Elapsed.TotalSeconds, MAX_FRAME_SECONDS);
        _clock.Restart();
        Draw(seconds);
    }

    // Asks for a frame; one is drawn at a time in the background, and one asked for meanwhile follows it
    private void Draw(float seconds)
    {
        _owedSeconds = Math.Min(_owedSeconds + seconds, MAX_FRAME_SECONDS);
        if (_instance == null)
            return;
        if (_rendering)
        {
            _again = true;
            return;
        }

        StartFrame();
    }

    private void StartFrame()
    {
        ModelInstance instance = _instance!;
        double scale = (_canvas.GetVisualRoot() as TopLevel)?.RenderScaling ?? 1.0;
        int width = Math.Max(MIN_SIDE, (int)(_canvas.Bounds.Width * scale));
        int height = Math.Max(MIN_SIDE, (int)(_canvas.Bounds.Height * scale));
        double shrink = Math.Sqrt(Math.Min(1.0, MAX_PIXELS / (double)(width * height)));
        width = Math.Max(MIN_SIDE, (int)(width * shrink));
        height = Math.Max(MIN_SIDE, (int)(height * shrink));
        ModelCamera camera = _camera.Clone();
        float seconds = _owedSeconds;
        _owedSeconds = 0;
        int supersample = _smooth.IsChecked == true ? SMOOTH_SCALE : 1;
        if (_pixels.Length != width * height)
        {
            _pixels = new uint[width * height];
            _copy = new int[width * height];
        }

        uint[] pixels = _pixels;
        _rendering = true;
        long started = Stopwatch.GetTimestamp();
        Task.Run(() =>
        {
            lock (_renderLock)
            {
                instance.Advance(seconds, camera.Position, Vector3.UnitZ);
                _renderer.Supersample = supersample;
                _renderer.Render(instance, camera, width, height, pixels);
            }
        }).ContinueWith(task => Dispatcher.UIThread.Post(() => FinishFrame(task, instance, width, height, started)));
    }

    // Back on the window's thread: shows the finished frame, keeps the numbers, and starts any frame asked for
    private void FinishFrame(Task task, ModelInstance instance, int width, int height, long started)
    {
        _rendering = false;
        if (task.Exception?.GetBaseException() is { } exception)
        {
            _timer.Stop();
            _playing = false;
            _play.Content = "Play";
            _status.Text = "This model couldn't be drawn: " + exception.Message +
                           (CrashLog.Write(exception, "Model viewer") is { } log ? " (details in " + log + ")" : "");
            return;
        }

        // A frame of a model that has since been replaced isn't shown
        if (!ReferenceEquals(instance, _instance) || _pixels.Length != width * height)
        {
            Again();
            return;
        }

        long uploading = Stopwatch.GetTimestamp();
        WriteableBitmap? bitmap = _bitmaps[_back];
        if (bitmap == null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
        {
            bitmap = new WriteableBitmap(new PixelSize(width, height), new Avalonia.Vector(96, 96),
                                         PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _bitmaps[_back] = bitmap;
        }

        Buffer.BlockCopy(_pixels, 0, _copy, 0, _pixels.Length * BYTES_PER_PIXEL);
        using (ILockedFramebuffer frame = bitmap.Lock())
        {
            for (int y = 0; y < height; ++y)
                Marshal.Copy(_copy, y * width, frame.Address + y * frame.RowBytes, width);
        }

        _image.Source = bitmap;
        _back = 1 - _back;
        Measure(Stopwatch.GetElapsedTime(uploading).TotalMilliseconds,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, width, height);
        Again();
    }

    private void Again()
    {
        if (!_again)
            return;
        _again = false;
        StartFrame();
    }

    // Frame times for the readout under the view (twice a second, so it doesn't flicker) and the performance log
    private void Measure(double uploadMs, double frameMs, int width, int height)
    {
        PerfLog.Add("skin", _renderer.SkinMs);
        PerfLog.Add("mesh", _renderer.MeshMs);
        PerfLog.Add("particles", _renderer.ParticlesMs);
        PerfLog.Add("finish", _renderer.FinishMs);
        PerfLog.Add("upload", uploadMs);
        PerfLog.Frame(frameMs);
        _perfFrames++;
        _perfTotal += frameMs;
        _perfParts += new Vector4((float)_renderer.SkinMs, (float)_renderer.MeshMs, (float)_renderer.ParticlesMs,
                                  (float)(_renderer.FinishMs + uploadMs));
        double elapsed = _perfClock.Elapsed.TotalSeconds;
        if (elapsed < PERF_SECONDS)
            return;
        if (_showPerf.IsChecked == true)
        {
            Vector4 parts = _perfParts / Math.Max(1, _perfFrames);
            _perf.Text = (_perfFrames / elapsed).ToString("0", CultureInfo.InvariantCulture) + " frames/s, " +
                         (_perfTotal / Math.Max(1, _perfFrames)).ToString("0.0", CultureInfo.InvariantCulture) +
                         " ms each: skin " + parts.X.ToString("0.0", CultureInfo.InvariantCulture) +
                         ", mesh " + parts.Y.ToString("0.0", CultureInfo.InvariantCulture) +
                         ", particles " + parts.Z.ToString("0.0", CultureInfo.InvariantCulture) +
                         ", finish " + parts.W.ToString("0.0", CultureInfo.InvariantCulture) + ". " +
                         width + "x" + height + (_renderer.Supersample > 1 ? " drawn at twice the size" : "") +
                         ", " + _renderer.TrianglesDrawn + " triangles, " + _renderer.ParticlesDrawn + " particles.";
        }

        _perfFrames = 0;
        _perfTotal = 0;
        _perfParts = Vector4.Zero;
        _perfClock.Restart();
    }

    // What the model holds, written once per model so the line under the picture stays still
    private void Describe()
    {
        if (_instance == null)
            return;
        M2Model model = _instance.Model;
        int triangles = _instance.Skin.Indices.Length / 3;
        _status.Text = model.Vertices.Length + " vertices, " + triangles + " triangles, " + model.Particles.Length +
                       (model.Particles.Length == 1 ? " particle emitter" : " particle emitters") +
                       (model.RibbonCount > 0 ? ", " + model.RibbonCount + " ribbons (not drawn yet)" : string.Empty) +
                       "." + _missing + " Drag to turn, wheel to zoom, double-click to reset.";
    }
}
