# Lesson A2: seeing through the trees

In a dense wood the crowns hide the colonists. The specification you adopted
(Forest Visibility and Canopy Occlusion v1.3) says how to help: the crowns in
front of a colonist you're looking at fade, but never so much that the wood
stops looking like a wood.

This step tries it out in the asset preview, not yet in the game. T's last
option, "the colony's trees", is where the preview now starts.

- **Point at a colonist,** or click one (Shift-click adds more, Esc lets go).
  The crowns in front of them fade.
- **M** turns on the manual foliage mode, which fades every crown; holding
  **N** does the same while it's held.
- **R** makes the fades jump instead of glide (Reduced Motion).
- **O** turns fading off, so you can compare.

## The arithmetic: `Canopy` in src/Colony

### When does a crown cover a colonist?

```csharp
public static bool Covers(Crown crown, Target target) => crown.FootY > target.FeetY && crown.Bounds.Overlaps(target.Bounds);
```

A crown covers a colonist when two things are true:
- the tree is drawn in front of them: its foot is lower on the screen than
  their feet;
- the crown's box (from the manifest's `crown_bounds`) overlaps the colonist's
  box.

### How see-through should each crown be?

One crown at opacity `a` hides `a` of what is behind it and lets `1 − a`
through. Two crowns let `(1 − a) × (1 − a)` through, and *n* crowns
`(1 − a)^n`. The specification caps what they hide together at 45 %. So each
of *n* crowns gets:

```csharp
public static double Share(double cap, int crowns) =>
    crowns <= 0 ? 1 : 1 - Math.Pow(1 - Math.Clamp(cap, 0, 1), 1.0 / crowns);
```

For one crown that's 45 %. For three it's about 18 % each, and three crowns at
18 % hide 45 % together.

### A crown in front of two colonists

The crown takes the smaller of the two shares, so both colonists stay within
the cap. That's `Math.Min` in `Canopy.Goals`.

`goals.GetValueOrDefault(crown.Index, 1)` reads from a dictionary with a
fallback, like Python's `d.get(key, 1)`.

## Fading over time: `Fader`

Each frame, the preview calls

```csharp
fader.Step(delta, goals, manualNow ? manualOpacity : null, manualNow ? AllCrowns() : null);
```

`delta` is the real time since the last frame, in seconds. The fader:
- moves each crown towards its goal, down from solid in 0.15 s;
- after a crown loses its goal, waits 0.15 s;
- then brings it back to solid in 0.3 s.

The wait stops crowns flickering while a colonist walks past their edge.

The times are real seconds, so pausing the game or running it fast wouldn't
change them. The specification asks for exactly this (O5).

`double?` means "a decimal, or nothing", like `Optional[float]` in Python.
`manualNow ? manualOpacity : null` passes the manual mode's opacity only when
that mode is on.

## Drawing a tree in two parts

The crown fades but the trunk doesn't (O3). So the preview draws each tree's
picture in two pieces with `DrawTextureRectRegion`, which draws one part of a
picture:

```csharp
DrawTextureRectRegion(picture, crownOnScreen, new Rect2(0, 0, art.Width, art.CrownLine), opacity);
DrawTextureRectRegion(picture, trunkOnScreen, new Rect2(0, art.CrownLine, art.Width, art.Height - art.CrownLine));
```

- The first draws the rows above the crown line, tinted with the crown's
  opacity: a `Color` of (1, 1, 1, opacity) leaves the colours as they are and
  changes only how see-through they are.
- The second draws the trunk, solid.

The outlines round picked colonists are drawn after everything else, so the
leaves never dim them (O4).

## What the check found

The cap was checked on the rendered pixels as well as by the tests. A colonist
in a wood with a tree on every tile was drawn once as a red box and once as a
blue box; where the red and blue show through the crowns differently, you can
work out how much the crowns hide. Over that colonist, seven crown boxes
overlapped, but on the actual pixels the crowns hid at most 29 %. No pixel has
all seven crowns on it, so the cap of 45 % is never reached there. The
crowns are clearer than the cap allows, which the specification permits; your
eyes will judge whether that's too clear.

## Exercise

1. Press `-` a few times to lower the cap to 20 %, and look at a colonist in
   the wood (W, then point). Then raise it to 70 % with `=`. Which looks
   right to you?
2. In `Fader`, change `Hold` from 0.15 to 0.6, and walk the mouse slowly
   across a few colonists. What changes?
3. In `AssetPreview.Canopy.cs`, the corners are drawn in `DrawMarks`, after
   the wood. Move the call to `DrawMarks()` in `_Draw` before `DrawWood()`.
   What happens to the corners of a colonist in the wood, and why?

Put each change back afterwards.
