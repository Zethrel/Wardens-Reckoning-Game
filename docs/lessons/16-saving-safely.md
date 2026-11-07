# Lesson 16: saving safely

The colony could already be saved and loaded. This step proves it for the whole
loop, and makes sure a save that goes wrong never costs the player a good one.
These are the design library's FP-08 (save and reload) and FP-09 (save failure
handling). Your calls are written into the library as Review 35.

## A bug the new checks found

Since lesson 15a, a finished or cancelled building site's pile of materials is
closed (`Stocks.Close`). The ledger's past lines still name it, though, so a
save made after any piece was finished could not be loaded ("ledger[7] names
inventory 6, which does not exist").

The tests of 15a to 15c only saved before a piece was done, so none saw it.
The new read-back (below) refused the very first such save. Saves now keep the
numbers of closed inventories (`closed_inventories`), and a test pins it.

## The checksum and the content fingerprint (`src/Colony/ColonySave.cs`)

Every save now ends with two more facts:

- **`checksum`:** the SHA-256 of the save's own text without it. Loading works
  it out again. If it differs, the file was changed after the game wrote it:
  damaged, or edited by hand.
  - Loading such a save throws `ColonySaveChangedException` ("changed outside
    the game, or is damaged... Load it anyway only if you changed it
    yourself").
  - If the player says yes (`acceptChanged: true`), it loads with a warning.
  - The sum is worked out on the save as the game writes it, so changing only
    the spacing does not count; changing any value does. Saves stay
    hand-editable.
- **`content`:** the content's fingerprint (`Content.Fingerprint`, the SHA-256
  of every content file's JSON). If you tweak a value after saving, loading
  says so ("the colony goes on with the current values, so it may play out
  differently") and loads with the new values. Line endings and spacing in the
  content files do not count, so a Windows checkout gives the same
  fingerprint.

A damaged save is still refused for its own reason first (broken JSON, a part
missing, the goods not adding up). The checksum is only looked at once
everything else is fine.

`ColonySave.Open(path)` and `ColonySave.Opening(text)` return an `Opened`: the
colony and its warnings. `Load` and `Read` give just the colony.

## Writing never puts a bad save in place of a good one

`ColonySave.Save(path, colony)` now goes:

1. written beside it (`path.writing`);
2. **read back and checked**, exactly as loading would;
3. moved into place, and the save it replaces is kept as `path.bak` (one
   level back).

If any step fails, it throws `ColonySaveException`, removes the half-written
file, and says whether the old save is as it was. A step can fail because the
disk is full, the folder is not there, there is no permission, or the read-back
refuses it. A `.writing` left by a crash is simply written over next time.

## The session (`src/Colony/Session.cs`)

`Session` is what the colony screen will hold and call once a frame. Nothing
in it touches Godot.

- `Frame(seconds)` asks the `Pacer` how many ticks are due at the chosen
  `Speed`, runs them one at a time, and makes the autosave as soon as 06:00 is
  passed.
- `AutosaveNow()` writes today's autosave slot (no `.bak`: the three slots are
  the history). If it fails, the reason goes into `Reports` and play goes on.
- `SaveAs(path)` and `LoadFrom(path, acceptChanged)` do the same for the
  player's own saves. A save that cannot be loaded leaves the colony being
  played as it was.
- `Session.Newest(folder, content, skipped)` finds the newest autosave that
  loads, skipping (and naming) a damaged slot.

## 1. Run it

Nothing on screen yet. In the game's folder (PowerShell): `dotnet test tests`.
The new tests are in `tests/ColonySaveTests.cs`.

What they prove:

- **FP-08, the whole loop:**
  - The founders' first two days are saved to a file and loaded at every hour,
    and again the first minute each hour that someone is carrying goods.
    Among those saves are goods in someone's arms, claims waiting to be
    carried, a building and a grinding half done, and someone eating,
    drinking or sleeping.
  - Every loaded copy plays on to the end and finishes exactly like the colony
    that was never saved, down to the last ledger line.
  - Identities go on from where they were.
  - A save made after pieces are finished or cancelled loads, and a broken
    record of closed inventories is refused three ways.
- **FP-09:**
  - A save changed by hand is refused until the player says yes, then loads
    with a warning. One without a checksum counts as changed, and spacing
    alone does not.
  - A save made with other content loads with a warning, and line endings
    don't change the fingerprint.
  - A writing that comes out broken, or reads but differs from what the game
    wrote, replaces nothing and leaves no half-written file. So does a folder
    that is not there.
  - The `.bak` is one level back.
  - Six kinds of damaged file are refused with a reason and left exactly as
    they were: empty, half a file, random bytes, a list, another program's
    file, an older version.
- **The session:**
  - ticks at ×1, ×3 and paused;
  - autosaves at 06:00 into three slots in turn over four mornings;
  - a failed autosave is reported while play goes on;
  - failed saves and loads are reported and keep the colony;
  - a damaged autosave slot is skipped for the newest good one.
- **Mutation check:** 17 deliberate mistakes in the new code; 16 caught at
  first, all 17 once the autosave test also checked that it keeps no `.bak`.

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `class ColonySaveChangedException(string message) : ColonySaveException(message)` | `class SaveChanged(SaveError): ...` (a kind of the other exception) | `ColonySave.cs` |
| `catch (ColonySaveChangedException e) { ... } catch (ColonySaveException e) { ... }` | `except SaveChanged: ...` before `except SaveError: ...` (the narrower one first) | `ColonySave.Open` |
| `File.Replace(fresh, path, path + ".bak")` | `os.replace`, after copying the old file to `.bak` | `ColonySave.Save` |
| `internal static void Save(..., Func<string, string> tamper)` | a function passed in only by the tests, to break a writing on purpose | `ColonySave.Save` |

## 2. Exercise

1. Save a colony with `ColonySave.Save`, open the file in a text editor, and
   change a colonist's name. What does `ColonySave.Load` say? And with
   `acceptChanged: true`?
2. Change only the indentation of that file instead. Why does it still load
   without a warning?
3. In the session test, make the autosave folder a file (as one test does) and
   run three days. How many reports are there, and why does the colony still
   reach the third day?
