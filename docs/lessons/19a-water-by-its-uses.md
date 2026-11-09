# Lesson 19a: water by its uses

You decided in Review 37 that water should be short through **labour and
quality**, not by rivers running dry: about 15 litres a colonist a day,
drawn at a source and carried. This step builds the labour half (fouled water
and boiling are 19b). The new code is `src/Colony/Water.cs`; the values are in
`content/needs.json` ("water", "wash", the "hygiene" need) and three new
pieces in `construction.json`.

## Sources by their kind

```csharp
public enum WaterKind { Spring, Well, River, Lake, Brook }
```

An **enum** is a type with a fixed list of names. A water source remembers its
kind, and `LitresOf` gives its litres for today with a `switch` on it:

```csharp
WaterKind.River or WaterKind.Lake => content.Water.PlentyLitres,      // 1000, topped up every hour
WaterKind.Brook => source.Frozen ? 0 : content.Water.BrookByMonth[colony.Now.Month - 1],
_ => source.Litres,                                                   // a spring's or a well's own
```

`BrookByMonth[colony.Now.Month - 1]`: arrays count from 0, months from 1, so
January is `[0]`.

Sources only give: `Inventory.SourceOnly` makes `Takes` say no, so nothing is
ever carried *to* the brook. (Before this step the founders' carts could have
been emptied into the stream, since it was the nearest store that took water.)

## Frost

A colony founded in a cold biome (`cold_biomes`: taiga, tundra...) has
`Work.Cold` set. Each morning `Freeze` asks whether it is a freezing month:

- a brook freezes and gives nothing until the thaw;
- a river or a lake freezes over, and the colony plans an **ice hole** on its
  bank: a piece with `"opens_ice": true`, no materials, 30 minutes of work.
  It reuses everything a building has (a builder takes it, it can wait and
  say why, it can be paused), but when it is finished it leaves nothing
  standing: `Complete` calls `OpenIce` instead of `AddPiece`. The next morning
  the river is frozen again, and another hole is planned.

`Drawable(inventory)` keeps everyone away from frozen water: nobody drinks it,
washes in it or fetches from it.

## Carrying: capacity and room

A barrel holds 100 litres, a basin 20: `Inventory.Capacity`. How much more may
be brought is its **room**:

```csharp
int coming = jobs.Values.Where(j => j.Kind == JobKind.Haul && j.To == inventory.Id).Sum(j => j.Amount);
return Math.Max(0, capacity - inventory.Goods.Values.Sum() - coming);
```

The water already on its way counts, so two loads never overfill it. `Fetch`
sends one colonist with one load (20 litres) whenever a barrel or basin is a
load short; `NearestStore` (used when the carts are emptied) skips anything
without room, and a load is never bigger than the room.

## Hygiene and washing

Hygiene grows 2 points an hour awake and 1 asleep: about 40 a day, so a
colonist *wants* to wash about once a day. `"collapse_hours": null` makes
`CollapseHours` infinity: nobody is ever felled by dirt.

`WashPlace` picks the shorter walk: a basin with 9 litres free, or the bank
of a river, brook or lake not frozen. At a basin those 9 litres are claimed
and used up; on a bank none are.

One trap, found while writing the tests: an *urgent* need stops a colonist
taking new work. On the test ground there is nowhere to wash, so after a day
and a half everyone would have stopped working for good. Now hygiene only
counts towards that when there is somewhere to wash (`Level` in `Life.cs`).

## Not yet

- **Cooking water:** there is no cooking yet. When there is, a cooking recipe
  takes water as an input like any other good.
- **Taking down** a barrel or a basin: not offered yet (its water would have
  to go somewhere).
- **Wellbeing:** low hygiene will lower it, once there is wellbeing.

## Exercise

1. Make a brook richer in summer: `brook_litres_by_month` in `needs.json`.
   Which test in `WaterTests.cs` fails, and what does it tell you?
2. Change `load_litres` to 40. Which test fails? (Look at what a colonist can
   carry: `carry_kg` in `colony.json`.) What would you change so 40 works?
3. Add `"meadow"` to `cold_biomes` and run `dotnet test --filter WaterTests`.
   Which test fails, and which line of it? (The tests found colonies in
   month 10 directly; playing there would take nine in-game months.)
