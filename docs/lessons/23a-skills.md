# Lesson 23a: skills

Until now every colonist worked at the same pace. This step gives them
**skills**: how good each one is at a kind of work, from 0 to 100. The design
library (Skills, Traits and Knowledge Progression) asks for a few things this
step follows:

- A skill is a number underneath, but the player reads a **rank**:
  Untrained, Novice, Apprentice, Journeyman, Adept, Expert, Master.
- Skill changes how well work goes (here: how fast), but it is **never a
  permission**: an untrained colonist can still build, only slowly.
- Only the skills the colony's work uses now (R21-08): **Construction** for all
  building, **Food preparation** for grinding and boiling. Carrying and care
  use none.

Learning by practice comes in 23b, interest in 23c.

## The content

`content/skills.json`:

```json
"skills": [
  { "id": "construction", "name": "Construction", "used_for": [ "build" ] },
  { "id": "food_preparation", "name": "Food preparation", "used_for": [ "quern", "boil" ] }
],
"ranks": [ [ 0, "Untrained" ], [ 10, "Novice" ], ..., [ 90, "Master" ] ],
"speed": [ [ 0, 0.5 ], [ 40, 1.0 ], [ 100, 1.5 ] ],
"standard": 40
```

`speed` is a list of points: at proficiency 0 work goes at half pace, at 40
(a Journeyman) at the usual pace, at 100 half again as fast. Between two
points the pace lies on the straight line joining them:

```csharp
var (p0, s0) = SkillSpeed[i - 1];
var (p1, s1) = SkillSpeed[i];
if (p <= p1)
    return s0 + (s1 - s0) * (p - p0) / (p1 - p0);
```

At 70: between (40, 1.0) and (100, 1.5), so `1.0 + 0.5 × 30 / 60 = 1.25`. This
is **linear interpolation**, the same `from + (to - from) × t` as the drawing
of lesson 21b, with `t = (p - p0) / (p1 - p0)`.

`var (p0, s0) = ...` takes a pair apart into two names at once
(**deconstruction**), like Python's `p0, s0 = pair`.

Founders draw their skills from `founding.json`:

```json
"skills": { "construction": [ 5, 65 ], "food_preparation": [ 5, 65 ] }
```

on dice of their own (`colony.DiceFor("skills")`), so adding skills changed
nothing else the founding draws: the same names, the same places.

## Which skill a job uses

```csharp
internal string? SkillOf(Job job) => content.SkillFor.GetValueOrDefault(
    job.Kind switch
    {
        JobKind.Build => BuildWork,
        JobKind.Boil => BoilWork,
        JobKind.Craft => sites[job.Site!.Value.Value].Workshop,
        _ => "",
    });
```

A `switch` **expression** picks a value by the job's kind (`_` is "anything
else"). `GetValueOrDefault` looks the kind of work up in the table built from
`used_for`; carrying (`""`) is not in it, so it gives `null`: no skill.

## Pace, and the seconds left over

The pace of a job is the skill's speed times the health's (lesson 19b: an ill
colonist works at 70%):

```csharp
internal double Pace(Worker w, Job job) =>
    (SkillOf(job) is string skill ? content.SpeedAt(w.Skills.GetValueOrDefault(skill)) : 1) * (Ill(w) ? content.UpsetStomach.Work : 1);
```

Work is counted in whole seconds, but 0.7 or 1.25 of a second is not whole. So
what is left over is **carried** to the next think:

```csharp
w.Carry += seconds * pace;
int done = (int)Math.Floor(w.Carry + 1e-9);
w.Carry = Math.Max(0, w.Carry - done);
return done;
```

Ten seconds at 1.25 are worth 12.5: 12 are done and 0.5 is carried. The next
ten are worth 0.5 + 12.5 = 13: 13 done, nothing carried. Twenty seconds, 25
done: exactly 1.25 a second. Before this step the same idea
counted only the seconds an ill colonist *lost* (`Slow`); now it counts the
work *done*, which also covers a skilled colonist's extra. The save keeps it
as `"carry"` (saves version 15).

## What the player sees

The colonist's inspector has a line for each skill:

```
Construction       Adept, works at 127% of the usual pace
Food preparation   Untrained, works at 54% of the usual pace
```

The number (72.4) only in the developer's trace (F3): the library wants the
player to manage by ranks, not raw numbers.

## Keeping the tests' timings

Many tests time work: a wall takes 45 minutes, boiling 30. They still do,
because a colonist added by hand (as the tests do) gets the `standard`
proficiency, 40, where the pace is exactly 1. Founded colonies in the tests
that time work use founders pinned at 40 (`Tweaked()` sets the range to
`[ 40, 40 ]`). `SkillTests` then times a wall at proficiency 0, 40 and 100:
90, 45 and 30 minutes.

## Exercise

1. In `content/skills.json`, change the last speed point to `[ 100, 3.0 ]`.
   Start a colony: what does the inspector say for a founder at about 60?
   Work it out by hand first (between which two points does 60 lie?). Put it
   back.
2. Change `founding.json`'s ranges to `[ 90, 100 ]` for construction. Found a
   colony and plan a small house: how long does it take compared to before?
3. Why does `SkillOf` give `null` for carrying instead of a skill at the usual
   proficiency? (Hint: what would a "Carrying" line in the inspector add, and
   what does the library say about hauling, R21-08?)
