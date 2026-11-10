# Wardens' Reckoning: the game

The game of Wardens' Reckoning, in Godot 4.7 with C#. The world, its map and
its first 300 years are made by the generator (Zethrel/Wardens-Reckoning):
`python worldgen.py --export` writes a folder `export/` that this game opens.

## Running it

1. Make a world with the generator, in its folder:
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. Open this folder's `project.godot` in Godot 4.7 (.NET), and press Play (F5).
3. "Open a world" and choose the `world.json` in
   `output\world-42\h42\export\`.

On the map: the mouse wheel zooms, the right or middle mouse button (or
W, A, S, D and the arrow keys) moves, **Next year** (N) runs a year, **Play**
(Space) runs them one after another, the slider at the bottom picks the year
shown, **Book** (B) opens everything that happened, and a click on a faction
in the list or on one of its towns opens that faction's own history. Esc
closes the book, or opens the menu (save, load, settings).

**A colony** (step 18a): the white diamonds on the world map are the export's
founding sites (also listed on the right). Pick one, then **Found a colony
here**. On the colony's local map: the wheel zooms, the right mouse button or
W, A, S, D moves, a left click inspects a colonist or a tile, **Space** pauses
or plays, **1**, **2** and **3** set the speed, **Tab** picks the next
colonist, Esc opens the menu (save, load). A saved colony loads from the main
menu's **Load a colony**.

**Building** (step 18b): **B** opens the build panel. Walls, doors and roads
are dragged in straight lines, floors and stockpiles in boxes; the well and
the small house go where you click (**R** turns the house, **F** mirrors it).
Every tile shows whether it can go there before you let go, and the bar says
why not. The arrow keys and **Enter** do the same without the mouse; **Esc**
puts the tool down. A picked tile lists what can be done there, with its key:
pause (**P**), cancel or take down (**X**), move a bedroll (**M**), empty the
carts (**E**), remove a stockpile (**R**). **J** lists what waits and why;
**F3** adds the developer's trace. The menu's Settings have the interface size.

## The tests

`dotnet test` in this folder runs the tests of `src/World`, `src/Sim` and `src/Colony`,
with no Godot needed.

The plan, step by step: `docs/plan.md`. The first playable's evidence, scenario by scenario: `docs/acceptance.md`. Its performance: `docs/performance.md` (measured with `dotnet run -c Release --project tools/Profile`).

## Layout

- `src/World`: reads an exported world (as the generator's north/world_file.py does).
- `src/Sim`: the history systems in C#, running the years on (so far: harvest, trade, mines, food, growth, ruin, memories, opinions, rulers, grievance, revolutions, your rules, war, alliances, conquest, rebellion, splits, merging and empires, feeding by tastes, drift, migration, rulers who age by their people's lifespan, learned paths and hybrids, capitals, attunement's mark, the harvest ritual, the great ritual in war, offshoot paths, the god of death and her signs, named threats and the great rituals against them, happenings and eras, and the chokepoints: needs, embargoes, stand-ins, smugglers, supply contracts, sabotage, labour displacement, secrets, legitimacy, the tech leak and debased coin, and whom a shortage is blamed on), and the chronicle telling the game's own eras in your sentences.
- `src/Colony`: the colony, the Wardens' settlement played up close, as the design library describes it (so far: the clock and calendar, speed, identifiers, dice, the save and the autosave's timing; the content files in `content/`, checked at start; inventories, claims and the ledger; workers who carry and grind, workshops asking for what they lack, and stopping, resuming and cancelling work; the colonists' needs, schedules, meals, sleep and collapse; who may do which work, and why work waits; the ground with its tiles and ways, and construction: walls, doors and floors; rooms, stockpiles, templates, and the founders' arrival; walking speed by ground and legs, roads, the well, taking pieces down; safe saving and loading, and the session that runs the colony; the founding on a local map of the export's founding sites; what the inspector shows of a tile or a colonist; placing lines, boxes and templates, the commands on a tile, and the list of what waits; water by its uses: sources, barrels and basins, washing, frost; fouled water, marks, boiling and the upset stomach).
- `src/UI`: the screens, in Godot. The map is drawn in a SubViewport between the book and the factions, so no panel covers it; the colony's local map likewise, beside the inspector.
- `tests/`: the tests, and a small exported world to test with.

Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
