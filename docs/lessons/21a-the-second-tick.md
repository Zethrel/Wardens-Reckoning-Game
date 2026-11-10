# Lesson 21a: the second tick

Until now one tick was one game minute, and a colonist crossed up to 70 m
between two ticks. Library Review 39 (after the clock trial) made the tick a
game **second**. This step changes the colony to it; the screen follows in
21b.

## What a tick is now

`Calendar.cs` holds the one place that says so:

```csharp
public const int TicksPerMinute = SecondsPerMinute;          // 60
public const int TicksPerHour = TicksPerMinute * MinutesPerHour;   // 3,600
public const int TicksPerDay = TicksPerHour * HoursPerDay;         // 86,400
```

A `const` is a value fixed when the program is compiled. Because every other
file says `Calendar.TicksPerDay` and never `1440`, most of the colony changed
unit by changing these lines. The few places that had written "an hour is 60
ticks" by hand (an illness's onset, the morning autosave) were found by the
tests and now say `Calendar.TicksPerHour`.

`Moment` gained `Second`, `SecondOfDay`, `Clock` ("06:30:15"), `WholeMinutes`
and `OnTheMinute`. `TickOf` takes an optional `second`.

## Two rhythms in one tick

```csharp
public void Tick()
{
    foreach (var worker in workers.Values)
        if (worker.Heading != null)
            Stride(worker);
    if (colony.Now.OnTheMinute)
        Minute();
}
```

- **Every second:** everyone who is heading somewhere takes a second's steps
  (`Stride`).
- **On the whole minutes:** everything else (`Minute`): needs, jobs, water,
  building, fouling, illness. This is the old tick, unchanged. Review 39 lets
  a system built on the minute tick keep running once a minute until a
  measurement says it needs more.

## Heading

Before, `Walk` moved a colonist a whole minute's way at once. Now it only
decides:

```csharp
bool Walk(Worker worker, double x, double y, bool near = false)
{
    if (Arrived(worker, x, y, near))
    {
        worker.Heading = null;
        return true;
    }
    worker.Heading = (x, y, near);
    return false;
}
```

`Heading` is a **nullable tuple**: `(double X, double Y, bool Near)?`. Either
three values together, or `null` for "standing". `worker.Heading is var (hx,
hy, near)` both checks that it is not null and takes the three values out.

On each whole minute, `Minute` first clears every heading, and whatever a
colonist does that minute sets it again if they should walk on. So a walker
whose job is taken away stands still from the next whole minute. An order
counts at once: `Stop` clears the heading that very second.

`Stride` is the old walking loop with a budget of a sixtieth of a minute: from
tile to tile on the way the ground finds, each stretch at its tile's speed.

## The same minutes as before

Jobs finish in the same minute they did on the minute tick: the test "a batch
takes its walks, loading and grinding" still expects 185 minutes. What changed
is *where a colonist is* between: they set off on the whole minute after they
decide, walk second by second, and are seen to have arrived on the next whole
minute. Before, they jumped a minute's way at once and waited out the rest of
the minute there. Only choices that depend on where people stand at a given
minute (who is nearest a new job) can come out differently.

**What this leaves:** at real time (×1, in 21b), a colonist may stand for up to
a minute before setting off, and up to a minute after arriving, because
deciding is still done once a minute. If that looks sluggish on screen, the
deciding can be moved to every second, system by system.

## Saves: version 13

Every tick in a save is now a second, and each colonist's `heading` is saved,
so a save made between two minutes goes on exactly as if it had not been made
(a test saves every 97 seconds and checks). Saves of version 12 are refused.

## Tests in minutes

Most tests think in minutes, so `Colony.RunMinutes(n)` runs `n × 60` ticks;
`Run(n)` runs ticks. Of 798 tests, 33 failed at first and were changed: the calendar's and the
pacer's own numbers, and a few that looked at where someone stood after
exactly one minute.

## Exercise

1. In `tests/TickTests.cs`, read `Between_the_minutes_only_the_walking_happens`.
   Change the loop to run to `second <= 60` and run the test. Which assertion
   fails, and why?
2. In `Work.Tick`, change `if (colony.Now.OnTheMinute)` to `if (true)` and run
   `dotnet test tests --filter WorkTests`. How long does a batch of flour take
   now? Why? Put it back.
3. In `Stride`, change the budget to `1.0 / 30`. Which tests notice?
