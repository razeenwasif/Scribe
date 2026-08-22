using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace Scribe.Ink;

/// <summary>
/// Turns a stylus point stream into a filled variable-width outline.
///
/// WPF's stock stroke rendering stamps a fixed-size tip along the path, which
/// is why default WPF ink looks like a marker rather than a pen. This builds a
/// true outline whose half-width tracks stylus pressure, which is what gives
/// handwriting its thick-thin character on downstrokes.
/// </summary>
public static class StrokeGeometryBuilder
{
    /// <summary>Points closer together than this are treated as duplicates.</summary>
    private const double MinPointDistance = 0.35;

    /// <summary>Arc-length spacing of the resampled centreline, in DIPs.</summary>
    private const double ResampleSpacing = 1.5;

    /// <summary>Half-window for the pressure moving average.</summary>
    private const int PressureSmoothingRadius = 3;

    /// <summary>
    /// Pressure maps to width through a curve, not linearly. A light pass still
    /// leaves a visible line (MinWidthFactor) and the exponent keeps the
    /// mid-range — where most handwriting sits — responsive.
    /// </summary>
    private const double MinWidthFactor = 0.30;
    private const double PressureExponent = 0.7;

    /// <summary>Arc length over which stroke ends taper, in DIPs.</summary>
    private const double TaperLength = 3.5;

    /// <param name="applyTaper">
    /// False while ink is still wet. Wet ink arrives one packet at a time, and
    /// tapering every packet would pinch the line at each boundary.
    /// </param>
    public static Geometry Build(
        StylusPointCollection stylusPoints,
        DrawingAttributes attributes,
        bool pressureSensitive,
        bool applyTaper = true)
    {
        var samples = Extract(stylusPoints);
        double baseWidth = Math.Max(attributes.Width, attributes.Height);
        double baseHalf = Math.Max(baseWidth / 2.0, 0.05);

        if (samples.Count == 0)
            return Geometry.Empty;

        // A tap is a dot, and must still honour pressure.
        if (samples.Count == 1)
        {
            double r = baseHalf * (pressureSensitive ? WidthFactor(samples[0].Pressure) : 1.0);
            var dot = new EllipseGeometry(samples[0].Point, r, r);
            dot.Freeze();
            return dot;
        }

        if (attributes.FitToCurve && samples.Count > 2)
            samples = ResampleCatmullRom(samples, ResampleSpacing);

        SmoothPressure(samples);

        var halfWidths = new double[samples.Count];
        double totalLength = ArcLength(samples);

        for (int i = 0; i < samples.Count; i++)
        {
            double f = pressureSensitive ? WidthFactor(samples[i].Pressure) : 1.0;
            halfWidths[i] = baseHalf * f;
        }

        if (pressureSensitive && applyTaper)
            ApplyEndTaper(samples, halfWidths, totalLength);

        var geometry = BuildOutline(samples, halfWidths);
        geometry.Freeze();
        return geometry;
    }

    private static double WidthFactor(double pressure)
    {
        double p = Math.Clamp(pressure, 0.0, 1.0);
        return MinWidthFactor + (1.0 - MinWidthFactor) * Math.Pow(p, PressureExponent);
    }

    // ------------------------------------------------------------------ sampling

    private struct Sample
    {
        public Point Point;
        public double Pressure;
    }

    private static List<Sample> Extract(StylusPointCollection pts)
    {
        var list = new List<Sample>(pts.Count);

        for (int i = 0; i < pts.Count; i++)
        {
            var sp = pts[i];
            var p = new Point(sp.X, sp.Y);

            // Digitisers repeat coordinates when the pen is held still; those
            // duplicates create zero-length tangents further down.
            if (list.Count > 0)
            {
                var prev = list[^1].Point;
                if (Math.Abs(p.X - prev.X) < MinPointDistance &&
                    Math.Abs(p.Y - prev.Y) < MinPointDistance)
                {
                    continue;
                }
            }

            list.Add(new Sample { Point = p, Pressure = sp.PressureFactor });
        }

        // Everything collapsed to one position: still a legitimate dot.
        if (list.Count == 0 && pts.Count > 0)
        {
            list.Add(new Sample
            {
                Point = new Point(pts[0].X, pts[0].Y),
                Pressure = pts[0].PressureFactor,
            });
        }

        return list;
    }

    /// <summary>
    /// Centripetal-ish Catmull-Rom resampling. This is the jitter filter: raw
    /// pen data is polygonal at speed, and interpolating to a fixed arc-length
    /// spacing makes curves read as smooth without cutting corners the way a
    /// plain moving average does.
    /// </summary>
    private static List<Sample> ResampleCatmullRom(List<Sample> src, double spacing)
    {
        var outp = new List<Sample>(src.Count * 2);
        outp.Add(src[0]);

        for (int i = 0; i < src.Count - 1; i++)
        {
            var p0 = src[Math.Max(i - 1, 0)];
            var p1 = src[i];
            var p2 = src[i + 1];
            var p3 = src[Math.Min(i + 2, src.Count - 1)];

            double segLen = Distance(p1.Point, p2.Point);
            int steps = Math.Max(1, (int)Math.Ceiling(segLen / spacing));

            // Long segments would otherwise explode the point count on a fast
            // flick across the page.
            steps = Math.Min(steps, 64);

            for (int s = 1; s <= steps; s++)
            {
                double t = (double)s / steps;
                outp.Add(new Sample
                {
                    Point = CatmullRom(p0.Point, p1.Point, p2.Point, p3.Point, t),
                    Pressure = Lerp(p1.Pressure, p2.Pressure, t),
                });
            }
        }

        return outp;
    }

    private static Point CatmullRom(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double t2 = t * t;
        double t3 = t2 * t;

        double x = 0.5 * ((2 * p1.X) +
                          (-p0.X + p2.X) * t +
                          (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 +
                          (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);

        double y = 0.5 * ((2 * p1.Y) +
                          (-p0.Y + p2.Y) * t +
                          (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
                          (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);

        return new Point(x, y);
    }

    /// <summary>
    /// Averages pressure over a small window. Raw pressure is noisy enough that
    /// unsmoothed width visibly pulses along an otherwise even line.
    /// </summary>
    private static void SmoothPressure(List<Sample> samples)
    {
        if (samples.Count < 3) return;

        var original = new double[samples.Count];
        for (int i = 0; i < samples.Count; i++) original[i] = samples[i].Pressure;

        for (int i = 0; i < samples.Count; i++)
        {
            int lo = Math.Max(0, i - PressureSmoothingRadius);
            int hi = Math.Min(samples.Count - 1, i + PressureSmoothingRadius);

            double sum = 0;
            for (int j = lo; j <= hi; j++) sum += original[j];

            var s = samples[i];
            s.Pressure = sum / (hi - lo + 1);
            samples[i] = s;
        }
    }

    /// <summary>
    /// Narrows the first and last few DIPs of the stroke. Real nibs land and
    /// lift progressively; without this, strokes start and stop with a blunt
    /// stub that reads as machine-made.
    /// </summary>
    private static void ApplyEndTaper(List<Sample> samples, double[] halfWidths, double totalLength)
    {
        if (samples.Count < 3) return;

        // On a stroke shorter than two tapers, taper proportionally instead so
        // dots and tick marks do not vanish entirely.
        double taper = Math.Min(TaperLength, totalLength / 2.0);
        if (taper <= 0.01) return;

        double travelled = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            travelled += Distance(samples[i - 1].Point, samples[i].Point);
            if (travelled >= taper) break;
            halfWidths[i] *= Ramp(travelled / taper);
        }
        halfWidths[0] *= Ramp(0);

        travelled = 0;
        for (int i = samples.Count - 2; i >= 0; i--)
        {
            travelled += Distance(samples[i + 1].Point, samples[i].Point);
            if (travelled >= taper) break;
            halfWidths[i] *= Ramp(travelled / taper);
        }
        halfWidths[^1] *= Ramp(0);

        // Never taper fully to zero: a hairline still needs to be visible.
        static double Ramp(double t) => 0.35 + 0.65 * Math.Sqrt(Math.Clamp(t, 0, 1));
    }

    // ------------------------------------------------------------------- outline

    private static Geometry BuildOutline(List<Sample> samples, double[] halfWidths)
    {
        int n = samples.Count;
        var normals = new Vector[n];

        for (int i = 0; i < n; i++)
        {
            Vector tangent;

            if (i == 0)
                tangent = samples[1].Point - samples[0].Point;
            else if (i == n - 1)
                tangent = samples[n - 1].Point - samples[n - 2].Point;
            else
                tangent = samples[i + 1].Point - samples[i - 1].Point;

            double len = tangent.Length;
            if (len < 1e-9)
            {
                // Fall back to the previous normal rather than emitting NaN.
                normals[i] = i > 0 ? normals[i - 1] : new Vector(0, 1);
                continue;
            }

            tangent /= len;
            normals[i] = new Vector(-tangent.Y, tangent.X);
        }

        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };

        using (var ctx = geo.Open())
        {
            var start = samples[0].Point + normals[0] * halfWidths[0];
            ctx.BeginFigure(start, isFilled: true, isClosed: true);

            // Down one side...
            for (int i = 1; i < n; i++)
                ctx.LineTo(samples[i].Point + normals[i] * halfWidths[i], true, true);

            // ...round the end cap...
            //
            // Counterclockwise is not arbitrary. With the normal defined as
            // (-tangent.Y, tangent.X) and screen Y pointing down, the cap that
            // bulges *past* the tip is always the counterclockwise sweep. Using
            // Clockwise draws the other semicircle, which folds back over the
            // stroke with opposite winding and punches a hole in the fill.
            var endRadius = halfWidths[n - 1];
            ctx.ArcTo(
                samples[n - 1].Point - normals[n - 1] * endRadius,
                new Size(endRadius, endRadius),
                0, false, SweepDirection.Counterclockwise, true, true);

            // ...back up the other side...
            for (int i = n - 2; i >= 0; i--)
                ctx.LineTo(samples[i].Point - normals[i] * halfWidths[i], true, true);

            // ...and round the start cap to close, for the same reason.
            var startRadius = halfWidths[0];
            ctx.ArcTo(
                start,
                new Size(startRadius, startRadius),
                0, false, SweepDirection.Counterclockwise, true, true);
        }

        return geo;
    }

    // -------------------------------------------------------------------- maths

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double ArcLength(List<Sample> s)
    {
        double total = 0;
        for (int i = 1; i < s.Count; i++) total += Distance(s[i - 1].Point, s[i].Point);
        return total;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
