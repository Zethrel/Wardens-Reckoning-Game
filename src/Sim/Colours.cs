// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Telling colours apart, for every pair of eyes (the generator's north/colours.py). A new faction is drawn in a
/// shade of its parent's colour, so kin look alike on the map, but as far as possible from every other faction's.
/// How different two colours look is measured three ways and the smallest counts: as most people see them, and as
/// people with two kinds of red-green colour blindness see them.
/// </summary>
public static class Colours
{
    // The published matrices of Machado, Oliveira and Fernandes (2009), at a strength of 0.6, and no change at all.
    static readonly double[,] Deuteranomaly =
        { { 0.547494, 0.607765, -0.155259 }, { 0.181692, 0.781742, 0.036566 }, { -0.010410, 0.027275, 0.983136 } };
    static readonly double[,] Protanomaly =
        { { 0.385450, 0.769005, -0.154455 }, { 0.100526, 0.829802, 0.069673 }, { -0.007442, -0.022190, 1.029632 } };
    static readonly double[,] Normal = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    /// <summary>How a new faction's shade can differ from its parent's: lighter (above 0) or darker, tried in order.</summary>
    public static readonly double[] Shades = { 0.3, -0.3, 0.5, -0.5, 0.15, -0.15, 0.7, -0.4 };

    /// <summary>One 0..255 value as linear light, 0 to 1 (undoing the screen's gamma).</summary>
    static double Linear(int channel)
    {
        double value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>Linear light as CIE Lab (lightness, a, b), where straight-line distance is close to how different
    /// two colours look.</summary>
    static (double L, double A, double B) Lab(double r, double g, double b)
    {
        double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        double z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Pow(t, 1.0 / 3) : 7.787 * t + 16.0 / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    /// <summary>How `colour` looks through `eyes` (one of the matrices), in Lab.</summary>
    static (double, double, double) Seen((int R, int G, int B) colour, double[,] eyes)
    {
        double[] light = { Linear(colour.R), Linear(colour.G), Linear(colour.B) };
        var mixed = new double[3];
        for (int i = 0; i < 3; i++)
        {
            double sum = 0;
            for (int j = 0; j < 3; j++)
                sum += eyes[i, j] * light[j];
            mixed[i] = Math.Max(0.0, Math.Min(1.0, sum));
        }
        return Lab(mixed[0], mixed[1], mixed[2]);
    }

    /// <summary>How different two colours look, to the eyes that tell them apart worst.</summary>
    public static double Distance((int R, int G, int B) first, (int R, int G, int B) second)
    {
        double worst = double.PositiveInfinity;
        foreach (var eyes in new[] { Normal, Deuteranomaly, Protanomaly })
        {
            var (l1, a1, b1) = Seen(first, eyes);
            var (l2, a2, b2) = Seen(second, eyes);
            worst = Math.Min(worst, Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2)));
        }
        return worst;
    }

    /// <summary>`colour` made lighter (share above 0, toward white) or darker (below 0, toward black). Rounded as
    /// Python's round() does, a half to the even number.</summary>
    public static (int R, int G, int B) Shade((int R, int G, int B) colour, double share)
    {
        int Each(int c) => (int)Math.Round(share >= 0 ? c + (255 - c) * share : c * (1 + share), MidpointRounding.ToEven);
        return (Each(colour.R), Each(colour.G), Each(colour.B));
    }

    /// <summary>A shade of `parent` that stands as far as possible from all the `others` (the first of the shades on
    /// a tie). colours.kin_colour.</summary>
    public static (int R, int G, int B) KinColour((int R, int G, int B) parent, IEnumerable<(int R, int G, int B)> others)
    {
        var list = others.ToList();
        (int, int, int) best = parent;
        double bestGap = -1.0;
        foreach (double share in Shades)
        {
            var candidate = Shade(parent, share);
            double gap = list.Count == 0 ? double.PositiveInfinity : list.Min(other => Distance(candidate, other));
            if (gap > bestGap)
                (best, bestGap) = (candidate, gap);
        }
        return best;
    }
}
