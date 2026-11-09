# Lesson 17b: founding on a local map

Until now the founders arrived on the test ground, a flat 48 by 48 field
written by hand in `construction.json`. The generator now exports a local map
for each of the best founding sites (its lesson "Local maps", step 17a): 256
by 256 tiles, a metre each, with grass, rough ground, sand, snow, mud, fresh
and salt water, trees and rocks. This step reads those maps and founds the
colony on one. Your calls are in the design library as Review 36.

## Reading the map (`src/World/LocalMap.cs`)

`world.json` lists the sites under `founding_sites`, and each one names its
file:

```csharp
var world = ExportedWorld.Read(folder);
foreach (var site in world.FoundingSites)          // a FoundingSite record each
    Console.WriteLine(site.Site + ": " + site.Biome + (site.FreshWater ? ", water" : ""));
LocalMap map = world.LocalMap(1);                  // reads local/site-01.json
```

A **record** (`public sealed record FoundingSite(int Site, int X, ...)`) is a
class written in one line: C# makes the properties, a constructor, and `==`
that compares the values. That is all a site needs to be.

The file stores the map as rows of letters, with a key:

```json
"surfaces": { "g": "grass", "w": "fresh_water", ... },
"things":   { "T": "tree", "R": "rock" },
"ground":   ["ggggwwgg...", ...],
"on_ground":["..T.......", ...]
```

`LocalMap.Parse` turns each letter into its name through the key (a
`Dictionary<char, string?>`; `.` means nothing on the tile). It checks
everything a broken or foreign file could get wrong, and says which: the
format, a version newer than it knows, a row too short, a letter its key does
not give. Each problem throws a `WorldFileException` naming the file, the
same exception the world's own reader uses, so the menu can show it.

Look at `Bad(...)`: a **local function**, a small function written inside
another. It can use the outer function's variables (`where`, the file's name),
so each message says where it came from without passing it along.

## Water (`Surface.CanWalk`, `Ground.Water`)

A surface's `speed` may now be 0: it cannot be walked on, and nothing can be
built on it. `fresh_water` and `salt_water` have speed 0 in
`construction.json`, next to the new sand (0.7), snow (0.6) and mud (0.5).

Whether a tile blocks is now worked out in one place:

```csharp
void Reblock(int i) => blocked[i] = obstacles[i] != null || pieceBlocks[i] || !surfaces[surfaceOf[i]].CanWalk;
```

Before, each method set `blocked` itself, and taking a piece off a tile reset
it to "is there a rock". On water that would have unblocked the river. One
method that asks every question each time cannot forget one.

Planning on water says why it cannot: "It is fresh water: nothing can be
built on it yet." (Bridges and docks come later.)

## The ground of a map (`Ground.FromMap`)

Each tile gets its surface (looked up in the content by its id; a map with a
surface the content lacks is refused, naming it) and its tree or rock as an
obstacle. The ground's own surface is the one most tiles have, so a ground
of grass lists only its other tiles.

## Rooms: only walls and doors (`Ground.FindRooms`)

The first real maps showed a problem. In a forest, trees often stand on all
four sides of a tile. The old room rule ("closed in by anything that blocks")
called each such tile a room, and the first-day test found nine rooms instead
of one. The design library (Review 33) says a room is space closed in by
**walls and doors**, so the rule now follows it:

- the flood fill spreads through every tile that is not a wall or a door,
  trees, rocks and water included;
- a group that reaches the edge, or holds water, is outdoors (water is open
  to the sky, so walls round a pond make no room);
- in any other group, the tiles a person can stand on are the room.

A rock inside a house is fine (its tile is just not part of the room); a rock
in the line of the walls lets the outside in.

## The founding (`Founding.Found(seed, arrival, content, map)`)

1. The carts on the open tile nearest the map's middle (the generator keeps
   14 m round the middle clear, so this is the middle itself).
2. The founders and their bedrolls beside them, as on the test ground. The
   supplies now include 60 litres of water (`founding.json`).
3. If the map has fresh water: a drinking place on the **bank tile with the
   shortest walk** from the carts, named for the water ("The brook", "The
   river", "The lake"). It holds `spring_litres_per_day` (colony.json) each
   morning, like the stand-in stream did. Salt water is no drink: a site by
   the sea, or a dry one, has only what the founders brought until they build
   a well.

The test-ground `Found(seed, arrival, content, x, y)` is still there, for the
tests and for trying things out. Both share `Arrive`, so the founders come
the same way on either.

## Saves (version 10)

A save listed every tile that was not the ground's own surface, and every
obstacle, as `[what, x, y]`. On a 256 by 256 forest map that is tens of
thousands of entries, each written over five lines. Now the ground is saved
like the map itself: rows of letters, `'a'` the first surface in the save's
list, `'b'` the next. A colony on a whole map saves in about 140 KB.

Older saves (version 9) are refused, as before release 1.0.

## How fast?

On your world 7 (seed 7, history 7), the founding on sites 1, 2, 8 and 9 took
80 to 300 ms each, and 16 hours of play 340 to 700 ms (in the cloud, a
Release build). The test `On_a_whole_local_map_the_founders_still_house_themselves_on_the_first_day`
prints its own time.

## Exercise

1. In `content/construction.json`, make mud slower (`"speed": 0.3`). Run
   `dotnet test --filter LocalMapTests`. Which test notices, and why does
   only that one?
2. Give `fresh_water` a speed of 0.2 (wading). What can the founders do now
   that they could not, and which test fails?
3. In `Founding.WaterName`, add a name for `"sea"`. Why will no spring ever be
   given that name?
