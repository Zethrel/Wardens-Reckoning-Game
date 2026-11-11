# Lesson 21d: measured again, on the second tick

Steps 21a to 21c changed what a tick is (a game second instead of a game
minute) and when colonists decide (at their own think, every 2 to 7 seconds).
Evidence gathered on the old clock says little about the new one, so this
step runs the first-playable scenarios (FP-01 to FP-10) and the performance
scenarios (FP-12) again. The results are in `docs/acceptance.md` and
`docs/performance.md`.

There is little new code; the lesson is about two small changes and how to
read the new numbers.

## A save at any second

FP-08 asks that a colony saved and loaded goes on exactly as if it had never
been saved. On the minute clock, saving once every two hours, on the half
hour, was a fair sample. On the second clock a player can save at any second:
mid-stride, mid-load, a second before someone thinks. So the local-map test
now saves every 7,193 seconds:

```csharp
if ((c.Now.Tick - Start.Tick) % 7193 != 0)
    return;
```

`%` is the remainder again (lesson 21c). Why 7,193? Two hours are 7,200
seconds; 7,193 is 7 less, which is the same as 53 more than a whole number of
minutes (7,140 = 119 × 60). So each save falls 53 seconds further on in its
minute than the last: second 53, then 46, then 39 and so on, round and round.
Over 48 saves that reaches more than 40 different seconds, which the test
checks:

```csharp
var seconds = new HashSet<int>();
...
seconds.Add(c.Now.Second);
...
Assert.True(seconds.Count >= 40, "saved on only " + seconds.Count + " different seconds of the minute");
```

A `HashSet` keeps each value once, so its `Count` is the number of different
seconds seen (lesson 21c used one for the inventories a finished job touched).

## The profiler's new column

Before step 21c, everything but walking happened on the whole minutes, so the
profiler's "Slowest" (the slowest minute tick) was the slowest tick there
was. Since colonists decide between the minutes, a tick in between can be
slow too. The profiler now also reports the slowest of those:

```csharp
double slowestBetween = times.Where((_, t) => t >= WarmUp && (t + 1) % Calendar.TicksPerMinute != 0).Max();
```

`times` holds every tick's time. `Where((_, t) => ...)` keeps those whose
position `t` passes the test: `_` is the time itself, which the test does not
need (C# lets you name a value you do not use `_`). `t >= WarmUp` leaves out
the first game hour, while the code is still being compiled; `(t + 1) %
Calendar.TicksPerMinute != 0` leaves out the minute ticks (tick `t` ends at
second `t + 1`). `Max()` gives the largest left.

## Reading the numbers: time per tick and speed

A tick has less real time the faster the game runs:

| Speed | Ticks a real second | Time for one tick |
|---|---|---|
| ×1 | 1 | 1,000 ms |
| ×60 | 60 | 16.7 ms |

Two different questions follow from that:

- **On average, does the colony keep up?** Add up a game minute: one minute
  tick and 59 second ticks. Idle is about 0.3 ms a game minute, hauling about
  1 ms. At ×60 a game minute passes in a real second, so that is 0.1% of the
  machine's time: no trouble.
- **Does any single tick take too long?** At ×60 the screen gets one tick a
  frame. Idle's slowest tick (35 to 40 ms, when a way is worked out) makes two
  frames late: a small stutter. Arbitration's slowest on the profiler's own
  map (1.5 to 1.7 s) freezes the screen at every speed. That is what step 22
  takes on.

An average can hide the second problem completely: arbitration's ticks
between the minutes average 0.38 ms, yet the slowest of them took 1,658 ms.
That is why the profiler reports the median, the 99th percentile and the
slowest as well as the mean.

## The same scenario, another outcome

On the own map, arbitration (300 walls) asked for 1.6 million ways this time,
against 329,226 on the minute clock. It is still the same scenario, from the
same seed, and it plays out the same every run. But which walls go up first
now depends on the seconds, and in this run more colonists got walled in. A
walled-in colonist makes every project check, every time, that someone can
still reach it, and each check is a search over the whole map.

While working on step 21c, the same scenario was run with loading set to 4
and to 6 seconds: 1.07 million and 97,000 ways asked. A measurement like this
holds for its exact conditions; a small change elsewhere can swing it. Step
22 should be measured on several such variations, not only one.

## Exercise

1. Run the scenario tests: `dotnet test tests --filter "FullyQualifiedName~.FP"`.
   How many cases pass? Compare with the table in `docs/acceptance.md`.
2. In `AcceptanceTests`, change 7193 to 7200. Run the FP-08 test
   (`--filter "FullyQualifiedName~.FP08_a_colony_on_a_local_map"`). Which
   check fails, and why? Put it back.
3. Run the profiler on idle only:
   `dotnet run -c Release --project tools/Profile -- idle`. Work out what a
   game minute costs on your PC (the minute mean plus 59 times the second
   mean). How much of a real second is that at ×60?
