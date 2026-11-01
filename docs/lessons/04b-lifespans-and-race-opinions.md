# Lesson 4b: lifespans and race opinions

The lore of the First Death (decided 1 November 2026) changed two things the
rulers of lesson 4 do, in the generator and in the game alike:

- **A ruler ages by their people's lifespan.** A dwarf or an elf comes to the
  throne older and dies later than a human. The generator's
  `leader_settings.py` holds `LIFESPAN_SCALING`: at 1 a people's lifespan
  counts fully, at 0 every people ages as humans do.
- **Races have opinions of each other.** `RACE_OPINIONS` in
  `faction_settings.py` adds to how much one faction likes another, by their
  races: dwarves and gnomes get on, orc raiders are disliked by everyone.

(The third part of the lore for the history, the undead rising where magic's
mark is strong, belongs to the threats, which come to the game with step 9.)

## 1. Run it

1. Pull the generator and export a world again (the export now carries each
   faction's race and the races' lifespans):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. Open the Book, type `died at` in the
   search box: a long-lived people's ruler dies at 150 or 200 and their heir
   is crowned past 100.

Your saves of step 6c still load (they are version 7): nothing new is saved,
as a faction's race comes from the export.

## 2. Where the code is

- **`src/Sim/Rulers.cs`**:
  - `AgeScale` is `1 + LIFESPAN_SCALING x (lifespan / 80 - 1)`: 80 is a
    human's lifespan, so humans have a scale of 1 and a people living 240
    years a scale of 3 (at full scaling). A kind of a race (`"of"` in
    `race_settings.py`, such as elf_wood) lives as its race does.
  - `NewRuler` multiplies the new ruler's age by the scale, rounded as Python
    rounds.
  - `DeathChanceAt(age, scale)` counts a ruler of age `age` as a human of
    `age / scale`: a dwarf of 120 with a scale of 3 has a human's chance at 40.
- **`src/Sim/Opinions.cs`**: `RacePart` finds the opinion of one race of
  another in `RACE_OPINIONS`, trying the race, then the race it is a kind of,
  then `"*"` (any race), on both sides; `Opinion` adds it to the rest.
- **`src/Sim/GameSettings.cs`**: reads `LIFESPAN_SCALING`, `RACE_OPINIONS`
  and the races' lifespans from the export.

How the tests prove it (`tests/RulerTests.cs`, `LoreTests`): every faction's
scale is the generator's own (`tests/fixtures/make_expected.py` asks
`leaders.age_scale`); LIFESPAN_SCALING 0 makes every scale 1 and 0.5 halves
the difference; a long-lived people crowns older rulers; one of 300 is not
sure to die in a year where a human of 300 would be; and a faction that
splits away keeps its kin's lifespan. The opinions are checked against the
generator's own in `RulerTests.cs` (every pair of factions, from
`expected.json`), which now include the race part.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `string? race` | `race: str \| None` | `Rulers.AgeScale` |
| `IEnumerable<string> Keys(...)` with `yield return` | a generator function with `yield` | `Opinions.RacePart` |
| `about.Lifespan is not double lifespan` | `lifespan = about.get("lifespan")` and `if not lifespan` | `Rulers.AgeScale` |

## 3. Exercise

1. In the generator's `leader_settings.py`, set `LIFESPAN_SCALING` to 0.5.
   Export and play 100 years: how old are the elves' and dwarves' rulers when
   they die now? Set it to 0: every people's rulers die as humans do.
2. In `faction_settings.py`, give the humans `"orc_raider": -0.4` in
   `RACE_OPINIONS`:
   `"human": {"halfling": 0.15, "elf": -0.15, "orc_raider": -0.4},`
   Export, play: do the humans go to war with the orcs more often? Search the
   book for `war on`.
