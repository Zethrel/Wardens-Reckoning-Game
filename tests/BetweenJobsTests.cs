// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 25c: between jobs in Work hours (design library Review 43: Colonist Daily Life and Needs §11.1, with its
/// verification rows). Founders on the free-time meadow (FreeTimeTests) at 09:00, in their Work hours, with nothing
/// ordered: they idle near their work area, after the wait they may take free time near it at half its easing, and a
/// job added is taken at once by the nearest of them while the others carry on.
/// </summary>
public class BetweenJobsTests
{
    static Colony.Colony Found(int hour = 9, int seed = 7) => FreeTimeTests.Found(hour, seed: seed);

    static BetweenJobsRules Rules(Colony.Colony colony) => colony.Content!.Leisure.BetweenJobs;

    static long Wait(Colony.Colony colony) => (long)Rules(colony).WaitMinutes * Calendar.TicksPerMinute;

    static double Metres(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    // The nearest store or workplace to (x, y): a colonist's work area before they have worked (R43-C03).
    static (double X, double Y) Facility(Colony.Colony colony, double x, double y)
    {
        var places = colony.Stocks.Inventories.Where(i => i.Holder == Holder.Store).Select(i => (i.X, i.Y))
                           .Concat(colony.Work!.Sites.Select(s => (s.X, s.Y)));
        return places.OrderBy(p => Metres(p.Item1, p.Item2, x, y)).First();
    }

    [Fact]
    public void With_no_work_in_work_hours_they_idle_near_their_work_area_and_it_eases_nothing()
    {
        var colony = Found();
        var work = colony.Work!;
        var rules = Rules(colony);
        var before = work.Workers.ToDictionary(w => w.Id, w => w.FreeNeeds.ToDictionary(n => n.Key, n => n.Value));
        bool idled = false;
        // Until the wait is over: only idling, never free time, and never far from where they started.
        colony.Run(Wait(colony) - 2 * Calendar.TicksPerMinute, c =>
        {
            foreach (var w in c.Work!.Workers)
            {
                if (w.Outing is not Outing o)
                    continue;
                Assert.Equal(BetweenJobsRules.IdleId, o.Activity);
                Assert.True(o.Between);
                var (ax, ay) = Facility(c, o.X, o.Y);
                Assert.True(Metres(o.X, o.Y, ax, ay) <= rules.IdleMetres + 1, w.Name + " idles " + Metres(o.X, o.Y, ax, ay) + " m from the camp");
                idled = true;
            }
        });
        Assert.True(idled, "nobody idled");
        Assert.All(work.Workers, w => Assert.NotNull(w.BetweenSince));
        // Idling eased nothing: the free-time needs only rose.
        Assert.All(work.Workers, w => Assert.All(w.FreeNeeds, n => Assert.True(n.Value >= before[w.Id][n.Key], w.Name + "'s " + n.Key + " fell")));
        var idler = work.Workers.First(w => w.Outing != null);
        Assert.Equal(rules.Idle.Doing, work.Doing(idler.Id));
    }

    [Fact]
    public void After_the_wait_they_may_take_free_time_within_the_shorter_reach()
    {
        var colony = Found();
        var rules = Rules(colony);
        long start = colony.Now.Tick;
        var seen = new List<(long Tick, string Who, Outing Outing)>();
        colony.Run(3 * Calendar.TicksPerHour, c =>
        {
            foreach (var w in c.Work!.Workers)
                if (w.Outing is Outing o && o.Activity != BetweenJobsRules.IdleId && !seen.Any(s => ReferenceEquals(s.Outing, o)))
                    seen.Add((c.Now.Tick, w.Name, o));
        });
        Assert.NotEmpty(seen);
        Assert.All(seen, s =>
        {
            Assert.True(s.Outing.Between, s.Who + "'s " + s.Outing.Activity + " is not marked between jobs");
            Assert.True(s.Tick > start + Wait(colony), s.Who + " took free time before the wait was over");
            if (s.Outing.Talk == null)
            {
                var (ax, ay) = Facility(colony, s.Outing.X, s.Outing.Y);
                Assert.True(Metres(s.Outing.X, s.Outing.Y, ax, ay) <= rules.WithinMetres + 1, s.Who + "'s " + s.Outing.Activity + " is too far");
            }
        });
        Assert.True(seen.Select(s => s.Outing.Activity).Distinct().Count() >= 2, string.Join(", ", seen.Select(s => s.Outing.Activity)));
    }

    [Fact]
    public void Free_time_between_jobs_eases_at_the_share_set_in_content()
    {
        var colony = Found();
        var content = colony.Content!;
        var rules = Rules(colony);
        foreach (var w in colony.Work!.Workers)
            foreach (var need in w.FreeNeeds.Keys.ToList())
                w.FreeNeeds[need] = 60;
        // Each whole stay at a stop (not a talk, not idling): the need at its start and end, and the minutes in between
        // (each adds the need's rise).
        var stays = new List<(Activity Activity, double Before, double After, int Minutes)>();
        var open = new Dictionary<ColonistId, (Outing Outing, int StopsLeft, double Before, int Minutes)>();
        colony.Run(3 * Calendar.TicksPerHour, c =>
        {
            foreach (var w in c.Work!.Workers)
            {
                double now = w.FreeNeeds["recreation"];
                if (open.TryGetValue(w.Id, out var stay))
                {
                    if (ReferenceEquals(w.Outing, stay.Outing) && w.Outing!.StopsLeft == stay.StopsLeft && w.Outing.Left != null)
                    {
                        open[w.Id] = stay with { Minutes = stay.Minutes + (c.Now.OnTheMinute ? 1 : 0) };
                        continue;
                    }
                    open.Remove(w.Id);
                    // Over when the stay ended by itself (the next stop, or the outing done), not cut short.
                    var activity = content.Leisure.Activities.First(a => a.Id == stay.Outing.Activity);
                    bool whole = ReferenceEquals(w.Outing, stay.Outing) ? w.Outing!.StopsLeft == stay.StopsLeft - 1 : w.Outing == null && stay.StopsLeft == 0;
                    if (whole && w.Job == null)
                        stays.Add((activity, stay.Before, now, stay.Minutes + (c.Now.OnTheMinute ? 1 : 0)));
                }
                if (w.Outing is Outing o && o.Between && o.Talk == null && o.Activity != BetweenJobsRules.IdleId && o.Left != null)
                    open[w.Id] = (o, o.StopsLeft, now, 0);
            }
        });
        Assert.NotEmpty(stays);
        double rise = content.Leisure.Needs["recreation"].PerHour / Calendar.MinutesPerHour;
        Assert.All(stays, s =>
        {
            double eased = s.Activity.EasesPerHour.GetValueOrDefault("recreation") * rules.EasesShare * s.Activity.MinutesEach / Calendar.MinutesPerHour;
            Assert.Equal(s.Before + s.Minutes * rise - eased, s.After, 6);
        });
    }

    [Fact]
    public void A_new_job_is_taken_at_once_by_the_nearest_and_the_others_carry_on_their_wait_still_counting()
    {
        var colony = Found();
        var work = colony.Work!;
        colony.Run(Wait(colony) + 20 * Calendar.TicksPerMinute);
        Assert.Contains(work.Workers, w => w.Outing != null);
        var since = work.Workers.ToDictionary(w => w.Id, w => w.BetweenSince);
        var outings = work.Workers.ToDictionary(w => w.Id, w => w.Outing);
        var thinks = work.Workers.ToDictionary(w => w.Id, w => w.NextThink);
        var (project, placement) = work.Plan("road", 20, 20);
        Assert.True(placement.Ok, placement.Why);
        Assert.True(work.Reconsider);
        colony.Run(1);
        // Taken on the very next tick, by one colonist, though nobody's think may have come.
        var taker = Assert.Single(work.Workers, w => w.Job != null);
        Assert.Null(taker.Outing);
        Assert.Null(taker.BetweenSince);
        Assert.False(work.Reconsider);
        // The others carry on: the same outing (or none), the same wait (R43-C01, R43-C02).
        foreach (var w in work.Workers.Where(w => w != taker && thinks[w.Id] > colony.Now.Tick))
        {
            Assert.Equal(since[w.Id], w.BetweenSince);
            Assert.Same(outings[w.Id], w.Outing);
        }
        // While they hold the job, they are not between jobs, whatever they wait on (R43-C04).
        colony.Run(10 * Calendar.TicksPerMinute, c =>
        {
            foreach (var w in c.Work!.Workers.Where(w => w.Job != null))
                Assert.Null(w.BetweenSince);
        });
    }

    [Fact]
    public void Between_jobs_at_a_distant_workplace_they_stay_near_it()
    {
        var colony = Found();
        var work = colony.Work!;
        var w = work.Workers.First();
        var (fx, fy) = Facility(colony, w.X, w.Y);
        // Where they last finished work: 18 m from the camp's store, on open ground.
        w.WorkedAt = (fx + 18, fy);
        var rules = Rules(colony);
        var stops = new List<(double X, double Y)>();
        colony.Run(Wait(colony) + Calendar.TicksPerHour, c =>
        {
            if (w.Outing is Outing o && o.Talk == null && (stops.Count == 0 || stops[^1] != (o.X, o.Y)))
                stops.Add((o.X, o.Y));
        });
        Assert.NotEmpty(stops);
        Assert.All(stops, s => Assert.True(Metres(s.X, s.Y, fx + 18, fy) <= rules.WithinMetres + 1, "a stop " + Metres(s.X, s.Y, fx + 18, fy) + " m from where they worked"));
        Assert.Contains(stops, s => Metres(s.X, s.Y, fx, fy) > rules.IdleMetres + 1);   // not back at the camp
    }

    [Fact]
    public void Out_of_work_hours_nobody_is_between_jobs_and_free_time_goes_as_before()
    {
        // 16:00 to 18:00: an hour of Work, then Recreation.
        var colony = Found(16);
        var work = colony.Work!;
        colony.Run(Calendar.TicksPerHour - 1);
        Assert.Contains(work.Workers, w => w.BetweenSince != null);
        colony.Run(Calendar.TicksPerHour);
        Assert.All(work.Workers, w => Assert.Null(w.BetweenSince));
        Assert.All(work.Workers, w => Assert.Null(w.WorkedAt));
        Assert.All(work.Workers.Where(w => w.Outing != null), w => Assert.False(w.Outing!.Between));
        Assert.Contains(work.Workers, w => w.Outing != null);
    }

    [Fact]
    public void The_same_seeds_give_the_same_day_between_jobs()
    {
        string Day(int seed)
        {
            var colony = Found(seed: seed);
            var log = new System.Text.StringBuilder();
            colony.Run(3 * Calendar.TicksPerHour, c =>
            {
                foreach (var w in c.Work!.Workers)
                    if (w.Outing is Outing o && o.Left == null && o.StopsLeft == (o.Activity == BetweenJobsRules.IdleId ? Rules(c).Idle.Stops - 1 : -1))
                        log.Append(c.Now.Tick).Append(w.Name).Append(o.Activity).Append(o.X).Append(o.Y).Append(';');
            });
            return log + ColonySave.Fingerprint(colony);
        }
        Assert.Equal(Day(7), Day(7));
        Assert.NotEqual(Day(7), Day(8));
    }

    [Fact]
    public void A_save_between_jobs_goes_on_as_if_never_saved()
    {
        var colony = Found();
        colony.Run(Wait(colony));
        colony.Work!.Plan("road", 20, 20);                 // a job added, waiting to be taken at the next tick
        long end = colony.Now.Tick + 2 * Calendar.TicksPerHour;
        var copies = new List<Colony.Colony> { ColonySave.Read(ColonySave.Text(colony), colony.Content!) };
        bool idling = false, free = false;
        colony.Run(end - colony.Now.Tick, c =>
        {
            if (c.Now.Tick % 97 != 0)
                return;
            idling |= c.Work!.Workers.Any(w => w.Outing?.Activity == BetweenJobsRules.IdleId);
            free |= c.Work.Workers.Any(w => w.Outing is { Between: true } o && o.Activity != BetweenJobsRules.IdleId);
            string text = ColonySave.Text(c);
            var copy = ColonySave.Read(text, c.Content!);
            Assert.Equal(text, ColonySave.Text(copy));
            copies.Add(copy);
        });
        Assert.True(idling && free, "no save caught idling and free time between jobs");
        string fingerprint = ColonySave.Fingerprint(colony);
        foreach (var copy in copies.Where((_, i) => i % 12 == 0))
        {
            copy.Run(end - copy.Now.Tick);
            Assert.Equal(fingerprint, ColonySave.Fingerprint(copy));
        }
    }
}
