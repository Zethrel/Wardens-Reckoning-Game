# Lesson 8b: the great ritual

A faction of the attunement path that is losing a war may, in desperation,
spend half its metal on one great working against its enemy. The game now
works great rituals in its own years, as the generator does
(`attunement_settings.py`):

1. **Desperation.** A faction is losing when its side has lost a larger share
   of its people to the war than the other side has, and at least
   `RITUAL_DESPERATION` of them. Then each year there is a chance of
   `RITUAL_CHANCE` that it works a great ritual: twice that under the most
   reckless ruler (no planning, all ambition), none under the most careful.
   A faction works at most one in each war.
2. **The cost.** It spends `RITUAL_METAL` of the metal left in its mines. No
   metal, no ritual.
3. **The strike.** It is worked from its town nearest the enemy, against the
   enemy's town nearest to it. The crater falls halfway between. That town,
   and every other town of the enemy's side within twice `CRATER_RADIUS` of
   the crater, lose `RITUAL_DEATHS` of their people; they count as the war's
   dead, and the enemy remembers it bitterly.
4. **The scar.** The land within `CRATER_RADIUS` of the crater is marked at
   `PERMANENT` for good (lesson 8a): it never fades, and it keeps seeping into
   the land around it.

**Not as in the generator: the land stays.** In the generator the crater also
sinks into the height map, and everything that flows from it is worked out
again: the sea and lakes (a crater can fill into a lake), the rivers, the
climate, the biomes, the routes, and towns that end up under water drown.
That would mean bringing most of the map generator into the game, so the game
leaves the land as it is (decided 1 November 2026). The deaths, the memory and
the mark are the generator's.

Rituals against threats come with the threats, in step 9.

## 1. Run it

1. Great rituals are rare (two in 300 years on seed 1, none on seeds 3, 5, 7
   and 42). To see one, make them likelier: in the generator's
   `attunement_settings.py` set `RITUAL_CHANCE` to 0.5 and
   `RITUAL_DESPERATION` to 0.01, then export seed 7:
   `python worldgen.py --seed 7 --history 7 --size 256 --export`
   (put the settings back afterwards).
2. In Godot, F5, open it, play 100 years, and search the Book for `great
   ritual`.

With those settings the generator's own history of seed 7 has 7 great
rituals, 4 of them in its last century; three game centuries had 0, 1 and 0.
The game's wars are fewer (they have no chokepoints and happenings yet, step
9), and a faction that already worked one in a war of the generator's cannot
work another in it.

Saves are still version 8. A save of lesson 8a loads: its wars count as
having had no great ritual.

## 2. Where the code is

- **`src/Sim/Rituals.cs`**: `WarYear` (`rituals.year_of_rituals`), with
  `Losing`, `Recklessness`, `NearestPair`, `Strike` and `Scar`.
- **`src/Sim/War.cs`**: each war (`WarNow`) remembers in `Rituals` the
  factions that have worked one in it.
- **`src/Sim/YearCore.cs`**: the great rituals come right after the year's
  wars, as in the generator.

How the tests prove it (`tests/RitualTests.cs`): with `RITUAL_CHANCE` at 1000
(certain for any ruler who is not perfectly careful) and `RITUAL_DEATHS` one
number, the generator rolls no dice that matter, so
`tests/fixtures/make_expected.py` sets up a war on the export (the first
attunement faction losing to the first other faction, with a mine of its
own) and runs its own `year_of_rituals`. The tests do the same in the game
and compare the war's dead, every town's people, the memories, the mines'
metal, every patch's mark and the Book's entry. They also check what must
follow where dice decide: one ritual a war at most; none for a side that is
not losing or not desperate enough; none under a perfectly careful ruler, or
without metal; a reckless ruler twice as often as a middling one; the enemy's
towns near the crater lose people and no others; and a save keeps who has
worked one.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `(int Source, int Target)?` | a tuple, or `None` | `Rituals.NearestPair` |
| `best is { } found` | `if best is not None:` | `Rituals.NearestPair` |
| `foreach (var (side, other) in new[] { (0, 1), (1, 0) })` | `for side, other in ((0, 1), (1, 0)):` | `Rituals.WarYear` |
| `war.Sides.Where(...).SelectMany(side => side)` | `{f for side in war.sides if ... for f in side}` | `Rituals.Strike` |

## 3. Exercise

1. With the settings of "Run it", set `RITUAL_DEATHS` to `(0.1, 0.5)`. Do
   the rituals in the Book kill more or fewer than before?
2. Set `CRATER_RADIUS` to 12. Search the book for `rift`: does a great
   ritual open one?
3. In `Rituals.Recklessness`, what would a ruler of planning 0.5 and
   ambition 0.5 give? Work it out, then check it in the code.
