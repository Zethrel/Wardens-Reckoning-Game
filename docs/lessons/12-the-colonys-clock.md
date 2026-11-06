# Lesson 12: the colony's clock, names and memory

The colony is the Wardens' settlement played up close, as your design library
describes it. Before any colonist can eat or haul, it needs four things every
later system leans on. They are in a new project, `src/Colony`, with no Godot
in it, like `src/Sim`:

1. **A clock.** One tick is one in-game minute (library Review 29). Time is a
   single whole number, the tick, counted from midnight on the first day of
   Year 1. Everything else (year, month, day, weekday, season, hour) is worked
   out from it in one place, `Calendar.cs`, as the library's Calendar asks:
   12 months of 28 days, 336 days a year, every month starting on a Monday,
   spring in months 1 to 3. The month names (Aven to Hearthren) are only for
   showing; a save keeps numbers.
2. **Speed.** At normal speed a day lasts 12 real minutes, 2 ticks a second;
   double is 4, triple 6, and pause stops the clock (Review 30). `Pacer` turns
   real seconds into ticks and is the only place real time touches the colony.
   The colony itself only ever hears "run 3 ticks", so the same ticks always
   give the same colony, however fast the screen draws.
3. **Names that last.** Every colonist, task, item and so on gets an
   identifier: a number in a type of its own (`ColonistId`, `TaskId`...), so a
   colonist's number can never be passed where a task's is wanted, even if
   both are 17. Each family counts up from 1, and the counters are saved, so
   no number is ever given twice.
4. **A memory.** A colony save is plain JSON with a format name and a version.
   It is checked before loading: anything wrong, missing, older or newer is
   refused with a reason, and the file is left alone. A save is written beside
   the old one and only then moved into place, so a failed save never destroys
   the last good one. Every morning at 06:00 the game will autosave into one of
   three slots in turn.

The dice are the history's own (`Dice` from `src/Sim`), one set per system,
made from the colony's seed and the system's name (with "colony " in front,
so the colony's "weather" never rolls the same as the history's).

Nothing on screen changes yet: the colony gets its screen once there is
something to see (steps 13 and 14).

## 1. Run it

There is nothing to click yet. Run the tests in the game's folder
(PowerShell): `dotnet test tests`. The colony's are in
`tests/ColonyTests.cs`; to run only them:
`dotnet test tests --filter "FullyQualifiedName~ColonyTests"`.

## 2. Where the code is

- **`src/Colony/Calendar.cs`**: the calendar's numbers, `Calendar.TickOf`
  (a date to its tick, refusing impossible dates) and `Moment` (a tick and
  everything about it, written "14th of Orven, Year 12, 06:30").
- **`src/Colony/Speed.cs`**: `Speed` (Paused, Normal, Double, Triple) and
  `Pacer`.
- **`src/Colony/Ids.cs`**: the identifier types and `IdCounters`.
- **`src/Colony/Colony.cs`**: the colony's state: seed, clock, counters and
  dice; `Run(ticks)` moves the clock.
- **`src/Colony/ColonySave.cs`**: `Save`, `Load`, `Read`, and `Fingerprint`
  (a SHA-256 of the save text: two colonies with the same fingerprint are the
  same).
- **`src/Colony/Autosave.cs`**: when the morning comes, and which slot.

How the tests prove it (`tests/ColonyTests.cs`), with the library's
first-playable scenarios:
- **FP-01** (time and identity): the same commands (run 1 minute, make 5
  colonists, roll the weather three times, run 59 minutes...) give the same
  clock and identifiers twice over; identifiers stay unique and carry on after
  a save. FP-01's third part, work only in its schedule's hours, comes with
  schedules in step 14.
- **FP-07** (determinism): the same seed and commands give the same
  fingerprint at every checkpoint; saving and loading halfway ends exactly
  where an unbroken run ends; another seed rolls otherwise.
- **Each rule on its own**: every month starts on Monday and its 28th is a
  Sunday; dates turn into ticks and back; the last minute of a year runs into
  the first of the next; impossible dates are refused; 720 real seconds at
  normal speed are exactly one day; each speed's ticks a second; 60, 144 or 7
  frames a second run the same ticks; a stalled frame runs at most 60; sixteen
  broken saves, each refused with its reason; a refused save is left as it
  was; one morning each day, even across a long jump; the three slots take
  turns.
- Mutation check: 22 deliberate mistakes in the new code, 21 caught; the 22nd
  was a check that changed nothing, now removed.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `public readonly record struct Moment(long Tick)` | `@dataclass(frozen=True) class Moment: tick: int` | `Calendar.cs` |
| `public int Year => ...;` | `@property def year(self): ...` | `Moment` |
| `interface IStableId<TSelf>` with `static abstract` members | a protocol whose class methods every class must have | `Ids.cs` |
| `public T Next<T>() where T : IStableId<T>` | a function taking the class to make: `next(ColonistId)` | `IdCounters` |
| `enum Speed { Paused = 0, Normal = 1 }` | `class Speed(IntEnum)` | `Speed.cs` |
| `Action<Colony>? eachTick = null` | `each_tick: Callable[[Colony], None] \| None = None` | `Colony.Run` |
| `catch (JsonException e)` | `except json.JSONDecodeError as e:` | `ColonySave.Read` |

## 3. Exercise

1. In `Speed.cs`, a day at normal speed is `12.0 * 60.0` seconds. Make it 6
   minutes. Run the tests: which one fails, and what does it tell you?
   (Change it back after: 12 minutes is the library's decision, Review 30.)
2. In `Autosave.cs`, move the morning to 07:30 (`7 * 60 + 30`). Which test
   fails, and why?
3. Add a family of identifiers, say `HerdId` ("herd"), by copying one line in
   `Ids.cs`. Make a few in a test with `colony.Ids.Next<HerdId>()`: do they
   start at 1 even when colonists have been made?
