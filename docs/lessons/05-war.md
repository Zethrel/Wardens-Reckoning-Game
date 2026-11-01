# Lesson 5: war and alliance

Factions now fight in the game's years. Two that both think well enough of
each other become allies; a faction that hates, envies or covets a neighbour
may declare war, and its allies are asked to join (one that refuses betrays
the alliance). Each year the two sides fight a battle on every front where
their land meets; a won battle may take the town, with its land and its mines.
A war ends in peace when one side is weary enough, fizzles out after years
without a battle, or ends when one side is gone. A conquered town resents its
new rulers, and may rise and return to its own people.

## 1. Run it

1. Export a world again in the generator (the export now holds the history's
   grid as land: who farms each region, and where the sea is):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. Watch the borders on the map move
   (they are redrawn every ten years, and after a conquest), and open a
   faction's history in the **Book**: declarations, battles, conquests,
   betrayals, peace and risings.

In one run of that world, the game's 100 years had about as many wars,
conquests, peaces, rebellions and fallen factions as the generator's own next
100 years. It had no alliances, though, where the generator had 25: every one
of those was with a faction founded after year 300 by a split, and splits come
in step 6. Until then, a faction whose people are all gone **rises again** when
one of its towns rebels, where the generator might let the town found a new
faction.

## 2. Where the code is

- **`src/Sim/War.cs`**: the generator's `north/war.py`, function by function.
  `Year` (`year_of_war`) runs alliances, declarations, battles, peace and
  rebellion, in that order. `Strength`, `Fronts`, `Reasons`, `Declare`,
  `Battle`, `Conquer`, `Resentment`, `Unrest`, `Rebel` and `MakePeace` keep the
  generator's names. `RedrawBorders` and `CountBorders` are the generator's
  `factions.spread_territory` and `factions.count_borders`.
- **`src/Sim/Game.cs`**: the game now holds the wars (`WarNow`: who is on
  each side, how many have died, when the last battle was), the alliances, the
  history's grid as land (`Grid`) and who holds each region (`Regions`). Each
  town knows its own people's faction (`Origin`) and when it was last
  conquered (`ConqueredIn`).
- **`src/Sim/YearCore.cs`**: war comes after the opinions and before the
  rulers, as in the generator.
- **`src/Sim/SaveFile.cs`**: saves are now version 4. A save from step 4 is
  refused with a reason: start a new game on the world.

How the tests prove it (`tests/WarTests.cs`): where the generator rolls no
dice, the tests compare with its own functions (`tests/fixtures/make_expected.py`
runs them): the constants at the top of `War.cs`, an army's strength (whole,
or fighting from one town), the fronts between two factions, their reasons for
war, the chance of war and the reason given, the odds in a battle, how much
each conquered town resents its rulers and how restless it is, the borders
each faction shares, and the land shared out again. Where the dice decide,
`WarChance` and `Odds` give the chance without rolling, so the chance itself
can be checked; then the dice only compare a number with it. And the tests
check what must follow a roll: a battle costs the winner's town half what it
costs the loser's; about half the battles an attacker wins take the town; a
conquest moves the town's land and mines and sours the loser; a bold ally
joins and a timid one betrays; an ally of both sides stays out; friends ally,
and fall out only past the margin; a war fizzles after five quiet years; a
conquered town in time returns to its own people, unless its conqueror's hold
on it is nothing; and a save taken in the middle of a war plays on as if never
stopped.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `HashSet<int>[] Sides` | a list of two sets | `WarNow` |
| `HashSet<(int, int)> Alliances` | a set of tuples | `Game` |
| `int? Ended` | a number or `None` | `WarNow` |
| `war.Sides[0].Overlaps(living)` | `war.sides[0] & alive` (do they share any?) | `War.Year` |
| `.Aggregate((best, w) => w.Value > best.Value ? w : best)` | `max(...)`, keeping the first on a tie | `War.Year` |
| `public record LandGrid(int Columns, int Rows, int[] Owner, bool[] Ocean)` | a small named tuple | `Game.cs` |

A grid is kept flat in C#, one row after another: the region in column `x`,
row `y` is `Owner[y * Columns + x]`. The generator keeps it as a list of rows,
`owner[y][x]`.

## 3. Exercise

1. In the generator's `war_settings.py`, double `WARLIKE` (0.04 to 0.08).
   Export, play 100 years: how many more wars does the Book show? Do factions
   fall faster?
2. Lower `ALLY_OPINION` to 0.5 and export again. Do alliances now form in the
   game's years? Do betrayals follow?
3. In `src/Sim/War.cs`, set `DefenderAdvantage` to `1.0` (no advantage for the
   defender). Play: do the strong swallow the weak faster? Change it back, and
   keep `DEFENDER_ADVANTAGE` in the generator's `north/war.py` the same as
   the game's.
4. Open a save in VS Code and find `"wars"`: each has its `"sides"`, its
   `"dead"` on each side, and `"ended"` (null while it goes on).
