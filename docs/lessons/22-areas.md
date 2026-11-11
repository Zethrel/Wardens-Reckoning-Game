# Lesson 22: areas, or not searching for what cannot be found

Step 21d measured ticks of 1.5 to 1.7 seconds in the 300-wall scenario: the
screen would freeze. This step finds out where that time went, and takes it
away without changing anything the colonists do.

## Measure before you change

The plan said to try two ideas: rank jobs by straight-line distance first, or
search outward from each colonist once. Before building either, a counter on
each caller of `Far` (taken out again afterwards) showed where the ways were
worked out:

```
assign  worked 3,804  42,374 ms
think   worked    70     817 ms
supply  worked     0       0 ms
```

Nearly all of it was choosing jobs. And it was not that there were many jobs:
it was that the colonist choosing could not reach them. Look at how a job
finds its colonist (`Work.Assign`):

```csharp
var worker = free.Where(w => Available(w) && !w.Forbidden.Contains(KindOf(job)))
                 .Select(w => (Worker: w, Far: Far(w.X, w.Y, x, y))).Where(p => !double.IsPositiveInfinity(p.Far))
                 ...
```

For every open job, `Far` works out the way to it, over the whole map, before
finding out it is infinity: there is no way. A colonist walled in by the walls
they built does that for all 300 jobs, and again after every finished wall,
because a new wall makes the ground forget the ways it knew. Neither of the
two planned ideas avoids that, so the maintainer chose to do this first.

## Areas

Before asking *how far*, ask *whether at all*. The open tiles fall into
**areas**: tiles you can walk between. Two tiles have a way between them
exactly when they are in the same area. Working the areas out is a **fill**:

```csharp
for (int start = 0; start < area.Length; start++)
{
    if (blocked[start] || area[start] != -1)
        continue;
    area[start] = next;
    todo.Push(start);
    while (todo.Count > 0)
    {
        int i = todo.Pop();
        // each open neighbour across, not yet in an area: into this one, and onto the pile
    }
    next++;
}
```

Start at any open tile not yet in an area, give it the next number, and keep
taking a tile off the pile (`Stack`: last in, first out) and putting its open
neighbours into the same area, until the pile is empty. Then start at the next
tile left over. Every tile is looked at a few times, with no sorting, so a
256 × 256 map takes about 2 ms in the cloud: a whole-map way takes about ten.

Why only the four steps across, when colonists also walk on the slant? A slant
step is allowed only when both tiles beside it are open (no cutting corners:
`CanStep`). Then you could have gone round by those two tiles anyway, so a
slant step never joins two areas that are not already joined.

## The edges of the rule

`CanReach` must say exactly what the search says, also in odd cases:

- Someone standing on a blocked tile (a wall just finished where they stood)
  can step off onto any open tile beside it, so they belong to the areas of
  those tiles.
- A blocked tile can be a goal: a wall to work on, reached from beside it. But
  water cannot be stepped onto at all (its speed is 0), so it is never a goal
  that can be reached.
- From a tile to itself there is always a way.

`AreaTests` checks it the honest way: on six scattered grounds, from every
tile to many goals, it asks both `Metres` (the search) and `CanReach`, and
they must agree every time. A test like that, which compares a fast answer
with a slow one known to be right, is a good way to change how something is
done without changing what it does.

## Forgetting at the right time

The areas change only when a tile becomes blocked or open, so `Reblock`,
which every change of a tile goes through, forgets them then:

```csharp
bool was = blocked[i];
blocked[i] = obstacles[i] != null || pieceBlocks[i] || !surfaces[surfaceOf[i]].CanWalk;
if (blocked[i] != was)
    areaOf = null;
```

A floor (it changes the speed, not whether a tile is open) leaves them as
they are. `areaOf` is worked out again the next time it is asked for: lazily,
so ten walls finished in one tick cost one fill, not ten.

## The result

`Far` asks the areas first:

```csharp
if (!ground.CanReach(tx, ty, gx, gy))
    return double.PositiveInfinity;
return ground.Metres(tx, ty, gx, gy);
```

The slowest tick of the 300-wall scenario went from 1,658 ms to 53 ms, and
the ways worked out from 4,061 to 622, with the same walls built and every
test unchanged. Before, the same scenario swung between 97,000 and 1.6
million ways asked depending on small changes; now four variations all ask
about 56,000. `docs/performance.md` has the tables.

## Exercise

1. In `Work.Far`, put `//` in front of the two `CanReach` lines. Run the
   profiler on arbitration
   (`dotnet run -c Release --project tools/Profile -- arbitration`). What
   happens to "Slowest second" and "Worked out"? Take the `//` out again.
2. In `Ground.Areas`, make the loop go over all eight `Steps` instead of the
   first four. Run `dotnet test tests --filter AreaTests`. Do the tests still
   pass? Why (or why not)? Put it back.
3. In `AreaTests.A_corner_cut_on_the_slant_joins_nothing`, draw the 3 × 3
   ground on paper. Which tiles are open, and why is there no way?
