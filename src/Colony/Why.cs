// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;

namespace WardensReckoning.Colony;

/// <summary>
/// Why something is waiting, three ways (FP-06): for the player, what is wrong and what they could do about it; and
/// for the developer, a trace with the numbers behind it (job, colonist and tick), kept apart from the player's
/// words. The player's words only use what the colony knows about itself.
/// </summary>
public sealed record Explanation(string Player, string NextStep, string Trace);

/// <summary>
/// Who may do which work (the player's policy, FP-03), and why a job is waiting (FP-06): the third part of `Work`.
///
/// Every job is of a kind: "haul" (carry goods), "care" (bring food or water to a collapsed colonist) or the id
/// of the workshop it is worked at ("quern"). The player may forbid a colonist any kind; a colonist is only given
/// jobs of kinds they are allowed, and only when free for work (lesson 14a): their needs and schedule still come
/// first, whatever they are allowed.
/// </summary>
public sealed partial class Work
{
    public const string HaulWork = "haul";
    public const string CareWork = "care";

    /// <summary>The kinds of work there are: hauling, care, and each workshop.</summary>
    public IEnumerable<string> Kinds => new[] { BuildWork, CareWork, HaulWork }.Concat(content.Workshops.Keys);

    /// <summary>The kind of a job.</summary>
    public string KindOf(Job job) =>
        job.Kind == JobKind.Build ? BuildWork
        : job.Kind == JobKind.Craft ? sites[job.Site!.Value.Value].Workshop
        : workers.Values.Any(w => w.Arms == job.To) ? CareWork : HaulWork;

    /// <summary>Allows or forbids a colonist a kind of work. A job of a kind newly forbidden that they hold goes back
    /// to be taken by someone else (goods in their arms stay with them until delivered, as when stopped).</summary>
    public void Permit(ColonistId id, string kind, bool allowed)
    {
        if (!Kinds.Contains(kind))
            throw new ArgumentException("no kind of work \"" + kind + "\" (there are: " + string.Join(", ", Kinds) + ")", nameof(kind));
        var worker = workers[id.Value];
        if (allowed)
            worker.Forbidden.Remove(kind);
        else
            worker.Forbidden.Add(kind);
        if (!allowed && worker.Job is TaskId t && jobs[t.Value] is var job && KindOf(job) == kind
            && !InHand(job))
            Reopen(job, worker);
        Urge(worker);
    }

    /// <summary>Why a job is waiting for someone to take it, or null if it is not (it has a worker, or does not
    /// exist).</summary>
    public Explanation? Why(TaskId id)
    {
        if (!jobs.TryGetValue(id.Value, out var job) || job.Worker != null)
            return null;
        string kind = KindOf(job), what = Words(kind);
        string trace = "task " + id.Value + " (" + kind + ", " + job.Purpose + ") open at tick "
                       + colony.Now.Tick.ToString(CultureInfo.InvariantCulture) + ": ";
        // Those allowed (not counting the fallen colonist a care job is for: nobody cares for themselves).
        var allowed = workers.Values.Where(w => !w.Forbidden.Contains(kind) && w.Arms != job.To).ToList();
        if (allowed.Count == 0)
            return new Explanation("Nobody is allowed to " + what + ".",
                                   "Allow someone to " + what + " in their work settings.",
                                   trace + "no worker permits '" + kind + "' (" + workers.Count + " workers)");
        // Each allowed colonist's reason for not taking it, nearest to the job first.
        var reasons = allowed.Select(w => (Worker: w, Reason: NotNow(w, job))).ToList();
        string list = string.Join("; ", reasons.Select(r => r.Worker.Name + " " + r.Reason.Player));
        string next = reasons.Select(r => r.Reason.Next).FirstOrDefault(n => n != null)
                      ?? "Wait: it is taken in the next minute.";
        return new Explanation("Waiting for someone to " + what + ": " + list + ".", next,
                               trace + string.Join("; ", reasons.Select(r => "worker " + r.Worker.Id.Value + ": " + r.Reason.Trace)));
    }

    /// <summary>The kind of work in words: "carry goods", "bring food or water to the fallen", "work the hand quern".</summary>
    string Words(string kind) => kind switch
    {
        BuildWork => "build",
        HaulWork => "carry goods",
        CareWork => "bring food or water to the fallen",
        _ => "work the " + content.Workshops[kind].Name.ToLowerInvariant(),
    };

    /// <summary>Why an allowed colonist is not taking work now: for the player, a next step if there is one, and
    /// a trace.</summary>
    (string Player, string? Next, string Trace) NotNow(Worker w, Job job)
    {
        if (w.Collapsed != null)
            return ("has collapsed (" + w.Collapsed + ")", "See that there is " + (w.Collapsed == "fatigue" ? "time to sleep" : "food and water") + " in the stores.", "collapsed " + w.Collapsed);
        if (w.Stopped)
            return ("is stopped", "Resume " + w.Name + ".", "stopped");
        if (w.Job is TaskId t)
            return ("is busy (" + jobs[t.Value].Purpose + ")", null, "busy task " + t.Value);
        if (w.Asleep)
            return ("is asleep", null, "asleep");
        if (w.Personal is Personal p)
            return ("is " + Doing(w.Id), null, "personal " + p.Kind);
        var block = BlockOf(w);
        if (block != Block.Work)
        {
            string at = NextWorkHour(w);
            return ("is not in work hours (" + block.ToString().ToLowerInvariant() + ") until " + at, null, "block " + block + ", work at " + at);
        }
        var urgent = content.Needs.Values.FirstOrDefault(r => w.Needs[r.Name].Level >= r.Urgent);
        if (urgent != null)
            return ("is too " + (urgent.Name == content.Hunger.Name ? "hungry" : urgent.Name == content.Thirst.Name ? "thirsty" : "tired") + " to work"
                    + (w.Lacking != null ? " (" + w.Lacking + ")" : ""),
                    w.Lacking != null ? "Bring " + (urgent.Name == content.Hunger.Name ? "rations" : "water") + " to a store." : null,
                    "urgent " + urgent.Name + " " + w.Needs[urgent.Name].Level.ToString("0.#", CultureInfo.InvariantCulture));
        var (x, y) = Spot(job);
        if (double.IsPositiveInfinity(Far(w.X, w.Y, x, y)))
            return ("cannot get there", "Open a way: leave a gap or put a door in the wall.", "no way from " + Ground.TileOf(w.X, w.Y));
        return ("is free and will take it", null, "free");
    }

    /// <summary>When the colonist's next Work hour begins, as "08:00".</summary>
    string NextWorkHour(Worker w)
    {
        var hours = content.Schedules[w.Schedule].Hours;
        for (int ahead = 1; ahead <= Calendar.HoursPerDay; ahead++)
        {
            int hour = (colony.Now.Hour + ahead) % Calendar.HoursPerDay;
            if (hours[hour] == Block.Work)
                return hour.ToString("00", CultureInfo.InvariantCulture) + ":00";
        }
        return "never (no work hours in their schedule)";
    }

    // ---- Why a colonist is doing what they do (step 26c, the UI plan's UI-03) -----------------------------------

    /// <summary>
    /// Why a colonist is doing what they do now (step 26c, UI plan UI-03), three ways as for a job (FP-06): a short
    /// reason for the player ("In work hours · no work they are allowed"), what would help if anything would, and the
    /// developer's trace. Only from what the colony keeps about itself: their state, their schedule, the jobs there
    /// are, who holds them, what the colonist is allowed, and whether a way leads there. Nothing is guessed.
    /// </summary>
    public Explanation WhyDoing(ColonistId id)
    {
        var w = workers[id.Value];
        long now = colony.Now.Tick;
        var block = BlockOf(w);
        string trace = "worker " + id.Value + " at tick " + now.ToString(CultureInfo.InvariantCulture) + ", block " + block;
        if (w.Collapsed is string felled)
            return new Explanation("Collapsed: their " + felled + " was at its limit too long.",
                felled == content.Fatigue.Name ? "They sleep where they fell, and come round once rested."
                    : "Someone allowed to bring food or water to the fallen must reach them.",
                trace + "; collapsed (" + felled + ")");
        if (w.Asleep)
            return new Explanation(block == Block.Sleep ? "Asleep: their sleep time." : "Asleep outside their sleep time: too tired to go on.",
                                   "", trace + "; asleep" + (w.OnBedroll ? " on a bedroll" : " on the ground"));
        if (w.Personal is Personal personal)
        {
            var rule = personal.Kind switch
            {
                PersonalKind.Eat => content.Hunger,
                PersonalKind.Drink => content.Thirst,
                PersonalKind.Sleep => content.Fatigue,
                _ => content.Hygiene,
            };
            return new Explanation("Seeing to their " + rule.Name + ": " + Inspect.Word(rule, w.Needs[rule.Name].Level) + ".", "",
                                   trace + "; personal " + personal.Kind + ", level " + w.Needs[rule.Name].Level.ToString("0.0", CultureInfo.InvariantCulture));
        }
        if (w.Stopped)
            return new Explanation("Stopped by your order.", "Resume them to let them work again.", trace + "; stopped");
        if (w.Job is TaskId t && jobs.TryGetValue(t.Value, out var held))
            return new Explanation((block == Block.Work ? "In work hours: " : "Finishing work in hand after their work hours: ") + Words(KindOf(held)) + ".",
                                   "", trace + "; task " + t.Value + " (" + KindOf(held) + ", " + held.Step + ")");
        if (w.BetweenSince is long since && block == Block.Work)
        {
            var (why, next, counts) = NoWork(w);
            long freeFrom = since + (long)content.Leisure.BetweenJobs.WaitMinutes * Calendar.TicksPerMinute;
            string stage = w.Outing is { } o && o.Activity != BetweenJobsRules.IdleId
                ? "; free time near their work, at a share of its easing, until work comes"
                : now < freeFrom ? "; pottering about, and from " + Clock(freeFrom) + " free time near their work"
                : "; pottering about";
            return new Explanation("In work hours · " + why + stage + ".", next,
                                   trace + "; between jobs since " + since.ToString(CultureInfo.InvariantCulture) + "; " + counts);
        }
        if (w.Outing is Outing outing)
            return new Explanation("Their free time (" + block.ToString().ToLowerInvariant() + "): " + ActivityOf(outing.Activity).Name.ToLowerInvariant()
                                   + ", chosen by their needs and what they enjoy.", "", trace + "; outing " + outing.Activity);
        return block switch
        {
            Block.Work => new Explanation("In work hours: they look for work at their next think.", "", trace + "; next think " + w.NextThink),
            Block.Meals => new Explanation("Meal time: they eat and drink if they need to.", "", trace),
            Block.Sleep => new Explanation("Their sleep time.", "", trace),
            _ => w.LookAgain > now
                ? new Explanation("Their free time: nothing to do nearby just now; they look again at " + Clock(w.LookAgain) + ".",
                                  "", trace + "; look again at " + w.LookAgain)
                : new Explanation("Their free time: they choose what to do at their next think.", "", trace + "; next think " + w.NextThink),
        };
    }

    /// <summary>A tick's time of day, "17:40".</summary>
    static string Clock(long tick) => new Moment(tick) is var m
        ? m.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + m.Minute.ToString("00", CultureInfo.InvariantCulture) : "";

    /// <summary>
    /// Why a colonist free for work has none (step 26c), told apart as the plan asks: no work at all; only paused
    /// work; work they are not allowed; work out of their reach; or the work they may do already taken by others.
    /// Returns the few words, what would help, and the counts for the trace.
    /// </summary>
    (string Why, string Next, string Counts) NoWork(Worker w)
    {
        bool Paused(Job j) => j.Kind == JobKind.Build && projects[j.Site!.Value.Value].Paused;
        bool Allowed(Job j) => !w.Forbidden.Contains(KindOf(j)) && w.Arms != j.To;     // nobody cares for themselves
        var open = jobs.Values.Where(j => j.Step == Step.Open).ToList();
        var going = open.Where(j => !Paused(j)).ToList();
        var allowed = going.Where(Allowed).ToList();
        var reachable = allowed.Where(j => Spot(j) is var (x, y) && !double.IsPositiveInfinity(Far(w.X, w.Y, x, y))).ToList();
        bool othersHold = jobs.Values.Any(j => j.Worker != null && j.Worker != w.Id && Allowed(j));
        string counts = "open " + open.Count + ", not paused " + going.Count + ", allowed " + allowed.Count + ", reachable " + reachable.Count
                        + (othersHold ? ", others hold work they may do" : "");
        // Planned building that cannot be worked yet (nobody can reach it, its materials missing): its own reason.
        var waits = projects.Values.FirstOrDefault(p => !p.Paused && p.Blocked != null);
        if (going.Count == 0)
            return othersHold ? ("the work they may do is taken by others", "", counts)
                 : open.Count > 0 ? ("the only work is paused", "Resume a paused building.", counts)
                 : waits != null ? ("the building planned waits: " + waits.Blocked, waits.NextStep ?? "", counts + ", a project waits")
                 : ("no work to do", "Plan something to build, or order work at a workshop.", counts);
        if (allowed.Count == 0)
        {
            string kinds = string.Join(", ", going.Select(KindOf).Distinct().OrderBy(k => k, StringComparer.Ordinal).Select(Words));
            return othersHold ? ("the work they may do is taken by others; the rest they are not allowed (" + kinds + ")",
                                 "Allow them more kinds of work in their work settings.", counts)
                 : ("no work they are allowed (there is work to " + kinds + ")", "Allow them more kinds of work in their work settings.", counts);
        }
        if (reachable.Count == 0)
            return ("the work they may do is out of their reach", "Clear a way to it: no way leads there from where they are.", counts);
        return ("there is work for them: they take it at their next think", "", counts);
    }
}
