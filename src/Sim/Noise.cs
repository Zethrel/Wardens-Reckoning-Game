// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Smooth noise: the same as the generator's north/noise.py value_noise, so a seed gives the same pattern in
/// both. It is not dice: asking for the same point and seed always gives the same number, in any order,
/// which is what weather needs (nearby towns share their luck).
/// </summary>
public static class Noise
{
    /// <summary>A repeatable number from 0 up to (not including) 1 for one corner of the grid.
    /// The corner's position and the seed are scrambled together with multiplications and bit shifts (a hash);
    /// only the lowest 32 bits are kept at each step, as the Python does with "&amp; 0xFFFFFFFF".</summary>
    public static double Lattice(long ix, long iy, long seed)
    {
        ulong h = unchecked((ulong)ix * 374761393UL + (ulong)iy * 668265263UL + (ulong)seed * 1442695041UL) & 0xFFFFFFFFUL;
        h = unchecked((h ^ (h >> 13)) * 1274126177UL) & 0xFFFFFFFFUL;
        h ^= h >> 16;
        return h / 4294967296.0;
    }

    static double Smoothstep(double t) => t * t * (3 - 2 * t);

    /// <summary>Smooth noise at point (x, y): one octave, from 0 to 1.</summary>
    public static double Value(double x, double y, long seed)
    {
        long ix = (long)Math.Floor(x), iy = (long)Math.Floor(y);
        double tx = Smoothstep(x - ix), ty = Smoothstep(y - iy);
        double topLeft = Lattice(ix, iy, seed), topRight = Lattice(ix + 1, iy, seed);
        double bottomLeft = Lattice(ix, iy + 1, seed), bottomRight = Lattice(ix + 1, iy + 1, seed);
        double top = topLeft + (topRight - topLeft) * tx;
        double bottom = bottomLeft + (bottomRight - bottomLeft) * tx;
        return top + (bottom - top) * ty;
    }
}
