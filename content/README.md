# The colony's content

The goods, workshops and recipes the colony knows, and its everyday numbers.
They are read and checked when a colony starts (`src/Colony/Content.cs`): a
mistake stops the game with a message naming the file, the place in it and
what is wrong, before anything runs.

- `goods.json`: each good's `id` (lowercase letters, digits and `_`, used in
  saves: do not rename one a save uses), `name`, `mass_kg` and `volume_l` of
  one unit. Goods are counted in whole units; a unit of wheat or flour is 1 kg.
- `recipes.json`: `workshops` (an `id` and a `name`) and `recipes`: the
  `workshop` that works it, `inputs` and `outputs` (good id to whole units),
  and `work_minutes`. A good may not be made, directly or through other
  recipes, from itself.
- `colony.json`: `carry_kg` (what one person carries by hand),
  `walk_m_per_minute` (walking speed), `pickup_minutes` and `drop_minutes`
  (time to load and unload).

Every file has `"version": 1`: the shape of the file, raised when it changes.
Unknown keys are refused, so a misspelt key is caught rather than ignored.
