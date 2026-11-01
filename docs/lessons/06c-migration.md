# Lesson 6c: migration

People now move to new land in the game's years. A town that has filled its
land (CROWDED of the people its land can feed) or gone hungry HUNGRY_YEARS
years in a row may send some of its people away (MIGRATION is the chance a
year, MIGRANTS the share who go). They go to the best place within REACH of
home, by their people's own site score: free land, where they found a new
town with land of its own and a route home, or a ruin, which they bring back
to life. Either way it is a colony of their own faction, and one far from the
capital may in time go its own way in peace (lesson 6a). No one settles on
another living faction's land, in a rift, or past MOST_SETTLEMENTS towns.
Your settings are in `migration_settings.py`.

## 1. Run it

1. Export a world again in the generator (the export now holds what migration
   needs, on the history's grid):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. In the Book, look for "people leave
   crowded ... to found ..." and "... to resettle the ruins of ...". The new
   towns are on the map, with their land on the borders.

In three runs of that world the game's 100 years founded 4 to 7 colonies (the
generator founded 8 to 10 a century) and had 110 to 113 towns at year 400
(the generator 114).

A new town is drawn in the middle of its region of the history's grid. The
generator puts it on the best cell of that region, which needs the map's own
site scores, so the game's towns can sit a few cells from where the
generator would have drawn them. Where it stands in the history (its region)
is exactly the generator's.

**Famines.** The game's towns have fewer famines than the generator's (0 to 1
a century against 7 to 12). It is not migration: the generator's famines come
from a few towns its chokepoints cut off (a faction refusing to sell to one it
dislikes), and chokepoints come with step 9. Until then the game's trade
feeds them.

## 2. Where the code is

- **`src/Sim/Migration.cs`**: the generator's `north/migration.py` and the
  parts of `north/tastes.py` and `north/trade.py` it uses: `Year`
  (`year_of_migration`), `FreeSites` (`free_sites`), `ScoreGrid`
  (`Tastes.score_grid`: a people's own site score), `Claim` (`claim`: the
  land a new town farms), `Found` (`found`), `Resettle` (`resettle`),
  `RouteCost` (`trade.new_route` with `grids.cheapest_from`, Dijkstra's
  algorithm on the grid), `Nearness` (`settlements.nearness`).
  `Movements` holds what migration reads from the export and remembers.
- **`src/Sim/Game.cs`**: `AddTown` adds a town the migrants founded to the
  game and to the world's records. Each town knows its region of the
  history's grid (`HX`, `HY`); each mine its region too.
- **`src/World/ExportedWorld.cs`**: `AddTown`, so the map, the slider and the
  book know the new towns.
- **`src/Sim/SaveFile.cs`**: saves are now version 7. A step 6b save is
  refused: start a new game on the world.

The export's grids of decimals are written exactly, as base64 (each number's
eight bytes as text), not as rounded numbers: migrants choose the best site
to the last digit, and a rounded score could pick another.

How the tests prove it (`tests/MigrationTests.cs`): with MIGRATION at 1 the
generator rolls no dice at all, so the tests run a whole year of the
generator's own `year_of_migration` on the export
(`tests/fixtures/make_expected.py`) and compare everything the game does with
it: 22 migrations, the 20 new towns' places, names, food and capacity, the
land each farms, the new routes and what they cost, the free land left, the
borders and the mines. They also compare the free sites, every people's own
site score and a new town's land and route on their own; and check what must
follow: a faction with no ruler sends no one; the best ruin by the people's own
score, the lower number on a tie; free land wins over a ruin only when
strictly better; the town left behind keeps its size within the margin; no
one settles past MOST_SETTLEMENTS, in a rift or on another's land; and a save
carries it all.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `PriorityQueue<int, (double, long)>` | `heapq` with `(cost, order, ...)` tuples | `Migration.RouteCost` |
| `Convert.FromBase64String` and `BitConverter.ToDouble` | `base64.b64decode` and `np.frombuffer(..., "<f8")` | `Movements.Exact` |
| `moving.Sites ??= FreeSites(game)` | `if moves.sites is None: moves.sites = free_sites(...)` | `Migration.Year` |
| `out sites!` | (none: `!` tells C# the value is not null here) | `Migration.Year` |

## 3. Exercise

1. In `migration_settings.py`, set `MIGRATION` to 0.3. Export, play 100
   years: how many more colonies does the Book show?
2. Set `MOST_SETTLEMENTS` to the number of towns your world has now (count
   them in `world.json`'s `"settlements"`). What do migrants do then?
3. In `settlement_settings.py`, give the humans more weight on
   `away_from_others` in PEOPLE_SITES. Do their colonies go further out?
