using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Windows.Media;

namespace Scribe.Ink;

/// <summary>
/// Renders wet ink — the line under the pen tip before the stroke is committed.
///
/// This runs on WPF's dedicated pen thread, so it keeps up with the digitiser
/// even while the UI thread is busy laying out or saving. That is the single
/// biggest contributor to ink feeling attached to the nib rather than trailing
/// behind it.
///
/// It deliberately mirrors <see cref="ScribeStroke"/>'s geometry, so the line
/// does not visibly change shape at the moment the pen lifts.
/// </summary>
public sealed class ScribeDynamicRenderer : DynamicRenderer
{
    /// <summary>
    /// Mirrors the active tool. Read on the pen thread, written on the UI
    /// thread, so it is deliberately a single volatile-ish scalar rather than
    /// shared mutable state.
    /// </summary>
    public bool PressureSensitive { get; set; } = true;

    protected override void OnDraw(
        DrawingContext drawingContext,
        StylusPointCollection stylusPoints,
        Geometry geometry,
        Brush fillBrush)
    {
        if (stylusPoints is null || stylusPoints.Count == 0) return;

        var attributes = DrawingAttributes;

        var outline = StrokeGeometryBuilder.Build(
            stylusPoints,
            attributes,
            PressureSensitive,
            applyTaper: false);

        if (outline == Geometry.Empty) return;

        // Must match ScribeStroke exactly, or the line would change colour the
        // instant the pen lifts.
        var color = InkTheme.Adapt(attributes.Color);
        if (attributes.IsHighlighter)
            color = Color.FromArgb(InkTheme.HighlighterAlpha, color.R, color.G, color.B);

        var brush = new SolidColorBrush(color);
        brush.Freeze();

        drawingContext.DrawGeometry(brush, null, outline);
    }
}
