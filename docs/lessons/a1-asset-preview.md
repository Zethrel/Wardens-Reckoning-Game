# Lesson A1: the asset preview

Your first pictures (asset prototype v0.2) need trying in Godot before the
game uses them. This step builds a **test bench**: a scene of its own that
shows them, and nothing else. The game never loads it, so nothing in the
colony, the simulation or the main screen can change.

## Running it

In PowerShell, in the game folder:

```powershell
dotnet build
```

Then, in the Godot editor, open `src/UI/AssetPreview.tscn` and press **F6** (Run
Current Scene). Opening the project in the editor also *imports* the new
pictures (see below). Without the editor:

```powershell
& "<your Godot 4.7 .NET exe>" --path . --headless --import
& "<your Godot 4.7 .NET exe>" --path . res://src/UI/AssetPreview.tscn
```

## A scene made in code

`AssetPreview.tscn` holds one node with one script, as `Main.tscn` does:

```
[node name="AssetPreview" type="Node2D"]
script = ExtResource("1")
```

Everything else is made by `AssetPreview.cs` in `_Ready`: the camera, the
information panel (in the Ledger look, lesson 26b), and the pictures.

## Imported, not read from disk

```csharp
Texture2D? texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
```

The world map reads the generator's pictures straight from disk
(`Image.LoadFromFile`). These are loaded through Godot's **import system**:
when the editor first sees `assets/prototype/grass_01.png`, it writes
`grass_01.png.import` beside it (how to import it) and a ready-made copy in
`.godot/`. That's how pictures get into an exported game, and it is the
point of trying it here. Commit the `.import` files Godot makes.

A picture that isn't there is `null`, and the bench draws today's shape
instead and lists it as missing: the fallback the game will keep.

## One tile, one unit

As in the game, a tile is one unit on the map. A picture 32 pixels wide covers
one tile, so a picture is drawn `width / 32` units wide:

```csharp
DrawTextureRect(texture, new Rect2(foot.X - 16f / Pixels, foot.Y - 64f / Pixels, texture.GetWidth() / (float)Pixels,
                                   texture.GetHeight() / (float)Pixels), false);
```

That puts the tree's pixel (16, 64), just under the trunk's foot, on `foot`:
the middle of its tile's bottom edge.

## Who is in front

A tree is two tiles tall, so it covers part of the tile above it. Whoever
stands lower on the screen must be drawn **after**, to be in front. The bench
collects trees and colonists with where their feet are, and draws them in
that order:

```csharp
foreach (var (_, draw) in standing.OrderBy(s => s.Feet))
    draw();
```

`standing` holds pairs of a number (the feet's y) and an `Action`: a small
piece of code kept to be run later. The walker's loop passes behind two trees
and in front of a third; if the order were wrong, you'd see them on top of
the crowns.

## Variants without dice

```csharp
static int Variant(int x, int y) { ... h % 4 ... }
```

Which grass picture a tile shows is mixed from its x and y alone. The same tile
always shows the same picture, and no colony dice are used, so the game's
simulation can never be changed by how the ground looks.

## What to look for

1. **2 (32 px a tile):** crisp pixels, no grid in the grass, trees standing on
   their tiles (**A** shows the anchors), the walker behind and in front of
   the right trees.
2. **3 (40 px) and the wheel:** uneven pixels? Move with the arrows: does it
   shimmer? Does **P** (snap to whole pixels) cure it?
3. **4 (64 px):** clean double pixels.
4. **F:** smooth filtering: soft, or acceptable?
5. **The scale:** trees about 1.75 m next to a 1.1 m colonist. Too small?
6. **The frame time** in the panel.

## A second set of trees (v0.3)

The vegetation prototype v0.3 sits beside v0.2 in `assets/prototype-v0.3/`,
and **T** switches between them. Nothing of v0.2 is replaced, so the two can be
compared on the same field. **V** steps through the variants (green, a second
green, autumn, winter; the conifer has two), **Y** through the ages (mature,
young, sapling).

The pictures are of different sizes (64 × 96, 32 × 96, 32 × 64, 32 × 32), so
the code no longer says "16" for a tree's middle. It works it out:

```csharp
new Rect2(foot.X - texture.GetWidth() / 2f / Pixels, foot.Y - texture.GetHeight() / (float)Pixels, ...)
```

Half the picture's width to the left of the foot, its whole height above it.
For v0.2 (32 × 64) that is exactly what it was before. Press **A**: where a
v0.3 trunk is not in the middle of its picture, the cross shows it.

## A third set (v0.4)

v0.4's two trees sit in `assets/prototype-v0.4/`, and **T** now goes round
three sets: v0.2, v0.3, v0.4. Two sets have pictures of the same name
(`tree_mature_conifer_01`), so each picture is kept under a name of its own:

```csharp
LoadAs("v0.4/tree_mature_" + type + "_01", Vegetation4 + "tree_mature_" + type + "_01.png");
```

`LoadAs(name, path)` keeps the picture at `path` under `name`. `Load` is now
one line that calls it: one way of loading, used two ways.

## A fourth set (v0.5)

v0.5 revises v0.4's two trees, and sits beside them in
`assets/prototype-v0.5/`. **T** goes round four sets now. Sets from v0.4 on
have the same two file names, so they share one line of code:

```csharp
return Picture(Sets[treeSet] + "/tree_mature_" + (kind == 1 ? "deciduous" : "conifer") + "_01");
```

`Sets[treeSet]` is the set's name ("v0.4", "v0.5"), which is also the name the
pictures were kept under. A fifth set of the same kind would need only its
folder, its two `LoadAs` lines and its name in `Sets`.

## A fifth set (v0.6), and a loop instead of lines

With v0.6 the sets from v0.4 on are loaded by a loop over their names:

```csharp
foreach (var set in Sets.Skip(2))
    foreach (var type in new[] { "deciduous", "conifer" })
        LoadAs(set + "/tree_mature_" + type + "_01", "res://assets/prototype-" + set + "/vegetation/tree_mature_" + type + "_01.png");
```

`Sets.Skip(2)` is every name in `Sets` but the first two (v0.2 and v0.3, which
are laid out differently). A new set of the same kind now needs only its
folder and its name added to `Sets`: when the same few lines keep being
copied, a loop says it once. And `Sets[^1]`, used in the panel's help line, is
the last name in the list (`^1` counts from the end).

## A sixth set (v0.7): one word

v0.7 is the first set added the way the loop promised: its folder,
`assets/prototype-v0.7/`, and one more name in `Sets`:

```csharp
static readonly string[] Sets = { "v0.2", "v0.3", "v0.4", "v0.5", "v0.6", "v0.7" };
```

Nothing else changed. The loop loads its two trees, **T** goes round six sets,
and the help line reads "v0.2 to v0.7" by itself, since it shows `Sets[^1]`.

v0.8 came the same way: its folder, and `"v0.8"` at the end of `Sets`.

## An experiment beside the sets (high-detail v1)

The high-detail trees aren't another version of v0.8. Their files end in
`_hd_01`, and they are twice the size (128 × 192 and 64 × 192). So they get a
list of their own, beside `Sets`:

```csharp
static readonly (string Name, string Folder, string Suffix)[] Experiments = { ("high-detail v1", "hd-v1", "_hd_01") };
```

Each entry is a **tuple**: three values in one, with names (`e.Name`,
`e.Folder`, `e.Suffix`). **T** now counts on past the last set:
`treeSet` 0 to 6 are the sets, 7 is the first experiment. `Experiment` says
which:

```csharp
bool Experiment => treeSet >= Sets.Length;
```

`=>` here makes a **property** that is worked out each time it is read, like a
small method without brackets.

The size comes from the picture itself: `Pixels` (32) picture pixels make a
tile, so a 128-pixel-wide tree covers 4 tiles. **H** halves that for the
experiment, so its trees cover what v0.8's do:

```csharp
float scale = Experiment && halfSize ? 0.5f : 1f;
float wide = texture.GetWidth() * scale / Pixels, high = texture.GetHeight() * scale / Pixels;
```

At half size with nearest filtering, every other picture pixel is simply
skipped, so fine detail turns into noise. Compare it with F (smooth). Shrinking
a picture well is the artist's job, done once when the file is made, not the
game's on every frame.

## Two sizes, and a test wood (high-detail v2)

v2 comes in two sizes: **compact** pictures (64 × 96 and 32 × 96, the same as
v0.8's) and **full** ones (128 × 192 and 64 × 192). So an experiment now names
both ends of its file names, with `""` for "none":

```csharp
("high-detail v1", "hd-v1", "_hd_01", ""),
("high-detail v2", "hd-v2", "_hd_v2_full", "_hd_v2_compact"),
```

**H** sets `small`. For v2 that picks the compact pictures, drawn at their own
size. v1 has no compact pictures, so for v1 it still shrinks the full ones to
half while drawing:

```csharp
string end = small && e.Compact != "" ? e.Compact : e.Full;
```

`a ? b : c` is C#'s short "if": if `a`, then `b`, else `c`.

**W** fills a wood of 120 × 68 tiles below the fields, which is what a
1920 × 1080 screen shows at 16 px a tile. `WoodTrees` hands out its trees one at
a time:

```csharp
IEnumerable<(int X, int Y, int Kind)> WoodTrees()
{
    ...
                yield return (x, y, (x * 7 + y * 13) % 3 == 0 ? 2 : 1);
}
```

`yield return` makes a method that gives its results one by one, as they're
asked for (like a Python generator). `WoodTrees().Count()` counts them.

How long the drawing takes is measured twice:
- **Drawing (CPU):** `_Draw` notes `Time.GetTicksUsec()` (microseconds since the
  game started) at its start and end. That's the time spent handing Godot the
  pictures to draw.
- **Rendering (CPU and GPU):** `RenderingServer.ViewportSetMeasureRenderTime`
  asks Godot to time the rendering, and `ViewportGetMeasuredRenderTimeGpu`
  reads how long the graphics card took. Bigger pictures cost the graphics card
  more, so this number is where full and compact sizes can differ.

The frame time in the panel can't go below your screen's refresh (16.7 ms at
60 Hz) while vsync is on, so read the drawing and rendering times instead.

## Exercise

1. Rename `tree_02.png` for a moment and run again: what does the bench show,
   and what does the panel say? (Name it back.)
2. In `Variant`, return `1` always. What does the grass look like now, and why
   do variants matter?
3. Add a second entry to `Experiments` that points at a folder that doesn't
   exist. What does the panel's "Missing" line show when T reaches it?
4. Make the test wood half as wide (`WoodWide = 60`). How does the drawing time
   change with a tree on every tile?
5. Change the walker's loop so it passes *through* a tree's tile. What would the
   game need to stop that (hint: lesson 15a)?
