# Lesson A3: the resource test

Fourteen kinds of goods arrived as test pictures, each in four states (small,
medium, large and full), for the piles and containers of Review 45. This step
puts them in the asset preview, so you can judge them beside the trees at the
game's zoom levels before any of them goes into the game.

Open `src/UI/AssetPreview.tscn` and press F6 in the editor; the editor builds
the code and imports new pictures for you. Or run in PowerShell in the game's
folder:

```powershell
dotnet build
& "D:\Zethrel-Github-Projects\_Needed stuff\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path . --import
& "D:\Zethrel-Github-Projects\_Needed stuff\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --path . res://src/UI/AssetPreview.tscn
```

- **`dotnet build`:** compiles the C#. Started from the command line, Godot
  runs the last build it finds and doesn't compile anything itself.
- **`--import`:** brings new pictures into Godot's own folder (`.godot/`).
- **The last line:** opens the preview.

- **K** goes to the board and back.
- **J** switches between the compact pictures (32 pixels a tile, like the
  trees) and the fine ones (64).
- **L** shows the refined pictures (TEST v0.2) of stone, grain, flour,
  vegetables and fruit in place of the first ones (step A3b).
- **U** takes the white outline off TEST v0.3's pictures, and the release
  candidate's, in the rows at the bottom of the board (steps A3c and A3d).
- **1** to **4** zoom to 16, 32, 40 or 64 pixels a tile.

## Why the pictures are shrunk ahead of time

The crops are 100 to 340 pixels wide, ten times what one tile shows at the
game's usual zoom. Godot could shrink them while drawing, but with crisp
(nearest) filtering it skips most of the pixels. The result shimmers and
loses detail, as the first high-detail trees did (step A1h). So the pictures
in `assets/prototype-resources-v0.1/` were shrunk once, carefully, and are
drawn at their own size, as the v3 trees are. The README in that folder says
how.

## Drawing a picture on a tile

```csharp
float wide = texture.GetWidth() / pixels, tall = texture.GetHeight() / pixels;
float middle = left + 1 + s + 0.5f;
DrawTextureRect(texture, new Rect2(middle - wide / 2, bottom - tall, wide, tall), false);
```

- **Size:** `pixels` is 32 or 64, the picture's pixels a tile, so a 32-pixel
  picture is one tile wide, and so is a 64-pixel one drawn as "fine".
- **Place:** the picture is centred across its tile (`middle - wide / 2`) and
  stands on the tile's bottom edge (`bottom - tall`), as goods will lie on a
  stockpile tile.

`Rect2(x, y, width, height)` is a rectangle, like a tuple of four numbers in
Python.

## Writing small text in the world

The names under the rows are drawn in the world, so they zoom with
everything else. A font size is in pixels, and in the world one unit is a
whole tile, so a size of 10 would be ten tiles tall. So the drawing is
shrunk first:

```csharp
DrawSetTransform(new Vector2(left + 1, bottom + 0.4f), 0, new Vector2(1 / 32f, 1 / 32f));
DrawString(font, Vector2.Zero, name, HorizontalAlignment.Left, -1, 10, colour);
DrawSetTransform(Vector2.Zero);
```

- **Shrink:** `DrawSetTransform` moves and shrinks everything drawn after it,
  so a font pixel is 1/32 of a tile.
- **Put back:** the last line restores the usual drawing for what follows.

## What to look at

1. **Readability:** at 16 and 32 pixels a tile, can you tell timber from
   lumber, and iron ore from copper ore?
2. **Edges on dark ground:** do the light fringes left by the cut-out
   background show? They do most on the crates' edges.
3. **The four states:** do they step up clearly from small to full?
4. **Size:** does a full pile next to the tree and the colonist look the
   right size for one tile, one metre square?
5. **Detail:** is 32 pixels a tile enough, or does 64 (J) at the higher zooms
   look worth twice the drawing work?

## Step A3b: more batches, and a second version

Batches 4 to 6 add building materials, metals and tools, and more storage:
28 kinds in all, two to a row. The refinements of batches 2 and 3 (TEST v0.2)
redraw five kinds. Rather than replace the first pictures, the board can show
either, so you can compare them:

```csharp
Texture2D? ResourcePicture(string kind, string state)
{
    string name = (fine ? "fine" : "compact") + "/" + kind + "_" + state;
    return refined && RefinedKinds.Contains(kind) ? Picture("v0.2/" + name) : Picture(name);
}
```

`RefinedKinds.Contains(kind)` asks whether the kind is in the list, as
`kind in refined_kinds` does in Python.

## Step A3c: containers and their contents apart

TEST v0.3 draws the containers empty and their contents on their own, so the
game could put any contents in any container. The board's last row lays them
together, as one picture made ahead of time. In the game it would be two
pictures, the container and then the contents, each with a `DrawTextureRect`:

```csharp
DrawTextureRect(basket, basketRect, false);
DrawTextureRect(fruit, fruitRect, false);   // drawn later, so it covers the basket
```

What is drawn later covers what was drawn before, as in the colony's four
passes (lesson 27b). That's why the fruit covers the basket's front rim. A
true layered picture needs three pieces: the container's back, the contents,
and the container's front over them.

## Step A3d: three layers

The release candidate adds a front piece for each container, so the game can
draw a container in three layers, each a picture of the same size at the same
place:

```csharp
new[] { l.Container + "_base", l.Container + "_with_" + l.Contents, l.Container + "_front" }
```

The board draws that list in order, so the front piece covers the bottom of
the contents, as the basket's rim would. The row under it leaves the front
out, to compare.

`SelectMany` in the same file turns a list of lists into one list, like a
nested loop in a Python list comprehension:
`[p + "_" + s for p in piles for s in states]`.

## Step A3e: the peoples

The peoples' first pictures come in two sizes, 48 × 48 and 64 × 64. If both
were drawn at so many picture pixels a tile, the 64 would be a bigger person.
The question is the other one: at the same size in the world, does the 64
look better? So `AssetPreview.People.cs` works out the pixels a tile from the
canvas:

```csharp
const float WorldCanvas = 1.5f;                // tiles across either canvas
float pixels = size / WorldCanvas;             // 48 / 1.5 = 32, 64 / 1.5 = 42.7
```

and draws the picture `size / pixels` tiles wide, which is 1.5 for both.

Each picture stands on its feet: the lowest solid row, measured once and kept
in `PeopleFeet`. A `Dictionary` is Python's `dict`; `["human_48"] = 38` is
`"human_48": 38`.

In the small wood the trees and the peoples go into one list, and the list is
drawn in the order of where their feet are:

```csharp
foreach (var (_, draw) in standing.OrderBy(s => s.Feet))
    draw();
```

`OrderBy(s => s.Feet)` is Python's `sorted(standing, key=lambda s: s.feet)`.
Whatever stands further south is drawn later, so it covers what stands behind
it. Each `draw` is a lambda kept in the list, called only now.

The single humans (steps A3f and A3g) have canvases of 2 : 3, 32 × 48 or
48 × 72. `DrawHuman` draws any of them 1 by 1.5 tiles, with the bottom edge
on the feet, so a 48 × 72 picture is the same size in the world as a 32 × 48
one. They are listed in `Humans`, an array of pairs (a Python list of
tuples): the name in the preview and the file. The A3f human stands left of
the colonist, style C's two right of it, and the resolution trial's two (step
A3h) after a gap. A new picture needs only a line in `Humans` and a
`DrawHuman` call.

Press **Q** to go to the peoples and back, **Z** to switch the lower rows and
the wood between 48 and 64, and **A** to see each one's feet.

## Step A3i: clothing in layers

The clothing test (C) draws each person from up to five pictures stacked on
top of each other, the way the game would dress a colonist:

```csharp
foreach (string? layer in new[] { "neutral", outfit.Lower, outfit.Upper, "boots", "head" })
```

`string?` is a string that may be `null` (Python's `None`): the bare body has no
trousers or tunic, so those entries are `null` and skipped with `continue`.
Every layer is drawn into the same 1 × 1.5-tile rectangle, so the pictures line
up pixel for pixel. The head comes last, so a dwarf's beard always hangs over
the tunic.

An outfit is a tuple of names, like `("tunic", "runtime_reference_lower",
"alternate_upper")`: Python's `("tunic", "...", "...")`. Swapping a garment is
only choosing another name.

## Exercise

1. In `AssetPreview.Resources.cs`, change `BoardHalf` from 10 to 12. What
   moves, and why?
2. Change the 10 in `DrawString` to 16. How do the names look at 16 pixels a
   tile, and at 64?
3. Remove `DrawSetTransform(Vector2.Zero);`. What happens to the tree and the
   colonist at the top, and why?
4. In `AssetPreview.People.cs`, change `WorldCanvas` to 1. How big are the
   peoples now against the tree, and do the 48 and the 64 still match?
5. Change the elf's `["elf_48"] = 45` to 38. Press A. Where does the elf
   stand now?
6. In `AssetPreview.Clothing.cs`, move `"head"` to the start of the layer
   list in `DrawDressed`. Press C. What happens to the dwarf's beard, and why?

Put each change back afterwards.
