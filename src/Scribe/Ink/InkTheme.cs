using System.Windows.Media;

namespace Scribe.Ink;

/// <summary>
/// Adapts ink colours for display on a dark page.
///
/// This is a rendering concern only — the colour written to disk never
/// changes. Black ink stays black in the file and simply *displays* as near
/// white on a dark page, so a notebook written in dark mode still looks right
/// in light mode, and imported OneNote handwriting (almost always black) is
/// legible instead of invisible.
/// </summary>
public static class InkTheme
{
    public static bool IsDark { get; private set; } = true;

    /// <summary>
    /// Bumped on every theme change. Cached brushes compare against it so they
    /// know to rebuild without needing to subscribe to anything.
    /// </summary>
    public static int Version { get; private set; } = 1;

    public static void SetDark(bool dark)
    {
        if (IsDark == dark) return;

        IsDark = dark;
        Version++;
    }

    /// <summary>Colour to actually paint with, given the current theme.</summary>
    public static Color Adapt(Color color)
    {
        if (!IsDark) return color;

        double luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

        // Already light enough to read on a dark page — highlighters and pale
        // inks pass straight through.
        if (luminance >= 0.55) return color;

        var (h, s, l) = ToHsl(color);

        // Near-neutral dark ink is the common case: plain black handwriting.
        // Send it to a soft off-white rather than pure white, which glares.
        if (s < 0.18)
            return Color.FromArgb(color.A, 0xE9, 0xEC, 0xF3);

        // Coloured ink keeps its hue so red stays red, but is lifted to a
        // lightness that carries on a dark background, and desaturated a touch
        // because fully saturated colour vibrates against dark.
        double newL = Math.Max(l, 0.68);
        double newS = Math.Min(s, 0.72);

        var rgb = FromHsl(h, newS, newL);
        return Color.FromArgb(color.A, rgb.R, rgb.G, rgb.B);
    }

    /// <summary>
    /// Highlighters need more opacity on a dark page: a wash that reads as
    /// bright over white barely registers over near-black.
    /// </summary>
    public static byte HighlighterAlpha => IsDark ? (byte)150 : (byte)110;

    // ------------------------------------------------------------------ HSL

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-9) return (0, 0, l);

        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        double h;
        if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
        else if (max == g) h = ((b - r) / d + 2) / 6.0;
        else h = ((r - g) / d + 4) / 6.0;

        return (h, s, l);
    }

    private static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        if (s < 1e-9)
        {
            byte v = (byte)Math.Round(l * 255);
            return (v, v, v);
        }

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;

        return (Channel(p, q, h + 1.0 / 3.0), Channel(p, q, h), Channel(p, q, h - 1.0 / 3.0));

        static byte Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;

            double v = t < 1.0 / 6.0 ? p + (q - p) * 6 * t
                     : t < 1.0 / 2.0 ? q
                     : t < 2.0 / 3.0 ? p + (q - p) * (2.0 / 3.0 - t) * 6
                     : p;

            return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }
    }
}
