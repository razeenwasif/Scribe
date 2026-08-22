using System.Globalization;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Scribe.Ink;
using Scribe.Models;

namespace Scribe.Storage;

/// <summary>
/// Converts between WPF's in-memory ink and the open JSON stroke format.
/// </summary>
public static class StrokeCodec
{
    // Handwriting produces enormous point counts, so round aggressively. At
    // 2dp the error is ~1/50th of a pixel: invisible, and it shrinks files
    // substantially because most coordinates stop needing 15 digits.
    private const int PositionDigits = 2;
    private const int PressureDigits = 3;

    public static StrokeDto ToDto(Stroke stroke)
    {
        var da = stroke.DrawingAttributes;
        var pts = stroke.StylusPoints;

        var flat = new double[pts.Count * 3];
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            flat[i * 3 + 0] = Math.Round(p.X, PositionDigits);
            flat[i * 3 + 1] = Math.Round(p.Y, PositionDigits);
            flat[i * 3 + 2] = Math.Round(p.PressureFactor, PressureDigits);
        }

        bool pressureSensitive = stroke is ScribeStroke ss ? ss.PressureSensitive : true;

        return new StrokeDto
        {
            Color = ToHex(da.Color),
            Width = Math.Round(da.Width, 3),
            Height = Math.Round(da.Height, 3),
            Tip = da.StylusTip == StylusTip.Rectangle ? "rectangle" : "ellipse",
            Highlighter = da.IsHighlighter,
            FitToCurve = da.FitToCurve,
            PressureSensitive = pressureSensitive,
            Points = flat,
        };
    }

    public static Stroke FromDto(StrokeDto dto)
    {
        var count = dto.Points.Length / 3;
        var pts = new StylusPointCollection(Math.Max(count, 1));

        for (int i = 0; i < count; i++)
        {
            double x = dto.Points[i * 3 + 0];
            double y = dto.Points[i * 3 + 1];
            float pressure = (float)dto.Points[i * 3 + 2];

            // StylusPoint rejects out-of-range pressure outright, and hand-edited
            // or foreign-imported files are exactly where bad values show up.
            if (float.IsNaN(pressure) || pressure < 0f) pressure = 0.5f;
            if (pressure > 1f) pressure = 1f;

            pts.Add(new StylusPoint(x, y, pressure));
        }

        // A stroke with no points cannot be constructed; skip callers handle null.
        if (pts.Count == 0) pts.Add(new StylusPoint(0, 0, 0.5f));

        var da = new DrawingAttributes
        {
            Color = FromHex(dto.Color, Colors.Black),
            Width = dto.Width,
            Height = dto.Height,
            StylusTip = dto.Tip == "rectangle" ? StylusTip.Rectangle : StylusTip.Ellipse,
            IsHighlighter = dto.Highlighter,
            FitToCurve = dto.FitToCurve,
        };

        return new ScribeStroke(pts, da) { PressureSensitive = dto.PressureSensitive };
    }

    public static StrokeCollection FromDtos(IEnumerable<StrokeDto> dtos)
    {
        var col = new StrokeCollection();
        foreach (var d in dtos)
        {
            if (d.Points.Length < 3) continue;
            col.Add(FromDto(d));
        }
        return col;
    }

    public static List<StrokeDto> ToDtos(StrokeCollection strokes)
    {
        var list = new List<StrokeDto>(strokes.Count);
        foreach (var s in strokes) list.Add(ToDto(s));
        return list;
    }

    public static string ToHex(Color c) =>
        c.A == 255
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color FromHex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var s = hex.Trim().TrimStart('#');

        try
        {
            return s.Length switch
            {
                6 => Color.FromRgb(Hx(s, 0), Hx(s, 2), Hx(s, 4)),
                8 => Color.FromArgb(Hx(s, 0), Hx(s, 2), Hx(s, 4), Hx(s, 6)),
                _ => fallback,
            };
        }
        catch
        {
            return fallback;
        }

        static byte Hx(string s, int i) =>
            byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
