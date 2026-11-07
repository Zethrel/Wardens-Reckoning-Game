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
            && job.Step is not (Step.Carrying or Step.Unloading))
            Reopen(job, worker);
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
}
