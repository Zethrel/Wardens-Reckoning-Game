// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// Learning by practice, and forgetting (step 23b; design library: Skills, Traits and Knowledge Progression §9,
/// §10). Work teaches the skill it uses; what it teaches depends on how challenging it is for the colonist, their
/// aptitude, and whether they were once better. A skill left unpractised fades after a while, but never below a
/// share of the best they reached.
/// </summary>
public sealed partial class Work
{
    /// <summary>
    /// How much a job of `challenge` teaches a colonist of `proficiency`, as a share of `PointsPerHour` (before
    /// aptitude and recovery): 1 when they are as good as the work is hard; more when it is above them (at most
    /// `Most`); less as they get past it, and nothing once they are `Reach` past it. Easy work keeps teaching a
    /// beginner, but cannot make a master (§5.3, §9.2).
    /// </summary>
    internal double Teaches(double challenge, double proficiency)
    {
        var rules = content.Learning;
        return Math.Clamp((challenge + rules.Reach - proficiency) / rules.Reach, 0, rules.Most);
    }

    /// <summary>The challenge of a job (step 23b): its piece's, its recipe's, or boiling's; null for work that uses
    /// no skill.</summary>
    internal double? ChallengeOf(Job job) => job.Kind switch
    {
        JobKind.Build => content.Challenge[projects[job.Site!.Value.Value].Piece],
        JobKind.Craft => content.Challenge[job.Recipe!],
        JobKind.Boil => content.Challenge[BoilWork],
        _ => null,
    };

    /// <summary>`seconds` of work at `job` teach the colonist the skill it uses (step 23b).</summary>
    internal void Practise(Worker w, Job job, int seconds)
    {
        if (SkillOf(job) is string skill && ChallengeOf(job) is double challenge)
            Learn(w, skill, challenge, seconds);
    }

    /// <summary>Practice in free time at a workplace (step 25a): `seconds` (already the share of the time spent that
    /// practice teaches) of its skill, at the challenge of its hardest recipe.</summary>
    internal void PractiseAt(Worker w, Site site, int seconds)
    {
        if (content.SkillFor.GetValueOrDefault(site.Workshop) is not string skill)
            return;
        double challenge = content.Recipes.Values.Where(r => r.Workshop == site.Workshop)
                                  .Select(r => content.Challenge.GetValueOrDefault(r.Id)).DefaultIfEmpty(0).Max();
        Learn(w, skill, challenge, seconds);
    }

    /// <summary>`seconds` of practice at a challenge teach the colonist a skill (step 23b).</summary>
    void Learn(Worker w, string skill, double challenge, int seconds)
    {
        if (seconds <= 0 || !w.Skills.TryGetValue(skill, out var state))
            return;
        var rules = content.Learning;
        double gain = rules.PointsPerHour * seconds / Calendar.TicksPerHour * Teaches(challenge, state.Proficiency) * state.Aptitude;
        // Below the best they ever were: it comes back faster (§10.3), though not past that best at that pace.
        if (state.Proficiency < state.Peak)
            gain = Math.Min(gain * rules.Recovery, Math.Max(gain, state.Peak - state.Proficiency));
        state.Proficiency = Math.Min(100, state.Proficiency + gain);
        state.Practised += seconds;
        state.LastPractised = colony.Now.Tick;
    }

    /// <summary>
    /// Each morning (step 23b): a skill not practised for `DecayAfterDays` loses `DecayPerDay`, but never goes below
    /// `KeptShare` of the best the colonist reached (§10.1, §10.2).
    /// </summary>
    internal void Forget()
    {
        var rules = content.Learning;
        long now = colony.Now.Tick;
        foreach (var w in workers.Values)
            foreach (var state in w.Skills.Values)
            {
                if (now - state.LastPractised < rules.DecayAfterDays * Calendar.TicksPerDay)
                    continue;
                double floor = rules.KeptShare * state.Peak;
                if (state.Proficiency > floor)
                    state.Lose(Math.Min(rules.DecayPerDay, state.Proficiency - floor));
            }
    }

    /// <summary>
    /// What the player is told of a colonist's aptitude for a skill (step 23b): nothing until they have practised it
    /// for `AptitudeSeenHours` (it is seen in how fast they learn, not known beforehand); then "learns quickly",
    /// "learns slowly", or nothing for the usual.
    /// </summary>
    public string? AptitudeSeen(SkillState state)
    {
        var rules = content.Learning;
        if (state.Practised < rules.AptitudeSeenHours * Calendar.TicksPerHour)
            return null;
        return state.Aptitude >= rules.Quick ? "learns quickly" : state.Aptitude <= rules.Slow ? "learns slowly" : null;
    }
}
