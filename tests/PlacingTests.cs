// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 18b: what the colony screen places with a drag or a click, the commands on a tile, and the list of what
/// waits (library Review 38; FP-06). The screen only calls these, so they are checked here.
/// </summary>
public class PlacingTests
{
    static readonly Content Real = Content.Load(Path.Combine(AppContext.BaseDirectory, "content"));
    static readonly Moment Arrival = new(Calendar.TickOf(1, 1, 1, 8, 0));

    static (Colony.Colony Colony, Inventory Carts) Founded() => Founding.Found(7, Arrival, Real, 24, 20);

    // Runs the colony a minute at a time until `done` holds (at most `limit` minutes); false if it never did.
    static bool Until(Colony.Colony colony, Func<bool> done, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            if (done())
                return true;
            colony.Run(1);
        }
        return done();
    }

    [Theory]
    [InlineData(2, 3, 6, 4, "(2,3) (3,3) (4,3) (5,3) (6,3)")]         // further across than down: across
    [InlineData(6, 3, 2, 5, "(6,3) (5,3) (4,3) (3,3) (2,3)")]         // leftwards
    [InlineData(2, 3, 3, 6, "(2,3) (2,4) (2,5) (2,6)")]               // further down: down
    [InlineData(2, 6, 2, 4, "(2,6) (2,5) (2,4)")]                     // upwards
    [InlineData(4, 4, 6, 6, "(4,4) (5,4) (6,4)")]                     // a tie: across
    [InlineData(4, 4, 4, 4, "(4,4)")]                                 // a click: one tile
    public void A_drag_lays_a_straight_line_the_way_it_went_furthest(int x0, int y0, int x1, int y1, string tiles) =>
        Assert.Equal(tiles, string.Join(" ", Shapes.Line(x0, y0, x1, y1).Select(t => "(" + t.X + "," + t.Y + ")")));

    [Fact]
    public void A_box_is_the_same_whichever_corner_the_drag_began_at()
    {
        Assert.Equal(new Area(2, 3, 5, 2), Shapes.Box(2, 3, 6, 4));
        Assert.Equal(new Area(2, 3, 5, 2), Shapes.Box(6, 4, 2, 3));
        Assert.Equal(new Area(7, 7, 1, 1), Shapes.Box(7, 7, 7, 7));
    }

    [Fact]
    public void A_wall_dragged_over_a_rock_is_planned_on_both_sides_and_the_rock_is_named()
    {
        var (colony, _) = Founded();
        var work = colony.Work!;
        // The test ground has rocks at (30, 8) and (31, 8) (construction.json); a wall from (27, 8) to (33, 8) runs over them.
        var line = Shapes.Line(27, 8, 33, 8);
        var preview = work.CheckAll("wall", line);
        Assert.Equal(7, preview.Count);
        Assert.Equal(new[] { true, true, true, false, false, true, true }, preview.Select(p => p.Placement.Ok));
        var (planned, refused) = work.PlanAll("wall", line);
        Assert.Equal(5, planned);
        Assert.Equal("(30, 8): A rock is in the way.", refused);
        Assert.Equal(5, work.Projects.Count);
        Assert.Equal((0, null), work.PlanAll("wall", Array.Empty<(int, int)>()));
    }

    [Fact]
    public void A_template_is_previewed_tile_by_tile_turned_and_mirrored()
    {
        var (colony, _) = Founded();
        var work = colony.Work!;
        var house = Real.Templates["small_house"];
        var straight = work.CheckTemplate("small_house", 30, 26);
        Assert.Equal(house.Pieces.Count, straight.Count);
        Assert.All(straight, p => Assert.True(p.Placement.Ok, p.Placement.Why));
        Assert.Equal(house.Pieces.Select(p => (p.Piece, 30 + p.X, 26 + p.Y)), straight.Select(p => (p.What, p.X, p.Y)));
        // Turned a quarter: as PlanTemplate would lay it.
        var turned = work.CheckTemplate("small_house", 30, 26, turns: 1, mirror: true);
        var (projects, placement) = work.PlanTemplate("small_house", 30, 26, turns: 1, mirror: true);
        Assert.True(placement.Ok);
        Assert.Equal(projects.Select(p => (p.Piece, p.X, p.Y)).OrderBy(p => p), turned.Select(p => (p.What, p.X, p.Y)).OrderBy(p => p));
        // Its corner on the rock at (30, 8): that tile says no, and why.
        Assert.Contains(work.CheckTemplate("small_house", 30, 8), p => !p.Placement.Ok && p.Placement.Why == "A rock is in the way.");
        Assert.Throws<ArgumentException>(() => work.CheckTemplate("castle", 0, 0));
    }

    [Fact]
    public void A_stockpile_goes_only_on_open_ground_not_under_another_and_is_numbered()
    {
        var (colony, _) = Founded();
        var work = colony.Work!;
        Assert.Equal("(30, 8) is not open ground.", work.CheckStockpile(new Area(29, 7, 3, 3)).Why);
        Assert.Equal("(48, 5) is off the map.", work.CheckStockpile(new Area(47, 5, 2, 1)).Why);
        var (first, ok) = work.PlaceStockpile(new Area(2, 30, 3, 3));
        Assert.True(ok.Ok);
        Assert.Equal("Stockpile 1", first!.Name);
        var (none, no) = work.PlaceStockpile(new Area(4, 32, 2, 2));
        Assert.Null(none);
        Assert.Equal("(4, 32) is already under Stockpile 1.", no.Why);
        Assert.Equal("Stockpile 2", work.PlaceStockpile(new Area(10, 30, 1, 1)).Pile!.Name);
        // Stockpile 1 removed: its number is free again.
        work.RemoveStockpile(first.Id);
        Assert.DoesNotContain(colony.Stocks.Inventories, i => i.Id == first.Id);
        Assert.Equal("Stockpile 1", work.PlaceStockpile(new Area(2, 30, 3, 3)).Pile!.Name);
        Assert.Throws<ArgumentException>(() => work.RemoveStockpile(colony.Stocks.Inventories.First(i => i.Area == null).Id));
    }

    [Fact]
    public void A_removed_stockpile_is_emptied_first_and_then_gone()
    {
        var (colony, carts) = Founded();
        var work = colony.Work!;
        var pile = work.PlaceStockpile(new Area(26, 22, 2, 2)).Pile!;
        carts.Emptying = true;
        Assert.True(Until(colony, () => carts.Goods.Count == 0, 24 * 60), "the carts were never emptied");
        int held = pile.Goods.Values.Sum();
        Assert.True(held > 0);
        var other = work.PlaceStockpile(new Area(2, 30, 3, 3)).Pile!;
        work.RemoveStockpile(pile.Id);
        Assert.Contains(" (being emptied)", Inspect.Tile(colony, 26, 22).Lines.Single(l => l.Label == "Store").Text);
        Assert.True(Until(colony, () => colony.Stocks.Inventories.All(i => i.Id != pile.Id), 3 * 24 * 60), "the stockpile was never removed");
        // Everything it held is in the other one (some may have been eaten or drunk on the way).
        Assert.InRange(other.Goods.Values.Sum(), held - 40, held);
        Assert.Contains(pile.Id, colony.Stocks.Closed);
        // The save keeps it closed.
        Assert.Equal(ColonySave.Text(colony), ColonySave.Text(ColonySave.Read(ColonySave.Text(colony), Real)));
    }

    [Fact]
    public void A_stockpile_with_goods_stays_until_they_are_carried_away_and_is_not_offered_twice()
    {
        var (colony, _) = Founded();
        var work = colony.Work!;
        var pile = work.PlaceStockpile(new Area(2, 30, 2, 2)).Pile!;
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, pile.Id, "timber", 5);
        work.RemoveStockpile(pile.Id);
        Assert.Contains(colony.Stocks.Inventories, i => i.Id == pile.Id);          // not while it holds timber
        Assert.DoesNotContain(Inspect.Commands(colony, 2, 30), c => c.Label == "Remove stockpile");
        // An empty store with no area (a crate) is not offered to be emptied.
        var crate = colony.Stocks.Open("Crate", Holder.Store, 20.5, 20.5);
        Assert.DoesNotContain(Inspect.Commands(colony, 20, 20), c => c.Label == "Empty");
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, crate.Id, "timber", 1);
        Assert.Contains(Inspect.Commands(colony, 20, 20), c => c.Label == "Empty");
    }

    [Fact]
    public void A_tile_offers_what_can_be_done_there_and_doing_it_works()
    {
        var (colony, _) = Founded();
        var work = colony.Work!;
        Assert.Empty(Inspect.Commands(colony, 30, 30));
        Assert.Empty(Inspect.Commands(colony, -1, 0));
        var wall = work.Plan("wall", 30, 30).Project!;
        var commands = Inspect.Commands(colony, 30, 30);
        Assert.Equal(new[] { "Pause", "Cancel" }, commands.Select(c => c.Label));
        Assert.Equal(new[] { 'P', 'X' }, commands.Select(c => c.Key));
        commands[0].Run();
        Assert.True(wall.Paused);
        Assert.Equal("Resume", Inspect.Commands(colony, 30, 30)[0].Label);
        Inspect.Commands(colony, 30, 30)[0].Run();
        Assert.False(wall.Paused);
        Inspect.Commands(colony, 30, 30)[1].Run();
        Assert.True(wall.Cancelled);
        Assert.Empty(Inspect.Commands(colony, 30, 30));
        // A finished wall can be taken down; while it is, the commands are those of the taking down.
        colony.Ground!.AddPiece("wall", true, 31, 31);
        var down = Assert.Single(Inspect.Commands(colony, 31, 31));
        Assert.Equal(("Take down", 'X'), (down.Label, down.Key));
        down.Run();
        Assert.Equal(new[] { "Pause", "Leave standing" }, Inspect.Commands(colony, 31, 31).Select(c => c.Label));
        // A well (two by two) cannot be taken down yet: no command for it.
        colony.Ground.AddPiece("well", true, 34, 34);
        Assert.Empty(Inspect.Commands(colony, 34, 34));
        // A stockpile: removed from any of its tiles.
        var pile = work.PlaceStockpile(new Area(2, 30, 2, 2)).Pile!;
        var remove = Assert.Single(Inspect.Commands(colony, 3, 31));
        Assert.Equal(("Remove stockpile", 'R'), (remove.Label, remove.Key));
        remove.Run();
        Assert.DoesNotContain(colony.Stocks.Inventories, i => i.Id == pile.Id);
    }

    [Fact]
    public void A_bedroll_is_moved_to_a_tile_picked_and_the_carts_are_emptied_on_command()
    {
        var (colony, carts) = Founded();
        var work = colony.Work!;
        var bed = work.Bedrolls.First();
        var (bx, by) = Ground.TileOf(bed.X, bed.Y);
        var move = Inspect.Commands(colony, bx, by).Single(c => c.Label == "Move bedroll");
        Assert.Equal('M', move.Key);
        Assert.Null(move.Run);                                   // it needs a tile first
        Assert.Equal("It is not open ground.", move.CheckAt!(30, 8).Why);              // the rock
        Assert.Equal("It is off the map.", move.CheckAt(-1, 0).Why);
        var other = work.Bedrolls.Last();
        Assert.Equal("A bedroll is already there.", move.CheckAt(Ground.TileOf(other.X, other.Y).X, Ground.TileOf(other.X, other.Y).Y).Why);
        Assert.True(move.CheckAt(10, 10).Ok);
        move.RunAt!(10, 10);
        Assert.Equal((10.5, 10.5), (work.Bedrolls.First(b => b.Id == bed.Id).X, work.Bedrolls.First(b => b.Id == bed.Id).Y));
        // The carts: "Empty", then they are being emptied, and the command is gone.
        var (cx, cy) = Ground.TileOf(carts.X, carts.Y);
        var empty = Inspect.Commands(colony, cx, cy).Single(c => c.Label == "Empty");
        Assert.Equal('E', empty.Key);
        empty.Run!();
        Assert.True(carts.Emptying);
        Assert.DoesNotContain(Inspect.Commands(colony, cx, cy), c => c.Label == "Empty");
        // The drinking place is never offered to be emptied.
        var spring = colony.Stocks[work.Springs.First()];
        Assert.DoesNotContain(Inspect.Commands(colony, Ground.TileOf(spring.X, spring.Y).X, Ground.TileOf(spring.X, spring.Y).Y), c => c.Label == "Empty");
    }

    [Fact]
    public void What_waits_is_listed_with_why_and_the_trace_only_when_asked()
    {
        var (colony, _) = Founded();
        var work = colony.Work!;
        Assert.Empty(Inspect.Waiting(colony));
        var wall = work.Plan("wall", 30, 30).Project!;
        work.Pause(wall.Id, true);
        var waiting = Assert.Single(Inspect.Waiting(colony));
        Assert.Equal((30, 30, "Timber wall"), (waiting.X, waiting.Y, waiting.What));
        Assert.Equal(work.WhyBuild(wall.Id), waiting.Why);
        Assert.DoesNotContain(Inspect.Tile(colony, 30, 30).Lines, l => l.Label == "Trace");
        var traced = Inspect.Tile(colony, 30, 30, trace: true);
        Assert.Equal(waiting.Why.Trace, traced.Lines.Single(l => l.Label == "Trace").Text);
        Assert.NotEqual(waiting.Why.Player, waiting.Why.Trace);
    }
}
