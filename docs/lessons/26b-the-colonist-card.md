# Lesson 26b: the colonist's card

Step 26a gave the map its space back. This step (your UI plan's phase B,
UI-02) makes what the inspector says about a colonist readable at a glance,
in groups:

1. **Name, status and schedule.** "Between jobs · available for work", "Standard
   day, now work".
2. **Doing.** The same words as before ("between jobs, pottering about").
3. **What needs seeing to now:** lacking something, unwell. In a warm red.
4. **Needs**, as bars.
5. Behind **More**: skills and interests, where they are and what they carry.
6. With **F3**: the developer's trace.

It is also the first piece in your "Warden's Ledger" look: a charcoal panel
with a thin brass edge, parchment words, brass headings.

## The card: a record of groups

```csharp
public sealed record ColonistCard(string Name, string Status, string Schedule, string Doing, IReadOnlyList<NeedBar> Needs,
                                  IReadOnlyList<Line> Alerts, IReadOnlyList<Line> Skills, IReadOnlyList<Line> More,
                                  IReadOnlyList<Line> Diagnostics);
```

`Inspect.Card` makes it. It does not write anything new about the colonist:
it takes the full list (`Inspect.Worker`, the same lines as before) and sorts
them into groups:

```csharp
lines.Where(l => l.Label is "Lacking" or "Unwell").ToList(),
```

So the card can never say something the full list does not. A test checks
exactly that: every line in a group is one of the full list's lines.

26a's compact view (`Inspection.Brief`) is gone: the card's groups, with More,
do its job better.

## Needs: which way round?

Your plan warned about this (UI-02): a need is counted as a **deficit**. 0 is
"not at all", 100 is "at its limit". So the bar **fills as the need grows**,
and changes colour with its word:

```csharp
public sealed record NeedBar(string Name, double Level, string Word, int Pressing);
```

`Pressing` is the word as a number, 0 (fine) to 4 (critical), and
`Ledger.Pressing` turns it into a colour: green, brass, amber, orange, red. The
number and the word are written beside the bar too ("35 · wants"), so nobody
has to tell colours apart to read it (FP-11: never by colour alone). The
group's heading says it in words: "Needs: how pressing".

A Godot `ProgressBar` draws the bar. Its look comes from two *style boxes*:
`background` (the empty track) and `fill` (the filled part):

```csharp
bar.AddThemeStyleboxOverride("background", Ledger.Flat(Ledger.CharcoalLight));
bar.AddThemeStyleboxOverride("fill", Ledger.Flat(Ledger.Pressing(need.Pressing)));
```

A theme override changes one look on one control, leaving the rest of the
game as it was.

## The status in a few words

```csharp
public static string Status(Work work, Worker w) =>
    w.Collapsed != null ? "Collapsed"
    : w.Asleep ? "Asleep"
    : w.Personal != null ? "Seeing to a need"
    ...
```

A chain of `condition ? a : b`: the first that is true wins, so the order says
what matters most. Collapsed comes before asleep, working before between
jobs. Every condition is something the colony itself keeps; nothing is
guessed (your plan: "Report the actual known cause").

## The Ledger look in one place

`src/UI/Ledger.cs` holds the colours and the panel's style. Only the inspector
uses it so far; when the art pass comes (your plan, section 6), the other
screens take theirs from the same place, so the game stays one look.

## Exercise

1. In `Ledger.cs`, change `Brass` to `new(0.55f, 0.65f, 0.75f)` (a cool steel).
   Run the game: does the inspector still feel like a ledger?
2. In `Inspect.Status`, move the `w.Job != null` line above `w.Asleep`. Can a
   colonist be asleep and hold a job? Read `Process` in `Work.cs` to find out.
3. Make the bars show **satisfaction** instead (100 minus the level). What else
   would you have to change so the colours and words still agree? (Then put it
   back: the plan asks that the meaning is never reversed by accident.)
