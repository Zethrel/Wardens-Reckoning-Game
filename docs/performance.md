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
a tick was one in-game minute, and the game ran 2 ticks a second at ×1 and 6
at ×3. Since step 21a a tick is one in-game second (library Review 39), and
since step 21b the game runs as many ticks a real second as its speed:

| Speed | Ticks a real second | Time for one tick | A whole game minute takes |
|---|---|---|---|
| ×1 | 1 | 1,000 ms | 60 s |
| ×3 | 3 | 333 ms | 20 s |
| ×10 | 10 | 100 ms | 6 s |
| ×30 | 30 | 33 ms | 2 s |
| ×60 | 60 | 16.7 ms | 1 s |

Less the time for drawing: at ×60 the screen gets one tick each frame (60
frames a second), so a tick slower than about 10 ms makes a frame late, and
one of a second or more freezes the screen for that long.

On the second tick the profiler shows two kinds of tick: the **minute ticks**
(every sixtieth: the colony's own work, needs, asking, supplying) and the
**second ticks** between them (walking and the colonists who think that
second; since step 21c, deciding is done there). "Slowest second" is the
slowest of those.

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
