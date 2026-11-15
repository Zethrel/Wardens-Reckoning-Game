# Resource test pictures, release candidate (v1.0 RC, 15-11-2026)

From the package `Wardens_Reckoning_Resource_Asset_Library_v1.0_RC_15-11-2026`
(kept with the implementation planning). Of its 167 files, 143 pictures are
byte for byte the earlier test sets', already in `prototype-resources-v0.1/`
to `v0.3/`. Only its new pictures are here:

- **`mortar_*`:** four sizes of the clean mortar, all scaled from one picture
  (the holes in it remain).
- **`steel_ingots_cool_*`:** the steel ingots graded cooler, to tell them from
  iron.
- **`<container>_base`, `_front`, `_with_<contents>`:** the basket, sack,
  shelves and timber rack as layers to draw in order: the empty container,
  then the contents, then the front piece.

The package's front pieces are the same size as their empty containers, so
here all three layers of a container are cut to the empty container's box and
shrunk together. Drawn one over another at the same place, they line up.

The contents layers are the package's separate contents, placed on the
container's canvas. Their sizes and heights are first guesses, the same as
v0.3's (see its README).

Made as v0.1's were, at 32 pixels a tile (`compact/`) and 64 (`fine/`), in
two looks: `outlined/` as given, with the package's white outline, and
`clean/` with it peeled off (U in the preview).

**The package still has batch 1's names swapped,** as v0.1 had:
`barrel_small.png` is the timber pile, and so on (see v0.1's README).
