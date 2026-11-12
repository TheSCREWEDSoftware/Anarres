using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Anarres.App;

// A "!" drawn from its outline and centred by the ink itself, since a text line's spacing sits it off-centre
public sealed class InfoGlyph : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<InfoGlyph, IBrush?>(nameof(Foreground));

    private const double FONT_SIZE = 11;

    static InfoGlyph()
    {
        AffectsRender<InfoGlyph>(ForegroundProperty);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        FormattedText text = new FormattedText("!", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                               new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
                                               FONT_SIZE, Foreground ?? Brushes.Gray);
        Geometry? outline = text.BuildGeometry(new Point(0, 0));
        if (outline == null)
            return;
        Rect ink = outline.Bounds;
        double x = (Bounds.Width - ink.Width) / 2 - ink.X;
        double y = (Bounds.Height - ink.Height) / 2 - ink.Y;
        using (context.PushTransform(Matrix.CreateTranslation(x, y)))
            context.DrawGeometry(Foreground ?? Brushes.Gray, null, outline);
    }
}
