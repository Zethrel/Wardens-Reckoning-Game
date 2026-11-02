# Lesson 10: the chronicle as it happens

The history's years have always been told in your words: the generator's
chronicle (`chronicle_settings.py`) picks each era's most notable events and
writes your sentences for them, and the Book shows those sentences. Now the
game's own years are told the same way.

**When.** The years are cut into eras of `ERA_YEARS` (50), on the history's
grid: if the history ended at 300, the game's first era is 300 to 349 (its
events start in 301), then 350 to 399, and so on, as if the history had run
on. When an era ends, its `MOST_PER_ERA` (12) most notable events are told;
until then the Book shows their plain text. That is the generator's rule:
which events are the most notable is known only when the era is over.

**Which.** As in the generator (`chronicle.told`):

1. Every kind of event has a weight in `NOTABLE`; 0 or none, and it is never
   told. An event scores its weight, `DEAD_BONUS` more for every ten times as
   many people it touched (its dead, or the people who moved).
2. The highest score is chosen first, one at a time; each time a kind is
   chosen, the rest of that kind score `REPEAT_FADE` times as much (a half),
   so one kind cannot fill a chapter. Only kinds you wrote a sentence for
   (`TEMPLATES`) are told.
3. One of the kind's sentences is chosen by the chronicle's dice, filled
   with `{year}` and the event's facts, and every sentence starts with a
   capital.

The Book shows a told event with your sentence (without the year it opens
with, as the Book shows the year in front); **Only the chronicle** shows only
the told ones. A save keeps every sentence.

The export now carries your chronicle settings (`"continue"."settings".
"chronicle"`), so the game tells its years in the words the history was told
in. Change a sentence in `chronicle_settings.py` and export again to hear it.

## 1. Run it

1. Pull the generator and export a world again:
   `python worldgen.py --seed 42 --history 42 --size 512 --export`
2. In Godot, F5, open it, and play 50 years (to 350). Tick **Only the
   chronicle** in the Book: the game's own years 301 to 349 are told among
   the history's.

## 2. Where the code is

- **`src/Sim/Chronicle.cs`**: `Score`, `Notable`, `Fill` (as Python's
  `str.format` fills `{slot}`, with `{{` for a brace), `Capitals`, `Tell` (one
  era), `Eras` (the generator's eras of a history), and `YearEnds`, which
  tells an era when its last year ends.
- **`src/Sim/YearCore.cs`**: `Chronicle.YearEnds` at the end of every year.
- **`src/Sim/Game.cs`**: `GameEvent.Told`, and `Game.Tell`, which writes a
  sentence into the event and the Book.

How the tests prove it (`tests/ChronicleTests.cs`):
- **Against the generator.** `make_expected.py` runs the generator's own
  `chronicle.told` on the test world's 100 years of events, with your
  settings and a fixed list of numbers for its dice. The game tells the same
  eras from the same events, and every chapter, event and sentence must
  match.
- **Every fact is there.** Three games of 150 years each, and every game
  event of every kind in the generator's `chronicle.SLOTS` must carry every
  fact the generator's events of that kind carry, so your sentences fill
  whatever you write.
- **Each rule on its own.** Other tests check that an era is told when it
  ends and not before, and only its own years; that a save keeps the
  sentences; how the dead raise a score and repeats fade; that a notable
  kind with no sentence is left out; and Python's braces and capitals.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `Events[index] with { Told = sentence }` | `dataclasses.replace(event, told=sentence)` | `Game.Tell` |
| `new StringBuilder()` and `text.Append(...)` | building a string piece by piece | `Chronicle.Fill` |
| `Regex.Replace(text, m => ...)` | `re.sub(pattern, lambda m: ..., text)` | `Chronicle.Capitals` |
| `public sealed class ChronicleException(string message) : Exception(message);` | `class ChronicleError(Exception)` | `Chronicle.cs` |

## 3. Exercise

1. In `chronicle_settings.py`, set `ERA_YEARS` to 10 and export again. Play
   20 years: the Book tells the game's years twice, at 309 and 319.
2. Write a second sentence for `"war_declared"` (make it a list of two).
   Play 50 years: do both appear?
3. Set `MOST_PER_ERA` to 3. Which events still make it into the chronicle?
