// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;

namespace WardensReckoning.Colony;

/// <summary>One line of an inspection: a label ("Doing") and what it says ("carrying timber to the house").</summary>
public sealed record Line(string Label, string Text);

/// <summary>What the inspector panel shows for a tile or a colonist: a title and its lines.</summary>
public sealed record Inspection(string Title, IReadOnlyList<Line> Lines);

/// <summary>
/// Something the player can do from the inspector (step 18b): its button's words, the key that does it too, what it
/// does in a sentence, and doing it. A command that needs a second tile (moving a bedroll there) has no `Run`, but
/// `CheckAt` (whether that tile will do, and why not) and `RunAt` (doing it there): the screen lets the player pick
/// the tile first.
/// </summary>
public sealed record Command(string Label, char Key, string Tooltip, Action? Run,
                             Func<int, int, Placement>? CheckAt = null, Action<int, int>? RunAt = null);

/// <summary>A building that waits (FP-06): where, what, and why in the player's words (with the next step) and in
/// the developer's.</summary>
public sealed record Waiting(int X, int Y, string What, Explanation Why);

/// <summary>
/// What the colony screen shows when the player inspects a tile or a colonist (step 18a, library Review 38;
/// Shared Systems §19.2: identity, where, what it is doing, needs, what it carries, why something waits).
///
/// The screen (src/UI, Godot) only draws what these return: the words are made here, in plain .NET, so the tests
/// can check them, and so the screen cannot change the colony by looking at it (§19.1: closing a panel or changing
/// what is selected never alters the simulation). Nothing here changes anything.
/// </summary>
public static class Inspect
{
    /// <summary>The colonist nearest the middle of tile-space point (x, y), within `reach` metres, or null.</summary>
    public static Worker? WorkerAt(Colony colony, double x, double y, double reach = 0.7)
    {
        Worker? best = null;
        double near = reach * reach;
        foreach (var w in colony.Work?.Workers ?? Array.Empty<Worker>())
        {
            double d = (w.X - x) * (w.X - x) + (w.Y - y) * (w.Y - y);
            if (d <= near)
                (best, near) = (w, d);
        }
        return best;
    }

    /// <summary>
    /// A colonist: what they are doing, where, their needs (each a level of 100 with the word the needs content
    /// gives that level: "fine", "peckish", "wants", "urgent", "critical"), their schedule's block now, and what they
    /// carry, and an illness once it shows (with `trace`, the developer's: one caught that has not shown yet too).
    /// </summary>
    public static Inspection Worker(Colony colony, ColonistId id, bool trace = false)
    {
        var work = colony.Work ?? throw new InvalidOperationException("the colony has no colonists");
        var w = work.Workers.First(x => x.Id == id);
        var lines = new List<Line>
        {
            new("Doing", work.Doing(id)),
            new("Where", Where(colony, w.X, w.Y)),
        };
        foreach (var (need, state) in w.Needs)
        {
            var rule = Rules(colony).Needs[need];
            lines.Add(new Line(Capital(rule.Name), Whole(state.Level) + " of 100, " + Word(rule, state.Level)));
        }
        lines.Add(new Line("Schedule", w.Schedule + ", now " + work.BlockOf(w).ToString().ToLowerInvariant()));
        lines.Add(new Line("Carrying", Goods(colony, colony.Stocks[w.Arms].Goods, trace)));
        if (w.Lacking != null)
            lines.Add(new Line("Lacking", w.Lacking));
        if (w.Illness is Illness illness && work.Ill(w))
            lines.Add(new Line("Unwell", illness.Name + ": working slower, thirsty and tired; until " + new Moment(illness.Ends)));
        else if (trace && w.Illness is Illness coming)
            lines.Add(new Line("Trace", "caught " + coming.Name.ToLowerInvariant() + ", shows " + new Moment(coming.Shows)));
        return new Inspection(w.Name, lines);
    }

    /// <summary>The word for a need's level: "fine" below peckish, then "peckish", "wants", "urgent", "critical".</summary>
    public static string Word(NeedRule rule, double level) =>
        level >= rule.Critical ? "critical" : level >= rule.Urgent ? "urgent" : level >= rule.Wants ? "wants"
        : level >= rule.Peckish ? "peckish" : "fine";

    /// <summary>
    /// A tile: its surface (and how fast it is walked), what is in the way or built on it, a building under way and
    /// why it waits, the stores, the drinking place and bedrolls on it, whether it is indoors, and who stands there.
    /// </summary>
    public static Inspection Tile(Colony colony, int x, int y, bool trace = false)
    {
        var ground = colony.Ground ?? throw new InvalidOperationException("the colony has no ground");
        if (!ground.Inside(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), "(" + x + ", " + y + ") is off the ground");
        var surface = ground.SurfaceAt(x, y);
        var lines = new List<Line>
        {
            new("Ground", surface.Name + (surface.CanWalk ? ", walked at " + Whole(ground.Speed(x, y) * 100) + "%" : ", cannot be walked on")),
        };
        if (ground.ObstacleAt(x, y) is string obstacle)
            lines.Add(new Line("In the way", Capital(obstacle)));
        if (ground.PieceAt(x, y) is string piece)
            lines.Add(new Line("Built", Rules(colony).Pieces[piece].Name));
        var work = colony.Work;
        foreach (var p in work?.Projects ?? Array.Empty<Project>())
        {
            var size = Rules(colony).Pieces[p.Piece];
            if (x < p.X || y < p.Y || x >= p.X + Math.Max(1, size.Width) || y >= p.Y + Math.Max(1, size.Height))
                continue;
            string what = (p.Removing ? "Taking down: " : "Building: ") + size.Name;
            lines.Add(new Line("Project", what + (p.Paused ? " (paused)" : "")));
            if (work!.WhyBuild(p.Id) is Explanation why)
            {
                lines.Add(new Line("Waiting", why.Player));
                if (why.NextStep != "")
                    lines.Add(new Line("Next step", why.NextStep));
                // The developer's trace (FP-06: told apart from the player's words by its label, and shown only
                // when asked for: F3).
                if (trace)
                    lines.Add(new Line("Trace", why.Trace));
            }
        }
        foreach (var inventory in colony.Stocks.Inventories.Where(i => i.Holder == Holder.Store && On(i, x, y)))
        {
            bool spring = work?.Springs.Contains(inventory.Id) == true;
            string holds = inventory.Capacity is int capacity ? " (of " + capacity + ")" : "";
            lines.Add(new Line(spring ? "Water" : "Store", inventory.Name + ": " + Goods(colony, inventory.Goods, trace) + holds
                                                         + (inventory.Emptying ? " (being emptied)" : "")));
            if (spring)
                lines.Add(new Line("Source", Source(colony, work!, inventory.Id)));
            if (inventory.Mark != WaterMark.None)
                lines.Add(new Line("Marked", inventory.Mark == WaterMark.BoilFirst ? "boil first" : "do not drink"));
            if (work?.WhyNotBoiled(inventory.Id) is string why)
                lines.Add(new Line("Boiling", why));
            if (trace && spring && work!.SourceOf(inventory.Id) is { FouledUntil: long until } fouled)
                lines.Add(new Line("Trace", "fouled until " + new Moment(until) + (fouled.Sign is string s ? "; sign: " + s + (fouled.SignSeen ? " (seen)" : "") : "; no sign")));
        }
        foreach (var bed in work?.Bedrolls ?? Array.Empty<Bedroll>())
            if (Ground.TileOf(bed.X, bed.Y) == (x, y))
            {
                string owner = bed.Owner is ColonistId o ? work!.Workers.FirstOrDefault(w => w.Id == o)?.Name ?? "" : "";
                lines.Add(new Line("Bedroll", owner == "" ? "nobody's" : owner + "'s"));
            }
        if (surface.CanWalk)
            lines.Add(new Line("Shelter", ground.Indoors(x, y) ? "indoors (a room)" : "outdoors"));
        var here = (work?.Workers ?? Array.Empty<Worker>()).Where(w => Ground.TileOf(w.X, w.Y) == (x, y)).Select(w => w.Name).ToList();
        if (here.Count > 0)
            lines.Add(new Line("Here", string.Join(", ", here)));
        return new Inspection("Tile (" + x + ", " + y + ")", lines);
    }

    /// <summary>
    /// What the player can do on tile (x, y) (step 18b, library Review 38): pause or resume a building under way,
    /// cancel it; take down a finished piece; move a bedroll; empty the carts (or another store) into the
    /// stockpiles; empty and remove a stockpile. Each command's key is shown on its
    /// button, so the keyboard alone can do it too (FP-11).
    /// </summary>
    public static IReadOnlyList<Command> Commands(Colony colony, int x, int y)
    {
        var work = colony.Work;
        var ground = colony.Ground;
        var commands = new List<Command>();
        if (work == null || ground == null || !ground.Inside(x, y))
            return commands;
        bool underWay = false;
        foreach (var p in work.Projects.Where(p => !p.Cancelled))
        {
            var size = Rules(colony).Pieces[p.Piece];
            if (x < p.X || y < p.Y || x >= p.X + Math.Max(1, size.Width) || y >= p.Y + Math.Max(1, size.Height))
                continue;
            underWay = true;
            var id = p.Id;
            commands.Add(p.Paused
                ? new Command("Resume", 'P', "Go on with this " + size.Name.ToLowerInvariant(), () => work.Pause(id, false))
                : new Command("Pause", 'P', "Stop work on this " + size.Name.ToLowerInvariant() + " for now; it keeps its progress", () => work.Pause(id, true)));
            commands.Add(new Command(p.Removing ? "Leave standing" : "Cancel", 'X',
                p.Removing ? "Stop taking it down" : "Give up this " + size.Name.ToLowerInvariant() + "; what was brought is carried back",
                () => work.CancelBuild(id)));
        }
        if (!underWay && ground.PieceAt(x, y) is string piece)
        {
            var kind = Rules(colony).Pieces[piece];
            if (kind.Width <= 1 && kind.Height <= 1 && kind.HoldsLitres == 0)
                commands.Add(new Command("Take down", 'X', "Take down this " + kind.Name.ToLowerInvariant() + "; half its materials come back",
                    () => work.TakeDown(x, y)));
        }
        foreach (var bed in work.Bedrolls.Where(b => Ground.TileOf(b.X, b.Y) == (x, y)).ToList())
        {
            var id = bed.Id;
            commands.Add(new Command("Move bedroll", 'M', "Lay this bedroll out somewhere else (pick the tile)", null,
                work.CheckBedroll, (tx, ty) => work.MoveBedroll(id, tx + 0.5, ty + 0.5)));
        }
        var springs = work.Springs.ToHashSet();
        foreach (var store in colony.Stocks.Inventories.Where(i => i.Holder == Holder.Store && i.Area == null && !i.Emptying
                     && !springs.Contains(i.Id) && i.Capacity == null && i.Goods.Count > 0 && Ground.TileOf(i.X, i.Y) == (x, y)).ToList())
            commands.Add(new Command("Empty", 'E', "Carry everything in " + store.Name + " to the stockpiles", () => store.Emptying = true));
        // Water marks (step 19b): on a source, a barrel, a basin, the carts, a stockpile: any store water may be in.
        string drinkGood = Rules(colony).Drink.Good;
        foreach (var store in colony.Stocks.Inventories.Where(i => i.Holder == Holder.Store && On(i, x, y)
                     && (i.Accepts.Count == 0 || i.Accepts.Contains(drinkGood) || Rules(colony).Drinkable.Any(g => i.Count(g) > 0))).ToList())
        {
            var id = store.Id;
            if (store.Mark != WaterMark.BoilFirst)
                commands.Add(new Command("Boil first", 'K', "Do not drink the water of " + store.Name + " as it is: boil it first", () => work.SetMark(id, WaterMark.BoilFirst)));
            if (store.Mark != WaterMark.DoNotDrink)
                commands.Add(new Command("Do not drink", 'N', "Nobody drinks the water of " + store.Name + " (it may still be washed in)", () => work.SetMark(id, WaterMark.DoNotDrink)));
            if (store.Mark != WaterMark.None)
                commands.Add(new Command("Drink freely", 'U', "The water of " + store.Name + " is drunk as it is again", () => work.SetMark(id, WaterMark.None)));
        }
        foreach (var pile in colony.Stocks.Inventories.Where(i => i.Area is Area a && a.Covers(x, y) && !i.Emptying).ToList())
        {
            var id = pile.Id;
            commands.Add(new Command("Remove stockpile", 'R', "Carry everything in " + pile.Name + " to the other stores, then remove it",
                () => work.RemoveStockpile(id)));
        }
        return commands;
    }

    // "a brook, 250 litres a day, frozen: nothing to draw until spring".
    static string Source(Colony colony, Work work, InventoryId id)
    {
        var source = work.SourceOf(id);
        string what = source.Kind switch
        {
            WaterKind.River => "a river, more than the colony draws",
            WaterKind.Lake => "a lake, more than the colony draws",
            WaterKind.Brook => "a brook, " + work.LitresOf(id) + " litres a day this month",
            WaterKind.Well => "a well, " + work.LitresOf(id) + " litres a day",
            _ => "a spring, " + work.LitresOf(id) + " litres a day",
        };
        if (source.Frozen)
            what += source.Kind == WaterKind.Brook ? "; frozen: nothing to draw until the thaw" : "; frozen over: an ice hole must be cut";
        return what;
    }

    /// <summary>Every building under way that waits, and why (FP-06), in the order they were planned.</summary>
    public static IReadOnlyList<Waiting> Waiting(Colony colony)
    {
        var list = new List<Waiting>();
        foreach (var p in colony.Work?.Projects ?? Array.Empty<Project>())
            if (colony.Work!.WhyBuild(p.Id) is Explanation why)
                list.Add(new Waiting(p.X, p.Y, (p.Removing ? "Taking down: " : "") + Rules(colony).Pieces[p.Piece].Name, why));
        return list;
    }

    // Whether an inventory is on tile (x, y): its area covers it, or it stands there.
    static bool On(Inventory inventory, int x, int y) =>
        inventory.Area is Area area ? area.Covers(x, y) : Ground.TileOf(inventory.X, inventory.Y) == (x, y);

    // "(12, 40), on grass".
    static string Where(Colony colony, double x, double y)
    {
        var (tx, ty) = Ground.TileOf(x, y);
        string on = colony.Ground is Ground g && g.Inside(tx, ty) ? ", on " + g.SurfaceAt(tx, ty).Name.ToLowerInvariant() : "";
        return "(" + tx + ", " + ty + ")" + on;
    }

    /// <summary>Goods in words, by name: "60 Timber, 30 Travel ration", or "nothing". Fouled water looks like water
    /// (step 19b: the truth is not shown to the player): it is counted as water, unless `trace` (the developer's
    /// view) asks for the truth.</summary>
    public static string Goods(Colony colony, IReadOnlyDictionary<string, int> goods, bool trace = false)
    {
        var rules = Rules(colony);
        var seen = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (good, n) in goods.Where(g => g.Value > 0))
        {
            string shown = !trace && good == rules.Sanitation.FouledGood ? rules.Drink.Good : good;
            seen[shown] = seen.GetValueOrDefault(shown) + n;
        }
        var listed = seen.Select(g => g.Value.ToString(CultureInfo.InvariantCulture) + " " + rules.Goods[g.Key].Name).ToList();
        return listed.Count == 0 ? "nothing" : string.Join(", ", listed);
    }

    // The colony's content: a colony on the ground always has it (it is made with it, or loaded with it).
    static Content Rules(Colony colony) => colony.Content ?? throw new InvalidOperationException("the colony has no content");

    static string Whole(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
    static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
