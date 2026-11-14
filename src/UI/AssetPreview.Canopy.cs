// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Colony;

namespace WardensReckoning.UI;

/// <summary>
/// Canopy fading in the asset preview (prototype step A2; the adopted Forest Visibility and Canopy Occlusion
/// specification v1.3, Experiments A to C of its §10.2). It works on the colony's own trees (`res://assets/vegetation/`,
/// T's last option, "the colony's trees"), in the two fields and in the test wood (W).
///
/// - Point at a colonist to see through the crowns in front of them (hover, B1); click to pick one (Shift-click adds
///   more, B6); Esc lets go.
/// - Each crown in front of a colonist you're looking at fades whole (O2); its trunk stays solid (crown line, N7 a).
/// - Over each such colonist, the crowns together hide no more than the cap (B4; N8: each crown's share, the
///   smallest for a crown in front of several).
/// - Picked colonists get a brass outline drawn above the leaves (O4); the one under the mouse a light one.
/// - The fades run in real seconds (O5): down in 0.15 s, held 0.15 s, back in 0.3 s; R (Reduced Motion) makes them
///   jump.
/// - M turns the manual foliage mode on, and holding N turns it on while held (B10): every crown at its own opacity
///   (9 and 0 change it), never above a picked colonist's share.
/// - O turns the fading off and on (to compare, and to time it with the test wood); - and = change the cap.
///
/// It only draws: nothing of the game is involved. The arithmetic is in src/Colony/Canopy.cs, with its tests.
/// </summary>
public partial class AssetPreview
{
    // The colony's tree pictures by kind, with what the manifest says about each.
    readonly Dictionary<string, (TreeArt Art, Texture2D Picture)> colonyTrees = new();
    readonly Fader fader = new();
    bool fading = true, manual;
    double cap = 0.45, manualOpacity = 0.6;
    readonly HashSet<string> picked = new();
    string? hovered;
    // The last frame's covered targets: the most crowns over one, and the most they hid.
    (int Crowns, double Hidden) worst;

    // The colonists standing in the test wood, by tile from its corner.
    static readonly (int X, int Y)[] WoodColonists = { (5, 4), (12, 9), (20, 6), (30, 15), (45, 20), (8, 30) };

    /// <summary>Whether T is on the last option: the colony's own trees, the ones that can fade.</summary>
    bool ColonyTrees => treeSet == Sets.Length + Experiments.Length;

    void LoadColonyTrees()
    {
        const string folder = "res://assets/vegetation/";
        if (!Godot.FileAccess.FileExists(folder + "manifest.json"))
        {
            missing.Add("vegetation/manifest.json");
            return;
        }
        foreach (var art in TreeArts.Read(Godot.FileAccess.GetFileAsString(folder + "manifest.json")))
            if (ResourceLoader.Exists(folder + art.File) && GD.Load<Texture2D>(folder + art.File) is Texture2D picture)
                colonyTrees[art.Kind] = (art, picture);
            else
                missing.Add("vegetation/" + art.File);
    }

    static string KindName(int kind) => kind == 1 ? "deciduous" : "conifer";

    // ---- The colonists who can be looked at, and the crowns near them --------------------------------------------

    // Every colonist drawn with pictures: their name here, and where their feet are.
    IEnumerable<(string Id, Vector2 Feet)> Colonists()
    {
        yield return ("standing", Standing);
        yield return ("walker", Walker());
        if (wood > 0)
            for (int i = 0; i < WoodColonists.Length; i++)
                yield return ("wood " + (i + 1), new Vector2(WoodColonists[i].X + 0.5f, WoodTop + WoodColonists[i].Y + 0.5f));
    }

    // A colonist's box (their picture's: 32 × 48 pixels, feet at (16, 47)), in tiles.
    static Box ColonistBox(Vector2 feet) => new(feet.X - 16.0 / Pixels, feet.Y - 47.0 / Pixels, 1, 48.0 / Pixels);

    // A crown's number: the fields' trees 0 to 3 (their place in Trees), the wood's from 1000 on.
    static int WoodCrown(int x, int y) => 1000 + y * WoodWide + x;

    // The crowns that may be in front of a colonist whose feet are at `feet`: the fields' four trees, and the wood's
    // trees on the tiles from one left to one right of theirs and from their row to three rows below (a crown reaches
    // less than three tiles up from its foot and one tile to each side).
    IEnumerable<Canopy.Crown> CrownsNear(Vector2 feet)
    {
        for (int i = 0; i < Trees.Length; i++)
        {
            var (tx, ty, kind) = Trees[i];
            if (colonyTrees.TryGetValue(KindName(kind), out var tree))
                yield return new Canopy.Crown(i, ty + 1, tree.Art.Crown(tx + 0.5, ty + 1));
        }
        if (wood == 0)
            yield break;
        int fx = (int)Math.Floor(feet.X), fy = (int)Math.Floor(feet.Y) - WoodTop;
        for (int y = Math.Max(0, fy); y <= Math.Min(WoodHigh - 1, fy + 3); y++)
            for (int x = Math.Max(0, fx - 1); x <= Math.Min(WoodWide - 1, fx + 1); x++)
                if (WoodKind(x, y) is int kind && colonyTrees.TryGetValue(KindName(kind), out var tree))
                    yield return new Canopy.Crown(WoodCrown(x, y), WoodTop + y + 1, tree.Art.Crown(x + 0.5, WoodTop + y + 1));
    }

    // Every crown there is (for the manual mode).
    IEnumerable<int> AllCrowns() =>
        Enumerable.Range(0, Trees.Length).Concat(wood > 0 ? WoodTrees().Select(t => WoodCrown(t.X, t.Y)) : Enumerable.Empty<int>());

    // ---- Each frame ------------------------------------------------------------------------------------------------

    /// <summary>Works out who is looked at, which crowns should fade, and moves the fades on by `seconds` (real time).</summary>
    void UpdateCanopy(double seconds)
    {
        var mouse = GetGlobalMousePosition();
        var colonists = Colonists().ToList();
        // Under the mouse: the front-most colonist whose picture the mouse is on.
        hovered = colonists.Where(c => ColonistBox(c.Feet).Contains(mouse.X, mouse.Y)).OrderByDescending(c => c.Feet.Y)
                           .Select(c => c.Id).FirstOrDefault();
        picked.RemoveWhere(id => colonists.All(c => c.Id != id));          // the wood went away, say

        var goals = new Dictionary<int, double>();
        var looked = colonists.Where(c => picked.Contains(c.Id) || c.Id == hovered).ToList();
        var nearby = new Dictionary<int, Canopy.Crown>();
        if (ColonyTrees && fading)
        {
            foreach (var c in looked)
                foreach (var crown in CrownsNear(c.Feet))
                    nearby[crown.Index] = crown;
            goals = Canopy.Goals(looked.Select(c => new Canopy.Target(c.Feet.Y, ColonistBox(c.Feet))), nearby.Values.ToList(), cap);
        }
        bool manualNow = ColonyTrees && (manual || Input.IsKeyPressed(Key.N));
        fader.Step(seconds, goals, manualNow ? manualOpacity : null, manualNow ? AllCrowns() : null);

        // For the panel: over each colonist looked at, how many crowns are in front and how much they hide now.
        worst = (0, 0);
        foreach (var c in looked)
        {
            var target = new Canopy.Target(c.Feet.Y, ColonistBox(c.Feet));
            var over = nearby.Values.Where(crown => Canopy.Covers(crown, target)).ToList();
            double hidden = Canopy.Hidden(over.Select(crown => fader.OpacityOf(crown.Index)));
            worst = (Math.Max(worst.Crowns, over.Count), Math.Max(worst.Hidden, hidden));
        }
    }

    string CanopyLine() =>
        !ColonyTrees ? "Canopy fading: on the colony's trees only (T to \"the colony's trees\")"
        : "Canopy fading " + (fading ? "on" : "off") + " (O) · cap " + (cap * 100).ToString("0") + " % (- =)"
          + " · manual mode " + (manual || Input.IsKeyPressed(Key.N) ? "on" : "off") + " (M, hold N) at " + (manualOpacity * 100).ToString("0") + " % (9 0)"
          + " · Reduced Motion " + (fader.ReducedMotion ? "on" : "off") + " (R)\n"
          + "Looking at: " + (picked.Count == 0 && hovered == null ? "nobody (point at or click a colonist; Shift adds; Esc lets go)"
                              : string.Join(", ", picked.OrderBy(p => p)) + (hovered != null && !picked.Contains(hovered) ? (picked.Count > 0 ? " + " : "") + hovered + " (under the mouse)" : ""))
          + (worst.Crowns > 0 ? " · in front of them: up to " + worst.Crowns + " crowns, hiding " + (worst.Hidden * 100).ToString("0.0") + " % at most now" : "")
          + " · crowns fading: " + fader.Fading;

    // ---- Input -----------------------------------------------------------------------------------------------------

    /// <summary>The canopy's keys and the left mouse button; true if the event was one of them.</summary>
    bool CanopyInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                var mouse = GetGlobalMousePosition();
                var at = Colonists().Where(c => ColonistBox(c.Feet).Contains(mouse.X, mouse.Y)).OrderByDescending(c => c.Feet.Y)
                                    .Select(c => c.Id).FirstOrDefault();
                if (!click.ShiftPressed)
                    picked.Clear();
                if (at != null && !picked.Add(at))
                    picked.Remove(at);                     // Shift-click on a picked one lets go of it
                return true;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.Keycode)
                {
                    case Key.O: fading = !fading; return true;
                    case Key.M: manual = !manual; return true;
                    case Key.R: fader.ReducedMotion = !fader.ReducedMotion; return true;
                    case Key.Minus: cap = Math.Max(0.05, cap - 0.05); return true;
                    case Key.Equal: cap = Math.Min(0.95, cap + 0.05); return true;
                    case Key.Key9: manualOpacity = Math.Max(0.05, manualOpacity - 0.05); return true;
                    case Key.Key0: manualOpacity = Math.Min(1, manualOpacity + 0.05); return true;
                    case Key.Escape: picked.Clear(); return true;
                }
                return false;
        }
        return false;
    }

    // ---- Drawing ---------------------------------------------------------------------------------------------------

    /// <summary>One of the colony's trees, its foot on `foot`: the crown (the rows above the crown line) at its crown's
    /// opacity now, then the trunk, always solid. False if there is no picture for that kind.</summary>
    bool DrawColonyTree(Vector2 foot, int kind, int crown)
    {
        if (!colonyTrees.TryGetValue(KindName(kind), out var tree))
            return false;
        var (art, picture) = tree;
        var box = art.Picture(foot.X, foot.Y, Pixels);
        float split = art.CrownLine / (float)Pixels;
        var opacity = new Color(1, 1, 1, (float)fader.OpacityOf(crown));
        DrawTextureRectRegion(picture, new Rect2((float)box.X, (float)box.Y, (float)box.Width, split),
                              new Rect2(0, 0, art.Width, art.CrownLine), opacity);
        DrawTextureRectRegion(picture, new Rect2((float)box.X, (float)box.Y + split, (float)box.Width, (float)box.Height - split),
                              new Rect2(0, art.CrownLine, art.Width, art.Height - art.CrownLine));
        return true;
    }

    /// <summary>The outlines, above everything (O4): brass corners round each picked colonist, with a dark edge so
    /// they show on any ground; a light frame round the one under the mouse.</summary>
    void DrawMarks()
    {
        float px = 1 / camera!.Zoom.X;
        foreach (var (id, feet) in Colonists())
        {
            var box = ColonistBox(feet);
            var rect = new Rect2((float)box.X - 2 * px, (float)box.Y - 2 * px, (float)box.Width + 4 * px, (float)box.Height + 4 * px);
            if (picked.Contains(id))
            {
                Corners(rect, Ink, 5 * px);
                Corners(rect, Ledger.Brass, 2.5f * px);
            }
            else if (id == hovered)
                DrawRect(rect, new Color(1, 1, 1, 0.8f), false, 1.5f * px);
        }
    }

    // The four corners of `rect`, each a quarter of its shorter side long.
    void Corners(Rect2 rect, Color colour, float width)
    {
        float arm = Math.Min(rect.Size.X, rect.Size.Y) / 4;
        var (a, b) = (rect.Position, rect.End);
        foreach (var (corner, dx, dy) in new[] { (a, 1f, 1f), (new Vector2(b.X, a.Y), -1f, 1f), (new Vector2(a.X, b.Y), 1f, -1f), (b, -1f, -1f) })
        {
            DrawLine(corner, corner + new Vector2(dx * arm, 0), colour, width);
            DrawLine(corner, corner + new Vector2(0, dy * arm), colour, width);
        }
    }
}
