// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>A line of news for the player: when, and what (a sign seen at the water, someone falling ill).</summary>
public sealed record News(long Tick, string Text);

/// <summary>
/// Fouled water (step 19b, library Review 37: Sanitation and Environmental Contamination §10, the Stage 2 choice).
/// The seventh part of `Work`.
///
/// **Truth.** Fouled water is a good of its own (sanitation.json "fouled_good"), so every litre of it is counted
/// and can be followed: it looks like water to the colonists, who drink, wash with and carry it the same, and to
/// the player, who sees it as "Water" (only the developer's trace, F3, tells it apart).
///
/// **Release.** Each morning, every river, lake and brook not fouled may foul, on Sanitation's own dice: on
/// average once in `days_between` days. It stays fouled for 3 to 10 days: its water turns, and what it gives is
/// fouled; then it runs clean again. Wells are groundwater, out of reach.
///
/// **Carrying.** Water keeps what it was when it was drawn: water drawn before the fouling stays clean. Fouled
/// water carried into a store fouls the water there (`Mix`, each minute).
///
/// **Knowledge.** In a share of the foulings (`sign_share`) there is a sign (dead fish, a smell, scum). The first
/// colonist to come to the water sees it, and the player is told. Otherwise nobody knows, until someone falls ill
/// (Health.cs), and even then it is the player who draws the line to the water.
///
/// **What the player can do.** Mark a source or a store "boil first" (its water is boiled before it is drunk,
/// `BoilJobs`) or "do not drink".
/// </summary>
public sealed partial class Work
{
    readonly List<News> news = new();

    /// <summary>What the player has been told, oldest first.</summary>
    public IReadOnlyList<News> Told => news;

    /// <summary>Tells the player something (it is kept in the colony and saved).</summary>
    void Tell(string text) => news.Add(new News(colony.Now.Tick, text));

    /// <summary>For a save: what the player was told.</summary>
    internal void PutNews(News item) => news.Add(item);

    string Fouled => content.Sanitation.FouledGood;

    /// <summary>The drinkable water in an inventory, clean or fouled, free (not claimed).</summary>
    int FreeWater(InventoryId id) => content.Drinkable.Sum(g => Stocks.Free(id, g));

    /// <summary>Which of the drinkable goods to take from an inventory: whichever it has free, fouled first (an
    /// inventory holding both is about to be all fouled anyway: `Mix`). Null if none.</summary>
    string? WaterIn(InventoryId id) => content.Drinkable.Reverse().FirstOrDefault(g => Stocks.Free(id, g) > 0);

    /// <summary>Whether colonists may drink from an inventory as it is: not marked, and not frozen.</summary>
    bool DrinkFrom(Inventory inventory) => inventory.Mark == WaterMark.None && Drawable(inventory);

    /// <summary>Marks a store or a source (step 19b): the colonists go by it from now on.</summary>
    public void SetMark(InventoryId id, WaterMark mark)
    {
        Stocks[id].Mark = mark;
        Fetch();                                          // water is fetched and boiled by the new mark at once
        BoilJobs();
    }

    // ---- Release ----------------------------------------------------------------------------------------------

    // Each morning, before the sources fill: a fouling ends, or one begins.
    void Foul()
    {
        var dice = colony.DiceFor("sanitation");
        var rules = content.Sanitation;
        foreach (var (id, source) in springs)
        {
            var inventory = new InventoryId(id);
            if (source.FouledUntil is long until)
            {
                if (colony.Now.Tick < until)
                    continue;
                // Clean water has run in: the fouled water in the source is gone, clean in its place.
                source.FouledUntil = null;
                source.Sign = null;
                Turn(inventory, Fouled, content.Drink.Good);
                continue;
            }
            if (!source.Natural || !dice.Chance(1 / rules.DaysBetween))
                continue;
            int days = dice.Int(rules.LastsFrom, rules.LastsTo);
            source.FouledUntil = colony.Now.Tick + (long)days * Calendar.TicksPerDay;
            source.Sign = dice.Chance(rules.SignShare) ? dice.Choice(rules.Signs) : null;
            source.SignSeen = false;
            Turn(inventory, content.Drink.Good, Fouled);
        }
    }

    // All the free `from` in an inventory becomes `to` (water turning foul, or clean again): one operation.
    void Turn(InventoryId id, string from, string to)
    {
        int free = Stocks.Free(id, from);
        if (free == 0)
            return;
        var claim = Stocks.Reserve(id, from, free, "turns to " + to).Reservation!;
        Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { claim.Id }, id, new Dictionary<string, int> { [to] = free });
    }

    // Each minute: wherever fouled water and clean water are together (a barrel a fouled load was poured into,
    // someone's arms), the clean water is fouled too.
    void Mix()
    {
        foreach (var inventory in Stocks.Inventories.ToList())
            if (Stocks.Free(inventory.Id, Fouled) > 0 && Stocks.Free(inventory.Id, content.Drink.Good) > 0)
                Turn(inventory.Id, content.Drink.Good, Fouled);
    }

    // Each minute: a colonist standing at a fouled source with a sign not yet seen sees it, and says so.
    void Notice()
    {
        foreach (var (id, source) in springs)
        {
            if (source.Sign is not string sign || source.SignSeen)
                continue;
            var place = Stocks[new InventoryId(id)];
            if (workers.Values.FirstOrDefault(w => w.Collapsed == null && Distance(w.X, w.Y, place.X, place.Y) <= 1.5) is Worker seer)
            {
                source.SignSeen = true;
                Tell(seer.Name + " saw " + sign + " at " + place.Name.ToLowerInvariant() + ".");
            }
        }
    }

    // ---- Boiling ----------------------------------------------------------------------------------------------

    /// <summary>Why a store marked "boil first" is not being boiled now, by its inventory (for the inspector).</summary>
    readonly Dictionary<long, string> boilWhy = new();

    /// <summary>Why the water of a store marked "boil first" waits, or null.</summary>
    public string? WhyNotBoiled(InventoryId id) => boilWhy.GetValueOrDefault(id.Value);

    // Each minute: a batch of boiling for each store marked "boil first" that has water and none under way, if
    // there is fuel to be had and somewhere to put the boiled water.
    void BoilJobs()
    {
        var rules = content.Sanitation;
        boilWhy.Clear();
        foreach (var water in Stocks.Inventories.Where(i => i.Mark == WaterMark.BoilFirst && Drawable(i)).ToList())
        {
            if (WaterIn(water.Id) is not string good || jobs.Values.Any(j => j.Kind == JobKind.Boil && j.Boiling == water.Id))
                continue;
            var fuel = Nearest(Stocks.Inventories.Where(i => i.Holder == Holder.Store && Stocks.Free(i.Id, rules.Fuel) >= rules.FuelUnits), water.X, water.Y);
            if (fuel == null)
            {
                boilWhy[water.Id.Value] = "waiting for " + content.Goods[rules.Fuel].Name.ToLowerInvariant() + " to boil it with: none free in any store";
                continue;
            }
            var to = Nearest(Stocks.Inventories.Where(i => i.Takes(content.Drink.Good) && i.Mark == WaterMark.None && i.Id != water.Id
                                                           && RoomIn(i) > 0), water.X, water.Y);
            if (to == null)
            {
                boilWhy[water.Id.Value] = "waiting for somewhere to put boiled water: no unmarked store with room takes water";
                continue;
            }
            int litres = Math.Min(Math.Min(rules.BoilLitres, Stocks.Free(water.Id, good)), RoomIn(to));
            var claims = new List<ReservationId>
            {
                Stocks.Reserve(fuel.Id, rules.Fuel, rules.FuelUnits, "fuel to boil water").Reservation!.Id,
                Stocks.Reserve(water.Id, good, litres, "water to boil").Reservation!.Id,
            };
            var job = new Job
            {
                Id = colony.Ids.Next<TaskId>(), Kind = JobKind.Boil, Good = content.Drink.Good, Amount = litres, From = fuel.Id, To = to.Id,
                Claims = claims, First = colony.Ids.Next<OperationId>(), Second = colony.Ids.Next<OperationId>(),
                Third = colony.Ids.Next<OperationId>(), Boiling = water.Id, Purpose = "boil water from " + water.Name.ToLowerInvariant(),
            };
            jobs[job.Id.Value] = job;
        }
    }

    // The boiling is done: the fuel (in their arms) and the claimed water are used up, and as much clean water is
    // in their arms, to be carried to the store.
    void Boiled(Worker worker, Job job, long now)
    {
        var fuel = Stocks.Reserve(worker.Arms, content.Sanitation.Fuel, content.Sanitation.FuelUnits, "burnt").Reservation!;
        job.Claims.Add(fuel.Id);
        Stocks.Make(job.Third, now, job.Claims, worker.Arms, new Dictionary<string, int> { [content.Drink.Good] = job.Amount });
        job.Claims.Clear();
        job.Step = Step.Carrying;
    }
}
