// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>Step 20b: the path-finding counters the performance scenarios read.</summary>
public class MeasureTests
{
    [Fact]
    public void A_way_is_counted_when_asked_for_and_when_worked_out()
    {
        var ground = new Ground(20, 20);
        Assert.Equal((0, 0, 0L, 0), (ground.WaysAsked, ground.WaysWorked, ground.WayTime, ground.WaysKept));
        ground.Metres(0, 0, 10, 10);
        ground.Metres(5, 0, 10, 10);                            // the same way, already known
        ground.Next(0, 0, 10, 10);
        Assert.Equal((3, 1, 1), (ground.WaysAsked, ground.WaysWorked, ground.WaysKept));
        Assert.True(ground.WayTime > 0);
        ground.Metres(0, 0, 3, 3);
        Assert.Equal((4, 2, 2), (ground.WaysAsked, ground.WaysWorked, ground.WaysKept));
        // Something blocking is built: the ways are forgotten, and worked out again when asked for.
        ground.AddPiece("wall", blocks: true, 7, 7);
        Assert.Equal(0, ground.WaysKept);
        ground.Metres(0, 0, 10, 10);
        Assert.Equal((5, 3, 1), (ground.WaysAsked, ground.WaysWorked, ground.WaysKept));
        // Standing at the goal asks for nothing.
        ground.Metres(10, 10, 10, 10);
        Assert.Equal(5, ground.WaysAsked);
    }

    [Fact]
    public void Past_the_most_ways_kept_the_least_lately_asked_for_is_forgotten()
    {
        var ground = new Ground(20, 20) { MostWays = 2 };
        Assert.Equal(512, Ground.DefaultMostWays);
        ground.Metres(0, 0, 10, 10);                            // a way to (10, 10)
        ground.Metres(0, 0, 3, 3);                              // and to (3, 3)
        ground.Metres(5, 5, 10, 10);                            // (10, 10) asked for again: (3, 3) is the older
        ground.Metres(0, 0, 15, 15);                            // a third: (3, 3) is forgotten
        Assert.Equal((3, 2), (ground.WaysWorked, ground.WaysKept));
        ground.Metres(1, 1, 10, 10);                            // still known
        Assert.Equal(3, ground.WaysWorked);
        ground.Metres(1, 1, 3, 3);                              // worked out again
        Assert.Equal(4, ground.WaysWorked);
    }

    [Fact]
    public void A_way_forgotten_and_worked_out_again_is_the_same_way()
    {
        // The content's test ground (its rough patches and what is in the way) with a wall and a road added:
        // every tile's metres to one goal, kept or worked out again after being forgotten, the same.
        var content = TestContent.Real;
        Ground Made(int most)
        {
            var g = Ground.Test(content);
            g.MostWays = most;
            for (int y = 5; y < 25; y++)
                g.AddPiece("wall", blocks: true, 15, y);
            for (int x = 0; x < g.Width; x++)
                g.AddPiece("road", blocks: false, x, 27, speed: 1.0);
            return g;
        }
        double[] Every(Ground g) => Enumerable.Range(0, g.Width * g.Height).Select(i => g.Metres(i % g.Width, i / g.Width, 20, 10)).ToArray();
        var first = Every(Made(512));
        var small = Made(1);
        small.Metres(0, 0, 1, 1);
        var again = Every(small);
        small.Metres(0, 0, 2, 2);                               // pushes the way to (20, 10) out
        Assert.Equal(first, again);
        Assert.Equal(first, Every(small));
        Assert.Equal(4, small.WaysWorked);                     // (1, 1), (20, 10), (2, 2), (20, 10) again
        // From (10, 15), on the far side of the wall: round its end, well over the 11.2 m straight across.
        Assert.True(first[10 + 15 * small.Width] > Math.Sqrt(125) + 5);
    }

    [Fact]
    public void Measuring_changes_nothing_that_happens()
    {
        // The counters are not saved: a colony whose ways were counted saves exactly as one just loaded.
        var (colony, _) = Founding.Found(7, new Moment(Calendar.TickOf(1, 1, 1, 8, 0)), SanitationTests.Tweaked(), SanitationTests.Map(40, 30));
        colony.RunMinutes(6 * 60);
        Assert.True(colony.Ground!.WaysAsked > 0);
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, colony.Content!);
        Assert.Equal(0, loaded.Ground!.WaysAsked);
        Assert.Equal(text, ColonySave.Text(loaded));
    }
}
