// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>The speeds the player can choose (library Review 30): paused, or 1, 2 or 3 times normal.</summary>
public enum Speed
{
    Paused = 0,
    Normal = 1,
    Double = 2,
    Triple = 3,
}

/// <summary>
/// Turns real time into ticks. For now (step 21a) the screen keeps the pace of library Review 30: at normal speed
/// a day lasts 12 real minutes, 86,400 ticks in 720 seconds, so 120 ticks (two game minutes) a real second; double
/// speed is 240 and triple 360. Step 21b brings Review 39's speeds (real time at ×1, up to ×60).
///
/// This is the only place real time touches the colony, and it only says how many ticks to run. The simulation
/// itself never sees seconds or frames, so the same ticks always give the same colony, however fast or slow the
/// screen draws (FP-01: "advancement cannot depend on UI rendering timing"). The part of a tick not yet due is
/// carried over to the next frame and is not part of the colony's state.
/// </summary>
public sealed class Pacer
{
    /// <summary>Ticks a real second at normal speed.</summary>
    public const double TicksPerSecond = Calendar.TicksPerDay / (12.0 * 60.0);

    /// <summary>The most ticks one frame may ask for (an hour of game time: ten real seconds at triple speed). If
    /// the computer stalls, the game slows down instead of trying to catch up all at once.</summary>
    public const int MostTicksAFrame = 60 * Calendar.TicksPerMinute;

    double owed;
    public Speed Speed { get; set; } = Speed.Normal;

    /// <summary>How many ticks to run after `seconds` of real time at the current speed.</summary>
    public int TicksFor(double seconds)
    {
        if (Speed == Speed.Paused || seconds <= 0)
            return 0;
        owed += seconds * TicksPerSecond * (int)Speed;
        // Frame times add up with tiny rounding errors (144 frames of 1/144 second come to 0.99999...), so a tick
        // within a billionth of being due counts as due.
        int whole = (int)Math.Min(Math.Floor(owed + 1e-9), MostTicksAFrame);
        owed -= whole;
        if (owed > MostTicksAFrame)
            owed = 0;      // what could not be run after a stall is dropped, not saved up
        return whole;
    }
}
