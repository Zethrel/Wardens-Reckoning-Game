# Lesson 18b: building from the screen

Step 18a showed the colony; now the player can build in it. Everything the
colony could already do in the tests (plan pieces and templates, pause,
cancel, take down, stockpiles, bedrolls, emptying the carts) can now be done
from the screen, with the mouse or the keyboard. Library Review 38.

## What is where

- `src/Colony/Placing.cs` (plain .NET, tested): `Shapes.Line` and
  `Shapes.Box` (the tiles a drag covers), and a fifth part of `Work`:
  `CheckAll`, `PlanAll`, `CheckTemplate`, `CheckStockpile`, `PlaceStockpile`,
  `RemoveStockpile`, `CheckBedroll`.
- `src/Colony/Inspect.cs` gained `Commands` (what a tile offers) and
  `Waiting` (what waits, and why).
- `src/UI/ColonyScreen.Build.cs`: the build tools on screen.
- `tests/PlacingTests.cs`: 15 tests.

## Check first, then place

A drag can cover a dozen tiles. Before the mouse button comes up, each tile is
**checked** (`Work.Check`, from step 15a) and drawn as it would be: a tile
that can be used is outlined, one that cannot is shaded and crossed out. When
the button comes up, `PlanAll` plans every tile that can be used and skips the
rest, so a wall dragged over a rock is built on both sides of it:

```csharp
var (planned, refused) = work.PlanAll("wall", Shapes.Line(27, 8, 33, 8));
// planned: 5; refused: "(30, 8): A rock is in the way."
```

A template is all or nothing (`PlanTemplate`, step 15b): a house missing a
corner is no house. `CheckTemplate` shows every piece first, turned and
mirrored as it would be laid.

## Tuples

`(int Planned, string? Refused)` is a **tuple**: two values returned together
without writing a class for them. The caller can take them apart in one line:
`var (planned, refused) = ...`. The names are only labels; the values are a
number and a text.

## A command is a record with an action in it

```csharp
public sealed record Command(string Label, char Key, string Tooltip, Action? Run,
                             Func<int, int, Placement>? CheckAt = null, Action<int, int>? RunAt = null);
```

`Action` is "a piece of code to run later, taking nothing"; `Func<int, int,
Placement>` is "code taking two whole numbers and giving back a Placement".
`Inspect.Commands` makes them with **lambdas**, small nameless functions:

```csharp
new Command("Pause", 'P', "Stop work on this timber wall for now", () => work.Pause(id, true))
```

The `() => ...` is not run here; it is kept in the record, and run when the
player presses the button or P. Because the commands are made in plain .NET,
the tests press them too (`commands[0].Run()`) and check what happened.

Moving a bedroll needs a second tile, so that command has no `Run`, but
`CheckAt` and `RunAt`: the screen hands the player a one-off tool to pick the
tile, shows `CheckAt`'s answer on it, and calls `RunAt` there.

## Partial classes

The screen grew big, so its building part is in a second file,
`ColonyScreen.Build.cs`, both starting with `public partial class
ColonyScreen`. C# joins them into one class: fields in one file are used in
the other.

## A bug found by running it

The first run of "Move bedroll" in Godot crashed: `Aim` took the tool in hand
(which draws the preview at once) before it remembered which command the tool
was for, so the preview asked the colony for a piece called "target". Two
lines swapped fixed it. The tests could not see this, because it was in the
order of calls on the screen; running the game in the cloud and reading its
error did.

## FP-06: why work waits, for the player and for the developer

**J** lists every building that waits, in the player's words with a next
step, and a "Go to" button for each. **F3** adds the developer's trace beside
it, in grey and labelled "Trace", so the two cannot be mistaken for each
other (FP-06 asks for exactly that).

## FP-11: your checklist

FP-11 is checked by a person: "keyboard/mouse operation, visible focus,
legible scalable text, and meaningful non-colour-only feedback work for the
first playable's central workflows". Please go through it on your PC and note
what fails:

**Keyboard only (put the mouse aside):**
1. Main menu: Tab/arrows reach every button and the focused one is visibly
   marked; Enter presses it.
2. Open a world; pick a founding site from the list on the right with the
   keyboard; Enter on **Found a colony here**.
3. In the colony: Space, 1, 2, 3; Tab through the colonists.
4. B, arrows to **Small house**, Enter; arrows to move the outline; R and F;
   Enter to place.
5. B, **Stockpile**: Enter, arrows, Enter.
6. B, **Timber wall**: Enter, arrows along a line, Enter.
7. Select a tile with a project (you need the mouse to pick a tile; or J and
   a **Go to** button), then P, P, X.
8. Esc: puts down the tool; Esc again opens the menu.

**Mouse only:** the same, by clicking and dragging.

**Text:** Settings → Interface size at 150% and 200%: everything readable,
nothing cut off or overlapping (note where it is).

**Not by colour alone:** a tile that cannot be built is crossed out, not only
red; the speed in use is named in the Pause button's tooltip; paused projects
are crossed through; each surface has a symbol.

Your check on 9 November found that at 200% the build panel ran into the bar
and the bar ran off the right edge. Now the bar is an `HFlowContainer` (a row
that wraps onto a second line when it does not fit) and the build panel sits in
a `ScrollContainer` that `FitBuildPanel` keeps above the bar every frame.

One gap I know of: picking a tile to inspect needs the mouse (or J's list).
A keyboard tile cursor for inspecting, like the build cursor, would close it;
tell me if you want it.

## Exercise

1. In `ColonyScreen.Build.cs`, make roads drawn in boxes instead of lines
   (look at `Tools()`). Which is better for a road?
2. In `Inspect.Commands`, give "Cancel" the key 'C' instead of 'X'. What
   else must change for the button and the key to agree? (Look at the tests.)
3. Change `Shapes.Line` so a tie goes down instead of across. Which test
   fails, and what does it say?
