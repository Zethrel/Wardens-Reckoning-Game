# Lesson 1: the first screen

The game opens a world the generator exported and shows it: the terrain, the
borders and the towns of any year, a main menu and a menu during play. No
years run yet; that is step 2.

## 1. Run it

1. In the generator's folder (PowerShell):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. Start Godot 4.7 (.NET), choose **Import**, and pick this folder's
   `project.godot`. Godot builds the C# the first time.
3. Press **F5** (Run Project). Choose **Open a world...** and pick
   `output\world-42\h42\export\world.json` in the generator's folder.

On the map: the mouse wheel zooms towards the mouse, the right or middle
button drags the map, W A S D and the arrow keys move it, the slider at the
bottom picks the year, and **Esc** opens the menu (the game pauses).

## 2. Two kinds of code

Open the folder in VS Code. The code is in two places, on purpose:

- **`src/World`**: plain C#, no Godot. It reads the export and works out what
  a year looks like. Because it needs no engine, `dotnet test` can check it
  in a second, and it can be checked against the generator's Python.
- **`src/UI`**: Godot. Every class here is a Godot node (`Node`, `Control`,
  `Node2D`...) and only draws what `src/World` worked out.

The simulation (`src/Sim`, from step 2) will be plain C# too, for the same reason.

## 3. Reading the export: `src/World/ExportedWorld.cs`

Compare it with the generator's `north/world_file.py`: it is the same reader.
`Town`, `Faction` and `Borders` follow the Python line by line. The tests
prove they agree: `tests/fixtures/make_expected.py` runs the *Python* reader
on a small exported world and writes down what it finds
(`tests/fixtures/expected.json`); `tests/WorldTests.cs` makes the *C#* reader
find the same.

`Grid.cs` reads the grey PNG grids, the same steps as `read_grid` in Python:
find the chunks, unzip the pixels (`ZLibStream`), undo each row's filter.

C# you will meet, next to the Python you know:

| C# | Python | Where |
| --- | --- | --- |
| `int? holder = null;` | `holder = None` (the `?` lets an `int` be null) | `Town` |
| `foreach (var (since, faction) in town.Owners)` | `for since, faction in town["owners"]:` | `Town` |
| `record TownState(int? Holder, int People, bool Ruined)` | a small class like a named tuple | top of `ExportedWorld.cs` |
| `list.Where(...).OrderByDescending(...)` (LINQ) | a list comprehension and `sorted(..., reverse=True)` | `MapYear.Towns` |
| `(int)data["size"]!` | `data["size"]` (the `!` says "it is there", the `(int)` says what it is) | the constructor |
| `public static` | a function on the class, not on one object | `MapYear` |

## 4. Drawing: `src/UI`

- **`Main.cs`** is the game's first node. It shows the main menu and swaps in
  the map when a world opens.
- **`MapView.cs`** draws one year. The terrain is `terrain.png` as a sprite;
  the borders are a picture the size of the map, filled again whenever the
  year changes (`Pictures.BordersInto`); the towns are drawn on a node of
  their own, added last, because a Godot node draws itself *before* its
  children (otherwise the terrain would cover them).
- **`MapCamera.cs`** moves and zooms.
- **`PauseMenu.cs`** pauses the game with `GetTree().Paused = true`. Its
  `ProcessMode = Always` keeps the menu working while everything else stops.
  Save and Load are there but switched off: nothing changes yet, so there is
  nothing to save.
- **`Settings.cs`** keeps the settings in `settings.cfg` in Godot's user
  folder (`%APPDATA%\Godot\app_userdata\Wardens' Reckoning\`).

The screens are built in C# rather than in Godot's scene editor, so a whole
screen can be read in one file.

## 5. Exercise

1. In the game's **Settings**, drag **Border strength** to the right. Which
   strength lets you read both the borders and the land?
2. In `src/UI/MapCamera.cs`, set `ZoomStep = 1.5f`. How does the mouse wheel
   feel now? Set `Largest = 10f`: how far can you still zoom in?
3. In `src/World/MapYear.cs`, `SpriteSide` draws pictures at least 10 map
   cells wide. Make it 20 and open a 512 world: what happens where towns
   stand close together?
4. Run `dotnet test` in the game's folder. Now change `3 => (left + up) / 2,`
   in `src/World/Grid.cs` to `3 => (left + up + 1) / 2,` and run it again:
   which test catches it? Change it back.
