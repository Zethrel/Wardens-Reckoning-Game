# Lesson 9e: chokepoints, part 2

The rest of `chokepoint_settings.py` now goes on in the game's years. Every
system of the generator's history is in the game now. After the year's trade,
once part 1 has counted the shortages and told the embargoes (lesson 9d):

1. **Tech leak** (`LEAK_*`). A living `LEAK_PATH` faction (mechanism) whose
   towns have some `LEAK_FROM` (artifact cores), made or bought, may learn to
   make `LEAK_GOOD` (resonance engines): `LEAK_CHANCE` times how well its
   need of `LEAK_FROM` was met. It learns it once, for good.
2. **Debased coin** (`DEBASE_*`). Every debased coin recovers
   `DEBASE_RECOVERY` a year, and is gone below 0.01. The `DEBASER` (the
   Coinhouse) at war may debase its coin (`DEBASE_CHANCE`) by `DEBASE_STEP`, at
   most 0.9. Its towns craft that much less of it, and every faction that
   bought it blames the Coinhouse (`DEBASE_RESENTMENT` times how debased).
3. **Supply contracts** (`CONTRACT_*`). For each good a faction needs that it
   has no contract for, with `CONTRACT_CHANCE` it signs one with the faction
   that makes most of it, of those both think `CONTRACT_OPINION` of. That
   maker then sells to it first and never embargoes it (lesson 9d), until one
   thinks ill of the other or the maker stops making it.
4. **Sabotage** (`SABOTAGE_*`). In every war, each faction may with
   `SABOTAGE_CHANCE` wreck an enemy town that makes a needed good, chosen at
   random. That town makes none of it for `SABOTAGE_YEARS`, and its holder
   blames the saboteur.
5. **Labour displacement** (`CONSTRUCTS`, `DISPLACE_*`). The Stitched's
   constructs at work in towns of the displaced paths (handcraft, mechanism)
   take up to `DISPLACE_GROWTH` of their growth this year (all of it at
   `DISPLACE_FULL` constructs a thousand people). The displaced blame the
   Stitched and warm to each other.
6. **Secrets** (`SECRETS`, `SECRET_*`, `DOUBLE_SALE`, `BETRAYAL`). A faction
   that bought sealed secrets may learn one about a rival (a neighbour,
   chosen at random) and think less of it. Sold twice, the rival thinks less
   of the buyer too, and if the two are allies their alliance may break.
7. **Legitimacy** (`LEGITIMACY_GOOD`, `LEGITIMACY`, `SOFT_POWER`). A
   faction whose towns are supplied with ancestor consultation feels up to
   `LEGITIMACY` less grievance this year, and warms to those it bought it
   from.

The leak and debased coin roll the chokepoints' dice; the rest roll their
own, as in the generator.

**Fixed on the way.** A ruler's grievance added up its four parts with a
plain sum. Python's `sum()` (since 3.12) adds decimals more carefully, and the
game now does the same (`Py.Sum`).

## 1. Run it

1. The generator's export is as in lesson 9d; export a world if you have none:
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. In the Book search for `secret`,
   `wreck`, `sign to buy`, `debase` and `learn to make`.

In three game centuries of seed 42 at 512 (history 42) there were 16 to 32
sabotages, 7 to 19 secrets and 4 to 18 contracts; the generator's own last
century had 33, 5 and 3. On seed 7: 1 to 12 sabotages against 5. History 44
of seed 42 has less of everything in the game than in the generator (10 to
17 sabotages against 45, and 0 or 1 embargo against 8). I have not found
out why that world goes so differently, and I won't guess at it here.

Saves are still version 8; a save keeps the contracts, sabotage, debased coin
and leaks (and every faction's learned goods, as before).

## 2. Where the code is

- **`src/Sim/Chokepoints.cs`**: `AfterTrade` runs the parts in the
  generator's order: `Leak`, `Debase`, `Contract` (which first ends the
  contracts that end), `Sabotage`, `Displace`, `Secrets`, `Legitimacy`.
  `GrowthFactor` is a town's growth after displacement.
- **`src/Sim/YearCore.cs`**: `AfterTrade` gets the year's sales and both dice;
  a town's growth is times its `GrowthFactor`.
- **`src/Sim/Rulers.cs`**: grievance is times the legitimacy factor.

How the tests prove it (`tests/ChokepointTests.cs`):
- **Against the generator.** `make_expected.py` runs three years of the
  generator's own chokepoints on the test world, with a fixed list of
  numbers for both its dice. It makes the parts likely and uses goods the
  world trades (the test world does not trade sealed secrets or ancestor
  consultation), with a war between the two largest factions and every two
  factions allies. The game does the same, and every year is compared: the
  contracts, sabotage, debased coin, leaks, learned goods, alliances, the
  growth and grievance factors, the blame, the counts and every event. Those
  three years hold a leak, debasement, contracts signed and ended, sabotage,
  secrets and a betrayal.
- **Each rule on its own.** Other tests check the leak's share, that coin is
  debased only at war, at most 0.9, and recovers until gone, that a contract
  goes to the maker of most and is not signed twice, that a wrecked town is
  not wrecked again, that the constructs' maker is not displaced, that a
  secret comes from the lowest-numbered seller, and that a played year's
  growth and grievance feel it.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `new SortedSet<(int Taker, int Maker)>()` | `bought = set()` then `sorted(bought)` | `Chokepoints.Debase` |
| `.OrderBy(m => -m.Value).ThenBy(m => m.Key).ToList()[0]` | `min((-amount, maker) for ...)` | `Chokepoints.Contract` |
| `targets[(int)(dice() * targets.Count)]` | `rng.choice(targets)` | `Chokepoints.Sabotage` |
| `buyer < about ? (buyer, about) : (about, buyer)` | `frozenset({buyer, about})` | `Chokepoints.Secrets` |

## 3. Exercise

1. Set `SABOTAGE_CHANCE` to 0.2. Export, play 100 years: how many towns are
   wrecked (`wreck`)? Are there more famines (`ruin`)?
2. Set `BETRAYAL` to 1.0. How many alliances break over a secret (`breaks`)?
3. Make `DISPLACE_GROWTH` 0.8: do the handcraft towns near the Stitched
   shrink?
4. Set `LEGITIMACY` to 0.9. Are there fewer revolutions (`revolution`)?
