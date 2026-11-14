# Lesson 28a: felling trees

Until now a tree was only in the way. This step lets the colonists cut trees
down, as the library's Review 44 decided (adopted 14-11-2026):

- **D1 A:** the player marks trees, one at a time or by dragging a box;
- **D2 C:** a felled tree gives 4 timber after 30 minutes of work, the timber
  lies where the tree stood, and no stump is left;
- **D3 A:** felling uses a new skill, Forestry;
- **D4 C:** felled trees don't grow back (yet).

In the game:
- **To mark trees:** open the build panel (B) and pick **Fell trees**, then
  drag a box over them. Or pick a tree's tile and press **F** (Fell).
- **To unmark trees:** **Leave trees standing** unmarks a box of them, and X
  (Leave standing) unmarks one.
- **How it looks:** a marked tree has a red-brown notch on its trunk. Felled
  timber lies on the tile as a few logs until someone carries it to a store.

## The tree in the content

`content/construction.json` has a new list:

```json
"trees": [
  { "id": "tree", "name": "Tree", "yield": { "timber": 4 }, "work_minutes": 30 }
]
```

- **The `id`** is the obstacle's kind on the ground: the local maps call their
  trees "tree".
- **Other kinds:** the list can grow later, for example a young tree that
  gives 1 timber, without a change to the code. That's D2's "data-defined per
  tree type".

`Content.cs` reads each entry into a `TreeKind` record and checks it, as it
checks the pieces. Two new rules:
- a tree's id may not also be a piece's;
- every kind of tree needs a challenge in `skills.json` (for learning).

`skills.json` has the new skill, `forestry`, used for the work `"fell"`.
`founding.json` gives the founders a Forestry range like the other skills.

## A felling is a project

Building already has everything felling needs:
- a job someone allowed takes, worked from beside the tile;
- progress kept when it's put down;
- pausing, cancelling, and the reasons it waits;
- saving.

So a felling is a `Project`, with one new flag:

```csharp
public bool Felling { get; init; }
```

Its `Piece` is then the kind of tree ("tree"), not a piece of
`construction.json`.

Much of the code asks a project "what piece are you?" to get its size, its
name or its minutes, with `content.Pieces[p.Piece]`. That would fail for a
tree. So those places now ask `KindOf`:

```csharp
public PieceKind KindOf(Project p)
{
    if (!p.Felling)
        return content.Pieces[p.Piece];
    if (!treePieces.TryGetValue(p.Piece, out var kind))
    {
        var tree = content.Trees[p.Piece];
        kind = new PieceKind(tree.Id, tree.Name, new Dictionary<string, int>(), tree.WorkMinutes, Blocks: true);
        treePieces[p.Piece] = kind;
    }
    return kind;
}
```

For a felling it makes a piece that:
- is one tile;
- needs no materials;
- takes the tree's minutes.

It keeps it in a dictionary, so it's made once per kind of tree.

`TryGetValue` is Python's `d.get(key)` that also tells you whether the key
was there. The `out var kind` hands the found value back through the
argument.

Because a felling needs no materials, the project's usual check ("are all
materials here?") is true at once, and its job is made straight away.

## Who fells it

`Work.KindOf(job)` says what kind of work a job is, for permissions and
skills:

```csharp
job.Kind == JobKind.Build ? (projects[job.Site!.Value.Value].Felling ? FellWork : BuildWork)
```

`a ? b : c` is Python's `b if a else c`, and here there is one inside the
other.

So a felling is work of the kind `"fell"`:
- a colonist can be forbidden it, as they can be forbidden building;
- their Forestry proficiency sets how fast they fell, and felling teaches it.

## Felled

When the work is done, `Complete` does three things:

```csharp
Ground!.RemoveObstacle(project.X, project.Y);
if (tree.Yield.Count > 0)
    Stocks.Make(job.First, now, Array.Empty<ReservationId>(), project.Stock, tree.Yield);
project.Cancelled = true;
```

1. **The tree is gone:** the tile is open ground, to walk on and build on.
2. **The timber is made:** 4 of it, in the project's own pile on that tile.
   `Make` is one operation of the ledger, so it can only ever happen once,
   even across a save and a reload.
3. **The project counts as cancelled:** a cancelled project carries its
   goods to a store and then disappears. That's exactly what felled timber
   needs, so felling reuses it.

## Painting the ground again

The colony screen paints the whole ground into one picture, with a dark pixel
for each tree. That picture was painted once. After a felling, the tree's dark
pixel would still show.

So `Ground` now counts its obstacle changes (`ObstacleChanges`).
`ColonyView._Process` compares that count with the one it last painted from.
When they differ, it paints again.

## Saving

A project's save now has `"felling"`. The save's version goes up to 23, so
older saves are refused, as with every change before release 1.0.

When it loads a felling, the save checks that the tree still stands on its
tile, unless the felling is already done.

## Exercise

1. In `construction.json`, change the tree's `"yield"` to
   `{ "timber": 6 }` and its `"work_minutes"` to 45. Fell a tree. How many
   logs lie there, and how long did it take?
2. In `skills.json`, change `"tree": 20` under `challenge` to 60. Fell a few
   trees and watch a colonist's Forestry in the inspector. Does it rise faster
   or slower than before? (Lesson 23b explains the challenge.)
3. Remove the `"tree"` line from `challenge` altogether and start the game.
   What does it say, and where?
4. Run `dotnet test tests --filter FellingTests`. Then change the 4 in the
   yield to 5 and run them again. Which tests notice?

Put each change back afterwards.
