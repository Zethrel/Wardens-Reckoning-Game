# Lesson 14b: who may work, and why work waits

Two things the design library asks of the first playable: the player decides
who may do which work, and the colonists' needs and hours still come first
(FP-03); and when work waits, the game says why, and what to do about it
(FP-06). The code is `src/Colony/Why.cs`, a third part of `Work`.

## Kinds of work

Every job is of a kind: `haul` (carry goods), `care` (bring food or water to
a fallen colonist) or the id of the workshop it is worked at (`quern`).
`Work.Kinds` lists them. Each colonist may do every kind until you forbid one:

```csharp
camp.Work.Permit(ada.Id, Work.HaulWork, false);   // Ada no longer carries
camp.Work.Permit(ada.Id, Work.HaulWork, true);    // she may again
```

A colonist is only given jobs of kinds they may do, and only when they are
free for work: in a Work hour, awake, with no urgent need (lesson 14a). Being
allowed never overrules the needs or the hours. Forbidding a kind a colonist
is busy with gives the job back to the others, except a delivery in hand: the
goods in their arms are still theirs to deliver.

## Why a job waits

`Work.Why(job)` explains a job nobody has taken, three ways:

- **`Player`**: what is wrong, for the player, naming each colonist who may do
  it and why they are not:
  `Waiting for someone to carry goods: Ada is busy (bring wheat to the hand
  quern); Bram is stopped; Cael has collapsed (fatigue).`
- **`NextStep`**: what the player could do: `Resume Bram.`, `Allow someone to
  carry goods in their work settings.`, `Bring rations to a store.`
- **`Trace`**: for the developer, the numbers behind it (job, colonists,
  tick), kept out of the player's words:
  `task 7 (haul, bring wheat to the hand quern) open at tick 145150080: worker 1: busy task 5; worker 2: stopped; ...`

The player's words only use what the colony knows about itself, as the
library asks: nothing hidden in the world.

## Changed on the way

A grinding put down (at the end of a work hour, for a meal, or by stopping
the colonist) now keeps its progress: whoever takes it next carries on where
it was left. With schedules, starting again from nothing at every meal meant
a 180-minute grinding was rarely finished. A cancelled grinding is still gone.

Saves are version 5 (each colonist's forbidden kinds of work).

## 1. Run it

Nothing on screen yet. In the game's folder (PowerShell): `dotnet test tests`.
The new tests are in `tests/PolicyTests.cs`.

## 2. Where the code is

- **`src/Colony/Why.cs`**: `HaulWork`, `CareWork`, `Kinds`, `KindOf`,
  `Permit`, `Why`, and `NotNow` (one colonist's reason).
- **`src/Colony/Work.cs`**: `Worker.Forbidden`; `Assign` asks whether the
  kind is allowed; `Reopen` keeps a grinding's progress.
- **`src/Colony/ColonySave.cs`**: version 5.

How the tests prove it (`tests/PolicyTests.cs`):
- **FP-03**: over two days, Ada (no carrying) and Bram (no grinding) never
  take forbidden work, and the order is finished between them; an allowed,
  urgently hungry colonist eats first; in the evening nobody works; a job is
  taken back when forbidden, but not a delivery in hand; only existing kinds
  can be named; care is a kind of its own.
- **FP-06**: nobody allowed (and what to do); out of work hours (until when);
  too hungry with nothing to eat (bring rations); busy, stopped, collapsed,
  asleep and going to drink, each named; nothing to explain for a job being
  done; and the player's words carry no task numbers or ticks.
- **The save** keeps each colonist's forbidden kinds, and refuses unknown ones.
- Mutation check: 16 deliberate mistakes, 15 caught (one needed a new test);
  the 16th changes nothing (it only matters in a work hour, when the question
  is never asked).

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `public const string HaulWork = "haul";` | `HAUL_WORK = "haul"` on the class | `Why.cs` |
| `kind switch { HaulWork => "...", _ => "..." }` | `match kind: case HAUL_WORK: ...` | `Words` |
| `public sealed record Explanation(string Player, string NextStep, string Trace);` | a frozen dataclass of three strings | `Why.cs` |
| `w.Forbidden.Contains(kind)` on a `SortedSet<string>` | `kind in w.forbidden` on a `set` | `Assign` |

## 3. Exercise

1. In a test, forbid every colonist `quern` and order a grinding. What does
   `Work.Why` say once the wheat is at the quern?
2. Give Bram a schedule with no work hours at all (add one to
   `content/schedules.json`). What does his part of the explanation say?
3. Forbid Ada `care`, then let her be the only colonist standing when Bram
   collapses. What does the care job's explanation suggest?
