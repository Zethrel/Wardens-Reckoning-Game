# Lesson 18a: the colony screen

Until now the colony lived only in the tests. This step puts it on screen:
the founding sites on the world map, the local map with its colonists, time
you can pause and speed up, and an inspector that tells you what a tile or a
colonist is. Your choices are in the design library as Review 38: plain tiles
with symbols for now, and the start from the world map.

## Three new files

- `src/Colony/Inspect.cs` (plain .NET): what the inspector says.
- `src/UI/ColonyView.cs` (Godot): draws the local map.
- `src/UI/ColonyScreen.cs` (Godot): the screen around it: the bar, the
  inspector panel, the keys.

`Main.cs`, `MapScreen.cs`, `MapView.cs`, `MainMenu.cs`, `PauseMenu.cs` and
`Saves.cs` gained what joins them up.

## Why the inspector's words are not in the UI

The library's rule (Shared Systems §19.1) is that the screen shows the
colony and sends commands, but never changes it by looking. So the words of
the inspector are made in `Inspect.cs`, in the plain-.NET colony project:

```csharp
public sealed record Line(string Label, string Text);
public sealed record Inspection(string Title, IReadOnlyList<Line> Lines);

var shown = Inspect.Worker(colony, id);   // "Doing: going to drink", "Thirst: 70 of 100, urgent", ...
var tile = Inspect.Tile(colony, 128, 128); // "Ground: Grass, walked at 80%", "Store: The founders' carts: ..."
```

Because it is plain .NET, `tests/InspectTests.cs` can check every line,
including one test that inspects every tile and colonist and then checks the
save is byte for byte what it was before: looking changed nothing.

`Inspect.Word` turns a need's level into the word of its threshold from
`needs.json`: below `peckish` it is "fine", then "peckish", "wants",
"urgent", "critical". A **switch on conditions** like this is written with
`? :` chained:

```csharp
level >= rule.Critical ? "critical" : level >= rule.Urgent ? "urgent" : ...
```

Read it top to bottom: the first condition that holds gives the answer.

## Drawing the map (`ColonyView`)

A `Node2D` draws with `_Draw()`: Godot calls it when asked (`QueueRedraw()`),
and everything drawn there uses the node's own units. One unit is one tile, so
a tree at tile (10, 40) is drawn at (10, 40) to (11, 41), and the camera
(the world map's `MapCamera`, reused) does the zooming.

Drawing 65,536 tiles one by one each frame would be slow, so the ground is a
**picture of one pixel a tile** (an `Image`, made once), stretched by the
camera with `TextureFilterEnum.Nearest` so the squares stay crisp. Trees and
rocks are painted into it too, so a forest shows even zoomed out. `_Draw` then
draws only what is on screen, worked out from the camera:

```csharp
var view = GetViewport().GetCanvasTransform().AffineInverse() * GetViewportRect();
```

The canvas transform turns map units into screen pixels; its inverse turns the
screen's rectangle back into map units.

A node draws itself **before its children**. The ground picture is a child
(a `Sprite2D`), so at first it covered everything `_Draw` drew. The fix is one
property: `ShowBehindParent = true`.

**Not by colour alone (FP-11):** every surface also has a symbol (ripples on
water, specks on rough ground, a dash on mud), the carts and the drinking place
carry a letter (C, W), and the speed in use is named in the pause button's
tooltip as well as shown pressed in.

## Time (`ColonyScreen._Process`)

```csharp
public override void _Process(double delta)
{
    session.Frame(delta);
    ...
}
```

`_Process` runs once a frame with the real seconds since the last one. The
screen hands them to the `Session` (step 16), which turns them into ticks at
the chosen speed. The screen never runs the colony itself, so a slow or fast
computer plays the same colony.

When the in-game menu opens, Godot pauses the whole tree
(`GetTree().Paused = true`), `_Process` stops being called, and so time stops.

## Saving

The colonies have their own folder beside the world's games, and each colony
its own autosaves:

```
%APPDATA%\Godot\app_userdata\Wardens' Reckoning\colonies\
    colony-1234-year-1-month-01-day-01-0800.json      your saves
    autosaves-colony-1234\autosave-1.json ... -3      the three morning autosaves
```

A save changed outside the game asks before loading (library Review 35): a
`ConfirmationDialog` with "Load it" and "Do not load".

## How it was checked

The tests (700) pass. This time Godot itself also ran in the cloud
(software drawing, no real mouse): the main menu, your world 7 with its 24
sites, the site offer, a colony founded on site 1, saved and loaded again,
and the in-game menu, each as a screenshot. That is not a test of how it
feels to play: that part is yours.

## Exercise

1. In `ColonyView.cs`, change the colour of `"grass"` in `Colours`. Run the
   game: what else changes colour with it, and why does a tree's tile not?
2. In `ColonyScreen.cs`, the camera starts at `32` screen pixels a tile
   (`camera.Look(..., 32)`). Try 12 and 40. At which zoom do the surfaces'
   symbols appear (look for `closer` in `ColonyView._Draw`)?
3. In `needs.json`, lower thirst's `"urgent"` to 50. Inspect a colonist after
   a few hours: when does their thirst say "urgent" now? Which test in
   `InspectTests.cs` fails, and why that one?
