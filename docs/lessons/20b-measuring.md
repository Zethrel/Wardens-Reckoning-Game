# Lesson 20b: measuring the colony

FP-12, the last of the first-playable scenarios, is not a pass or a fail but
a measurement: how long the colony takes over each minute, how much memory it
uses, how long saving takes, and where the time goes. The library asks for
named scenarios, measured the same way every time, *before* anything is made
faster (Review 24, R24-08): guessing where a program is slow is usually wrong.

This step adds a small program, `tools/Profile`, that runs five scenarios and
measures them, and four counters on the ground's path-finding. The results
are in `docs/performance.md`.

## A program of its own

`tools/Profile/Profile.csproj` is a **console program**: it has a `Main`
method where it starts, and it prints its results. Like the tests, it uses
`src/Colony` and nothing of Godot:

```xml
<OutputType>Exe</OutputType>
...
<ProjectReference Include="../../src/Colony/Colony.csproj" />
```

Run it as a **Release** build: `dotnet run -c Release --project tools/Profile`.
A Debug build (the default) leaves out the compiler's optimisations so a
debugger can follow every line; it can be several times slower, and its
timings say little. The program writes "DEBUG BUILD: not comparable" in its
report if it is one. It knows which it is from `#if DEBUG`, a line the
compiler keeps or drops depending on the build:

```csharp
#if DEBUG
    const bool IsRelease = false;
#else
    const bool IsRelease = true;
#endif
```

## Timing a tick

```csharp
long started = Stopwatch.GetTimestamp();
colony.Run(1, command);
times[t] = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
```

`Stopwatch.GetTimestamp()` reads the computer's finest clock, in its own
units; `Stopwatch.Frequency` is how many of them make a second. So the
difference, times 1000, divided by the frequency, is milliseconds.

The **mean** of the tick times hides the ticks that matter: one tick in a
thousand taking two seconds is a freeze the player sees, yet barely moves the
mean. So the report gives the **median** (half the ticks are faster), the
**95th** and **99th percentile** (all but one in twenty, or one in a hundred,
are faster) and the **slowest**. To find a percentile, the times are sorted
and the one at that place is taken:

```csharp
var measured = times.Skip(WarmUp).OrderBy(x => x).ToArray();
double Pct(double p) => measured[Math.Min(measured.Length - 1, (int)Math.Ceiling(p * measured.Length) - 1)];
```

The first hour (`WarmUp`, 60 ticks) is left out. .NET compiles each method
to machine code the first time it runs (**JIT**, just in time), so the first
minutes are slower for a reason that has nothing to do with the colony.

How fast must a tick be? At ×1 the game runs 2 ticks a second, at ×3 six: a
tick has 500 ms at ×1 and about 167 ms at ×3, and it must leave time for
drawing the screen too.

## Counting without changing anything

The ground now counts its path-finding (`Ground.cs`): `WaysAsked`,
`WaysWorked`, `WayTime`, and `WaysKept`. They are written only by the ground
and read only by the profiler. Nothing in the colony reads them and they are
not saved, so a measured colony plays out exactly as an unmeasured one. That
matters: the library requires that measuring never changes the simulation,
and `MeasureTests` checks it.

`{ get; private set; }` is a property anyone may read but only its own class
may change.

## What it found

See `docs/performance.md`. In short: everyday play is fast (a tenth of a
millisecond a tick), but path-finding is the hotspot. The ground works out a
way to a tile by spreading out over the *whole* map (65,536 tiles on a local
map), keeps it (half a megabyte each), and forgets *all* of them whenever
anything that blocks is built. With 300 walls planned, every finished wall
makes the colonists work out their ways to the other 299 again: freezes of
seconds and several hundred megabytes. Making it faster is a choice with
trade-offs, for the next step.

## Exercise

1. Run `dotnet run -c Release --project tools/Profile -- idle hauling`. Then
   run it again without `-c Release`. How much slower is the Debug build?
2. In `Program.cs`, change `WarmUp` to 0 and run `idle` again. What happens
   to the slowest tick, and why?
3. In the arbitration scenario, change every `planned < 300` to `planned < 30`. How
   do "Worked out" and "Slowest" change? What does that say about how the
   cost grows with the number of planned pieces?
