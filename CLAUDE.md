# Working agreement

The game of Wardens' Reckoning. The world is made by the generator
(Zethrel/Wardens-Reckoning, Python); this repository loads an exported world
and plays it on in Godot 4.7 with C#. The plan is in the generator's
docs/roadmap.md ("The game, decided 31 October 2026").

## Who writes what

As in the generator: Claude writes everything, and the maintainer tweaks it
afterwards. No distinction between AI-made and hand-made: nothing records who
or what made a picture, a value or a sentence. The game is also how the
maintainer learns C#, so explain the code in comments well enough that a
beginner can follow it.

**Commits** are authored as `Zethrel <97619482+Zethrel@users.noreply.github.com>`
only, with no co-author lines.

## Lessons

Each step comes with a lesson in `docs/lessons/` that walks through the new
code and ends with an exercise in changing its values. Write it for someone
who reads basic Python and is new to C#.

## Code

- Godot 4.7 (.NET build), C# on .NET 8. Any library beyond Godot and the .NET
  base library (and xunit for the tests) is a decision for the maintainer.
- `src/World` reads an export and `src/Sim` holds the history systems: both
  are plain .NET, with no Godot in them, so their tests run anywhere. Godot is
  only in `src/UI`.
- The same save and the same decisions must always play out the same: no
  `System.Random` without a seed, and no dice shared between systems.
- `dotnet test` must pass before every commit.
- Every source file starts with the copyright line.
