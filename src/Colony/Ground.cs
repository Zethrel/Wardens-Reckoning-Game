// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// The ground the colony stands on (step 15a): a grid of tiles, each 1 m square (Review 32), and what is on each
/// one: something in the way (a rock, a tree) or a finished construction piece (a wall, a door, a floor). Until the
/// world generator makes local terrain (step 17), this is the flat test ground of construction.json.
///
/// Tile (x, y) covers the metres from x to x + 1 across and y to y + 1 down; its centre is (x + 0.5, y + 0.5).
///
/// Each tile has a **surface** (grass, rough ground; step 15c, library Review 34), and walking on it goes at that
/// surface's speed: 1 is a road's (100%), grass 0.8, rough ground 0.55. A piece may set its own speed (a road, a
/// floor: 1); otherwise the surface beneath it counts.
///
/// The ground also finds the colonists' ways (path-finding): from any tile to any other, around what blocks
/// walking, moving to any of the 8 tiles around, and never cutting a corner past a wall or a rock (a slanting step
/// needs both tiles beside it to be open). A step costs its length (1 m across, √2 = 1.41 m on the slant) divided
/// by the speed of the tile it steps onto: so a metre of grass counts as 1.25 m of road, and the way found is the
/// quickest, not always the shortest.
///
/// And it finds the **rooms** (step 15b, library Review 33): a room is a group of open tiles closed in on every
/// side by walls, doors or anything else that blocks, with no open way out to the edge of the ground. Its tiles
/// count as indoors. There are no roofs yet.
/// </summary>
public sealed class Ground
{
    public int Width { get; }
    public int Height { get; }

    readonly string?[] obstacles;
    readonly string?[] pieces;
    readonly bool[] blocked;
    readonly bool[] encloses;
    // Ways already worked out, by the tile they lead to; forgotten whenever something that blocks is built.
    readonly Dictionary<int, double[]> ways = new();
    // The rooms, once worked out; forgotten whenever anything is built.
    List<IReadOnlyList<(int X, int Y)>>? rooms;
    int[]? roomOf;

    readonly string[] surfaceOf;
    readonly Dictionary<string, Surface> surfaces = new(StringComparer.Ordinal);
    readonly double[] pieceSpeed;

    /// <summary>A ground of `surface` everywhere (if not given: plain ground at a road's speed).</summary>
    public Ground(int width, int height, Surface? surface = null)
    {
        if (width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "a ground is at least 1 tile by 1 tile");
        (Width, Height) = (width, height);
        obstacles = new string?[width * height];
        pieces = new string?[width * height];
        blocked = new bool[width * height];
        encloses = new bool[width * height];
        var everywhere = surface ?? new Surface("ground", "Ground", 1);
        surfaces[everywhere.Id] = everywhere;
        surfaceOf = new string[width * height];
        Array.Fill(surfaceOf, everywhere.Id);
        pieceSpeed = new double[width * height];
        Array.Fill(pieceSpeed, double.NaN);
        Surface = everywhere;
    }

    /// <summary>The surface the ground was made of everywhere (patches aside).</summary>
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

    /// <summary>The surface of a tile.</summary>
    public Surface SurfaceAt(int x, int y) => surfaces[surfaceOf[Index(x, y)]];

    /// <summary>The walking speed on a tile (1 = a road's): its piece's, if the piece has one, or its surface's.</summary>
    public double Speed(int x, int y)
    {
        double own = pieceSpeed[Index(x, y)];
        return double.IsNaN(own) ? surfaces[surfaceOf[Index(x, y)]].Speed : own;
    }

    /// <summary>Every tile whose surface is not the ground's own, row by row, as (surface, x, y) (for a save).</summary>
    public IEnumerable<(Surface Surface, int X, int Y)> Patches =>
        Enumerable.Range(0, Width * Height).Where(i => surfaceOf[i] != Surface.Id).Select(i => (surfaces[surfaceOf[i]], i % Width, i / Width));

    internal void SetSurface(int x, int y, Surface surface)
    {
        if (surfaces.TryGetValue(surface.Id, out var known) && known != surface)
            throw new ArgumentException("a different surface called \"" + surface.Id + "\" is already on this ground");
        surfaces[surface.Id] = surface;
        surfaceOf[Index(x, y)] = surface.Id;
        ways.Clear();
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

    /// <summary>Every obstacle, row by row (for a save).</summary>
    public IEnumerable<Obstacle> Obstacles =>
        Enumerable.Range(0, Width * Height).Where(i => obstacles[i] != null).Select(i => new Obstacle(obstacles[i]!, i % Width, i / Width));

    /// <summary>Every finished piece, row by row, as (piece, x, y) (for a save).</summary>
    public IEnumerable<(string Piece, int X, int Y)> Pieces =>
        Enumerable.Range(0, Width * Height).Where(i => pieces[i] != null).Select(i => (pieces[i]!, i % Width, i / Width));

    internal void AddObstacle(string kind, int x, int y)
    {
        if (!Inside(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), "(" + x + ", " + y + ") is not on the ground");
        obstacles[Index(x, y)] = kind;
        blocked[Index(x, y)] = true;
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
        if (blocks)
            blocked[Index(x, y)] = true;
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
        blocked[i] = obstacles[i] != null;
        rooms = null;
        ways.Clear();
    }

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
    /// Works the rooms out: every group of tiles a person could stand on (open, and not closing a room in, as a
    /// door does), joined across
    /// (not on the slant: a slanting gap cannot be walked through either), is filled out from its first tile. A
    /// group that reaches the edge of the ground is outdoors; any other group is a room.
    /// </summary>
    void FindRooms()
    {
        if (rooms != null)
            return;
        rooms = new();
        roomOf = new int[Width * Height];
        Array.Fill(roomOf, -1);
        var seen = new bool[Width * Height];
        bool Space(int x, int y) => Open(x, y) && !encloses[Index(x, y)];
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
                if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1)
                    outdoors = true;
                foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                    if (Space(x + dx, y + dy) && !seen[Index(x + dx, y + dy)])
                    {
                        seen[Index(x + dx, y + dy)] = true;
                        todo.Push(Index(x + dx, y + dy));
                    }
            }
            if (outdoors)
                continue;
            group.Sort();
            foreach (int i in group)
                roomOf[i] = rooms.Count;
            rooms.Add(group.Select(i => (i % Width, i / Width)).ToList());
        }
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
    /// going on from the nearest tile not yet done) and kept until something blocking is built.
    /// </summary>
    double[] WayTo(int gx, int gy)
    {
        int goal = Index(gx, gy);
        if (ways.TryGetValue(goal, out var known))
            return known;
        var far = new double[Width * Height];
        Array.Fill(far, double.PositiveInfinity);
        far[goal] = 0;
        // Tiles still to do, nearest first (and the lower index on a tie, so it always works out the same).
        var todo = new SortedSet<(double Metres, int Index)> { (0, goal) };
        while (todo.Count > 0)
        {
            var (metres, i) = todo.Min;
            todo.Remove(todo.Min);
            int x = i % Width, y = i / Width;
            foreach (var (dx, dy, step) in Steps)
            {
                // A step from the neighbour to here is allowed exactly when a step from here to it is.
                if (!CanStep(x, y, dx, dy))
                    continue;
                int n = Index(x + dx, y + dy);
                // Stepping from the neighbour onto this tile: its length at this tile's speed.
                double through = metres + step / Speed(x, y);
                if (through < far[n])
                {
                    if (!double.IsPositiveInfinity(far[n]))
                        todo.Remove((far[n], n));
                    far[n] = through;
                    todo.Add((through, n));
                }
            }
        }
        ways[goal] = far;
        return far;
    }

    /// <summary>How far it is to walk from tile (x, y) to tile (gx, gy), counted in metres of road (a metre of
    /// grass at 80% counts as 1.25 m), or infinity if there is no way. Someone standing on a blocked tile (a wall
    /// just finished where they stood) can still step off it.</summary>
    public double Metres(int x, int y, int gx, int gy)
    {
        if (!Inside(x, y) || !Inside(gx, gy))
            return double.PositiveInfinity;
        if (x == gx && y == gy)
            return 0;
        var far = WayTo(gx, gy);
        if (Open(x, y))
            return far[Index(x, y)];
        double best = double.PositiveInfinity;
        foreach (var (dx, dy, step) in Steps)
            if (CanStep(x, y, dx, dy))
                best = Math.Min(best, step / Speed(x + dx, y + dy) + far[Index(x + dx, y + dy)]);
        return best;
    }

    /// <summary>The next tile on the way from (x, y) to (gx, gy), or null if there is no way (or already there).
    /// Of the tiles around, the one with the quickest way left (the first in `Steps` on a tie).</summary>
    public (int X, int Y)? Next(int x, int y, int gx, int gy)
    {
        if (!Inside(gx, gy) || (x == gx && y == gy))
            return null;
        var far = WayTo(gx, gy);
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
