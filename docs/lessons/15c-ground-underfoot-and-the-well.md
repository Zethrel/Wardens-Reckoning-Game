# Lesson 15c: what is underfoot, and the well

Colonists now walk more slowly on grass than on a road, and more slowly still
on rough ground. Each also has their own legs, which will matter once injuries
exist. There are roads to build, a well (the first structure bigger than one
tile), and pieces can be taken down again. Your calls, written into the
library as Review 34:

| Underfoot | Speed |
| --- | --- |
| a road, a floor | 100% |
| grass | 80% |
| rough ground | 55% |

The road takes labour only (10 minutes a tile). The stream (the stand-in
spring) stays until step 17 brings rivers; the well is a second, closer
source.

## Speed (`src/Colony/Ground.cs`, `Work.Walk`)

Every tile has a **surface**, and a piece may set its own speed (a road and a
floor do; a wall or door does not, so the surface beneath counts).
`ground.Speed(x, y)` gives the tile's speed.

**Finding the way.** A step used to cost its length. Now it costs its length
divided by the speed of the tile it steps onto, so 1 m of grass counts as
1 / 0.8 = 1.25 m of road. The way found is the **quickest**, not the shortest:
across rough ground, a longer way along a road can win.

**Walking.** A minute of walking is now a minute of time, spent stretch by
stretch. Each stretch goes at:

```
walk_m_per_minute (70, on a road) × the tile's speed × the colonist's legs
```

So in a minute a healthy colonist walks 70 m of road, 56 m of grass or 38.5 m
of rough ground. With legs at 50%, half that.

**Legs** (`worker.Legs`) start at 1 (100%). Body functions and injuries come
later and will lower it; for now you can set it by hand to see the effect. It
must be above 0 and at most 1.

## The well (a fixed footprint)

The well covers 2 by 2 tiles and is used from one tile beside it (`access` in
the content: just below its left half). The library calls this the second
style of building: one object with its own footprint, not a room put together
from pieces.

- **Planning** checks every tile of it ("At (30, 8): A rock is in the way.") and
  that its access tile is open ground. A wall may not be planned on that
  tile, before or after.
- **Building:** the timber is brought to its access tile, and the builder
  stands there, not on any tile beside it.
- **Finished:** its four tiles block walking, and a store of water named
  "Well" stands on the access tile. It is filled to 200 litres each morning,
  like the stream. Colonists drink from whichever is nearer.

## Taking down (`Work.TakeDown(x, y)`)

A taking down is a project like building:

- It is worked for half the piece's minutes, and needs no materials.
- When done, the tile is as it was underneath.
- Half the piece's materials, rounded down, are left there and carried to a
  store: a wall (2 timber) gives 1 back, a door (3) gives 1, a floor (1) and a
  road give nothing.
- The materials that come back are written in the ledger as made, so every
  unit is still accounted for.

A well cannot be taken down yet.

## Two workplaces on their own

The library asks for an example where two workplaces work locally, with no
central building. Nothing new was needed: workshops already fetch from the
nearest store with goods free, and send their output to the nearest store that
takes it. A test shows it: two querns at opposite corners, each with its own
stockpile, never haul across.

Saves are version 8: the ground's surfaces, each colonist's legs, each spring
and well's litres, and takings-down.

## 1. Run it

Nothing on screen yet. In the game's folder (PowerShell): `dotnet test tests`.
The new tests are in `tests/MovementTests.cs`.

What they prove:

- **Distances:** 9 m of grass counts as 11.25 m of road and 9 m of rough
  ground as 16.4 m; a road brings it back to 9.
- **A minute of walking** goes 70 m on a road, 56 on grass, 38.5 on rough
  ground and 28 on grass with half-strength legs.
- **Legs** may not be 0 or above 1.
- **Roads:** colonists take one when it is quicker, even if longer; without
  it, straight across is quickest.
- **Building a road** uses nothing but time, and a floor is as quick to walk
  on.
- **The well:**
  - it is built from its side and blocks its four tiles;
  - its water stands on the access tile, and a wall cannot go there;
  - after 150 litres are drawn, it is full again the next morning;
  - planning it is refused on a rock, off the ground, or with its side
    blocked;
  - colonists drink at the nearer of the stream and the well.
- **Taking down:**
  - a wall takes half the time and gives 1 timber back, carried to the carts
    and written in the ledger;
  - a road gives nothing;
  - a well cannot be taken down, nor can empty ground;
  - taking a wall out of the house opens the room.
- **Two workplaces** work from their own stockpiles.
- **The save** keeps surfaces, legs, the well and takings-down, ends the same
  as an unsaved colony, and refuses five broken versions.
- **More tests:**
  - with legs at 1% a walk inside one rough tile goes at that tile's speed,
    and stepping off a wall goes at the next tile's speed;
  - a builder coming from afar works the well only from its side;
  - each spring and well keeps its own litres (a trickle of 50 stays at 50).
- **Mutation check:** 29 deliberate mistakes in the new code; 24 caught at
  first, 28 once four more tests were added. The one left lets a walk run
  slightly past the minute and then back. On a straight way that comes out
  exactly even, so no test of a straight walk can see it; it would only show
  on a way with a corner.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `double.NaN` and `double.IsNaN(x)` | `math.nan` and `math.isnan(x)` (here: "this piece sets no speed") | `Ground.Speed` |
| `public double Legs { get => legs; set => legs = ok ? value : throw ...; }` | a `@property` with a setter that raises | `Worker` |
| `kind.Access is var (dx, dy)` | `if kind.access is not None: dx, dy = kind.access` | `Build.cs` |
| `.Where(m => m.Value / 2 > 0)` with whole numbers | `m // 2 > 0` (C#'s `/` on whole numbers drops the remainder) | `Recovered` |

## 2. Exercise

1. In `content/construction.json`, make grass 0.9. Which test fails, and what
   does it say? Change the test's number to match.
2. Give the well a smaller footprint (`"size": [1, 1]`, `"access": [0, 1]`).
   Which tests notice?
3. In a test, set `ada.Legs = 0.25` and send her to the far spring. How many
   minutes does she need now?
