# Lesson 2: the years run

The game now runs the years on from where the generator's history ended: each
year every town gets its harvest, eats what its land grew, grows or starves,
changes size, and may fall into ruin. You can save the game and load it again.

## 1. Run it

1. In the generator's folder, export a world again (the export now holds what
   the game needs to go on, under `"continue"`):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, press **F5**, **Open a world...**, and pick its `world.json`.
3. Press **Next year** (or **N**) to run one year, **Play** (or **Space**) to
   run them one after another. The slider now reaches past the generator's
   last year: drag it back to see any year, the game's too.
4. **Esc**, **Save**. Then **Main menu**, **Load a game**.

What is *not* in the game's years yet: trade (until step 3 every town lives on
its own land, so towns that lived on bought food go hungry), rulers coming and
going, war, splits and everything after them in the plan (`docs/plan.md`).

## 2. Where the code is

- **`src/Sim/GameSettings.cs`** reads the generator's settings from the export.
  `GROWTH_RATE` in your `history_settings.py` becomes `GrowthRate` here: change
  it in Python, export again, and the game uses the new value.
- **`src/Sim/Game.cs`** holds everything that changes from year to year: each
  town's people, size, low years and hungry years; each faction's people.
- **`src/Sim/YearCore.cs`** runs one year. Each part is named after the Python
  function it comes from in the generator's `north/history.py`: put them side
  by side. `Grow` and `grow`, `KeepBand` and `keep_band` are the same lines.
- **`src/Sim/Noise.cs`** is the generator's `noise.py` value noise, used for
  the weather. The test `The_weather_is_the_generator_s_noise` checks it gives
  the same numbers as the Python.
- **`src/Sim/SaveFile.cs`** writes and reads a save: a JSON file in
  `%APPDATA%\Godot\app_userdata\Wardens' Reckoning\saves`. It keeps where the
  export is, so moving or re-exporting that world makes the save refuse to load
  (and say why).

How the tests prove the C# is right: `tests/fixtures/make_expected.py` works
out one ordinary year for every town of the test world with the generator's
own Python functions; `One_year_s_core_agrees_with_the_generator_s_functions`
makes the C# find the same. And
`Saving_then_loading_plays_on_as_if_never_stopped` runs 40 years straight, and
25 + save + load + 15, and checks both end the same.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `town.Hungry = fed < 0.999 ? town.Hungry + 1 : 0;` | `hungry = hungry + 1 if fed < 0.999 else 0` | `RunYear` |
| `Math.Max(0.0, x)` | `max(0.0, x)` | `Grow` |
| `town.Yields.GetValueOrDefault(good)` | `town["yields"].get(good, 0)` | `FedShare` |
| `public double People { get; set; }` | an attribute anyone may read and change | `TownNow` |
| `File.Move(fresh, path, overwrite: true)` | `os.replace(fresh, path)` | `SaveFile.Write` |

## 3. Exercise

1. In the game's **Settings**, set the years a second to 10, and Play. How many
   towns are hungry after 50 years, and do any fall? (Without trade, a town that
   lived on bought food has only its own land.)
2. In the generator's `history_settings.py`, set `STARVATION = 0.02`. Export
   again, open it, and play 50 years: what changes? Set it back.
3. In `src/Sim/YearCore.cs`, change `SizeMargin` to `0.0` and run
   `dotnet test`: which test catches it? (Then think about why the margin is
   there.) Change it back.
