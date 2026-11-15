# Resource test pictures, targeted refinements (TEST v0.3, 15-11-2026)

From the package `WR_Asset_Library_v0.3_Targeted_Refinements_TEST` (kept with
the implementation planning). The asset preview's resource board shows them in
three rows under the others:

1. **Empty containers:** basket, sack, shelves and timber rack, with the
   mortar cleaned of its trowel and bucket (one state only).
2. **Contents on their own:** grain, flour, timber, lumber, vegetables, fruit
   and tools.
3. **Contents in containers:** the contents laid over the empty containers,
   put together here ahead of time, at the full size, before shrinking. Nothing
   is masked, so the contents cover the front rim. That shows what a layered
   picture would need: a front piece drawn over the contents, or an anchor and
   a mask for each container.

The package's fourth folder (the v0.2 pictures) is byte for byte the same as
TEST v0.2, which is already in `prototype-resources-v0.2/`.

Made as v0.1's were (see its README), at 32 pixels a tile (`compact/`) and 64
(`fine/`), each picture one tile across, in two looks:
- `outlined/`: as the package gives them, with a white outline round each
  picture;
- `clean/`: with that outline peeled off, a ring of light grey pixels at a
  time from the outside, until a ring has hardly any left. U switches.

**Holes:** the flour and the mortar have holes where the background cut-out
also took pale parts of the pile. The ground shows through them.

**The rows' sizes and offsets** in the third row are first guesses:

| Container | Contents | Width (share of the container's) | Bottom (share of its height, from its foot) |
|---|---|---:|---:|
| Basket | Fruit, vegetables | 0.70 | 0.42 |
| Sack | Grain, flour | 0.62 | 0.62 |
| Shelves | Tools | 0.55 | 0.40 |
| Timber rack | Timber, lumber | 0.78 | 0.12 |
