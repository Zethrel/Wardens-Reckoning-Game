// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Steps 27a and A2: the tree pictures' manifest, which picture a tree tile shows, and canopy fading's arithmetic
/// (the adopted Forest Visibility and Canopy Occlusion specification v1.3): crowns in front of a target, each
/// crown's share of the cap, the smallest share for a crown in front of several targets, and fading over time.
/// </summary>
public class CanopyTests
{
    const double Cap = 0.45;

    static IReadOnlyList<TreeArt> Arts() =>
        TreeArts.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "vegetation", "manifest.json")));

    [Fact]
    public void The_manifest_holds_both_v3_compact_trees()
    {
        var arts = Arts().ToDictionary(a => a.Kind);
        var deciduous = arts["deciduous"];
        Assert.Equal((64, 96, 32, 95, 76), (deciduous.Width, deciduous.Height, deciduous.FootX, deciduous.FootY, deciduous.CrownLine));
        var conifer = arts["conifer"];
        Assert.Equal((32, 96, 16, 95, 89), (conifer.Width, conifer.Height, conifer.FootX, conifer.FootY, conifer.CrownLine));
        // The crown lies inside the picture, and above the crown line.
        foreach (var a in arts.Values)
            Assert.True(a.CrownX >= 0 && a.CrownX + a.CrownWidth <= a.Width && a.CrownY >= 0 && a.CrownY + a.CrownHeight <= a.CrownLine);
    }

    [Fact]
    public void A_tree_stands_with_its_foot_on_the_middle_of_its_tiles_bottom_edge()
    {
        var deciduous = Arts().Single(a => a.Kind == "deciduous");
        var picture = deciduous.Picture(5.5, 8);          // the tree on tile (5, 7)
        Assert.Equal(new Box(4.5, 5, 2, 3), picture);
    }

    [Fact]
    public void The_same_tile_always_shows_the_same_tree_and_about_one_in_three_is_a_conifer()
    {
        Assert.Equal(TreeArts.KindAt(17, 42), TreeArts.KindAt(17, 42));
        int conifers = 0;
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
                if (TreeArts.KindAt(x, y) == "conifer")
                    conifers++;
        Assert.InRange(conifers, 3000, 3700);
    }

    [Fact]
    public void Each_crowns_share_lets_n_crowns_hide_exactly_the_cap()
    {
        Assert.Equal(Cap, Canopy.Share(Cap, 1), 9);
        for (int n = 1; n <= 6; n++)
            Assert.Equal(Cap, Canopy.Hidden(Enumerable.Repeat(Canopy.Share(Cap, n), n)), 9);
        Assert.Equal(1, Canopy.Share(Cap, 0));
    }

    [Fact]
    public void Only_a_crown_in_front_of_a_target_and_over_it_covers_it()
    {
        var target = new Canopy.Target(5.5, new Box(5, 4, 1, 1.5));
        Assert.True(Canopy.Covers(new Canopy.Crown(0, 7, new Box(4.5, 4.4, 2, 2)), target));     // in front, over it
        Assert.False(Canopy.Covers(new Canopy.Crown(1, 5, new Box(4.5, 2.4, 2, 2)), target));    // behind it
        Assert.False(Canopy.Covers(new Canopy.Crown(2, 7, new Box(7, 4.4, 2, 2)), target));      // in front, beside it
    }

    [Fact]
    public void Three_crowns_over_one_colonist_together_hide_the_cap()
    {
        var target = new Canopy.Target(5.5, new Box(5, 4, 1, 1.5));
        var crowns = new[]
        {
            new Canopy.Crown(0, 6, new Box(4.5, 3.5, 2, 2)),
            new Canopy.Crown(1, 7, new Box(4.5, 4.5, 2, 2)),
            new Canopy.Crown(2, 7, new Box(3.5, 4.5, 2, 2)),
            new Canopy.Crown(3, 4, new Box(4.5, 1.5, 2, 2)),      // behind: not faded
        };
        var goals = Canopy.Goals(new[] { target }, crowns, Cap);
        Assert.Equal(new[] { 0, 1, 2 }, goals.Keys.OrderBy(k => k));
        Assert.Equal(Cap, Canopy.Hidden(goals.Values), 9);
    }

    [Fact]
    public void Over_any_target_the_crowns_never_hide_more_than_the_cap()
    {
        // Many random woods and several targets at once, with crowns shared between targets (the specification's
        // N8 and N9): over every target, the crowns covering it hide at most the cap. Seeded, so it is the same
        // test every time.
        var dice = new Random(27);
        for (int round = 0; round < 300; round++)
        {
            var crowns = Enumerable.Range(0, dice.Next(1, 25)).Select(i =>
            {
                double x = dice.Next(0, 10), y = dice.Next(0, 10);
                return new Canopy.Crown(i, y + 1, new Box(x - 0.5, y - 2, 2, 2));
            }).ToList();
            var targets = Enumerable.Range(0, dice.Next(1, 5)).Select(_ =>
            {
                double x = dice.NextDouble() * 10, y = dice.NextDouble() * 10;
                return new Canopy.Target(y, new Box(x - 0.5, y - 1.5, 1, 1.5));
            }).ToList();
            var goals = Canopy.Goals(targets, crowns, Cap);
            foreach (var target in targets)
            {
                var over = crowns.Where(c => Canopy.Covers(c, target)).Select(c => goals[c.Index]);
                Assert.True(Canopy.Hidden(over) <= Cap + 1e-9);
            }
        }
    }

    [Fact]
    public void A_crown_fades_down_holds_then_comes_back_in_real_seconds()
    {
        var fader = new Fader();
        var goals = new Dictionary<int, double> { [4] = Cap };
        fader.Step(0.075, goals);                                       // halfway down
        Assert.Equal(1 - (1 - Cap) / 2, fader.OpacityOf(4), 9);
        fader.Step(0.075, goals);                                       // there after 150 ms
        Assert.Equal(Cap, fader.OpacityOf(4), 9);
        var none = new Dictionary<int, double>();
        fader.Step(0.1, none);                                          // held
        Assert.Equal(Cap, fader.OpacityOf(4), 9);
        fader.Step(0.05, none);                                         // the hold is over
        fader.Step(0.15, none);                                         // halfway back
        Assert.Equal(Cap + (1 - Cap) / 2, fader.OpacityOf(4), 9);
        fader.Step(0.15, none);                                         // solid again after 300 ms
        Assert.Equal(1, fader.OpacityOf(4));
        Assert.Equal(0, fader.Fading);
    }

    [Fact]
    public void Reduced_motion_jumps_straight_to_the_faded_opacity()
    {
        var fader = new Fader { ReducedMotion = true };
        fader.Step(0.001, new Dictionary<int, double> { [1] = Cap });
        Assert.Equal(Cap, fader.OpacityOf(1));
        fader.Step(0.2, new Dictionary<int, double>());
        Assert.Equal(1, fader.OpacityOf(1));
    }

    [Fact]
    public void The_manual_mode_fades_every_crown_but_never_above_a_targets_share()
    {
        var fader = new Fader { ReducedMotion = true };
        fader.Step(0.01, new Dictionary<int, double> { [1] = 0.2 }, everyCrown: 0.6, all: new[] { 1, 2, 3 });
        Assert.Equal(0.2, fader.OpacityOf(1));       // the target's share is lower: kept
        Assert.Equal(0.6, fader.OpacityOf(2));
        Assert.Equal(0.6, fader.OpacityOf(3));
    }
}
