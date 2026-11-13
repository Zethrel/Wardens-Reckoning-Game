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
compiled then). The results up to step 20c were measured on the minute tick:
a tick was one in-game minute. Since step 21a a tick is one in-game second
(library Review 39). Since step 24a (Review 41, the hybrid clock) a real
second at ×1 runs 24 of them:

| Speed | Ticks a real second | Time for one tick | A day takes |
|---|---|---|---|
| ×1 | 24 | 41.7 ms | 60 min |
| ×2 | 48 | 20.8 ms | 30 min |
| ×3 | 72 | 13.9 ms | 20 min |
| ×5 | 120 | 8.3 ms | 12 min |

At 60 frames a second, ×5 asks for two ticks a frame. Results before step 24
were measured at the old speeds (×1 to ×60, one tick a real second at ×1) and
on the old pace: they are kept for comparison below, but the ×5 table of step
24b is the one Review 41's thresholds are about.

On the second tick the profiler shows two kinds of tick: the **minute ticks**
(every sixtieth: the colony's own work, needs, asking, supplying) and the
**second ticks** between them (walking and the colonists who are processed
that tick). "Slowest second" is the slowest of those.

Since step 24b a second table replays each scenario's tick times through the
game's own pacer at ×5, frame by frame at 60 frames a second (a frame lasts
1/60 s, or as long as its ticks take): the **sustained speed** (ticks run
over ticks asked for), the **simulation's share of a core** (its time over the
real time) and the **slowest frame**, beside Review 41's provisional
thresholds. Drawing is not counted (the screen does not run in the profiler).
Which scenarios count as everyday and which as stress: idle and hauling are
everyday; arbitration (300 walls) and interrupted are stress.

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

### The maintainer's PC, 10 November 2026

Windows 10.0.26200, 16 logical processors, .NET 8.0.33, Release build; the
same commit (step 20b). Every count (ways asked and worked out, walls built)
is the same as in the cloud, to the unit: the colony plays out the same on
Windows and Linux.

**The profiler's own map:**

| Scenario | Mean | Median | 95% | 99% | Slowest | Worked out | Their ms | Peak MB |
|---|---|---|---|---|---|---|---|---|
| idle | 0.045 | 0.011 | 0.015 | 0.034 | 101 | 4 | 134 | 31 |
| hauling | 0.145 | 0.072 | 0.265 | 0.375 | 104 | 8 | 273 | 56 |
| arbitration | 7.80 | 0.961 | 4.16 | 48.2 | 1,454 | 1,852 | 17,883 | 565 |
| interrupted | 0.037 | 0.017 | 0.063 | 0.089 | 31 | 8 | 78 | 123 |

**Site 01:**

| Scenario | Mean | Median | 95% | 99% | Slowest | Worked out | Their ms | Peak MB |
|---|---|---|---|---|---|---|---|---|
| idle | 0.041 | 0.011 | 0.015 | 0.032 | 87 | 4 | 116 | 30 |
| hauling | 0.131 | 0.066 | 0.206 | 0.280 | 89 | 7 | 203 | 55 |
| arbitration | 1.93 | 0.954 | 3.69 | 33.7 | 103 | 246 | 1,903 | 325 |
| interrupted | 0.033 | 0.016 | 0.049 | 0.070 | 22 | 7 | 52 | 62 |

**Saving:** 479 KB (20 KB more than in the cloud, most likely because Windows
ends each line with two characters and Linux with one; not checked), text 9.7 / 30 ms, written 31 / 56 ms, loaded 20 / 32 ms on the own
map; 8.5 / 25, 29 / 48 and 19 / 37 ms on site 01.

The PC is about twice as fast as the cloud machine throughout; the
bottleneck is the same.

### After step 20c, cloud, 10 November 2026

The faster search (the same ways, worked out about twice as fast) and a
ceiling of 512 ways kept. The same machine as above; the counts are
unchanged, as they must be.

| Scenario | Map | Mean | 99% | Slowest | Their ms | Before: mean / slowest / their ms |
|---|---|---|---|---|---|---|
| idle | own | 0.050 | 0.069 | 70 | 93 | 0.079 / 165 / 224 |
| hauling | own | 0.249 | 0.677 | 67 | 183 | 0.301 / 140 / 415 |
| arbitration | own | 7.61 | 41.4 | 1,189 | 14,517 | 14.9 / 2,636 / 33,809 |
| interrupted | own | 0.053 | 0.136 | 23 | 62 | 0.071 / 50 / 140 |
| idle | site 01 | 0.060 | 0.135 | 66 | 86 | 0.076 / 157 / 206 |
| hauling | site 01 | 0.278 | 0.758 | 67 | 142 | 0.308 / 126 / 322 |
| arbitration | site 01 | 3.02 | 35.4 | 125 | 1,840 | 4.13 / 230 / 3,955 |
| interrupted | site 01 | 0.052 | 0.130 | 22 | 48 | 0.066 / 44 / 107 |

Peak memory is unchanged (arbitration about 600 MB on the own map): the
ceiling is above the 302 ways that scenario keeps. A ceiling below the goals
in play makes the colonists work the same ways out again and again; measured
on the own map's arbitration:

| Most ways kept | Worked out | Mean ms | Peak MB |
|---|---|---|---|
| 64 | 37,346 | 71.4 | 260 |
| 128 | 32,442 | 56.6 | 257 |
| 256 | 6,680 | 7.8 | 718 |
| 320 | 1,852 | 8.0 | 607 |
| no ceiling | 1,852 | 8.6 | 607 |

### On the second tick (step 21d), cloud, 11 November 2026

The same machine as the cloud runs above (Ubuntu 24.04, 2 logical processors,
.NET 8.0.33, Release build), commit 08ad82a (step 21c) with the profiler's
"Slowest second" column. Tick times in milliseconds; a minute tick's figures
are over the minute ticks only, "Second mean" and "Slowest second" over the
ticks between them.

**The profiler's own map:**

| Scenario | Ticks | Minute mean | 99% | Slowest | Second mean | Slowest second | Ways asked | Worked out | Their ms | Peak MB |
|---|---|---|---|---|---|---|---|---|---|---|
| idle | 259,200 | 0.039 | 0.116 | 40 | 0.0050 | 38 | 47,624 | 4 | 141 | 133 |
| hauling | 172,800 | 0.051 | 0.117 | 9 | 0.0159 | 14 | 85,553 | 8 | 229 | 135 |
| arbitration | 172,800 | 2.68 | 6.76 | 1,465 | 0.381 | 1,658 | 1,632,441 | 4,061 | 43,650 | 504 |
| interrupted | 172,800 | 0.047 | 0.140 | 9 | 0.0153 | 14 | 85,565 | 8 | 81 | 142 |

**Site 01 of world 7:**

| Scenario | Ticks | Minute mean | 99% | Slowest | Second mean | Slowest second | Ways asked | Worked out | Their ms | Peak MB |
|---|---|---|---|---|---|---|---|---|---|---|
| idle | 259,200 | 0.034 | 0.093 | 35 | 0.0048 | 35 | 47,139 | 4 | 124 | 134 |
| hauling | 172,800 | 0.062 | 0.158 | 28 | 0.0146 | 31 | 89,221 | 7 | 177 | 133 |
| arbitration | 172,800 | 2.11 | 7.07 | 37 | 0.106 | 91 | 56,409 | 696 | 6,856 | 608 |
| interrupted | 172,800 | 0.046 | 0.125 | 9 | 0.0133 | 9 | 89,233 | 7 | 59 | 139 |

Arbitration built 186 walls of the 300 in two days on the own map, and 196
on site 01.

**Saving** (the arbitration colony after a day; mean of 19 / slowest of 20):
450 KB, text 21 / 67 ms, written 58 / 158 ms, loaded 35 / 81 ms on the own
map; 443 KB, 22 / 55, 59 / 107 and 38 / 57 ms on site 01.

**Garbage collections** (generation 0/1/2): 2/0/0 in idle, 9 to 10/0/0 in
hauling and interrupted; 56/28/28 in arbitration on the own map, 12/1/0 on
site 01. Peak working set of a whole run: about 890 MB.

**The maintainer's PC, 11 November 2026** (Windows 10.0.26200, 16 logical
processors, .NET 8.0.33, Release build; the same commit, step 21d). Every
count (ways asked and worked out, walls built) is the same as in the cloud,
to the unit.

| Scenario | Map | Minute mean | 99% | Slowest | Second mean | Slowest second | Their ms | Peak MB |
|---|---|---|---|---|---|---|---|---|
| idle | own | 0.018 | 0.050 | 16 | 0.0026 | 17 | 63 | 55 |
| hauling | own | 0.027 | 0.076 | 16 | 0.0071 | 14 | 119 | 57 |
| arbitration | own | 1.52 | 3.57 | 741 | 0.209 | 847 | 23,912 | 454 |
| interrupted | own | 0.025 | 0.070 | 7 | 0.0070 | 7 | 49 | 60 |
| idle | site 01 | 0.019 | 0.060 | 16 | 0.0028 | 14 | 56 | 55 |
| hauling | site 01 | 0.028 | 0.070 | 17 | 0.0068 | 16 | 113 | 57 |
| arbitration | site 01 | 1.04 | 3.40 | 18 | 0.0542 | 43 | 3,866 | 550 |
| interrupted | site 01 | 0.026 | 0.068 | 6 | 0.0070 | 7 | 41 | 63 |

Saving on the PC: 469 KB, text 11 / 32 ms, written 38 / 67 ms, loaded
20 / 32 ms on the own map; 462 KB, 11 / 34, 34 / 57 and 18 / 29 ms on site
01. Peak working set of a whole run: 787 MB.

As on the minute tick, the PC is about twice as fast as the cloud machine.
On the PC the 300-wall stress test on the own map still has single ticks of
0.74 to 0.85 s; at ×60 even idle's slowest (16 to 17 ms) is about one
frame's time.

**What a game minute costs now:** the minute tick plus 59 second ticks. At
rest about 0.3 ms (idle), hauling about 1 ms, arbitration on site 01 about 8
ms and on the own map about 25 ms on average. At ×60 a game minute passes in a
real second, so on average even arbitration uses a few percent of the
machine.

**What changed against the minute tick (step 20c):**

- The **slow ticks** are where they were: arbitration on the own map has
  minute ticks of 1.5 s and, new, ticks between the minutes of 1.7 s, once
  walls close colonists in on the second day (each project then checks that
  someone can still reach it, and each colonist thinking ranks the open jobs
  by their ways). Before step 21c that work was all on the minute (1.2 s
  slowest after 20c). On site 01 the slowest tick is 91 ms.
- **Ways asked** went up (hauling 85,553 against 5,448; arbitration on the
  own map 1.6 million against 329,226): colonists now decide every 2 to 7
  seconds instead of once a minute. Almost all are answered from the ways
  kept (hauling works out 8 ways in two days, as before).
- Which walls go up first now depends on the seconds, so arbitration on the
  own map went another way: 4,061 ways worked out against 1,852, because more
  of its colonists got walled in. Over a day of play this one scenario swings
  between about 100,000 and 1.6 million ways asked depending on the order the
  walls go up (measured while working on step 21c, with loading set to 4 and
  to 6 seconds).
- Everything else (idle, hauling, interrupted, saving) is as fast or faster:
  the minute ticks do less, because deciding moved off them.

### Step 22: areas, cloud, 11 November 2026

Measured first (with a counter on each caller, not kept): of the 43.6 s the
own map's arbitration spent working out ways, 42.4 s were the colonists
choosing jobs, and almost all of it for jobs they could not reach. A
colonist walled in weighs every open job; each job's way is worked out over
the whole map only to find there is none, and again after every finished
wall (each one makes the ground forget its ways). Checking that someone can
still reach a project cost nothing extra: the same ways.

Step 22 gives the ground **areas**: one fill marks which open tiles connect.
"Is there any way at all?" is then answered from the areas, without a search,
and only a way that exists is worked out. The areas are worked out again only
when a tile becomes blocked or open (about 2 ms on a 256 × 256 map in the cloud,
measured on site 01).
The answer is exactly the search's (`AreaTests` compares them on scattered
grounds, tile by tile), so the colony plays out the same: the same walls
built, the same fingerprints, every test unchanged.

| Scenario | Map | Ways asked | Worked out | Their ms | Minute mean | Slowest | Second mean | Slowest second |
|---|---|---|---|---|---|---|---|---|
| arbitration, before | own | 1,632,441 | 4,061 | 43,650 | 2.68 | 1,465 | 0.381 | 1,658 |
| arbitration, areas | own | 55,793 | 622 | 6,682 | 2.39 | 23 | 0.177 | 53 |
| arbitration, before | site 01 | 56,409 | 696 | 6,856 | 2.11 | 37 | 0.106 | 91 |
| arbitration, areas | site 01 | 56,409 | 696 | 7,092 | 2.02 | 41 | 0.104 | 88 |

(Ways asked counts only the ways looked up now: one with no way at all is
no longer asked for. On site 01 nobody was walled in, so nothing changed.)
Idle, hauling and interrupted are as before; saving too.

**Several variations** of the own map's arbitration, loading set to 4, 6 and
8 seconds (5 is the content's): before the areas the same scenario swung
between about 97,000 and 1.6 million ways asked depending on which walls went
up first; with them:

| Loading | Ways asked | Worked out | Their ms | Slowest | Slowest second | Walls built |
|---|---|---|---|---|---|---|
| 4 s | 56,450 | 729 | 6,937 | 28 | 65 | 192 |
| 5 s | 55,793 | 622 | 6,682 | 23 | 53 | 186 |
| 6 s | 56,105 | 603 | 6,205 | 23 | 50 | 192 |
| 8 s | 56,384 | 621 | 6,422 | 30 | 65 | 192 |

**What is left:** the slowest ticks (50 to 65 ms in the cloud, so about half
that on the maintainer's PC) are ticks where a few ways are worked out at
once, each over the whole map (about 10 ms each here). At ×60 that is two to
four frames late, now and then while a large plan is built; nothing freezes.
The two ideas measured against each other in the plan (jobs ranked by
straight-line distance first; one search outward from each colonist
choosing) would make those fewer or smaller; not built, as the areas took
away what blocked play (the maintainer's choice, 11 November).

### Step 24b: the hybrid clock, cloud, 12 November 2026

The content as shipped: walking 1.2 m a real second at ×1 (0.05 m a tick),
loading and unloading 120 ticks, deciding every 48 to 168 ticks, building at
two thirds of its former minutes (lesson 24b). The same scenarios and days,
so less gets done in them than before (walking costs game time now).

Measured 12 Nov 2026 09:16 on Ubuntu 24.04.5 LTS, 2 logical processors, .NET 8.0.33, Release build. Own map.

| Scenario | Ticks | Minute tick mean ms | Median | 95% | 99% | Slowest | Second tick mean ms | Slowest second | Ways asked | Worked out | Their ms | Most kept | Peak MB | GCs (0/1/2) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| idle | 259200 | 0.033 | 0.023 | 0.041 | 0.069 | 9.724 | 0.0024 | 26.0 | 17996 | 4 | 100.4 | 5 | 133 | 0/0/0 |
| hauling | 172800 | 0.031 | 0.026 | 0.047 | 0.062 | 3.394 | 0.0027 | 26.3 | 138352 | 8 | 205.6 | 9 | 138 | 1/0/0 |
| arbitration | 172800 | 5.486 | 5.594 | 11.6 | 13.5 | 20.4 | 0.0190 | 34.0 | 496446 | 198 | 1862.6 | 305 | 397 | 8/0/0 |
| interrupted | 172800 | 0.023 | 0.021 | 0.032 | 0.044 | 0.141 | 0.0014 | 8.461 | 152408 | 8 | 66.1 | 9 | 144 | 1/0/0 |

At ×5:

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0029 | 26.0 | 100.0% | 0.04% | 26.0 |
| hauling | everyday | 0.0031 | 26.3 | 100.0% | 0.04% | 26.3 |
| arbitration | stress | 0.1101 | 34.0 | 100.0% | 1.3% | 41.0 |
| interrupted | stress | 0.0018 | 8.461 | 100.0% | 0.02% | 8.464 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 552 | 22.4 / 65.9 | 58.5 / 117.0 | 40.1 / 81.4 |

Arbitration: 69 of the 300 walls built in its two days (186 on the old pace
with 45-minute walls; 26 in the hybrid-clock trial before the rebalance).

**Against Review 41's provisional thresholds (cloud):**

| Threshold | Measured | Within |
|---|---|---|
| Sustained speed 99% at ×5 | 100.0% in every scenario | yes |
| At most 25% of a core | 1.3% at most (arbitration) | yes |
| Slowest tick 33 ms, everyday | 26.3 ms (hauling) | yes |
| Slowest tick 100 ms, stress | 34.0 ms (arbitration) | yes |
| Saving under 1 s | 22 ms to text, 59 ms written (117 slowest) | yes |
| Loading under 1 s | 40 ms (81 slowest) | yes |

**The maintainer's PC, 12 November 2026** (Windows 10.0.26200, 16 logical
processors, .NET 8.0.33, Release build; own map):

| Scenario | Ticks | Minute tick mean ms | Median | 95% | 99% | Slowest | Second tick mean ms | Slowest second | Ways asked | Worked out | Their ms | Most kept | Peak MB | GCs (0/1/2) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| idle | 259200 | 0.010 | 0.009 | 0.016 | 0.027 | 0.606 | 0.0010 | 18.0 | 17996 | 4 | 61.0 | 5 | 56 | 2/0/0 |
| hauling | 172800 | 0.044 | 0.040 | 0.068 | 0.100 | 0.381 | 0.0034 | 14.5 | 138352 | 8 | 116.6 | 9 | 57 | 3/0/0 |
| arbitration | 172800 | 2.201 | 2.340 | 4.693 | 5.686 | 8.157 | 0.0087 | 18.2 | 496446 | 198 | 927.2 | 305 | 324 | 22/0/0 |
| interrupted | 172800 | 0.010 | 0.009 | 0.013 | 0.023 | 0.185 | 0.0007 | 5.381 | 152408 | 8 | 39.3 | 9 | 65 | 3/0/0 |

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0011 | 18.0 | 100.0% | 0.01% | 18.0 |
| hauling | everyday | 0.0041 | 14.5 | 100.0% | 0.05% | 14.5 |
| arbitration | stress | 0.0453 | 18.2 | 100.0% | 0.54% | 19.9 |
| interrupted | stress | 0.0009 | 5.381 | 100.0% | 0.01% | 5.383 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 576 | 10.4 / 26.5 | 36.4 / 57.6 | 21.1 / 44.4 |

Arbitration: 69 of the 300 walls built, as in the cloud; every scenario asked
for and worked out exactly as many ways as in the cloud: the same colony on
Windows and Linux. (The save file is 576 KB against 552 KB in the cloud,
probably the line endings Windows writes; not checked.) Every provisional
threshold is met on the PC too, with more room than in the cloud: the
slowest tick 18.2 ms, the share of a core 0.54% at most, saving and loading
under 60 ms.

Worth knowing, not over any threshold: the everyday scenarios' slowest
ticks (26 ms) are single ticks in which a colonist works out a long way; at
×5 one such tick makes one frame late (26 ms against 16.7). The arbitration
colony keeps up to 305 ways (about 150 MB of its 397 MB).

**Approved (the maintainer, 12 November 2026):** Review 41's thresholds as
they stand, now binding: at ×5, 99% of the speed sustained; at most 25% of a
core for the simulation; no tick slower than 33 ms in everyday play or 100 ms
in a stress scenario; saving and loading each under 1 s. A step that goes over
one is investigated and the result written here.

### Step 25a: free time, cloud, 12 November 2026

Free time (library Review 42) makes colonists walk to new spots every evening.
First measured: the idle scenario's slowest tick rose to 52 ms (over the
everyday threshold of 33 ms), and the test suite went from 2.5 to 15 minutes.
Three causes, each measured and fixed:

- Choosing looked at every tile within 60 m once per activity, with
  allocations: now one look at each tile sorts it into every kind of place,
  clipped to the ground.
- With nothing to do (in the dark, sky gazing waiting after an
  interruption), a colonist looked at every think: 1,845 looks in one game
  hour on the acceptance test's map. Now they look again after
  `look_again_minutes` (10).
- Each free-time walk worked a way out over the whole ground. Now a short
  way is worked out only so far (1.5 × `within_m` from its goal; 2 ×
  `stroll_m` for the next stop of a walk). Within that it is exactly the
  whole way's answer (`AreaTests`: every metre and every step compared);
  beyond it the whole way is worked out after all.

Measured 12 Nov 2026 on Ubuntu 24.04.5 LTS, 2 logical processors, .NET 8.0.33, Release build. Own map.

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0052 | 34.8 | 100.0% | 0.06% | 34.8 |
| hauling | everyday | 0.0082 | 14.3 | 100.0% | 0.10% | 14.3 |
| arbitration | stress | 0.1338 | 33.2 | 100.0% | 1.6% | 60.2 |
| interrupted | stress | 0.0072 | 13.1 | 100.0% | 0.09% | 13.1 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 558 | 20.7 / 73.1 | 61.6 / 120.3 | 41.3 / 83.0 |

Ways worked out in idle: 103 (4 before free time), in 332 ms all told (1,310
ms before the short ways). **Over a threshold, in the cloud:** idle's slowest
tick, 34.8 ms against 33 ms; two more runs of idle alone gave 29.8 and 30.3
ms. The slowest ticks fall on the first evening, when the way-finding code
has run only a few times before (a likely cause, not verified: .NET compiles
code into its faster form after it has run a few dozen times). The binding
check is the maintainer's PC, where step 24 measured about twice the cloud's
speed: run there next. The test suite takes 2 minutes 56 seconds.

**The maintainer's PC, 12 November 2026** (Windows 10.0.26200, 16 logical
processors, .NET 8.0.33, Release build; own map): **every threshold met.**

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0031 | 16.9 | 100.0% | 0.04% | 16.9 |
| hauling | everyday | 0.0068 | 20.5 | 100.0% | 0.08% | 20.5 |
| arbitration | stress | 0.0557 | 19.5 | 100.0% | 0.67% | 19.5 |
| interrupted | stress | 0.0043 | 8.351 | 100.0% | 0.05% | 8.355 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 582 | 14.3 / 37.1 | 40.4 / 68.3 | 25.4 / 61.9 |

The same ways asked and worked out in every scenario as in the cloud, and the
same 69 walls: the same colony on Windows and Linux.

### Step 25b: talk, cloud, 13 November 2026

Talk adds a seventh free-time activity, which needs someone to talk to and
a topic. Measured 13 Nov 2026 on Ubuntu 24.04.5 LTS, 2 logical processors,
.NET 8.0.33, Release build. Own map.

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0049 | 33.6 | 100.0% | 0.06% | 33.6 |
| hauling | everyday | 0.0078 | 17.7 | 100.0% | 0.09% | 17.7 |
| arbitration | stress | 0.1200 | 65.9 | 100.0% | 1.4% | 76.5 |
| interrupted | stress | 0.0069 | 13.1 | 100.0% | 0.08% | 13.1 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 560 | 21.7 / 68.1 | 59.0 / 151.5 | 36.4 / 82.2 |

As in 25a, idle's slowest tick in the cloud is just over the everyday
threshold (33.6 ms against 33; 25a measured 34.8 here and 16.9 on the
maintainer's PC). The PC run decides. The test suite takes 2 minutes 43
seconds.

**The maintainer's PC, 13 November 2026** (Windows 10.0.26200, 16 logical
processors, .NET 8.0.33, Release build; own map): **every threshold met.**

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0027 | 19.7 | 100.0% | 0.03% | 19.7 |
| hauling | everyday | 0.0064 | 8.463 | 100.0% | 0.08% | 8.469 |
| arbitration | stress | 0.0577 | 34.8 | 100.0% | 0.69% | 34.9 |
| interrupted | stress | 0.0038 | 8.758 | 100.0% | 0.05% | 8.759 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 584 | 11.3 / 30.1 | 35.6 / 63.1 | 21.8 / 53.7 |

The same 70 walls as in the cloud.

### Step 25c: between jobs, cloud, 13 November 2026

Between jobs (library Review 43) makes colonists without work in Work hours
idle and, after a while, take free time; a new job is offered at once to those
between jobs. Measured 13 Nov 2026 on Ubuntu 24.04.5 LTS, 2 logical
processors, .NET 8.0.33, Release build. Own map.

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0060 | 34.5 | 100.0% | 0.07% | 34.5 |
| hauling | everyday | 0.0085 | 11.6 | 100.0% | 0.10% | 11.6 |
| arbitration | stress | 0.1188 | 55.7 | 100.0% | 1.4% | 65.6 |
| interrupted | stress | 0.0082 | 18.1 | 100.0% | 0.10% | 18.1 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 561 | 21.4 / 73.4 | 52.2 / 137.0 | 34.5 / 86.1 |

As in 25a and 25b, idle's slowest tick in the cloud is just over the
everyday threshold (34.5 ms against 33); on the maintainer's PC 25b measured
19.7. Idle's mean tick rose from 0.0049 to 0.0060 ms: idling colonists now
walk in Work hours. The PC run decides. The test suite takes 3 minutes 1
second.

**The maintainer's PC, 13 November 2026** (Windows 10.0.26200, 16 logical
processors, .NET 8.0.33, Release build; own map): **every threshold met.**

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0046 | 20.6 | 100.0% | 0.05% | 20.6 |
| hauling | everyday | 0.0074 | 8.508 | 100.0% | 0.09% | 8.510 |
| arbitration | stress | 0.0746 | 38.7 | 100.0% | 0.89% | 46.1 |
| interrupted | stress | 0.0054 | 7.664 | 100.0% | 0.07% | 7.671 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 585 | 11.6 / 36.0 | 38.9 / 68.2 | 22.8 / 56.5 |

The same 70 walls as in the cloud.

### Step 25d: gathering places, cloud, 13 November 2026

Gathering places change only where talks meet (the profiler's scenarios mark
none). Measured 13 Nov 2026 on Ubuntu 24.04.5 LTS, 2 logical processors, .NET
8.0.33, Release build. Own map.

| Scenario | Kind | Mean tick ms | Slowest tick ms | Sustained speed | Simulation's share of a core | Slowest frame ms |
|---|---|---|---|---|---|---|
| idle | everyday | 0.0060 | 34.9 | 100.0% | 0.07% | 34.9 |
| hauling | everyday | 0.0104 | 17.4 | 100.0% | 0.13% | 19.6 |
| arbitration | stress | 0.1243 | 33.1 | 100.0% | 1.5% | 33.1 |
| interrupted | stress | 0.0087 | 24.9 | 100.0% | 0.10% | 24.9 |

| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |
|---|---|---|---|---|
| after a day of arbitration | 561 | 19.2 / 59.8 | 60.6 / 171.4 | 41.2 / 92.2 |

As in 25a to 25c, idle's slowest tick in the cloud is just over the everyday
threshold (34.9 ms against 33); on the maintainer's PC 25c measured 20.6. The
PC run decides. The test suite takes 3 minutes 6 seconds.

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
screen's own drawing time (Godot).

Decided (the maintainer, 10 November): step 20c makes the search faster with
the same results and sets a ceiling (512) on the ways kept; step 20d measures
two ways of asking for fewer ways (ranking jobs by straight-line distance
first, or one search outward from each colonist choosing). After 20c the
slowest minute with 300 walls planned is still 1.2 s in the cloud.

**Found in step 21c (cloud, 11 November):** since colonists decide between
the minutes, slow ticks can fall between them too, and the table's "Slowest"
counted only the whole-minute ticks. The profiler now shows the slowest
in-between tick as well ("Slowest second"). In arbitration, once walls close
colonists in on the second day, single ticks between the minutes take 1.4 to
1.9 s, in step 21c's code as first pushed and after its follow-ups alike
(over a million ways asked in the two days instead of about 100,000, depending
on which walls go up first). Measured in step 21d (above); step 22 takes it on.

**On the second tick (step 21d):** everyday play still costs next to nothing:
a game minute at rest or hauling takes 0.3 to 1 ms, so even ×60 (a game
minute each real second) leaves the machine nearly idle. What blocks the
intended experience is unchanged and is still path-finding with many pieces
planned: a tick of 1.5 to 1.7 s freezes the screen at every speed, and at ×60
already a tick of 35 to 40 ms (idle's slowest, when a way is worked out) makes
two frames late. The budget per tick shrinks with speed (1,000 ms at ×1,
16.7 ms at ×60), so step 22 (fewer ways asked and worked out) is measured
against ×60. On the maintainer's PC (about twice as fast) the worst ticks
are 0.74 to 0.85 s: still a visible freeze.

**After step 22 (areas):** the freezes are gone: the worst tick of the
300-wall scenario is 50 to 65 ms in the cloud, in every variation measured,
against 1.5 to 1.7 s before. Path-finding is still the largest cost (about
6.5 s of the scenario's two days) but no longer blocks the intended
experience; at ×60 a large plan can make a frame or a few late now and
then.
