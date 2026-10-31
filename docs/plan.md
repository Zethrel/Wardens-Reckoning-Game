# The plan

The game's steps, as decided with the maintainer on 31 October 2026 (the
whole decision is in the generator's docs/roadmap.md, "The game, decided 31
October 2026"). Each step has its own tests and a lesson in `docs/lessons/`.

| Step | What | Done |
| --- | --- | --- |
| 1 | Open an export; the map with the sprites; move and zoom; the main menu and the in-game menu | 31 Oct |
| 2 | The years run on: harvest, food, growth, ruin; Next year and Play; saving and loading | 31 Oct |
| 2b | The book of events, and a faction's own history (below) | |
| 3 | Trade | |
| 4 | Rulers and grievance | |
| 5 | War and alliance | |
| 6 | Splits and migration | |
| 7 | Paths, learning and capitals | |
| 8 | Attunement and rituals | |
| 9 | Happenings, threats, signs and chokepoints | |
| 10 | The chronicle as it happens | |
| | Then the Wardens: the player's decisions, the legacy number, losing when the Wardens fall | |

For each step, the generator adds what that system carries from year to year
to the export's `"continue"` (its docs/export-format.md).

## A faction's history (maintainer, 31 October 2026)

One book of every event, and clicking a faction (its row in the list, or one
of its towns on the map) opens the same book with only the entries that name
it, under a card with its path, ruler, belief, government, capital, people
over time and where it came from. One list of events, two ways to read it; the
game's own years add to the same list. Events carry the factions they concern
by number (the game's do already; the generator will add them to the
export's events).
