# Lesson 7: learning and capitals

Two of the generator's systems now play on in the game's years:

- **Learning** (`learning_settings.py`). Every faction has a main path, and
  may hold learned paths besides. At the end of each year a faction may learn
  a path from a faction it is in touch with: by trade (`LEARN_BY_TRADE`), by
  alliance (`LEARN_BY_ALLIANCE`) or across a shared border
  (`LEARN_BY_BORDER`). A conqueror may learn the paths of the faction it took a
  town from (`LEARN_BY_CONQUEST`), and a people may work a path out alone
  (`LEARN_BY_EFFORT`), more readily one that suits them. A learned path that
  no one in touch holds may be forgotten (`FORGET_CHANCE`). A learned path
  lets its towns make that path's goods at `LEARNED_SKILL` of the full rate,
  and a faction holding both halves of a hybrid (`HYBRIDS`: handcraft and
  attunement make runecarving) makes the hybrid's goods too.
- **Capitals** (`split_settings.py`). A faction that loses its capital picks
  another of its towns: with `CAPITAL_CHOICE` "weighted" every town's chance
  is its share of the faction's people, so usually a big town and now and
  then a small one. A faction that breaks away picks its first capital the
  same way, and once in a while a faction moves its capital
  (`CAPITAL_MOVE_CHANCE`).

Until now the game kept the paths each faction held in the export, and a
lost capital became the largest town left.

## 1. Run it

1. In Godot, F5, open your world (the export of lesson 4b is fine: nothing new
   is needed from the generator) and play 100 years.
2. Open the Book and type `learn` in the search box: "the Kilnrow learn
   mechanism by their own effort", "the Nakari learn transmutation from the
   Vernier Guild (trade)". Type `capital`: "the Whisperward make Zethradra
   their capital, Ethdraethora being lost".

Your saves of step 6c still load (they are version 7): what a faction holds
was already saved, and its hybrids are worked out again from it.

In three runs of the seed 42 world, the game's century learned 5 to 9 paths,
forgot 2 to 5 and gave 5 to 9 factions a new capital. The generator's three
centuries learned 3 to 6, forgot 2 to 4 and gave 2 to 7 new capitals.

**Not in it: a path of its own.** In the generator a faction that breaks
away may found a path that branches from its kin's (`OFFSHOOT_CHANCE` in
`split_settings.py`). It is 0 in your settings, so nothing is missing. If you
set it above 0, the game's own breakaways still found none (and the game has
not been tried on a world exported that way).

## 2. Where the code is

- **`src/Sim/Learning.cs`**: the generator's `north/learning.py`. `Remember`
  notes who holds which paths and towns as the year begins; `Year` runs the
  six steps at its end: `Welcome` (a faction that has just risen gets its
  `START_PATHS`), `Conquests`, `Contacts` and `ByContact`, `ByEffort`,
  `Forget`, and `UpdateSkills`. `Settle` works out every faction's skills and
  hybrids when a world is opened or a save loaded.
- **`src/Sim/Capitals.cs`**: the generator's `north/capitals.py`. `Mend`
  runs twice a year: before the splits (which measure how far a town is from
  the capital) and at the end of the year, when a capital may also move.
  `Pick` chooses a capital: the largest town, or one drawn by its people.
- **`src/Sim/YearCore.cs`**: the year's order is now war, rulers, capitals,
  splits, migration, drift, capitals again, learning: the generator's.
- **`src/Sim/GameSettings.cs`**: reads `learning_settings.py`,
  `CAPITAL_CHOICE` and `CAPITAL_MOVE_CHANCE`.

Each has its own dice ("learning", "capitals"), so a change to one changes
nothing else that is drawn at random.

How the tests prove it (`tests/LearningTests.cs`): with every chance to learn
at 1, to work a path out at 0 and to forget at 1, the generator rolls no dice
that matter, so the tests run its own `Learning.year` and `Capitals.mend` on
the export (`tests/fixtures/make_expected.py`, "learning") with a year's
trade, an alliance and a conquest, and compare everything the game does with
it: the 83 events, every faction's paths, skills (in their order) and
hybrids, and every capital. They also check what must follow where dice
decide: a faction that has just risen gets its `START_PATHS` in silence;
effort follows how well a path suits the people; a path is kept while
someone in touch holds it; no one learns from a faction that is no more; a
hybrid is told once, and a breakaway keeps its kin's without being told
again; with `PATHS_AS_SKILLS` off no one learns; a lost capital is replaced
and told; a weighted pick goes by people, and the largest town is the first
of those with the most; a breakaway picks its first capital in silence;
capitals move at the end of a played year; and a game and its save play on
the same.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `sealed record Start(...)` | a small class that only holds values | `Learning.Start` |
| `SortedSet<int>` | `sorted(set)`, kept sorted as it grows | `Learning.Contacts` |
| `void Add(...)` inside a method | a function inside a function | `Learning.Contacts` |
| `ways.TryAdd(way, other)` | `ways.setdefault(way, other)` | `Learning.ByContact` |
| `towns[^1]` | `towns[-1]` | `Capitals.Pick` |
| `bool moving = false` | `moving=False` | `Capitals.Mend` |

## 3. Exercise

1. In `learning_settings.py`, set `LEARN_BY_BORDER` to 0.01 (twenty times
   more). Export, play 100 years, search the book for `(border)`. How many
   more paths spread across borders?
2. In `split_settings.py`, set `CAPITAL_CHOICE` to `"largest"`. Do the
   capitals in the book still land on small towns?
3. Set `CAPITAL_MOVE_CHANCE` to 0.02. Search for `move their capital`.
4. Read `UpdateSkills` in `Learning.cs`: why does a hybrid's goods come at
   1.0, and a learned path's at `LEARNED_SKILL`?
