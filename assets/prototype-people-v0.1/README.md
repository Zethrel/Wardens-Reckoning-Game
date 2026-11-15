# Peoples Batch 1, one-tile test (TEST v0.1)

From `WR_Peoples_Batch_01_One_Tile_Test_v0.1.zip` (the temporary folder's
`Assets\people`, 15 Nov 2026): the eight peoples a colony can have, one
picture each, standing and facing south, as `48/<people>.png` (48 × 48) and
`64/<people>.png` (64 × 64). Shown in the asset preview's people patch (Q).

## What was changed here

- **Stray specks removed:** the elf, orc, goblin and kobold had pieces of
  their neighbours in the source line-up at the canvas edges. Each picture
  keeps its largest piece and whatever lies inside that piece's box.
- **Solid or clear:** the edges were partly see-through; every pixel is now
  either solid (alpha 128 and up) or clear, as the trees are.
- The canvases and the pixels' places are unchanged.

## Where the feet are

The package's README gives one feet point for all eight: (24, 45) on the 48
canvas and (32, 61) on the 64. Only the elf's feet are there. The preview
stands each picture on its own lowest solid row instead (pixel row + 1):

| People | 48: feet at | below them | 64: feet at | below them |
|---|---|---|---|---|
| human | 38 | 7 | 52 | 9 |
| dwarf | 38 | 7 | 52 | 9 |
| elf | 45 | 0 | 61 | 0 |
| halfling | 41 | 4 | 56 | 5 |
| gnome | 40 | 5 | 55 | 6 |
| orc | 38 | 7 | 51 | 10 |
| goblin | 39 | 6 | 53 | 8 |
| kobold | 43 | 2 | 59 | 2 |

"Below them" is how many empty rows lie under the feet, down to the declared
point. Drawn at the declared point, those peoples would float that many
pixels above their tile.

## Heights, at the same world size

Both canvases are 1.5 tiles across in the world. Height of the figure in
tiles (48 and 64 agree within a pixel):

| human | dwarf | elf | halfling | gnome | orc | goblin | kobold |
|---|---|---|---|---|---|---|---|
| 1.00 | 0.81 | 1.34 | 0.53 | 0.56 | 1.13 | 0.56 | 0.59 |

The elf is the tallest, a third taller than the human and taller than the
orc.

## The 32 × 48 human

`32x48/human.png` is `wardens_reckoning_human_front_32x48.png` (the
temporary folder's `Assets\people`, 15 Nov 2026), as it came: solid or clear
already, 15 colours. It is the prototype colonist's canvas, and is drawn the
same way, at 32 pixels a tile: 1 tile wide and 1.5 high, its feet on its
bottom row. The figure is 43 pixels high, 1.34 tiles: a third taller than the
human of batch 1 (1.00 tiles), as tall as batch 1's elf. In the preview it
stands left of the colonist on each row.

## Style C's human, at two sizes

`style-c/human_32x48.png` and `style-c/human_48x72.png` are
`wardens_human_style_c_32x48.png` and `wardens_human_style_c_48x72.png`
(the temporary folder's `Assets\people`, 15 Nov 2026), as they came: solid or
clear already, feet on the bottom row. Both are drawn 1 tile wide and 1.5
high, so the 48 × 72 is drawn at 48 pixels a tile and shows more detail, not
a bigger person. The figures are 46 and 70 pixels high, 1.44 and 1.46 tiles.
They have no dark outline and many colours (544 and 1,270), where the A3f
human has 15. In the preview they stand right of the colonist on each row.
