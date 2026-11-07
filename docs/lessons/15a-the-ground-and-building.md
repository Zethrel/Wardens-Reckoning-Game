# Lesson 15a: the ground, and building on it

Until now the colony stood nowhere: places were points in metres, and colonists
walked straight from one to another. Now there is ground under them, made of
tiles, and the player can build on it. This is the design library's FP-04: a
construction order whose claims, hauling, work and finished piece stay
consistent the whole way, and that says clearly what it lacks.

Decided with you (library Review 32): tiles are 1 m square; colonists find
their way on the tiles; until the world generator makes local terrain (step
17), the colony stands on a flat test ground from `content/construction.json`.

## The ground (`src/Colony/Ground.cs`)

A `Ground` is a grid: tile (x, y) covers the metres x to x + 1 across and y to
y + 1 down. Each tile may hold an **obstacle** (a rock, a tree) or a finished
**piece** (a wall, a door, a floor). Rocks, trees and walls block walking; a
door and a floor do not.

**Finding the way** (path-finding). To walk to a tile, the ground works out,
for every tile, how far it is to walk from there to the goal. It starts at the
goal (0 m) and spreads out, always going on from the nearest tile not yet done.
This is Dijkstra's method, kept in `WayTo`. A step to a tile beside costs 1 m;
a step on the slant costs √2 (about 1.41 m). A slanting step is only allowed if
both tiles beside it are open, so nobody squeezes past the corner of a wall.

Once that table exists, walking is simple. From where you stand, step to
whichever tile around you has the shortest way left (`Next`). The table is kept
until something that blocks is built, and then it is forgotten and worked out
again.

`Metres(from, to)` is the length of the way, or infinity
(`double.PositiveInfinity`) if there is none. That one number now answers
"which store is nearest?" and "who is nearest to this job?" everywhere, so
colonists pick by walking distance, not straight lines, and skip anything they
cannot reach.

A colony without a ground (as in the tests of lessons 13 and 14) still walks
straight; `colony.Lay(ground)` gives it one, once, before anything is placed.

## Building (`src/Colony/Build.cs`)

The library's loop (Construction and Buildings §5), one piece on one tile:

1. **Check and plan.** `Work.Check("wall", x, y)` says whether it may go there,
   and if not, why, in words for the player:
   - "It is outside the ground"
   - "A rock is in the way."
   - "There is already a timber wall there."
   - "A timber door is already planned there."
   - "The hand quern stands there: a timber wall would block it."

   A floor may go under the quern; a wall may not. A place nobody can reach
   may still be planned, with a warning. `Work.Plan(...)` makes the
   **project**, even with no timber in the stores (library §5.3).
2. **Supply.** Each minute a project asks for what it lacks, as a workshop does
   (the same `Bring` code): hauls from the nearest store with timber free.
3. **Build.** With all its materials there, a build job claims them. A
   colonist allowed to `build` (a new kind of work) walks to a tile beside it
   and works the piece's minutes. If they stop, the work keeps its progress.
4. **Finish.** The materials are used up (one ledger operation) and the piece
   stands on the ground. A finished wall blocks walking from that minute on.

**Pause** (`Work.Pause(id, true)`) stops a project: nothing more is brought,
and its building is put down with its progress. **Cancel**
(`Work.CancelBuild(id)`) ends it. Materials are only used up when the piece is
finished, so cancelling gives every unit back: what was delivered is carried
to a store, and goods on the way are taken to a store instead.

**Why it waits** (`Work.WhyBuild(id)`) answers in the same three parts as
lesson 14b (for the player, a next step, a trace for the developer):

- "A timber wall at (20, 20) is waiting: waiting for timber: 2 more needed, and
  no store has any free." Next step: "Bring timber to a store, or let some
  free."
- "... is waiting: no colonist can reach it." Next step: "Open a way to it:
  leave a gap or put a door in the wall."
- "... is waiting: it is paused." Next step: "Resume it."
- When the build job is waiting for someone: "Nobody is allowed to build.", or
  each colonist's reason, now including "cannot get there".

## Changed on the way

- **Taking jobs.** Before, if nobody could take the oldest open job, nobody
  took any job that minute. Now the others are still handed out. With the
  ground this matters: a colonist cut off from one job can still take another.
- **New good:** timber (10 kg a unit, so a person carries 2). It is the only
  building material for now; which materials the first settlement uses is
  still an open question in the library.
- **Saves are version 6:** the ground (its size, its obstacles, the finished
  pieces) and the projects.

## 1. Run it

Nothing on screen yet. In the game's folder (PowerShell): `dotnet test tests`.
The new tests are in `tests/BuildTests.cs`.

What they prove:

- **The ground.** The way goes around a wall, is the same both ways, never
  cuts a corner, and is infinite once the wall closes. A colonist takes about
  90 m around a wall instead of 27 m straight. Someone left standing on a tile
  where a wall was just finished can still step off it. Through a wall with a
  door, every step of the way passes the wall only at the door.
- **FP-04.** A wall goes from planned to standing:
  - the haul of 2 timber;
  - the claim on both units at the site;
  - the builder working from beside the tile;
  - the piece standing, the site's inventory closed, 38 timber left, and one
    ledger line using the 2 units.

  No timber is lost or made from nothing in any minute of any construction
  test. Other FP-04 tests:
  - A floor takes its 15 minutes.
  - A wall planned with no timber says what it lacks, and is built once timber
    comes.
  - Every bad place is refused with its reason.
  - A place nobody can reach gets a warning, takes no timber, and says why.
  - A build nobody can get to, and a build nobody is allowed to do, each say
    why.
  - A paused wall waits and then carries on.
  - Cancelling a door being carried and a wall being built gives all 40
    timber back.
- **The save** keeps the ground, the pieces and the projects (a paused one
  too). A colony saved halfway through building ends the same as one that was
  never saved. Ten kinds of broken ground or project are refused.
- **More FP-04 tests:**
  - A project paused before its timber comes takes none.
  - A cancelled project keeps its timber until a store takes it back, and
    says so.
  - A builder coming from elsewhere works from the next tile.
  - A colonist whose way is walled up gives the job (or the meal) back, and
    says why.
  - The ground's distances match a slow, simple calculation on a ground with
    70 random rocks.
- **Mutation check:** 25 deliberate mistakes in the new code; 16 caught at
  first, all 25 once nine more tests were added.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `double.PositiveInfinity` | `math.inf` | `Ground.Metres` |
| `new SortedSet<(double Metres, int Index)>` and `.Min` | a heap of `(metres, index)` tuples, `heapq` | `WayTo` |
| `readonly string?[] pieces = new string?[w * h];` | `pieces = [None] * (w * h)` | `Ground` |
| `if (ground.Next(x, y, gx, gy) is not var (nx, ny)) return false;` | `step = ground.next(...)`, `if step is None: return False`, `nx, ny = step` | `Work.Walk` |
| `record Placement(bool Ok, string? Why = null, string? Warning = null)` | a frozen dataclass with defaults | `Build.cs` |

## 2. Exercise

1. In `content/construction.json`, make a wall cost 3 timber. A person carries
   2, so how many hauls does one wall need now? Which test tells you, and what
   does it say?
2. Move the rock at (30, 8) to (20, 20). Which test fails, and why?
3. Make the door block walking (`"blocks": true`). Which test fails, and what
   does its message tell you about the colonist's way?
