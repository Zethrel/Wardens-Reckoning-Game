# Lesson 6a: splits, merging and empires

Factions now break apart and come together in the game's years. A far
province can break away from its faction and become a faction of its own, of
the same people: when its people are divided from their rulers, when it lies
too far from the capital, or behind a rival when a strong ruler dies. A colony
that leaves for being far away leaves in peace. Two small, friendly
neighbours on the same path may merge, and a small faction next to an empire
may join it. An empire (a faction with a large share of all the people) holds
together better. And a town that rises against its conqueror when its own
faction is gone can now found a faction of its own, as in the generator.

## 1. Run it

1. Export a world again in the generator (the export now holds the factions
   still waiting to rise, and names for the factions the game founds):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. New factions appear in the legend
   on the right, in a shade of their parent's colour. Open one's history in
   the **Book**: its card shows the faction it broke away from and the year
   it came to be.

In one run of that world, the game's 100 years had about as many splits as
the generator's own next 100 years (16 in peace, 5 by division, against 16
and 6), 13 small factions joining an empire (10 in the generator), and 31 new
factions (26). Alliances, which never formed in step 5, now do (35, against
25): they form between the new factions and their kin.

## 2. Where the code is

- **`src/Sim/Splits.cs`**: the generator's `north/splits.py`, function by
  function: `Year` (`year_of_splits`), `CapitalOf`, `Province`, `Fragility`,
  `Divided`, `Secede`, `Merge`, `UpdateEmpires`, `JoinEmpires`, `Absorb`,
  `HandOver`. `NameFor` and `Vary` are `north/newcomers.py`: how a new
  faction may differ from its kin when your `split_settings.py` says so.
- **`src/Sim/Colours.cs`**: the generator's `north/colours.py`, which picks
  a new faction's shade so it stands apart from every other faction, also
  for people with red-green colour blindness.
- **`src/Sim/War.cs`**: `Rebel` now founds a faction (`Splits.Secede`) when
  a town's own faction is gone and it neither rises again nor joins its kin.
- **`src/Sim/YearCore.cs`**: the empires are worked out before the wars, and
  the splits come after the rulers, as in the generator.
- **`src/World/ExportedWorld.cs`**: `AddFaction` adds a faction the game
  founded to the world's records, so the map, the legend and the book know it.
- **`src/Sim/SaveFile.cs`**: saves are now version 5. A step 5 save is refused:
  start a new game on the world.

What is not in it yet:

- **Capitals.** When a faction loses its capital, the largest town it still
  holds becomes the capital (the generator's `capital_of`). The generator's
  own way, a town chosen at random with bigger towns likelier
  (`CAPITAL_CHOICE`), comes with step 7.
- **A path of its own** (`OFFSHOOT_CHANCE`) also comes with step 7. It is 0 in
  your settings, so nothing is missing yet.
- **The era's temper** (happenings, step 9) does not yet make splits likelier
  or rarer.

How the tests prove it (`tests/SplitTests.cs`): where the generator rolls no
dice, the tests compare with its own functions (`tests/fixtures/make_expected.py`
runs them): the constants, each faction's capital, fragility and how divided it
is, which factions are empires, how many hold land, every town's journey from
its capital, the outer towns in order, the province that breaks away, and a new
faction's shade. Where it rolls dice, they check what must follow: a province
takes its towns, land and mines and starts bitter with its parent; a colony
leaves in peace; a faction of your FACTIONS waiting to rise is the one that
rises; a town taken from another people breaks away as theirs; a fragile realm
splits behind a rival; a cautious one, or one past MAX_FACTIONS, does not; far
towns drift away nearest first; small friendly neighbours merge; a small
faction joins an empire; a town whose faction is gone founds one; new factions
differ from their kin by your settings; and a save carries the new factions.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `Stack<int>` with `Push` and `Pop` | a list with `append` and `pop` | `Splits.Province` |
| `double[,] Normal = { { 1, 0, 0 }, ... }` | a list of lists | `Colours.cs` |
| `var (r, g, b) = Colours.KinColour(...)` | `r, g, b = colours.kin_colour(...)` | `Splits.Secede` |
| `List<GameEvent>? toldLater = null` | a keyword argument that may be `None` | `Splits.Secede` |
| `game.Alliances.RemoveWhere(pair => ...)` | `alliances = {p for p in alliances if not ...}` | `Splits.Absorb` |

## 3. Exercise

1. In the generator's `split_settings.py`, raise `DIVISION` from 0.03 to 0.1.
   Export, play 100 years: how many more factions does the legend show?
2. Set `MAX_FACTIONS` to 12 and export again. What stops growing, and what
   still happens?
3. Set `TRAIT_SPREAD` to 0.2 and `BELIEF_CHANCE` to 0.5. Export, play, and
   look in the Book for "unlike their kin".
4. Open a save in VS Code and find `"new_factions"` in `"years"`: each faction
   the game founded, with its colour, the faction it broke away from and the
   year.
