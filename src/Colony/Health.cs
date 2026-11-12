// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>An illness a colonist has caught: which (health.json), when it shows, and when it ends.</summary>
public sealed record Illness(string Name, long Shows, long Ends);

/// <summary>
/// Illness (step 19b, library Review 37: Healthcare §10 item 8, the first noncombat condition). The eighth part of
/// `Work`.
///
/// Drinking fouled water is an **exposure** (Sanitation supplies it); it does not always make anyone ill: on
/// Healthcare's own dice, `chance` (a third). If it does, the **upset stomach** shows 6 to 12 hours later and lasts
/// 1 to 2 days: the colonist works at 70% (`Slowed`), and thirst and fatigue grow half again as fast. It ends by
/// itself, and does not pass from one colonist to another. The player is told when it shows and when it is over:
/// what they see is the illness; that the water caused it is for them to work out.
/// </summary>
public sealed partial class Work
{
    /// <summary>A colonist drank fouled water: they may fall ill (not again while already ill or about to be).</summary>
    internal void Expose(Worker w)
    {
        var rule = content.UpsetStomach;
        var dice = colony.DiceFor("health");
        if (w.Illness != null || !dice.Chance(rule.Chance))
            return;
        // On a whole minute (illness shows and passes on the minutes, with the colony's own work: step 21c).
        long minute = (colony.Now.Tick + Calendar.TicksPerMinute - 1) / Calendar.TicksPerMinute * Calendar.TicksPerMinute;
        long shows = minute + (long)dice.Int(rule.OnsetFrom, rule.OnsetTo) * Calendar.TicksPerHour;
        long ends = shows + (long)dice.Int(rule.LastsFrom, rule.LastsTo) * Calendar.TicksPerHour;
        w.Illness = new Illness(rule.Name, shows, ends);
    }

    /// <summary>Whether a colonist's illness has shown and not yet ended.</summary>
    public bool Ill(Worker w) => IllAt(w, colony.Now.Tick);

    /// <summary>Whether a colonist's illness had shown and not yet ended at tick `tick`.</summary>
    static bool IllAt(Worker w, long tick) => w.Illness is Illness i && tick >= i.Shows && tick < i.Ends;

    // Illnesses over this tick, forgotten at its end (Work.Tick), once the work done while ill has been counted.
    readonly List<Worker> ended = new();

    /// <summary>
    /// Each minute: an illness shows, or is over (and the player is told). Either changes how fast the colonist
    /// works, so it wakes them (step 24, Review 41): the work since they were last processed is counted at the pace
    /// it was done at, and their deadline is worked out again at the new one.
    /// </summary>
    void Sicken()
    {
        long now = colony.Now.Tick;
        foreach (var w in workers.Values)
        {
            if (w.Illness is not Illness i)
                continue;
            if (now == i.Shows)
            {
                Tell(w.Name + " is ill: " + i.Name.ToLowerInvariant() + ".");
                w.ActionDue = now;
            }
            if (now >= i.Ends && !ended.Contains(w))
            {
                ended.Add(w);
                Tell(w.Name + " is well again.");
                w.ActionDue = now;
            }
        }
    }

    /// <summary>
    /// How many of `seconds` of work count, for a colonist working slower while ill: they keep a running count of
    /// the work they miss (0.3 a second at 70%), and a second is lost each time it reaches one. No dice: the same
    /// seconds are lost every time. A colonist who is well: all of them.
    /// </summary>
    /// <remarks>The `seconds` are the ticks just past, so they are worked at the pace of their first tick (step 24): a
    /// change of pace wakes the colonist (`Sicken`), so the pace did not change during them.</remarks>
    internal int Effort(Worker w, Job job, int seconds) => Effort(w, Pace(w, job, colony.Now.Tick - seconds), seconds);

    /// <summary>How fast a colonist works at a job (now, or at tick `at`): their skill at it (step 23a: 1 at the usual
    /// proficiency) times their health (an ill colonist at 70%).</summary>
    internal double Pace(Worker w, Job job, long? at = null) =>
        (SkillOf(job) is string skill && w.Skills.TryGetValue(skill, out var state) ? content.SpeedAt(state.Proficiency) : 1)
        * (IllAt(w, at ?? colony.Now.Tick) ? content.UpsetStomach.Work : 1);

    /// <summary>The skill a job uses (step 23a): building, boiling or a workshop's recipe, as skills.json says; null
    /// for work that uses none (carrying).</summary>
    internal string? SkillOf(Job job) => content.SkillFor.GetValueOrDefault(
        job.Kind switch
        {
            JobKind.Build => BuildWork,
            JobKind.Boil => BoilWork,
            JobKind.Craft => sites[job.Site!.Value.Value].Workshop,
            _ => "",
        });

    /// <summary>
    /// The seconds of work `seconds` are worth at `pace` (1: all of them; 0.7: seven in ten). Seconds are whole, so
    /// what is left over is carried in `Carry` until it makes a whole second: ten seconds at 0.7 give 7, and so do
    /// ten single seconds one at a time; at 1.5, ten give 15. No dice: the same every time.
    /// </summary>
    internal static int Effort(Worker w, double pace, int seconds)
    {
        if (seconds <= 0)
            return 0;
        if (pace == 1 && w.Carry == 0)
            return seconds;
        w.Carry += seconds * pace;
        int done = (int)Math.Floor(w.Carry + 1e-9);
        w.Carry = Math.Max(0, w.Carry - done);
        return done;
    }

    /// <summary>How many times as fast a need grows for this colonist now (thirst and fatigue while ill).</summary>
    internal double Faster(Worker w, NeedRule rule) =>
        !Ill(w) ? 1 : rule.Name == content.Thirst.Name ? content.UpsetStomach.Thirst : rule.Name == content.Fatigue.Name ? content.UpsetStomach.Fatigue : 1;
}
