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
- `needs.json`: how the needs grow (`hunger`, `thirst`, `fatigue`): points an
  hour awake (`per_hour`) and asleep (`asleep_per_hour`), from 0 (none) to 100
  (the limit); the levels at which a colonist eats or drinks at mealtimes
  (`peckish`), wants to see to it (`wants`), stops new work for it (`urgent`)
  and drops what they are doing (`critical`); and how many hours at the limit
  before they collapse (`collapse_hours`; 0: at once). `eat` and `drink`: the
  good, how many points one unit eases, how many minutes it takes. `sleep`:
  points of fatigue an hour of sleep on a bedroll or on the ground eases.
  `spring_litres_per_day`: what the spring gives each morning (a stand-in
  until rivers and wells).
- `schedules.json`: schedule templates: 24 hours, each one of `sleep`,
  `work`, `meals`, `recreation`, `flexible`; and the `default` template.
- `construction.json`:
  - `surfaces`: what the ground may be (road, grass, rough ground), each with
    its walking `speed` as a share of a road's (1 = 100%, grass 0.8).
  - `pieces`: what can be built (a wall, a door, a floor, a road, a well).
    Each piece states what it is made of (`materials`, good id to whole
    units; a road needs none), how long building it takes (`work_minutes`),
    whether it blocks walking (`blocks`), whether it closes a room in
    (`encloses`: a wall and a door do, a floor does not), its own walking
    `speed` (or `null`: the surface beneath counts), its `size` in tiles
    `[across, down]`, the `access` tile it is built and used from (`[x, y]`
    from its top left tile, or `null` for a one-tile piece worked from beside
    it) and the `water_per_day` it gives (a well; 0 for the rest).
  - `templates`: buildings to plan in one go, drawn as `rows` of letters, one
    per tile, with a `key` saying which piece each letter is (`.` leaves the
    tile as it is).
  - `test_ground`: the ground the colony stands on until the world generator
    makes local terrain: its `width` and `height` in tiles (a tile is 1 m
    square), its `surface` everywhere, its `patches` of other surfaces (blocks
    of tiles) and its `obstacles` (a `kind` such as rock or tree, on a tile
    `x`, `y`, counted from 0 at the top left).

- `founding.json`: who founds the colony: how many `founders`, the `names`
  they are drawn from (by the colony's dice; at least as many names as
  founders), the `supplies` they bring (good id to whole units) and whether
  each brings a bedroll (`bedroll_each`).

Every file has `"version": 1`: the shape of the file, raised when it changes.
Unknown keys are refused, so a misspelt key is caught rather than ignored.
