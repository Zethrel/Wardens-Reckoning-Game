# Lesson 8c: offshoot paths

A faction that breaks away may now found a path of its own in the game's
years, as in the generator (split_settings.py `OFFSHOOT_CHANCE`): a new school
of attunement, say, that branches from its kin's path. The game invents
nothing. The new path keeps its parent's goods, land, abilities and what
suits it, and every number about it is its parent's, moved by a draw between
`-PATH_SPREAD` and `PATH_SPREAD`:

- its ritual factor (1 for your own paths), which multiplies the chances of
  its factions' great and harvest rituals;
- `METAL_NEED`, `PATH_HARVEST` and `PATH_STEADINESS` (faction_settings.py);
- `PATH_OPINIONS`: what it thinks of every other path and of itself, and what
  every other path thinks of it;
- `PATH_BLAME` (attunement_settings.py).

It is named from `PATH_NAMES` (faction_settings.py) for its root, the first
path of its family, skipping a name that is a path already; when they run out,
"&lt;the faction's name&gt; way", then "... way 2". Any faction may later take
it up, as paths change today (lesson 4), and learn it (lesson 7).

A faction that founds a path may then take a **stance** towards every other
faction (split_settings.py `STANCES`): one draw against each stance's
`"chance"`, in order; it then remembers every other faction by the stance's
`"memory"` more (or less) kindly.

Your settings have `OFFSHOOT_CHANCE` 0 and every stance's chance 0, so none of
this happens until you raise them.

## 1. Run it

1. In the generator's `split_settings.py`, set `OFFSHOOT_CHANCE` to 0.5,
   `PATH_SPREAD` to 0.2, and the `"hostile"` stance's `"chance"` to 0.3.
   Optionally give a root path names in `faction_settings.py`, such as
   `PATH_NAMES = {"attunement": ["Ashen Choir", "Deep Hum"]}`.
2. Export: `python worldgen.py --seed 42 --history 42 --size 512 --export`.
   The generator's own history now founds paths too, and the export carries
   them.
3. In Godot, F5, open it, play 100 years and search the Book for `found a
   path` and `towards all others`. Put the settings back afterwards
   (`git checkout -- north/split_settings.py north/faction_settings.py`).

With those settings (and no `PATH_NAMES`), seed 42's history founded 32
paths, 17 of them in its last century, and took 10 stances; three game
centuries founded 17 or 18 and took 5 or 6, offshoots of offshoots among
them, and factions took them up: by year 400 the Vernier Guild followed one.

Saves are still version 8; they now hold the paths the game founded and their
values. A save of 8b loads (it founded none).

## 2. Where the code is

- **`src/Sim/GameSettings.cs`**: `FoundPath` (the generator's
  `paths.Book.found`) draws the new path's values in the generator's order and
  names it (`PathNameFor`); `AddPath` puts a path into every table by path.
  `Founded` lists the paths the game founded (a save writes them, and puts
  them back with `AddPath`). `RitualFactor` reads a path's factor.
- **`src/Sim/Splits.cs`**: `Vary` founds the path (after the traits, belief
  and government, as `newcomers.vary`), `Settle` takes the stance (after the
  parting memories are set, as `newcomers.settle`). Both are told after the
  split itself.
- **`src/Sim/Rituals.cs`**: `ByPath` multiplies a ritual's chance by the
  path's ritual factor (`rituals.by_path`).

**Why the settings change now.** Until this step the game's settings were
read once from the export and never changed. A founded path has to be in
every table by path (opinions, metal, harvest...), so `AddPath` adds to
them. Each game has its own `GameSettings`, so two games never share a path.

How the tests prove it (`tests/OffshootTests.cs`): with `PATH_SPREAD` 0 no
value moves, so `tests/fixtures/make_expected.py` founds three paths with the
generator's own `paths.Book` (named Stonehand from `PATH_NAMES`, skipping
"attunement", which is a path already; then "Kilnrow way" and "Kilnrow way
2") and the tests compare every table's entry for each, the opinions both
ways included. They also check what must follow where dice decide: values move
by at most `PATH_SPREAD` and stay in their bounds; a breakaway with
`OFFSHOOT_CHANCE` 1 founds one, of its kin's root, told after the split; a
founder takes a stance that moves its memories; none of it without
`OFFSHOOT_CHANCE`; a ritual factor of 0.5 halves a ritual's chance; and a save
keeps the founded paths and plays on the same.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `Func<double> draw` and `() => dice.Uniform(-s, s)` | passing a function (a `lambda`) | `GameSettings.FoundPath` |
| `double? metal = ... ? Moved(m, 0.0) : null` | `metal = moved(m, 0.0) if ... else None` | `GameSettings.FoundPath` |
| `((Dictionary<string, T>)table)[key] = value` | (none: C# must be told the table can change) | `GameSettings.AddPath` |
| `stance is not { } taken` | `if stance is None:` | `Splits.Settle` |

## 3. Exercise

1. With the settings of "Run it", set `PATH_SPREAD` to 0.6. Do the new
   attunement paths work more rituals than the old one, or fewer? Search the
   book for `great ritual` and `harvest ritual`.
2. Set the `"friendly"` stance's `"chance"` to 1 and the others to 0. Do
   founders go to war less often?
3. In `GameSettings.FoundPath`, what would change if the row of opinions were
   drawn after the column? (The values would be the same; which numbers each
   draw goes to would not.)
