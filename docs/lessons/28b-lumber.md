# Lesson 28b: timber into lumber

Timber is heavy: 10 kg a unit, so a colonist carries 2 by hand. This step
adds lumber, as the library's Review 44 decided:

- **D5 C:** a one-tile sawhorse, where timber is sawn;
- **D6 C:** 1 timber makes 2 lumber of 3 kg and 4 litres each, in 20
  minutes, by the new Carpentry skill;
- **D7 A:** doors, floors, water barrels and wash basins are built of lumber,
  with a quarter less work; walls and the well stay timber, and timber stays
  the fuel for boiling;
- **D8 B:** the founders bring 50 timber, 20 lumber and a sawhorse.

In the game, the sawhorse stands two steps east of the carts. Pick its tile,
press **O** (Order 5 more) to saw five timber, and **S** to stop the order.
The inspector shows the order, what is at hand and what has been made.

Almost all of this step is content. The code adds only the founders'
workplaces, the sawhorse's commands and how it's drawn.

## The content

`goods.json` has a new good:

```json
{ "id": "lumber", "name": "Lumber", "mass_kg": 3.0, "volume_l": 4.0 }
```

`recipes.json` has a new workshop and its recipe:

```json
{ "id": "sawhorse", "name": "Sawhorse" }
...
{ "id": "saw_timber", "name": "Saw timber into lumber", "workshop": "sawhorse",
  "inputs": { "timber": 1 }, "outputs": { "lumber": 2 }, "work_minutes": 20 }
```

The hand quern has worked this way since step 13b, so nothing new was needed
to run the recipe:
- the workshop asks for its timber;
- a colonist carries the timber there and works the recipe;
- the lumber is carried to a store.

**Mass isn't kept:** 10 kg of timber goes in and 6 kg of lumber comes out.
The ledger counts units, not kilograms, so the 4 kg of bark and offcuts
simply leave the colony's goods. That's D6's "accounted processing loss".

`skills.json` adds Carpentry for the work `"sawhorse"`. Felling taught us
that every recipe needs a challenge, so `saw_timber` has one.

In `construction.json`, four pieces changed:

| Piece | Before | Now |
|---|---|---|
| Door | 3 timber, 45 min | 6 lumber, 34 min |
| Floor | 1 timber, 10 min | 2 lumber, 8 min |
| Water barrel | 3 timber, 40 min | 6 lumber, 30 min |
| Wash basin | 2 timber, 20 min | 4 lumber, 15 min |

A quarter less of 45 is 33.75 and of 10 is 7.5. Minutes are whole numbers, so
those are rounded up (34 and 8), never giving more than the quarter.

## Carrying

Nothing in the carrying code changed. `CarryUnits` still divides what a
person carries by the good's mass:

```csharp
int CarryUnits(string good) => Math.Max(1, (int)Math.Floor(content.CarryKg / content.Goods[good].MassKg));
```

25 kg ÷ 3 kg is 8.33, rounded down to 8 lumber a trip, against 2 timber
(25 ÷ 10). A door's 6 lumber is one trip, where its 3 timber were two.

`Math.Floor` is Python's `math.floor`; the `(int)` turns the result into a
whole number.

## The founders' workplaces

`founding.json` has a new list, `"workplaces": [ "sawhorse" ]`. Each must be a
workshop of `recipes.json`, and `Content.cs` checks that. `Founding.Arrive`
sets each one up after the bedrolls are laid:

```csharp
foreach (string workshop in content.Founding.Workplaces)
{
    var (wx, wy) = OpenNear(ground, x + 2, y, used);
    work.AddSite(workshop, wx + 0.5, wy + 0.5);
}
```

`OpenNear` finds the open tile nearest a point that isn't used yet: the
sawhorse goes as near as it can to two tiles east of the carts.

A new skill changes the founders. Their skills are drawn in order of the
skill's id, and "carpentry" comes first, so the same seed now gives founders
with other proficiencies and interests than before. Some tests had to set a
colonist's skill or interest outright for that reason.

## Ordering at the sawhorse

`Inspect.Commands` gives a workplace's tile two commands:

```csharp
int more = (site.Recipe == recipe.Id ? site.Batches : 0) + 5;
commands.Add(new Command("Order 5 more", 'O', ..., () => work.Order(id, recipe.Id, more)));
```

`() => ...` is a lambda, like Python's `lambda: ...`: the order is only
placed when the button is pressed.

`more` is worked out when the commands are made, which is each time the tile
is looked at. So a second press adds 5 to the order as it stands then.

## Exercise

1. In `recipes.json`, make sawing give 3 lumber instead of 2. Run
   `dotnet test tests --filter LumberTests`. Which test notices, and why
   that one?
2. In `goods.json`, change lumber's `mass_kg` to 2.5. How many lumber does a
   colonist carry now? Work it out first, then check in the game by watching
   a delivery to a door.
3. In `founding.json`, remove `"sawhorse"` from `workplaces` (leave the list
   empty: `[]`). Found a colony. What can't the founders do, and what can
   they still build?
4. In `construction.json`, set the door back to `{ "timber": 3 }`. Found a
   colony and plan the small house. Is it finished sooner or later on the
   first day?

Put each change back afterwards.
