# Lesson 20a: the first playable's evidence

The design library lists twelve first-playable scenarios, FP-01 to FP-12, and
accepts the first playable only when each has actually been run and passed
(FP-12, performance, is measured instead). Most of the tests were written
along the way, in steps 12 to 16; this step gathers them into a record,
`docs/acceptance.md`, and adds two scenarios for the colony as it is now.
There is no new game code.

## Finding tests by their names

A test is a method marked `[Fact]` (one case) or `[Theory]` (one case per
input). Every scenario test's name begins with its scenario:

```csharp
[Fact]
public void FP04_a_wall_is_planned_supplied_built_and_stands()
```

`dotnet test` can run only some tests, chosen by a **filter**:

```powershell
dotnet test tests --filter "FullyQualifiedName~.FP04_"
```

A test's *fully qualified name* is its namespace, class and method:
`WardensReckoning.Tests.BuildTests.FP04_a_wall_is_planned_supplied_built_and_stands`.
`~` means "contains", so `.FP04_` picks every test whose method name starts
with `FP04_`, and `.FP` picks all the scenarios. (`=` would mean "is exactly",
and `!~` "does not contain".)

`--logger "console;verbosity=normal"` lists every test with its result, not
just the totals. That list, with the date and the commit, is the evidence.

## Two scenarios on a local map

The determinism (FP-07) and save (FP-08) scenarios of step 16 run on the flat
test ground. Since then the colony has moved onto local maps and gained water,
washing, fouling, marks, boiling and illness. `tests/AcceptanceTests.cs` runs
both scenarios again on such a colony.

The trick for the save scenario is the `eachTick` callback of `Colony.Run`:

```csharp
colony.Run(end - Start.Tick, c =>
{
    Command(c);
    ...
    if (c.Now.MinuteOfDay % 120 != 90)
        return;
    ColonySave.Save(path, c);
    var copy = ColonySave.Load(path, c.Content!);
    copies.Add(copy);
});
```

The lambda `c => { ... }` runs after every tick. Every two hours, on the half
hour, the colony is saved and loaded into a copy. At the end, each copy plays
on to the same moment, giving the same command at the same minute (`Command`),
and must end with exactly the same fingerprint as the colony that was never
saved. `%` is the remainder: `MinuteOfDay % 120 == 90` is true at 01:30,
03:30, 05:30 and so on.

A scenario only proves something if what it is about happens in it. So the
test records whether, along the way, water was in someone's arms, the brook
was fouled, a store was marked, a boiling was under way and someone was ill,
and fails if any of them never happened. The first version of this test did
fail that way: nobody fell ill, because the colonists drank the carts' clean
water for weeks before they ever needed the brook. The scenario now starts
with the carts' water spilt.

## Exercise

1. Run all the scenarios with each test listed:
   `dotnet test tests --filter "FullyQualifiedName~.FP" --logger "console;verbosity=normal"`.
   How many cases does FP-10 have, and why so many more than its three tests?
2. In `AcceptanceTests.cs`, change `illness: 1` to `illness: 0` and run
   `dotnet test tests --filter "FullyQualifiedName~AcceptanceTests"`. Which
   test fails, and what does its message say? Put it back.
3. Change `% 120 != 90` to `% 360 != 90`. How many saves are there now, and
   which assertion notices? Put it back.
