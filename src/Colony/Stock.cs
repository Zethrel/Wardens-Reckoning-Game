// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>What kind of place an inventory is.</summary>
public enum Holder
{
    /// <summary>A store: a stockpile, a granary, the founders' carts.</summary>
    Store,
    /// <summary>What a workshop has at hand to work with.</summary>
    WorkshopInput,
    /// <summary>What a workshop has made, waiting to be carried away.</summary>
    WorkshopOutput,
    /// <summary>What a person is carrying.</summary>
    Carrier,
}

/// <summary>
/// One inventory: a place goods are, with whole units of each good. A store, a workshop's shelf and a carrier's
/// arms are all inventories, so every unit of goods is always in exactly one of them: it always has a custodian.
/// </summary>
public sealed class Inventory(InventoryId id, string name, Holder holder, double x, double y)
{
    public InventoryId Id { get; } = id;
    public string Name { get; } = name;
    public Holder Holder { get; } = holder;
    /// <summary>Where it is, in metres (step 13: points; the tile grid comes with construction, step 15).</summary>
    public double X { get; set; } = x;
    public double Y { get; set; } = y;

    readonly SortedDictionary<string, int> goods = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, int> kept = new(StringComparer.Ordinal);

    /// <summary>Units of each good here (goods with none are left out).</summary>
    public IReadOnlyDictionary<string, int> Goods => goods;

    public int Count(string good) => goods.GetValueOrDefault(good);

    /// <summary>A protected reserve: units of a good that may not be taken from here (the library's "keep" policy).</summary>
    public IReadOnlyDictionary<string, int> Kept => kept;

    /// <summary>Keep at least `amount` of `good` here (0: keep none).</summary>
    public void Keep(string good, int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "a reserve cannot be below 0");
        if (amount == 0)
            kept.Remove(good);
        else
            kept[good] = amount;
    }

    internal void Add(string good, int amount)
    {
        int now = Count(good) + amount;
        if (now < 0)
            throw new InvalidOperationException(Name + " would hold " + now + " " + good);   // never happens: callers check
        if (now == 0)
            goods.Remove(good);
        else
            goods[good] = now;
    }
}

/// <summary>A claim on goods in an inventory, for one purpose: claimed goods stay where they are, but nobody else
/// may claim or take them.</summary>
public sealed record Reservation(ReservationId Id, InventoryId Inventory, string Good, int Amount, string Purpose);

/// <summary>What happened to goods, as one line of the ledger.</summary>
public enum Change
{
    /// <summary>Goods arrived from outside the colony (the founders' supplies).</summary>
    Arrived,
    /// <summary>Goods moved from one inventory to another.</summary>
    Moved,
    /// <summary>Goods used up (a recipe's inputs).</summary>
    Used,
    /// <summary>Goods made (a recipe's outputs).</summary>
    Made,
}

/// <summary>One line of the ledger: which operation, when, what changed, how much, from where and to where.</summary>
public sealed record Entry(OperationId Operation, long Tick, Change Change, string Good, int Amount,
                           InventoryId? From, InventoryId? To);

/// <summary>Why something could not be done, in words for the player (null: it was done).</summary>
public sealed record Outcome(bool Done, string? Why = null, bool AlreadyDone = false)
{
    public static readonly Outcome Ok = new(true);
    public static Outcome No(string why) => new(false, why);
}

/// <summary>
/// The colony's goods (the design library's inventories, reservations and transfer transactions; step 13a).
///
/// - **Every change is an operation** with its own identifier, and is written in the ledger. An operation done
///   once is never done again: if the same operation comes a second time (a doubled order, a replay after a
///   load) nothing changes and it says so ("exactly once", FP-05).
/// - **Goods are counted, never lost**: they only come in through `Arrive` (from outside) or `Make` (a recipe),
///   and only leave through `Use` (a recipe). Every other change moves them between inventories.
/// - **Taking needs a claim**: goods are first reserved (`Reserve`), and only reserved goods are moved or used. A
///   reservation cannot claim goods someone else claimed, or a store's protected reserve.
/// </summary>
public sealed class Stocks(IdCounters ids)
{
    readonly SortedDictionary<long, Inventory> inventories = new();
    readonly SortedDictionary<long, Reservation> reservations = new();
    readonly HashSet<long> done = new();
    readonly List<Entry> ledger = new();

    public IReadOnlyCollection<Inventory> Inventories => inventories.Values;
    public IReadOnlyCollection<Reservation> Reservations => reservations.Values;
    public IReadOnlyList<Entry> Ledger => ledger;

    /// <summary>Whether an operation has been done.</summary>
    public bool Done(OperationId operation) => done.Contains(operation.Value);

    /// <summary>A new, empty inventory.</summary>
    public Inventory Open(string name, Holder holder, double x, double y)
    {
        var inventory = new Inventory(ids.Next<InventoryId>(), name, holder, x, y);
        inventories[inventory.Id.Value] = inventory;
        return inventory;
    }

    public Inventory this[InventoryId id] => inventories[id.Value];

    /// <summary>Units of `good` in `inventory` claimed by reservations.</summary>
    public int Reserved(InventoryId inventory, string good) =>
        reservations.Values.Where(r => r.Inventory == inventory && r.Good == good).Sum(r => r.Amount);

    /// <summary>Units of `good` in `inventory` free to claim: there, not claimed, not kept as a reserve.</summary>
    public int Free(InventoryId inventory, string good)
    {
        var here = this[inventory];
        return Math.Max(0, here.Count(good) - Reserved(inventory, good) - here.Kept.GetValueOrDefault(good));
    }

    /// <summary>Claims `amount` of `good` in `inventory` for `purpose`, or says why it cannot.</summary>
    public (Reservation? Reservation, Outcome Outcome) Reserve(InventoryId inventory, string good, int amount, string purpose)
    {
        if (amount < 1)
            return (null, Outcome.No("nothing to claim"));
        var here = this[inventory];
        int free = Free(inventory, good);
        if (free < amount)
        {
            int has = here.Count(good), claimed = Reserved(inventory, good), keep = here.Kept.GetValueOrDefault(good);
            string why = here.Name + " has " + has + " " + good;
            if (claimed > 0)
                why += ", " + claimed + " already claimed";
            if (keep > 0)
                why += ", " + keep + " kept as a reserve";
            return (null, Outcome.No(why + ": " + free + " free, " + amount + " wanted"));
        }
        var reservation = new Reservation(ids.Next<ReservationId>(), inventory, good, amount, purpose);
        reservations[reservation.Id.Value] = reservation;
        return (reservation, Outcome.Ok);
    }

    /// <summary>Lets a claim go: the goods stay, free again.</summary>
    public void Release(ReservationId reservation) => reservations.Remove(reservation.Value);

    /// <summary>Goods arriving from outside (the founders' supplies), as operation `operation` at `tick`.</summary>
    public Outcome Arrive(OperationId operation, long tick, InventoryId to, string good, int amount)
    {
        if (done.Contains(operation.Value))
            return new Outcome(true, "already done", AlreadyDone: true);
        if (amount < 1)
            return Outcome.No("nothing arrives");
        this[to].Add(good, amount);
        Record(new Entry(operation, tick, Change.Arrived, good, amount, null, to));
        return Outcome.Ok;
    }

    /// <summary>Moves the goods `reservation` claimed to `to`; the claim is used up.</summary>
    public Outcome Move(OperationId operation, long tick, ReservationId reservation, InventoryId to)
    {
        if (done.Contains(operation.Value))
            return new Outcome(true, "already done", AlreadyDone: true);
        if (!reservations.TryGetValue(reservation.Value, out var claim))
            return Outcome.No("the claim is gone (it was used or let go)");
        if (claim.Inventory == to)
            return Outcome.No("the goods are already there");
        this[claim.Inventory].Add(claim.Good, -claim.Amount);
        this[to].Add(claim.Good, claim.Amount);
        reservations.Remove(reservation.Value);
        Record(new Entry(operation, tick, Change.Moved, claim.Good, claim.Amount, claim.Inventory, to));
        return Outcome.Ok;
    }

    /// <summary>
    /// A recipe's work done: the goods `inputs` claimed are used up and `outputs` are made in `into`, all in one
    /// operation, so a recipe is never half done.
    /// </summary>
    public Outcome Make(OperationId operation, long tick, IReadOnlyList<ReservationId> inputs, InventoryId into,
                        IReadOnlyDictionary<string, int> outputs)
    {
        if (done.Contains(operation.Value))
            return new Outcome(true, "already done", AlreadyDone: true);
        var claims = new List<Reservation>();
        foreach (var id in inputs)
            if (reservations.TryGetValue(id.Value, out var claim))
                claims.Add(claim);
            else
                return Outcome.No("a claim on the inputs is gone (it was used or let go)");
        foreach (var claim in claims)
        {
            this[claim.Inventory].Add(claim.Good, -claim.Amount);
            reservations.Remove(claim.Id.Value);
            Record(new Entry(operation, tick, Change.Used, claim.Good, claim.Amount, claim.Inventory, null), first: false);
        }
        foreach (var (good, amount) in outputs)
        {
            this[into].Add(good, amount);
            Record(new Entry(operation, tick, Change.Made, good, amount, null, into), first: false);
        }
        done.Add(operation.Value);
        return Outcome.Ok;
    }

    void Record(Entry entry, bool first = true)
    {
        ledger.Add(entry);
        if (first)
            done.Add(entry.Operation.Value);
    }

    /// <summary>Units of `good` in the whole colony.</summary>
    public int Total(string good) => inventories.Values.Sum(i => i.Count(good));

    /// <summary>
    /// Units of `good` the ledger says there should be: what arrived and was made, less what was used. Equal to
    /// `Total` always, or goods were lost or made from nothing.
    /// </summary>
    public int Accounted(string good) => ledger.Where(e => e.Good == good).Sum(e => e.Change switch
    {
        Change.Arrived or Change.Made => e.Amount,
        Change.Used => -e.Amount,
        _ => 0,
    });

    // For loading a save.
    internal void Put(Inventory inventory) => inventories[inventory.Id.Value] = inventory;
    internal void PutReservation(Reservation reservation) => reservations[reservation.Id.Value] = reservation;
    internal void PutEntry(Entry entry)
    {
        ledger.Add(entry);
        done.Add(entry.Operation.Value);
    }
}
