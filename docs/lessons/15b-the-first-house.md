# Lesson 15b: the first house

The founders now arrive the way the library describes (Review 33). Three of
them come with their supplies: 60 timber, 30 travel rations, 50 wheat, and a
bedroll each. The player can plan a whole house in one go, and once its walls
close a room, the bedrolls and the goods can be moved inside.

Your target was that the founders build a medium building on their first day,
to house their bedrolls and supplies. A test now shows it. They arrive at
08:00, the small house (6 by 6, 19 walls and a door) stands at 14:53, and by
the end of the day the bedrolls and every supply are inside.

## The founders (`src/Colony/Founding.cs`)

`Founding.Found(seed, arrival, content, x, y)` makes the whole start:

- a colony on the test ground;
- the founders' carts on tile (x, y), holding the supplies;
- the founders, on the open tiles nearest the carts;
- their bedrolls just south of the carts;
- the stand-in spring a little further south. The well replaces it in 15c.

Who the founders are is, for now, only their names. They are drawn without
repeats from the list in `content/founding.json`, by the colony's own dice
(`DiceFor("founders")`), so the same seed always brings the same founders.

`OpenNear` finds a free tile near a spot. It searches in rings outwards
(distance 0, then 1, then 2...), and each ring row by row, so the answer is
always the same.

## Rooms (`Ground.Rooms`, `RoomAt`, `Indoors`)

A room is a group of open tiles closed in on every side by walls and doors,
with no open way out to the edge of the ground. Its tiles count as indoors.
(Until step 17b, rocks closed rooms in too; lesson 17b says why that changed.) There are no roofs yet: this is the simplified
indoor rule the library allows for the first prototype.

How it is found: a **flood fill**. Start at a tile a person could stand on,
spread to the tiles beside it (across, not on the slant), and keep going until
nothing new is reached. If the group touched the edge of the ground, it is
outdoors; otherwise it is a room.

Which pieces close a room in is set in the content (`"encloses"`): a wall and
a door do, a floor does not. A door is walked through but still closes the
room.

Two walls that touch only at their corners also close a room. People cannot
walk through a slanting gap (lesson 15a), so air shouldn't either.

## Stockpiles and emptying the carts

- `Work.AddStockpile(name, x, y, width, height)` lays a store over a block of
  tiles. A wall may not be planned on any of them.
- A store marked **`Emptying`** (the founders' carts, once there is a
  stockpile) takes nothing more. Its goods are carried to the nearest store
  that takes them, one load at a time, and only when nobody has other work
  waiting. That way the carts never claim all the rations long before anyone
  carries them, and the rations stay free to eat.
- `Work.MoveBedroll(id, x, y)` lays a bedroll somewhere else. For now this is
  at once, as the player places it; carrying bedrolls can come later.

## Templates (`Work.PlanTemplate`)

A template is drawn in `content/construction.json` as rows of letters, one per
tile:

```
"WWWWWW",
"W....W",      W: wall, D: door, '.': nothing
"W....W",
"W....W",
"W....W",
"WWDWWW"
```

`PlanTemplate("small_house", x, y, turns, mirror)` plans every piece at once,
with the top left corner on (x, y). `turns` is how many quarter turns
clockwise, and `mirror` flips it left to right first. It is all or nothing: if
one piece may not go where it would, nothing is planned, and the first such
piece is named ("At (30, 8): A rock is in the way."). Once planned, the pieces
are ordinary projects that can be paused or cancelled one by one.

Turning a piece at (x, y) a quarter clockwise in a template `w` wide and `h`
high moves it to (h - 1 - y, x), and the template becomes `h` wide and `w`
high.

Saves are version 7: each store's tiles, if it is a stockpile, and whether it
is being emptied.

## 1. Run it

Nothing on screen yet. In the game's folder (PowerShell): `dotnet test tests`.
The new tests are in `tests/FoundingTests.cs`.

To see when the house stood:

```
dotnet test tests --filter "FullyQualifiedName~first_day" --logger "console;verbosity=detailed"
```

What they prove:

- **The founders:**
  - three, with different names from the list, and their supplies, each
    written in the ledger as arrived;
  - a bedroll each, and nobody on top of anyone;
  - the same seed always brings the same founders, and other seeds others;
  - no arrival on a rock.
- **Rooms:**
  - a ring of walls makes one room of 3 by 3, and a door keeps it one;
  - a gap, or a floor in the gap, does not;
  - walls touching at corners close a room;
  - walls against the edge do not;
  - two rooms come in the same order every time, and building forgets the
    old answer.
- **Templates:** the small house has 19 walls and a door. Each turn, and the
  mirror, puts the door where it should be, and every turn keeps the same
  ring. One that does not fit plans nothing and names the tile.
- **Stockpiles:**
  - on open tiles only, and walls are kept off them;
  - the emptied carts go to the stockpile only when there is nothing else to
    do, never more than one load waiting;
  - all 60 timber arrive.
- **The first day:** see above.
- **The save** keeps stockpiles and emptying, ends the same as an unsaved
  colony, and refuses four broken versions.
- **More tests:**
  - never a founder's name twice, over 60 seeds;
  - a bedroll only on open ground;
  - the carts emptied one waiting load at a time even with one founder;
  - eleven broken versions of the templates and the founding file.
- **Mutation check:** 21 deliberate mistakes in the new code; 15 caught at
  first, all 21 once six more tests were added.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `var todo = new Stack<int>(); todo.Push(start); todo.Pop()` | `todo = [start]`, `todo.append(...)`, `todo.pop()` | `FindRooms` |
| `(x, y, w, h) = (h - 1 - y, x, h, w);` | `x, y, w, h = h - 1 - y, x, h, w` | `Work.Lay` |
| `bedrolls[id] with { X = x, Y = y }` | `dataclasses.replace(bedroll, x=x, y=y)` | `MoveBedroll` |
| `public sealed record Area(int X, int Y, int Width, int Height)` with a method | a frozen dataclass with a method | `Stock.cs` |
| `public class FoundingTests(ITestOutputHelper output)` | a test class given a place to print to | `FoundingTests.cs` |

## 2. Exercise

1. In `content/founding.json`, make it 2 founders. Run the first-day test: does
   the house still stand on the first day? What does the test print?
2. Draw a bigger house in `construction.json` (8 by 6, the door on a long
   side). How much timber does it need? Will 60 do?
3. Put a floor (`F`) in the small house's middle row and add `"F": "floor"` to
   its key. Which test fails, and why?
