# Lesson 21c: each colonist thinks on their own

After step 21b, a colonist at 1x could stand still for up to a real minute
before setting off, because everything but walking was decided on the whole
minutes. This step gives every colonist their own **think**: every 2 to 7
seconds they look at what they are doing and decide what comes next. The
colony's own work (needs growing, workshops asking for goods, water filling,
illness) stays on the whole minutes.

## A rhythm drawn once

`content/colony.json` has a new setting:

```json
"think_seconds": [2, 7]
```

When a colonist joins, `AddWorker` draws their rhythm from it:

```csharp
worker.ThinkEvery = colony.DiceFor("thinking").Int(content.ThinkFrom, content.ThinkTo);
worker.Thought = colony.Now.Tick;
worker.NextThink = colony.Now.Tick + 1 + worker.Id.Value % worker.ThinkEvery;
```

- `DiceFor("thinking")` is the colony's own dice for thinking, seeded from the
  colony's seed. No other system rolls them, so adding thinking did not change
  what any other dice give (the game's rule: no dice shared between systems).
- `Int(2, 7)` gives a whole number from 2 to 7, both included.
- `%` is the **remainder**: `5 % 3` is 2. Colonist 4 with a rhythm of 3 thinks
  first `1 + 4 % 3 = 2` seconds after joining, colonist 5 three seconds after.
  That spreads the first thinks out, so the whole camp does not decide in the
  same second.

`Thought` is when they last thought; `NextThink` when they think next.

## The tick

```csharp
public void Tick()
{
    foreach (var worker in workers.Values)
        if (worker.Heading != null)
            Stride(worker);
    if (colony.Now.OnTheMinute)
        Minute();
    long now = colony.Now.Tick;
    var thinking = workers.Values.Where(w => w.NextThink <= now).ToList();
    if (thinking.Count == 0)
        return;
    foreach (var worker in thinking)
        Think(worker);
    foreach (var worker in Assign(thinking))
        Act(worker, jobs[worker.Job!.Value.Value], 0);
    foreach (var worker in thinking)
        worker.NextThink = now + worker.ThinkEvery;
}
```

`Where(w => w.NextThink <= now)` keeps those whose time has come; `w => ...`
is a small function written in place (a **lambda**), like Python's `lambda w:
...`. `ToList()` makes the list there and then: without it, `Where` would look
again each time the list is read, and the answer could change while we are
changing the colonists.

Then three steps, in order:

1. `Think` each of them: what they were doing goes on by the seconds since
   their last think (`elapsed`), and they see to their needs or carry on.
2. `Assign`: those of them free for work take jobs (each open job goes to the
   nearest), and set off at once. Only those thinking now can take a job, so a
   job does not jump to someone who is not thinking.
3. Each sets their next think.

## Counting work in seconds

A job's `Left` is now in seconds. A grinding of 180 minutes starts at
`180 * 60`, and each think takes off the seconds since the last one:

```csharp
if ((job.Left -= Effort(worker, seconds)) > 0)
    return;
```

`a -= b` is `a = a - b`; in C# it also gives the new value, so the line
subtracts and compares in one go. The job ends at the first think after its
time is up, so up to 7 seconds late; the tests allow for that.

An ill colonist works at 0.7 of the pace. Seconds are whole numbers, so the
lost fractions are saved up in `Slow` until they make a whole second:

```csharp
w.Slow += seconds * (1 - content.UpsetStomach.Work);
int lost = (int)Math.Floor(w.Slow + 1e-9);
w.Slow = Math.Max(0, w.Slow - lost);
return seconds - lost;
```

Ten seconds give 7 of work; ten single seconds, one at a time, give 7 too.
The `1e-9` (0.000000001) is there because decimals in a computer are not
exact: 0.3 + 0.3 + 0.3 + 0.1 can come out as 0.99999999..., and `Floor` of that
is 0, not 1.

## An order is thought over at once

When the player stops someone, lets them go on, forbids or allows a kind of
work, or changes their schedule, they think in the next second, whatever
their rhythm:

```csharp
internal void Urge(Worker worker) => worker.NextThink = Math.Min(worker.NextThink, colony.Now.Tick + 1);
```

`Math.Min` keeps the sooner of the two, so an order never puts a think off.
Later steps (a fire breaking out, say) can call the same `Urge`.

## Loading in seconds, and asking at once

Thinking sooner was not enough on its own: in the first try of this step,
colonists still stood for up to a minute, for two other reasons.

- Loading and unloading took a whole game minute each (`pickup_minutes` and
  `drop_minutes`, from the time a minute was the smallest step). They are now
  `pickup_seconds` and `drop_seconds` in `colony.json`, 5 seconds each.
- A wall or a quern asked for its next job only on the whole minute. Now, when
  a job is finished, `Finish` notes where it took from and brought to, and the
  tick lets those workplaces ask again before anyone takes a job:

```csharp
if (stirred.Count > 0)
    AsksAfterFinishing();
```

`stirred` is a `HashSet<long>`: a set of numbers, each in it at most once (like
Python's `set`). Only the quern or the building site the job touched asks
again, not every one: asking all 300 walls of the profiler's test after every
haul cost over a million searches for a way an hour once walls closed
colonists in (each project checks that someone can still reach it).

`Asks` is the asking part that used to sit in `Minute` (workshops, building
projects, stores being emptied, barrels, boiling); `Minute` and founding call
it. The player's orders make the workplace they change ask at once: ordering a
batch asks that quern (`Ask(here)`), planning asks that project
(`Supply(project)`), a stockpile or a mark asks the stores or the water. So new
work is there at the colonists' very next think. Pausing a building now also
drops its hauls not yet picked up, since they would otherwise be taken at once.
Asking twice is harmless, because each workplace looks at how things stand and
counts what is already on its way. The flag is cleared at the start of every
tick, so it never needs saving: a save is made between ticks.

## Saves

Version 14 saves each colonist's `"think": [every, next, thought]`. A save that
says a colonist thinks every 0 seconds, or next thinks in the past, or last
thought in the future, is refused with its reason. Saves of version 13 are
refused, as every older version is until 1.0.

## What to expect at 1x

A colonist given something to do sets off within a few seconds, loads for 5
seconds and unloads for 5. One whose job is done takes the next within a few
seconds, and a new order (a wall just planned, a batch at the quern) is taken
up within a few seconds too. What still waits for the whole minute: needs
growing, water filling and illness, the colony's own slow work.

## Exercise

1. In `content/colony.json`, set `"think_seconds": [1, 1]`. Run the game at 1x
   and watch: what changes? Run `dotnet test tests --filter ThinkTests`: which
   test fails, and why?
2. Set it to `[20, 30]`. How long does someone stand after being given work?
   What does `dotnet test tests --filter ContentTests` say about `[30, 20]`?
3. Put it back to `[2, 7]`. Then set `"pickup_seconds": 60`: how long does a
   colonist now stand at the carts? Put it back to 5.
4. In `Work.Urge`, `Math.Min` keeps the sooner think. Today every order comes
   between two ticks, when everyone's next think is at least a second away, so
   `worker.NextThink = colony.Now.Tick + 1;` would give the same. Why keep
   `Math.Min` for later, when something in the middle of a tick (a fire
   breaking out) calls `Urge` on someone whose think is due this very second?
