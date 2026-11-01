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

## The tests

`dotnet test` in this folder runs the tests of `src/World` and `src/Sim`,
with no Godot needed.

The plan, step by step: `docs/plan.md`.

## Layout

- `src/World`: reads an exported world (as the generator's north/world_file.py does).
- `src/Sim`: the history systems in C#, running the years on (so far: harvest, trade, mines, food, growth, ruin, memories, opinions, rulers, grievance, revolutions, your rules, war, alliances, conquest, rebellion, splits, merging and empires, feeding by tastes, drift, migration, rulers who age by their people's lifespan, learned paths and hybrids, and capitals).
- `src/UI`: the screens, in Godot. The map is drawn in a SubViewport between the book and the factions, so no panel covers it.
- `tests/`: the tests, and a small exported world to test with.

Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
