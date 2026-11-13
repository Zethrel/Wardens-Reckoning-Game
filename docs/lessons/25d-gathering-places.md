# Lesson 25d: gathering places

Talk (lesson 25b) happened wherever two colonists happened to be. The design
library (Colonist Daily Life and Needs §11.1, "Places and time") asks for a
**gathering place**: an area the player marks, as a stockpile is, where talks
happen more often. You chose the narrow version: it changes talk only, and is
marked by dragging an area.

(The other half of that library line, "activities follow the time of day", was
already built in 25a: each activity's `when` in `leisure.json`.)

## A record for a place

```csharp
public sealed record Gathering(BuildingId Id, string Name, Area Area);
```

A `record` is a class whose job is to hold values. This one line gives it a
constructor, three read-only properties, and equality by value: two
`Gathering`s with the same id, name and area are equal. `Area` is the same
block of tiles a stockpile uses, with `Covers(x, y)` and `Tiles`.

The places themselves are kept in `Work`, in a new partial file `Gathering.cs`:

```csharp
readonly SortedDictionary<long, Gathering> gatherings = new();
```

A `SortedDictionary` keeps its keys in order, so looping over it always goes in
the same order: the order they were marked. That matters for determinism: the
same orders must give the same colony.

`CheckGathering`, `PlaceGathering` and `RemoveGathering` copy the stockpile's
three methods. A gathering place holds nothing, so removing it is just
`gatherings.Remove(...)`: talks there go on to their end.

## Meeting there

`TalkSpot` (lesson 25b) found someone to ask and a spot beside them. Now, before
that, it asks `MeetingPoint` for a tile of a gathering place that both can walk
to, within reach:

```csharp
if (MeetingPoint(w, p, cx, cy, within) is var (mx, my) && Beside(w, mx, my) is var (sx, sy))
    return (sx, sy, null, p.Id, (mx, my));
```

`is var (mx, my)` does two things at once: it checks the result is not null,
and unpacks the pair into two variables. If either part fails, the `if` is
false and the old way (walking over to them) is used.

`MeetingPoint` picks the tile nearest the middle between the two, so neither
walks much further than the other. It goes over the tiles in a fixed order
(row, then column), and only a strictly nearer tile replaces the best so far,
so on a tie the first one wins, every time.

## Waiting for each other

Before, the one asked stayed put and the talk began when the other arrived.
Now both may be walking, and either may arrive first. So the talk begins only
when everyone in it is at their place:

```csharp
if (talk.Began == null && talk.With.Any(id => workers[id.Value] is var p && !(p.Outing is Outing po && Arrived(p, po.X, po.Y, false))))
    return false;
```

Read it from the inside out: for a colonist `p` in the talk, are they at their
outing's spot? `Any(... !...)` asks: is anyone not there yet? If so, whoever
just arrived waits. The last to arrive begins the talk, and wakes the others
(as in 25b, their time in the talk starts now: waiting eases nothing).

## "More often"

Two numbers in `leisure.json`:

```json
"gathering": { "willing": 0.2, "drawn": 1.5 }
```

- `willing` is added to the chance that the one asked says yes, when asked to
  meet at a gathering place;
- `drawn` multiplies talk's weight in the weighted draw (lesson 25a) when the
  talk would be at one.

`GatheringTests` checks the "more often" by counting talks over three evenings,
on three seeds, with and without a gathering place. Breaking the rule (setting
both to "no effect") made that test fail: 8 talks either way.

## Saves

Version 22 keeps the gathering places in `"work"`: id, name and area. The save
test saves every 23 ticks, often enough to catch a colonist on their way to a
meeting, and checks the loaded copy goes on exactly as the original.

## A test that was right for the wrong reason

One older test failed after this step: `SanitationTests` checks that a boiling
cancelled with its fuel already in hand is finished, not abandoned, and then
that nobody holds timber. With 25d, colonists meet a little differently, so
the timings shift a few seconds, and now a **new** boiling (the water is still
marked "boil first") had begun by then, its boiler carrying the timber for
it. The game was right; the test asked too much. It now checks that nobody
without a boiling in hand holds timber, which is what it meant.

When a change makes an unrelated test fail, find out which is wrong before
changing either: here, running the test step by step and printing each
colonist's job showed the timber belonged to the new boiling.

## Exercise

1. Build the game, mark a gathering place near the camp in the evening, and
   watch: do pairs walk to it? Who gets there first, and what do they do while
   they wait?
2. Set `"drawn": 5`. What happens to the other free-time activities in the
   evening? Is that what you'd want?
3. Mark a gathering place 50 m from the camp. Do they still meet there? Read
   `MeetingPoint` and work out why (hint: `within`).
