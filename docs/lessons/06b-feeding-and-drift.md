# Lesson 6b: feeding by tastes, and drift

Two things that change slowly, and one that was missing.

**Feeding by tastes.** Your settings give the peoples their own food
(PEOPLE_FOOD in `settlement_settings.py`) and let paths farm hard land as if
it were kinder (PATH_LAND in `faction_settings.py`). So what a town's land
feeds depends on who farms it. In the generator, when a town changes hands
(a conquest, a split) or its holder changes path, its food, capacity and
harvest are worked out again at the start of the next year. Until now the
game kept every town's food as the export left it: since step 4, towns that
changed path or hands grew and harvested by the wrong people's tastes. The
game now does as the generator does.

**Drift.** Every faction's values (its people's own ways, which its rulers
pull away from) move slowly toward what the land it holds encourages: your
LAND_INFLUENCES (coast, rivers, highlands, lowlands) on its people's nature.
They go halfway there every DRIFT_HALF_LIFE years (`migration_settings.py`).
A faction that broke away into the mountains slowly becomes a mountain people.

## 1. Run it

1. Export a world again in the generator (the export now holds the history's
   land, region by region: biomes, lakes and kinds of land):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. Nothing new is drawn: the change is
   in how towns grow after a conquest or a split, and in the factions' ways.

## 2. Where the code is

- **`src/Sim/Feeding.cs`**: the generator's `north/tastes.py`: `FoodOf`
  (`food_of`), `CapacityOf` (`capacity`), `YieldsOf` (`yields`, and
  `trade.yields_of`), `Refresh` (`Feeding.refresh`). Each town knows the
  regions it farms (`TownNow.Cells`) and who it was last fed as (`FedAs`).
- **`src/Sim/Drift.cs`**: `migration.year_of_drift`, with
  `factions.shares_of` and `factions.influenced_traits`.
- **`src/Sim/Py.cs`**: Python's own `sum()` and `round()`. Since Python 3.12,
  `sum()` keeps the tiny bits each addition loses and adds them back at the
  end, so adding the same numbers can differ from C#'s `+` in the last digit.
  That is why `CLAUDE.md` asks for Python 3.12: the generator is the
  reference, and the game must add the same way.
- **`src/Sim/YearCore.cs`**: feeding comes first in the year, drift after the
  splits.
- **`src/Sim/SaveFile.cs`**: saves are now version 6. A step 6a save is
  refused: start a new game on the world.

How the tests prove it (`tests/FeedingTests.cs`): the generator's own
`Tastes`, run on the exported land, gives every town exactly the capacity
the export holds, so the exported land is complete. Then for every town the
tests compare the food, capacity and harvest for its holder's people and path,
and for another people and path, with the generator's; and the land's shares,
the targets and a year of drift with `year_of_drift`'s. They also check what
must follow: a town taken is fed by its new people from the next year and
not before; a ruin is not fed; between LAND_YEARS the targets stay; a save
carries it all.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `(int? People, string? Path)? FedAs` | a tuple that may be `None` | `TownNow` |
| `(kind & 2) != 0` | `kind & 2` (is that bit set?) | `Drift.SharesOf` |
| `MidpointRounding.ToEven` | `round()`: a half goes to the even number | `Py.Round` |

## 3. Exercise

1. In `settlement_settings.py`, give the elves twice the food from `taiga` in
   PEOPLE_FOOD. Export, play 100 years, and compare an elven town's people in
   the Book before and after.
2. In `migration_settings.py`, set `DRIFT_HALF_LIFE` to 5. Export, play: do
   the factions' paths change more often (rulers and revolutions follow the
   people's ways)?
3. In `src/Sim/Py.cs`, the first test in `tests/FeedingTests.cs` shows why:
   add 0.1 ten times in C# and in Python, and look at the last digits.
