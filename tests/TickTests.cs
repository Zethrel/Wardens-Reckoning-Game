// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 21a: the second tick (library Review 39). A tick is a game second; walking is done every second; everything
/// else is worked out on the whole minutes.
/// </summary>
public class TickTests
{
    static readonly Moment Start = new(Calendar.TickOf(1, 1, 1, 9, 0));

    static (Colony.Colony Colony, Inventory Carts) Founded(int seed = 7) =>
        Founding.Found(seed, Start, SanitationTests.Tweaked(), SanitationTests.Map(40, 30));

    [Fact]
    public void A_tick_is_a_second_and_sixty_make_a_minute()
    {
        var (colony, _) = Founded();
        colony.Run(1);
        Assert.Equal(Start.Tick + 1, colony.Now.Tick);
        Assert.Equal("09:00:01", colony.Now.Clock);
        colony.RunMinutes(2);
        Assert.Equal("09:02:01", colony.Now.Clock);
        Assert.Throws<ArgumentOutOfRangeException>(() => colony.Run(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => colony.RunMinutes(-1));
        int called = 0;
        colony.RunMinutes(3, _ => called++);
        Assert.Equal(3, called);
    }

    [Fact]
    public void Between_the_minutes_only_the_walking_happens()
    {
        var (colony, _) = Founded();
        var work = colony.Work!;
        var ada = work.Workers.First();
        ada.Needs["thirst"].Level = 70;                         // she will go and drink
        colony.RunMinutes(1);
        Assert.NotNull(ada.Heading);
        double thirst = ada.Needs["thirst"].Level, hunger = ada.Needs["hunger"].Level;
        int ledger = colony.Stocks.Ledger.Count, jobs = work.Jobs.Count();
        var (x, y) = (ada.X, ada.Y);
        for (int second = 1; second < 60; second++)
        {
            var before = (ada.X, ada.Y);
            colony.Run(1);
            if (ada.Heading != null)
                Assert.NotEqual(before, (ada.X, ada.Y));        // a step every second
            // Nothing else moves on: needs, goods, jobs.
            Assert.Equal((thirst, hunger, ledger, jobs), (ada.Needs["thirst"].Level, ada.Needs["hunger"].Level, colony.Stocks.Ledger.Count, work.Jobs.Count()));
        }
        Assert.NotEqual((x, y), (ada.X, ada.Y));
        // What she carries is where she is, every second.
        Assert.Equal((ada.X, ada.Y), (colony.Stocks[ada.Arms].X, colony.Stocks[ada.Arms].Y));
        colony.Run(1);                                          // the whole minute: needs grow
        Assert.True(ada.Needs["hunger"].Level > hunger);
    }

    [Fact]
    public void A_second_of_walking_is_a_sixtieth_of_a_minutes_way()
    {
        var (colony, _) = Founded();
        var ada = colony.Work!.Workers.First();
        ada.Needs["thirst"].Level = 70;
        colony.RunMinutes(1);
        var (hx, hy, _) = ada.Heading!.Value;
        var from = (ada.X, ada.Y);
        colony.Run(1);
        double walked = Math.Sqrt((ada.X - from.X) * (ada.X - from.X) + (ada.Y - from.Y) * (ada.Y - from.Y));
        // Grass (0.8 of a road's 70 m a minute): 56 / 60 m a second, unless a tile's corner was turned.
        Assert.InRange(walked, 0.5, 56.0 / 60 + 1e-9);
        // She walks until she is there, and then stands.
        Assert.True(Until(colony, () => ada.Heading == null, 600));
        Assert.Equal((hx, hy), (ada.X, ada.Y));
        var there = (ada.X, ada.Y);
        while (!colony.Now.OnTheMinute)
        {
            colony.Run(1);
            Assert.Equal(there, (ada.X, ada.Y));
        }
    }

    // A colony with a wall planned far from the camp, run until someone is on their way to a job with more than
    // 8 m still to go, early in a minute.
    static (Colony.Colony Colony, Worker Walker, Project Wall) OnTheWay()
    {
        var (colony, _) = Founded();
        var work = colony.Work!;
        var (wall, placed) = work.Plan("wall", 4, 22);
        Assert.True(placed.Ok, placed.Why);
        bool Far(Worker w) => w.Heading is var (hx, hy, _) && w.Job != null && w.Personal == null
                              && Math.Abs(hx - w.X) + Math.Abs(hy - w.Y) > 8 && colony.Now.Second is >= 3 and <= 10;
        Assert.True(Until(colony, () => work.Workers.Any(Far), 6 * 3600), string.Join("; ", work.Workers.Select(w => w.Name + ": " + work.Doing(w.Id))));
        return (colony, work.Workers.First(Far), wall!);
    }

    [Fact]
    public void Stopping_someone_stops_them_that_second()
    {
        var (colony, walker, _) = OnTheWay();
        colony.Work!.Stop(walker.Id);
        Assert.Null(walker.Heading);
        var where = (walker.X, walker.Y);
        colony.Run(200);
        Assert.Equal(where, (walker.X, walker.Y));
    }

    [Fact]
    public void Nobody_walks_on_past_a_whole_minute_unless_what_they_do_then_says_so()
    {
        // Everything but walking is worked out on the whole minutes. Someone heading somewhere with nothing to do
        // there any more (as when a job is taken away under them) walks on to the whole minute, and then stands.
        var (colony, _) = Founded();
        colony.RunMinutes(1);
        var idle = colony.Work!.Workers.First(w => w.Job == null && w.Personal == null && w.Heading == null);
        idle.Legs = 0.05;                                       // slow: the way is far from walked in a minute
        var from = (idle.X, idle.Y);
        idle.Heading = (idle.X - 15, idle.Y, false);
        colony.Run(30);
        Assert.NotEqual(from, (idle.X, idle.Y));
        Assert.NotNull(idle.Heading);
        var half = (idle.X, idle.Y);
        colony.Run(30);                                         // the whole minute
        Assert.True(colony.Now.OnTheMinute);
        Assert.NotEqual(half, (idle.X, idle.Y));
        Assert.Null(idle.Job);
        Assert.Null(idle.Heading);
        var where = (idle.X, idle.Y);
        colony.Run(45);
        Assert.Equal(where, (idle.X, idle.Y));
    }

    [Fact]
    public void A_save_between_two_minutes_goes_on_as_if_never_saved()
    {
        // Saved every 97 seconds (so nearly always between two minutes), with people on their way.
        var (colony, _) = Founded();
        colony.Work!.Plan("water_barrel", 26, 22);
        var copies = new List<Colony.Colony>();
        bool walking = false;
        long end = Start.Tick + 4 * Calendar.TicksPerHour;
        colony.Run(end - Start.Tick, c =>
        {
            if ((c.Now.Tick - Start.Tick) % 97 != 0)
                return;
            walking |= c.Work!.Workers.Any(w => w.Heading != null) && !c.Now.OnTheMinute;
            string text = ColonySave.Text(c);
            var copy = ColonySave.Read(text, c.Content!);
            Assert.Equal(text, ColonySave.Text(copy));
            copies.Add(copy);
        });
        Assert.True(walking, "no save caught anyone walking between two minutes: " + copies.Count + " saves");
        Assert.True(copies.Count >= 140);
        string fingerprint = ColonySave.Fingerprint(colony);
        foreach (var copy in copies)
        {
            copy.Run(end - copy.Now.Tick);
            Assert.Equal(fingerprint, ColonySave.Fingerprint(copy));
        }
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("[1, 2, 3]")]
    [InlineData("[\"a\", 2, true]")]
    [InlineData("\"north\"")]
    public void A_save_with_a_broken_heading_is_refused(string heading)
    {
        var (colony, _) = Founded();
        var save = ColonySave.Write(colony);
        save["work"]!["workers"]![0]!["heading"] = JsonNode.Parse(heading);
        Assert.Contains(".heading is not [x, y, near]", Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), colony.Content!)).Message);
    }

    [Fact]
    public void A_save_of_the_minute_tick_is_refused()
    {
        var (colony, _) = Founded();
        var save = ColonySave.Write(colony);
        Assert.Equal(13, ColonySave.Version);
        save["version"] = 12;
        Assert.Contains("older saves are not converted", Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), colony.Content!)).Message);
    }

    static bool Until(Colony.Colony colony, Func<bool> done, int seconds)
    {
        for (int i = 0; i < seconds && !done(); i++)
            colony.Run(1);
        return done();
    }
}
