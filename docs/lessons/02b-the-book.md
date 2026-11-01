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

## 3. Exercise

1. In `src/UI/BookPanel.cs`, set `MostShown = 50`. How far back can you read
   the whole book now?
2. Open a faction that broke away from another: what does "Came to be" say?
   Click its parent in the list and compare their lines of people.
3. In the generator's `chronicle_settings.py`, raise `MOST_PER_ERA`. Export
   again: does **Only the chronicle** show more?
