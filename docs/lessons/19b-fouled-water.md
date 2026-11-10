# Lesson 19b: fouled water

Review 37 made water short through **quality** as well as labour: now and
then a river, lake or brook fouls; the colonists cannot tell; drinking it may
bring an upset stomach; and the player can mark water "boil first" or "do not
drink". The new code is `src/Colony/Sanitation.cs` and `src/Colony/Health.cs`;
the values are in two new files, `content/sanitation.json` and
`content/health.json`.

## The truth is a good of its own

Fouled water is a good, `foul_water`, next to `water` in `goods.json`. That
keeps the truth countable: every fouled litre sits in some inventory, and the
ledger records where it went. The colonists treat the two the same (they
drink, wash with and carry either), and so does the player's view:

```csharp
string shown = !trace && good == rules.Sanitation.FouledGood ? rules.Drink.Good : good;
```

`Inspect.Goods` counts fouled water as "Water" unless `trace` is true (the
developer's view, F3). The `?:` is the **conditional operator**: `a ? b : c`
is `b` when `a` is true, otherwise `c`.

`Content.Drinkable` lists both goods, `[water, foul_water]`. Wherever 19a
asked "is there water?", 19b asks "is there any drinkable good?":

```csharp
int FreeWater(InventoryId id) => content.Drinkable.Sum(g => Stocks.Free(id, g));
```

## Fouling, on its own dice

Each morning, before the sources fill, `Foul` goes through the sources:

```csharp
if (!source.Natural || !dice.Chance(1 / rules.DaysBetween))
    continue;
int days = dice.Int(rules.LastsFrom, rules.LastsTo);
source.FouledUntil = colony.Now.Tick + (long)days * Calendar.TicksPerDay;
source.Sign = dice.Chance(rules.SignShare) ? dice.Choice(rules.Signs) : null;
```

- `Natural` is true for a river, lake or brook: wells are groundwater and
  never foul.
- `1 / rules.DaysBetween` is 1/60: on average once in 60 days. `DaysBetween`
  is a `double`, so this is 0.0166…; if it were an `int`, `1 / 60` would be
  **0** in C# (whole-number division drops the remainder), and nothing would
  ever foul.
- `dice` is `colony.DiceFor("sanitation")`: Sanitation's own dice. Illness
  rolls on `"health"`. Because no two systems share dice, someone falling ill
  never changes which day the brook fouls (a test checks exactly that).

Then `Turn` changes all the free water in the source to fouled water, as one
operation in the ledger. When `FouledUntil` has passed, the same `Turn` goes
the other way.

## Carrying and mixing

Water keeps what it was when drawn: water fetched to a barrel before the
fouling stays clean. But fouled water poured into clean water fouls all of it:
`Mix` runs every minute and turns the clean water of any inventory that also
holds fouled water.

## Knowing

In half of the foulings there is a sign (dead fish, a smell, scum). `Notice`
looks every minute for a colonist within 1.5 m of a source whose sign nobody
has seen; the first one there sees it, and the player is told:

```csharp
Tell(seer.Name + " saw " + sign + " at " + place.Name.ToLowerInvariant() + ".");
```

`Tell` adds a `News` (the tick and the text) to the colony's news, which is
saved, and which the colony screen shows on its bar (the last ten in the
tooltip).

Without a sign, nobody knows until someone falls ill, and even then the game
does not say why: that is for the player to work out.

## Marks

`Inventory.Mark` is an enum: `None`, `BoilFirst`, `DoNotDrink`. The commands
on any store water may be in (a source, a barrel, the carts) are "Boil first"
(K), "Do not drink" (N) and "Drink freely" (U). Both marks mean nobody drinks
it as it is:

```csharp
bool DrinkFrom(Inventory inventory) => inventory.Mark == WaterMark.None && Drawable(inventory);
```

Barrels are not filled from marked water either. Washing still may use it
(Review 37: only drinking is an exposure).

## Boiling: a job with three steps

For a store marked "boil first" with water, `BoilJobs` makes a job of the new
kind `JobKind.Boil`, when there is fuel (a timber) in a store and an unmarked
store with room for the boiled water. Otherwise it says why, for the
inspector's "Boiling" line.

The job goes through its steps in `Act`:

1. **Going** to the fuel, **Loading** it into the arms (as a haul does);
2. **Fetching**: carrying the fuel to the water (the new step);
3. **Working**: 30 minutes at the water; then `Boiled` uses up the timber and
   the claimed 10 litres (clean or fouled) and makes 10 litres of clean water
   in the arms;
4. **Carrying** and **Unloading** to the store, as a haul.

Three ledger operations (`First`, `Second`, `Third`) record the pick-up, the
delivery and the boiling. `InHand(job)` says whether the job has goods in the
worker's arms, so stopping them, or a need, never leaves fuel or boiled water
lost in mid-air.

## The upset stomach

`Expose` is called whenever a colonist drinks fouled water:

```csharp
if (w.Illness != null || !dice.Chance(rule.Chance))
    return;
```

`||` is "or", and C# stops at the first true part: a colonist already ill
(or about to be) does not roll at all. Otherwise a one-in-three chance; the
illness shows 6 to 12 hours later and lasts 24 to 48 hours.

While ill, a colonist works at 70%. Rather than dice, `Slowed` keeps a
running count:

```csharp
w.Slow += 1 - content.UpsetStomach.Work;     // 0.3 a minute
if (w.Slow < 1) return false;
w.Slow -= 1;
return true;                                  // this minute of work is lost
```

Three minutes in ten are lost, always the same ones: the same save plays out
the same. Thirst and fatigue grow half again as fast (`Faster`).

## Saves: version 12

New in a save: each inventory's `mark`; each worker's `illness` and `slow`;
each source's fouling (`fouled_until`, `sign`, `sign_seen`, so a spring is now
7 entries); the news; a job's `third` operation and the water it is
`boiling`.

## Not yet

- **Other fuel:** only timber boils water until there is firewood.
- **Wells fouling:** not by the dice; later, perhaps from a midden too close.
- **Treatment:** the upset stomach passes by itself; healers come later.

## Exercise

1. In `content/sanitation.json`, set `"days_between": 10`. Play a colony on a
   river for a month (×3 speed): how often do you see the sign news? Then set
   `"sign_share": 0` and play again. What tells you now that the river is
   fouled?
2. In `content/health.json`, change `"work": 0.7` to `0.5`. Run
   `dotnet test tests --filter SanitationTests`. Which test fails, and why
   does it name the numbers it does?
3. Change `"litres": 10` under `boil` to `20`. Which tests fail? Is a load of
   20 litres of boiled water too heavy to carry? (Look at `carry_kg` in
   `colony.json` and `mass_kg` of water in `goods.json`.)
