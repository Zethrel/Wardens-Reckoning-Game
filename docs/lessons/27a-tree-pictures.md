# Lesson 27a: the trees as pictures

Until now the colony drew every tree as a dark green triangle. This step draws
the adopted v3 trees instead (Vegetation Art Standard v1.1): the 64 × 96
deciduous tree and the 32 × 96 conifer. Further out, or if the pictures can't
be found, the triangles are still there.

Three files do the work:

- `assets/vegetation/`: the two pictures and `manifest.json`, which says where
  each tree's foot is and where its crown ends;
- `src/Colony/Canopy.cs`: reading that manifest, and choosing which tree a
  tile shows (plain .NET, so the tests can check it);
- `src/UI/ColonyView.cs`: drawing them.

## The manifest

```json
{
  "kind": "deciduous",
  "file": "tree_mature_deciduous_01.png",
  "width": 64, "height": 96,
  "foot": [32, 95],
  "crown_line": 76,
  "crown_bounds": [1, 12, 62, 64]
}
```

The numbers are pixels in the picture, counted from its top left corner.

- **`foot`** is the point that stands on the tile: pixel (32, 95) is the middle
  of the bottom row.
- **`crown_line`** is the first row of the trunk. Everything above it is
  crown, which canopy fading will need (lesson A2).
- **`crown_bounds`** is the crown's box: x, y, width, height.

`TreeArts.Read` turns the file into one `TreeArt` per tree. A `record` is a
small class that only holds values, like a Python `dataclass(frozen=True)`:

```csharp
public sealed record TreeArt(string Kind, string File, int Width, int Height, int FootX, int FootY, int CrownLine,
                             int CrownX, int CrownY, int CrownWidth, int CrownHeight)
```

## Which tree a tile shows

The simulation knows only "tree". You chose to mix the two pictures by tile,
about one conifer in three, without changing the simulation:

```csharp
public static string KindAt(int x, int y)
{
    uint h = unchecked((uint)x * 73856093u ^ (uint)y * 19349663u);
    h ^= h >> 13;
    h = unchecked(h * 0x5bd1e995u);
    h ^= h >> 15;
    return h % 3 == 0 ? "conifer" : "deciduous";
}
```

This mixes the tile's two numbers into one, called a *hash*, and takes it
modulo 3.
- **Same tile, same tree:** the same tile always gives the same number, so it
  shows the same tree in every game and on every PC.
- **`unchecked`** tells C# that numbers too big for a `uint` should simply wrap
  around, as they do in C. Without it, the multiplication might stop with an
  overflow error.
- **`^`** is "exclusive or" and **`>>`** shifts the bits right, as in Python.

C# has a ready-made `GetHashCode`, but it gives different numbers each time
the game starts, so the forest would change every time you loaded a save.

## Standing a tree on its tile

`TreeArt.Picture` works out where the picture goes, in tiles. One tile is 32
picture pixels:

```csharp
public Box Picture(double footX, double footY, int pixels = 32) =>
    new(footX - FootX / (double)pixels, footY - (FootY + 1.0) / pixels, Width / (double)pixels, Height / (double)pixels);
```

The foot of a tree on tile (x, y) is the middle of the tile's bottom edge,
(x + 0.5, y + 1). Then:
- **the left edge** is half the picture to the left of that;
- **the top** is the picture's whole height above it.

`(double)pixels` turns the whole number into a decimal before dividing.
Without it, `32 / 32` and `16 / 32` would be whole-number divisions, and the
second would give 0, as `16 // 32` does in Python.

## Drawing order

A deciduous tree is 2 tiles wide and 3 tall, so it covers the tiles above its
own. `_Draw` therefore goes row by row, from the top, and in each row:
1. paints each tile's ground: under a tree or a rock, its surface's colour,
   over the dark pixel the ground picture has for it;
2. draws the surface symbol and any piece (wall, floor, road);
3. then draws that row's trees.

So a tree covers what is behind it (the rows above), and nothing is painted
over it afterwards.

The order inside a row matters too. If each tree painted its own tile's ground
just before drawing itself, it would paint over the crown of the tree to its
left, leaving a hole. That's why all of the row's ground goes first.

A crown can reach 3 tiles up and half a tile to each side, so the loop goes
3 rows below the screen and 1 column past each side. Otherwise trees just off
the edge would have their crowns cut off.

Colonists are still drawn last, on top of every tree, as you chose. They'll be
sorted in among the trees in a later step, together with canopy fading.

## Crisp pixels

```csharp
TextureFilter = TextureFilterEnum.Nearest;
```

This makes Godot draw the pictures with crisp pixels instead of smoothing
them. It applies to everything `ColonyView` draws: the shapes are unchanged,
and the names are drawn at screen size, so they look the same.

## Exercise

1. In `ColonyView.cs`, change `TreePictures` from 16 to 24, then zoom in and
   out with the wheel. At what zoom do the triangles come back?
2. In `TreeArts.KindAt`, change `h % 3 == 0` to `h % 2 == 0`. About how many
   conifers are there now? Run `dotnet test tests` and see which test notices.
3. Rename `assets/vegetation/manifest.json` for a moment and start the game.
   What does the colony draw, and why doesn't it stop with an error?

Put each change back afterwards.
