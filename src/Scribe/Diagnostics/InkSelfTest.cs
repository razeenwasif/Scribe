using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scribe.Ink;

namespace Scribe.Diagnostics;

/// <summary>
/// Renders a sheet of sample strokes to a PNG and exits.
///
/// Run with:  Scribe.exe --render-selftest out.png
///
/// This exists because ink cannot be exercised by automation: WPF collects ink
/// through the stylus stack, which ignores injected mouse input, so there is no
/// way to script a drawing test. Rendering strokes built in code goes through
/// the identical <see cref="ScribeStroke"/> path the pen uses, which makes
/// pressure response, tapering and smoothing inspectable.
/// </summary>
public static class InkSelfTest
{
    private const int Width = 1100;
    private const int BandHeight = 720;

    public static void Run(string outputPath)
    {
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            // Dark band on top, light band beneath, so the theme-adaptive ink
            // colours can be compared directly.
            InkTheme.SetDark(true);
            DrawBand(dc, 0, Color.FromRgb(0x22, 0x21, 0x1F), Color.FromRgb(0x2E, 0x2C, 0x29),
                     Color.FromRgb(0xEF, 0xED, 0xE7), "Dark theme");

            InkTheme.SetDark(false);
            DrawBand(dc, BandHeight, Colors.White, Color.FromRgb(0xE9, 0xE4, 0xD9),
                     Color.FromRgb(0x22, 0x20, 0x1D), "Light theme");
        }

        var bitmap = new RenderTargetBitmap(Width, BandHeight * 2, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static void DrawBand(
        DrawingContext dc, double top, Color paper, Color rule, Color label, string title)
    {
        dc.DrawRectangle(new SolidColorBrush(paper), null, new Rect(0, top, Width, BandHeight));

        var rulePen = new Pen(new SolidColorBrush(rule), 1);
        for (double y = top + 40; y < top + BandHeight; y += 32)
            dc.DrawLine(rulePen, new Point(0, y), new Point(Width, y));

        Text(dc, title, 24, top + 14, 15, label, bold: true);

        var black = Color.FromRgb(0x1A, 0x1A, 0x1A);
        var blue = Color.FromRgb(0x1D, 0x4E, 0xD8);
        var red = Color.FromRgb(0xC0, 0x28, 0x28);
        var yellow = Color.FromRgb(0xFF, 0xE0, 0x3D);

        double y0 = top + 70;

        // 1. Pressure ramp: the headline behaviour. Should swell in the middle
        //    and taper to a point at both ends.
        Text(dc, "Pressure 0 → 1 → 0", 24, y0, 11, label);
        Draw(dc, Wave(60, y0 + 46, 700, 0, 1), black, 3.2, pressure: true);

        // 2. The same stroke with pressure ignored, for comparison.
        y0 += 96;
        Text(dc, "Same stroke, pressure off (even width)", 24, y0, 11, label);
        Draw(dc, Wave(60, y0 + 46, 700, 0, 1), black, 3.2, pressure: false);

        // 3. Cursive-like handwriting at a realistic nib size.
        y0 += 96;
        Text(dc, "Handwriting, 2.4px nib", 24, y0, 11, label);
        Draw(dc, Cursive(60, y0 + 46, 760), black, 2.4, pressure: true);

        // 4. Fine nib and colour, to check thin strokes survive the taper.
        y0 += 96;
        Text(dc, "Fine nib 1.2px, and colour", 24, y0, 11, label);
        Draw(dc, Cursive(60, y0 + 40, 360), blue, 1.2, pressure: true);
        Draw(dc, Cursive(440, y0 + 40, 360), red, 2.0, pressure: true);

        // 5. Highlighter over ink: must sit translucently on top.
        y0 += 96;
        Text(dc, "Highlighter over ink", 24, y0, 11, label);
        Draw(dc, Cursive(60, y0 + 44, 500), black, 2.4, pressure: true);
        Draw(dc, Line(60, y0 + 44, 500), yellow, 17, pressure: false, highlighter: true);

        // 6. A single tap, which must still produce a visible dot.
        y0 += 92;
        Text(dc, "Single tap (dot)", 24, y0, 11, label);
        Draw(dc, new[] { (new Point(70, y0 + 30), 0.8f) }, black, 4, pressure: true);
        Draw(dc, new[] { (new Point(100, y0 + 30), 0.3f) }, black, 4, pressure: true);

        // 7. The OneNote import path, exercised without OneNote.
        //
        //    OneNote stores handwriting as base64 ISF inside <one:InkData>.
        //    This runs the identical chain — ISF bytes, then the same decode and
        //    JSON conversion the importer uses — so if this row matches row 1,
        //    imported handwriting keeps its pressure rather than arriving flat.
        y0 += 68;
        Text(dc, "Round-tripped through ISF + JSON (the OneNote import path)", 24, y0, 11, label);
        DrawViaIsf(dc, Wave(60, y0 + 46, 700, 0, 1), black, 3.2);
    }

    /// <summary>
    /// Builds a stroke, serialises it to ISF exactly as OneNote stores ink,
    /// reads it back, and pushes it through the importer's JSON conversion
    /// before drawing.
    /// </summary>
    private static void DrawViaIsf(
        DrawingContext dc, IEnumerable<(Point Point, float Pressure)> samples, Color color, double width)
    {
        var points = new StylusPointCollection();
        foreach (var (p, pr) in samples) points.Add(new StylusPoint(p.X, p.Y, pr));

        var original = new StrokeCollection
        {
            new Stroke(points, new DrawingAttributes { Color = color, Width = width, Height = width }),
        };

        using var isf = new MemoryStream();
        original.Save(isf);
        isf.Position = 0;

        var restored = new StrokeCollection(isf);

        // Same hop the importer makes: WPF strokes to the open JSON format and
        // back into renderable ScribeStrokes.
        var dtos = Storage.StrokeCodec.ToDtos(restored);
        foreach (var stroke in Storage.StrokeCodec.FromDtos(dtos))
            stroke.Draw(dc);
    }

    private static void Draw(
        DrawingContext dc,
        IEnumerable<(Point Point, float Pressure)> samples,
        Color color,
        double width,
        bool pressure,
        bool highlighter = false)
    {
        var points = new StylusPointCollection();
        foreach (var (p, pr) in samples)
            points.Add(new StylusPoint(p.X, p.Y, pr));

        var stroke = new ScribeStroke(points, new DrawingAttributes
        {
            Color = color,
            Width = width,
            Height = width,
            IsHighlighter = highlighter,
            FitToCurve = true,
        })
        {
            PressureSensitive = pressure,
        };

        stroke.Draw(dc);
    }

    // --------------------------------------------------------------- shapes

    private static List<(Point, float)> Wave(double x, double y, double length, float p0, float p1)
    {
        var pts = new List<(Point, float)>();
        const int n = 90;

        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n;
            double px = x + length * t;
            double py = y + 26 * Math.Sin(t * Math.PI * 2);

            // Ramp up then back down, so both tapers are visible in one stroke.
            float pressure = (float)(p0 + (p1 - p0) * Math.Sin(t * Math.PI));
            pts.Add((new Point(px, py), Math.Clamp(pressure, 0.02f, 1f)));
        }

        return pts;
    }

    private static List<(Point, float)> Line(double x, double y, double length)
    {
        return new List<(Point, float)>
        {
            (new Point(x, y), 0.5f),
            (new Point(x + length, y), 0.5f),
        };
    }

    /// <summary>
    /// A looping stroke shaped like joined-up writing, with pressure that rises
    /// on downstrokes the way a real hand loads the nib.
    /// </summary>
    private static List<(Point, float)> Cursive(double x, double y, double length)
    {
        var pts = new List<(Point, float)>();
        const int n = 260;

        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n;
            double px = x + length * t;

            double phase = t * Math.PI * 9;
            double py = y + 20 * Math.Sin(phase) + 7 * Math.Sin(phase * 2.3);

            // Downstrokes (descending y) get more pressure.
            double slope = Math.Cos(phase);
            float pressure = (float)Math.Clamp(0.55 - slope * 0.4, 0.08, 1.0);

            // Ease the very start and end so the stroke lands and lifts.
            double ends = Math.Min(1, Math.Min(t, 1 - t) * 14);
            pressure = (float)(pressure * (0.25 + 0.75 * ends));

            pts.Add((new Point(px, py), Math.Clamp(pressure, 0.02f, 1f)));
        }

        return pts;
    }

    private static void Text(
        DrawingContext dc, string text, double x, double y, double size, Color color, bool bold = false)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"),
                         FontStyles.Normal,
                         bold ? FontWeights.SemiBold : FontWeights.Normal,
                         FontStretches.Normal),
            size,
            new SolidColorBrush(color),
            96);

        dc.DrawText(formatted, new Point(x, y));
    }
}
