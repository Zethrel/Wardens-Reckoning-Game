# Lesson 13b: carrying and grinding

Lesson 13a counted the goods; now people move them. A worker (a colonist
with no needs yet: those come in step 14) carries wheat from the founders'
carts to a hand quern, grinds it, and carries the flour to a granary. That is
the library's economy loop, FP-02. Everything happens one minute (tick) at a
time, in `src/Colony/Work.cs`.

## What happens each minute

1. **Workers act**, lowest number first. A worker with a job walks towards
   it, 70 m a minute (straight lines for now; the tile grid comes in step 15),
   loads (a minute), carries, unloads (a minute), or turns the quern (180
   minutes). Someone already standing where the job is starts at once.
2. **Workshops ask.** A workshop with an order (`Work.Order(quern, "grind_wheat", 3)`:
   grind three times) that lacks wheat for its next batch asks for what is
   missing, counting wheat already on its way. The wheat is claimed in the
   nearest store with some free, no more than a person can carry (25 kg) per
   haul. With the wheat at hand, a job to grind is made. Flour the quern has
   made is carried to the nearest store that takes flour.
3. **Workers take jobs.** Each open job, oldest first, goes to the free
   worker nearest to where it starts.

Each pick-up and put-down is an operation of 13a's ledger, numbered when the
job is made, so it can only ever happen once.

## When work is interrupted (FP-05)

- **Stop a worker** (`Work.Stop`). A job they had not yet picked up goes back
  to be taken by someone else; the goods stay claimed for it. Goods already in
  their arms stay there, with the job, until they are resumed
  (`Work.Resume`): their arms are an inventory, so the goods always have a
  keeper.
- **Cancel a job** (`Work.Cancel`). Goods not picked up are let go where they
  are. Goods being carried are taken to the nearest other store that takes
  them. If the workshop still needs them, it simply asks again next minute:
  that is the retry.
- A stopped or cancelled grinding starts again from the beginning; its wheat
  stays claimed. (Since lesson 14b a stopped grinding keeps its progress.)

Why a workshop waits is in `Site.Blocked`, for example:

```
waiting for wheat: 4 more needed, and no store has any free (50 in the stores: 48 kept as a reserve, 2 claimed for other work)
```

A store says which goods it takes (`Inventory.Accepts`; empty: everything).

## The save

Version 3 keeps the work too: each store's accepted goods, the workers (where
they stand, what they carry, their job, whether stopped), the workshops and
their orders, and every job with its step and minutes left. A save made in
the middle of a haul loads and goes on exactly as if it had never been saved.

## 1. Run it

Nothing on screen yet. In the game's folder (PowerShell): `dotnet test tests`.
The new tests are in `tests/WorkTests.cs`. To watch one batch, read
`A_batch_takes_its_walks_loading_and_grinding`: its comment adds up the
minutes (1 + 1 + 2 + 1 + 180 = 185).

## 2. Where the code is

- **`src/Colony/Work.cs`**: `Worker`, `Site` (a workshop where it stands),
  `Job` and its `Step`s, and `Work`: `AddWorker`, `AddSite`, `Order`, `Stop`,
  `Resume`, `Cancel`, and `Tick` with its three parts (`Act`, `Ask`, `Assign`).
- **`src/Colony/Colony.cs`**: a colony made with content has `Work`, and
  `Run` works one tick at a time.
- **`src/Colony/ColonySave.cs`**: the save's work part and its checks.

How the tests prove it (`tests/WorkTests.cs`):
- **FP-02**: three batches ordered: 15 wheat carried and ground, 12 flour in
  the granary, nothing left at the quern, and the ledger shows every step.
- **The minutes**: one batch takes 185 minutes; with no time to load and
  unload, 184; with 3 and 2, 188; a walker goes 70 m a minute.
- **FP-05**: a stopped carrier keeps the wheat and the job until resumed,
  and nobody else takes it; a job stopped before pick-up goes to someone
  else; a cancelled haul takes its cargo to another store and the quern asks
  again; a haul cancelled before pick-up lets the wheat go; a stopped
  grinding starts again. After each, nothing was made or lost.
- **Choices**: the nearest store with free wheat; the nearest free worker,
  the lower number on a tie; never two workers on one job (checked every
  minute for a day with three workers); 60 wheat fetched as 25, 25 and 10.
- **Why it waits**: no free wheat, or no store for the flour.
- **FP-07 and the save**: the same camp works out the same, fingerprint for
  fingerprint; saving and loading in the middle of a haul changes nothing; 14
  broken saves refused with their reasons.
- Mutation check: 29 deliberate mistakes, all caught (seven needed the new
  tests above first).

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `public sealed class Worker(ColonistId id, ...)` (a primary constructor) | `def __init__(self, id, ...)` | `Work.cs` |
| `public TaskId Id { get; init; }` | an attribute set only when made | `Job` |
| `new Job { Id = ..., Kind = ... }` | `Job(id=..., kind=...)` | `Work.Ask` |
| `job.Step is Step.Carrying or Step.Unloading` | `job.step in (Step.CARRYING, Step.UNLOADING)` | `Work.Stop` |
| `var (x, y) = cond ? (a, b) : (c, d);` | `x, y = (a, b) if cond else (c, d)` | `Work.Act` |
| `--job.Left > 0` | `job.left -= 1; job.left > 0` | `Work.Act` |

## 3. Exercise

1. In `content/colony.json`, set `walk_m_per_minute` to 35. Which test
   fails, and by how many minutes? Work it out first.
2. Set `carry_kg` to 3. How many hauls does one batch of 5 wheat need now?
   Check with `A_workshop_asks_only_for_what_is_missing`'s way of looking at
   `camp.Work.Jobs`.
3. In a test, order 2 batches with two workers and stop one of them halfway
   through the first grinding. Who finishes it, and how long does the order
   take now?
