// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 25d: gathering places (design library Review 42: Colonist Daily Life and Needs §11.1, "Places and time"): an
/// area the player marks, as a stockpile is, where talks happen more often. Founders on the free-time meadow
/// (FreeTimeTests) in their Recreation hours.
/// </summary>
public class GatheringTests
{
    static Colony.Colony Found(int seed = 7) => FreeTimeTests.Found(17, seed: seed);

    // A 4 by 4 gathering place a few metres from the camp's store.
    static Gathering Mark(Colony.Colony colony)
    {
        var store = colony.Stocks.Inventories.First(i => i.Holder == Holder.Store);
        var (sx, sy) = Ground.TileOf(store.X, store.Y);
        var (gathering, placement) = colony.Work!.PlaceGathering(new Area(sx + 3, sy + 3, 4, 4));
        Assert.True(placement.Ok, placement.Why);
        return gathering!;
    }

    // Every talk as it begins: where, and whether everyone it began with (`Took`, so far: not someone who asked to join
    // in that same tick and is still on the way) stood at their place.
    static List<(Talk Talk, bool AllThere)> Begun(Colony.Colony colony, int ticks)
    {
        var begun = new List<(Talk, bool)>();
        colony.Run(ticks, c =>
        {
            foreach (var talk in c.Work!.Talks.Where(t => t.Began == c.Now.Tick))
                begun.Add((talk, talk.Took.Count >= 2 && talk.Took.All(id => c.Work.Workers.First(w => w.Id == id) is var w
                                                     && w.Outing is Outing o && w.X == o.X && w.Y == o.Y)));
        });
        return begun;
    }

    [Fact]
    public void A_gathering_place_is_marked_like_a_stockpile_and_can_be_removed()
    {
        var colony = Found();
        var work = colony.Work!;
        Assert.False(work.CheckGathering(new Area(38, 5, 4, 2)).Ok);                       // off the map
        Assert.Contains("not open ground", work.CheckGathering(new Area(34, 5, 3, 2)).Why); // the stream at x = 35
        var first = work.PlaceGathering(new Area(5, 20, 3, 3)).Gathering!;
        Assert.Equal("Gathering place 1", first.Name);
        Assert.Contains("already in Gathering place 1", work.CheckGathering(new Area(7, 22, 3, 3)).Why);
        var second = work.PlaceGathering(new Area(20, 20, 2, 2)).Gathering!;
        Assert.Equal("Gathering place 2", second.Name);
        // The inspector names it, and offers to remove it.
        Assert.Contains(Inspect.Tile(colony, 6, 21).Lines, l => l.Label == "Gathering" && l.Text.StartsWith("Gathering place 1"));
        Inspect.Commands(colony, 6, 21).Single(c => c.Label == "Remove gathering place").Run!();
        Assert.Null(work.GatheringAt(6, 21));
        Assert.Equal(new[] { second }, work.Gatherings);
        Assert.Equal("Gathering place 1", work.PlaceGathering(new Area(5, 20, 1, 1)).Gathering!.Name);   // the lowest number free
    }

    [Fact]
    public void Within_reach_of_a_gathering_place_talks_meet_there_and_begin_once_both_are_there()
    {
        var colony = Found();
        var gathering = Mark(colony);
        foreach (var w in colony.Work!.Workers)
            w.FreeNeeds["social"] = 80;
        var begun = Begun(colony, 3 * Calendar.TicksPerHour);
        Assert.NotEmpty(begun);
        Assert.All(begun, b =>
        {
            Assert.True(gathering.Area.Covers((int)Math.Floor(b.Talk.X), (int)Math.Floor(b.Talk.Y)), "a talk began at (" + b.Talk.X + ", " + b.Talk.Y + ")");
            Assert.True(b.AllThere, "a talk began before everyone was at their place");
        });
    }

    [Fact]
    public void Talks_happen_more_often_with_a_gathering_place()
    {
        int Talks(bool marked) => new[] { 7, 8, 9 }.Sum(seed =>
        {
            var colony = Found(seed);
            if (marked)
                Mark(colony);
            return Begun(colony, 3 * Calendar.TicksPerHour).Count;
        });
        int without = Talks(false), with = Talks(true);
        Assert.True(with > without, "talks with a gathering place: " + with + ", without: " + without);
    }

    [Fact]
    public void A_save_with_a_gathering_place_goes_on_as_if_never_saved()
    {
        var colony = Found();
        Mark(colony);
        foreach (var w in colony.Work!.Workers)
            w.FreeNeeds["social"] = 80;
        long end = colony.Now.Tick + 2 * Calendar.TicksPerHour;
        var copies = new List<Colony.Colony>();
        bool caught = false;
        colony.Run(end - colony.Now.Tick, c =>
        {
            if (c.Now.Tick % 23 != 0)
                return;
            // Someone on their way to meet at the gathering place, the talk not begun.
            caught |= c.Work!.Talks.Any(t => t.Began == null && c.Work.Workers.Any(w => w.Outing?.Talk == t.Id && w.Heading != null));
            string text = ColonySave.Text(c);
            var copy = ColonySave.Read(text, c.Content!);
            Assert.Equal(text, ColonySave.Text(copy));
            Assert.Equal(c.Work.Gatherings.Select(g => (g.Id, g.Name, g.Area)), copy.Work!.Gatherings.Select(g => (g.Id, g.Name, g.Area)));
            copies.Add(copy);
        });
        Assert.True(caught, "no save caught a meeting on the way");
        string fingerprint = ColonySave.Fingerprint(colony);
        foreach (var copy in copies.Where((_, i) => i % 40 == 0))
        {
            copy.Run(end - copy.Now.Tick);
            Assert.Equal(fingerprint, ColonySave.Fingerprint(copy));
        }
    }
}
