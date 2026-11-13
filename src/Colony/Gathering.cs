// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>A gathering place (step 25d): an area the player marks, where talks happen more often. Its number, its
/// name ("Gathering place 1") and the tiles it covers.</summary>
public sealed record Gathering(BuildingId Id, string Name, Area Area);

/// <summary>
/// Gathering places (step 25d; library Review 42, Daily Life §11.1 "Places and time"): an area the player marks, as a
/// stockpile is, where talks happen more often. Nothing is built and nothing is held: it only changes where a
/// colonist wishing to talk meets the one they ask, and how readily they say yes (leisure.json "gathering"). A talk
/// under way there is joined before one elsewhere.
/// </summary>
public sealed partial class Work
{
    readonly SortedDictionary<long, Gathering> gatherings = new();

    /// <summary>The gathering places, in the order they were marked.</summary>
    public IReadOnlyCollection<Gathering> Gatherings => gatherings.Values;

    /// <summary>A gathering place from a save.</summary>
    internal void Put(Gathering gathering) => gatherings[gathering.Id.Value] = gathering;

    /// <summary>
    /// Whether a gathering place may cover `area`: every tile on the map, open ground (nothing in the way, no wall,
    /// no water), and not already in another gathering place. It may lie over a stockpile or a road.
    /// </summary>
    public Placement CheckGathering(Area area)
    {
        var ground = Ground ?? throw new InvalidOperationException("a gathering place needs ground to lie on");
        foreach (var (x, y) in area.Tiles)
        {
            if (!ground.Inside(x, y))
                return new Placement(false, "(" + x + ", " + y + ") is off the map.");
            if (!ground.Open(x, y))
                return new Placement(false, "(" + x + ", " + y + ") is not open ground.");
            if (GatheringAt(x, y) is Gathering other)
                return new Placement(false, "(" + x + ", " + y + ") is already in " + other.Name + ".");
        }
        return new Placement(true);
    }

    /// <summary>A gathering place over `area`, if CheckGathering allows it, named "Gathering place 1", "Gathering place
    /// 2"... (the lowest number not in use).</summary>
    public (Gathering? Gathering, Placement Placement) PlaceGathering(Area area)
    {
        var check = CheckGathering(area);
        if (!check.Ok)
            return (null, check);
        var names = gatherings.Values.Select(g => g.Name).ToHashSet();
        int n = 1;
        while (names.Contains("Gathering place " + n))
            n++;
        var gathering = new Gathering(colony.Ids.Next<BuildingId>(), "Gathering place " + n, area);
        gatherings[gathering.Id.Value] = gathering;
        return (gathering, check);
    }

    /// <summary>Removes a gathering place. Talks there go on to their end; new ones meet elsewhere.</summary>
    public void RemoveGathering(BuildingId id)
    {
        if (!gatherings.Remove(id.Value))
            throw new ArgumentException("no gathering place " + id.Value, nameof(id));
    }

    /// <summary>The gathering place covering tile (x, y), if any.</summary>
    public Gathering? GatheringAt(int x, int y) => gatherings.Values.FirstOrDefault(g => g.Area.Covers(x, y));

    /// <summary>Whether the spot (x, y), in metres, is in a gathering place.</summary>
    bool InGathering(double x, double y) => Ground.TileOf(x, y) is var (tx, ty) && GatheringAt(tx, ty) != null;

    /// <summary>
    /// Where two colonists would meet to talk at a gathering place (step 25d), or null: a tile of a gathering place
    /// within `within` of where the one asking looks (cx, cy), open, with nothing built on it and held by no outing,
    /// that both can walk to; of those, the one nearest the middle between them (ties: the lowest row, then column).
    /// </summary>
    (double X, double Y)? MeetingPoint(Worker w, Worker p, double cx, double cy, double within)
    {
        if (gatherings.Count == 0 || Ground is not Ground g)
            return null;
        double mx = (w.X + p.X) / 2, my = (w.Y + p.Y) / 2;
        var held = workers.Values.Where(o => o.Outing != null).Select(o => Ground.TileOf(o.Outing!.X, o.Outing.Y)).ToHashSet();
        var (wx, wy) = Ground.TileOf(w.X, w.Y);
        var (px, py) = Ground.TileOf(p.X, p.Y);
        (double X, double Y)? best = null;
        double bestMetres = double.MaxValue;
        foreach (var gathering in gatherings.Values)
            foreach (var (x, y) in gathering.Area.Tiles.OrderBy(t => t.Y).ThenBy(t => t.X))
            {
                var (sx, sy) = Ground.Centre(x, y);
                double metres = Math.Sqrt((sx - mx) * (sx - mx) + (sy - my) * (sy - my));
                if (metres >= bestMetres || (sx - cx) * (sx - cx) + (sy - cy) * (sy - cy) > within * within
                    || !g.Open(x, y) || g.PieceAt(x, y) != null || held.Contains((x, y))
                    || !g.CanReach(wx, wy, x, y) || !g.CanReach(px, py, x, y))
                    continue;
                (best, bestMetres) = ((sx, sy), metres);
            }
        return best;
    }
}
