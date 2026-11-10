// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// The autosave (library Review 30): every in-game morning, into one of three slots in turn, kept apart from the
/// player's own saves. Each morning writes the slot after the one the morning before wrote (1, 2, 3, 1...): the
/// slot comes from the day's number, so the three newest mornings are always kept and no counter needs saving.
/// </summary>
public static class Autosave
{
    /// <summary>When the morning autosave happens: 06:00 (a first value; change it here).</summary>
    public const int MorningMinute = 6 * Calendar.MinutesPerHour;

    public const int Slots = 3;

    /// <summary>Whether a morning was passed going from `before` to `after` (after it, not at `before` itself).</summary>
    public static bool MorningPassed(Moment before, Moment after)
    {
        // The first morning after `before`: today's, if it is still to come, or else tomorrow's. (If `after` is not
        // later than `before`, it cannot have reached it.)
        long today = before.AbsoluteDay * Calendar.TicksPerDay + (long)MorningMinute * Calendar.TicksPerMinute;
        long next = before.Tick < today ? today : today + Calendar.TicksPerDay;
        return after.Tick >= next;
    }

    /// <summary>The slot (1 to 3) the morning of `now`'s day writes to.</summary>
    public static int SlotFor(Moment now) => (int)(now.AbsoluteDay % Slots) + 1;

    /// <summary>The file of a slot in `folder`.</summary>
    public static string PathOf(string folder, int slot) => Path.Combine(folder, "autosave-" + slot + ".json");
}
