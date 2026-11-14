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

## Exercise

1. Rename `tree_02.png` for a moment and run again: what does the bench show,
   and what does the panel say? (Name it back.)
2. In `Variant`, return `1` always. What does the grass look like now, and why
   do variants matter?
3. Change the walker's loop so it passes *through* a tree's tile. What would the
   game need to stop that (hint: lesson 15a)?
