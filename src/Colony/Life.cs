// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// The colonists' needs and day (the design library's Colonist Daily Life and Needs; step 14a). This is the second
/// half of `Work` (C#'s `partial class`: one class written in two files).
///
/// **Needs.** Hunger, thirst and fatigue each run from 0 (none) to 100 (the limit), growing every minute by the
/// rates in content/needs.json (hunger and thirst more slowly asleep; fatigue only awake, and sleep eases it).
/// Each need has four levels: *peckish* (eats or drinks at mealtimes), *wants* (sees to it when free),
/// *urgent* (takes no new work and sees to it, but finishes a delivery in hand: library §5.3) and *critical*
/// (drops what they are doing at once: goods in their arms stay there, with the job, until they come back to it).
///
/// **The day.** Each hour of a colonist's schedule is one of the five blocks (Daily Life §4.2): in *Sleep* they go
/// to their bedroll (or sleep where they are); in *Meals* they eat and drink if even peckish; in *Work* they take
/// jobs; in *Recreation* and *Flexible* time they see to what they want, or rest if tired. The library's order
/// (§5.2): survival before sleep, sleep before work. Only colonists in a Work hour, with no urgent need, take jobs.
///
/// **Collapse.** Fatigue at 100 collapses a colonist at once: they fall asleep where they stand. Hunger or thirst
/// at 100 for its `collapse_hours` collapses them: they can do nothing, and another colonist brings them food or
/// water (a haul to their arms), which they eat or drink at once. Nobody dies of it yet: death is Healthcare's,
/// which comes later.
/// </summary>
public sealed partial class Work
{
    public IReadOnlyCollection<Bedroll> Bedrolls => bedrolls.Values;

    /// <summary>A bedroll laid out at (`x`, `y`), for `owner` (or nobody yet).</summary>
    public Bedroll AddBedroll(double x, double y, ColonistId? owner)
    {
        var bedroll = new Bedroll(colony.Ids.Next<BuildingId>(), x, y, owner);
        bedrolls[bedroll.Id.Value] = bedroll;
        return bedroll;
    }

    /// <summary>The spring (a stand-in for rivers and wells): a store of water at (`x`, `y`), filled to
    /// SpringPerDay litres when made and every morning.</summary>
    public Inventory AddSpring(double x, double y)
    {
        var spring = Stocks.Open("Spring", Holder.Store, x, y);
        spring.Accepts.Add(content.Drink.Good);
        springs.Add(spring.Id.Value);
        Fill(spring.Id);
        return spring;
    }

    /// <summary>The springs' inventories.</summary>
    public IEnumerable<InventoryId> Springs => springs.Select(s => new InventoryId(s));

    void Fill(InventoryId spring)
    {
        int missing = content.SpringPerDay - Stocks[spring].Count(content.Drink.Good);
        if (missing > 0)
            Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, spring, content.Drink.Good, missing);
    }

    /// <summary>Changes a colonist's schedule template.</summary>
    public void SetSchedule(ColonistId id, string schedule)
    {
        if (!content.Schedules.ContainsKey(schedule))
            throw new ArgumentException("no schedule \"" + schedule + "\" in the content", nameof(schedule));
        workers[id.Value].Schedule = schedule;
    }

    /// <summary>The block a colonist's schedule gives this hour.</summary>
    public Block BlockOf(Worker worker) => content.Schedules[worker.Schedule].Hours[colony.Now.Hour];

    /// <summary>Whether a colonist can take a job now: not stopped, collapsed, asleep or busy with their own needs;
    /// in a Work hour; and with no urgent need.</summary>
    bool Available(Worker w) =>
        !w.Stopped && w.Job == null && w.Collapsed == null && !w.Asleep && w.Personal == null
        && BlockOf(w) == Block.Work && Level(w) < 3;

    /// <summary>The highest level any need of the colonist has reached: 0 none, 1 peckish, 2 wants, 3 urgent,
    /// 4 critical.</summary>
    int Level(Worker w) => content.Needs.Values.Max(rule => LevelOf(rule, w.Needs[rule.Name].Level));

    internal static int LevelOf(NeedRule rule, double level) =>
        level >= rule.Critical ? 4 : level >= rule.Urgent ? 3 : level >= rule.Wants ? 2 : level >= rule.Peckish ? 1 : 0;

    /// <summary>
    /// A colonist's minute for themselves: needs grow, collapse, sleep, eating and drinking. True if they are free
    /// to go on with their job this minute.
    /// </summary>
    bool Live(Worker w)
    {
        Grow(w);
        if (w.Collapsed != null)
        {
            Recover(w);
            return false;
        }
        if (w.Asleep)
            return Wake(w);
        if (w.Personal != null)
        {
            Continue(w);
            return false;
        }
        if (w.Stopped)
            return true;                                   // a stopped colonist does nothing for themselves either
        var (wish, level) = Wish(w);
        if (wish == null)
            return true;
        // A job they hold: a critical need drops it now; an urgent need or the hour finishes a delivery in hand.
        if (w.Job is TaskId t && jobs[t.Value] is var job)
        {
            bool delivering = job.Kind == JobKind.Haul && job.Step is Step.Carrying or Step.Unloading;
            if (level < 4 && delivering)
                return true;
            if (!delivering)
                Reopen(job, w);
            // else: critical, carrying: the job and the goods stay with them while they see to it.
        }
        Start(w, wish.Value);
        return false;
    }

    void Grow(Worker w)
    {
        foreach (var rule in content.Needs.Values)
        {
            var need = w.Needs[rule.Name];
            double perHour = w.Asleep ? rule.AsleepPerHour : rule.PerHour;
            need.Level = Math.Clamp(need.Level + perHour / Calendar.MinutesPerHour, 0, 100);
            need.MinutesAtLimit = need.Level >= 100 ? need.MinutesAtLimit + 1 : 0;
        }
        if (w.Asleep)
        {
            var fatigue = w.Needs[content.Fatigue.Name];
            fatigue.Level = Math.Max(0, fatigue.Level - (w.OnBedroll ? content.BedrollPerHour : content.GroundPerHour) / Calendar.MinutesPerHour);
        }
        if (w.Collapsed != null)
            return;
        foreach (var rule in content.Needs.Values)
            if (w.Needs[rule.Name].Level >= 100 && w.Needs[rule.Name].MinutesAtLimit >= rule.CollapseHours * Calendar.MinutesPerHour)
            {
                Collapse(w, rule.Name);
                return;
            }
    }

    void Collapse(Worker w, string need)
    {
        w.Collapsed = need;
        if (w.Personal is { Claim: ReservationId claim })
            Stocks.Release(claim);
        w.Personal = null;
        if (w.Job is TaskId t && jobs[t.Value] is var job && job.Step is not (Step.Carrying or Step.Unloading))
            Reopen(job, w);
        if (need == content.Fatigue.Name)
        {
            w.Asleep = true;                               // asleep where they fell
            w.OnBedroll = false;
        }
    }

    /// <summary>A collapsed colonist eats or drinks what is in their arms, and comes round once the need that felled
    /// them is below urgent (fatigue: below "wants", and they wake).</summary>
    void Recover(Worker w)
    {
        foreach (var use in new[] { content.Eat, content.Drink })
        {
            var rule = use == content.Eat ? content.Hunger : content.Thirst;
            while (w.Needs[rule.Name].Level >= rule.Peckish && Stocks.Free(w.Arms, use.Good) > 0)
            {
                var claim = Stocks.Reserve(w.Arms, use.Good, 1, "fed").Reservation!;
                Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { claim.Id }, w.Arms, new Dictionary<string, int>());
                w.Needs[rule.Name].Level = Math.Max(0, w.Needs[rule.Name].Level - use.Eases);
            }
        }
        var felled = content.Needs[w.Collapsed!];
        if (w.Needs[felled.Name].Level < (felled.Name == content.Fatigue.Name ? felled.Wants : felled.Urgent))
        {
            w.Collapsed = null;
            w.Asleep = false;
            w.OnBedroll = false;
        }
    }

    /// <summary>Whether a sleeper wakes: for critical hunger or thirst; or out of their sleep hours once rested
    /// below "wants".</summary>
    bool Wake(Worker w)
    {
        bool starving = w.Needs[content.Hunger.Name].Level >= content.Hunger.Critical
                        || w.Needs[content.Thirst.Name].Level >= content.Thirst.Critical;
        bool rested = BlockOf(w) != Block.Sleep && w.Needs[content.Fatigue.Name].Level < content.Fatigue.Wants;
        if (!starving && !rested)
            return false;
        w.Asleep = false;
        w.OnBedroll = false;
        return true;
    }

    /// <summary>What a colonist should see to now, and how pressing it is (4 critical ... 1 the hour asks it), in
    /// the library's order; null: nothing.</summary>
    (PersonalKind? Wish, int Level) Wish(Worker w)
    {
        int Of(NeedRule rule) => LevelOf(rule, w.Needs[rule.Name].Level);
        var block = BlockOf(w);
        bool free = w.Job == null;
        foreach (int level in new[] { 4, 3 })
        {
            if (Of(content.Thirst) >= level && Can(w, PersonalKind.Drink)) return (PersonalKind.Drink, level);
            if (Of(content.Hunger) >= level && Can(w, PersonalKind.Eat)) return (PersonalKind.Eat, level);
            if (Of(content.Fatigue) >= level) return (PersonalKind.Sleep, level);
        }
        if (block == Block.Sleep)
            return (PersonalKind.Sleep, 2);
        if (block == Block.Meals)
        {
            if (Of(content.Thirst) >= 1 && Can(w, PersonalKind.Drink)) return (PersonalKind.Drink, 1);
            if (Of(content.Hunger) >= 1 && Can(w, PersonalKind.Eat)) return (PersonalKind.Eat, 1);
        }
        if (block != Block.Work || free)
        {
            if (Of(content.Thirst) >= 2 && Can(w, PersonalKind.Drink)) return (PersonalKind.Drink, 2);
            if (Of(content.Hunger) >= 2 && Can(w, PersonalKind.Eat)) return (PersonalKind.Eat, 2);
            if (block != Block.Work && Of(content.Fatigue) >= 2) return (PersonalKind.Sleep, 2);
        }
        return (null, 0);
    }

    /// <summary>Whether there is food or water to be had (and if not, the colonist notes it for the player).</summary>
    bool Can(Worker w, PersonalKind kind)
    {
        string good = kind == PersonalKind.Eat ? content.Eat.Good : content.Drink.Good;
        if (Source(w, good) != null)
            return true;
        w.Lacking = "no " + content.Goods[good].Name.ToLowerInvariant() + " free in any store";
        return false;
    }

    /// <summary>The nearest inventory with `good` free a colonist may take from: their own arms first, then stores.</summary>
    Inventory? Source(Worker w, string good) =>
        Stocks.Free(w.Arms, good) > 0 ? Stocks[w.Arms]
        : Nearest(Stocks.Inventories.Where(i => i.Holder == Holder.Store && Stocks.Free(i.Id, good) > 0), w.X, w.Y);

    /// <summary>Starts seeing to a wish: claims the food or water, or picks the bed.</summary>
    void Start(Worker w, PersonalKind kind)
    {
        w.Lacking = null;
        if (kind == PersonalKind.Sleep)
        {
            var bed = bedrolls.Values.FirstOrDefault(b => b.Owner == w.Id);
            w.Personal = new Personal { Kind = kind, X = bed?.X ?? w.X, Y = bed?.Y ?? w.Y, Bedroll = bed != null };
        }
        else
        {
            string good = kind == PersonalKind.Eat ? content.Eat.Good : content.Drink.Good;
            var from = Source(w, good)!;
            var (claim, _) = Stocks.Reserve(from.Id, good, 1, kind == PersonalKind.Eat ? w.Name + " eats" : w.Name + " drinks");
            w.Personal = new Personal { Kind = kind, X = from.X, Y = from.Y, Claim = claim!.Id, Operation = colony.Ids.Next<OperationId>() };
        }
        Continue(w);
    }

    /// <summary>A minute of seeing to it: walking there, then eating, drinking or lying down.</summary>
    void Continue(Worker w)
    {
        var p = w.Personal!;
        if (p.Left == null)
        {
            // The way there is gone (a wall was built across it): they give up, letting go of what they claimed.
            if (double.IsPositiveInfinity(Far(w.X, w.Y, p.X, p.Y)))
            {
                if (p.Claim is ReservationId lost)
                    Stocks.Release(lost);
                w.Personal = null;
                w.Lacking = "cannot reach " + (p.Kind == PersonalKind.Sleep ? "their bedroll" : "the store");
                return;
            }
            bool there = w.X == p.X && w.Y == p.Y;
            if (!Walk(w, p.X, p.Y))
            {
                Stocks[w.Arms].X = w.X;
                Stocks[w.Arms].Y = w.Y;
                return;
            }
            Stocks[w.Arms].X = w.X;
            Stocks[w.Arms].Y = w.Y;
            if (p.Kind == PersonalKind.Sleep)
            {
                w.Asleep = true;
                w.OnBedroll = p.Bedroll;
                w.Personal = null;
                return;
            }
            p.Left = (p.Kind == PersonalKind.Eat ? content.Eat : content.Drink).Minutes;
            if (!there)
                return;
        }
        if (--p.Left > 0)
            return;
        var use = p.Kind == PersonalKind.Eat ? content.Eat : content.Drink;
        var rule = p.Kind == PersonalKind.Eat ? content.Hunger : content.Thirst;
        Stocks.Make(p.Operation, colony.Now.Tick, new[] { p.Claim!.Value }, w.Arms, new Dictionary<string, int>());
        w.Needs[rule.Name].Level = Math.Max(0, w.Needs[rule.Name].Level - use.Eases);
        w.Personal = null;
    }

    /// <summary>The collapsed get food and water brought to them: a haul to their arms from the nearest store,
    /// unless one is already on its way.</summary>
    void Care()
    {
        foreach (var w in workers.Values.Where(x => x.Collapsed != null))
            foreach (var (use, rule) in new[] { (content.Eat, content.Hunger), (content.Drink, content.Thirst) })
            {
                // (Food or water already in their arms was eaten or drunk this minute, in Recover.)
                if (w.Needs[rule.Name].Level < rule.Wants
                    || jobs.Values.Any(j => j.Kind == JobKind.Haul && j.To == w.Arms && j.Good == use.Good))
                    continue;
                var from = Nearest(Stocks.Inventories.Where(i => i.Holder == Holder.Store && Stocks.Free(i.Id, use.Good) > 0), w.X, w.Y);
                if (from != null)
                    NewHaul(from.Id, w.Arms, use.Good, 1, "bring " + content.Goods[use.Good].Name.ToLowerInvariant() + " to " + w.Name);
            }
    }

    /// <summary>What a colonist is doing, in words for the player.</summary>
    public string Doing(ColonistId id)
    {
        var w = workers[id.Value];
        if (w.Collapsed != null)
            return "collapsed (" + w.Collapsed + ")";
        if (w.Stopped)
            return "stopped";
        if (w.Asleep)
            return w.OnBedroll ? "asleep on a bedroll" : "asleep on the ground";
        if (w.Personal is Personal p)
            return (p.Kind, p.Left) switch
            {
                (PersonalKind.Sleep, _) => "going to sleep",
                (PersonalKind.Eat, null) => "going to eat",
                (PersonalKind.Eat, _) => "eating",
                (PersonalKind.Drink, null) => "going to drink",
                _ => "drinking",
            };
        if (w.Job is TaskId t)
            return jobs[t.Value].Purpose;
        return BlockOf(w) switch
        {
            Block.Work => "waiting for work",
            Block.Sleep => "awake in sleeping hours",
            Block.Meals => "at a meal",
            Block.Recreation => "at leisure",
            _ => "free time",
        };
    }
}
