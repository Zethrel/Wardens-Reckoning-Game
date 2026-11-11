// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// The shapes the colony screen places with one drag of the mouse (step 18b, library Review 38): a straight line
/// of tiles (walls, doors, roads) or a filled box (floors, a stockpile). Plain arithmetic, so the tests check it.
/// </summary>
public static class Shapes
{
    /// <summary>
    /// The tiles from (x0, y0) towards (x1, y1) in a straight line, across or down, whichever way the drag went
    /// further (on a tie, across). Walls are built in straight runs; a corner is two drags.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
    {
        var tiles = new List<(int, int)>();
        if (Math.Abs(x1 - x0) >= Math.Abs(y1 - y0))
            for (int i = 0, step = Math.Sign(x1 - x0); i <= Math.Abs(x1 - x0); i++)
                tiles.Add((x0 + i * step, y0));
        else
            for (int i = 0, step = Math.Sign(y1 - y0); i <= Math.Abs(y1 - y0); i++)
                tiles.Add((x0, y0 + i * step));
        return tiles;
    }

    /// <summary>The box with corners (x0, y0) and (x1, y1), whichever way round they are given.</summary>
    public static Area Box(int x0, int y0, int x1, int y1) =>
        new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0) + 1, Math.Abs(y1 - y0) + 1);
}

/// <summary>One tile of a preview: what would go there, and whether it may (and if not, why).</summary>
public sealed record Preview(string What, int X, int Y, Placement Placement);

/// <summary>
/// Placing from the colony screen (step 18b): checking a whole drag or template before it is placed, so the screen
/// can show each tile as it would be, then placing it. The fifth part of `Work`.
/// </summary>
public sealed partial class Work
{
    /// <summary>Each tile of `tiles` checked for `piece` (Check), in order.</summary>
    public IReadOnlyList<Preview> CheckAll(string piece, IEnumerable<(int X, int Y)> tiles) =>
        tiles.Select(t => new Preview(piece, t.X, t.Y, Check(piece, t.X, t.Y))).ToList();

    /// <summary>
    /// Plans `piece` on each of `tiles` where it may go, and leaves the others: a wall dragged across a tree is
    /// built on both sides of it. Returns how many were planned, and the first reason one was not (or null).
    /// </summary>
    public (int Planned, string? Refused) PlanAll(string piece, IEnumerable<(int X, int Y)> tiles)
    {
        int planned = 0;
        string? refused = null;
        foreach (var (x, y) in tiles)
        {
            var (project, placement) = Plan(piece, x, y);
            if (project != null)
                planned++;
            else
                refused ??= "(" + x + ", " + y + "): " + placement.Why;
        }
        return (planned, refused);
    }

    /// <summary>A template's pieces where they would go with its top left corner on (x, y), turned and mirrored,
    /// each checked. PlanTemplate places it only if all may go.</summary>
    public IReadOnlyList<Preview> CheckTemplate(string template, int x, int y, int turns = 0, bool mirror = false)
    {
        if (!content.Templates.TryGetValue(template, out var t))
            throw new ArgumentException("no template \"" + template + "\" in the content", nameof(template));
        return Lay(t, turns, mirror).Select(p => new Preview(p.Piece, x + p.X, y + p.Y, Check(p.Piece, x + p.X, y + p.Y))).ToList();
    }

    /// <summary>
    /// Whether a stockpile may cover `area`: every tile open ground (nothing in the way, no wall, no water) and not
    /// already under another stockpile.
    /// </summary>
    public Placement CheckStockpile(Area area)
    {
        var ground = Ground ?? throw new InvalidOperationException("a stockpile needs ground to lie on");
        foreach (var (x, y) in area.Tiles)
        {
            if (!ground.Inside(x, y))
                return new Placement(false, "(" + x + ", " + y + ") is off the map.");
            if (!ground.Open(x, y))
                return new Placement(false, "(" + x + ", " + y + ") is not open ground.");
            if (Stocks.Inventories.FirstOrDefault(i => i.Area is Area other && other.Covers(x, y)) is Inventory pile)
                return new Placement(false, "(" + x + ", " + y + ") is already under " + pile.Name + ".");
        }
        return new Placement(true);
    }

    /// <summary>A stockpile over `area`, if CheckStockpile allows it, named "Stockpile 1", "Stockpile 2"... (the
    /// lowest number not in use).</summary>
    public (Inventory? Pile, Placement Placement) PlaceStockpile(Area area)
    {
        var check = CheckStockpile(area);
        if (!check.Ok)
            return (null, check);
        var names = Stocks.Inventories.Select(i => i.Name).ToHashSet();
        int n = 1;
        while (names.Contains("Stockpile " + n))
            n++;
        var pile = AddStockpile("Stockpile " + n, area.X, area.Y, area.Width, area.Height);
        Tidy();                                           // what it takes is brought to it at once (step 21c)
        return (pile, check);
    }

    /// <summary>Whether a bedroll may be laid out on tile (x, y): open ground, with no other bedroll on it.</summary>
    public Placement CheckBedroll(int x, int y)
    {
        var ground = Ground ?? throw new InvalidOperationException("a bedroll needs ground to lie on");
        if (!ground.Open(x, y))
            return new Placement(false, ground.Inside(x, y) ? "It is not open ground." : "It is off the map.");
        if (Bedrolls.Any(b => Ground.TileOf(b.X, b.Y) == (x, y)))
            return new Placement(false, "A bedroll is already there.");
        return new Placement(true);
    }

    /// <summary>
    /// Empties a stockpile and then removes it: it takes nothing more, its goods are carried to the other stores
    /// (as the founders' carts are emptied), and once it is empty and nothing is claimed in it, it is gone.
    /// </summary>
    public void RemoveStockpile(InventoryId id)
    {
        var pile = Stocks[id];
        if (pile.Area == null)
            throw new ArgumentException(pile.Name + " is not a stockpile", nameof(id));
        pile.Emptying = true;
        Clear(pile);
        Tidy();
    }

    /// <summary>Closes a stockpile being emptied once nothing is in it or claimed from it (each tick).</summary>
    void ClearStockpiles()
    {
        foreach (var pile in Stocks.Inventories.Where(i => i.Area != null && i.Emptying).ToList())
            Clear(pile);
    }

    void Clear(Inventory pile)
    {
        if (pile.Goods.Count == 0 && Stocks.Reservations.All(r => r.Inventory != pile.Id)
            && jobs.Values.All(j => j.From != pile.Id && j.To != pile.Id))
            Stocks.Close(pile.Id);
    }
}
