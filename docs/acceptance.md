# The first playable: evidence

The design library's first-playable scenarios (`000 - Revisions and
Amendments/Review_25_Final_Baseline/First_Playable_Acceptance_Criteria.md`),
and the evidence for each in this repository. The library accepts the first
playable when FP-01 to FP-11 have **demonstrated PASS** and FP-12 has measured
results; a scenario without a run is not a pass. This file is the evidence;
the library records the result only through its own review, on the
maintainer's word.

## How the evidence is run

Every scenario test's name begins with its scenario: `FP04_...` belongs to
FP-04. All of them, and nothing else:

```powershell
# PowerShell, in D:\Zethrel-Github-Projects\Wardens-Reckoning-game
dotnet test tests --filter "FullyQualifiedName~.FP" --logger "console;verbosity=normal"
```

One scenario only: `--filter "FullyQualifiedName~.FP04_"`. With
`--logger "console;verbosity=normal"` every test is listed with its result.

The tests set up everything they use (no files from outside the repository,
no clock but the colony's own); where dice play a part, the seed is written in
the test. The same command on the same commit gives the same results on any
machine.

## The scenarios

| ID | What the library asks | Tests (cases) | Seen by the maintainer |
|---|---|---|---|
| FP-01 | The same clock and identifiers from the same commands; identifiers unique and kept; work taken only in its hours | 3 (3) | — |
| FP-02 | A good obtained, stored, carried to a workplace, made into a product and stored, without making or losing any | 1 (1) | Goods in the inspector (18a, 9 Nov) |
| FP-03 | Needs and permitted work shape a colonist's day without overruling who may do what | 2 (2) | — |
| FP-04 | A construction order directed by the player; claims, hauling, work and the finished piece consistent; reasons when it cannot be done | 12 (12) | Building from the colony screen (18b, 9 Nov) |
| FP-05 | A delivery stopped, cancelled and tried again: every unit somewhere and with someone, each change done once | 7 (7) | — |
| FP-06 | A job that waits names its cause and a next step, without telling hidden truth; the developer's trace kept apart | 6 (6) | What waits (J) and the trace (F3) (18b, 9 Nov); fouled water seen as water (19b, 10 Nov) |
| FP-07 | The same start, seed and commands give the same state at every checkpoint | 6 (6) | — |
| FP-08 | Saved with jobs, claims and goods in hand; loaded, it goes on with the same identities and nothing done twice | 4 (4) | Saving and loading from the menus (18a, 9 Nov) |
| FP-09 | A broken or unsupported save is refused with a reason, and the file is not harmed | 5 (10) | — |
| FP-10 | Broken content (duplicate ids, missing references, bad numbers) is caught before play, with where it is | 3 (93) | — |

"Cases": a test given several inputs counts once for each.

### FP-01: time, identity and scheduling

- `ColonyTests.FP01_the_same_commands_give_the_same_clock_and_identifiers`
- `ColonyTests.FP01_identifiers_are_unique_and_carry_on_after_a_save`
- `LifeTests.FP01_a_job_is_taken_only_in_a_work_hour`: a job ordered at 17:00
  (recreation) is not taken until 08:00 the next morning.

### FP-02: the economy loop

- `WorkTests.FP02_wheat_is_carried_ground_and_its_flour_stored`: three
  batches of wheat carried from the carts to the quern, ground, and the flour
  carried to the granary; every unit counted (`Conserved`), and the ledger
  shows each move, use and making.

### FP-03: needs and priorities

- `PolicyTests.FP03_a_colonist_never_takes_work_they_are_not_allowed`
- `PolicyTests.FP03_permission_does_not_overrule_needs_or_hours`

### FP-04: construction

All in `BuildTests`: planned, supplied, built and standing; the pieces' own
minutes; planned before the materials exist and saying what it lacks; a bad
place refused with its reason; a place nobody can reach; nobody allowed to
build; paused and resumed; paused before supply; cancelled with its timber
returned; every unit given back on cancelling; a builder working from beside
the tile.

### FP-05: interrupted haul

- `StockTests.FP05_an_operation_is_done_exactly_once`
- `StockTests.FP05_a_thousand_random_operations_never_lose_or_make_goods`
- `WorkTests.FP05_*`: a carrier stopped and resumed keeps the goods; a job
  stopped before pickup taken by someone else; a cancelled haul taking its
  cargo to a store and tried again; cancelled before pickup letting its goods
  go; a stopped grinding carried on by another.

### FP-06: blocked-work explanations

- `PolicyTests.FP06_*`: nobody allowed (and what to do); out of work hours
  (until when); too hungry with nothing to eat (bring rations); busy, stopped,
  collapsed; asleep or seeing to themselves.
- `SanitationTests.FP06_the_player_sees_fouled_water_as_water_and_only_the_trace_tells_it_apart`:
  the hidden-truth part: fouled water is "Water" to the player; only the trace
  tells it apart.

### FP-07: determinism

- `ColonyTests.FP07_the_same_seed_and_commands_give_the_same_fingerprints`:
  seed 42, a fingerprint after each command.
- `ColonyTests.FP07_saving_and_loading_halfway_changes_nothing`
- `ColonyTests.FP07_another_seed_rolls_otherwise`: seeds 42 and 43.
- `WorkTests.FP07_the_same_camp_works_out_the_same`
- `WorkTests.FP07_saving_and_loading_in_the_middle_of_a_haul_changes_nothing`
- `AcceptanceTests.FP07_a_colony_on_a_local_map_plays_out_the_same_twice_and_otherwise_with_another_seed`:
  (step 20a) the colony as it is now: founded on a local map by a brook,
  water carried, washing, the brook fouling, a mark, boiling and illness;
  four days, a fingerprint every midnight; seeds 7 and 8.

### FP-08: save and reload

- `ColonySaveTests.FP08_saved_at_any_hour_the_colony_goes_on_exactly_as_if_it_had_never_been_saved`:
  the flat test ground, 40 hours, saved every hour and whenever goods are in
  someone's arms; every copy ends exactly as the colony never saved.
- `ColonySaveTests.FP08_identities_go_on_from_where_they_were`
- `ColonySaveTests.FP08_a_save_made_after_pieces_are_finished_or_cancelled_loads`
- `AcceptanceTests.FP08_a_colony_on_a_local_map_saved_at_any_of_its_hours_goes_on_exactly_as_if_never_saved`:
  (step 20a) the same on a local map with everything since: four days, saved
  every two hours (48 saves), with water in someone's arms, the brook fouled,
  a store marked, a boiling under way and someone ill along the way.

### FP-09: save failure handling

- `ColonySaveTests.FP09_a_damaged_file_is_refused_with_a_reason_and_left_as_it_is`
- `ColonySaveTests.FP09_a_save_changed_outside_the_game_is_only_loaded_if_the_player_says_so`
- `ColonySaveTests.FP09_a_save_made_with_other_content_loads_with_a_warning`
- `ColonySaveTests.FP09_a_save_that_cannot_be_written_leaves_the_old_one_as_it_was`
- `ColonySaveTests.FP09_the_save_a_new_one_replaces_is_kept_as_a_backup`

Migration: none is supported. Saves of an older version are refused until 1.0
(library Review 30), and that refusal is tested in `ColonyTests` and
`ColonySaveTests` (the version tests).

### FP-10: content validation

- `ContentTests.FP10_broken_content_is_caught_and_named`: 91 broken
  variations of the real content, each refused with where it is.
- `ContentTests.FP10_broken_JSON_and_a_missing_file_are_named`
- `ContentTests.FP10_every_problem_is_reported_at_once`

## Runs

| Date | Where | Commit | Result |
|---|---|---|---|
| 10 Nov 2026 | Cloud (Linux, .NET 8) | step 20a | 144 cases passed, 0 failed (49 tests) |
| 10 Nov 2026 | The maintainer's PC (Windows) | step 20a | 144 cases passed |

Every FP case passed in the cloud run: FP-01 3, FP-02 1, FP-03 2, FP-04 12,
FP-05 7, FP-06 6, FP-07 6, FP-08 4, FP-09 10, FP-10 93.

## FP-11: accessible core interactions

Checked by a person: "keyboard/mouse operation, visible focus, legible
scalable text, and meaningful non-colour-only feedback work for the first
playable's central workflows". The checklist (from lesson 18b, with the water
of step 19):

**Keyboard only** (the mouse put aside):

1. Main menu: Tab and the arrows reach every button, the focused one is
   visibly marked, Enter presses it.
2. Open a world; pick a founding site from the list on the right; Enter on
   **Found a colony here**.
3. In the colony: Space, 1, 2, 3; Tab through the colonists.
4. B, the arrows to **Small house**, Enter; the arrows move the outline; R and
   F; Enter places it.
5. B, **Stockpile**: Enter, the arrows, Enter.
6. B, **Timber wall**: Enter, the arrows along a line, Enter.
7. A project (J and its **Go to** button): P, P, X.
8. The water: inspect its tile (with the mouse: see the known gap below),
   then K, N and U from the keyboard.
9. Esc puts down the tool; Esc again opens the menu.

**Mouse only:** the same, by clicking and dragging.

**Text:** Settings, Interface size, at 150% and 200%: everything readable,
nothing cut off or overlapping.

**Not by colour alone:** a tile that cannot be built is crossed out, not only
red; the speed in use is named in the Pause button's tooltip; paused projects
are crossed through; each surface has a symbol; a marked store says so in
words in the inspector.

**Known gap:** picking a tile to inspect needs the mouse, or J's list (a
keyboard tile cursor would close it).

| Date | Who | Result |
|---|---|---|
| 9 Nov 2026 | The maintainer | Interface size 200%: the bar and the build panel cut off; fixed in 18b |
| 10 Nov 2026 | The maintainer | The whole checklist: nothing failed |

## FP-12: performance

Measured, not passed or failed: `docs/performance.md`.

## Still to come

- **FP-12**: the path-finding bottleneck (steps 20c and 20d,
  `docs/performance.md`).
- Then a library review recording the results, adopted only on the
  maintainer's word.
