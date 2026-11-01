# Lesson 2b: the book

Everything that happened is now in one book, on the left of the map. Click a
faction in the list on the right, or one of its towns on the map, and the
same book shows only that faction's entries, under a card about it.

## 1. Run it

1. Export a world again in the generator (its events now say which factions
   they concern, and carry the chronicle's sentences):
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, and press **Book** (or **B**).
3. Click a faction on the right. Drag the year back: the card and the entries
   follow the year. Tick **Only the chronicle** for the notable events alone.
4. Press **Next year** a few times: the game's own events join the same book.

## 2. Where the code is

- **`src/World/Book.cs`**: `BookEntry` is one entry. `Book.FromExport` reads
  the export's events; `Book.Read` picks the entries to show (up to a year, one
  faction or all, chronicle only or not), the newest first; `Book.Card` makes a
  faction's card from its record in that year.
- **`src/Sim/Game.cs`**: `Game.Record` writes each of the game's events into
  `Events` (which a save keeps) and into the same book.
- **`src/UI/BookPanel.cs`**: the panel; `PeopleGraph` draws the line of a
  faction's people over the years.
- **`src/World/MapYear.cs`** `TownAt`: which town a click on the map hits.
- In the generator, `north/export.py` `event_factions` works out which
  factions each event concerns: the factions its facts name, or else whoever
  held its place that year.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `Func<IReadOnlyList<BookEntry>> entries` | passing a function to call later | `BookPanel` |
| `record BookEntry(...)` with `public string Shown => ...` | a small class with a property worked out when asked | `Book.cs` |
| `shown.Take(MostShown)` | `shown[:MOST_SHOWN]` | `BookPanel.Refresh` |
| `line.GuiInput += input => { ... }` | connecting a function to a signal | `MapScreen.ShowLegend` |

### Searching the book (added 1 November 2026)

The box under "Only the chronicle" searches the book as you type:

- **text** anywhere in an entry, in any case: a town's name, a ruler's,
  "famine". The kind of entry counts too, so "colony" finds every colony
  founded and "rebellion" every rising;
- **a year**: `315`;
- **a span of years**: `310-320`, `310-` (from 310 on), `-320` (up to 320).

It works with a faction's own book and with "Only the chronicle", and never
shows years past the one on the map. Enter, or a click on the map, gives the
keys back to the map (while you type, W A S D and the arrows do not move it).
The code: `Book.Search` in `src/World/Book.cs` (`Parse` reads what was typed,
`Finds` says whether an entry matches), the box in `src/UI/BookPanel.cs`.

### The year said once (added 1 November 2026)

Every entry shows its year in front, so a chronicle sentence that opens with
it would say it twice ("262   In 262, the Whisperward took..."). The book
now drops that opening: "In 262, " and "By 262, " go, "From 262, " becomes
"From then on, ", and "After a long battle in 30, " becomes "After a long
battle, ". A year anywhere else in the sentence stays, and the generator's
`chronicle.md` keeps every sentence as written. The code: `BookEntry.Shown`
and `BookEntry.WithoutYear` in `src/World/Book.cs`; the tests are in
`tests/BookTests.cs`.

## 3. Exercise

1. In `src/UI/BookPanel.cs`, set `MostShown = 50`. How far back can you read
   the whole book now?
2. Open a faction that broke away from another: what does "Came to be" say?
   Click its parent in the list and compare their lines of people.
3. In the generator's `chronicle_settings.py`, raise `MOST_PER_ERA`. Export
   again: does **Only the chronicle** show more?
