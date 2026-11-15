# Resource test pictures (TEST v0.1, 15-11-2026)

Piles and containers for the asset preview's resource board
(`src/UI/AssetPreview.tscn`: K goes there and back, J switches the size). The
game itself does not use them.

They are made from the three packages `WR_01_Wood_and_Storage_TEST_v0.1`,
`WR_02_Stone_and_Minerals_TEST_v0.1` and `WR_03_Food_and_Agriculture_TEST_v0.1`
(their `_TEST.png` crops), which are kept with the implementation planning, not
here. Each picture was:

1. **Renamed, for the first package:** its crops' names have the kind and the
   state swapped (`barrel_small` holds the timber, `timber_full` the empty
   barrel). Here each is named by what it shows.
2. **Cleaned:** only the picture's main shape and the parts inside its box are
   kept. That removes bits of neighbouring pictures left at the crops' edges.
3. **Shrunk ahead of time,** with the colours weighted by how see-through they
   are, and then made fully see-through or fully solid at half way, as the v3
   trees are:
   - `compact/`: 32 pixels across a tile, the trees' scale;
   - `fine/`: 64 pixels across a tile, drawn at the same size, for twice the
     detail when zoomed in.

Sizes:
- **A pile's four pictures** (small, medium, large, full) share one scale: the
  widest of the four is one tile across.
- **A crate's and a barrel's** are each one tile across, since the container
  doesn't change size as it fills.

Each kind's file is `<kind>_<state>.png`. The kinds are timber, lumber, crate,
barrel, stone, rubble, coal, iron_ore, copper_ore, grain, flour,
harvested_crops, vegetables and fruit; the states are small, medium, large and
full.
