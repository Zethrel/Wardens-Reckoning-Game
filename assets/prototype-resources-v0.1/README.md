# Resource test pictures (TEST v0.1, 15-11-2026)

Piles and containers for the asset preview's resource board
(`src/UI/AssetPreview.tscn`: K goes there and back, J switches the size). The
game itself does not use them.

They are made from the six packages' `_TEST.png` crops, which are kept with
the implementation planning, not here:
- `WR_01_Wood_and_Storage_TEST_v0.1`;
- `WR_02_Stone_and_Minerals_TEST_v0.1`;
- `WR_03_Food_and_Agriculture_TEST_v0.1`;
- `Wardens_Reckoning_Batch_04_Construction_Sprites_REPACK`;
- `Wardens_Reckoning_Batch_05_Manufactured_Sprites_REPACK`;
- `Wardens_Reckoning_Batch_06_Specialised_Storage_Sprites_REPACK`.

Each picture was:

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
- **The containers'** (crate, barrel, baskets, shelves, timber rack) are each
  one tile across, since a container doesn't change size as it fills. How big
  shelves or a timber rack should really be is not decided.

Each kind's file is `<kind>_<state>.png`. The kinds are timber, lumber, crate,
barrel, stone, rubble, coal, iron_ore, copper_ore, grain, flour,
harvested_crops, vegetables and fruit (batches 1 to 3); bricks, clay,
cut_stone, mortar and sand (4); copper_ingots, bronze_ingots, iron_ingots,
steel_ingots and tools (5); sacks, baskets, shelves and timber_rack (6). The
states are small, medium, large and full.

Known faults of the crops, kept as they are:
- **Flat edges:** some are cut flat along the bottom or the top, for example
  the small stone, the bricks, some ingots and the sacks.
- **Mortar:** its large and full states still hold a trowel and a bucket.
