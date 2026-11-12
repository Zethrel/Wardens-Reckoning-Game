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
- `colony.json`: `carry_kg` (what one person carries by hand). The physical
  values are in **real seconds at ×1** (library Review 41, the hybrid clock),
  for a pace that is pleasant to watch, and turned into ticks when the content
  is read (24 ticks a real second; a duration rounded to the nearest whole tick,
  halves away from zero, at least 1 tick if above 0): `walk_m_per_real_second`
  (walking speed on a road; divided by 24 for a tick, not rounded),
  `pickup_real_seconds` and `drop_real_seconds` (time to load and unload; 0 for
  none), `think_real_seconds` (how often a colonist decides what to do next:
  drawn once for each colonist between the two, in whole ticks).
- `needs.json`: how the needs grow (`hunger`, `thirst`, `fatigue`, `hygiene`): points an
  hour awake (`per_hour`) and asleep (`asleep_per_hour`), from 0 (none) to 100
  (the limit); the levels at which a colonist eats or drinks at mealtimes
  (`peckish`), wants to see to it (`wants`), stops new work for it (`urgent`)
  and drops what they are doing (`critical`); and how many hours at the limit
  before they collapse (`collapse_hours`; 0: at once; `null`: never, as
  hygiene). `eat` and `drink`: the good, how many points one unit eases, how
  many minutes it takes. `wash`: the litres of water a wash at a wash basin
  uses (on a bank: none), the points of hygiene it eases, its minutes.
  `sleep`: points of fatigue an hour of sleep on a bedroll or on the ground
  eases. `spring_litres_per_day`: what a spring gives each morning (the test
  ground's stream). `water` (step 19a): `load_litres` (a load of water
  carried), `plenty_litres` (what a river or lake holds, topped up every
  hour), `brook_litres_by_month` (a brook's litres a day, January first),
  `freezing_months` and `cold_biomes` (where and when water freezes), and the
  `ice_hole` piece.
- `sanitation.json` (step 19b): `fouled_good` (the good fouled water is:
  `foul_water` in `goods.json`, shown to the player as water); `fouling`: on
  average once in how many days a river, lake or brook fouls
  (`days_between`), how many days it lasts (`lasts_days`, from and to), the
  share of foulings with a sign (`sign_share`) and the `signs` to choose
  from; `boil`: the `litres` of a batch, the `fuel` good and how many units
  (`fuel_units`), and its `minutes`.
- `health.json` (step 19b): the `upset_stomach`: its `name`, the `chance`
  that a drink of fouled water brings it, the hours before it shows
  (`onset_hours`) and how long it lasts (`lasts_hours`), each from and to; the
  share of work an ill colonist does (`work`), and how many times as fast
  thirst and fatigue grow (`thirst`, `fatigue`).
- `schedules.json`: schedule templates: 24 hours, each one of `sleep`,
  `work`, `meals`, `recreation`, `flexible`; and the `default` template.
- `construction.json`:
  - `surfaces`: what the ground may be (road, grass, rough ground, sand,
    snow, mud, fresh and salt water), each with its walking `speed` as a share
    of a road's (1 = 100%, grass 0.8). A speed of 0 cannot be walked or built
    on (water). A local map of the world generator names its surfaces by these
    ids (`fresh_water`, `salt_water` and the rest), so each one it uses must
    be here.
  - `pieces`: what can be built (a wall, a door, a floor, a road, a well, a
    water barrel, a wash basin; the ice hole is the colony's own to plan).
    Each piece states what it is made of (`materials`, good id to whole
    units; a road needs none), how long building it takes (`work_minutes`),
    whether it blocks walking (`blocks`), whether it closes a room in
    (`encloses`: a wall and a door do, a floor does not), its own walking
    `speed` (or `null`: the surface beneath counts), its `size` in tiles
    `[across, down]`, the `access` tile it is built and used from (`[x, y]`
    from its top left tile, or `null` for a one-tile piece worked from beside
    it), the `water_per_day` it gives (a well; 0 for the rest), the
    `holds_litres` of water carried to it (a barrel, a basin; 0 for the
    rest), whether colonists wash at it (`wash_place`), and whether it is the
    work of opening frozen water (`opens_ice`).
  - `templates`: buildings to plan in one go, drawn as `rows` of letters, one
    per tile, with a `key` saying which piece each letter is (`.` leaves the
    tile as it is).
  - `test_ground`: the ground of a colony founded without a world (the tests,
    trying things out): its `width` and `height` in tiles (a tile is 1 m
    square), its `surface` everywhere, its `patches` of other surfaces (blocks
    of tiles) and its `obstacles` (a `kind` such as rock or tree, on a tile
    `x`, `y`, counted from 0 at the top left).

- `founding.json`: who founds the colony: how many `founders`, the `names`
  they are drawn from (by the colony's dice; at least as many names as
  founders), the `supplies` they bring (good id to whole units), whether
  each brings a bedroll (`bedroll_each`), and for every skill the range
  `[from, to]` a founder's proficiency is drawn from (`skills`, step 23a).
- `skills.json` (step 23a): the `skills`, each with an `id`, a `name` and the
  work it is `used_for`: `build` (all building), `boil` (boiling water) or a
  workshop's id (its recipes); each kind of work uses one skill at most, and
  work that uses none (carrying, care) goes at the usual pace. `ranks`: the
  word shown for a proficiency, each `[the proficiency it starts at, its
  name]`, the first at 0. `speed`: how fast work goes, as points
  `[proficiency, speed]` from 0 to 100 joined by straight lines (1 is the
  usual pace: 0.5 takes twice as long, 1.5 two thirds of the time).
  `standard`: the proficiency of a colonist added without one (the tests'),
  where the speed should be 1. Learning (step 23b): `challenge`, how hard
  each piece, recipe and `boil` is, from 0 to 100 (every one must be
  there); `learning`: `points_per_hour` of proficiency an hour of work
  teaches when the colonist is as good as the work is hard, more when it is
  above them (at most `most` times), nothing once they are `reach` past it;
  `aptitude` `[from, to]` founders' learning speed is drawn from (1 is the
  usual), shown in the inspector as quick (at least `quick`) or slow (at most
  `slow`) after `aptitude_seen_hours` of practice; a skill not practised for
  `decay_after_days` loses `decay_per_day` each morning, never below
  `kept_share` of the best ever reached; below that best, learning goes
  `recovery` times as fast.

Every file has `"version": 1`: the shape of the file, raised when it changes.
Unknown keys are refused, so a misspelt key is caught rather than ignored.
