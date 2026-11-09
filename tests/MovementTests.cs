// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 15c (library Review 34): walking speed by what is underfoot and by a colonist's legs; roads; the well; taking
/// pieces down; two workplaces working on their own.
/// </summary>
public class MovementTests
{
    static readonly Content Real = Content.Load(Path.Combine(AppContext.BaseDirectory, "content"));
    static readonly Moment Nine = new(Calendar.TickOf(301, 1, 1, 9, 0));
    static Surface S(string id) => Real.Surfaces[id];

    /// <summary>A colony on `ground` with one colonist at (x, y) and the founders' carts at (1.5, 1.5) with some
    /// timber and rations.</summary>
    static (ColonyState Colony, Worker Ada, Inventory Carts) Camp(Ground ground, double x, double y, int timber = 20)
    {
        var colony = new ColonyState(42, Nine, Real);
        colony.Lay(ground);
        var carts = colony.Stocks.Open("Carts", Holder.Store, 1.5, 1.5);
        if (timber > 0)
            colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, carts.Id, "timber", timber);
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, carts.Id, "ration", 10);
        var ada = colony.Work!.AddWorker("Ada", x, y);
        return (colony, ada, carts);
    }

    // ---- Speed by what is underfoot, and by legs ---------------------------------------------------------------

    [Fact]
    public void A_way_over_grass_counts_longer_than_the_same_way_on_a_road()
    {
        var grass = new Ground(10, 1, S("grass"));
        Assert.Equal(9 / 0.8, grass.Metres(0, 0, 9, 0), 9);                     // 9 m of grass = 11.25 m of road
        for (int x = 0; x < 10; x++)
            grass.AddPiece("road", false, x, 0, closes: false, speed: 1);
        Assert.Equal(9, grass.Metres(0, 0, 9, 0), 9);
        Assert.Equal(1, grass.Speed(3, 0));
        var rough = new Ground(10, 1, S("rough"));
        Assert.Equal(9 / 0.55, rough.Metres(0, 0, 9, 0), 9);
        Assert.Equal("rough", rough.SurfaceAt(4, 0).Id);
    }

    [Theory]
    [InlineData("road", 1.0, 70)]
    [InlineData("grass", 1.0, 56)]
    [InlineData("rough", 1.0, 38.5)]
    [InlineData("grass", 0.5, 28)]
    public void A_minute_of_walking_goes_as_far_as_the_ground_and_the_legs_allow(string surface, double legs, double metres)
    {
        var (colony, ada, _) = Camp(new Ground(200, 3, S(surface)), 0.5, 1.5, timber: 0);
        ada.Legs = legs;
        ada.Needs["thirst"].Level = 90;                                         // she must walk to the far spring
        colony.Work!.AddSpring(190.5, 1.5);
        colony.Run(1);
        Assert.Equal(PersonalKind.Drink, ada.Personal?.Kind);
        Assert.Equal(0.5 + metres, ada.X, 6);
    }

    [Fact]
    public void The_last_stretch_inside_a_tile_goes_at_that_tiles_speed_and_stepping_off_a_wall_too()
    {
        // With very weak legs (1%), a walk inside one tile of rough ground takes minutes: 70 × 0.55 × 0.01 m a
        // minute.
        var (colony, ada, _) = Camp(new Ground(5, 5, S("rough")), 2.1, 2.5, timber: 0);
        ada.Legs = 0.01;
        ada.Needs["thirst"].Level = 90;
        colony.Work!.AddSpring(2.9, 2.5);
        colony.Run(1);
        Assert.Equal(2.1 + 70 * 0.55 * 0.01, ada.X, 6);
        // Someone on a tile that became blocked steps off at the speed of the tile they step onto.
        var ground = new Ground(5, 5, S("rough"));
        ground.AddPiece("wall", true, 2, 2);
        Assert.Equal(1 / 0.55, ground.Metres(2, 2, 2, 3), 9);
    }

    [Fact]
    public void Legs_carry_from_above_nothing_to_all_of_a_walk()
    {
        var (_, ada, _) = Camp(new Ground(5, 5), 1.5, 1.5);
        Assert.Equal(1, ada.Legs);
        Assert.Throws<ArgumentOutOfRangeException>(() => ada.Legs = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => ada.Legs = 1.1);
    }

    [Fact]
    public void Colonists_take_the_road_when_it_is_quicker_even_if_it_is_longer()
    {
        // Rough ground everywhere, a road along the top row: 29 m straight across the rough is slower than going
        // up to the road, along it and down again.
        var ground = new Ground(30, 11, S("rough"));
        for (int x = 0; x < 30; x++)
            ground.AddPiece("road", false, x, 0, closes: false, speed: 1);
        double way = ground.Metres(0, 5, 29, 5);
        Assert.True(way < 29 / 0.55, "the way is " + way + " m of road, straight would be " + 29 / 0.55);
        var (x0, y0) = (0, 5);
        bool onRoad = false;
        while (ground.Next(x0, y0, 29, 5) is var (nx, ny))
        {
            onRoad |= ny == 0;
            (x0, y0) = (nx, ny);
        }
        Assert.True(onRoad, "the way goes along the road");
        // Without the road, the straight way across is the quickest.
        Assert.Equal(29 / 0.55, new Ground(30, 11, S("rough")).Metres(0, 5, 29, 5), 9);
    }

    [Fact]
    public void A_road_is_built_with_labour_alone_and_floors_are_as_quick()
    {
        var (colony, _, carts) = Camp(Ground.Test(Real), 20.5, 20.5);
        var work = colony.Work!;
        var road = work.Plan("road", 21, 20).Project!;
        var floor = work.Plan("floor", 22, 20).Project!;
        int ledger = colony.Stocks.Ledger.Count;
        colony.Run(60);
        Assert.Equal("road", colony.Ground!.PieceAt(21, 20));
        Assert.Equal(1, colony.Ground.Speed(21, 20));
        Assert.Equal(1, colony.Ground.Speed(22, 20));                           // a floor too
        Assert.Equal(0.8, colony.Ground.Speed(23, 20));                         // the grass beside them
        Assert.Null(work.Project(road.Id));
        Assert.Null(work.Project(floor.Id));
        // The road used nothing: the only ledger lines since are the floor's one timber, brought and used.
        Assert.Equal(19, carts.Count("timber"));
        Assert.Single(colony.Stocks.Ledger.Skip(ledger), e => e.Change == Change.Used);
    }

    // ---- The well ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_well_is_built_from_its_side_and_gives_water_every_morning()
    {
        var (colony, ada, carts) = Camp(Ground.Test(Real), 20.5, 20.5);
        var work = colony.Work!;
        var (well, placement) = work.Plan("well", 24, 24);
        Assert.True(placement.Ok, placement.Why);
        Assert.Equal((24.5, 26.5), (colony.Stocks[well!.Stock].X, colony.Stocks[well.Stock].Y));   // its pile below it
        Assert.Equal("At (25, 24): A well is already planned there.", work.Check("well", 25, 23).Why);
        Assert.Equal("The planned well is to be used from there.", work.Check("wall", 24, 26).Why);
        bool atSide = false;
        colony.Run(8 * 60, _ =>
        {
            if (work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Build && j.Step == Step.Working) is not null)
                atSide = Ground.TileOf(ada.X, ada.Y) == (24, 26) || atSide;
        });
        Assert.True(atSide, "she builds it standing at its side");
        foreach (var (x, y) in new[] { (24, 24), (25, 24), (24, 25), (25, 25) })
        {
            Assert.Equal("well", colony.Ground!.PieceAt(x, y));
            Assert.False(colony.Ground.Open(x, y));
        }
        Assert.Equal(12, carts.Count("timber"));                                // 8 used
        var water = colony.Stocks.Inventories.Single(i => i.Name == "Well");
        Assert.Equal((24.5, 26.5), (water.X, water.Y));
        Assert.Equal(200, work.LitresOf(water.Id));
        var small = work.AddSpring(3.5, 40.5, "A trickle", 50);                 // each source has its own litres
        Assert.Equal(50, small.Count("water"));
        Assert.Equal("A store (Well) is there: a timber wall would block it.", work.Check("wall", 24, 26).Why);
        // Draw 150 litres from it (used up), and the next morning it is full again.
        var drawn = colony.Stocks.Reserve(water.Id, "water", 150, "drawn for the test").Reservation!;
        colony.Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { drawn.Id }, water.Id, new Dictionary<string, int>());
        Assert.True(water.Count("water") <= 50);
        colony.Until(() => colony.Now.MinuteOfDay == Autosave.MorningMinute + 1, Calendar.TicksPerDay);
        Assert.InRange(water.Count("water"), 195, 200);                         // filled to 200 (a drink or so since)
        Assert.InRange(small.Count("water"), 45, 50);                           // and the trickle to its 50
    }

    [Fact]
    public void A_builder_coming_from_afar_works_the_well_from_its_side_only()
    {
        // The timber is already at the well's side, so whoever builds it walks there to build, not to deliver.
        var (colony, ada, _) = Camp(Ground.Test(Real), 10.5, 40.5);
        var work = colony.Work!;
        var well = work.Plan("well", 24, 24).Project!;
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, well.Stock, "timber", 8);
        int working = 0;
        colony.Run(6 * 60, _ =>
        {
            if (work.Jobs.Any(j => j.Kind == JobKind.Build && j.Step == Step.Working))
            {
                working++;
                Assert.Equal((24, 26), Ground.TileOf(ada.X, ada.Y));
            }
        });
        Assert.True(working > 200);
        Assert.Equal("well", colony.Ground!.PieceAt(24, 24));
    }

    [Fact]
    public void A_well_must_fit_and_be_reachable_from_its_side()
    {
        var (colony, _, _) = Camp(Ground.Test(Real), 20.5, 20.5);
        var work = colony.Work!;
        Assert.Equal("At (30, 8): A rock is in the way.", work.Check("well", 29, 7).Why);
        Assert.Equal("It is used from (30, 8), which must be open ground.", work.Check("well", 30, 6).Why);
        Assert.Equal("At (48, 10): It is outside the ground (48 by 48 tiles).", work.Check("well", 47, 10).Why);
        Assert.Equal("It is used from (10, 48), which must be open ground.", work.Check("well", 10, 46).Why);
        work.Plan("wall", 10, 12);
        Assert.Equal("It is used from (10, 12), which must be open ground.", work.Check("well", 10, 10).Why);
    }

    [Fact]
    public void Colonists_drink_at_the_nearer_of_the_stream_and_the_well()
    {
        var (colony, ada, _) = Camp(Ground.Test(Real), 20.5, 20.5);
        var work = colony.Work!;
        work.AddSpring(3.5, 40.5, "The stream");
        work.Plan("well", 24, 24);
        colony.Until(() => colony.Stocks.Inventories.Any(i => i.Name == "Well"), 8 * 60);
        ada.Needs["thirst"].Level = 90;
        colony.Run(1);
        var well = colony.Stocks.Inventories.Single(i => i.Name == "Well");
        Assert.Equal(PersonalKind.Drink, ada.Personal?.Kind);
        Assert.Equal((well.X, well.Y), (ada.Personal!.X, ada.Personal.Y));
    }

    // ---- Taking down ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_wall_taken_down_gives_half_its_timber_back_and_leaves_the_tile_open()
    {
        // Ada starts beside the wall, so she starts work in her first minute (as in lesson 15a's floor test).
        var (colony, _, carts) = Camp(Ground.Test(Real), 21.5, 21.5, timber: 0);
        var ground = colony.Ground!;
        var work = colony.Work!;
        ground.AddPiece("wall", true, 22, 22, closes: true);
        var (down, placement) = work.TakeDown(22, 22);
        Assert.True(placement.Ok);
        Assert.Equal("It is already being taken down.", work.TakeDown(22, 22).Placement.Why);
        long started = -1, done = -1;
        colony.Run(120, c =>
        {
            if (started < 0 && work.Jobs.Any(j => j.Kind == JobKind.Build && j.Step == Step.Working))
                started = c.Now.Tick;
            if (done < 0 && ground.PieceAt(22, 22) == null)
                done = c.Now.Tick;
            Assert.Equal(c.Stocks.Accounted("timber"), c.Stocks.Total("timber"));
        });
        Assert.Equal(Real.Pieces["wall"].WorkMinutes / 2, done - started + 1);    // half the building time
        Assert.True(ground.Open(22, 22));
        Assert.Equal(1, carts.Count("timber"));                                  // half of 2, carried to the carts
        var made = Assert.Single(colony.Stocks.Ledger, e => e.Change == Change.Made && e.Good == "timber");
        Assert.Equal(1, made.Amount);
        Assert.Null(work.Project(down!.Id));
        Assert.Empty(work.Jobs);
    }

    [Fact]
    public void What_cannot_be_taken_down_is_refused_and_a_road_gives_nothing_back()
    {
        var (colony, _, carts) = Camp(Ground.Test(Real), 20.5, 20.5, timber: 0);
        var ground = colony.Ground!;
        var work = colony.Work!;
        Assert.Equal("There is nothing built there to take down.", work.TakeDown(30, 30).Placement.Why);
        foreach (var (x, y) in new[] { (30, 30), (31, 30), (30, 31), (31, 31) })
            ground.AddPiece("well", true, x, y);
        Assert.Equal("A well cannot be taken down yet.", work.TakeDown(31, 31).Placement.Why);
        ground.AddPiece("road", false, 21, 21, closes: false, speed: 1);
        var road = work.TakeDown(21, 21).Project!;
        colony.Run(30);
        Assert.Null(ground.PieceAt(21, 21));
        Assert.Equal(0.8, ground.Speed(21, 21));
        Assert.Null(work.Project(road.Id));
        Assert.Equal(0, carts.Count("timber"));
        Assert.DoesNotContain(colony.Stocks.Ledger, e => e.Change == Change.Made);
        Assert.Equal(1, Work.Recovered(Real.Pieces["door"])["timber"]);         // 3 / 2, rounded down
        Assert.Empty(Work.Recovered(Real.Pieces["floor"]));                     // 1 / 2 rounds to nothing
    }

    [Fact]
    public void Taking_down_a_wall_opens_the_room()
    {
        var (colony, carts) = Founding.Found(7, new Moment(Calendar.TickOf(1, 1, 1, 8, 0)), Real, 24, 24);
        var work = colony.Work!;
        foreach (var (piece, x, y) in Work.Lay(Real.Templates["small_house"], 0, false))
            colony.Ground!.AddPiece(piece, piece == "wall", 27 + x, 20 + y, closes: true);
        Assert.Single(colony.Ground!.Rooms);
        work.TakeDown(27, 22);
        colony.Run(4 * 60);
        Assert.Empty(colony.Ground.Rooms);
        Assert.Equal(61, colony.Stocks[carts.Id].Count("timber"));
    }

    // ---- Two workplaces on their own ----------------------------------------------------------------------------

    [Fact]
    public void Two_workplaces_work_from_their_own_stockpiles_without_a_central_store()
    {
        // Two querns at opposite corners of the test ground, each with a stockpile of wheat beside it, and no
        // other store: each is supplied from its own pile and puts its flour there (library Construction §13.1).
        var colony = new ColonyState(42, Nine, Real);
        colony.Lay(Ground.Test(Real));
        var work = colony.Work!;
        var west = work.AddStockpile("West pile", 2, 2, 2, 2);
        var east = work.AddStockpile("East pile", 43, 43, 2, 2);
        foreach (var pile in new[] { west, east })
            colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, pile.Id, "wheat", 10);
        var westQuern = work.AddSite("quern", 5.5, 2.5);
        var eastQuern = work.AddSite("quern", 42.5, 45.5);
        work.AddWorker("Ada", 4.5, 4.5);
        work.AddWorker("Bram", 41.5, 41.5);
        work.Order(westQuern.Id, "grind_wheat", 1);
        work.Order(eastQuern.Id, "grind_wheat", 1);
        var hauls = new List<(InventoryId From, InventoryId To)>();
        colony.Run(6 * 60, _ =>
        {
            foreach (var j in work.Jobs.Where(j => j.Kind == JobKind.Haul))
                if (!hauls.Contains((j.From, j.To)))
                    hauls.Add((j.From, j.To));
        });
        Assert.Contains(hauls, h => h.From == west.Id && h.To == westQuern.Input);
        Assert.Contains(hauls, h => h.From == east.Id && h.To == eastQuern.Input);
        Assert.DoesNotContain(hauls, h => h.From == west.Id && h.To == eastQuern.Input);
        Assert.DoesNotContain(hauls, h => h.From == east.Id && h.To == westQuern.Input);
        Assert.Equal((4, 4), (west.Count("flour"), east.Count("flour")));
    }

    // ---- The save ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_save_keeps_surfaces_legs_wells_and_takings_down()
    {
        var (colony, ada, _) = Camp(Ground.Test(Real), 20.5, 20.5);
        var work = colony.Work!;
        ada.Legs = 0.6;
        work.Plan("well", 24, 24);
        colony.Ground!.AddPiece("wall", true, 10, 10, closes: true);
        work.TakeDown(10, 10);
        colony.Run(300);
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, Real);
        Assert.Equal(text, ColonySave.Text(loaded));
        Assert.Equal(0.6, loaded.Work!.Workers.Single().Legs);
        Assert.Equal("rough", loaded.Ground!.SurfaceAt(35, 31).Id);
        Assert.Equal(0.55, loaded.Ground.Speed(35, 31));
        colony.Run(600);
        loaded.Run(600);
        Assert.Equal(ColonySave.Fingerprint(colony), ColonySave.Fingerprint(loaded));
        Assert.Contains(loaded.Stocks.Inventories, i => i.Name == "Well");
    }

    public static IEnumerable<object[]> BrokenGrounds()
    {
        var (colony, _, _) = Camp(Ground.Test(Real), 20.5, 20.5);
        colony.Ground!.AddPiece("wall", true, 10, 10, closes: true);
        colony.Work!.TakeDown(10, 10);
        colony.Work.AddSpring(3.5, 3.5);
        string good = ColonySave.Text(colony);
        object[] Case(Action<JsonObject> change, string reason)
        {
            var save = JsonNode.Parse(good)!.AsObject();
            change(save);
            return new object[] { save.ToJsonString(), reason };
        }
        yield return Case(s => s["ground"]!["surfaces"]![0]![2] = 1.5, "ground.surfaces[0] has a speed that cannot be");
        yield return Case(s => s["ground"]!["surface"] = "sand", "ground.surface \"sand\" is not in ground.surfaces");
        yield return Case(s => s["ground"]!["surfaces"]![0]![2] = -1, "ground.surfaces[0] has a speed that cannot be");
        yield return Case(s => s["ground"]!["surfaces"]![0] = new JsonArray("grass", "Grass", 0), "ground.surface \"grass\" is not in ground.surfaces, or cannot be walked on");
        yield return Case(s => s["ground"]!["surfaces"]!.AsArray().Add(s["ground"]!["surfaces"]![0]!.DeepClone()), "ground.surfaces[2] has a speed that cannot be, or is named twice");
        yield return Case(s => s["ground"]!["rows"] = new JsonArray(), "ground.rows should be 48 rows");
        yield return Case(s => s["ground"]!["rows"]![0] = 7, "ground.rows[0] should be 48 letters");
        yield return Case(s => s["ground"]!["pieces"] = new JsonArray(), "work.projects[0] is planned where it cannot be (10, 10)");
    }

    [Theory]
    [MemberData(nameof(BrokenGrounds))]
    public void A_save_with_a_broken_surface_or_taking_down_is_refused(string text, string reason) =>
        Assert.Contains(reason, Assert.Throws<ColonySaveException>(() => ColonySave.Read(text, Real)).Message);
}

static class Waiting
{
    /// <summary>Runs a minute at a time until `done`, failing after `most` minutes.</summary>
    public static void Until(this ColonyState colony, Func<bool> done, int most)
    {
        for (int i = 0; !done(); i++)
        {
            Assert.True(i < most, "it never happened");
            colony.Run(1);
        }
    }
}
