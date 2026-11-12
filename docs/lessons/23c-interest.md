# Lesson 23c: interest

Skills say how good a colonist is at a kind of work (23a), and aptitude how
fast they learn it (23b). Interest is the third, separate thing: whether they
**like** doing it. The design library (Skills, Traits and Knowledge
Progression §6.2 to §6.3) keeps the three apart: someone can be a master
builder who dislikes building, or an untrained one who loves it.

What you chose (12 November 2026):

- Interest only steers **which job a colonist takes**. It never makes them work
  better or faster, and never makes them refuse work.
- It is **shown openly** in the inspector: how someone feels about their work
  is something they would tell you.

## Three values: an enum

```csharp
public enum Interest
{
    Dislikes = -1,
    None = 0,
    Enjoys = 1,
}
```

An `enum` is a small set of named values. `Interest.Enjoys` reads better than
a magic number, and the compiler stops you from writing `Interest.Loves` by
mistake. Each colonist has a table of them by skill:

```csharp
public SortedDictionary<string, Interest> Interests { get; } = new(StringComparer.Ordinal);
```

A skill not in the table is `None`. `GetValueOrDefault(skill)` gives `None`
for those, because `None` is the enum's 0, its default.

## Founders draw them

`founding.json` gives two chances:

```json
"interests": { "enjoys": 0.3, "dislikes": 0.15 }
```

For each founder and each skill, one roll from 0 to 1 on dice of their own
(`DiceFor("interests")`, so names and skills are drawn exactly as before):

```csharp
double roll = interestDice.Uniform(0, 1);
if (roll < enjoys)
    founder.Interests[skill] = Interest.Enjoys;
else if (roll < enjoys + dislikes)
    founder.Interests[skill] = Interest.Dislikes;
```

Picture the line from 0 to 1 cut into three pieces: 0 to 0.3 enjoys, 0.3 to
0.45 dislikes, the rest neither. That is why the two chances together may be
at most 1: the content check says so if they are not.

## Choosing work in rounds

`Work.Assign` used to give every open job, oldest first, to the nearest free
colonist allowed to do it. Now it does that three times:

```csharp
foreach (var round in new[] { Interest.Enjoys, Interest.None, Interest.Dislikes })
    foreach (var job in jobs.Values.Where(j => j.Step == Step.Open).ToList())
    {
        ...
        var worker = free.Where(w => ... && InterestIn(w, skill) == round) ...
```

First each job may only go to someone who enjoys its skill; then to someone
who feels nothing either way; last to someone who dislikes it. So disliked
work is still done when nobody else is free: never a refusal. Carrying uses no
skill, so it always falls in the middle round. With no interests at all,
everything falls in the middle round, and the result is exactly as before.

`.ToList()` makes a copy of the open jobs before the loop, because the loop
changes jobs from open to taken. Changing a collection while going through it
with `foreach` is an error in C#.

**A trade-off:** a colonist who enjoys building takes a wall even if someone
who doesn't care stands right next to it. Enjoyed work wins over a short walk.
If that looks wrong in play, interest could instead act as a distance bonus,
so a much nearer colonist still wins. That would be a later change, if you
want it.

## Shown and saved

The inspector adds "; enjoys it" or "; dislikes it" to the skill's line:

```
Construction       Adept, works at 127% of the usual pace; enjoys it
```

Saves are version 18: each colonist has `"interests": { "construction":
"enjoys" }`. Anything but `"enjoys"` or `"dislikes"`, or a skill the content
doesn't know, is refused with a reason.

## Exercise

1. Set `"enjoys": 1, "dislikes": 0` in `founding.json` and found a colony.
   What does every founder's inspector say? Then try `"enjoys": 0.8,
   "dislikes": 0.4`: what does the game say, and why? Put it back.
2. In `InterestTests`, `Disliked_work_is_still_done_when_nobody_else_is_free`
   stops the far colonist. Remove that line: who builds the wall now, and why?
3. In `Work.Assign`, swap `Interest.Enjoys` and `Interest.Dislikes` in the
   rounds. Which tests fail? What would the colony be like to watch?
