# Lesson 13a: goods, claims and the ledger

Step 13 makes the colony's first goods move: wheat from the founders' carts
to a hand quern, ground into flour, and the flour carried to a store
(FP-02). It is built in two parts. This one, 13a, is the bookkeeping every
later part relies on; 13b brings the people who carry and grind.

## Content: what the colony knows

The goods, workshops and recipes are not in the C# code but in JSON files in
`content/` (your decision, 7 November): `goods.json` (wheat and flour, 1 kg
a unit), `recipes.json` (a hand quern grinds 5 wheat into 4 flour in 180
minutes) and `colony.json` (a person carries 25 kg and walks 70 m a minute;
loading and unloading take a minute each). `content/README.md` explains each
key.

When the colony starts, `Content.Load` reads them and checks everything
(FP-10): broken JSON, a missing file, a missing or misspelt key, a number that
is not allowed, two things with the same id, a recipe naming a good or
workshop that does not exist, or a good made from itself (directly, or
through other recipes). Every problem is reported at once, each naming its
file and its place, for example:

```
recipes.json.recipes[0].inputs.barley: no good "barley"
```

## Inventories: every unit has a keeper

An inventory is anywhere goods can be: a store, what a workshop has at hand,
what it has made, what a person carries. Every unit is always in exactly one
inventory, so every unit always has a custodian. For now an inventory sits at
a point in metres (`X`, `Y`); the tile grid comes with construction (step 15).

## Claims: nobody takes what is promised

Goods are taken in two moves. First they are **claimed** (`Stocks.Reserve`):
the goods stay where they are, but nobody else can claim or take them. Then
the claim is **used**: moved somewhere (`Move`) or used up by a recipe
(`Make`). A claim can also be let go (`Release`). A claim that cannot be made
says why, in words:

```
The founders' carts has 50 wheat, 40 already claimed: 10 free, 20 wanted
```

A store can **keep** a protected reserve (`Inventory.Keep("wheat", 20)`):
those units cannot be claimed at all (the library's reserve policy).

## The ledger: every change once

Every change is an **operation** with its own number (`OperationId`), and is
written in the ledger: when, what, how much, from where, to where. Goods come
in only by arriving from outside (`Arrive`, the founders' supplies) or being
made (`Make`), and leave only by being used (`Make`'s inputs). So at any time,
what the colony holds equals what the ledger accounts for (`Total` and
`Accounted`); a save whose goods do not add up is refused.

An operation done once is never done again: the same operation a second time
changes nothing and says "already done". That is FP-05's "exactly once": a
doubled order or a replay after loading cannot copy goods.

A save (version 2 now) keeps the inventories, the claims and the ledger, and
checks them on loading: unknown goods, inventories or claims that do not
exist, claims for more than is there, a ledger line that makes no sense.

## 1. Run it

Nothing to click yet. In the game's folder (PowerShell): `dotnet test tests`.
The new tests are in `tests/ContentTests.cs` and `tests/StockTests.cs`.

Then try a mistake in the content, the way the game will catch it: in
`content/recipes.json`, change `"wheat": 5` to `"wheet": 5` and run the tests
again. `The_real_content_loads` fails, naming the place:
`recipes.json.recipes[0].inputs.wheet: no good "wheet"`. Change it back.

## 2. Where the code is

- **`content/*.json`** and **`content/README.md`**: the content.
- **`src/Colony/Content.cs`**: `Content.Load` and `Content.Read`, the checks
  (the small `Checker` class), and `Loops` (goods made from themselves).
- **`src/Colony/Stock.cs`**: `Inventory`, `Reservation`, `Entry` (a ledger
  line), `Outcome` (done, or why not) and `Stocks` (all of it together).
- **`src/Colony/ColonySave.cs`**: the save's new parts and their checks.

How the tests prove it:
- **FP-10**: 24 broken versions of the real files (duplicates, missing goods
  and workshops, bad numbers, misspelt keys, wrong versions, goods made from
  themselves), each caught with its file and place; broken JSON and a
  missing file; several problems reported together.
- **FP-05** (its ledger half): an operation sent twice is done once; a
  recipe with one lost claim changes nothing at all; and a thousand random
  claims, moves, let-goes, recipes and doubled receipts never lose or make a
  unit (checked after every one). The interrupted haul itself is 13b.
- **The save**: kept exactly, both copies go on the same way, and 15 broken
  saves are refused with their reasons.
- Mutation check: 27 deliberate mistakes in the new code, all caught (three
  needed new tests: nothing arriving, an older content version, and claims
  that each fit but together are too many).

C# you will meet:

| C# | Python | Where |
| --- | --- | --- |
| `public sealed record Good(string Id, string Name, ...)` | `@dataclass(frozen=True) class Good` | `Content.cs` |
| `(Reservation? Reservation, Outcome Outcome)` (a tuple with names) | returning `(reservation, outcome)` | `Stocks.Reserve` |
| `IEnumerable<string> Loops(...)` with `yield return` | a generator function with `yield` | `Content.Loops` |
| `item[key] is JsonValue v && v.TryGetValue(out int n)` | `isinstance(v, int)` | `Checker.Int` |
| `e.Change switch { Change.Used => -e.Amount, _ => 0 }` | `match e.change: case Change.USED: ...` | `Stocks.Accounted` |

## 3. Exercise

1. In `content/recipes.json`, make the quern give 5 flour from 5 wheat. Which
   test fails, and why? (It checks the real file's numbers.) Which number
   would you change to make grinding twice as slow?
2. Add a good `bran` (`mass_kg` 1, `volume_l` 2) and let grinding make
   `"bran": 1` as well as the flour. Do the content checks accept it? Then
   make bran an input of the same recipe: what do the checks say?
3. In `StockTests`, the founders bring 50 wheat. Make a test that keeps 20 as
   a reserve and tries to claim 31. What does the refusal say?
