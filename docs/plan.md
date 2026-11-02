# The plan

The game's steps, as decided with the maintainer on 31 October 2026 (the
whole decision is in the generator's docs/roadmap.md, "The game, decided 31
October 2026"). Each step has its own tests and a lesson in `docs/lessons/`.

| Step | What | Done |
| --- | --- | --- |
| 1 | Open an export; the map with the sprites; move and zoom; the main menu and the in-game menu | 31 Oct |
| 2 | The years run on: harvest, food, growth, ruin; Next year and Play; saving and loading | 31 Oct |
| 2b | The book of events, and a faction's own history (below) | 1 Nov |
| 3 | Trade | 1 Nov |
| 4 | Rulers, grievance and opinions (with your rules from history_rules.py) | 1 Nov |
| 5 | War and alliance | 1 Nov |
| 6a | Splits, merging and empires (and a rising town founding a faction) | 1 Nov |
| 6b | Feeding by tastes (a town fed by whoever holds it now), and drift | 1 Nov |
| 6c | Migration: colonies founded and ruins resettled | 1 Nov |
| 6d | The lore of the First Death: rulers age by their people's lifespan, and race opinions (lesson 04b; the undead come with the threats in step 9) | 1 Nov |
| 7 | Learning (paths as skills, hybrids) and capitals (offshoot paths not yet: OFFSHOOT_CHANCE is 0) | 1 Nov |
| 8a | Attunement's mark (marking, spreading, fading, rifts, prey, towns given up, blame) and the harvest ritual | 1 Nov |
| 8b | Great rituals in war: the strike, the dead and the mark; the crater does not reshape the land (decided 1 November) | 2 Nov |
| 8c | Offshoot paths: a faction that breaks away founding a path of its own, stances, a path's ritual factor | 2 Nov |
| 9a | The god of death (death sites, hidden metal and finds, beliefs, anger at digging) and her signs | 2 Nov |
| 9b | Named threats: lairs, waking and sleeping, moving, raids, rising; great rituals against a threat | 2 Nov |
| 9c | Happenings (spreading, striking, what lasts), tempers and eras; a faction's fertility in growth (left out until now) | 2 Nov |
| 9d | Chokepoints, part 1: needs, a shortage's effects the year after, embargoes and resentment, stand-ins, smugglers (and the history's contracts, sabotage and debased coin kept and traded by) | 2 Nov |
| 9e | Chokepoints, part 2: new supply contracts, sabotage, labour displacement, secrets, legitimacy, the tech leak, debased coin (every system of the generator's history is now in the game) | 2 Nov |
| 9f | Suspicion (the maintainer's idea): a shortage is blamed on near producers and regular traders, drawn by weight, in the generator and the game | 2 Nov |
| 10 | The chronicle as it happens: each era's most notable game events told in your sentences when the era ends (on the history's grid) | 2 Nov |
| | Then the Wardens: the player's decisions, the legacy number, losing when the Wardens fall | |

For each step, the generator adds what that system carries from year to year
to the export's `"continue"` (its docs/export-format.md).

## A faction's history (maintainer, 31 October 2026)

One book of every event, and clicking a faction (its row in the list, or one
of its towns on the map) opens the same book with only the entries that name
it, under a card with its path, ruler, belief, government, capital, people
over time and where it came from. One list of events, two ways to read it; the
game's own years add to the same list. Events carry the factions they concern
by number, and the export's events the chronicle's sentence where it tells
them; the game's own events show their plain text until their era ends and the
chronicle tells them (step 10).

## The colony, decided (maintainer, 1 November 2026)

The long-run goal is a colony game inside this world (the maintainer's
concept: `docs/concept/Foundation_Game_Concept.md` and
`docs/concept/Prototype_Design.md`): the Wardens' seat played up close, with
colonists, a surface and layered underground, physical logistics and
production chains, while the world simulation of steps 2 to 10 runs the
factions around it (neighbours, caravans, wars, raids), a year at a time.

**First the world game, then the colony.** Steps 5 to 10 and the Wardens are
built as planned; the colony prototype starts on top of them. Weighed and set
aside: the colony replacing the world game (the world a frozen backdrop), and
the colony first.

What the colony will need that the world does not have yet, to plan for then:

- A **local map** for one region of the world map, generated in the game from
  that region's height, biome, water and geology, seeded from the world's two
  seeds and the region's place, so the same site always gives the same ground.
- **Underground layers** made from the region's geology (mines, caves, metal,
  volcanoes), as the generator has no strata.
- **Materials** for the colony (ores, elements, products) beside the world's
  abstract goods per 1000 people, with a mapping between them (a generator iron
  mine gives the colony an iron ore deposit).
- **Two clocks**: the colony runs in days and seasons; the world around it a
  year at a time.
