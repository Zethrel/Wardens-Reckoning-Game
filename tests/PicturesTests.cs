// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.World;

namespace WardensReckoning.Tests;

public class PicturesTests
{
    static readonly byte[] Pixels = { 255, 0, 0, 255, 10, 200, 30, 128, 100, 100, 100, 0, 1, 2, 3, 4 };

    [Fact]
    public void A_ruin_is_dimmed_as_the_generator_dims_it()
    {
        // What north/render.dimmed gives for the same four pixels.
        Assert.Equal(new byte[] { 103, 32, 32, 255, 55, 108, 60, 128, 70, 70, 70, 0, 1, 1, 2, 4 }, Pictures.Dimmed(Pixels, 0.6));
        Assert.Equal(new byte[] { 38, 38, 38, 255, 62, 62, 62, 128, 50, 50, 50, 0, 1, 1, 1, 4 }, Pictures.Dimmed(Pixels, 1.0));
        Assert.Equal(Pixels, Pictures.Dimmed(Pixels, 0));
    }

    [Fact]
    public void The_borders_colour_only_held_land()
    {
        var world = ExportedWorld.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "world-7"));
        var land = Pictures.Land(world);
        var regions = world.Borders(world.Years)!;
        var rgba = new byte[world.Size * world.Size * 4];
        Pictures.BordersInto(rgba, world, regions, land, 105);
        int coloured = 0;
        for (int cell = 0; cell < land.Length; cell++)
        {
            int x = cell % world.Size, y = cell / world.Size;
            int holder = regions[y / world.HistoryFactor * world.BorderColumns + x / world.HistoryFactor];
            bool shown = rgba[cell * 4 + 3] != 0;
            Assert.Equal(land[cell] && holder >= 0, shown);
            if (shown)
            {
                coloured++;
                Assert.Equal(world.Factions[holder].Red, rgba[cell * 4]);
                Assert.Equal(105, rgba[cell * 4 + 3]);
            }
        }
        Assert.True(coloured > 0);
        Pictures.BordersInto(rgba, world, null, land, 105);
        Assert.All(rgba, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Rivers_are_land_for_the_borders_and_the_sea_and_lakes_are_not()
    {
        // As the atlas's land mask: a river runs through its holder's land; the sea and lakes are no one's.
        var world = ExportedWorld.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "world-7"));
        var water = world.Grid("water").Values;
        var land = Pictures.Land(world);
        Assert.Contains(3, water);
        for (int cell = 0; cell < water.Length; cell++)
            Assert.Equal(water[cell] == 0 || water[cell] == 3, land[cell]);
    }
}
