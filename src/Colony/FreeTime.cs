// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// Free time (step 25a; design library Review 42: Colonist Daily Life and Needs §11.1). In their Recreation hours and
/// Flexible Time, a colonist with nothing urgent to see to does something of their own: a nature walk, sky gazing,
/// sitting by the water, resting in the sun, practising work they enjoy, wandering about the camp. The activities are
/// content (leisure.json), so more can be added without code.
///
/// How it fits the rest:
///
/// - **Choosing** happens at a colonist's think (`Live`), never at a wake-up: on dice of their own ("leisure"),
///   weighted by how much an activity eases their free-time needs, by their interests, by how far it is, and away
///   from what they did last.
/// - **Doing** is an outing: walk to a spot, stay there a while, perhaps walk on to the next stop. The existing
///   walking and deadlines carry it (arriving wakes them; a stay ends on its tick): no clock of its own.
/// - **Benefit** counts only the ticks spent at a stop, never the walk (Review 42, R42-C02).
/// - **Interruptions:** it is the lowest of their priorities. The end of the free block, a need, an order or a
///   collapse ends it at their next processing; what it held (its spot) is let go at once; and that activity waits
///   `RetryMinutes` before it is offered to them again.
/// </summary>
public sealed partial class Work
{
    /// <summary>Whether the colonist's block now is free time: Recreation or Flexible Time.</summary>
    bool FreeBlock(Worker w) => BlockOf(w) is Block.Recreation or Block.Flexible;

    /// <summary>The free-time needs rise by the minute while they are awake (step 25a).</summary>
    void GrowFree(Worker w)
    {
        if (w.Asleep || w.Collapsed != null)
            return;
        foreach (var need in content.Leisure.Needs.Values)
            w.FreeNeeds[need.Id] = Math.Min(100, w.FreeNeeds.GetValueOrDefault(need.Id) + need.PerHour / Calendar.MinutesPerHour);
    }

    /// <summary>
    /// A colonist free for it chooses something to do (step 25a), at their think: every activity they may do now
    /// (not waiting after an interruption, at a fitting time of day, with a place for it) is weighed, and one is
    /// drawn on the "leisure" dice by those weights. Nothing fits: they stay where they are.
    /// </summary>
    void Choose(Worker w, bool between = false)
    {
        var rules = content.Leisure;
        long now = colony.Now.Tick;
        int hour = colony.Now.Hour;
        if (w.LookAgain > now)
            return;                                        // they looked a little while ago and found nothing
        // Where they look, and how far: around themselves in their free time; between jobs, nearer, around their work
        // area (step 25c, R43-C03).
        var (cx, cy) = between ? Origin(w) : (w.X, w.Y);
        double within = between ? rules.BetweenJobs.WithinMetres : rules.WithinMetres;
        // The activities that may be done now at all (the time of day, no wait), before looking at the ground.
        var possible = rules.Activities.Where(a => !(w.NotBefore.TryGetValue(a.Id, out long until) && until > now)
                                                  && a.When.Any(time => rules.InTime(time, hour))).ToList();
        var offered = new List<(Activity Activity, (double X, double Y, BuildingId? Site) Spot, double Weight)>();
        (double X, double Y, long? Join, ColonistId? Ask)? talkSpot = null;
        var spots = possible.Count > 0 ? Spots(w, cx, cy, within) : null;   // every kind of place around, in one look
        foreach (var activity in possible)
        {
            if (activity.Place == "company")
            {
                // A talk (step 25b): someone to talk with, or a talk to join, nearby.
                if (TalkSpot(w, cx, cy, within) is not var (tx, ty, join, ask))
                    continue;
                talkSpot = (tx, ty, join, ask);
                offered.Add((activity, (tx, ty, null), Weight(w, activity, Math.Sqrt((tx - w.X) * (tx - w.X) + (ty - w.Y) * (ty - w.Y)))));
                continue;
            }
            if (FirstSpot(w, activity, spots!, cx, cy, within) is not var (x, y, site))
                continue;                                  // no place for it: another is chosen (Review 42)
            offered.Add((activity, (x, y, site), Weight(w, activity, Math.Sqrt((x - w.X) * (x - w.X) + (y - w.Y) * (y - w.Y)))));
        }
        if (offered.Count == 0)
        {
            // Nothing to do now: they look again in a little while, not at every think (step 25a).
            w.LookAgain = now + rules.LookAgainMinutes * Calendar.TicksPerMinute;
            return;
        }
        // A weighted draw: a number from 0 to the sum of the weights, and the activity whose share it falls in.
        double roll = colony.DiceFor("leisure").Uniform(0, offered.Sum(o => o.Weight));
        var chosen = offered[^1];
        foreach (var o in offered)
        {
            if (roll < o.Weight)
            {
                chosen = o;
                break;
            }
            roll -= o.Weight;
        }
        if (chosen.Activity.Place == "company")
        {
            // Asked, the other may say no: then nothing now, and they look again in a while.
            if (!StartTalk(w, talkSpot!.Value, chosen.Activity, between))
                w.LookAgain = now + rules.LookAgainMinutes * Calendar.TicksPerMinute;
            return;
        }
        w.Outing = new Outing
        {
            Activity = chosen.Activity.Id, X = chosen.Spot.X, Y = chosen.Spot.Y, StopsLeft = chosen.Activity.Stops - 1,
            Site = chosen.Spot.Site, Between = between,
        };
        w.LastActivity = chosen.Activity.Id;
        Head(w, w.Outing.X, w.Outing.Y, near: w.Outing.Site != null);
    }

    /// <summary>
    /// A colonist between jobs, at their think, with nothing to do (step 25c, library Review 43): after the wait
    /// (`WaitMinutes`, counted since they were first left without work), they may choose free time, near their work
    /// area; until then, or if nothing is found, they idle there.
    /// </summary>
    void Occupy(Worker w)
    {
        var rules = content.Leisure.BetweenJobs;
        if (colony.Now.Tick - w.BetweenSince!.Value >= (long)rules.WaitMinutes * Calendar.TicksPerMinute)
        {
            Choose(w, between: true);
            if (w.Outing != null)
                return;
        }
        Idle(w);
    }

    /// <summary>Whether a colonist has been between jobs for the whole wait (step 25c): free for free time, and so for a
    /// talk.</summary>
    bool PastWait(Worker w) => BlockOf(w) == Block.Work && w.BetweenSince is long since
                               && colony.Now.Tick - since >= (long)content.Leisure.BetweenJobs.WaitMinutes * Calendar.TicksPerMinute;

    /// <summary>
    /// Idling between jobs (step 25c): they potter from spot to spot within a short reach of their work area. It eases
    /// nothing and is not free time (Review 43). With no spot there, they stand where they are.
    /// </summary>
    void Idle(Worker w)
    {
        var rules = content.Leisure.BetweenJobs;
        var (cx, cy) = Origin(w);
        if (Draw(Spots(w, cx, cy, rules.IdleMetres, near: true), "near") is not var (x, y))
            return;
        w.Outing = new Outing { Activity = rules.Idle.Id, X = x, Y = y, StopsLeft = rules.Idle.Stops - 1, Between = true };
        Head(w, x, y);
    }

    /// <summary>
    /// A colonist's work area (step 25c, R43-C03): where they last finished work in this Work block; or else the store
    /// or workplace nearest to them. Never one fixed centre of the camp, so someone at a far workplace stays there.
    /// </summary>
    (double X, double Y) Origin(Worker w)
    {
        if (w.WorkedAt is var (ax, ay))
            return (ax, ay);
        var places = Stocks.Inventories.Where(i => i.Holder == Holder.Store).Select(i => (i.X, i.Y, Id: i.Id.Value))
                           .Concat(sites.Values.Select(s => (s.X, s.Y, Id: s.Id.Value)));
        var nearest = places.OrderBy(p => (p.X - w.X) * (p.X - w.X) + (p.Y - w.Y) * (p.Y - w.Y)).ThenBy(p => p.Id).FirstOrDefault();
        return nearest == default ? (w.X, w.Y) : (nearest.X, nearest.Y);
    }

    /// <summary>The activity of an outing: one of the catalogue, or idling between jobs (step 25c).</summary>
    Activity ActivityOf(string id) =>
        id == BetweenJobsRules.IdleId ? content.Leisure.BetweenJobs.Idle : content.Leisure.Activities.First(a => a.Id == id);

    /// <summary>Sets off for a free-time spot: its way worked out first only a little beyond `WithinMetres` (it is
    /// near; Ground.Metres: the same way, found with less work), then walked as any other.</summary>
    void Head(Worker w, double x, double y, bool near = false, double? reach = null)
    {
        Far(w.X, w.Y, x, y, reach ?? Reach);
        Walk(w, x, y, near);
    }

    /// <summary>How far a free-time way is worked out (step 25a).</summary>
    double Reach => 1.5 * content.Leisure.WithinMetres;

    /// <summary>
    /// How much a colonist would like an activity now (step 25a): what it eases of their free-time needs (more, the
    /// more they need it), twice as much if an interest draws them and a third if they dislike it, less the farther
    /// it is, and a quarter if it is what they did last. Never 0, so the dice keep some surprise.
    /// </summary>
    double Weight(Worker w, Activity activity, double metres)
    {
        double weight = 0;
        foreach (var (need, perHour) in activity.EasesPerHour)
            weight += perHour * (10 + w.FreeNeeds.GetValueOrDefault(need)) / 100;
        foreach (string drawn in activity.DrawnBy)
            weight *= drawn == LeisureRules.EnjoyedSkill ? 2          // practice is only offered for a skill they enjoy
                    : w.Interests.GetValueOrDefault(drawn) switch { Interest.Enjoys => 2, Interest.Dislikes => 1 / 3.0, _ => 1 };
        weight /= 1 + metres / 20;
        if (activity.Id == w.LastActivity)
            weight /= 4;
        return Math.Max(weight, 0.01);
    }

    /// <summary>
    /// Where an activity would start (step 25a): for practice, a workplace of a skill they enjoy that nobody works
    /// at; otherwise a tile of the activity's kind of place within `WithinMetres`, open, reachable and held by no
    /// other outing, drawn on the "leisure" dice. Null: there is none.
    /// </summary>
    (double X, double Y, BuildingId? Site)? FirstSpot(Worker w, Activity activity, Dictionary<string, List<(double X, double Y)>> spots,
                                                      double cx, double cy, double within)
    {
        if (activity.Place == "workplace")
        {
            foreach (var site in sites.Values)
            {
                if (content.SkillFor.GetValueOrDefault(site.Workshop) is not string skill || w.Interests.GetValueOrDefault(skill) != Interest.Enjoys
                    || (site.X - cx) * (site.X - cx) + (site.Y - cy) * (site.Y - cy) > within * within)
                    continue;
                bool busy = jobs.Values.Any(j => j.Site == site.Id && j.Worker != null && j.Step == Step.Working)
                            || workers.Values.Any(o => o != w && o.Outing?.Site == site.Id);
                if (!busy && !double.IsPositiveInfinity(Far(w.X, w.Y, site.X, site.Y, Reach)))
                    return (site.X, site.Y, site.Id);
            }
            return null;
        }
        return Draw(spots, activity.Place) is var (x, y) ? (x, y, null) : null;
    }

    /// <summary>One of the spots of a kind of place, drawn on the "leisure" dice; null if there is none.</summary>
    (double X, double Y)? Draw(Dictionary<string, List<(double X, double Y)>> spots, string place) =>
        spots.TryGetValue(place, out var fit) && fit.Count > 0 ? fit[colony.DiceFor("leisure").Int(0, fit.Count - 1)] : null;

    /// <summary>
    /// The spots for free time within `metres` of (`cx`, `cy`), by kind of place (step 25a): tiles open, outdoors,
    /// with nothing built on them, held by no other outing and reachable from the colonist, as their middles. One
    /// look at each tile sorts it into every kind it is: beside a tree ("trees"), beside water ("water"), with nothing
    /// beside it ("open"), near a store ("camp"). Without a ground (a camp in the tests) only "camp" has spots:
    /// points around the nearest store.
    /// </summary>
    Dictionary<string, List<(double X, double Y)>> Spots(Worker w, double cx, double cy, double metres, bool near = false)
    {
        var found = new Dictionary<string, List<(double X, double Y)>>
        {
            ["trees"] = new(), ["water"] = new(), ["open"] = new(), ["camp"] = new(), ["near"] = new(),
        };
        var store = NearestStoreOf(w);
        double stroll = content.Leisure.StrollMetres;
        if (Ground is not Ground g)
        {
            if (near)
            {
                // Idling without a ground: a point a little way from the work area.
                var dice = colony.DiceFor("leisure");
                double angle = dice.Uniform(0, 2 * Math.PI), far = dice.Uniform(0.5, metres);
                found["near"].Add((cx + Math.Round(Math.Cos(angle) * far, 1), cy + Math.Round(Math.Sin(angle) * far, 1)));
                return found;
            }
            if (store is var (sx, sy))
            {
                var dice = colony.DiceFor("leisure");
                double angle = dice.Uniform(0, 2 * Math.PI), far = dice.Uniform(1, Math.Min(metres, stroll));
                found["camp"].Add((sx + Math.Round(Math.Cos(angle) * far, 1), sy + Math.Round(Math.Sin(angle) * far, 1)));
            }
            return found;
        }
        var held = new HashSet<(int, int)>();
        foreach (var o in workers.Values)
            if (o != w && o.Outing != null)
                held.Add(Ground.TileOf(o.Outing.X, o.Outing.Y));
        var (wx, wy) = Ground.TileOf(w.X, w.Y);
        var (ox, oy) = Ground.TileOf(cx, cy);
        int r = (int)Math.Ceiling(metres);
        double most = metres * metres, campMost = stroll * stroll;
        bool Tree(int x, int y) => g.ObstacleAt(x, y) == "tree";
        bool Clear(int x, int y) => g.Open(x, y) && g.PieceAt(x, y) == null;
        // (Only the tiles of the ground: a colonist near the edge looks at less.)
        for (int y = Math.Max(0, oy - r); y <= Math.Min(g.Height - 1, oy + r); y++)
            for (int x = Math.Max(0, ox - r); x <= Math.Min(g.Width - 1, ox + r); x++)
            {
                if ((x - ox) * (x - ox) + (y - oy) * (y - oy) > most || !Clear(x, y) || g.Indoors(x, y) || held.Contains((x, y)))
                    continue;
                bool trees = Tree(x + 1, y) || Tree(x - 1, y) || Tree(x, y + 1) || Tree(x, y - 1);
                bool water = g.Water(x + 1, y) || g.Water(x - 1, y) || g.Water(x, y + 1) || g.Water(x, y - 1);
                bool open = Clear(x + 1, y) && Clear(x - 1, y) && Clear(x, y + 1) && Clear(x, y - 1);
                bool camp = store is var (scx, scy) && (x + 0.5 - scx) * (x + 0.5 - scx) + (y + 0.5 - scy) * (y + 0.5 - scy) <= campMost;
                if (!(trees || water || open || camp || near) || !g.CanReach(wx, wy, x, y))
                    continue;
                var middle = Ground.Centre(x, y);
                if (near) found["near"].Add(middle);   // any tile at all, for idling between jobs (step 25c)
                if (trees) found["trees"].Add(middle);
                if (water) found["water"].Add(middle);
                if (open) found["open"].Add(middle);
                if (camp) found["camp"].Add(middle);
            }
        return found;
    }

    /// <summary>Where the nearest store is (the camp's heart), or null with none.</summary>
    (double X, double Y)? NearestStoreOf(Worker w)
    {
        var store = Stocks.Inventories.Where(i => i.Holder == Holder.Store)
                          .OrderBy(i => (i.X - w.X) * (i.X - w.X) + (i.Y - w.Y) * (i.Y - w.Y)).ThenBy(i => i.Id.Value).FirstOrDefault();
        return store == null ? null : (store.X, store.Y);
    }

    /// <summary>
    /// An outing goes on (step 25a): `seconds` have passed since they were last processed. On the way, they walk; at
    /// a stop, the time there (never more than is left of the stay) eases their needs and, for practice, teaches;
    /// a stay over moves them on to the next stop, or ends the outing.
    /// </summary>
    void Spend(Worker w, int seconds)
    {
        var o = w.Outing!;
        var activity = ActivityOf(o.Activity);
        if (o.Talk != null && !SpendTalk(w, o))
            return;                                        // a talk not yet under way for them (step 25b)
        if (o.Left == null)
        {
            // The way there is gone, or the workplace taken: unavailable, so they let it go.
            // (A free-time spot is near: its way is worked out only a little beyond `WithinMetres`, Ground.Metres.)
            if (double.IsPositiveInfinity(Far(w.X, w.Y, o.X, o.Y, o.StopsLeft == activity.Stops - 1 ? Reach : 2 * content.Leisure.StrollMetres))
                || o.Site is BuildingId taken && jobs.Values.Any(j => j.Site == taken && j.Worker != null && j.Step == Step.Working))
            {
                EndOuting(w, interrupted: true);
                return;
            }
            if (!Walk(w, o.X, o.Y, near: o.Site != null))
                return;
            o.Left = activity.MinutesEach * Calendar.TicksPerMinute;
            return;                                        // the stay begins now, on arriving: the walk eased nothing
        }
        int spent = Math.Min(seconds, o.Left.Value);
        // Between jobs, free time eases at a share of its usual rate (step 25c, Review 43): free time in their own
        // hours stays worth more.
        double share = o.Between ? content.Leisure.BetweenJobs.EasesShare : 1;
        foreach (var (need, perHour) in activity.EasesPerHour)
            w.FreeNeeds[need] = Math.Max(0, w.FreeNeeds.GetValueOrDefault(need) - perHour * share * spent / Calendar.TicksPerHour);
        if (activity.Teaches > 0 && o.Site is BuildingId at)
            PractiseAt(w, sites[at.Value], (int)(spent * activity.Teaches));
        o.Left -= spent;
        if (o.Left > 0)
            return;
        // The next stop: near the last; for idling, near their work area again (step 25c).
        bool idling = o.Activity == BetweenJobsRules.IdleId;
        var (scx, scy) = idling ? Origin(w) : (o.X, o.Y);
        double sm = idling ? content.Leisure.BetweenJobs.IdleMetres : content.Leisure.StrollMetres;
        if (o.StopsLeft > 0 && Draw(Spots(w, scx, scy, sm, near: idling), activity.Place) is var (nx, ny))
        {
            o.X = nx;
            o.Y = ny;
            o.StopsLeft--;
            o.Left = null;
            Head(w, o.X, o.Y, reach: 2 * content.Leisure.StrollMetres);   // the next stop is near the last
            return;
        }
        EndOuting(w, interrupted: false);
    }

    /// <summary>An outing ends (step 25a): what it held (its spot) is let go with it. Interrupted or unavailable,
    /// that activity waits `RetryMinutes` before it is offered to them again (Review 42, R42-C02).</summary>
    void EndOuting(Worker w, bool interrupted)
    {
        if (w.Outing is not Outing o)
            return;
        if (interrupted && o.Activity != BetweenJobsRules.IdleId)        // (idling is not free time: no wait)
            w.NotBefore[o.Activity] = colony.Now.Tick + content.Leisure.RetryMinutes * Calendar.TicksPerMinute;
        w.Outing = null;
        w.Heading = null;
        if (o.Talk is long talk)
            LeaveTalk(w, talk);                            // the talk goes on with two or more (step 25b)
    }

    /// <summary>What a colonist on an outing is doing, in words.</summary>
    string OutingWords(Worker w, Outing o)
    {
        // Free time taken between jobs says so (step 25c); idling's own words already do.
        string between = o.Between && o.Activity != BetweenJobsRules.IdleId ? ", between jobs" : "";
        if (o.Talk != null)
            return TalkWords(w, o) + between;
        var activity = ActivityOf(o.Activity);
        return (o.Left == null && o.StopsLeft == activity.Stops - 1 ? activity.Doing + " (on the way)" : activity.Doing) + between;
    }
}
