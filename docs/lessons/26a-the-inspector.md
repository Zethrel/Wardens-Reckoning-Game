# Lesson 26a: the inspector

Your UI plan (`docs/ui-plan.md`) starts with the inspector: a grey column 320
pixels wide, always on the right, a quarter of a 1280 × 720 window even when
nothing is picked. This step (the plan's phase A, UI-01) gives that space back
to the map.

- Nothing picked: no inspector at all; the map fills the screen.
- Something picked: the inspector lies **over** the map's right side.
- A colonist is shown **compact** at first; **More** (or M) shows everything,
  **Less** goes back.
- **×** (or I) hides it but keeps what is picked; a small **◂ Inspector** tab
  at the right edge (or I again) brings it back.
- **Esc** lets go of what is picked, and the inspector goes. A second Esc
  opens the menu, as before.

## Over the map, not beside it

Before, the screen's middle was an `HBoxContainer`: a box that lays its
children out side by side. The map got what the inspector left.

```csharp
var middle = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
```

Now it is a plain `Control`, which lays out nothing: each child places itself
with **anchors**. The map's frame is anchored to all four sides
(`LayoutPreset.FullRect`), so it fills the middle; the inspector to the right
side, top to bottom (`LayoutPreset.RightWide`), and then pulled 320 pixels
left:

```csharp
side.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
side.OffsetLeft = -InspectorWidth;
```

An anchor is a fraction of the parent (0 is its left or top edge, 1 its right
or bottom); an offset is pixels from that point. Right-wide sets both of the
side's horizontal anchors to 1 (the right edge); an offset of -320 on the left
makes it 320 wide. Because the inspector is a later child, it is drawn on top
of the map, and clicks on it stop there instead of reaching the map.

Opening and closing it never moves the map: the camera keeps looking at the
same place.

## Compact: the brief lines

Which lines are "the main things" is a decision about the colony, not about
Godot, so it is made in `src/Colony/Inspect.cs`, where tests can check it:

```csharp
public sealed record Inspection(string Title, IReadOnlyList<Line> Lines)
{
    public IReadOnlyList<Line>? Brief { get; init; }
}
```

The `{ get; init; }` property is set only when the record is made:

```csharp
return new Inspection(w.Name, lines) { Brief = Brief(lines, ...) };
```

`Brief` picks from the full list, so the compact view says exactly what the
full one does, never something worded differently:

```csharp
var brief = lines.Where(l => l.Label is "Doing" or "Schedule").ToList();
var wanting = lines.Where(l => needs.Contains(l.Label) && !l.Text.EndsWith(", fine")).ToList();
brief.AddRange(wanting.Count > 0 ? wanting : new[] { new Line("Needs", "all fine") });
```

`l.Label is "Doing" or "Schedule"` is a *pattern*: true if the label is either.

The plan warned about one trap (UI-02): the need numbers are deficits, where 0
is fine and 100 is at the limit. The brief keeps them as words and numbers,
exactly as the full list has them; bars come with phase B, and must fill the
right way round.

A tile has no brief (`null`), so `Inspect.Shown` gives all its lines and the
screen hides the More button.

## Nothing in the simulation changes

Hiding, showing, More and Less change only what the screen draws: `hidden` and
`expanded` are fields of `ColonyScreen`, not of the colony. The colony is not
saved with them and does not know about them, which is the plan's "closing a
panel must not cancel or change a colonist's activity".

## Tests

`InspectorTests` checks the brief: three lines for a colonist in good shape
("Needs: all fine"), only the needs that are not fine (word for word as in
the full list), and no brief for a tile. The screen itself is Godot, so it is
checked by hand on the PC.

## Exercise

1. In `ColonyScreen.cs`, change `InspectorWidth` to 400. Run the game: what
   gets better, what worse, at 1280 × 720?
2. In `Inspect.Brief`, add `"Carrying"` to the first `Where`. Run
   `InspectorTests`: which test fails, and what would you change in it?
3. Should a picked colonist's "Where" be in the brief? Try it, play a little,
   and decide.
