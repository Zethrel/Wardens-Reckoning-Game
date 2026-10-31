// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.World;

/// <summary>Small changes to pictures, kept free of Godot so they can be tested.</summary>
public static class Pictures
{
    /// <summary>
    /// A ruin's picture: each colour moves `amount` of the way to its own grey and loses up to half its
    /// light (0: as it was; 1: grey at half the light), as the generator's render.dimmed draws ruins.
    /// `rgba` is red, green, blue and alpha for every pixel, one byte each; a changed copy is returned
    /// and the transparency is kept.
    /// </summary>
    public static byte[] Dimmed(byte[] rgba, double amount)
    {
        var result = (byte[])rgba.Clone();
        if (amount <= 0)
            return result;
        double light = 1.0 - 0.5 * amount;
        for (int i = 0; i + 3 < result.Length; i += 4)
        {
            double grey = 0.299 * rgba[i] + 0.587 * rgba[i + 1] + 0.114 * rgba[i + 2];
            for (int c = 0; c < 3; c++)
                // Math.Round rounds a half to the even number, as Python's round does.
                result[i + c] = (byte)Math.Round((rgba[i + c] + amount * (grey - rgba[i + c])) * light);
        }
        return result;
    }

    /// <summary>
    /// The borders as a picture the size of the map: each land cell in its holder's colour at `alpha`
    /// (0 to 255), everything else clear. `regions` are Borders(year); `land` says which map cells are
    /// land (rivers count, the sea and lakes do not), row after row. The picture is written into `rgba`
    /// (Size * Size * 4 bytes) so the same bytes can be filled again every year.
    /// </summary>
    public static void BordersInto(byte[] rgba, ExportedWorld world, int[]? regions, bool[] land, byte alpha)
    {
        Array.Clear(rgba);
        if (regions == null)
            return;
        int size = world.Size, factor = world.HistoryFactor;
        for (int y = 0; y < size; y++)
        {
            int row = Math.Min(y / factor, world.BorderRows - 1) * world.BorderColumns;
            for (int x = 0; x < size; x++)
            {
                if (!land[y * size + x])
                    continue;
                int holder = regions[row + Math.Min(x / factor, world.BorderColumns - 1)];
                if (holder < 0 || holder >= world.Factions.Count)
                    continue;
                var f = world.Factions[holder];
                int at = (y * size + x) * 4;
                rgba[at] = f.Red;
                rgba[at + 1] = f.Green;
                rgba[at + 2] = f.Blue;
                rgba[at + 3] = alpha;
            }
        }
    }

    /// <summary>Which map cells are land for the borders: the water grid's 0 (land) and 3 (river).</summary>
    public static bool[] Land(ExportedWorld world)
    {
        var water = world.Grid("water");
        return water.Values.Select(v => v == 0 || v == 3).ToArray();
    }
}
