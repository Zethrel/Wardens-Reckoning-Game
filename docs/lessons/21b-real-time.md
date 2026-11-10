# Lesson 21b: real time on the screen

Step 21a made a tick a game second. This step makes the screen follow library
Review 39: at **1x** a game second lasts a real second, and the player speeds
up to 3x, 10x, 30x or 60x. The colony itself does not change; only how many
ticks the screen asks for, and how it draws what happens between them.

## An enum whose numbers mean something

```csharp
public enum Speed
{
    Paused = 0,
    X1 = 1,
    X3 = 3,
    X10 = 10,
    X30 = 30,
    X60 = 60,
}
```

An `enum` names a set of choices. Each has a whole number behind it; here the
number is the speed itself, so `(int)Speed.X30` is 30, the game seconds that
pass in a real second. The pacer uses it directly:

```csharp
owed += seconds * TicksPerSecond * (int)Speed;
```

`Pacer.Speeds` lists them in order for the bar's buttons and the keys 1 to 5.

## Saying what a speed means

Each button's tooltip says what the speed means in time, worked out from the
calendar so it can never disagree with it:

```csharp
double dayMinutes = Calendar.TicksPerDay / (double)(int)speed / 60;
```

86,400 ticks a day, divided by the ticks a real second, divided by 60, gives
real minutes a day: 1,440 at 1x (shown as 24 hours), 24 at 60x. The
`(double)` makes it a division with decimals; without it, `86400 / 7` would
drop the remainder, because dividing two whole numbers in C# gives a whole
number.

## Drawing between two ticks

At 1x a tick comes once a second, and a walking colonist moves about a metre.
Drawn only where they are, they would hop a metre a second. So the screen
draws them **on the way** from where they stood before the last tick to where
they are now, as far as the next tick is due:

```csharp
float t = (float)Math.Clamp(Due, 0, 1);
return new Vector2((float)(from.X + (w.X - from.X) * t), (float)(from.Y + (w.Y - from.Y) * t));
```

This is **linear interpolation**: `from + (to - from) × t` is `from` at `t =
0`, `to` at `t = 1`, and evenly between. `Due` comes from the pacer (what is
owed towards the next tick, 0 to 1).

Where they stood before comes from an **event**: the session calls
`BeforeTick` just before each tick it runs, and the view, which subscribed with
`session.BeforeTick += view.Remember;`, notes everyone's position. `+=` adds a
method to an event's list; `-=` (in `_ExitTree`, when the screen closes) takes
it off again, so a closed screen is not called any more.

The drawn colonist is up to one tick behind the true one: at most a second, a
metre. Clicking still finds them, since the inspector's reach is larger.

Nothing of this is in the colony or in a save: drawing never changes what
happens (Shared Systems §19.1).

## What to expect at 1x

Real time is slow on purpose: a building that takes 30 minutes of work takes 30
real minutes at 1x. The bar's tooltips say how long a day takes at each speed.
Colonists may also stand for up to a game minute (a real minute at 1x) before
setting off and after arriving, because everything but walking is still decided
on the whole minutes (step 21a).

## Exercise

1. In `Speed.cs`, add `X120 = 120` to the enum and to `Pacer.Speeds`. Run the
   game: what does the new button's tooltip say? Run `dotnet test tests --filter
   ColonyTests`: which test fails, and why? Take it out again.
2. In `ColonyView.DrawnAt`, replace `t` by `1`. Watch a colonist walk at 1x:
   what do you see?
3. Why does the screen set `Due` to 1 when paused? (Hint: where would a
   colonist be drawn while paused if it did not?)
