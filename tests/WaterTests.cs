// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 19a: water by its uses (library Review 37): the hygiene need and washing, barrels and basins filled by
/// carrying, a brook's litres by the month, and frost: frozen brooks, and ice holes in rivers and lakes.
/// </summary>
public class WaterTests
{
    static readonly Content Real = TestContent.Real;

    static Moment On(int month, int hour = 8) => new(Calendar.TickOf(1, month, 1, hour, 0));

    /// <summary>A local map of `size` tiles of grass, with fresh water down the column x = `water` (or none),
    /// as the generator writes it.</summary>
    static LocalMap Map(int size, int? water, string? kind, string biome = "meadow")
    {
        var rows = Enumerable.Range(0, size).Select(_ => new string(Enumerable.Range(0, size).Select(x => x == water ? 'w' : 'g').ToArray())).ToArray();
        return LocalMap.Parse(new JsonObject
        {
            ["format"] = "the-north-local", ["version"] = 1, ["size"] = size, ["biome"] = biome, ["water"] = kind,
            ["surfaces"] = new JsonObject { ["g"] = "grass", ["w"] = "fresh_water" },
            ["things"] = new JsonObject(),
            ["ground"] = new JsonArray(rows.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
            ["on_ground"] = new JsonArray(rows.Select(r => (JsonNode?)JsonValue.Create(new string('.', size))).ToArray()),
        }, "map");
    }

    // Runs the colony a minute at a time until `done` holds (at most `limit` minutes); false if it never did.
    static bool Until(Colony.Colony colony, Func<bool> done, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            if (done())
                return true;
            colony.RunMinutes(1);
        }
        return done();
    }

    // A piece planned and built at (x, y) (it must be placeable there): runs the colony until it stands.
    static void Built(Colony.Colony colony, string piece, int x, int y)
    {
        var (project, placement) = colony.Work!.Plan(piece, x, y);
        Assert.True(placement.Ok, placement.Why);
        Assert.True(Until(colony, () => colony.Ground!.PieceAt(x, y) == piece, 24 * 60), "the " + piece + " was never built");
    }

    [Fact]
    public void The_water_content_is_read_as_review_37_gives_it()
    {
        Assert.Equal(("hygiene", 2.0, 1.0, double.PositiveInfinity), (Real.Hygiene.Name, Real.Hygiene.PerHour, Real.Hygiene.AsleepPerHour, Real.Hygiene.CollapseHours));
        Assert.Equal(new Washing("water", 9, 100, 15), Real.Wash);
        Assert.Equal((20, 1000, "ice_hole"), (Real.Water.LoadLitres, Real.Water.PlentyLitres, Real.Water.IceHole));
        Assert.Equal(new[] { 600, 600, 600, 250, 250, 250, 400, 400, 400, 300, 300, 300 }, Real.Water.BrookByMonth);
        Assert.Equal(new[] { 10, 11, 12 }, Real.Water.FreezingMonths.OrderBy(m => m));
        Assert.Contains("taiga", Real.Water.ColdBiomes);
        Assert.DoesNotContain("meadow", Real.Water.ColdBiomes);
        Assert.Equal((100, false), (Real.Pieces["water_barrel"].HoldsLitres, Real.Pieces["water_barrel"].WashPlace));
        Assert.Equal((20, true), (Real.Pieces["wash_basin"].HoldsLitres, Real.Pieces["wash_basin"].WashPlace));
        Assert.True(Real.Pieces["ice_hole"].OpensIce);
        Assert.Empty(Real.Pieces["ice_hole"].Materials);
    }

    [Fact]
    public void Hygiene_grows_about_forty_a_day_and_never_fells_anyone()
    {
        var (colony, _) = Founding.Found(7, On(1), Real, 24, 20);
        var ada = colony.Work!.Workers.First();
        colony.RunMinutes(24 * 60);
        // 16 waking hours at 2 and 8 asleep at 1: 40 (the stream on the test ground is no bank, so nobody washed).
        Assert.InRange(ada.Needs["hygiene"].Level, 38, 42);
        ada.Needs["hygiene"].Level = 100;
        colony.RunMinutes(3 * 24 * 60);
        Assert.Equal(100, ada.Needs["hygiene"].Level);
        Assert.Null(ada.Collapsed);
    }

    [Fact]
    public void Nowhere_to_wash_does_not_stop_the_colony_working()
    {
        // On the test ground there is nowhere to wash: after two and a half days everyone's hygiene is past urgent,
        // and they still build (the rations last until the fourth day; hunger would stop them then, as it should).
        var (colony, _) = Founding.Found(7, On(1), Real, 24, 20);
        colony.RunMinutes(60 * 60);
        Assert.All(colony.Work!.Workers, w => Assert.True(w.Needs["hygiene"].Level >= Real.Hygiene.Urgent));
        Built(colony, "wall", 30, 30);
        var ada = colony.Work.Workers.First();
        Assert.Equal("nowhere to wash: no wash basin with water, and no fresh water open on the map", ada.Lacking);
    }

    [Fact]
    public void A_colonist_washes_on_the_bank_of_a_brook_and_takes_none_of_its_water()
    {
        var (colony, _) = Founding.Found(7, On(1, hour: 17), Real, Map(40, 30, "stream"));
        var work = colony.Work!;
        var brook = colony.Stocks[work.Springs.Single()];
        var ada = work.Workers.First();
        ada.Needs["hygiene"].Level = 50;                        // wants: washes in free time (17:00, a flexible hour)
        Assert.True(Until(colony, () => work.Doing(ada.Id) is "going to wash" or "washing", 60));
        int before = brook.Count("water");
        Assert.True(Until(colony, () => work.Doing(ada.Id) == "washing", 60));
        Assert.Equal(Ground.TileOf(brook.X, brook.Y), Ground.TileOf(ada.X, ada.Y));   // where she washes (afterwards she may go off in her free time)
        Assert.True(Until(colony, () => ada.Needs["hygiene"].Level < 5, 120), "never washed");
        Assert.Equal(before, brook.Count("water"));
    }

    [Fact]
    public void A_barrel_and_a_basin_are_filled_a_load_at_a_time_and_never_past_full()
    {
        var (colony, carts) = Founding.Found(7, On(1), Real, 24, 20);
        var work = colony.Work!;
        Built(colony, "water_barrel", 26, 22);
        var barrel = colony.Stocks.Inventories.Single(i => i.Name == "Water barrel");
        Assert.Equal((100, false), (barrel.Capacity, barrel.WashPlace));
        // Filled a load at a time: at most 20 litres in anyone's arms, only one colonist fetching for it at once,
        // and its room counts the water already on its way.
        Assert.DoesNotContain(Inspect.Commands(colony, 26, 22), c => c.Label == "Take down");   // not with its water in it
        bool sawLoad = false;
        for (int i = 0; i < 12 * 60 * 12; i++)                // every 5 seconds (a load is carried for seconds)
        {
            colony.Run(5);
            Assert.True(barrel.Count("water") <= 100);
            var carrying = work.Workers.Where(w => colony.Stocks[w.Arms].Count("water") > 0).ToList();
            Assert.True(carrying.Count <= 1, "two fetching at once");
            foreach (var w in carrying)
            {
                int load = colony.Stocks[w.Arms].Count("water");
                Assert.True(load <= 20, "a load of " + load);
                Assert.Equal(Math.Max(0, 100 - barrel.Count("water") - load), work.RoomIn(barrel));
                sawLoad = true;
            }
        }
        Assert.True(sawLoad, "nobody was ever seen carrying water");
        Assert.True(barrel.Count("water") >= 80);
        // The carts' water goes to the barrel only while it has room; never to the stream.
        var stream = colony.Stocks[work.Springs.Single()];
        Assert.True(stream.SourceOnly);
        // A full barrel and carts with water to empty: the water has nowhere to go, and stays in the carts.
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, barrel.Id, "water", work.RoomIn(barrel));
        Assert.Equal(100, barrel.Count("water"));
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, carts.Id, "water", 30);
        carts.Emptying = true;
        for (int i = 0; i < 60; i++)
        {
            colony.RunMinutes(1);
            Assert.True(barrel.Count("water") <= 100, "the barrel overfilled: " + barrel.Count("water"));
        }
        Assert.True(carts.Count("water") > 0);
        // With room for five, the carts send five, not a whole load.
        var drink = colony.Stocks.Reserve(barrel.Id, "water", 5, "test").Reservation!;
        colony.Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { drink.Id }, barrel.Id, new Dictionary<string, int>());
        for (int i = 0; i < 60; i++)
        {
            colony.RunMinutes(1);
            Assert.True(barrel.Count("water") <= 100, "the barrel overfilled: " + barrel.Count("water"));
        }
        for (int i = 0; i < 24 * 60; i++)
        {
            colony.RunMinutes(1);
            Assert.True(barrel.Count("water") <= 100, "the barrel overfilled: " + barrel.Count("water"));
        }
        Assert.True(stream.Count("water") <= work.LitresOf(stream.Id));
        // A basin: a wash place, filled the same way.
        Built(colony, "wash_basin", 28, 22);
        var basin = colony.Stocks.Inventories.Single(i => i.Name == "Wash basin");
        Assert.Equal((20, true), (basin.Capacity, basin.WashPlace));
        Assert.True(Until(colony, () => basin.Count("water") >= 10, 12 * 60), "the basin was never filled");
        Assert.True(basin.Count("water") <= 20);
    }

    [Fact]
    public void At_a_wash_basin_a_wash_takes_nine_litres_of_its_water()
    {
        var (colony, _) = Founding.Found(7, On(1), Real, 24, 20);
        var work = colony.Work!;
        Built(colony, "wash_basin", 26, 22);
        var basin = colony.Stocks.Inventories.Single(i => i.Name == "Wash basin");
        Assert.True(Until(colony, () => basin.Count("water") == 20, 12 * 60), "the basin was never filled");
        // Only Ada goes on: the others are stopped, so nobody fetches more water while she washes.
        var ada = work.Workers.First();
        foreach (var other in work.Workers.Skip(1))
            work.Stop(other.Id);
        Assert.True(Until(colony, () => colony.Stocks[ada.Arms].Goods.Count == 0 && ada.Job == null, 120), "Ada never put her load down");
        work.Stop(ada.Id);
        Assert.Equal(20, basin.Count("water"));
        ada.Needs["hygiene"].Level = 70;                        // urgent: sees to it at once
        work.Resume(ada.Id);
        Assert.True(Until(colony, () => work.Doing(ada.Id) == "washing", 60), "never went to wash");
        Assert.True(Until(colony, () => ada.Needs["hygiene"].Level < 5, 30), "never finished washing");
        Assert.InRange(basin.Count("water"), 11 - 3, 11);       // nine for the wash (and maybe a drink or two)
    }

    [Fact]
    public void A_brook_gives_its_months_litres_each_morning()
    {
        foreach (var (month, litres) in new[] { (1, 600), (4, 250), (7, 400), (10, 300) })
        {
            var (colony, _) = Founding.Found(7, On(month), Real, Map(40, 30, "stream"));
            var brook = colony.Work!.Springs.Single();
            Assert.Equal(WaterKind.Brook, colony.Work.SourceOf(brook).Kind);
            Assert.Equal(litres, colony.Work.LitresOf(brook));
            Assert.Equal(litres, colony.Stocks[brook].Count("water"));
        }
    }

    [Fact]
    public void A_river_has_plenty_every_hour()
    {
        var (colony, _) = Founding.Found(7, On(4), Real, Map(40, 30, "river"));
        var work = colony.Work!;
        var river = colony.Stocks[work.Springs.Single()];
        Assert.Equal(WaterKind.River, work.SourceOf(river.Id).Kind);
        Assert.Equal(1000, river.Count("water"));
        // Drawn from (a fetch for nothing, by hand): back to plenty within the hour.
        var take = colony.Stocks.Reserve(river.Id, "water", 300, "test").Reservation!;
        colony.Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { take.Id }, river.Id, new Dictionary<string, int>());
        Assert.Equal(700, river.Count("water"));
        colony.RunMinutes(60);
        Assert.Equal(1000, river.Count("water"));
    }

    [Fact]
    public void In_a_cold_winter_a_brook_freezes_and_the_founders_drink_what_they_brought()
    {
        var (colony, carts) = Founding.Found(7, On(10), Real, Map(40, 30, "stream", biome: "taiga"));
        var work = colony.Work!;
        Assert.True(work.Cold && work.Freezing);
        var brook = work.Springs.Single();
        Assert.True(work.SourceOf(brook).Frozen);
        Assert.Equal(0, work.LitresOf(brook));
        Assert.Empty(work.Projects);                                  // a brook gets no ice hole: it is frozen through
        var ada = work.Workers.First();
        ada.Needs["thirst"].Level = 70;
        Assert.True(Until(colony, () => ada.Needs["thirst"].Level < 30, 60), "never drank");
        Assert.True(carts.Count("water") < 60);
        // With the carts' water gone, nobody drinks from the frozen brook (the water in it is ice).
        var rest = colony.Stocks.Reserve(carts.Id, "water", colony.Stocks.Free(carts.Id, "water"), "test").Reservation!;
        colony.Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { rest.Id }, carts.Id, new Dictionary<string, int>());
        ada.Needs["thirst"].Level = 70;
        int ice = colony.Stocks[brook].Count("water");
        colony.RunMinutes(60);
        Assert.Equal(ice, colony.Stocks[brook].Count("water"));
        Assert.Equal("no water free in any store", ada.Lacking);
        Assert.Contains("frozen: nothing to draw until the thaw", Inspect.Tile(colony, 29, 20).Lines.Single(l => l.Label == "Source").Text);
        // Not in a meadow, and not before the freezing months.
        Assert.False(Founding.Found(7, On(10), Real, Map(40, 30, "stream")).Colony.Work!.Freezing);
        Assert.False(Founding.Found(7, On(9), Real, Map(40, 30, "stream", biome: "taiga")).Colony.Work!.Freezing);
    }

    [Fact]
    public void A_frozen_river_is_drawn_through_an_ice_hole_cut_each_morning()
    {
        var (colony, _) = Founding.Found(7, On(11), Real, Map(40, 30, "river", biome: "tundra"));
        var work = colony.Work!;
        var river = work.Springs.Single();
        var (rx, ry) = Ground.TileOf(colony.Stocks[river].X, colony.Stocks[river].Y);
        Assert.True(work.SourceOf(river).Frozen);
        var hole = Assert.Single(work.Projects);
        Assert.Equal(("ice_hole", rx, ry), (hole.Piece, hole.X, hole.Y));
        Assert.Contains("frozen over: an ice hole must be cut", Inspect.Tile(colony, rx, ry).Lines.Single(l => l.Label == "Source").Text);
        // Cut in its 30 minutes: the river opens, and nothing is left standing on the bank.
        Assert.True(Until(colony, () => !work.SourceOf(river).Frozen, 6 * 60), "the ice hole was never cut");
        Assert.Empty(work.Projects);
        Assert.Null(colony.Ground!.PieceAt(rx, ry));
        // The next morning it is frozen again, and another hole is planned.
        Assert.True(Until(colony, () => colony.Now.MinuteOfDay == Autosave.MorningMinute + 1, 30 * 60));
        Assert.True(work.SourceOf(river).Frozen);
        Assert.Single(work.Projects);
        // The ice hole is not offered in the build panel's way of placing (it is the colony's to plan): it opens ice.
        Assert.True(Real.Pieces[work.Projects.Single().Piece].OpensIce);
    }

    [Fact]
    public void While_nobody_cuts_it_one_ice_hole_stays_planned()
    {
        var (colony, _) = Founding.Found(7, On(11), Real, Map(40, 30, "lake", biome: "tundra"));
        var work = colony.Work!;
        foreach (var w in work.Workers)
            work.Stop(w.Id);
        colony.RunMinutes(23 * 60);                                     // past the next morning (not so long that anyone collapses)
        Assert.Single(work.Projects);
        var lake = work.Springs.Single();
        Assert.True(work.SourceOf(lake).Frozen);
        // Under the ice the lake holds plenty, but nobody can draw it: with the carts' water gone, a thirsty
        // colonist finds none.
        var carts = colony.Stocks.Inventories.Single(i => i.Name == "The founders' carts");
        var rest = colony.Stocks.Reserve(carts.Id, "water", colony.Stocks.Free(carts.Id, "water"), "test").Reservation!;
        colony.Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { rest.Id }, carts.Id, new Dictionary<string, int>());
        var ada = work.Workers.First();
        work.Resume(ada.Id);
        work.CancelBuild(work.Projects.Single().Id);             // nobody cuts the hole today
        ada.Needs["thirst"].Level = 95;
        colony.RunMinutes(20);
        Assert.True(ada.Needs["thirst"].Level >= 95, "Ada drank: thirst " + ada.Needs["thirst"].Level);
    }

    [Fact]
    public void Urgent_dirt_sends_a_colonist_from_their_work_to_wash()
    {
        var (colony, _) = Founding.Found(7, On(1, hour: 9), Real, Map(40, 30, "stream"));
        var work = colony.Work!;
        work.Plan("wall", 5, 5);
        Assert.True(Until(colony, () => work.Workers.Any(w => w.Job != null), 30), "nobody took the job");
        var busy = work.Workers.First(w => w.Job != null);
        busy.Needs["hygiene"].Level = 70;                        // urgent, in a work hour, on the way to fetch timber
        Assert.True(Until(colony, () => work.Doing(busy.Id) is "going to wash" or "washing", 1), busy.Name + " kept working");
        Assert.Null(busy.Job);                                   // the job went back to be taken again
    }

    [Fact]
    public void A_well_never_freezes()
    {
        var (colony, _) = Founding.Found(7, On(10), Real, Map(40, null, null, biome: "taiga"));
        var work = colony.Work!;
        Assert.Empty(work.Springs);
        var well = work.AddSpring(10.5, 10.5, "Well", 200, WaterKind.Well);
        colony.RunMinutes(24 * 60);
        Assert.False(work.SourceOf(well.Id).Frozen);
        Assert.Equal(200, work.LitresOf(well.Id));
    }

    [Fact]
    public void A_save_keeps_the_water_the_frost_the_containers_and_a_wash()
    {
        var (colony, _) = Founding.Found(7, On(11), Real, Map(40, 30, "river", biome: "tundra"));
        var work = colony.Work!;
        Built(colony, "wash_basin", 18, 18);
        var ada = work.Workers.First();
        ada.Needs["hygiene"].Level = 70;
        Assert.True(Until(colony, () => work.Doing(ada.Id) is "going to wash" or "washing", 120), "never went to wash");
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, Real);
        Assert.Equal(text, ColonySave.Text(loaded));
        Assert.True(loaded.Work!.Cold);
        colony.RunMinutes(2 * 24 * 60);
        loaded.RunMinutes(2 * 24 * 60);
        Assert.Equal(ColonySave.Fingerprint(colony), ColonySave.Fingerprint(loaded));
    }
}
