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
    public void Measuring_changes_nothing_that_happens()
    {
        // The counters are not saved: a colony whose ways were counted saves exactly as one just loaded.
        var (colony, _) = Founding.Found(7, new Moment(Calendar.TickOf(1, 1, 1, 8, 0)), SanitationTests.Tweaked(), SanitationTests.Map(40, 30));
        colony.Run(6 * 60);
        Assert.True(colony.Ground!.WaysAsked > 0);
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, colony.Content!);
        Assert.Equal(0, loaded.Ground!.WaysAsked);
        Assert.Equal(text, ColonySave.Text(loaded));
    }
}
