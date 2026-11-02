# Lesson 9b: named threats

The beasts and horrors of `threat_settings.py` (the dragon, the lich, the
vampire and the rest) now live on in the game's years, as they did in the
generator's history. Each year, after attunement's mark and before the towns
grow, every threat, in the order they rose:

1. **Sleeps or wakes.** A sleeping threat wakes when its years of sleep are
   over. An awake one may fade for good (`"fade"`), or fall asleep (`"sleep"`)
   for a number of years drawn from `"sleep_years"`.
2. **Moves.** With `"move"`, it looks for a new lair.
3. **Raids.** With `"raid"`, it raids a town within its `"reach"`, chosen at
   random. A raid is a fight, like a battle: the town's people, with
   reinforcements from its faction and the defender's advantage, against the
   threat's `"strength"`.
   - The threat wins: it kills `"deaths"` of the town's people, and the town's
     harvest the next year is `"harvest"` times what it would be.
   - The town wins: it loses half as many, and the threat is slain with a
     chance of `"slain"`, or beaten off.
   The dead lie where they fell: the god of death counts them (lesson 9a).
4. **Rises.** Then, with `RISE_CHANCE`, a new threat rises: its kind drawn
   by the kinds' `"weight"`, its lair a cell of land of the kind's `"biomes"`
   that no town holds, with a town within reach. The undead (`"undead":
   True`) look first where magic's mark is strong (lesson 8a).

A faction whose path works rituals (attunement), harmed by a threat, may in
desperation work a **great ritual** against it, as against an enemy at war
(lesson 8b): when the share of its people the threat has killed is at least
`RITUAL_DESPERATION`, with `RITUAL_CHANCE` (twice that for the most reckless
ruler), if it has metal. It spends `RITUAL_METAL` of its metal, the threat is
slain, and the land at its lair is marked for good. No one else dies.

A new threat is named from the export's names for the people holding the town
nearest its lair, unless its kind lists names of its own (`"names"`).

## 1. Run it

1. Pull the generator and export a world again (the export now holds the
   threats exactly, and names for new ones):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, play 100 years. In the Book search for `raids`,
   `slain`, `beaten off`, `makes its lair` and `sleeps`.

Your settings raise a new threat with a chance of 0.01 a year, so most
centuries have one or two threats about. In three game centuries of seed 42
at 512 there were 2 to 10 threat events (rising, raiding, being beaten off or
slain, moving, sleeping, waking), 0 to 3 of them raids won; in the
generator's own centuries of that world (histories 42, 43 and 44), 3 to 13,
1 to 6 of them raids won.

Saves are still version 8. An older save of version 8 loads: it keeps the
export's threats.

## 2. Where the code is

- **`src/Sim/Threats.cs`**: the `Threat` class (one threat), and `Threats.Year`
  (`threats.Threats.year`): waking, fading, sleeping, moving, raiding
  (`Raid`) and rising (`Rise`, with `DrawKind`, `FindLair` and `NameFor`).
  `Threats.Factor` is a raided town's harvest the year after.
- **`src/Sim/Rituals.cs`**: `Rituals.ThreatYear`
  (`rituals.year_of_threat_rituals`), the great ritual against a threat.
- **`src/Sim/YearCore.cs`**: the threats' year after attunement's, the rituals
  against them after the war rituals, and a spoiled harvest before a sign's.
- **`src/Sim/GameSettings.cs`**: `ThreatKind`, one kind of threat with the
  generator's defaults for every key you leave out.

How the tests prove it (`tests/ThreatTests.cs`): threats roll dice for
nearly everything, so `tests/fixtures/make_expected.py` gives the generator's
own `Threats.year` and `year_of_threat_rituals` a stand-in for their dice (a
fixed list of numbers, handed out in turn), with two test kinds added before
yours (a wyrm, and the ghast, an undead), a new threat every year and rituals
made likely. The game's code gets the same list, and twelve years are compared
to the last digit: every threat with every field, the names given, the
spoiled harvests and each town's harvest factor every year, the towns' people,
the dead, the mines' metal, the marks and every event. Those twelve years hold
each way a threat's year can go: rising, moving, raiding, being slain or beaten
off, fading, sleeping, waking, and three great rituals. A listed name that a
town already has is skipped. Other tests check that the undead's lairs are on
strongly marked land, that a lair keeps to its kind's biomes, that a kind is
drawn by weight exactly as Python draws it, that a raid spoils only the next
year's harvest, that without kinds no threat stirs, and that a save plays on
the same.

**One difference.** Python's `random.sample` picks the cells for a lair its
own way; the game picks them as Python does for a short list (each from
those left, the last taking its place). Both are fair draws; since the game's
dice are not Python's, the cells would differ anyway, and the tests give both
the same way of drawing.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `int? AsleepUntil` and `if (threat.AsleepUntil is int waking)` | `asleep_until = None` and `if threat.asleep_until is not None:` | `Threats.Year` |
| `(threat.Ended, threat.End) = (year, "faded")` | `threat.ended, threat.end = year, "faded"` | `Threats.Year` |
| `defence + threat.Strength > 0 && random() < ...` | `defence + threat.strength > 0 and rng.random() < ...` | `Threats.Raid` |
| `FindLair(...) is not { } lair` | `lair = ...; if lair is None:` | `Threats.Rise` |
| `mine.MinBy(i => ...)` | `min(mine, key=lambda i: ...)` | `Rituals.ThreatYear` |

## 3. Exercise

1. In `threat_settings.py`, set `RISE_CHANCE` to 0.1. Export, play 100 years:
   how many threats rise (search for `makes its lair`)? How many are slain?
2. Give the werewolf `"harvest": 0.5`. Which towns go hungry after a raid?
3. Make the zombie strong: `"strength": (5000, 9000)`. Do more towns fall
   into ruin near its lair?
4. A great ritual against a threat needs the threat to have killed at least
   `RITUAL_DESPERATION` (yours is 0.05) of a faction's people. In
   `attunement_settings.py`, set it to 0.0 and `RITUAL_CHANCE` to 1.0, export
   and play on: search for `against` to find one. Put them back afterwards.
