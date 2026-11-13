# Lesson 25b: talk

Free time (25a) gave colonists things to do alone. Now they talk. The design
library's rules (Review 42, with your R42-C03):

- **Nobody is made to talk.** The one asked must be free for it, and agree.
- **One talk:** one event, one duration, one topic, recorded once.
- **Leaving** leaves the talk to go on with two or more, or end.
- **Topics from what they've lived**, shown as a few words in a bubble, never
  telling what the player couldn't know.

The code is in `src/Colony/Talk.cs`, another file of the `partial class Work`.

## Talking is just another activity

`leisure.json` has a seventh activity:

```json
{ "id": "talk", "name": "Talking", "doing": "talking", "place": "company", ... }
```

Its place, `company`, means "with someone". `Choose` weighs it like the
others; only where to go is different. `TalkSpot` finds a talk nearby to join,
or else the nearest colonist who is free for it:

```csharp
bool FreeToTalk(Worker p) =>
    FreeBlock(p) && p.Job == null && !p.Stopped && !p.Asleep && p.Collapsed == null && p.Personal == null && Level(p) < 3
    && (p.Outing == null || p.Outing.Talk == null && p.Outing.Left != null);
```

A long `&&` chain reads as a list of conditions that must all hold: in their
free time, no job, not stopped, awake, nothing urgent (`Level(p) < 3`), and
either doing nothing or settled at a stop.

## Asking, and being told no

Only once talking is chosen is anyone asked. They answer on dice of their own:

```csharp
double chance = content.Leisure.Talk.Willing + p.FreeNeeds.GetValueOrDefault("social") / 200
                + p.Interests.GetValueOrDefault("company") switch { Interest.Enjoys => 0.25, Interest.Dislikes => -0.25, _ => 0 };
if (colony.DiceFor("talk").Uniform(0, 1) >= chance)
    return false;                              // not now: nobody is made to talk
```

A lonely colonist (Social 80) who enjoys company says yes 0.4 + 0.4 + 0.25:
almost always. One who dislikes company and isn't lonely says yes 0.15 of
the time. Told no, the asker does nothing now and looks again in ten minutes.

## One talk, shared

A `Talk` is a small object of its own, in a table in `Work`. Each colonist's
outing points to it by number (`Outing.Talk`). So there is exactly one topic
and one beginning, however many take part:

```csharp
talk.Began = now;
talk.Topic = TopicFor(talk);
foreach (var id in talk.With)
    workers[id.Value].Outing!.Left = length;   // one end, for everyone
```

The one asked waits where they are; the one who asked walks over; the talk
begins when they arrive. A bug the tests found here: the one who waited had
their *waiting* counted as talking, because their time is measured from when
they were last processed. Now their clock is set to the start:
`p.Thought = now`. One line, but without it one colonist gained 19.02 points
and the other 19.00, and the tests that compare them failed.

## Leaving

`EndOuting`, from any of its usual reasons (the block ends, a need, an
order), now also calls `LeaveTalk`. With two or more left, nothing else
happens. With one left, the talk ends: it is recorded (once, because it is
taken out of the table in the same place), and the one left stops at their
next processing, credited only up to now:

```csharp
po.Left = (int)Math.Max(0, now - p.Thought);   // only the time until now counts
```

## What they talk about

`TopicFor` gathers the possible topics, each with a weight, and draws one,
the same weighted draw as choosing an activity (lesson 25a):

- what happened lately (`Note` records a piece built, a collapse, the
  arrival): weight 3;
- someone **visibly** ill (`Ill` is true only once an illness shows): 3;
- a speaker's wants (hungry, thirsty, tired, needing a wash) and what they
  enjoy: 2;
- the sunset or the stars, by the time of day: 1;
- everyday topics: ½ each.

The words are all in `leisure.json`, with blanks: `"the new {piece}"`,
`"{name}'s {illness}"`. `Replace("{piece}", "wall")` fills them.

## The bubble

The screen asks `Work.Bubble(talk)` whose turn it is:

```csharp
long turn = (colony.Now.Tick - began) / Math.Max(1, content.Leisure.Talk.BubbleTicks);
return (there[(int)(turn % there.Count)], topic);
```

`%` is the remainder: with two speakers, turns 0, 1, 2, 3 give speaker 0, 1, 0,
1. Because it's worked out from the tick, the screen keeps nothing of its own,
and nothing about it needs saving.

## Exercise

1. In `leisure.json`, add a topic to `everyday`, e.g. `"the harvest"`, and
   watch a few evenings. Does it come up? How often, compared with
   "the journey here" on the first day? Why?
2. Set `"willing": 0` and found a colony. Who still talks, and why? (Hint:
   look at what else goes into `chance`.)
3. In `Talk.cs`, find `p.Thought = now;` and put `//` in front of it. Run
   `dotnet test tests --filter TalkTests`. Which tests fail, and what do their
   messages show? Put it back.
