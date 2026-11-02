# Lesson 9c: happenings and eras

Your happenings (`happening_settings.py`: the plague of stalled breath, the
routes turning to mud, the long winter, the great dying) and the eras with
their tempers now go on in the game's years, as in the generator's history.

**Each year**, after attunement's mark and before the threats:

1. **Spreading.** A happening that reaches `"routes"` and struck a town last
   year may move on along each route from it, with its `"spread"` chance, to a
   town that outbreak has not struck yet. It dies out when it strikes nowhere
   new.
2. **Starting.** A `"world"` happening rolls once for the whole world and
   strikes every town that someone holds. Every other happening rolls in every
   held town, with its `"chance"` times the era's temper: `"town"` strikes
   there, `"region"` every town within `"radius"` (cells of a 256 map),
   `"routes"` there, and then spreads.
3. **Striking**, at once: `"population"` gained or lost, `"driven_off"` to
   the towns on its routes, a `"belief"` its faction may come to, `"blame"`
   of the faction where it started. For the `"years"` after, `"growth"`,
   `"harvest"`, `"trade"` (what a town makes and crafts) and `"war"` (its
   faction's readiness for war) are multiplied by their numbers.

**Eras.** With two tempers or more, an era begins when the last one is over:
its temper drawn by `"weight"`, its length between the two `ERA_YEARS`. One of
the events in `ERA_ENDERS` may end it early, with `ERA_END_CHANCE`. An era's
temper multiplies every happening's chance (`"chance"`, and some once more,
`"happenings"`), every faction's readiness for war (`"war"`), and its chance
of breaking apart (`"splits"`). The Book tells when an era begins.

**Fertility**, fixed on the way: a faction with a `"fertility"` trait
(faction_settings.py) grows at 2 × fertility times `GROWTH_RATE`, as in the
generator. The game left it out until now, so the Stitched and others with
fertility 0.2 grew faster in the game than in the generator.

**Not in the game:** a happening's `"when"` rule. It is Python code, so the
export cannot hold it (and an export with one fails). None of yours has one.

## 1. Run it

1. Pull the generator and export a world again (the export now holds what the
   happenings leave behind, and the eras):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. In the Book search for `era begins`,
   `winter`, `mud` and `plague`.

In three game centuries of seed 42 at 512 (game seeds 1 to 3), the routes
turned to mud in 16 to 30 towns and the plague struck 5 to 8, and one era
began in each; in the third, the great dying struck all 112 towns. The
generator's own centuries of that world (histories 42, 43 and 44): mud 6 to
36, the plague 6 to 15, one or two eras, and once the great dying (115 towns).

Saves are still version 8. An older save of version 8 loads: it keeps the
export's happenings and eras.

## 2. Where the code is

- **`src/Sim/Happenings.cs`**: `Happenings.Year` (`Happenings.year`):
  spreading, starting and striking (`Strike`); `Factor`, a town's lasting
  factor this year; `WarFactors`, each faction's readiness for war;
  `BeginYear` and `EndYear`, the eras; `TemperNow`, the era's temper;
  `ScaleTrade` (`history.scale_trade`).
- **`src/Sim/YearCore.cs`**: an era begins as the year begins; a happening's
  harvest comes before a threat's, its trade scales the markets, its year runs
  before the threats', the people it killed count for a mass death, its
  growth (and the faction's fertility) multiply growth; the war factors before
  the wars; the era may end as the year ends.
- **`src/Sim/War.cs`** (`WarChance`) and **`src/Sim/Splits.cs`**: readiness
  for war, and the temper's `"splits"`.
- **`src/Sim/GameSettings.cs`**: `Happening` and `Temper`.

Also new: the era's own dice (`"eras"`), apart from the happenings', so a new
happening never changes when an era ends.

How the tests prove it (`tests/HappeningTests.cs`): `make_expected.py` gives
the generator's own `Happenings` a stand-in for both its dice (a fixed list of
numbers, handed out in turn), with test happenings (a blight that spreads
along the routes, an exodus that drives people off and turns beliefs, a quake
that strikes a region and is blamed, a boom that strikes the whole world),
test tempers before yours, and short eras that can end early. The game gets
the same lists, and eight years are compared to the last digit: every town's
people and its four factors each year, the beliefs, the blame, who was killed,
each faction's readiness for war, the eras, what lasts and spreads, and every
event. Other tests check that an era runs its drawn years before the next begins,
that a temper's mood multiplies one happening's chance, that a faction with
faith 1 is not turned, that a happening's dead make a mass death and its
harvest comes the year after, that a big event ends the era, that a temper's
"splits" makes factions more or less brittle, a happening's trade on a market, that readiness
multiplies the chance of war, that fertility 0.5 grows as before and 1.0
faster, that without happenings no era begins, and that a save plays on the
same.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `sealed record HappeningLasting(string Name, int First, ...)` | a tuple `(name, first, last, effects)` | `Happenings.cs` |
| `game.Eras[^1]` | `self.eras[-1]` | `Happenings.BeginYear` |
| `if (about.Belief is var (belief, chance))` | `belief, chance = effects["belief"]` | `Happenings.Strike` |
| `string.Join(", ", targets.Select(...))` | `", ".join(... for t in targets)` | `Happenings.Strike` |

## 3. Exercise

1. In `happening_settings.py`, give the long winter `"chance": 0.02`. Export,
   play 100 years: how many towns fall into ruin (search for `ruin`)?
2. Make the routes turning to mud spread further: `"spread": 0.5`. Search
   for `spreads to`: how far does one outbreak go?
3. Give the `"troubled"` temper `"happenings": {"the_plague_of_stalled_breath": 5.0}`.
   Do plagues come in bunches now?
4. Set `ERA_END_CHANCE` to 0.5: do the eras get shorter?
