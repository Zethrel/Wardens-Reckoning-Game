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
| 16 | Save and reload of the whole loop (saved at every hour of two days and carried on, exactly as if never saved); a checksum and the content's fingerprint in each save (warnings on mismatch); written beside, read back, then moved into place, keeping one backup; the colony session that runs time and the morning autosave, reporting failures without stopping play (FP-08, FP-09; library Review 35; lesson 16) | 7 Nov |
| 17a | In the generator: a scale (a small world is 1,000 km across), and a local map of 256 by 256 m for each of up to 24 founding sites (free land, spaced apart, moved to water beside them, those with fresh water first), with brooks from a flow of 5 (library Review 36; the generator's lesson "Local maps") | 9 Nov |
| 17b | The founding on a local map: the export's founding sites and their maps read and checked (`src/World/LocalMap.cs`); the ground made from one (sand 70%, snow 60%, mud 50%; water cannot be walked or built on; trees and rocks are in the way); the founders arrive at its middle and drink from the bank nearest (the river, the brook, the lake), with 60 L of water in the supplies; rooms closed only by walls and doors, as Review 33 says; saves keep the ground as rows of letters (version 10; lesson 17b) | 9 Nov |
| 18a | The colony screen, to see it (library Review 38): the export's founding sites as numbered diamonds on the world map and in a list; founding a colony at one; its local map drawn in plain tiles with a symbol for each surface, trees and rocks, the carts, bedrolls and the drinking place, the colonists with their names; moving and zooming; pause, play and the speeds; the inspector for a tile or a colonist (`src/Colony/Inspect.cs`); saving and loading the colony, with its own autosaves (lesson 18a) | 9 Nov |
| 18b | The colony screen, to build (Review 38): the build panel (B); walls, doors and roads dragged in lines, floors and stockpiles in boxes, the well and the small house (R turns, F mirrors) with every tile previewed and the reason a tile cannot be used; the arrow keys and Enter as well as the mouse; the tile's commands with their keys (pause, resume, cancel, take down, move a bedroll, empty the carts, remove a stockpile; `src/Colony/Placing.cs`, `Inspect.Commands`); what waits and why (J), the developer's trace (F3); the interface size setting; the FP-11 checklist for the maintainer (lesson 18b) | 9 Nov |
| 19a | Water by its uses (library Review 37): the hygiene need, washing at a wash basin (9 L) or on a bank; the water barrel (100 L) and the wash basin (20 L), filled a 20 L load at a time from the nearest source; a river or lake with plenty, a brook's litres by the month, a well's own; in a cold biome's freezing months, frozen brooks, and an ice hole cut in a river or lake each morning; sources that only give (`src/Colony/Water.cs`); saves version 11 (lesson 19a). Cooking water waits for cooking | 9 Nov |
| 19b | Fouled water (library Review 37): rivers, lakes and brooks foul on their own dice (once in 60 days, 3–10 days), fouled water a good of its own that looks like water, mixing, signs seen at the water and told as news; the marks "boil first" and "do not drink"; boiling 10 L for a timber in 30 minutes; the upset stomach on its own dice (`src/Colony/Sanitation.cs`, `src/Colony/Health.cs`); saves version 12 (lesson 19b) | 10 Nov |
| 20a | The first playable's evidence: every scenario test named by its scenario (FP-01 to FP-10), run by one filter and recorded with its result in `docs/acceptance.md`; FP-07 and FP-08 again on a colony founded on a local map with water, fouling, a mark, boiling and illness (`tests/AcceptanceTests.cs`; lesson 20a) | 10 Nov |
| 20b | FP-12 measured: a profiler (`tools/Profile`) running five named scenarios (idle, hauling, arbitration, save, interrupted) with tick-time percentiles, path-finding counters, memory and save times, in `docs/performance.md`; path-finding found to be the bottleneck when many pieces are planned; the FP-11 checklist in `docs/acceptance.md` (lesson 20b) | 10 Nov |
| 20c | Path-finding about twice as fast with the same ways (a priority queue, and neighbours already done passed over), and at most 512 ways kept (the least lately asked for forgotten); measured again; FP-11 passed by the maintainer, and the PC's measurements, in the records (lesson 20c) | 10 Nov |
| | Library Review 39 (adopted 10 Nov): the first playable accepted on the minute clock; one tick is one game second, real time at ×1, speeds Pause, ×1, ×3, ×10, ×30, ×60 (chosen after a throwaway trial of three clocks) | 10 Nov |
| 21a | The second tick in the colony (library Review 39): a tick is a game second; walking is done every second towards where each colonist is heading, everything else on the whole minutes as before; saves version 13 (ticks in seconds, each colonist's heading); the screen keeps its old pace until 21b (lesson 21a) | 10 Nov |
| 21b | The screen on the second tick: the speeds Pause, ×1 (real time), ×3, ×10, ×30, ×60 on the bar and keys 1 to 5, each named with how long a day takes; the clock with its seconds; colonists drawn walking smoothly between two ticks (lesson 21b) | 10 Nov |
| 21c | Each colonist thinks on their own rhythm, every 2 to 7 seconds (`think_seconds`, drawn once, the first thinks spread out); an order is thought over the next second; work and personal tasks counted in seconds from when they began; loading and unloading 5 seconds each (`pickup_seconds`, `drop_seconds`); a finished job makes the workplaces ask again at once; the colony's own work (needs, asking, supplying) stays on the whole minutes; saves version 14 (lesson 21c) | |
| 21d | Every first-playable scenario again on the second tick (FP-08 saving at any second of the minute), and the performance scenarios measured again, with the slowest tick between the minutes and the time a tick has at each speed (lesson 21d) | |
| 22 | Asking for fewer ways: measured where the ways go (choosing jobs nobody can reach); areas, so a way that cannot exist is never searched for (the maintainer's choice over ranking by straight line or one search per colonist, which stay for later if needed); measured on several variations (lesson 22) | |
| 23a | Skills (design library: Skills, Traits and Knowledge Progression; R21-08): Construction (all building) and Food preparation (grinding, boiling), proficiency 0 to 100 shown as ranks; work goes faster or slower by it (with illness); founders drawn with differing skills; saves version 15 (lesson 23a) | |
| 23b | Learning by practice: each piece, recipe and boiling has a challenge, and work teaches most above the colonist, nothing well past them (easy work cannot make a master); aptitude drawn for founders, hidden until 8 hours of practice; decay after a fortnight without practice, never below half the best; three times as fast back up to one's best; saves version 16 (lesson 23b). With it: tests that a piece left half built is finished by another from where it was | |
| 24a | The hybrid clock (library Review 41): ×1 runs 24 ticks a real second (`Calendar.TicksPerRealSecond`), a day lasts an hour; speeds Pause, ×1, ×2, ×3, ×5 (keys 1 to 4), at most a real second at ×5 in a frame; walking, loading and deciding written in real seconds at ×1 (`walk_m_per_real_second` 1.2, `pickup_real_seconds` and `drop_real_seconds` 5, `think_real_seconds` 2 to 7) and turned into ticks once; each colonist's action deadline kept apart from their decision deadline: a wake-up gets on with the work (a timed step ends on its tick, worked out from the work left at their pace; arriving wakes them the tick after), only a think chooses work; loading carries straight on into carrying; an illness showing or ending and a need turning critical wake them; saves version 17; the speed test (lesson 24a). The tests keep the old pace until 24b | |
| 24b | Rebalancing the work and needs for the 60-minute day; the FP evidence again; the profiler measures sustained speed and saving and loading; runs on the maintainer's PC and in the cloud, against the provisional thresholds of Review 41, which the maintainer then approves | |
| 23c | Interest: which allowed job a colonist picks, never how well they do it; shown apart from skill (cannot / does not prefer / not available now) | |
| | Then: personality and convictions (with the first fighting or weapon-making); free time (colonists walking about and doing something of their own in their recreation hours instead of standing still: the maintainer, 11 November 2026, not yet a priority; design library, Colonist Daily Life and Needs §11); basic healthcare; the colony joins the world's years; the Wardens' war, peace, path and rituals; the legacy number; losing when the Wardens fall | |

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
