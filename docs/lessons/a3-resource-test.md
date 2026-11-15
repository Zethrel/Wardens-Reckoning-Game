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

## Exercise

1. In `AssetPreview.Resources.cs`, change `BoardHalf` from 10 to 12. What
   moves, and why?
2. Change the 10 in `DrawString` to 16. How do the names look at 16 pixels a
   tile, and at 64?
3. Remove `DrawSetTransform(Vector2.Zero);`. What happens to the tree and the
   colonist at the top, and why?

Put each change back afterwards.
