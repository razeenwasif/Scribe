using System.Windows;
using System.Windows.Media;

namespace Scribe.Controls;

/// <summary>
/// Draws the page's rule lines, grid or dot field.
///
/// Rendered as a single element behind the ink rather than as a tiled brush so
/// the line spacing stays locked to page coordinates while zooming, which is
/// what lets handwriting keep sitting on the same rule at every zoom level.
/// </summary>
public sealed class PageBackground : FrameworkElement
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind), typeof(string), typeof(PageBackground),
            new FrameworkPropertyMetadata("ruled", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing), typeof(double), typeof(PageBackground),
            new FrameworkPropertyMetadata(32.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(
            nameof(LineBrush), typeof(Brush), typeof(PageBackground),
            new FrameworkPropertyMetadata(
                new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF5)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public PageBackground()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double spacing = Math.Max(Spacing, 4);

        if (w <= 0 || h <= 0) return;

        var kind = (Kind ?? "ruled").ToLowerInvariant();
        if (kind == "blank") return;

        var brush = LineBrush;
        if (brush is null) return;

        if (kind == "dots")
        {
            DrawDots(dc, w, h, spacing, brush);
            return;
        }

        var pen = new Pen(brush, 1.0);
        pen.Freeze();

        // Snap to whole pixels so lines stay crisp instead of blurring across
        // two rows of pixels.
        for (double y = spacing; y < h; y += spacing)
        {
            double sy = Math.Round(y) + 0.5;
            dc.DrawLine(pen, new Point(0, sy), new Point(w, sy));
        }

        if (kind == "grid")
        {
            for (double x = spacing; x < w; x += spacing)
            {
                double sx = Math.Round(x) + 0.5;
                dc.DrawLine(pen, new Point(sx, 0), new Point(sx, h));
            }
        }
        else if (kind == "ruled")
        {
            // The margin rule, as on paper.
            var marginPen = new Pen(brush, 1.0);
            marginPen.Freeze();
            double mx = Math.Round(spacing * 3) + 0.5;
            if (mx < w) dc.DrawLine(marginPen, new Point(mx, 0), new Point(mx, h));
        }
    }

    private static void DrawDots(DrawingContext dc, double w, double h, double spacing, Brush brush)
    {
        // A geometry group keeps this to one draw call rather than one per dot,
        // which matters on a page tens of thousands of DIPs tall.
        var group = new GeometryGroup();

        for (double y = spacing; y < h; y += spacing)
            for (double x = spacing; x < w; x += spacing)
                group.Children.Add(new EllipseGeometry(new Point(x, y), 1.1, 1.1));

        group.Freeze();
        dc.DrawGeometry(brush, null, group);
    }
}
