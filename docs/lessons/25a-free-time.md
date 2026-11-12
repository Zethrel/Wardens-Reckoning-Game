# Lesson 25a: free time

Until now a colonist with nothing to do in their Recreation hours stood where
they were. Review 42 of the design library (Colonist Daily Life and Needs
§11.1) asks for free time, and this step builds its first part:

- two slow needs, **Recreation** and **Social**, that only free time eases;
- a **catalogue of activities kept as content**, so new ones need no code;
- colonists **choosing** one of their own accord, by their needs and interests;
- clean **interruptions**: benefit only for the time actually spent at it.

Talking comes in 25b, the gathering place in 25c.

## The catalogue: leisure.json

```json
{ "id": "sky_gazing", "name": "Sky gazing", "doing": "gazing at the sky",
  "place": "open", "when": ["dusk", "dark"],
  "stops": 1, "minutes_each": 40, "eases_per_hour": { "recreation": 45 },
  "drawn_by": ["sky"], "teaches": 0 }
```

Each activity says where it happens (`place`), when (`when`, parts of the day
defined in `times`), how many stops and how long at each, what it eases, and
which interests draw people to it. Add a seventh activity here and colonists
start doing it; the code never names an activity.

The content check (`Content.Read`) refuses an activity whose place, time,
interest or need doesn't exist, with a message saying which. That's how a typo
in the content can't quietly break the game.

## A new partial file

The free-time code is in `src/Colony/FreeTime.cs`, which starts:

```csharp
public sealed partial class Work
```

`partial` lets one class be written across several files. `Work` is now in
eleven: `Life.cs` for needs, `Learning.cs` for skills, and so on. To the
compiler they're one class; to you, a file per subject.

## Choosing: a weighted draw

At a colonist's think, in Recreation or Flexible Time, with nothing urgent,
`Choose` looks at every activity they could do now and gives each a
**weight**:

```csharp
foreach (var (need, perHour) in activity.EasesPerHour)
    weight += perHour * (10 + w.FreeNeeds.GetValueOrDefault(need)) / 100;
```

The more an activity eases, and the more the colonist needs it, the heavier.
Then interests (twice as heavy if they enjoy it, a third if they dislike it),
distance (`weight /= 1 + metres / 20`: 20 m away halves it), and variety (a
quarter for what they did last).

Then one is drawn by weight. Think of the weights laid end to end on a line,
and a point dropped on it at random:

```csharp
double roll = colony.DiceFor("leisure").Uniform(0, offered.Sum(o => o.Weight));
foreach (var o in offered)
{
    if (roll < o.Weight) { chosen = o; break; }
    roll -= o.Weight;
}
```

A weight of 3 next to a weight of 1 is chosen three times as often, but never
always. The dice are the colony's "leisure" dice, kept in the save, so the
same seeds always give the same evening.

## Where: finding a spot

`SpotNear` looks at every tile within `within_m` (60 m) and keeps those that
fit the place: beside a tree, beside water, open ground with nothing beside it,
or near a store for wandering. It also checks the tile is open, outdoors,
reachable (`CanReach`, lesson 22) and not already held by someone else's
outing. One is drawn from those.

A tile is held simply by being someone's outing's spot; when the outing ends,
the spot is free again. Nothing else needs releasing (Review 42, R42-C02).

## Doing it: an outing

```csharp
public sealed class Outing
{
    public string Activity { get; init; } = "";
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public int StopsLeft { get; internal set; }
    public int? Left { get; internal set; }   // null while walking there
}
```

`Spend` moves it on, from the same two places as work (lesson 24a): a
wake-up and a think. On the way, they walk; arriving wakes them the tick
after, and `Left` is set to the stay. At the stop, the time spent eases the
needs:

```csharp
int spent = Math.Min(seconds, o.Left.Value);
w.FreeNeeds[need] = Math.Max(0, w.FreeNeeds[need] - perHour * spent / Calendar.TicksPerHour);
```

`Math.Min(seconds, o.Left.Value)` makes sure only time actually at the stop
counts. And because `Left` is null while walking, the walk itself eases
nothing: the library's rule, in one `if`.

## Ending it

`EndOuting` is called from four places: an order (`Stopped`), a need (`Start`
of eating or sleeping), a collapse, and the end of the free block. When it's
an interruption, the activity waits an hour before it's offered again:

```csharp
w.NotBefore[o.Activity] = colony.Now.Tick + content.Leisure.RetryMinutes * Calendar.TicksPerMinute;
```

so a colonist doesn't walk back and forth to the same river.

## Making it cheap: measure, then fix

The first version worked, and every test passed, but the suite went from 2½
to 15 minutes, and the profiler's slowest everyday tick went over the binding
33 ms. Measuring (not guessing) found three causes:

1. **Looking at the same tiles six times.** Choosing looked for a spot once
   per activity. Now `Spots` looks at each tile once and sorts it into every
   kind of place it is: one loop instead of six.
2. **Looking when there is nothing to find.** In the dark, with sky gazing
   waiting after an interruption, there is nothing to do; a colonist still
   looked at every think: 1,845 times in an hour. Now, finding nothing, they
   set `LookAgain` and wait `look_again_minutes` (10). That alone took four
   days of the test colony from 13.9 seconds to 1.8.
3. **Searching the whole ground for a short walk.** A way is worked out from
   its goal over every tile (Dijkstra's method, lesson 20). A free-time spot
   is never far, so its way stops spreading past a limit:

```csharp
if (metres > limit)
{
    cut = true;                // everything left is farther than asked for
    break;
}
```

Is a way cut short still right? Dijkstra's method finishes tiles nearest
first, so every tile finished before the cut has its true distance. The rest
are reset to "not worked out". From a tile within the limit, the quickest
next step always goes to a nearer tile, so it's inside the limit too: the
walk is exactly the same. `AreaTests` checks this on scattered grounds: every
metre and every step compared with the whole way. The test caught one case I
had wrong (someone standing on a just-built wall), which is now fixed.

The lesson: a test suite that suddenly takes much longer is telling you
something about the game, not only about the tests.

## Saves

Version 19 keeps each colonist's `"free"`: their free-time needs, their outing
(activity, spot, stops left, ticks left), the activities waiting, the last one
done, and when they look again. `FreeTimeTests` saves every 97 ticks through an evening, loads each
copy, runs it on, and checks it ends exactly as the colony never saved.

## Exercise

1. Add an activity to `leisure.json`: `"cloud_watching"`, in the open, by day,
   one stop of 15 minutes, easing recreation by 30 an hour, drawn by `sky`.
   Found a colony at 17:00 and watch the inspector. Does anyone do it?
2. Set `"within_m": 5`. What happens to the nature walks on the test meadow,
   where the trees are 15 m from the camp? Why?
3. In `Weight`, change `/ 4` for the last activity to `/ 1`. Run
   `FreeTimeTests`: does any test fail? What would you see in the game?
