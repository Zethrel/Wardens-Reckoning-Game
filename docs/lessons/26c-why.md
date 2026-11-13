# Lesson 26c: why

Your UI plan's phase C (UI-03) asks the game to explain itself: why is Cael
pottering about in work hours? The card now answers under **Doing**:

> ⓘ In work hours · the work they may do is taken by others; pottering about,
> and from 09:34 free time near their work.

Hover over it (or press More) for **what would help**, if anything would;
F3 adds the developer's trace with the numbers behind it.

## Three ways, as before

Work that waits already explained itself since step 14b, with a record of
three strings:

```csharp
public sealed record Explanation(string Player, string NextStep, string Trace);
```

The player's words, a next step, and a trace for the developer (FP-06). This
step reuses it for colonists: `Work.WhyDoing(id)` returns an `Explanation` too.
One shape for every "why" in the game.

## Only what the colony knows

The plan says: report the actual cause, never guess. So `WhyDoing` reads only
what the colony itself keeps, in the order that matters most:

```csharp
if (w.Collapsed is string felled) ...
if (w.Asleep) ...
if (w.Personal is Personal personal) ...
if (w.Stopped) ...
if (w.Job is TaskId t && jobs.TryGetValue(t.Value, out var held)) ...
if (w.BetweenSince is long since && block == Block.Work) ...
```

`w.Collapsed is string felled` is a pattern again: if `Collapsed` is a string
(not null), it is also put in a new variable `felled`, ready to use. The same
trick unpacks `Personal`, the job's id, and the tick they became between jobs.

## Telling the cases apart

Between jobs, the plan asks for the difference between *no work*, *work they
may not do*, *work they cannot reach* and *work someone else took*. `NoWork`
narrows down step by step, each list a filter of the one before:

```csharp
var open = jobs.Values.Where(j => j.Step == Step.Open).ToList();
var going = open.Where(j => !Paused(j)).ToList();
var allowed = going.Where(Allowed).ToList();
var reachable = allowed.Where(j => ... !double.IsPositiveInfinity(Far(...))).ToList();
```

Where a list becomes empty, that is the reason. `Allowed` and `Far` are the
very checks `Assign` uses to hand out jobs, so the explanation and the
decision cannot drift apart.

One case surprised me while testing: a road planned across the stream gave
"no work to do". The test was right to fail. A building nobody can reach makes
**no job at all**: it waits as a project, with its own reason ("no colonist can
reach it", lesson 15b). So `NoWork` now asks the waiting projects too, and
says "the building planned waits: no colonist can reach it", with that
project's own next step. Writing the test first found a case the plan's four
didn't name.

## Nothing is kept

`WhyDoing` is worked out when the inspector asks (four times a second, for the
one colonist picked), from the state as it is. Nothing new is saved, and the
explanation can never be out of date: if a job appears, the next refresh says
so.

## Exercise

1. Plan a road, then forbid everyone "build" in their work settings. What does
   the card say? Allow one of them again: what changes, and for whom?
2. In `NoWork`, swap the `allowed` and `reachable` filters (and the two `if`s
   that use them). Does any test fail? Then think of work that is both
   forbidden to a colonist and out of their reach: which reason should the
   player read first, and why?
3. Add a case: a colonist whose only allowed work is a haul whose goods are
   all claimed. Is that already covered? Read `Why(TaskId)` and decide.
