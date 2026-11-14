# Lesson 26d: pointing things out

Your UI plan's phase D has two parts:

- **UI-04, selection and hover.** The colonist or tile you picked is marked in
  brass with a dark edge, so it shows on any ground. Whatever is under the
  mouse is marked lightly, and a small tooltip beside the mouse says who it is
  and what they are doing, or what is notable on a tile ("Tree", "Road", the
  name of a store or a gathering place: `Inspect.Hint`).
- **UI-05, alerts.** At the map's top right, the few things you may want to act
  on: someone collapsed or with a need at critical, someone unwell or lacking
  something, no food left free, buildings held up. A click goes to it; × puts
  it away.

## Alerts are worked out, not kept

The plan asks: "avoid repeated alerts for the same unresolved condition". One
way would be a list of messages, with code to remember which were already
said. Another, simpler: work the alerts out from the colony as it is, every
time.

```csharp
public sealed record Alert(string Key, int Severity, string Text, ColonistId? Who = null, (int X, int Y)? Where = null);
```

The **key** names the condition: `"collapsed:2"` (colonist 2 is down),
`"waits:no colonist can reach it"`. While the condition holds, every working
out gives the same key, so the screen shows one alert, not a new one each
time. When the condition clears, its key is gone, and so is the alert. Nothing
is saved, and nothing can be out of date.

`Who = null` in the record's list is a **default value**: an alert about the
colony as a whole ("no food") leaves `Who` and `Where` out.

## Few on purpose

```csharp
foreach (var (need, state) in w.Needs)
    if (Word(content.Needs[need], state.Level) == "critical")
```

Only **critical** needs are alerted. An urgent thirst is part of a working day
(colonists see to it themselves), and an alert for every one would bury the
ones that matter. The plan: "prefer severity and relevance over volume".

Buildings held up are **grouped by reason** with LINQ's `GroupBy`: twenty walls
waiting for timber are one alert, "20 buildings wait: ...", leading to the
first.

## The screen's side

`ColonyScreen.Notices.cs` is a new part of `ColonyScreen` (a `partial class`,
as `Work` is). Two small things are worth seeing:

- **Made again only when they change.** The alerts are worked out four times
  a second, but the buttons are only made again when the *signature* (keys and
  words) changes, so a button is never replaced while you are pressing it.
- **The tooltip lets the mouse through:** `MouseFilter = Ignore`. Without it,
  the tooltip under the mouse would catch the clicks meant for the map.

Over a panel or the bar, or with a build tool in hand, nothing is hovered:
`GuiGetHoveredControl()` tells which control is under the mouse, and only the
map's frame counts.

## Not by colour alone

An alert's seriousness is in its colour **and** a mark: `!` danger, `•` want,
`·` work held up (FP-11, as with the need bars).

## Exercise

1. In `Inspect.Alerts`, add urgent needs too (`"urgent"` as well as
   `"critical"`). Play a day: is it better or worse? Put it back if worse.
2. Change `MostAlerts` to 3. When would you miss the fourth?
3. Plan five walls with too little timber. How many alerts do you get, and
   why only that many?
