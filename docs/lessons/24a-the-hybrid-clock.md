# Lesson 24a: the hybrid clock

Until now a game second lasted a real second at ×1: a day took 24 real hours.
You wanted a 60-minute day without colonists racing about (library Review 41,
the hybrid clock). This step does it with **one clock** still: a tick is still
a game second, but at ×1 the game runs 24 of them every real second. The
things you watch move (walking, loading, how often someone decides) are written
in real seconds, so they look the same as before; the rest (building, eating,
the hours of the day) stays in game time.

And it fixes what you saw in the trial: Hild arrived at the cart at 15:10, was
carrying the water at 15:15, and only set off at 15:16.

## One constant

```csharp
public const int TicksPerRealSecond = 24;
```

It lives in `Calendar` and is used for exactly two things: the pacer (how many
ticks to run in a frame) and turning the content's real seconds into ticks.
A `const` is a value fixed when the program is built; every place that needs 24
names it, so it can never disagree with itself.

The speeds are now Pause, ×1, ×2, ×3 and ×5 (keys 1 to 4):

```csharp
owed += seconds * Calendar.TicksPerRealSecond * (int)Speed;
```

`(int)Speed` turns the enum into its number: `Speed.X5` is 5. A frame never
runs more than `MostTicksAFrame` (120, a real second at ×5): if the computer
cannot keep up, the game slows down. It never skips a tick.

## Real seconds into ticks

`colony.json` now says:

```json
"walk_m_per_real_second": 1.2,
"pickup_real_seconds": 5,
"drop_real_seconds": 5,
"think_real_seconds": [2, 7]
```

When the content is read, each is turned into ticks once, as the library says:

```csharp
public static int TicksOf(double realSeconds)
{
    double ticks = Math.Round(realSeconds * TicksPerRealSecond, MidpointRounding.AwayFromZero);
    return realSeconds > 0 ? Math.Max(1, (int)ticks) : 0;
}
```

5 seconds are 120 ticks. `Math.Round` normally rounds a half to the nearest
*even* number (2.5 becomes 2), which surprises most people; the library wants
halves rounded away from zero (2.5 becomes 3), so we say so with
`MidpointRounding.AwayFromZero`. Anything above 0 is at least one tick, so a
tiny value never vanishes.

The walking speed is a *rate*, not a duration, so it is only divided, never
rounded: 1.2 / 24 = 0.05 m a tick. That makes walking slow in game time (140 m
take 48 game minutes) while it looks like 1.2 m a second on screen.

## Two deadlines

Before this step a colonist did everything at their think, every 2 to 7
seconds. Now that a think comes every 48 to 168 ticks, a step that ended between
two thinks would wait up to 168 ticks: that was Hild's minute at the cart. So
each colonist now has two deadlines (`Worker`):

```csharp
public long NextThink { get; internal set; }   // the decision deadline
public long? ActionDue { get; internal set; }  // the action deadline
```

`long?` is a `long` that may also be `null`: nothing due (walking, standing,
asleep). Each tick, everyone whose deadline has come is processed, once, in
the order of their numbers:

```csharp
var due = workers.Values.Where(w => w.NextThink <= now || w.ActionDue <= now).ToList();
```

(`w.ActionDue <= now` is simply false when `ActionDue` is `null`.)

`Process` then does two things, in this order:

1. **The time that has passed.** Whatever they were doing goes on by the ticks
   since they were last processed: a load gets lighter, a wall nearer done. A
   step whose work is done moves on to the next one.
2. **Only at a think: deciding.** Needs first (`Live`); a free colonist takes a
   job (`Assign`).

A colonist woken by their action deadline does only the first. That is the
rule you approved: a wake-up gets on with the work; only a think chooses new
work, on its own 2 to 7 second rhythm.

Doing the first part first also matters when a think puts the job down (a need
turning critical): the work done up to that tick is counted before the job
goes back.

## Working out the deadline

After processing, `Schedule` works out the action deadline again from what is
left. Loading, unloading and eating are simple: `now + job.Left`. Work goes at
the colonist's pace (skill times health), so `TicksToFinish` looks for the
fewest ticks whose work reaches what is left:

```csharp
long ticks = Math.Max(1, (long)Math.Ceiling((job.Left - w.Carry) / pace));
while (Math.Floor(w.Carry + ticks * pace + 1e-9) < job.Left)
    ticks++;
while (ticks > 1 && Math.Floor(w.Carry + (ticks - 1) * pace + 1e-9) >= job.Left)
    ticks--;
```

The division gives the answer, nearly. Decimal numbers in a computer can be a
hair off (0.1 + 0.2 is 0.30000000000000004), so the two loops check it with the
very same sum `Effort` uses when the work is done. So the work ends on the tick
the deadline says: not a tick sooner, nor later.

A pace can change during a step: an illness shows or ends. Then `Sicken` wakes
the colonist on that tick, and the ticks before it are worked at the pace they
were worked at:

```csharp
internal int Effort(Worker w, Job job, int seconds) => Effort(w, Pace(w, job, colony.Now.Tick - seconds), seconds);
```

`colony.Now.Tick - seconds` is the tick the time began. Nothing done is ever
lost: the work left stays on the job.

## Arriving, and loading

When `Stride` brings a colonist to where they were going, `Arrive` wakes them
the next tick:

```csharp
long next = colony.Now.Tick + 1;
if (worker.ActionDue is not long early || early > next)
    worker.ActionDue = next;
```

`is not long early` is true when there is no deadline; otherwise it also names
the deadline `early`. An earlier wake-up is never pushed later.

And loading now carries straight on, in the same tick:

```csharp
job.Step = job.Kind == JobKind.Boil ? Step.Fetching : Step.Carrying;
Act(worker, job, 0);                      // and they set off with it at once
```

Each step moves on in one place, inside `switch (job.Step)`, so it can only
happen once: the second time round the step is already the next one.

## Things that wake a colonist sooner

- An order (as before): they think on the next tick.
- A need turning critical (`Grow`): they think on the next tick.
- A job cancelled: they think on the next tick, and its deadline is dropped.
- An illness showing or ending: they are woken (not a think) on that tick.

## Something the longer thinks showed

A test checks that grinding goes on only in Work hours (FP-01). It passed
until now by luck: a colonist used to think every few seconds, and some small
need usually came up at the hour and took them off. With thinks 48 to 168 ticks
apart the timing changed, and the test found that nothing actually put work
down when the Work hours ended. Now `Live` does, at the first think after the
hour, keeping the work done; a delivery in hand is still finished first.

## Saves

Version 17 keeps the action deadline as the fourth number of `"think"`:
`[every, next think, last processed, action deadline or null]`. A save
between two ticks goes on as if it was never made (`ClockTests`).

## The tests keep the old pace

The tests were written for walking 70 m a game minute and deciding every 2 to
7 ticks. They test how things work, not how long a day of play feels, so
`TestContent.Files()` puts those values back (in real seconds: walking 28 m a
real second is 70 m a game minute). The content as shipped is
`TestContent.RealFiles()`, used by `SpeedTests` and the conversion tests. In
24b the work is rebalanced for the 60-minute day, and the tests of whole
colonies move to the real content.

## Exercise

1. In `content/colony.json`, set `"pickup_real_seconds": 0.02`. Work out by
   hand how many ticks that is (careful with the rounding rule), then check it
   with `dotnet test tests --filter Real_seconds_become_ticks` after adding an
   `[InlineData(0.02, ...)]` line. Put the content back.
2. In `Work.Act`, under `case Step.Loading:`, put `//` in front of the line
   `Act(worker, job, 0);  // and they set off with it at once`. Run `dotnet test tests --filter ClockTests`. Which test
   fails, and what does its message say happened? Take the `//` out again.
3. In `Work.Process`, swap the two parts (decide first, then the time that has
   passed: move the `if (decides && Live(worker) ...` lines above the others).
   Which test catches it? Why does the order matter for a colonist who drops
   their work to eat?
