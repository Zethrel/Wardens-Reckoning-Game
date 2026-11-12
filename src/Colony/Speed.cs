// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// The speeds the player can choose (library Review 41): paused, or 1, 2, 3 or 5 times the normal speed. The
/// number of each is what it multiplies: at ×1 a real second runs 24 ticks (`Calendar.TicksPerRealSecond`), at ×5
/// 120.
/// </summary>
public enum Speed
{
    Paused = 0,
    X1 = 1,
    X2 = 2,
    X3 = 3,
    X5 = 5,
}

/// <summary>
/// Turns real time into ticks. A tick is a game second, and at ×1 a real second runs 24 of them (library Review 41,
/// the hybrid clock): a day takes an hour at ×1, and 12 minutes at ×5.
///
/// This is the only place real time touches the colony, and it only says how many ticks to run. The simulation
/// itself never sees real seconds or frames, so the same ticks always give the same colony, however fast or slow
/// the screen draws (FP-01: "advancement cannot depend on UI rendering timing"). The part of a tick not yet due is
/// carried over to the next frame and is not part of the colony's state; the screen uses it to draw walkers
/// between two ticks (`Due`).
/// </summary>
public sealed class Pacer
{
    /// <summary>The speeds in order, for the screen's buttons and keys 1 to 4.</summary>
    public static readonly Speed[] Speeds = { Speed.X1, Speed.X2, Speed.X3, Speed.X5 };

    /// <summary>The most ticks one frame may ask for: a real second at ×5 (Review 41: catch-up is bounded). If the
    /// computer stalls, or cannot keep up, the game slows down instead of trying to catch up all at once: no tick is
    /// skipped, there are only fewer of them.</summary>
    public const int MostTicksAFrame = 120;

    double owed;
    public Speed Speed { get; set; } = Speed.X1;

    /// <summary>How far the next tick is due, from 0 (just run) to just under 1 (about to run).</summary>
    public double Due => Speed == Speed.Paused ? 0 : Math.Clamp(owed, 0, 1);

    /// <summary>How many ticks to run after `seconds` of real time at the current speed.</summary>
    public int TicksFor(double seconds)
    {
        if (Speed == Speed.Paused || seconds <= 0)
            return 0;
        owed += seconds * Calendar.TicksPerRealSecond * (int)Speed;
        // Frame times add up with tiny rounding errors (144 frames of 1/144 second come to 0.99999...), so a tick
        // within a billionth of being due counts as due.
        int whole = (int)Math.Min(Math.Floor(owed + 1e-9), MostTicksAFrame);
        owed -= whole;
        if (owed >= 1)
            owed = 0;      // what could not be run after a stall is dropped, not saved up
        return whole;
    }
}
