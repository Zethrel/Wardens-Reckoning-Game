# Lesson 9f: suspicion — whom a shortage is blamed on

Your idea (2 November 2026): a hungry town should not blame every faction
in the world that has food to spare. It should suspect those it can see and
those it deals with: the towns nearby with plenty, and its regular traders,
whom it suspects of hiding their excess.

The generator and the game now do it the same way (the generator's
`north/suspicion.py`, the game's `src/Sim/Suspicion.cs`):

1. **Near producers.** Factions with a town at most `SUSPECT_REACH` (3)
   routes away, not through ruins, that ended the year with some of the good
   to spare. Next door weighs 1, two routes away 1/2, three away 1/3.
2. **Regular traders.** Factions it has bought the good from, by their share
   of what it remembers buying of it. The memory fades by half every
   `TRADE_MEMORY_HALF_LIFE` (10) years.
3. **The draw.** One to `SUSPECTS_MOST` (3) suspects are drawn by weight, and
   each gets the whole blame. Usually the obvious neighbour; sometimes the old
   trading partner across the sea.

Food counts as one good: the staple foods together (and each extra, if you
add any). The settings are in `history_settings.py`.

Who uses it:
- **Your rules' blame** (`negative_memory` in `history_rules.py`): the
  hungry town's faction blames its suspects, not every faction with food to
  spare.
- **The chokepoints' resentment**: a faction short of a needed good blames
  those who refused to sell it (always), and its suspects. Before, it blamed
  its bordering factions that had some to spare.

Suspicion rolls its own dice ("suspicion"), so the other systems roll as
before. The histories are not the same as before: who is blamed changes
opinions, and opinions change wars.

**Fixed on the way.**
- The generator blamed the chokepoints' shortages in the order a Python set
  gave the goods, which changes from run to run (only the last digits could
  differ). It now goes in sorted order, as the game does.
- The game's `FedShare` now adds up a town's food the way Python's `sum()`
  does, as the generator's `fed_share`. The two could differ in the last
  digit.

## 1. Run it

1. Pull the generator and export a world again (the export now holds every
   faction's trade memory):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. In the Book search for `blames the`.

What changed, in the generator's own 300 years (seed 42, history 42, and
seed 7, at 512): a hungry town's blame used to name 7 to 18 factions at once.
Now it names 1.5 to 2. Wars fell from 188 to 122 (seed 42) and from 181
to 147 (seed 7). In three game centuries on the new exports there were 12 to
55 wars (the generator's last century: 51) and 10 to 49 (generator: 25), and
each hungry town's blame named 1 to 2.4 factions.

(History 44 of seed 42 now ends with no one alive: the long winter struck in
year 20 and the great dying in year 25. Their dice are the happenings' own,
but the happenings roll for every lived-in town. Once suspicion changed a few
wars the towns differed, and so did the years the catastrophes fell on.)

Saves are still version 8. An older save of version 8 loads: it keeps the
export's trade memory.

## 2. Where the code is

- **`src/Sim/Suspicion.cs`**: `Remember` (the trade memory, after each year's
  trade), `Shares`, `Within` (towns some routes away), `Spare`, and
  `Suspects` (the draw).
- **`src/Sim/Rules.cs`**: a rule's blame falls on `Suspects` of its town's
  hunger.
- **`src/Sim/Chokepoints.cs`**: `Resent` blames the refusers and the
  suspects, in order of faction and good.
- **`src/Sim/YearCore.cs`**: `Remember` right after the year's trade.

How the tests prove it (`tests/SuspicionTests.cs`):
- **Against the generator.** `make_expected.py` asks the generator's own
  `suspects` about every town short of food and every faction short of a
  needed good, with a fixed list of numbers for its dice. The game asks the
  same with the same numbers, and every suspect must match. The chokepoint
  cross-checks (lessons 9d and 9e) now run with suspicion on both sides.
- **Each rule on its own.** Other tests check:
  - the trade memory (food as one good, a faction's own trade not
    remembered, fading in its half-life, dropping out below a millionth);
  - the reach and ruins, and a hundredth of food to spare;
  - that the nearer is heavier, and a draw exactly on an edge goes to the
    next;
  - two suspects are two, and the dead are never suspected;
  - only towns short of a good look around;
  - a hungry town's rule blames its suspects;
  - a save keeps the memory.

(One mutation can't be caught in the test world: your trade settings have
no extra foods, so the extras' part of food to spare is never tried.)

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `Dictionary<(int Buyer, string Good, int Seller), double>` | a dict with tuple keys | `Game.Bought` |
| `new Queue<double>(...)` and `dice.Dequeue()` | stand-in dice | `SuspicionTests` |
| `foreach (var key in food.Contains(good) ? new[] { good, Food } : new[] { good })` | `for key in ((good, FOOD) if good in food else (good,)):` | `Suspicion.Remember` |

## 3. Exercise

1. In `history_settings.py`, set `SUSPECT_REACH` to 1: only the next town is
   suspected. Export, play 100 years: are there fewer wars out of hatred?
2. Set `SUSPECTS_MOST` to 1 and `TRADE_MEMORY_HALF_LIFE` to 50. Who do the
   hungry blame now (search `blames the`)?
