# Lesson 25c: between jobs

In Work hours with nothing ordered, colonists stood where they were. And in a
big colony a colonist could be left without work for a whole block, because
each job went to someone nearer. Review 43 of the design library (Colonist
Daily Life and Needs §11.1) says what they do instead:

- **between jobs** means: in their Work block, with no job of their own and no
  work they may take;
- first they **idle** near their work area, easing nothing;
- after a **wait** (30 game minutes) they may take **free time** near it, at
  **half** its usual easing;
- when a job is added or put down, those between jobs are **offered it at
  once**, and only the one who gets it stops what they are doing.

## Content first: leisure.json

```json
"between_jobs": {
  "wait_minutes": 30,
  "within_m": 25,
  "eases_share": 0.5,
  "idle": { "doing": "between jobs, pottering about", "within_m": 8, "stops": 3, "minutes_each": 4 }
}
```

Every number the review left "set in content" is here, so you can change
them without touching code. `Content.Read` turns them into a
`BetweenJobsRules` record, and the idling into an `Activity` of its own (id
`idle`) that is not in the catalogue, so nobody chooses it as free time.

## Two new things a colonist remembers

```csharp
public long? BetweenSince { get; internal set; }
public (double X, double Y)? WorkedAt { get; internal set; }
```

`long?` is a `long` that may also be `null` ("not between jobs"). `(double X,
double Y)?` is a pair of numbers, or `null`. A tuple like this is a small
bundle of values without writing a class for it.

`BetweenSince` is the tick they were first left without work. It is set with
`??=`:

```csharp
worker.BetweenSince ??= now;
```

`??=` means "set it only if it is null". So a colonist who thinks again, still
without work, keeps the tick they first got there: the wait goes on counting.
That is R43-C01 in one operator. It goes back to `null` in only two places:
when they start work (`StartWork`), and when their Work block ends (`Live`).

`WorkedAt` is set in `Finish`, when a job is done: where they stood. That is
their **work area** (R43-C03). Before they have worked, `Origin` uses the
nearest store or workplace instead, so a colonist at a distant workshop stays
there rather than walking back to the founding camp.

## The order of things in a tick

`Tick` already went: process those due, then those thinking take jobs
(`Assign`). Between jobs comes **after** `Assign`, because only someone who got
no job is between jobs:

```csharp
var took = Assign(Asked(deciding));
foreach (var worker in took)
    StartWork(worker, processed: due.Contains(worker));
foreach (var worker in deciding)
    if (worker.Job == null && Available(worker))
    {
        worker.BetweenSince ??= now;
        if (worker.Outing == null)
            Occupy(worker);
    }
```

`Available` is the old test for "free for work": in Work hours, no job, not
stopped, asleep, collapsed or seeing to a need, nothing urgent. A colonist
holding a job, even one waiting for a delivery, is not available, so never
between jobs (R43-C04).

`Occupy` idles them, or, after the wait, lets them choose free time with the
same `Choose` as in lesson 25a, given `between: true`. That one flag makes it
look around their work area within 25 m, and marks the outing `Between`.

## "At once", but only for the one who gets it

A job can appear between two of a colonist's thinks. Waiting for their think
would leave work undone for seconds; making everyone stop would send several
running for one job. The review (R43-C02) asks for the middle way, and the code
does it with one flag:

```csharp
public bool Reconsider { get; internal set; }
```

Everywhere a job is made, or put down (`Reopen`), sets `Reconsider = true`. On
the next tick, `Asked` adds everyone between jobs to those offered the open
jobs:

```csharp
return deciding.Concat(workers.Values.Where(w => w.BetweenSince != null && !deciding.Contains(w)))
               .OrderBy(w => w.Id.Value).ToList();
```

`Assign` is unchanged: each job to the nearest. So exactly one colonist gets a
new job, and `StartWork` ends what they were doing. Everybody else is not
touched at all: same outing, same wait. A job merely existing interrupts
nobody.

`StartWork` has one subtle part. A colonist taken this way may not have been
processed this tick, so the time since they were last processed has not been
counted yet. It counts it first, as `Process` would:

```csharp
int elapsed = (int)Math.Min(now - worker.Thought, int.MaxValue);
worker.Thought = now;
if (worker.Outing != null)
    Spend(worker, elapsed);
```

Without this, the minutes they had spent sitting by the water would be lost,
and worse, the next time they were processed those minutes would count as
work. The rule from lesson 24a again: time first, then deciding.

`Reconsider` is kept in the save. A job the player orders between two ticks
sets it; a save made just then must remember it, or a loaded game would give
the job out a tick later than the game never saved.

## Half the easing

In `Spend`:

```csharp
double share = o.Between ? content.Leisure.BetweenJobs.EasesShare : 1;
```

`condition ? a : b` picks `a` if the condition is true, `b` otherwise. Free
time between jobs eases at half the rate, so the Recreation hours you give them
are still worth more.

## Tests

`BetweenJobsTests` checks each of Review 43's verification rows: idling near
the work area easing nothing; free time only after the wait and within 25 m;
the exact half rate over whole stays; a new job taken on the very next tick by
one, the others carrying on with their wait unchanged; staying near a distant
work area; nothing between jobs out of Work hours; the same seeds giving the
same day; and saves every 97 ticks that go on as if never saved.

To check the tests test something, each rule was broken on purpose once (the
half rate set to full, `Reconsider` ignored, the work area ignored, the reset
at the end of the block removed): each time, its test failed.

One older test had to change. `ThinkTests` counted a colonist's thinks by
`Thought`, the tick they were last processed. Idling colonists now walk, and
arriving wakes them, which also sets `Thought`. The test now counts thinks by
what only a think does: setting the next think one rhythm ahead.

## Exercise

1. Set `"wait_minutes": 0`. Found a colony at 09:00 and watch: what do they do
   straight away? Set it to `600`: what then?
2. In `Spend`, change the `share` line to `double share = 1;` (the content
   still says 0.5). Run `BetweenJobsTests`. Which test fails, and why is that
   the right test to fail? Put the line back.
3. Set the idle `"within_m"` to `2`. What does pottering about look like now?
4. In the game, let them idle, then place one road. Who goes to build it? Watch
   the others' inspectors: does anyone else stop?
