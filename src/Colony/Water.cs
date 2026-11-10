// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>What a water source is (step 19a): how much it gives, and whether it freezes.</summary>
public enum WaterKind
{
    /// <summary>A spring, as on the test ground: its own litres every morning.</summary>
    Spring,
    /// <summary>A well (Construction §13.1): its piece's litres every morning; groundwater, so it never freezes.</summary>
    Well,
    /// <summary>A river: more than a colony draws. Frozen over in a cold winter: drawn through an ice hole.</summary>
    River,
    /// <summary>A lake: as a river.</summary>
    Lake,
    /// <summary>A brook: its litres a day by the month (needs.json water.brook_litres_by_month). In a cold winter it
    /// freezes and gives nothing.</summary>
    Brook,
}

/// <summary>One water source: its kind, its litres a day (a spring's or a well's), and whether it is frozen now.</summary>
public sealed class WaterSource
{
    public WaterKind Kind { get; init; }
    public int Litres { get; init; }
    public bool Frozen { get; internal set; }
    /// <summary>Until when it is fouled (step 19b), or null: clean.</summary>
    public long? FouledUntil { get; internal set; }
    /// <summary>The sign this fouling shows (dead fish, a smell...), or null: none.</summary>
    public string? Sign { get; internal set; }
    /// <summary>Whether a colonist has seen the sign (and the player been told).</summary>
    public bool SignSeen { get; internal set; }

    /// <summary>A river, a lake or a brook: water on the local map, where colonists may wash on the bank.</summary>
    public bool Natural => Kind is WaterKind.River or WaterKind.Lake or WaterKind.Brook;
}

/// <summary>
/// Water by its uses (step 19a, library Review 37: Colonist Daily Life and Needs §7.1–7.2, World Generation and
/// Terrain §1.1, Construction §13.1). The sixth part of `Work`.
///
/// **Sources.** A river or a lake has more than a colony draws (topped up to `plenty_litres` every hour). A brook
/// gives its month's litres each morning. A well or a spring gives its own litres each morning. Nothing is ever
/// carried *to* a source (`Inventory.SourceOnly`).
///
/// **Frost.** In a colony whose biome is cold (`Cold`), in the freezing months: brooks freeze and give nothing; a
/// river or a lake freezes over each night, and the colony plans an ice hole on it each morning (30 minutes' work,
/// a piece with `opens_ice`); once it is cut, the water can be drawn until the next night. Wells never freeze.
///
/// **Carrying.** A water barrel or a wash basin holds so many litres (`Inventory.Capacity`). When it is a load
/// short, a colonist fetches a load (20 litres) from the nearest source it can draw from: the water is carried, so
/// a well or barrels near the homes save walking. Colonists drink from whatever is nearest: a barrel, the carts, a
/// stockpile, the source itself.
///
/// **Washing.** At a wash basin (its water: 9 litres a wash) or on the bank of a river, a brook or a lake (no water
/// carried, but the walk), whichever is the shorter walk.
/// </summary>
public sealed partial class Work
{
    // The water sources (springs, wells, rivers, lakes, brooks), by their inventory.
    readonly SortedDictionary<long, WaterSource> springs = new();

    /// <summary>Whether the colony's biome is a cold one (needs.json water.cold_biomes): its water freezes in the
    /// freezing months. Set when it is founded.</summary>
    public bool Cold { get; set; }

    /// <summary>A water source at (`x`, `y`): a spring (the test ground's stream), a well, or a river, lake or brook
    /// of the local map. A spring or a well holds `litres` (if not given: the content's spring_litres_per_day)
    /// every morning; the others as their kind gives.</summary>
    public Inventory AddSpring(double x, double y, string name = "Spring", int? litres = null, WaterKind kind = WaterKind.Spring)
    {
        var spring = Stocks.Open(name, Holder.Store, x, y);
        spring.Accepts.UnionWith(content.Drinkable);
        spring.SourceOnly = true;
        springs[spring.Id.Value] = new WaterSource { Kind = kind, Litres = litres ?? content.SpringPerDay };
        Freeze(springs[spring.Id.Value], spring.Id);
        Fill(spring.Id);
        return spring;
    }

    /// <summary>The water sources' inventories.</summary>
    public IEnumerable<InventoryId> Springs => springs.Keys.Select(s => new InventoryId(s));

    /// <summary>A water source.</summary>
    public WaterSource SourceOf(InventoryId spring) => springs[spring.Value];

    /// <summary>The litres a water source has this morning: a spring's or a well's own, a brook's for the month,
    /// a river's or a lake's plenty (0 for a brook frozen).</summary>
    public int LitresOf(InventoryId spring)
    {
        var source = springs[spring.Value];
        return source.Kind switch
        {
            WaterKind.River or WaterKind.Lake => content.Water.PlentyLitres,
            WaterKind.Brook => source.Frozen ? 0 : content.Water.BrookByMonth[colony.Now.Month - 1],
            _ => source.Litres,
        };
    }

    /// <summary>Whether water may be drawn from this inventory now: anything but a frozen source.</summary>
    bool Drawable(Inventory inventory) => !(springs.TryGetValue(inventory.Id.Value, out var source) && source.Frozen);

    /// <summary>Whether it is the freezing time of year in a cold colony.</summary>
    public bool Freezing => Cold && content.Water.FreezingMonths.Contains(colony.Now.Month);

    // Each morning: frost comes or goes, the sources fill, the ice holes are planned.
    void Morning()
    {
        foreach (var (id, source) in springs)
        {
            Freeze(source, new InventoryId(id));
            Fill(new InventoryId(id));
        }
    }

    // Whether a source is frozen this morning; a river or a lake gets an ice hole planned on it.
    void Freeze(WaterSource source, InventoryId id)
    {
        bool freezing = Freezing;
        source.Frozen = freezing && source.Kind is WaterKind.Brook or WaterKind.River or WaterKind.Lake;
        if (!source.Frozen || source.Kind == WaterKind.Brook || Ground == null)
            return;
        var (x, y) = Ground.TileOf(Stocks[id].X, Stocks[id].Y);
        if (projects.Values.Any(p => p.Piece == content.Water.IceHole && p.X == x && p.Y == y && !p.Cancelled))
            return;
        Plan(content.Water.IceHole, x, y);
    }

    /// <summary>The ice hole at tile (x, y) is cut: the source there may be drawn from until the next morning.</summary>
    void OpenIce(int x, int y)
    {
        foreach (var (id, source) in springs)
            if (Ground.TileOf(Stocks[new InventoryId(id)].X, Stocks[new InventoryId(id)].Y) == (x, y))
                source.Frozen = false;
    }

    // Tops a source up to its litres for the day (water left from yesterday stays: a source never loses water);
    // a fouled source gives fouled water (step 19b).
    void Fill(InventoryId spring)
    {
        int missing = LitresOf(spring) - content.Drinkable.Sum(g => Stocks[spring].Count(g));
        string good = springs[spring.Value].FouledUntil != null ? content.Sanitation.FouledGood : content.Drink.Good;
        if (missing > 0)
            Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, spring, good, missing);
    }

    // Every hour: rivers and lakes are full again.
    void TopUp()
    {
        foreach (var (id, source) in springs)
            if (source.Kind is WaterKind.River or WaterKind.Lake)
                Fill(new InventoryId(id));
    }

    /// <summary>Units more that may be brought to an inventory: its capacity, less what is in it and what is on
    /// its way (fetched, or boiled for it); for one without a capacity, no limit.</summary>
    public int RoomIn(Inventory inventory)
    {
        if (inventory.Capacity is not int capacity)
            return int.MaxValue;
        int coming = jobs.Values.Where(j => j.Kind is JobKind.Haul or JobKind.Boil && j.To == inventory.Id).Sum(j => j.Amount);
        return Math.Max(0, capacity - inventory.Goods.Values.Sum() - coming);
    }

    // Barrels and basins: when one is a load short (or, if it holds less than a load, half empty), a colonist
    // fetches water for it from the nearest source that is not itself a barrel or basin, nor marked (step 19b: a
    // source marked "boil first" is boiled into the barrels instead). Whatever water is there is fetched: clean or
    // fouled, nobody can tell.
    void Fetch()
    {
        foreach (var container in Stocks.Inventories.Where(i => i.Capacity != null && i.Takes(content.Drink.Good)).ToList())
        {
            int room = RoomIn(container);
            if (room < Math.Min(content.Water.LoadLitres, container.Capacity!.Value / 2.0) || room == 0)
                continue;
            if (jobs.Values.Any(j => j.Kind == JobKind.Haul && j.To == container.Id))
                continue;                                        // one load at a time
            var from = Nearest(Stocks.Inventories.Where(i => i.Holder == Holder.Store && i.Capacity == null && DrinkFrom(i)
                                                                   && FreeWater(i.Id) > 0), container.X, container.Y);
            if (from == null)
                continue;
            string water = WaterIn(from.Id)!;
            int load = Math.Min(Math.Min(content.Water.LoadLitres, room), Stocks.Free(from.Id, water));
            NewHaul(from.Id, container.Id, water, load, "fetch water for the " + container.Name.ToLowerInvariant()
                                                         + " at (" + Ground.TileOf(container.X, container.Y).X + ", " + Ground.TileOf(container.X, container.Y).Y + ")");
        }
    }

    /// <summary>Where a colonist would wash now: a wash basin with water for a wash, or the bank of a river, brook or
    /// lake not frozen, whichever is the shorter walk; and whether it is a basin. Null: nowhere.</summary>
    (Inventory Place, bool Basin)? WashPlace(Worker w)
    {
        var places = Stocks.Inventories
            .Where(i => (i.WashPlace && WashWater(i.Id) != null)
                        || (springs.TryGetValue(i.Id.Value, out var s) && s.Natural && !s.Frozen))
            .ToList();
        var nearest = Nearest(places, w.X, w.Y);
        return nearest == null ? null : (nearest, nearest.WashPlace);
    }

    // A finished water barrel or wash basin: a store for water on its tile, holding its litres.
    void AddContainer(PieceKind kind, int x, int y)
    {
        var (cx, cy) = Ground.Centre(x, y);
        var container = Stocks.Open(kind.Name, Holder.Store, cx, cy);
        container.Accepts.UnionWith(content.Drinkable);
        container.Capacity = kind.HoldsLitres;
        container.WashPlace = kind.WashPlace;
    }

    /// <summary>The good a wash at a basin would use (enough of it free for a wash), or null.</summary>
    string? WashWater(InventoryId basin) => content.Drinkable.Reverse().FirstOrDefault(g => Stocks.Free(basin, g) >= content.Wash.Litres);

    /// <summary>For a save: puts a water source back as it was.</summary>
    internal void PutSpring(InventoryId spring, WaterSource source) => springs[spring.Value] = source;
}
