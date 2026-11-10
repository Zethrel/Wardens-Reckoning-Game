// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// The speeds the player can choose (library Review 39): paused, or 1, 3, 10, 30 or 60 times real time. The
/// number of each is how many game seconds pass in a real second.
/// </summary>
public enum Speed
{
    Paused = 0,
    X1 = 1,
    X3 = 3,
    X10 = 10,
    X30 = 30,
    X60 = 60,
}

/// <summary>
/// Turns real time into ticks. A tick is a game second, and at ×1 a game second lasts a real second (library Review
/// 39): a day takes 24 real hours at ×1, and 24 real minutes at ×60.
///
/// This is the only place real time touches the colony, and it only says how many ticks to run. The simulation
/// itself never sees real seconds or frames, so the same ticks always give the same colony, however fast or slow
/// the screen draws (FP-01: "advancement cannot depend on UI rendering timing"). The part of a tick not yet due is
/// carried over to the next frame and is not part of the colony's state; the screen uses it to draw walkers
/// between two ticks (`Due`).
/// </summary>
public sealed class Pacer
{
    /// <summary>The speeds in order, for the screen's buttons and keys 1 to 5.</summary>
    public static readonly Speed[] Speeds = { Speed.X1, Speed.X3, Speed.X10, Speed.X30, Speed.X60 };

    /// <summary>Ticks a real second at ×1: one.</summary>
    public const double TicksPerSecond = 1;

    /// <summary>The most ticks one frame may ask for: ten real seconds at ×60. If the computer stalls, the game
    /// slows down instead of trying to catch up all at once.</summary>
    public const int MostTicksAFrame = 600;

    double owed;
    public Speed Speed { get; set; } = Speed.X1;

    /// <summary>How far the next tick is due, from 0 (just run) to just under 1 (about to run).</summary>
    public double Due => Speed == Speed.Paused ? 0 : Math.Clamp(owed, 0, 1);

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
        if (owed >= 1)
            owed = 0;      // what could not be run after a stall is dropped, not saved up
        return whole;
    }
}
