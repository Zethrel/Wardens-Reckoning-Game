# Lesson 9a: the god of death and her signs

The world bible says it plainly: a god of death who wants people alive, so
that more of them die in time; who does the bare minimum; and whose intent is
never stated. In the engine she is a rule, not a character, and the game now
plays her rule on in its own years (`death_settings.py`):

1. **Death sites.** Where many die in one place in one year, the dead are
   counted: every battle and rising, every great ritual, and every town that
   loses at least a tenth of its people at once (and at least 50 of them) to
   hunger or any of your rules. Ordinary deaths do not count.
2. **The metal.** After `METAL_DELAY` years, `METAL_PER_DEATH` metal for each
   of the dead forms, hidden, a few steps from where they fell. Metal forming
   close to metal already hidden joins it: a field of many battles becomes
   one rich site.
3. **Exploring.** Each year every town near formed metal may find it, the
   likelier the more its people dare (risk), and the less the more they hold
   the dead in awe: those who fear her dig little, those who worship her less
   still, those who deny her more. The finder's faction gets a new mine, or
   its mine close by grows.
4. **Beliefs.** A people that does not know her and finds metal where the
   dead lie comes to deny her (the bold) or worship her (the rest). A people
   that loses a tenth of its own in one year to hunger or plague may come to
   fear her.
5. **Digging angers.** With `DIG_ANGER` above 0, every faction that fears or
   worships her thinks less of a finder.
6. **Signs** (`SIGNS`): after a great death a sign may be seen around it,
   with your chance; the factions there may come to its belief, and their
   harvest has its factor for some years.

Your `DIG_ANGER` is 0 and `SIGNS` is empty, so finding metal angers no one
and no sign is seen until you give them values. The metal, the finds and the
beliefs are on.

## 1. Run it

1. Pull the generator and export a world again (the export now holds the
   metal still hidden, and names for new mines):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. In the Book search for `find metal`
   (a find, with how many died there and when) and `come to` (beliefs).

In three runs of seed 42 at 512 the game's century found metal 29 to 37
times (5 to 8 of them growing a mine already there); the generator's own
centuries found it 3, 37 and 23 times, rising as more battles are fought.

Saves are still version 8. An older save of version 8 loads: it keeps the
export's hidden metal.

## 2. Where the code is

- **`src/Sim/Death.cs`**: `Death.Year` (`death.year_of_death`): the year's
  sites become hidden metal (`Hidden`), formed metal is found (`Find`, which
  opens a mine with `NewMine` or grows one), `Angered`; `Death.Loss`
  (`death.year_of_loss`); and `Signs.Year` and `Signs.Factor` (`signs.py`).
- **`src/Sim/War.cs`**, **`src/Sim/Rituals.cs`**: battles, risings and great
  rituals write where the dead fell (`game.Deaths`).
- **`src/Sim/YearCore.cs`**: a town that loses many at once is a site
  (`game.MassDeaths`); after the year's wars and rituals the god of death's
  year runs, then the losses, then the signs; a sign's harvest comes before
  the rituals' and the mark's.

How the tests prove it (`tests/DeathTests.cs`): the god of death rolls dice
for almost everything (where the metal forms, how much, when, whether it is
found), so `tests/fixtures/make_expected.py` gives the generator's own
`year_of_death`, `year_of_loss` and signs a stand-in for their dice: a fixed
list of numbers, handed out in turn (`FIXED_DICE`). Python's `uniform(a, b)`
is `a + (b - a) * random()`, as the game's is, so the test gives the game's
code the same list, and everything is compared to the last digit: a year
with two battles and a town that lost many, 27 finds, 3 mines that grow, 68
angry factions, a sign seen three times and the beliefs it turned, every
mine, every memory, the metal still hidden and the lasting harvests. Other
tests check what that year does not reach: a people that did not know her
comes to deny or worship her by its risk, metal not yet formed is not found,
a great loss turns those who deny her to fear but not those who worship her,
a played year's dead all become hidden metal, and a save plays on the same.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `Func<double> random` and `game.DiceFor("death").Random` | passing the method `rng.random` | `Death.Year` |
| `(int)(-spread + (2 * spread + 1) * random())` | `int(rng.uniform(-spread, spread + 1))` | `Death.Year` |
| `faction.Belief is not ("fear" or "worship")` | `faction.belief not in ("fear", "worship")` | `Death.Angered` |
| `List<(int Year, int X, int Y, int Dead)>` | a list of tuples | `Game.Deaths` |

## 3. Exercise

1. In `death_settings.py`, set `METAL_DELAY` to `(5, 10)`. Export, play 100
   years: how many more finds does the Book show?
2. Set `DIG_ANGER` to 0.1. Search the book for `angered`: who is angered,
   and by whom?
3. Give yourself a sign: `SIGNS = {"pale_lights": {"chance": 0.5, "dead": 300,
   "reach": 30, "belief": ["worship", 0.3]}}`. Search for `pale lights`.
