// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json;

namespace WardensReckoning.Colony;

/// <summary>
/// A tree's picture (step 27a, Vegetation Art Standard v1.1): which kind it is, its file, its size in picture pixels,
/// the foot (the pixel placed on the middle of the tree tile's bottom edge), the crown line (the first row of the
/// trunk: the rows above it are the crown, the part that fades) and the crown's bounds (the box, in picture pixels,
/// used to tell which crowns cover a colonist). All of it comes from `assets/vegetation/manifest.json`.
/// </summary>
public sealed record TreeArt(string Kind, string File, int Width, int Height, int FootX, int FootY, int CrownLine,
                             int CrownX, int CrownY, int CrownWidth, int CrownHeight)
{
    /// <summary>The whole picture, in tiles, for a tree whose foot is at (`footX`, `footY`) (tiles), with `pixels`
    /// picture pixels to a tile (32). The foot pixel's left edge and its bottom row's bottom edge are the foot: for
    /// the deciduous tree (64 × 96, foot (32, 95)) that is the middle of the picture's bottom edge.</summary>
    public Box Picture(double footX, double footY, int pixels = 32) =>
        new(footX - FootX / (double)pixels, footY - (FootY + 1.0) / pixels, Width / (double)pixels, Height / (double)pixels);

    /// <summary>The crown's bounds, in tiles, for a tree whose foot is at (`footX`, `footY`).</summary>
    public Box Crown(double footX, double footY, int pixels = 32)
    {
        var picture = Picture(footX, footY, pixels);
        return new(picture.X + CrownX / (double)pixels, picture.Y + CrownY / (double)pixels,
                   CrownWidth / (double)pixels, CrownHeight / (double)pixels);
    }
}

/// <summary>A box: its top left corner, width and height (in tiles, or in whatever unit it was made in).</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// <summary>Whether the two boxes share any area (boxes that only touch along an edge don't).</summary>
    public bool Overlaps(Box other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    /// <summary>Whether a point is inside the box.</summary>
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>Reading the tree pictures' manifest, and choosing which picture a tree tile shows.</summary>
public static class TreeArts
{
    /// <summary>Reads `assets/vegetation/manifest.json` (its text): one TreeArt for each entry in `assets`.</summary>
    public static IReadOnlyList<TreeArt> Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var arts = new List<TreeArt>();
        foreach (var a in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            var foot = a.GetProperty("foot");
            var crown = a.GetProperty("crown_bounds");
            arts.Add(new TreeArt(a.GetProperty("kind").GetString()!, a.GetProperty("file").GetString()!,
                                 a.GetProperty("width").GetInt32(), a.GetProperty("height").GetInt32(),
                                 foot[0].GetInt32(), foot[1].GetInt32(), a.GetProperty("crown_line").GetInt32(),
                                 crown[0].GetInt32(), crown[1].GetInt32(), crown[2].GetInt32(), crown[3].GetInt32()));
        }
        return arts;
    }

    /// <summary>
    /// Which kind of tree a tree tile shows: "conifer" for about one tile in three, "deciduous" for the rest. The
    /// simulation only knows "tree"; this is how it looks, nothing more (the maintainer, 14-11-2026). It comes from a
    /// fixed sum of the tile's numbers, so the same tile always shows the same tree, in every game and on every PC.
    /// (C#'s own GetHashCode can't be used: it gives different numbers each time the game runs.)
    /// </summary>
    public static string KindAt(int x, int y)
    {
        uint h = unchecked((uint)x * 73856093u ^ (uint)y * 19349663u);
        h ^= h >> 13;
        h = unchecked(h * 0x5bd1e995u);
        h ^= h >> 15;
        return h % 3 == 0 ? "conifer" : "deciduous";
    }
}

/// <summary>
/// Canopy fading (prototype step A2; the adopted Forest Visibility and Canopy Occlusion specification v1.3): how
/// see-through each tree crown should be so that the colonists the player is looking at can be seen through the
/// trees in front of them. Only the arithmetic is here, so the tests can check it; the screens draw with it. It
/// changes nothing in the colony.
///
/// The rules it follows:
/// - A crown covers (obstructs) a target when it is drawn in front of it (its foot is lower on the screen than the
///   target's feet) and its crown box overlaps the target's box (O9).
/// - A whole crown fades, never a patch of it (O2).
/// - Over each target, all the crowns covering it together hide no more than the cap (45 % at first, B3, B4). One
///   crown at opacity a lets 1 − a of what is behind through; n crowns at a let (1 − a)^n through, so they hide
///   1 − (1 − a)^n. Each crown gets the share a = 1 − (1 − cap)^(1/n), so that the n of them hide exactly the cap.
/// - A crown in front of several targets takes the smallest of their shares, so every one of them stays within its
///   cap (the specification's N8 and N9).
/// </summary>
public static class Canopy
{
    /// <summary>A crown that may cover a target: its number (the caller's, to find it again), where its tree's foot
    /// is on the screen (in tiles, down), and its crown box.</summary>
    public readonly record struct Crown(int Index, double FootY, Box Bounds);

    /// <summary>Something the player is looking at (a colonist): where its feet are on the screen, and its box.</summary>
    public readonly record struct Target(double FeetY, Box Bounds);

    /// <summary>Each crown's opacity so that `crowns` of them, one over another, hide exactly `cap` (0 to 1).</summary>
    public static double Share(double cap, int crowns) =>
        crowns <= 0 ? 1 : 1 - Math.Pow(1 - Math.Clamp(cap, 0, 1), 1.0 / crowns);

    /// <summary>How much crowns with these opacities hide, one over another: 1 − (1 − a₁)(1 − a₂)…</summary>
    public static double Hidden(IEnumerable<double> opacities) => 1 - opacities.Aggregate(1.0, (through, a) => through * (1 - a));

    /// <summary>Whether `crown` covers `target`: in front of it, and overlapping it.</summary>
    public static bool Covers(Crown crown, Target target) => crown.FootY > target.FeetY && crown.Bounds.Overlaps(target.Bounds);

    /// <summary>
    /// How see-through each covering crown should be, once fully faded: crown number → opacity. Crowns that cover no
    /// target aren't in it (they stay as they are). `crowns` must hold every crown that could cover any of the
    /// targets (the screens pick them from the tiles near each target), or a target's count would be too low.
    /// </summary>
    public static Dictionary<int, double> Goals(IEnumerable<Target> targets, IReadOnlyList<Crown> crowns, double cap)
    {
        var goals = new Dictionary<int, double>();
        foreach (var target in targets)
        {
            var over = crowns.Where(c => Covers(c, target)).ToList();
            double share = Share(cap, over.Count);
            foreach (var crown in over)
                goals[crown.Index] = Math.Min(share, goals.GetValueOrDefault(crown.Index, 1));
        }
        return goals;
    }
}

/// <summary>
/// The crowns' opacity over time (B5, O5): a crown that should fade goes down to its goal over `FadeIn` seconds;
/// when it no longer should, it waits `Hold` seconds (so a colonist walking past a crown's edge doesn't make it
/// flicker) and then comes back to fully solid over `Restore` seconds. Times are real seconds, given by the screen
/// each frame, so game speed and pause don't change them. With `ReducedMotion` the opacity jumps straight to where
/// it is going (the hold still applies).
/// </summary>
public sealed class Fader
{
    public double FadeIn { get; set; } = 0.15;
    public double Hold { get; set; } = 0.15;
    public double Restore { get; set; } = 0.30;
    public bool ReducedMotion { get; set; }

    // Each crown being faded or coming back: its opacity now, how long it has had no goal, and the lowest it went
    // (so coming back from there takes `Restore` seconds, however far it had faded).
    readonly Dictionary<int, (double Opacity, double Idle, double Low)> crowns = new();

    /// <summary>A crown's opacity now: 1 (solid) unless it is fading.</summary>
    public double OpacityOf(int crown) => crowns.TryGetValue(crown, out var c) ? c.Opacity : 1;

    /// <summary>How many crowns are not fully solid now.</summary>
    public int Fading => crowns.Count;

    /// <summary>Moves every crown `seconds` on, towards `goals` (crown number → opacity; a crown not in it goes back
    /// to solid). `everyCrown`, when not null, is the opacity every crown goes to anyway (the manual foliage mode,
    /// B10): a crown's goal is then the lower of the two, so the cap still holds over targets.</summary>
    public void Step(double seconds, IReadOnlyDictionary<int, double> goals, double? everyCrown = null, IEnumerable<int>? all = null)
    {
        // Crowns with a goal now: start them if new.
        var wanted = new Dictionary<int, double>(goals);
        if (everyCrown is double manual && all != null)
            foreach (int crown in all)
                wanted[crown] = Math.Min(manual, wanted.GetValueOrDefault(crown, 1));
        foreach (var (crown, _) in wanted)
            if (!crowns.ContainsKey(crown))
                crowns[crown] = (1, 0, 1);

        foreach (int crown in crowns.Keys.ToList())
        {
            var (opacity, idle, low) = crowns[crown];
            if (wanted.TryGetValue(crown, out double goal))
            {
                idle = 0;
                // Down from solid to the goal in FadeIn seconds: each second, (1 − goal) / FadeIn of the way.
                opacity = ReducedMotion ? goal : Towards(opacity, goal, (1 - goal) * seconds / FadeIn);
            }
            else
            {
                idle += seconds;
                // After the hold, back up from the lowest it went to solid in Restore seconds. Only the time past
                // the hold counts: a step that ends the hold doesn't also come back by its whole length.
                double past = Math.Min(seconds, idle - Hold);
                if (past > 0)
                    opacity = ReducedMotion ? 1 : Towards(opacity, 1, (1 - low) * past / Restore);
            }
            low = Math.Min(low, opacity);
            if (opacity >= 1 && !wanted.ContainsKey(crown))
                crowns.Remove(crown);
            else
                crowns[crown] = (opacity, idle, low);
        }
    }

    // `from` moved towards `to` by `step`, without going past it.
    static double Towards(double from, double to, double step) =>
        from > to ? Math.Max(to, from - step) : Math.Min(to, from + step);
}
