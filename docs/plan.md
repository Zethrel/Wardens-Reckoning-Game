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
| 12 | The colony's clock and calendar, speed, stable IDs, seeded dice per system, the save and the autosave's timing (FP-01 but its schedule part, FP-07; lesson 12) | 6 Nov |
| 13a | Goods: content files (goods, recipes, everyday numbers) checked at start; inventories, claims, protected reserves and the ledger, every change once (FP-10, FP-05's ledger half; lesson 13a) | 7 Nov |
| 13b | People who carry and grind: workers, workshops and orders, haul and grinding jobs, workshops asking for what they lack, stop, resume and cancel; the save keeps the work (FP-02, FP-05; lesson 13b) | 7 Nov |
| 14a | The colonists' day: hunger, thirst, fatigue; the five schedule blocks (Standard Day); rations, the stand-in spring, bedrolls; urgent needs wait for a delivery in hand, critical ones do not; collapse and care, no death yet (FP-01's schedule part; lesson 14a) | 7 Nov |
| 14b | Who may do which work (kinds of work each colonist may do), why a job waits (for the player, a next step, a developer's trace); a put-down grinding keeps its progress (FP-03, FP-06; lesson 14b) | 7 Nov |
| 15a | Construction on a flat test ground: 1 m tiles and path-finding; walls, doors and floors of timber; construction orders planned before their materials exist, supplied, built and finished; reasons when one waits; pause and cancel (FP-04; lesson 15a). Library Review 32 decided where local terrain comes from: the generator, for founding sites pre-made in its export (step 17) | 7 Nov |
| 15b | Rooms (space closed in by walls and doors counts as indoors), stockpiles and emptying the carts, templates (a small house, turned or mirrored), the founders' arrival (3 founders, 60 timber, 30 rations, 50 wheat, a bedroll each; library Review 33), and the first-day test: the founders build their house and are housed on their first day (lesson 15b) | 7 Nov |
| 15c | Walking speed by what is underfoot (road and floor 100%, grass 80%, rough ground 55%) and by a colonist's legs (100% until body functions); roads (labour only); a well (2 by 2, used from one side, 200 litres a day; the stream stays until step 17); taking pieces down (half the materials back); two workplaces working on their own (library Review 34; lesson 15c) | 7 Nov |
| 16 | Save and reload of the whole loop; a broken or unknown save refused safely (FP-08, FP-09) | |
| 17 | Local terrain from the world generator for the founding sites (R26-05); the generator needs a scale in metres and a local-terrain maker | |
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
  that region's height, biome, water and geology (by the generator, for
  founding sites pre-made in its export: decided 7 November, step 17), seeded from the world's two
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
  the generator (decided 7 November, library Review 32): its export pre-makes
  local maps for a list of founding sites, and the player chooses among them
  (players have no Python when founding). Built at step 17; until then the
  colony stands on the flat test ground of `content/construction.json`.
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
