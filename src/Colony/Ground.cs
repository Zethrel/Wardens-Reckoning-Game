// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Diagnostics;
using WardensReckoning.World;

namespace WardensReckoning.Colony;

/// <summary>
/// The ground the colony stands on (step 15a): a grid of tiles, each 1 m square (Review 32), and what is on each
/// one: something in the way (a rock, a tree) or a finished construction piece (a wall, a door, a floor). Until the
/// colony is founded on a local map of the world generator's export (step 17b, `FromMap`), or on the flat test
/// ground of construction.json (`Test`).
///
/// Tile (x, y) covers the metres from x to x + 1 across and y to y + 1 down; its centre is (x + 0.5, y + 0.5).
///
/// Each tile has a **surface** (grass, rough ground; step 15c, library Review 34), and walking on it goes at that
/// surface's speed: 1 is a road's (100%), grass 0.8, rough ground 0.55. A piece may set its own speed (a road, a
/// floor: 1); otherwise the surface beneath it counts. A surface of speed 0 (fresh water, salt water; library
/// Review 36) cannot be walked on or built on: it blocks, like a rock.
///
/// The ground also finds the colonists' ways (path-finding): from any tile to any other, around what blocks
/// walking, moving to any of the 8 tiles around, and never cutting a corner past a wall or a rock (a slanting step
/// needs both tiles beside it to be open). A step costs its length (1 m across, √2 = 1.41 m on the slant) divided
/// by the speed of the tile it steps onto: so a metre of grass counts as 1.25 m of road, and the way found is the
/// quickest, not always the shortest.
///
/// And it finds the **rooms** (step 15b, library Review 33): a room is space closed in by walls and doors (pieces
/// that close a room in), with no way out to the edge of the ground. Its open tiles count as indoors. There are no
/// roofs yet. Trees and rocks close nothing in (step 17b: on a local map, a tile ringed by trees is not a room),
/// and water is open to the sky: space that holds water is outdoors, so a wall built round a pond is no room.
/// </summary>
public sealed class Ground
{
    public int Width { get; }
    public int Height { get; }

    readonly string?[] obstacles;
    readonly string?[] pieces;
    readonly bool[] blocked;
    readonly bool[] pieceBlocks;
    readonly bool[] encloses;
    // Ways already worked out, by the tile they lead to, each with when it was last asked for (a count of the
    // asking); forgotten whenever something that blocks is built, and the least lately asked for when there are
    // more than `MostWays` (step 20c).
    // The ways known, by goal: the metres from every tile, when it was last asked for, and how far it was worked
    // out (infinity: the whole ground; step 25a: a short way stops at a limit).
    readonly Dictionary<int, (double[] Far, long Asked, double Limit)> ways = new();
    // The areas, once worked out (step 22): for every tile, the number of the area of open tiles it belongs to, or
    // -1 if it is blocked. Forgotten whenever a tile becomes blocked or open.
    int[]? areaOf;
    // The rooms, once worked out; forgotten whenever anything is built.
    List<IReadOnlyList<(int X, int Y)>>? rooms;
    int[]? roomOf;

    readonly string[] surfaceOf;
    readonly Dictionary<string, Surface> surfaces = new(StringComparer.Ordinal);
    readonly double[] pieceSpeed;

    /// <summary>
    /// For measuring only (step 20b, the performance scenarios): how many times a way was asked for, how many of
    /// them had to be worked out (not already known), and the time that took, in `Stopwatch` ticks
    /// (`Stopwatch.Frequency` a second). Nothing in the colony reads these, and they are not saved, so measuring
    /// changes nothing that happens.
    /// </summary>
    public long WaysAsked { get; private set; }
    /// <inheritdoc cref="WaysAsked"/>
    public long WaysWorked { get; private set; }
    /// <inheritdoc cref="WaysAsked"/>
    public long WayTime { get; private set; }
    /// <summary>How many ways are known now (each holds a number for every tile of the ground).</summary>
    public int WaysKept => ways.Count;

    /// <summary>
    /// How many ways are kept at most (step 20c). Each holds a number for every tile: on a local map of 256 ×
    /// 256 tiles that is half a megabyte, so 512 ways are 256 MB. When a way is worked out and there are already
    /// this many, the one least lately asked for is forgotten (and worked out again if it is asked for again: the
    /// same way, so this changes only how long things take, never what happens).
    ///
    /// It is a ceiling, not a saving: the colonists ask for a way to every job they weigh up, and with fewer kept
    /// than the jobs in play they work the same ways out again and again (measured in step 20c: 300 walls planned
    /// and 64 kept took five times as long as keeping them all).
    /// </summary>
    public int MostWays { get; set; } = DefaultMostWays;

    /// <summary>The usual `MostWays`.</summary>
    public const int DefaultMostWays = 512;

    /// <summary>A ground of `surface` everywhere (if not given: plain ground at a road's speed).</summary>
    public Ground(int width, int height, Surface? surface = null)
    {
        if (width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "a ground is at least 1 tile by 1 tile");
        (Width, Height) = (width, height);
        obstacles = new string?[width * height];
        pieces = new string?[width * height];
        blocked = new bool[width * height];
        pieceBlocks = new bool[width * height];
        encloses = new bool[width * height];
        var everywhere = surface ?? new Surface("ground", "Ground", 1);
        if (!everywhere.CanWalk)
            throw new ArgumentException("a ground cannot be made of \"" + everywhere.Id + "\" everywhere: it cannot be walked on", nameof(surface));
        surfaces[everywhere.Id] = everywhere;
        surfaceOf = new string[width * height];
        Array.Fill(surfaceOf, everywhere.Id);
        pieceSpeed = new double[width * height];
        Array.Fill(pieceSpeed, double.NaN);
        Surface = everywhere;
    }

    /// <summary>The surface the ground was made of everywhere (other tiles aside).</summary>
    public Surface Surface { get; }

    /// <summary>The test ground of the content: its size, its surfaces and what is in the way.</summary>
    public static Ground Test(Content content)
    {
        var tg = content.TestGround;
        var ground = new Ground(tg.Width, tg.Height, content.Surfaces[tg.Surface]);
        foreach (var patch in tg.Patches)
            for (int y = patch.Y; y < patch.Y + patch.Height; y++)
                for (int x = patch.X; x < patch.X + patch.Width; x++)
                    ground.SetSurface(x, y, content.Surfaces[patch.Surface]);
        foreach (var o in tg.Obstacles)
            ground.AddObstacle(o.Kind, o.X, o.Y);
        return ground;
    }

    /// <summary>
    /// The ground of a local map (step 17b): a tile for each of the map's, with its surface (each must be one of the
    /// content's, by its id) and its tree or rock as an obstacle. The ground's own surface (`Surface`) is the one
    /// most of its tiles have (on a tie, the first by name), so a save lists as few other tiles as can be.
    /// </summary>
    public static Ground FromMap(LocalMap map, Content content)
    {
        var missing = map.Surfaces.Where(s => !content.Surfaces.ContainsKey(s)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException("the local map has surfaces the content does not know: " + string.Join(", ", missing)
                                        + " (construction.json needs a surface of each name)", nameof(map));
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int y = 0; y < map.Size; y++)
            for (int x = 0; x < map.Size; x++)
                count[map.SurfaceAt(x, y)] = count.GetValueOrDefault(map.SurfaceAt(x, y)) + 1;
        // The most common surface that can be walked on (a ground is never made all of water).
        var walkable = count.Where(c => content.Surfaces[c.Key].CanWalk).ToList();
        if (walkable.Count == 0)
            throw new ArgumentException("the local map is all water: there is nowhere to stand", nameof(map));
        string own = walkable.First(c => c.Value == walkable.Max(w => w.Value)).Key;
        var ground = new Ground(map.Size, map.Size, content.Surfaces[own]);
        for (int y = 0; y < map.Size; y++)
            for (int x = 0; x < map.Size; x++)
            {
                if (map.SurfaceAt(x, y) != own)
                    ground.SetSurface(x, y, content.Surfaces[map.SurfaceAt(x, y)]);
                if (map.ThingAt(x, y) is string thing)
                    ground.AddObstacle(thing, x, y);
            }
        return ground;
    }

    /// <summary>The surface of a tile.</summary>
    public Surface SurfaceAt(int x, int y) => surfaces[surfaceOf[Index(x, y)]];

    /// <summary>The walking speed on a tile (1 = a road's): its piece's, if the piece has one, or its surface's.</summary>
    public double Speed(int x, int y)
    {
        double own = pieceSpeed[Index(x, y)];
        return double.IsNaN(own) ? surfaces[surfaceOf[Index(x, y)]].Speed : own;
    }

    internal void SetSurface(int x, int y, Surface surface)
    {
        if (surfaces.TryGetValue(surface.Id, out var known) && known != surface)
            throw new ArgumentException("a different surface called \"" + surface.Id + "\" is already on this ground");
        surfaces[surface.Id] = surface;
        int i = Index(x, y);
        surfaceOf[i] = surface.Id;
        Reblock(i);
        ways.Clear();
        rooms = null;
    }

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>The tile a point in metres is on.</summary>
    public static (int X, int Y) TileOf(double x, double y) => ((int)Math.Floor(x), (int)Math.Floor(y));

    /// <summary>The middle of a tile, in metres.</summary>
    public static (double X, double Y) Centre(int x, int y) => (x + 0.5, y + 0.5);

    int Index(int x, int y) => y * Width + x;

    /// <summary>What is in the way on a tile ("rock", "tree"), or null.</summary>
    public string? ObstacleAt(int x, int y) => Inside(x, y) ? obstacles[Index(x, y)] : null;

    /// <summary>The finished piece on a tile ("wall", "door", "floor"), or null.</summary>
    public string? PieceAt(int x, int y) => Inside(x, y) ? pieces[Index(x, y)] : null;

    /// <summary>Whether someone can walk onto a tile: on the ground, and nothing blocking on it.</summary>
    public bool Open(int x, int y) => Inside(x, y) && !blocked[Index(x, y)];

    /// <summary>Every finished piece, row by row, as (piece, x, y) (for a save).</summary>
    public IEnumerable<(string Piece, int X, int Y)> Pieces =>
        Enumerable.Range(0, Width * Height).Where(i => pieces[i] != null).Select(i => (pieces[i]!, i % Width, i / Width));

    internal void AddObstacle(string kind, int x, int y)
    {
        if (!Inside(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), "(" + x + ", " + y + ") is not on the ground");
        obstacles[Index(x, y)] = kind;
        Reblock(Index(x, y));
        ways.Clear();
        rooms = null;
    }

    /// <summary>A finished piece put on a tile (`blocks`: whether it blocks walking; `closes`: whether it closes
    /// a room in, as a wall or a door does; if not given, the same as `blocks`).</summary>
    internal void AddPiece(string piece, bool blocks, int x, int y, bool? closes = null, double? speed = null)
    {
        pieces[Index(x, y)] = piece;
        encloses[Index(x, y)] = closes ?? blocks;
        pieceSpeed[Index(x, y)] = speed ?? double.NaN;
        rooms = null;
        pieceBlocks[Index(x, y)] = blocks;
        Reblock(Index(x, y));
        if (blocks || speed != null)
            ways.Clear();
    }

    /// <summary>Takes the piece off a tile (step 15c): the tile is as it was before, whatever was beneath.</summary>
    internal void RemovePiece(int x, int y)
    {
        int i = Index(x, y);
        pieces[i] = null;
        encloses[i] = false;
        pieceSpeed[i] = double.NaN;
        pieceBlocks[i] = false;
        Reblock(i);
        rooms = null;
        ways.Clear();
    }

    /// <summary>Whether a tile is blocked: something in the way on it, a piece that blocks, or a surface that cannot be
    /// walked on (water).</summary>
    void Reblock(int i)
    {
        bool was = blocked[i];
        blocked[i] = obstacles[i] != null || pieceBlocks[i] || !surfaces[surfaceOf[i]].CanWalk;
        if (blocked[i] != was)
            areaOf = null;                                // the areas are worked out again when next asked for
    }

    /// <summary>Whether a tile is water, or anything else that cannot be walked on by its surface.</summary>
    public bool Water(int x, int y) => Inside(x, y) && !surfaces[surfaceOf[Index(x, y)]].CanWalk;

    // ---- Rooms -------------------------------------------------------------------------------------------

    /// <summary>Every room: its tiles, row by row. Rooms are listed by their first tile (top row first, then left
    /// to right), so they always come in the same order.</summary>
    public IReadOnlyList<IReadOnlyList<(int X, int Y)>> Rooms
    {
        get
        {
            FindRooms();
            return rooms!;
        }
    }

    /// <summary>The room a tile is in (its place in `Rooms`), or null: outdoors, or not an open tile.</summary>
    public int? RoomAt(int x, int y)
    {
        if (!Inside(x, y))
            return null;
        FindRooms();
        int r = roomOf![Index(x, y)];
        return r < 0 ? null : r;
    }

    /// <summary>Whether a tile is indoors: in a room.</summary>
    public bool Indoors(int x, int y) => RoomAt(x, y) != null;

    /// <summary>
    /// Works the rooms out: every group of tiles that do not close a room in (everything but walls and doors),
    /// joined across (not on the slant: a slanting gap cannot be walked through either), is filled out from its
    /// first tile. A group that reaches the edge of the ground, or holds water, is outdoors; in any other group, the
    /// tiles a person can stand on (not a tree, a rock or a well) are a room.
    /// </summary>
    void FindRooms()
    {
        if (rooms != null)
            return;
        rooms = new();
        roomOf = new int[Width * Height];
        Array.Fill(roomOf, -1);
        var seen = new bool[Width * Height];
        bool Space(int x, int y) => Inside(x, y) && !encloses[Index(x, y)];
        for (int start = 0; start < Width * Height; start++)
        {
            if (seen[start] || !Space(start % Width, start / Width))
                continue;
            var group = new List<int>();
            var todo = new Stack<int>();
            todo.Push(start);
            seen[start] = true;
            bool outdoors = false;
            while (todo.Count > 0)
            {
                int i = todo.Pop();
                group.Add(i);
                int x = i % Width, y = i / Width;
                if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1 || Water(x, y))
                    outdoors = true;
                foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                    if (Space(x + dx, y + dy) && !seen[Index(x + dx, y + dy)])
                    {
                        seen[Index(x + dx, y + dy)] = true;
                        todo.Push(Index(x + dx, y + dy));
                    }
            }
            group.RemoveAll(i => !Open(i % Width, i / Width));
            if (outdoors || group.Count == 0)
                continue;
            group.Sort();
            rooms.Add(group.Select(i => (i % Width, i / Width)).ToList());
        }
        // Listed by their first tile (a group can begin at a tree, before its first open tile).
        rooms.Sort((a, b) => Index(a[0].X, a[0].Y).CompareTo(Index(b[0].X, b[0].Y)));
        for (int r = 0; r < rooms.Count; r++)
            foreach (var (x, y) in rooms[r])
                roomOf[Index(x, y)] = r;
    }

    // The 8 steps from a tile, always in this order (so that ties always go the same way): across first, then
    // on the slant.
    static readonly (int Dx, int Dy, double Metres)[] Steps =
    {
        (0, -1, 1), (1, 0, 1), (0, 1, 1), (-1, 0, 1),
        (1, -1, Math.Sqrt(2)), (1, 1, Math.Sqrt(2)), (-1, 1, Math.Sqrt(2)), (-1, -1, Math.Sqrt(2)),
    };

    /// <summary>Whether a step from (x, y) by (dx, dy) is allowed: onto an open tile, and on the slant only if
    /// both tiles beside it are open (no cutting corners).</summary>
    bool CanStep(int x, int y, int dx, int dy) =>
        Open(x + dx, y + dy) && (dx == 0 || dy == 0 || (Open(x + dx, y) && Open(x, y + dy)));

    /// <summary>
    /// The way to a tile: for every tile, the metres of walking from its centre to the centre of (gx, gy), or
    /// infinity where there is no way. Worked out once (Dijkstra's method: spread out from the goal, always
    /// going on from the nearest tile not yet done) and kept until something blocking is built, or until it is the
    /// least lately asked for of more than `MostWays`.
    /// </summary>
    double[] WayTo(int gx, int gy, double limit = double.PositiveInfinity, int? from = null)
    {
        int goal = Index(gx, gy);
        WaysAsked++;
        // A way worked out at least this far will do; so will a shorter one that already reaches the tile asked from
        // (its metres there are exact: see below).
        if (ways.TryGetValue(goal, out var known)
            && (known.Limit >= limit || from is int f && !double.IsPositiveInfinity(known.Far[f])))
        {
            ways[goal] = (known.Far, WaysAsked, known.Limit);
            return known.Far;
        }
        long started = Stopwatch.GetTimestamp();
        var far = new double[Width * Height];
        Array.Fill(far, double.PositiveInfinity);
        far[goal] = 0;
        // Tiles still to do, nearest first. A tile is put in again each time a quicker way to it is found, and
        // the older entries are passed over when they come out (`metres > far[i]`): cheaper than finding and
        // taking out the old entry (step 20c; until then a SortedSet did that). Which of two equally near tiles
        // comes out first does not matter: the metres every tile ends with are the same either way.
        var todo = new PriorityQueue<int, double>();
        todo.Enqueue(goal, 0);
        bool cut = false;
        while (todo.TryDequeue(out int i, out double metres))
        {
            if (metres > far[i])
                continue;                                       // an older entry: this tile is already done
            if (metres > limit)
            {
                cut = true;                                     // everything left is farther than asked for
                break;
            }
            int x = i % Width, y = i / Width;
            // Stepping from a neighbour onto this tile: the step's length at this tile's speed.
            double speed = Speed(x, y);
            foreach (var (dx, dy, step) in Steps)
            {
                if (!Inside(x + dx, y + dy))
                    continue;
                int n = Index(x + dx, y + dy);
                // A neighbour already as near as this tile cannot be reached any quicker through it: skip it
                // before the dearer checks below (step 20c).
                if (far[n] <= metres)
                    continue;
                // A step from the neighbour to here is allowed exactly when a step from here to it is.
                if (!CanStep(x, y, dx, dy))
                    continue;
                double through = metres + step / speed;
                if (through < far[n])
                {
                    far[n] = through;
                    todo.Enqueue(n, through);
                }
            }
        }
        // A way cut short (step 25a): every tile done is exact (Dijkstra's method finishes the nearer tiles first);
        // the rest are forgotten, so a number kept is always right, and a tile beyond the limit reads as "not worked
        // out" (infinity). From a tile inside the limit, the quickest next step never goes beyond it, so walking on
        // such a way is exactly as on the whole one.
        if (cut)
            for (int t = 0; t < far.Length; t++)
                if (far[t] > limit)
                    far[t] = double.PositiveInfinity;
        if (ways.Count >= MostWays && !ways.ContainsKey(goal))
            ways.Remove(ways.MinBy(w => w.Value.Asked).Key);    // the least lately asked for
        ways[goal] = (far, WaysAsked, cut ? limit : double.PositiveInfinity);
        WaysWorked++;
        WayTime += Stopwatch.GetTimestamp() - started;
        return far;
    }

    /// <summary>
    /// Whether there is any way at all from tile (x, y) to tile (gx, gy) (step 22): exactly when `Metres` would
    /// not be infinity, but without working the way out. The open tiles fall into areas: tiles you can walk
    /// between, across (on the slant only past two open tiles, so a slant step never joins two areas). Two open
    /// tiles have a way between them when they are in the same area. Someone standing on a blocked tile can
    /// step off it onto any open tile beside it (as `Metres` allows), and a blocked tile can be walked onto from
    /// beside it if it has a speed (a finished wall's tile, to work at it; not water).
    /// </summary>
    public bool CanReach(int x, int y, int gx, int gy)
    {
        if (!Inside(x, y) || !Inside(gx, gy))
            return false;
        if (x == gx && y == gy)
            return true;
        var area = Areas();
        if (Open(x, y) && Open(gx, gy))
            return area[Index(x, y)] == area[Index(gx, gy)];
        // At least one end blocked: the areas it touches.
        var from = Touching(x, y, area);
        if (!Open(gx, gy) && !(Speed(gx, gy) > 0))
            return false;                                 // nothing can be stepped onto there (water)
        var to = Touching(gx, gy, area);
        foreach (int a in from)
            if (to.Contains(a))
                return true;
        return false;
    }

    // The areas of tile (x, y): its own if it is open, or else of the open tiles beside it that can be stepped to.
    List<int> Touching(int x, int y, int[] area)
    {
        var touching = new List<int>(8);
        if (Open(x, y))
            touching.Add(area[Index(x, y)]);
        else
            foreach (var (dx, dy, _) in Steps)
                if (CanStep(x, y, dx, dy))
                    touching.Add(area[Index(x + dx, y + dy)]);
        return touching;
    }

    /// <summary>How many areas of open tiles the ground has now (step 22; see `CanReach`).</summary>
    public int AreaCount => Areas().DefaultIfEmpty(-1).Max() + 1;

    // The areas: a fill from each open tile not yet in one, across to open tiles beside it. Every tile is looked at
    // a few times, with no sorting: far cheaper than working out one way.
    int[] Areas()
    {
        if (areaOf != null)
            return areaOf;
        var area = new int[Width * Height];
        Array.Fill(area, -1);
        var todo = new Stack<int>();
        int next = 0;
        for (int start = 0; start < area.Length; start++)
        {
            if (blocked[start] || area[start] != -1)
                continue;
            area[start] = next;
            todo.Push(start);
            while (todo.Count > 0)
            {
                int i = todo.Pop();
                int x = i % Width, y = i / Width;
                for (int s = 0; s < 4; s++)                  // the four steps across (the first four of Steps)
                {
                    int nx = x + Steps[s].Dx, ny = y + Steps[s].Dy;
                    if (!Inside(nx, ny))
                        continue;
                    int n = Index(nx, ny);
                    if (blocked[n] || area[n] != -1)
                        continue;
                    area[n] = next;
                    todo.Push(n);
                }
            }
            next++;
        }
        return areaOf = area;
    }

    /// <summary>How far it is to walk from tile (x, y) to tile (gx, gy), counted in metres of road (a metre of
    /// grass at 80% counts as 1.25 m), or infinity if there is no way. Someone standing on a blocked tile (a wall
    /// just finished where they stood) can still step off it.</summary>
    public double Metres(int x, int y, int gx, int gy) => Metres(x, y, gx, gy, double.PositiveInfinity);

    /// <summary>As `Metres`, working the way out only as far as `limit` metres from the goal, if it is not already
    /// known (step 25a: a short walk need not search the whole ground). Farther than that: worked out in full, so the
    /// answer is the same either way.</summary>
    public double Metres(int x, int y, int gx, int gy, double limit)
    {
        if (!Inside(x, y) || !Inside(gx, gy))
            return double.PositiveInfinity;
        if (x == gx && y == gy)
            return 0;
        var far = WayTo(gx, gy, limit, Open(x, y) ? Index(x, y) : null);
        double best;
        if (Open(x, y))
            best = far[Index(x, y)];
        else
        {
            best = double.PositiveInfinity;
            foreach (var (dx, dy, step) in Steps)
                if (CanStep(x, y, dx, dy))
                    best = Math.Min(best, step / Speed(x + dx, y + dy) + far[Index(x + dx, y + dy)]);
        }
        // Only an answer within the limit is sure to be right (from a blocked tile, a step beside it beyond the limit
        // reads as "not worked out" and a dearer one may win): beyond it, the whole way after all, so the answer is
        // always the same as without a limit.
        return best > limit ? Metres(x, y, gx, gy) : best;
    }

    /// <summary>The next tile on the way from (x, y) to (gx, gy), or null if there is no way (or already there).
    /// Of the tiles around, the one with the quickest way left (the first in `Steps` on a tie).</summary>
    public (int X, int Y)? Next(int x, int y, int gx, int gy)
    {
        if (!Inside(gx, gy) || (x == gx && y == gy))
            return null;
        var far = WayTo(gx, gy, from: Open(x, y) ? Index(x, y) : null);
        (int, int)? best = null;
        double left = double.PositiveInfinity;
        foreach (var (dx, dy, step) in Steps)
        {
            if (!CanStep(x, y, dx, dy))
                continue;
            double through = step / Speed(x + dx, y + dy) + far[Index(x + dx, y + dy)];
            if (through < left)
                (best, left) = ((x + dx, y + dy), through);
        }
        return best;
    }
}
