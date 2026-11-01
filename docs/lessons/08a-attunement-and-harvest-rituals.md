# Lesson 8a: attunement's mark and the harvest ritual

Attunement takes now and pays later, and the land pays. The game now runs
the generator's mark (`attunement_settings.py`) in its own years:

- **Marking.** Each town of a faction whose path marks the land (attunement)
  marks its own land by the metal it used this year: `MARK_PER_METAL` for
  every unit.
- **Spreading and fading.** Rivers carry `RIVER_SHARE` of a patch's mark
  downstream, animals `ANIMAL_SHARE` to the land around it, and a mark halves
  every `MARK_HALF_LIFE` years, unless it once reached `PERMANENT`.
- **What it does.** From `FAINT` a town's harvest suffers (up to
  `HARVEST_LOSS` at `STRONG`) and the Book tells of unease; where a patch on or
  by its land reaches `STRONG`, things prey on it (`PREY` of its people a
  year); where its land reaches `SEVERE`, it is given up and falls into ruin.
  A patch at `SEVERE` is a rift: it gets a name, and no one settles there.
- **Blame.** A faction whose land carries another's mark thinks less of it
  (`BLAME`, and `PATH_BLAME` for its own path's view).
- **The harvest ritual.** A faction whose path works rituals (attunement),
  with a town that went hungry last year, gambles on a ritual for this year's
  harvest (`HARVEST_RITUAL_CHANCE`), if it has metal; in a poor season
  (below `HARVEST_RITUAL_POOR`) it may too, the less the better it plans. The
  harvest is multiplied by a number from `HARVEST_RITUAL`, better under a
  competent ruler. Every other faction thinks a little less of it, and when it
  goes wrong it marks its own land.

The land is divided into *patches*, squares of a few regions of the
history's grid, each with one number, its mark. The generator now exports
every patch's mark, and the names for the rifts each people opens.

## 1. Run it

1. Pull the generator and export a world again:
   `python worldgen.py --seed 1 --history 1 --size 256 --export`
   (seed 1 has attunement factions that mark the land hard; seed 42 at 512
   has none alive by year 300, so nothing happens there).
2. In Godot, F5, open it, play 100 years. In the Book search for `harvest
   ritual`, `unease`, `prey` and `rift`.

In three runs of seed 1 the game's century opened 9 rifts and gave 5 or 6
towns up to the mark (the generator's own centuries: 0, 2 and 4 rifts, and 0,
1 and 10 towns, rising as its permanent marks spread). No one works a
harvest ritual there in the game's years: every attunement faction of seed 1
is gone by year 300, but their marks are not. On seed 7 the game's century
had 3 to 10 harvest rituals, the generator's 8 to 18: most rituals come of
hunger, and the game has fewer hungry towns until the chokepoints come with
step 9.

Saves are now version 8 (they hold the marks). A step 7 save is refused:
start a new game on the world.

## 2. Where the code is

- **`src/Sim/Attunement.cs`**: the generator's `north/attunement.py`.
  `Marks` holds every patch's mark and what the marks have done (`Read` takes
  them from the export, `Write` puts them in a save); `Attunement.Year` runs
  marking, `Spread`, `Fade`, `NameRifts`, `Effects` and `AddBlame`.
- **`src/Sim/Rituals.cs`**: the harvest ritual (`HarvestYear`) of
  `north/rituals.py`. The great ritual comes with lesson 8b.
- **`src/Sim/Py.cs`**: `PyDict`, a table that keeps its keys in the order
  they were put in, as a Python dict does (see below).
- **`src/Sim/YearCore.cs`**: the harvest ritual and the mark change the
  harvest before the markets; the mark's year runs after the trade, before the
  people eat: the generator's order.
- **`src/Sim/Migration.cs`**: a town the game founds gets its land by patch,
  and `IsRift` reads the marks.

**Why the order of a table matters.** The mark that spreads into a patch is
added up from every patch around it. Decimals added in another order can
differ in the last digit, and after a few years that digit can decide whether
a town is given up. Python's dict keeps the order its keys were put in; C#'s
`Dictionary` usually does too, but after a key is taken out it may put the
next new one in the gap. `PyDict` always puts a new key at the end, as Python
does, so the game adds the marks in the generator's order.

How the tests prove it (`tests/AttunementTests.cs`): the generator's mark
rolls no dice, so `tests/fixtures/make_expected.py` runs its own
`year_of_attunement` five years on the export, with the land marked thirty
times harder and every attunement town using metal, and a harvest ritual year
before them that is certain and goes wrong. The tests run the game's code the
same way and compare everything, to the last digit: every patch's mark and
maker, the rifts and their names, the harvest shares, every town's people,
the towns given up, the blame and the Book's entries. They also check what
must follow where dice decide: a frightened faction works a ritual in a poor
year the less the better it plans; no metal, no ritual; a new town has its
land by patch; no one settles in a rift the game opens; and a save plays on
the same.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `sealed class PyDict<TKey, TValue>` | `dict` (a class of your own that works like one) | `Py.cs` |
| `where TKey : notnull` | (none: a promise that a key is never null) | `Py.cs` |
| `List<((int, int) Patch, int Cells)>` | a list of `((column, row), cells)` tuples | `Marks.Lands` |
| `void Bring(...)` inside `Spread` | a function inside a function | `Attunement.Spread` |
| `(why, odds) = ("hungry", chance)` | `why, odds = "hungry", chance` | `Rituals.HarvestYear` |

## 3. Exercise

1. In `attunement_settings.py`, set `MARK_HALF_LIFE` to 300. Export seed 1,
   play 100 years: do more towns fall to the mark?
2. Set `HARVEST_RITUAL` to `(0.5, 1.2)`: rituals go wrong more often. Search
   the book for `goes wrong`.
3. Set `PREY` to 0.2. What happens to the towns near a rift?
