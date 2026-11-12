using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Anarres.Core.Models;
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
    private WriteableBitmap? _bitmap;
    private uint[] _pixels = Array.Empty<uint>();
    private int[] _copy = Array.Empty<int>();
    private Point? _drag;
    private bool _playing = true;
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

        _models.SelectionChanged += async (_, _) => await LoadSelectedAsync();
        _animations.SelectionChanged += (_, _) =>
        {
            if (_instance != null && _animations.SelectedIndex >= 0)
                _instance.SetSequence(_animations.SelectedIndex);
        };
        _particles.IsCheckedChanged += (_, _) =>
        {
            if (_instance == null)
                return;
            _instance.ShowParticles = _particles.IsChecked == true;
            foreach (ParticleSystem system in _instance.Particles)
                system.Clear();
        };
        _play.Click += (_, _) =>
        {
            _playing = !_playing;
            _play.Content = _playing ? "Pause" : "Play";
            UpdateTimer();
        };
        Button reset = Ui.Button("Reset view", ResetView);
        _timer.Tick += (_, _) => Tick();

        View = Ui.Vertical(6,
            Ui.Horizontal(Ui.Label("Model", 60), _models),
            _canvas,
            Ui.Horizontal(Ui.Label("Animation", 70), _animations, _play, _particles, reset),
            _status);
    }

    public Control View { get; }

    public ModelChoice? Current => _models.SelectedIndex >= 0 && _models.SelectedIndex < _choices.Count
                                       ? _choices[_models.SelectedIndex]
                                       : null;

    // Shows these models, the first one loaded; empty shows the message instead
    public void Show(List<ModelChoice> choices, string emptyMessage)
    {
        _choices = choices;
        _models.ItemsSource = choices.Select(choice => choice.Label + "  (" + choice.Path + ")").ToList();
        _models.IsVisible = choices.Count > 1;
        if (choices.Count == 0)
        {
            _instance = null;
            _image.Source = null;
            _animations.ItemsSource = null;
            _status.Text = emptyMessage;
            UpdateTimer();
            return;
        }

        if (_models.SelectedIndex == 0)
            _ = LoadSelectedAsync();
        else
            _models.SelectedIndex = 0;
    }

    private async Task LoadSelectedAsync()
    {
        ModelChoice? choice = Current;
        int version = ++_loadVersion;
        if (choice == null)
            return;
        _status.Text = "Loading " + choice.Path + "...";
        List<string> missing = new List<string>();
        try
        {
            ModelInstance instance = await Task.Run(() => ModelLoader.Load(_read, choice, missing));
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
            ResetView();
            UpdateTimer();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                              ArgumentException or IndexOutOfRangeException)
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
            (min, max) = _instance.PosedBounds();

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

    // Moves the animation on and draws a frame at the view's size (capped, then stretched, on very big screens)
    private void Draw(float seconds)
    {
        if (_instance == null)
            return;
        double scale = (_canvas.GetVisualRoot() as TopLevel)?.RenderScaling ?? 1.0;
        int width = Math.Max(MIN_SIDE, (int)(_canvas.Bounds.Width * scale));
        int height = Math.Max(MIN_SIDE, (int)(_canvas.Bounds.Height * scale));
        double shrink = Math.Sqrt(Math.Min(1.0, MAX_PIXELS / (double)(width * height)));
        width = Math.Max(MIN_SIDE, (int)(width * shrink));
        height = Math.Max(MIN_SIDE, (int)(height * shrink));

        _instance.Advance(seconds, _camera.Position, Vector3.UnitZ);
        if (_pixels.Length != width * height)
        {
            _pixels = new uint[width * height];
            _copy = new int[width * height];
        }

        _renderer.Render(_instance, _camera, width, height, _pixels);
        if (_bitmap == null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
        {
            _bitmap = new WriteableBitmap(new PixelSize(width, height), new Avalonia.Vector(96, 96),
                                          PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _image.Source = _bitmap;
        }

        Buffer.BlockCopy(_pixels, 0, _copy, 0, _pixels.Length * BYTES_PER_PIXEL);
        using (ILockedFramebuffer frame = _bitmap.Lock())
        {
            for (int y = 0; y < height; ++y)
                Marshal.Copy(_copy, y * width, frame.Address + y * frame.RowBytes, width);
        }

        _image.InvalidateVisual();
        M2Model model = _instance.Model;
        _status.Text = model.Vertices.Length + " vertices, " + _renderer.TrianglesDrawn + " triangles drawn, " +
                       model.Particles.Length + " particle emitters (" + _renderer.ParticlesDrawn + " live)" +
                       (model.RibbonCount > 0 ? ", " + model.RibbonCount + " ribbons (not drawn)" : string.Empty) +
                       "." + _missing + " Drag to turn, wheel to zoom, double-click to reset.";
    }
}
