// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 22: the ground's areas. `CanReach` must say exactly what working the way out would say (a way, or
/// infinity), on any ground and between any two tiles, blocked or not; only faster.
/// </summary>
public class AreaTests
{
    static readonly Surface Grass = new("grass", "Grass", 0.8);
    static readonly Surface Road = new("road", "Road", 1);
    static readonly Surface Water = new("water", "Water", 0);

    // A ground of 24 x 16 with things in the way scattered by `seed`: rocks, water, walls, floors and doors that
    // can be walked over, and a ring of wall with a gap or without.
    static Ground Scattered(int seed, double crowded)
    {
        var dice = new Random(seed);                      // seeded: the same ground every run
        var ground = new Ground(24, 16, Grass);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 24; x++)
            {
                double roll = dice.NextDouble();
                if (roll < crowded * 0.4)
                    ground.AddObstacle("rock", x, y);
                else if (roll < crowded * 0.6)
                    ground.SetSurface(x, y, Water);
                else if (roll < crowded)
                    ground.AddPiece("wall", true, x, y);
                else if (roll < crowded + 0.05)
                    ground.AddPiece("floor", false, x, y, speed: 1.2);
                else if (roll < crowded + 0.1)
                    ground.SetSurface(x, y, Road);
            }
        if (seed % 2 == 0)
            for (int i = 0; i < 6; i++)                   // a ring 6 x 6 at (8, 5), closed on even seeds of four
            {
                ground.AddPiece("wall", true, 8 + i, 5);
                ground.AddPiece("wall", true, 8 + i, 10);
                ground.AddPiece("wall", true, 8, 5 + i);
                if (seed % 4 == 0 || i != 3)
                    ground.AddPiece("wall", true, 13, 5 + i);
            }
        return ground;
    }

    [Theory]
    [InlineData(1, 0.1)]
    [InlineData(2, 0.25)]
    [InlineData(3, 0.35)]
    [InlineData(4, 0.45)]
    [InlineData(5, 0.55)]
    [InlineData(8, 0.3)]
    public void Can_reach_says_exactly_what_working_the_way_out_says(int seed, double crowded)
    {
        var ground = Scattered(seed, crowded);
        int checkedPairs = 0, ways = 0;
        // Every tile to a sample of goals, open and blocked alike (every fifth tile).
        for (int g = 0; g < 24 * 16; g += 5)
        {
            int gx = g % 24, gy = g / 24;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 24; x++)
                {
                    bool way = !double.IsPositiveInfinity(ground.Metres(x, y, gx, gy));
                    Assert.True(way == ground.CanReach(x, y, gx, gy),
                                $"from ({x}, {y}) to ({gx}, {gy}): the search says {(way ? "a way" : "none")}");
                    checkedPairs++;
                    ways += way ? 1 : 0;
                }
        }
        // Both answers came up (or the test would prove little); on the least crowded ground everything may be
        // reachable.
        if (crowded >= 0.25)
            Assert.InRange(ways, 1, checkedPairs - 1);
    }

    [Theory]
    [InlineData(1, 0.1, 6.0)]
    [InlineData(3, 0.35, 9.5)]
    [InlineData(4, 0.45, 4.0)]
    [InlineData(8, 0.3, 12.0)]
    public void A_way_cut_short_gives_the_same_metres_and_steps_as_the_whole_way(int seed, double crowded, double limit)
    {
        // Step 25a: free time works short ways out only so far. From any tile, the answer must be what the whole
        // way says: within the limit from the cut way, beyond it from the whole way worked out after all.
        for (int g = 0; g < 24 * 16; g += 7)
        {
            int gx = g % 24, gy = g / 24;
            var whole = Scattered(seed, crowded);
            var cut = Scattered(seed, crowded);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 24; x++)
                {
                    var cutFirst = Scattered(seed, crowded);       // a fresh ground each time: nothing known yet
                    double expected = whole.Metres(x, y, gx, gy);
                    Assert.Equal(expected, cutFirst.Metres(x, y, gx, gy, limit));
                    Assert.Equal(expected, cut.Metres(x, y, gx, gy, limit));       // and with what earlier asks left
                    Assert.Equal(whole.Next(x, y, gx, gy), cut.Next(x, y, gx, gy));
                }
        }
    }

    [Fact]
    public void Areas_are_worked_out_again_when_a_tile_is_blocked_or_opened()
    {
        var ground = new Ground(10, 5, Grass);
        Assert.Equal(1, ground.AreaCount);
        for (int y = 0; y < 5; y++)
            ground.AddPiece("wall", true, 4, y);         // a wall right across
        Assert.Equal(2, ground.AreaCount);
        Assert.False(ground.CanReach(0, 0, 9, 0));
        Assert.True(ground.CanReach(0, 0, 4, 0));        // up to the wall (to work at it), from either side
        Assert.True(ground.CanReach(9, 0, 4, 0));
        ground.RemovePiece(4, 2);                         // a gap
        Assert.Equal(1, ground.AreaCount);
        Assert.True(ground.CanReach(0, 0, 9, 0));
        ground.AddPiece("door", false, 4, 2);             // a door: walked through
        Assert.True(ground.CanReach(0, 0, 9, 0));
        ground.SetSurface(4, 2, Water);                   // water in the gap
        Assert.False(ground.CanReach(0, 0, 9, 0));
        Assert.False(ground.CanReach(0, 0, 4, 2));        // nor onto the water itself
        Assert.True(ground.CanReach(4, 2, 4, 2));         // already there
        Assert.False(ground.CanReach(-1, 0, 4, 0));       // off the ground
    }

    [Fact]
    public void A_corner_cut_on_the_slant_joins_nothing()
    {
        // Two open tiles touching only at a corner, between two walls: no way, as in the search.
        var ground = new Ground(3, 3, Grass);
        foreach (var (x, y) in new[] { (1, 0), (0, 1), (2, 1), (1, 2), (1, 1) })
            ground.AddPiece("wall", true, x, y);
        Assert.False(ground.CanReach(0, 0, 2, 2));
        Assert.True(double.IsPositiveInfinity(ground.Metres(0, 0, 2, 2)));
    }
}
