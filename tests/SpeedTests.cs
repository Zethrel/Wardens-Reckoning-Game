// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// The speed the player picks changes only how quickly ticks are run, never what they do (the maintainer,
/// 12-11-2026; library Review 41): the same colony run for the same number of ticks ends the same at every speed,
/// however the frames fall. On the content as shipped, at the hybrid clock's pace.
/// </summary>
public class SpeedTests
{
    static readonly Moment Start = new(Calendar.TickOf(1, 1, 1, 9, 0));

    // A founded colony with work to do (walls and floors planned), run through a session to tick `ticks` at `speed`,
    // with frames of uneven lengths drawn from `frames` (a seeded random, as a real screen's frames are uneven); the
    // last ticks one at a time, so every speed stops at exactly the same tick. Its save, as text.
    static string RunAt(Speed speed, long ticks, int frames)
    {
        var colony = Founding.Found(7, Start, Content.Read(TestContent.RealFiles()), SanitationTests.Map(40, 30)).Colony;
        for (int x = 15; x < 25; x++)
            colony.Work!.Plan(x % 3 == 0 ? "floor" : "wall", x, 12);
        var session = new Session(colony, Path.Combine(Scratch.Root, "speed-" + speed + "-" + frames));
        session.Speed = speed;
        var dice = new Random(frames);
        long end = colony.Now.Tick + ticks;
        while (colony.Now.Tick < end - Pacer.MostTicksAFrame)
            session.Frame(dice.Next(4) == 0 ? dice.NextDouble() * 3 : dice.NextDouble() / 30);   // mostly 0 to 33 ms; now and then up to 3 s (a stall)
        session.Speed = Speed.X1;
        while (colony.Now.Tick < end)
            session.Frame(1.0 / Calendar.TicksPerRealSecond);       // the rest one tick at a time
        Assert.Equal(end, colony.Now.Tick);
        return ColonySave.Text(colony);
    }

    [Fact]
    public void The_same_ticks_end_the_same_at_every_speed()
    {
        long day = Calendar.TicksPerDay;
        string atOne = RunAt(Speed.X1, day / 4, 1);
        foreach (var speed in Pacer.Speeds)
            Assert.Equal(atOne, RunAt(speed, day / 4, (int)speed + 1));
    }
}
