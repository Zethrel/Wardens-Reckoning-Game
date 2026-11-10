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
    public bool Ill(Worker w) => w.Illness is Illness i && colony.Now.Tick >= i.Shows && colony.Now.Tick < i.Ends;

    // Each minute: an illness shows, or is over (and the player is told).
    void Sicken()
    {
        foreach (var w in workers.Values)
        {
            if (w.Illness is not Illness i)
                continue;
            if (colony.Now.Tick == i.Shows)
                Tell(w.Name + " is ill: " + i.Name.ToLowerInvariant() + ".");
            if (colony.Now.Tick >= i.Ends)
            {
                w.Illness = null;
                w.Slow = 0;
                Tell(w.Name + " is well again.");
            }
        }
    }

    /// <summary>
    /// How many of `seconds` of work count, for a colonist working slower while ill: they keep a running count of
    /// the work they miss (0.3 a second at 70%), and a second is lost each time it reaches one. No dice: the same
    /// seconds are lost every time. A colonist who is well: all of them.
    /// </summary>
    internal int Effort(Worker w, int seconds)
    {
        if (!Ill(w) || seconds <= 0)
            return seconds;
        w.Slow += seconds * (1 - content.UpsetStomach.Work);
        int lost = (int)Math.Floor(w.Slow + 1e-9);
        w.Slow = Math.Max(0, w.Slow - lost);
        return seconds - lost;
    }

    /// <summary>How many times as fast a need grows for this colonist now (thirst and fatigue while ill).</summary>
    internal double Faster(Worker w, NeedRule rule) =>
        !Ill(w) ? 1 : rule.Name == content.Thirst.Name ? content.UpsetStomach.Thirst : rule.Name == content.Fatigue.Name ? content.UpsetStomach.Fatigue : 1;
}
