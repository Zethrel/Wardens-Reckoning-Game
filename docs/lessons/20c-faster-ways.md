# Lesson 20c: faster ways, and a ceiling

Step 20b measured the colony and found path-finding to be the bottleneck. You
chose two changes: find ways faster **with exactly the same results**, and set
a ceiling on how many ways are kept. Both are in `Ground.WayTo`
(`src/Colony/Ground.cs`).

## Dijkstra's method, again

A way to a goal tile is an array with a number for every tile: the metres of
walking from there to the goal. It is worked out by spreading out from the
goal: take the nearest tile not yet done, and for each tile around it, see
whether going through it is quicker than what that tile has so far.

"Take the nearest tile not yet done" needs a collection that hands out its
smallest item quickly. Until now that was a `SortedSet`, which keeps its items
in order. When a quicker way to a tile was found, its old entry had to be
found and taken out first: work on every improvement.

Now it is a `PriorityQueue<int, double>`: a tile (the `int`) with its metres
(the `double`) as its **priority**; `TryDequeue` hands out the one with the
smallest. A priority queue cannot take an item out from the middle, so the
old entry simply stays, and is passed over when it comes out:

```csharp
while (todo.TryDequeue(out int i, out double metres))
{
    if (metres > far[i])
        continue;                       // an older entry: this tile is already done
```

`out int i` declares a variable that the method fills in: `TryDequeue` returns
`true` and puts the tile and its metres in `i` and `metres`, or returns `false`
when the queue is empty, which ends the loop.

A second saving: a neighbour already as near as this tile cannot be reached
any quicker through it, so it is skipped before the dearer checks (is it
inside the ground, is it open, can a slanting step get past the corners):

```csharp
if (far[n] <= metres)
    continue;
```

**Why the results are the same.** Each tile ends with the smallest of "a
neighbour's metres plus the step from it", over its neighbours. Which order
the tiles come out in, when two are equally near, does not change that
smallest number, and neither skipping does: both only pass over what could
never be smaller. The tests compare every tile's metres, and the save
scenarios' fingerprints are unchanged.

Together these make working out a way about twice as fast.

## The ceiling

Each way kept is half a megabyte on a local map. `MostWays` (512) is the most
kept at once. When another is worked out, the one **least lately asked for**
goes:

```csharp
if (ways.Count >= MostWays)
    ways.Remove(ways.MinBy(w => w.Value.Asked).Key);
```

Each kept way now carries the count of askings when it was last asked for
(`Asked`); `MinBy` finds the entry with the smallest. Forgetting a way never
changes what happens: it is worked out again, the same, when needed.

Why 512 and not something small? Measured: with 300 walls planned, the
colonists weigh up all 300, and keeping only 64 ways made them work the same
ways out again and again: 37,346 times instead of 1,852, five times as slow.
A ceiling below the number of goals in play costs far more than it saves. So
512 is a guard against running out of memory, not a saving.

## What is left

The slowest minute with 300 walls planned is still more than a second in the
cloud: after each finished wall, every way is forgotten and many are worked
out again in the same minute. Step 20d measures two ways of asking for fewer.

## Exercise

1. Run `dotnet run -c Release --project tools/Profile -- arbitration --most-ways 64`,
   then with `--most-ways 512`. Compare "Worked out" and the mean tick.
2. In `WayTo`, take out the `if (far[n] <= metres) continue;` lines and run
   the tests. Do any fail? Run the profiler's `idle` again: how much slower is
   working out a way? Put it back.
3. Why would taking out `if (metres > far[i]) continue;` make no test fail,
   but the profiler slower?
