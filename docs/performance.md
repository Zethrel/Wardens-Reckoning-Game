# The first playable: performance (FP-12)

The design library's FP-12 asks for named scenarios, repeated with tick time,
peak memory, path-finding work and save duration measured; their conditions,
variance and bottlenecks reported; and an assessment of whether a bottleneck
blocks the intended experience. It sets no targets: budgets come later, from
measurements (Review 24, R24-08). This is that report.

## How to measure

```powershell
# PowerShell, in D:\Zethrel-Github-Projects\Wardens-Reckoning-game
dotnet run -c Release --project tools/Profile -- --out profile-own.md
dotnet run -c Release --project tools/Profile -- --site <a local map>.json --out profile-site.md
```

The scenarios (`tools/Profile/Program.cs` says exactly what each does; the
seed is 20, the start 1 April of year 1 at 08:00):

| Scenario | What happens | Days |
|---|---|---|
| idle | The three founders, nothing ordered | 3 |
| hauling | Twelve colonists; four querns 25 to 60 m from the carts, 50 batches of grinding each: all the wheat carried out, all the flour carried back | 2 |
| arbitration | Twelve colonists and 300 wall pieces planned at once in rings around the camp: many jobs to choose among every minute | 2 |
| save | The arbitration colony after a day, made into text, written and loaded twenty times | — |
| interrupted | The hauling colony, with everyone carrying stopped every quarter hour and set going five minutes later, and a haul cancelled every hour | 2 |

Tick times leave out the first in-game hour (the code is still being
compiled then). A tick is one in-game minute; the game runs 2 ticks a second
at ×1 and 6 at ×3, so a tick has 500 ms at ×1 and about 167 ms at ×3, less
the time for drawing.

Measuring changes nothing in the colony (`MeasureTests`).

## Results

### Cloud, 10 November 2026

Ubuntu 24.04, 2 logical processors, .NET 8.0.33, Release build. Each map was
run two or three times; the figures below are the last full run, with nothing else
running. The counts (ways asked and worked out, walls built) were the same in
every run. Most times agreed within about 20% between the clean runs; the
99th percentile of hauling (0.5 to 1.1 ms) and the slowest tick of
arbitration (2.6 to 2.7 s on the own map) varied more, and so did the peak
memory of arbitration (350 to 576 MB: it is sampled, and counts garbage not
yet collected). Tick times in milliseconds.

**The profiler's own map** (256 × 256, a brook, trees and rocks):

| Scenario | Mean | Median | 95% | 99% | Slowest | Ways asked | Worked out | Their ms | Most kept | Peak MB |
|---|---|---|---|---|---|---|---|---|---|---|
| idle | 0.079 | 0.021 | 0.035 | 0.067 | 165 | 804 | 4 | 224 | 5 | 31 |
| hauling | 0.301 | 0.168 | 0.585 | 1.13 | 140 | 5,448 | 8 | 415 | 9 | 121 |
| arbitration | 14.9 | 2.34 | 7.14 | 98.7 | 2,636 | 329,226 | 1,852 | 33,809 | 302 | 570 |
| interrupted | 0.071 | 0.034 | 0.115 | 0.167 | 50 | 5,638 | 8 | 140 | 9 | 120 |

**Site 01 of world 7** (256 × 256, monsoon forest, a stream):

| Scenario | Mean | Median | 95% | 99% | Slowest | Ways asked | Worked out | Their ms | Most kept | Peak MB |
|---|---|---|---|---|---|---|---|---|---|---|
| idle | 0.076 | 0.022 | 0.040 | 0.068 | 157 | 504 | 4 | 206 | 5 | 30 |
| hauling | 0.308 | 0.178 | 0.692 | 0.853 | 126 | 6,883 | 7 | 322 | 8 | 124 |
| arbitration | 4.13 | 2.17 | 6.79 | 66.9 | 230 | 261,690 | 246 | 3,955 | 302 | 398 |
| interrupted | 0.066 | 0.033 | 0.115 | 0.181 | 44 | 6,896 | 7 | 107 | 8 | 124 |

Arbitration built 171 walls of the 300 in two days on the own map, and 174
on site 01.

**Saving** (the arbitration colony after a day; mean of 19 / slowest of 20):

| Map | Size | Text | Written | Loaded |
|---|---|---|---|---|
| Own | 459 KB | 17 / 43 ms | 49 / 86 ms | 30 / 67 ms |
| Site 01 | 459 KB | 19 / 59 ms | 55 / 112 ms | 33 / 53 ms |

**Garbage collections** (generation 0/1/2): none in idle, hauling and
interrupted; 47/8/5 in arbitration on the own map, 13/1/0 on site 01. The
process's peak working set was about 890 MB in a run of all five.

### The maintainer's PC

To be measured.

## Assessment

**Everyday play is far within its time.** A colony at rest or hauling takes a
tenth to a third of a millisecond a minute on average, and saving takes under
a tenth of a second.

**The bottleneck is path-finding, and it blocks building at any scale.** The
ground works out a way to a tile by spreading out over the whole map (65,536
tiles) with a sorted set: about 15 to 60 ms each on average on this machine. It keeps
each way (65,536 numbers, 512 KB) and forgets them all whenever something
that blocks is built. The colonists ask for a way to every job they weigh up,
so with 300 pieces planned:

- every finished wall makes them work out ways to the rest again: 1,852 ways
  in two days on the own map, 34 of the 43 seconds the scenario took;
- the slowest minute took **2.6 s** (over five ticks' time at ×1, and more
  than fifteen at ×3): the game would visibly freeze, again and again, while
  a large plan is built;
- the ways kept reach **302 × 512 KB ≈ 150 MB**, and the managed memory
  570 MB.

Even with nothing planned, a minute that has to work out a way can take
160 ms (idle's slowest tick): close to a tick's whole time at ×3.

**Not explained yet:** on site 01 the colonists worked out far fewer ways
(246 against 1,852) for as many walls built. The cost depends on more than
the number of pieces; to be looked into with the fix.

**Not measured yet:** colonies larger than twelve; maps larger than 256; the
screen's own drawing time (Godot); the maintainer's PC.

The fix is a decision for the maintainer (the next step); this step changes
nothing in how ways are found.
