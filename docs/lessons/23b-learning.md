# Lesson 23b: learning by practice, and forgetting

Step 23a gave colonists skills that never changed. Now they change: work
teaches the skill it uses, and a skill left alone fades. The design library
(Skills, Traits and Knowledge Progression §5.3, §9, §10) asks for:

- **Learning from meaningful work**, not from time passing.
- **Diminishing returns:** easy, familiar work teaches less and less, and can
  never make a master.
- **Aptitude:** some learn a skill faster than others; it is not the same as
  being good at it.
- **Decay:** a skill not practised fades, after a while and slowly, and never
  wipes out what someone once knew.
- **Faster recovery** for someone who was once better.

## A state per skill

A skill is no longer one number but a small object, `SkillState`:

```csharp
public sealed class SkillState(double proficiency, double aptitude, long lastPractised)
{
    public double Proficiency { get; set; }   // how good now (0 to 100)
    public double Peak { get; }               // the best ever reached
    public double Aptitude { get; }           // how fast they learn (1 the usual)
    public long Practised { get; }            // seconds practised in all
    public long LastPractised { get; }        // when, last
}
```

`Proficiency` has a **property with a body**: setting it also raises the best
ever reached:

```csharp
set
{
    proficiency = value;
    Peak = Math.Max(Peak, value);
}
```

`proficiency` (small p) is the **backing field** that holds the value; the
property `Proficiency` (big P) decides what happens when it is read or set.
Losing proficiency goes through `Lose`, which leaves `Peak` alone.

## How much work teaches

Each piece, recipe and boiling has a **challenge** in `skills.json` (a road 5,
a wall 25, the well 55; grinding 20, boiling 15). How much an hour teaches,
as a share of `points_per_hour`:

```csharp
return Math.Clamp((challenge + rules.Reach - proficiency) / rules.Reach, 0, rules.Most);
```

With `reach` 20, for a wall (25):

| Proficiency | Share |
|---|---|
| 5 | 2 (at most `most`) |
| 15 | 1.5 |
| 25 | 1 |
| 35 | 0.5 |
| 45 or more | 0 |

`Math.Clamp(x, low, high)` keeps `x` between `low` and `high`. So building
walls alone stops teaching at 45: a Journeyman, never a Master. To go further,
a colonist needs harder work (the well, at 55, teaches up to 75).

The gain from `seconds` of work is then:

```csharp
double gain = rules.PointsPerHour * seconds / Calendar.TicksPerHour * Teaches(challenge, state.Proficiency) * state.Aptitude;
```

`Practise` is called from the same place that counts the work done
(`Act`, `Step.Working`), with the real seconds spent, whatever the pace.

## Aptitude: hidden until seen

Founders draw an aptitude for each skill between 0.5 and 1.5, on dice of
their own (`DiceFor("aptitude")`), so nothing else the founding draws
changes. The player is not told it (your choice): the inspector says
"learns quickly" or "learns slowly" only after 8 hours of practice, because
by then you would have seen it. The developer's trace (F3) shows the number
all along.

## Forgetting, and coming back

Each morning `Forget` looks at every colonist's skills:

```csharp
if (now - state.LastPractised < rules.DecayAfterDays * Calendar.TicksPerDay)
    continue;
double floor = rules.KeptShare * state.Peak;
if (state.Proficiency > floor)
    state.Lose(Math.Min(rules.DecayPerDay, state.Proficiency - floor));
```

After 14 days without practice a skill loses half a point a day, but never
goes below half the best ever reached. `Math.Min` makes sure the last step
stops exactly at the floor.

While below their best, a colonist learns three times as fast (`recovery`),
but not past their best at that pace:

```csharp
if (state.Proficiency < state.Peak)
    gain = Math.Min(gain * rules.Recovery, Math.Max(gain, state.Peak - state.Proficiency));
```

## Tests and the pace of work

Learning changes a colonist's pace while they work, so a test that times a
wall would be off by a second or two. The tests of one mechanic at a time
(`TestContent.EverySecond`, and the sanitation tests' `Tweaked`) now learn
nothing (`points_per_hour` 0); learning has its own tests, `PracticeTests`,
on the real content.

## Saves

Version 16 keeps each skill as `[proficiency, best, aptitude, seconds
practised, last practised]`, and refuses one where the best is below the
proficiency, the aptitude is 0, or the last practice is in the future.

## A piece left half built

With this step come tests for something you asked: a wall its builder leaves
(stopped, building forbidden, a critical need, a collapse) is finished by
someone else from where it was. The building job keeps its work left
(`job.Left`) when it goes back to be taken, and the next builder does only
that, at their own skill. `InterruptedBuildingTests` checks all four, and
that a master finishes the rest half again as fast.

## Exercise

1. In `skills.json`, set the wall's challenge to 60. Run
   `dotnet test tests --filter PracticeTests`. Which test fails, and why?
   Put it back.
2. Set `decay_after_days` to 1 and found a colony with nothing to do. Look at a
   founder's skills in the inspector (with F3) on the first few mornings. What
   happens, and where does it stop?
3. Why is `Peak` raised in the `Proficiency` setter instead of in `Practise`?
   (Hint: what else sets a proficiency? Look at `Founding` and the save.)
