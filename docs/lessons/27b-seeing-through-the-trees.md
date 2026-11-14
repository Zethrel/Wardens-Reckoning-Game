# Lesson 27b: seeing through the trees in the colony

Step 27a drew the trees as pictures, with the colonists always on top. Now the
colonists stand *among* the trees:
- someone behind a tree is hidden by its crown, as in the asset preview;
- the canopy fading you tried there (lesson A2) makes sure you can still see
  the colonist you're looking at.

## Four passes

`ColonyView._Draw` now draws in four passes. The order decides what covers
what:

1. **Flat on the ground:** each tile's surface, symbol and piece (walls,
   floors, roads).
2. **Markers on the ground:** buildings under way, gathering places, stores,
   bedrolls.
3. **What stands up, by depth:** trees, rocks and colonists. The one whose foot
   is higher on the screen is drawn first, so whoever is lower stands in front.
4. **Over everything:** the tile marks, the build ghost and cursor, and the
   colonists' rings, names and talk.

Pass 3 is a merge of two lists that are both in order. The trees come row by
row, and the colonists are sorted by where they stand:

```csharp
var standing = work.Workers.Select(w => (Worker: w, At: DrawnAt(w))).OrderBy(s => s.At.Y).ToList();
int nextColonist = 0;
for (int y = top; y <= treeBottom; y++)
{
    for (; nextColonist < standing.Count && standing[nextColonist].At.Y < y + 1; nextColonist++)
        DrawColonistBody(standing[nextColonist].Worker, standing[nextColonist].At, radius);
    ...the row's trees and rocks...
}
```

A tree on row `y` has its foot at `y + 1`. Before drawing a row's trees, every
colonist whose feet are above that line is drawn. Those colonists are behind
the row's trees, so the trees cover them.

The `for` loop with nothing before its first `;` is C#'s way of writing
"carry on from where `nextColonist` already is". `nextColonist` keeps its
value between rows, so each colonist is drawn exactly once.

`(Worker: w, At: DrawnAt(w))` makes a tuple with named parts, like a small
record made on the spot.

## Fading, as in the preview

`UpdateCanopy` runs every frame, before drawing. It looks at two colonists:
the picked one (`SelectedWorker`) and the one under the mouse
(`HoverWorker`). It finds the trees on the tiles where a crown could be in
front of them, and asks `Canopy.Goals`, the same code the preview uses and the
tests check, how see-through each crown should be. The `Fader` then moves each
crown there in real seconds.

A crown's number is its tile's place in the map: `y * width + x`. Every tile
gets its own number without a list of trees.

## The settings are the player's, not the colony's

The fade's strength, the see-through strength and Reduced Motion are in
`Settings`, saved in `settings.cfg` like the interface size. They're never
saved with the colony.

The specification asks for this (O6), and it keeps an old rule: the same save
and the same decisions must always play out the same. What you can see through
is presentation; the colony doesn't change because a crown faded.

## The Trees button and V

`Settings.SeeThroughTrees` is the bar's Trees button: while it's on, every
crown on screen goes to the see-through opacity. Holding V does the same
while held:

```csharp
bool through = lastPictures && (Settings.SeeThroughTrees || Input.IsKeyPressed(Key.V));
```

`Input.IsKeyPressed` asks Godot whether a key is down right now. A key press
event would only say when it went down.

## Measuring the colony

`-- --bench` measures the colony's drawing. It pauses the colony, looks for
the part of the map with the most trees on screen at 16 px a tile, and
measures four settings: 16 px, 16 px with the Trees button on, 32 px and
40 px. Each row says whether the frame stays within your budget of 60 frames
a second.

Finding the densest view uses *running sums*. `sum[x, y]` holds how many trees
lie left of `x` and above `y`, so the trees in any rectangle are four
look-ups:

```csharp
int trees = sum[x + wide, y + high] - sum[x, y + high] - sum[x + wide, y] + sum[x, y];
```

Counting each possible view tile by tile would take about 5,000 tiles for
each of more than 30,000 positions. This way it's one pass over the map, then
four sums each.

## Step 27c: where the time went

The benchmark's first run on your PC, on a forest site at 1920 × 1080, missed
the budget at 16 px a tile: 39 frames a second. The GPU needed only 0.6 ms, so
the time was on the CPU, in `_Draw`. Timing each pass separately showed where:

| Part of `_Draw` | ms a frame (cloud) |
|---|---:|
| the ground's symbols | 10.2 |
| the rest of pass 1 | 0.8 |
| the trees and colonists (pass 3) | 0.7 |

The trees weren't the problem. Every rough-ground tile has two small specks,
and they were drawn with `DrawCircle`. Godot makes a circle out of many small
triangles, worked out each time; a forest on rough ground at 16 px is
thousands of tiles, so thousands of circles a frame. Drawn as squares
(`DrawRect`, two triangles each), the symbols took 1.0 ms. At that size a
square and a circle look nearly the same.

A second thing turned up. The camera's zoom can come out as 15.99999 when 16
was asked for, so `pixels >= 16` was false and the trees went back to
triangles. Rounding to a thousandth first fixes that:

```csharp
float pixels = MathF.Round(GetViewport().GetCanvasTransform().Scale.X * 1000) / 1000;
```

The lesson: measure before guessing. Here the guess, that the new tree
pictures were the slow part, would have been wrong.

## Exercise

1. In Settings, lower "Trees in front of a colonist you look at" to 20 %.
   Then pick a colonist in a wood. Is it easier or harder to tell what's going
   on around them?
2. In `ColonyView.UpdateCanopy`, remove `HoverWorker` from
   `new[] { SelectedWorker, HoverWorker }`. What changes when you move the
   mouse over a colonist in the trees?
3. In pass 3, change `< y + 1` to `< y + 2`. Where do colonists now appear
   compared with the trees just in front of them, and why?

Put each change back afterwards.
