# The plan

The game's steps, as decided with the maintainer on 31 October 2026 (the
whole decision is in the generator's docs/roadmap.md, "The game, decided 31
October 2026"), and from step 11 on as decided on 6 November 2026 (below,
"The colony in the generated world"). Each step has its own tests and a lesson
in `docs/lessons/`.

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
| 11 | Record the 6 November decisions: the design library (Reviews 26 to 28), the generator's roadmap, this plan | 6 Nov |
| 12 | The colony's clock and calendar, stable IDs, seeded dice per system, a minimal save (FP-01, FP-07) | |
| 13 | Goods: physical inventory, reservations, one production chain, an interrupted haul, checking the content files (FP-02, FP-05, FP-10) | |
| 14 | Colonists: needs and schedules; why a job is blocked (FP-03, FP-06) | |
| 15 | Construction; where the local terrain comes from is decided here (FP-04) | |
| 16 | Save and reload of the whole loop; a broken or unknown save refused safely (FP-08, FP-09) | |
| | Then: the colony joins the world's years; the Wardens' war, peace, path and rituals; the legacy number; losing when the Wardens fall | |

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

## The colony, decided (maintainer, 1 November 2026; its order replaced on 6 November)

The long-run goal is a colony game inside this world (the maintainer's
concept: `docs/concept/Foundation_Game_Concept.md` and
`docs/concept/Prototype_Design.md`): the Wardens' seat played up close, with
colonists, a surface and layered underground, physical logistics and
production chains, while the world simulation of steps 2 to 10 runs the
factions around it (neighbours, caravans, wars, raids), a year at a time.

**First the world game, then the colony.** Steps 5 to 10 and the Wardens are
built as planned; the colony prototype starts on top of them. *(Replaced on 6
November: steps 5 to 10 are built; the colony comes next and the Wardens'
decisions after it. See below.)* Weighed and set
aside: the colony replacing the world game (the world a frozen backdrop), and
the colony first.

What the colony will need that the world does not have yet, to plan for then:

- A **local map** for one region of the world map, generated from
  that region's height, biome, water and geology (in the game or by the
  generator: decided at step 15), seeded from the world's two
  seeds and the region's place, so the same site always gives the same ground.
- **Underground layers** made from the region's geology (mines, caves, metal,
  volcanoes), as the generator has no strata.
- **Materials** for the colony (ores, elements, products) beside the world's
  abstract goods per 1000 people, with a mapping between them (a generator iron
  mine gives the colony an iron ore deposit).
- **Two clocks**: the colony runs in days and seasons; the world around it a
  year at a time.

## The colony in the generated world (maintainer, 6 November 2026)

The maintainer wrote a design library for the game, outside this repository:
`D:\Zethrel-Github-Projects\._MASTER_Wardens_Reckoning_Design`. Its canonical
documents are listed in its `Canonical_Document_Manifest.md`, and every change
to it goes through its own change control (a review with snapshots, diffs and
hashes, adopted only on the maintainer's word). It now holds the decisions
below (Review 26, Master Architecture "Review 26").

- **Option A: the library is the game, built on what exists.** The generator
  makes the world and its history; this repository's `src/Sim` plays that
  history on as the **regional tier**, for every faction but the player's;
  a new **colony tier** (`src/Colony`, plain .NET like `src/Sim`) runs the
  player's settlement as the library describes: colonists with needs and
  schedules, a 336-day calendar, tiles and underground levels, goods that are
  carried. Godot stays in `src/UI` only. Weighed and set aside: two separate
  games, and starting the colony afresh without the world.
- **The start**: a new Warden settlement. A random assortment of colonists
  arrives at a spot the player chooses in the generated world, with supplies
  they brought to found it. How many, and which supplies, is open.
- **The lore stays**: the Wardens' belief is Death's vision (the generator's
  docs/world.md). The endings stay: endless, the legacy number, lost when the
  Wardens fall.
- **The local terrain** (tiles and underground around the colony) comes from
  the generator's export or from the game: decided at step 15, after gauging
  the generator's share of the work. The maintainer leans to the generator.
- **Steps 12 to 16** build the library's first playable: its scenarios FP-01
  to FP-10 (in `Review_25_Final_Baseline/First_Playable_Acceptance_Criteria.md`)
  as tests. A scenario counts only when its test has run and passed. FP-11
  (accessibility) and FP-12 (performance) are checked along the way, by the
  maintainer in Godot's window where a person is needed.
- **The first playable needs no world**: the colony is joined to `src/Sim`'s
  years after step 16. How the two tiers exchange state, and how often, is
  decided then.
- **The Book**: it shows the whole history, which is the world's truth. In the
  library's terms it is a developer's view; the player's own view of the
  history (only what the Wardens know) comes later.

The concept notes in `docs/concept/` are the earlier version of these ideas;
where they differ, the library is the one that holds.
