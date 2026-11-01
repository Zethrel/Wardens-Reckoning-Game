# Lesson 4: rulers, grievance and opinions

Factions now live on in the game's years: rulers age, die and are followed by
heirs or by whoever the government's succession chooses; hardship builds
grievance, and too much of it brings revolution; rulers pull their faction's
ways, may turn it to another path, and warm or sour it toward others; and every
faction's opinion of every other is worked out again each year. Your rules from
`history_rules.py` (famine, disease, the greedy faction's grumbling...) run too.

## 1. Run it

1. Export a world again in the generator (the export now holds names for the
   rulers the game crowns, and each faction's held paths):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 50 or 100 years, and open a faction's history
   in the **Book**: successions, revolutions, changes of path, and how it now
   sees the others ("now see the ... as wary").

## 2. Where the code is

- **`src/Sim/Rulers.cs`**: the generator's `north/leaders.py`, function by
  function: `NewRuler` (`new_ruler`), `Year` (`year_of_rulers`), `Vacuum`,
  `ChangePath`, `DriftPrejudice`, `PathFit`.
- **`src/Sim/Opinions.cs`**: `factions.opinion` and its parts (path, metal,
  border, envy, belief), and the words a faction uses for another.
- **`src/Sim/Rules.cs`**: your rules. They are Python code in the generator,
  so the game cannot read them from the export: **when you change a rule in
  `history_rules.py`, change it here too** (or ask Claude to).
- **`src/Sim/Dice.cs`**: the game's dice. Each system has its own
  (`game.DiceFor("rulers")`), made from the game's seed and the system's name,
  and a save keeps where each one is, so a loaded game rolls on as it would
  have.
- **`src/Sim/YearCore.cs`**: the year now adds up each faction's hardship
  (hunger, shortages, towns lost) for its grievance, runs your rules on every
  town, and calls the opinions and the rulers.

How the tests prove it: where the generator rolls no dice (opinions, how well
a path fits a people, how far a ruler is from the people's ways), the tests
compare with the generator's own functions (`tests/RulerTests.cs`). Where it
rolls dice, the tests check what must follow: an heir is near the old ruler,
a charismatic ruler of another path turns the faction, a starving year angers
the people more, and the same seed plays out the same.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `public delegate RuleEffect? SettlementRule(PlaceView place, Func<double, bool> chance);` | a function's shape, so a list can hold rules | `Rules.cs` |
| `("famine", (place, chance) => ...)` | a `lambda` | `Rules.Settlement` |
| `ulong` and `unchecked(...)` | a number that wraps around instead of growing | `Dice.cs` |
| `record Felt(double Hunger, ...)` | a small named tuple | `Rulers.Felt` |

## 3. Exercise

1. In the generator's `leader_settings.py`, lower a government's
   `"tolerance"`. Export, play 100 years: are there more revolutions in the
   book?
2. In `src/Sim/Rules.cs`, make `disease` strike one year in a hundred instead of
   one in three hundred and thirty (`chance(0.01)`). Play: what happens to the
   cities? Change it back, and keep the generator's rule the same as the game's.
3. Open the save file (`%APPDATA%\Godot\app_userdata\Wardens' Reckoning\saves`)
   in VS Code and find `"dice"`: the four numbers each system's dice are at.
