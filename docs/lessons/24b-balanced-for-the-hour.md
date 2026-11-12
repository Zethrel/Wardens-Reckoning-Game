# Lesson 24b: balanced for a day of an hour

Step 24a made walking, loading and deciding slower in game time: a colonist
walks 0.05 m a tick, so 140 m take about 48 game minutes. Building, eating and
the rest stayed as they were. This step checks what that did to a day, changes
what needed changing, and measures how fast the game runs at ×5.

## Measure first

Before changing a number, the founders' first day was run on the content as
shipped (the test `On_their_first_day_the_founders_build_a_house_...`, but on
the new pace):

- the house (19 walls and a door) stood at **15:54**;
- the carts were emptied into it at **08:06 the next morning**.

The house was in time; the supplies were not. Work hours end at 17:00, and
carrying everything from the carts in the hour left was not enough. Walking was
not the problem here (the house is beside the carts): the time went on building.

## The choice

Four ways were possible: build faster, carry more at a time, shorten loading,
or accept supplies the next morning. You chose to build faster (12 November).
The library says how long activities take is "balanced for play", while
walking and loading stay at the pace you saw and approved. So every piece's
work in `construction.json` became two thirds of what it was:

| Piece | Minutes before | Now |
|---|---|---|
| Wall | 45 | 30 |
| Door | 60 | 45 |
| Floor | 15 | 10 |
| Road | 10 | 7 |
| Well | 240 | 160 |
| Water barrel | 60 | 40 |
| Wash basin | 30 | 20 |
| Ice hole | 30 | 20 |

(The door was rounded to 45, not 40: it is the one piece with hinges.) Now the
house stands at **14:06**, and the carts are empty the same afternoon, on the
test ground and on a whole local map.

## Tests that read the content instead of remembering it

Changing a number in the content should break only the tests that are about
that number. A test like this one breaks for nothing:

```csharp
while (job.Left > 45 * 60 - 20 * 60)     // twenty of its 45 minutes done
```

so it now asks the content:

```csharp
int whole = colony.Content!.Pieces["wall"].WorkMinutes * 60;
while (job.Left > whole - whole / 3)     // a third done
```

`whole / 3` with two whole numbers is **integer division**: 1800 / 3 is 600,
and 1801 / 3 would also be 600 (the rest is dropped). Here that is fine, since
the test compares with the same sum.

The two first-day scenarios now run on `TestContent.Shipped`, the content as it
is in the game, because there the balance is the point. The tests of one
mechanic at a time keep the old pace (lesson 24a).

## How fast at ×5

The library (Review 41) set provisional thresholds: at ×5 the game should keep
99% of its speed, use at most a quarter of one processor core for the
simulation, and have no tick slower than 33 ms in everyday play (100 ms in a
stress test). Saving and loading should each take under a second.

The profiler times every tick of its scenarios, then replays those times
through the real `Pacer`, frame by frame, as if the screen ran at 60 frames a
second:

```csharp
int ticks = pacer.TicksFor(last);
double took = 0;
for (int i = 0; i < ticks && t < times.Count; i++)
    took += times[t++] / 1000;
last = Math.Max(frame, took);
real += last;
```

A frame lasts 1/60 s, or longer if its ticks took longer; the next frame then
asks for more ticks (up to the pacer's limit), as the game does. The sustained
speed is the ticks run over the ticks that real time should have run. Drawing
the screen is not counted: that needs Godot, and is for a later step.

The results are in `docs/performance.md`. You run the same on your PC; then you
approve or change the thresholds, and they become binding.

## Exercise

1. In `construction.json`, set the wall back to 45 minutes. Run
   `dotnet test tests --filter first_day --logger "console;verbosity=normal"`.
   When does the house stand now, and which assertion fails? Put it back.
2. In the profiler's `Paced`, change `const double frame = 1.0 / 60;` to
   `1.0 / 30` (a slow screen). Does the sustained speed change? Why not?
   (Hint: what does the pacer do with a longer frame?)
3. `SkillTests` times a wall built at proficiency 0, 40 and 100 (half pace,
   the usual pace, and half again as fast). With the wall at 30 minutes, work
   out by hand how long each should take, then find where the test gets the
   30 from.
