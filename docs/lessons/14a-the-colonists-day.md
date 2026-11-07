# Lesson 14a: the colonists' day

Until now the workers were tireless. Now they get hungry, thirsty and tired,
eat, drink and sleep, and keep a daily schedule, as the design library's
Colonist Daily Life and Needs describes. The code is `src/Colony/Life.cs`, the
second half of `Work` (C#'s `partial class`: one class in two files).

## Needs

Hunger, thirst and fatigue each run from 0 (none) to 100 (the limit), growing
every minute by the rates in `content/needs.json` (hunger and thirst more
slowly in sleep; fatigue only awake, and sleep eases it). Each has four
levels, your first values:

| Level | Hunger / thirst | Fatigue | What the colonist does |
| --- | --- | --- | --- |
| peckish | 15 | 50 | eats or drinks at mealtimes |
| wants | 35 | 70 | sees to it when free (rests in free time) |
| urgent | 65 | 85 | takes no new work; finishes a delivery in hand, then sees to it |
| critical | 90 | 95 | drops what they are doing now; goods in their arms stay there |

Hunger grows 6 an hour awake: after breakfast at 7, a colonist is peckish by
the noon meal, wants a third meal in the evening, and sleeps through the
night. That gives the two or three meals a day you asked for, varying with
work and sleep: someone hungry with a delivery in hand finishes it first.

## The day

Each hour of a colonist's schedule is one of the library's five blocks
(`content/schedules.json`; the Standard Day: sleep 23 to 7, meals 7 and 12,
work 8 to 12 and 13 to 17, recreation 17 to 20, free time 20 to 23):

- **Sleep**: they go to their bedroll (or sleep where they are) and stay
  asleep through the sleep hours; they wake once those are over and they are
  rested, or at once for critical hunger or thirst.
- **Meals**: they eat and drink if even peckish.
- **Work**: they take jobs; only colonists in a Work hour, with no urgent
  need, take jobs. A grinding is put down when the hour ends (it starts again
  later; its wheat stays claimed).
- **Recreation** and **Flexible**: they see to what they want, or rest if tired.

Eating takes a ration (20 minutes); drinking a litre of water from the spring
(2 minutes). The spring is a stand-in until rivers and wells: each morning at
06:00 it is filled back to 200 litres, written in the ledger as water arriving
from outside. Sleeping on a bedroll eases 15 fatigue an hour, on the ground 10.

## Collapse

Fatigue at 100 collapses a colonist at once: they fall asleep where they
stand and get up once below 70. Hunger or thirst at 100 for 72 or 24 hours
collapses them too: they can do nothing; a colonist in work hours brings them
a ration or water (a haul to their arms), which they eat or drink at once.
Nobody dies of it yet: death belongs to Healthcare, which comes later.

`Work.Doing(colonist)` says what a colonist is doing in words ("asleep on a
bedroll", "going to eat", "collapsed (hunger)"...), and `Worker.Lacking` what
they could not have ("no travel ration free in any store").

## 1. Run it

Nothing on screen yet. In the game's folder (PowerShell): `dotnet test tests`.
The new tests are in `tests/LifeTests.cs`; the step 13 tests (`WorkTests`) now
run with needs that never grow and every hour a work hour, so they test the
work alone.

## 2. Where the code is

- **`content/needs.json`**, **`content/schedules.json`**, and the rations
  and water in `goods.json` (explained in `content/README.md`).
- **`src/Colony/Life.cs`**: `Live` (a colonist's minute for themselves),
  `Grow`, `Collapse`, `Recover`, `Wake`, `Wish` (what to see to now, in the
  library's order), `Start` and `Continue` (walking there, eating, drinking or
  lying down), `Care`, `Doing`; bedrolls and the spring.
- **`src/Colony/Work.cs`**: `Worker` gains its needs, schedule, sleep and
  collapse; `Tick` begins with `Live`; `Assign` asks `Available`.
- **`src/Colony/Content.cs`**: reads and checks the two new files.
- **`src/Colony/ColonySave.cs`**: save version 4 keeps all of it.

How the tests prove it (`tests/LifeTests.cs`):
- **A standard day**, three days long with a grinding order: two or three
  meals a day, drinks, every night on the bedroll, never collapsed, and
  grinding only in work hours.
- **FP-01's schedule part**: work ordered at 17:00 waits until 08:00; a
  grinding is put down at the noon meal.
- **The levels**: each starts exactly at its threshold; peckish eats at the
  meal hour, not at work; urgent hunger finishes the delivery first; critical
  hunger drops it, the wheat staying in her arms; nobody takes work urgently
  hungry or tired; with no work she eats when hungry.
- **Sleep**: bedroll faster than ground; she wakes at 07:00 rested, or at
  once for critical thirst; she rests in free time when tired, on her own
  bedroll.
- **Collapse and care**: starved three days she collapses, and once rations
  arrive Bram brings her one; exhausted she falls asleep where she stands and
  is up after three hours.
- **The spring**, **words**, **schedules** and **the save** (one made in the
  middle of a meal goes on exactly the same; nine broken ones are refused);
  and 16 broken versions of the two new content files.
- Mutation check: 32 deliberate mistakes; 31 caught (eight needed new
  tests first), and the 32nd showed a check that could never matter, now
  removed.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `public sealed partial class Work` | one class, its methods in two files | `Work.cs`, `Life.cs` |
| `w.Personal is { Claim: ReservationId claim }` | `p is not None and p.claim is not None` | `Collapse` |
| `(PersonalKind? Wish, int Level)` | returning `(wish, level)`, `wish` maybe `None` | `Wish` |
| `(p.Kind, p.Left) switch { (PersonalKind.Eat, null) => ... }` | `match (p.kind, p.left): case (EAT, None): ...` | `Doing` |
| `Math.Clamp(x, 0, 100)` | `min(max(x, 0), 100)` | `Grow` |
| `internal` with `InternalsVisibleTo` | a name the tests may use, but nobody else | `Colony.csproj` |

## 3. Exercise

1. In `content/needs.json`, make hunger grow 9 an hour. Run the tests: which
   fail, and how many meals a day do the colonists eat now?
2. Change the Standard Day in `content/schedules.json` so meals are at 7, 12
   and 18 (and recreation 17 and 19 to 20). What does
   `A_standard_day_has_two_or_three_meals...` say now?
3. Give one colonist no bedroll. After a night, how much more tired is she
   than the others? Work it out from `needs.json` first.
