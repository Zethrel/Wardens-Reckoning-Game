// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using Xunit.Abstractions;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 15b: rooms, stockpiles, templates, and the founders' arrival, with the maintainer's first-day target (library
/// Review 33): the founders can build one medium building on their first day, to house their bedrolls and supplies.
/// </summary>
public class FoundingTests(ITestOutputHelper output)
{
    static readonly Content Real = TestContent.Real;
    static readonly Moment Arrival = new(Calendar.TickOf(1, 1, 1, 8, 0));

    // ---- The founders ------------------------------------------------------------------------------------------

    [Fact]
    public void The_founders_arrive_with_their_supplies()
    {
        var (colony, carts) = Founding.Found(7, Arrival, Real, 24, 24);
        var work = colony.Work!;
        Assert.Equal(3, work.Workers.Count);
        Assert.Equal(3, work.Workers.Select(w => w.Name).Distinct().Count());
        Assert.All(work.Workers, w => Assert.Contains(w.Name, Real.Founding.Names));
        Assert.Equal((60, 30, 50, 60), (carts.Count("timber"), carts.Count("ration"), carts.Count("wheat"), carts.Count("water")));
        Assert.Equal(4, colony.Stocks.Ledger.Count(e => e.Change == Change.Arrived && e.To == carts.Id));
        Assert.Equal(work.Workers.Select(w => w.Id).OrderBy(i => i.Value), work.Bedrolls.Select(b => b.Owner!.Value).OrderBy(i => i.Value));
        var tiles = work.Workers.Select(w => Ground.TileOf(w.X, w.Y)).Concat(work.Bedrolls.Select(b => Ground.TileOf(b.X, b.Y))).ToList();
        Assert.Equal(tiles.Count, tiles.Distinct().Count());                     // nobody on top of another
        Assert.All(tiles, t => Assert.True(colony.Ground!.Open(t.X, t.Y)));
        Assert.Single(work.Springs);
    }

    [Fact]
    public void The_same_seed_brings_the_same_founders()
    {
        string Names(long seed) => string.Join(",", Founding.Found(seed, Arrival, Real, 24, 24).Colony.Work!.Workers.Select(w => w.Name));
        Assert.Equal(Names(7), Names(7));
        Assert.Contains(Enumerable.Range(1, 20).Select(s => Names(s)), n => n != Names(7));   // other seeds, other founders
        foreach (int seed in Enumerable.Range(1, 60))                                      // and never a name twice
            Assert.Equal(3, Names(seed).Split(',').Distinct().Count());
        Assert.Equal(ColonySave.Text(Founding.Found(7, Arrival, Real, 24, 24).Colony), ColonySave.Text(Founding.Found(7, Arrival, Real, 24, 24).Colony));
    }

    [Fact]
    public void A_bedroll_is_laid_only_on_open_ground()
    {
        var (colony, _) = Founding.Found(7, Arrival, Real, 24, 24);
        var bed = colony.Work!.Bedrolls.First();
        Assert.Throws<ArgumentException>(() => colony.Work.MoveBedroll(bed.Id, 30.5, 8.5));       // a rock
        Assert.Equal((10.5, 10.5), (colony.Work.MoveBedroll(bed.Id, 10.5, 10.5).X, colony.Work.Bedrolls.First().Y));
    }

    [Fact]
    public void Founders_cannot_arrive_on_a_rock()
    {
        Assert.Throws<ArgumentException>(() => Founding.Found(7, Arrival, Real, 30, 8));
    }

    // ---- Rooms -----------------------------------------------------------------------------------------------

    /// <summary>A ring of walls from (4, 4) to (8, 8) on a 12 by 12 ground, with `door` (if given) in it.</summary>
    static Ground Ring((int X, int Y)? door = null, (int X, int Y)? gap = null)
    {
        var ground = new Ground(12, 12);
        for (int i = 4; i <= 8; i++)
            foreach (var (x, y) in new[] { (i, 4), (i, 8), (4, i), (8, i) })
                if (ground.PieceAt(x, y) == null && (x, y) != gap)
                    if ((x, y) == door)
                        ground.AddPiece("door", false, x, y, closes: true);
                    else
                        ground.AddPiece("wall", true, x, y);
        return ground;
    }

    [Fact]
    public void Walls_all_round_make_a_room_and_a_door_keeps_it_one()
    {
        var closed = Ring();
        var room = Assert.Single(closed.Rooms);
        Assert.Equal(9, room.Count);                                            // 3 by 3 inside
        Assert.True(closed.Indoors(6, 6));
        Assert.False(closed.Indoors(2, 2));
        Assert.False(closed.Indoors(4, 4));                                     // a wall's tile is not indoors
        var withDoor = Ring(door: (6, 4));
        Assert.Equal(9, Assert.Single(withDoor.Rooms).Count);
        Assert.False(withDoor.Indoors(6, 4));                                   // nor is the door's
        Assert.Empty(Ring(gap: (6, 4)).Rooms);                                  // a gap lets the outside in
        var floored = Ring(gap: (6, 4));
        floored.AddPiece("floor", false, 6, 4, closes: false);
        Assert.Empty(floored.Rooms);                                            // a floor does not close it
    }

    [Fact]
    public void Corners_that_only_touch_still_close_a_room_and_the_edge_never_does()
    {
        // A diamond: walls touching only at their corners. Nobody can walk through a slanting gap, so it is closed.
        var ground = new Ground(9, 9);
        foreach (var (x, y) in new[] { (4, 2), (5, 3), (6, 4), (5, 5), (4, 6), (3, 5), (2, 4), (3, 3) })
            ground.AddPiece("wall", true, x, y);
        Assert.Equal(new[] { (4, 3), (3, 4), (4, 4), (5, 4), (4, 5) }, Assert.Single(ground.Rooms));
        // Walls against the edge of the ground: the space they make reaches the edge, so it is outdoors.
        var corner = new Ground(6, 6);
        foreach (var (x, y) in new[] { (2, 0), (2, 1), (2, 2), (1, 2), (0, 2) })
            corner.AddPiece("wall", true, x, y);
        Assert.Empty(corner.Rooms);
        var bottom = new Ground(6, 6);                                          // and against the bottom edge
        foreach (var (x, y) in new[] { (1, 5), (1, 4), (1, 3), (2, 3), (3, 3), (3, 4), (3, 5) })
            bottom.AddPiece("wall", true, x, y);
        Assert.Empty(bottom.Rooms);
        Assert.False(bottom.Indoors(2, 4));
        // Two rooms are listed by their first tile; building forgets the old answer.
        var two = Ring();
        Assert.Single(two.Rooms);
        for (int y = 5; y <= 7; y++)
            two.AddPiece("wall", true, 6, y);
        Assert.Equal(2, two.Rooms.Count);
        Assert.Equal((5, 5), two.Rooms[0][0]);
        Assert.Equal((7, 5), two.Rooms[1][0]);
        Assert.Equal(1, two.RoomAt(7, 6));
    }

    // ---- Templates ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_template_is_planned_whole_turned_or_mirrored()
    {
        var house = Real.Templates["small_house"];
        Assert.Equal((6, 6), (house.Width, house.Height));
        Assert.Equal(19, house.Pieces.Count(p => p.Piece == "wall"));
        var door = Assert.Single(house.Pieces, p => p.Piece == "door");
        Assert.Equal((2, 5), (door.X, door.Y));
        (int, int) Door(int turns, bool mirror) => Work.Lay(house, turns, mirror).Where(p => p.Piece == "door").Select(p => (p.X, p.Y)).Single();
        Assert.Equal((3, 5), Door(0, true));                                    // flipped left to right
        Assert.Equal((0, 2), Door(1, false));                                   // a quarter turn: the bottom goes left
        Assert.Equal((3, 0), Door(2, false));
        Assert.Equal((5, 3), Door(3, false));
        Assert.Equal(Door(0, false), Door(4, false));
        Assert.Equal(Door(1, false), Door(-3, false));
        // Every turn keeps the same 20 tiles of the 6 by 6 ring.
        var ring = Work.Lay(house, 0, false).Select(p => (p.X, p.Y)).OrderBy(p => p).ToList();
        foreach (int turns in new[] { 1, 2, 3 })
            Assert.Equal(ring, Work.Lay(house, turns, true).Select(p => (p.X, p.Y)).OrderBy(p => p));

        var (colony, _) = Founding.Found(7, Arrival, Real, 24, 24);
        var (planned, placement) = colony.Work!.PlanTemplate("small_house", 27, 20, turns: 1);
        Assert.True(placement.Ok);
        Assert.Equal(20, planned.Count);
        Assert.Equal("door", colony.Work.Projects.Single(p => (p.X, p.Y) == (27, 22)).Piece);
    }

    [Fact]
    public void A_template_that_does_not_fit_plans_nothing_and_says_where()
    {
        var (colony, _) = Founding.Found(7, Arrival, Real, 24, 24);
        var work = colony.Work!;
        var (none, refused) = work.PlanTemplate("small_house", 25, 5);          // its right wall over the rock at (30, 8)
        Assert.Empty(none);
        Assert.Equal("At (30, 8): A rock is in the way.", refused.Why);
        Assert.Empty(work.Projects);
        Assert.Equal("At (48, 20): It is outside the ground (48 by 48 tiles).", work.PlanTemplate("small_house", 43, 20).Placement.Why);
        Assert.Throws<ArgumentException>(() => work.PlanTemplate("castle", 1, 1));
    }

    // ---- Stockpiles and emptying ---------------------------------------------------------------------------------

    [Fact]
    public void A_stockpile_lies_on_open_tiles_and_keeps_walls_off_them()
    {
        var (colony, _) = Founding.Found(7, Arrival, Real, 24, 24);
        var work = colony.Work!;
        Assert.Throws<ArgumentException>(() => work.AddStockpile("On the rocks", 29, 7, 3, 3));
        var pile = work.AddStockpile("Yard", 10, 10, 3, 2);
        Assert.Equal((11.5, 11.0), (pile.X, pile.Y));
        Assert.Equal("A store (Yard) is there: a timber wall would block it.", work.Check("wall", 12, 11).Why);
        Assert.True(work.Check("wall", 13, 11).Ok);
        Assert.True(work.Check("floor", 12, 11).Ok);
        Assert.Throws<InvalidOperationException>(() => new ColonyState(1, Arrival, Real).Work!.AddStockpile("Nowhere", 0, 0, 1, 1));
    }

    [Fact]
    public void Emptied_carts_go_to_the_stockpile_when_there_is_nothing_else_to_do()
    {
        var (colony, carts) = Founding.Found(7, Arrival, Real, 24, 24);
        var work = colony.Work!;
        var pile = work.AddStockpile("Yard", 10, 24, 4, 4);
        carts.Emptying = true;
        Assert.False(carts.Takes("timber"));                                     // nothing is brought to it now
        work.Order(work.AddSite("quern", 20.5, 30.5).Id, "grind_wheat", 1);      // other work: a grinding
        colony.Run(10);
        Assert.DoesNotContain(work.Jobs, j => j.From == carts.Id && j.To == pile.Id);   // the grinding's haul comes first
        colony.RunMinutes(16 * 60, _ =>
        {
            // Never more than one load waits to be carried, so the rest stays free (to eat).
            Assert.True(work.Jobs.Count(j => j.From == carts.Id && j.To == pile.Id && j.Step == Step.Open) <= 1);
            Assert.Equal(colony.Stocks.Accounted("ration"), colony.Stocks.Total("ration"));
        });
        Assert.Empty(colony.Stocks[carts.Id].Goods);
        Assert.Equal(60, pile.Count("timber"));
        Assert.Equal(colony.Stocks.Total("ration"), pile.Count("ration"));
    }

    [Fact]
    public void The_carts_are_emptied_one_load_at_a_time()
    {
        // With only one founder working, the loads still wait one at a time: never several claimed at once.
        var (colony, carts) = Founding.Found(7, Arrival, Real, 24, 24);
        var work = colony.Work!;
        foreach (var w in work.Workers.Skip(1))
            work.Stop(w.Id);
        var pile = work.AddStockpile("Yard", 10, 24, 4, 4);
        carts.Emptying = true;
        colony.RunMinutes(8 * 60, _ => Assert.True(work.Jobs.Count(j => j.From == carts.Id && j.Step == Step.Open) <= 1));
        Assert.True(pile.Count("timber") > 0);
        Assert.Equal(colony.Stocks.Total("ration"), pile.Count("ration"));
    }

    // ---- The first day: the maintainer's target ------------------------------------------------------------------

    [Fact]
    public void On_their_first_day_the_founders_build_a_house_for_their_bedrolls_and_supplies()
    {
        // They arrive at 08:00 on the first day and the player plans the small house (6 by 6, 19 walls and a door)
        // next to the carts, with a stockpile inside. Once its room is closed, the player lays the bedrolls inside
        // and empties the carts into the stockpile.
        // On the content as shipped, at the 60-minute day's pace (step 24b: the balance is the point here).
        var (colony, carts) = Founding.Found(7, Arrival, TestContent.Shipped, 24, 24);
        var work = colony.Work!;
        var ground = colony.Ground!;
        var (house, placement) = work.PlanTemplate("small_house", 27, 20);
        Assert.True(placement.Ok, placement.Why);
        var pile = work.AddStockpile("House store", 28, 21, 4, 4);
        long? housed = null;
        colony.RunMinutes(16 * 60, c =>
        {
            Assert.Equal(c.Stocks.Accounted("timber"), c.Stocks.Total("timber"));
            if (housed == null && ground.Rooms.Count == 1 && work.Projects.Count == 0)
            {
                housed = c.Now.Tick;
                int i = 0;
                foreach (var bed in work.Bedrolls.ToList())
                    work.MoveBedroll(bed.Id, 28.5 + i++, 24.5);
                carts.Emptying = true;
            }
        });
        Assert.NotNull(housed);
        var done = new Moment(housed!.Value);
        output.WriteLine("The house stood at " + done + " (" + (housed - Arrival.Tick) / Calendar.TicksPerMinute + " minutes after arrival).");
        Assert.Equal(Arrival.AbsoluteDay, done.AbsoluteDay);                     // on the first day
        // The house: every piece stands, and it closes one room of 16 tiles.
        Assert.Equal(19, house.Count(p => ground.PieceAt(p.X, p.Y) == "wall"));
        Assert.Equal("door", ground.PieceAt(29, 25));
        Assert.Equal(16, Assert.Single(ground.Rooms).Count);
        // Housed: the bedrolls and the stockpile are indoors, and everything the carts held is in the stockpile.
        Assert.All(work.Bedrolls, b => Assert.True(ground.Indoors(Ground.TileOf(b.X, b.Y).X, Ground.TileOf(b.X, b.Y).Y)));
        Assert.All(pile.Area!.Tiles, t => Assert.True(ground.Indoors(t.X, t.Y)));
        Assert.Empty(colony.Stocks[carts.Id].Goods);
        Assert.Equal((60 - 41, 50), (pile.Count("timber"), pile.Count("wheat")));
        Assert.Equal(colony.Stocks.Total("ration"), pile.Count("ration"));
        output.WriteLine("Rations left: " + pile.Count("ration") + "; the day ended at " + colony.Now + ".");
    }

    // ---- The save ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_save_keeps_the_stockpiles_and_the_emptying()
    {
        var (colony, carts) = Founding.Found(7, Arrival, Real, 24, 24);
        var pile = colony.Work!.AddStockpile("Yard", 10, 24, 4, 4);
        carts.Emptying = true;
        colony.RunMinutes(30);
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, Real);
        Assert.Equal(text, ColonySave.Text(loaded));
        Assert.Equal(new Area(10, 24, 4, 4), loaded.Stocks[pile.Id].Area);
        Assert.True(loaded.Stocks[carts.Id].Emptying);
        colony.RunMinutes(300);
        loaded.RunMinutes(300);
        Assert.Equal(ColonySave.Fingerprint(colony), ColonySave.Fingerprint(loaded));
    }

    public static IEnumerable<object[]> BrokenPiles()
    {
        var (colony, _) = Founding.Found(7, Arrival, Real, 24, 24);
        colony.Work!.AddStockpile("Yard", 10, 24, 4, 4);
        string good = ColonySave.Text(colony);
        object[] Case(Action<JsonObject> change, string reason)
        {
            var save = JsonNode.Parse(good)!.AsObject();
            change(save);
            return new object[] { save.ToJsonString(), reason };
        }
        JsonObject Yard(JsonObject s) => s["inventories"]!.AsArray().First(i => i!["name"]!.ToString() == "Yard")!.AsObject();
        yield return Case(s => Yard(s)["area"] = new JsonArray(46, 46, 4, 4), "the stockpile \"Yard\" lies off the ground");
        yield return Case(s => Yard(s)["area"] = new JsonArray(10, 24, 0, 4), "is not a block of tiles of a store");
        yield return Case(s => Yard(s)["area"] = new JsonArray(10, 24), "is not [x, y, width, height]");
        yield return Case(s => Yard(s)["emptying"] = "no", "emptying is not true or false");
    }

    [Theory]
    [MemberData(nameof(BrokenPiles))]
    public void A_save_with_a_broken_stockpile_is_refused(string text, string reason) =>
        Assert.Contains(reason, Assert.Throws<ColonySaveException>(() => ColonySave.Read(text, Real)).Message);
}
