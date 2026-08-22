using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace Scribe.Ink;

/// <summary>
/// A stroke that renders as a pressure-varying outline instead of WPF's
/// fixed-width stamped tip.
/// </summary>
public sealed class ScribeStroke : Stroke
{
    /// <summary>
    /// Set false for tools that should stay perfectly even, such as the
    /// highlighter, where a varying edge looks like a mistake.
    /// </summary>
    public bool PressureSensitive { get; set; } = true;

    private Geometry? _cachedGeometry;
    private Brush? _cachedBrush;
    private Color _cachedColor;
    private int _cachedThemeVersion;

    public ScribeStroke(StylusPointCollection points) : base(points) { }

    public ScribeStroke(StylusPointCollection points, DrawingAttributes attributes)
        : base(points, attributes) { }

    protected override void DrawCore(DrawingContext drawingContext, DrawingAttributes drawingAttributes)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        drawingAttributes ??= DrawingAttributes;

        // Rebuilding the outline for every frame would make panning a page of
        // handwriting crawl, so hold it until the stroke actually changes.
        var geometry = _cachedGeometry ??=
            StrokeGeometryBuilder.Build(StylusPoints, drawingAttributes, PressureSensitive);

        if (geometry == Geometry.Empty) return;

        var color = drawingAttributes.Color;

        if (_cachedBrush is null ||
            _cachedColor != color ||
            _cachedThemeVersion != InkTheme.Version)
        {
            _cachedColor = color;
            _cachedThemeVersion = InkTheme.Version;

            // Lifts dark ink so it reads on a dark page. The stored colour is
            // untouched — this only affects what is painted.
            var painted = InkTheme.Adapt(color);

            // A highlighter must let the ink underneath show through; WPF's own
            // highlighter flag only affects its native renderer, not ours.
            if (drawingAttributes.IsHighlighter)
                painted = Color.FromArgb(InkTheme.HighlighterAlpha, painted.R, painted.G, painted.B);

            var brush = new SolidColorBrush(painted);
            brush.Freeze();
            _cachedBrush = brush;
        }

        drawingContext.DrawGeometry(_cachedBrush, null, geometry);
    }

    public override Stroke Clone() =>
        new ScribeStroke(StylusPoints.Clone(), DrawingAttributes.Clone())
        {
            PressureSensitive = PressureSensitive,
        };

    // Any of these invalidate the built outline.

    protected override void OnStylusPointsChanged(EventArgs e)
    {
        Invalidate();
        base.OnStylusPointsChanged(e);
    }

    protected override void OnStylusPointsReplaced(StylusPointsReplacedEventArgs e)
    {
        Invalidate();
        base.OnStylusPointsReplaced(e);
    }

    protected override void OnDrawingAttributesChanged(PropertyDataChangedEventArgs e)
    {
        Invalidate();
        base.OnDrawingAttributesChanged(e);
    }

    protected override void OnDrawingAttributesReplaced(DrawingAttributesReplacedEventArgs e)
    {
        Invalidate();
        base.OnDrawingAttributesReplaced(e);
    }

    private void Invalidate()
    {
        _cachedGeometry = null;
        _cachedBrush = null;
    }
}
