// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;

namespace WardensReckoning.Colony;

/// <summary>
/// A construction order (the design library's construction project): one piece (a wall, a door, a floor) planned on
/// one tile. Its materials are brought to it and kept in its own inventory (`Stock`, the building site's pile) until
/// a builder uses them up; the piece is finished when the builder has worked it the piece's minutes.
/// </summary>
public sealed class Project(BuildingId id, string piece, int x, int y, InventoryId stock)
{
    public BuildingId Id { get; } = id;
    public string Piece { get; } = piece;
    /// <summary>The tile it is planned on.</summary>
    public int X { get; } = x;
    public int Y { get; } = y;
    /// <summary>The materials brought so far.</summary>
    public InventoryId Stock { get; } = stock;
    /// <summary>Paused by the player: nothing is brought to it or built until it is resumed.</summary>
    public bool Paused { get; internal set; }
    /// <summary>Cancelled: its materials are carried back to a store, then it is gone.</summary>
    public bool Cancelled { get; internal set; }
    /// <summary>A taking down (step 15c), not a building: the piece on the tile is worked off, and half its
    /// materials (rounded down) come back.</summary>
    public bool Removing { get; init; }
    /// <summary>Why it is waiting, for the player, and what they could do about it (null: it is not).</summary>
    public string? Blocked { get; internal set; }
    public string? NextStep { get; internal set; }
}

/// <summary>Whether a piece may be planned on a tile: if not, why not; if so, perhaps a warning (the design
/// library's "valid placement with a warning", Construction §4.1).</summary>
public sealed record Placement(bool Ok, string? Why = null, string? Warning = null);

/// <summary>
/// Construction (step 15a, FP-04): the fourth part of `Work`. The library's loop (Construction §5):
///
/// 1. **Plan** a piece on a tile (`Plan`). It is checked first (`Check`): on the ground, nothing in the way, no
///    piece there already (built or planned), and a wall not on top of a workshop, a store or a bedroll. A piece
///    nobody can reach yet may be planned, with a warning. It may be planned before the materials exist (§5.3).
/// 2. **Reserve and deliver**: each minute a project asks for the materials it lacks, as a workshop does (its
///    request is a haul from the nearest store with some free, no more than a person can carry).
/// 3. **Build**: with all its materials there, a build job claims them; a colonist allowed to "build" walks next
///    to the tile and works it the piece's minutes. Put down, it keeps its progress (Review 32).
/// 4. **Complete**: the materials are used up (one operation of the ledger) and the piece stands on the ground. A
///    wall blocks walking from then on; a door and a floor do not.
///
/// The player can **pause** a project (nothing is brought or built until resumed) or **cancel** it: the materials
/// are only used up when the piece is finished, so cancelling before that gives every unit back: what was
/// delivered is carried back to a store, and goods on their way are taken to a store instead.
/// </summary>
public sealed partial class Work
{
    public const string BuildWork = "build";

    readonly SortedDictionary<long, Project> projects = new();

    public IReadOnlyCollection<Project> Projects => projects.Values;
    public Project? Project(BuildingId id) => projects.GetValueOrDefault(id.Value);

    Ground? Ground => colony.Ground;

    /// <summary>Whether `piece` may be planned on tile (x, y), and why not.</summary>
    public Placement Check(string piece, int x, int y)
    {
        var ground = Ground ?? throw new InvalidOperationException("the colony has no ground to build on");
        if (!content.Pieces.TryGetValue(piece, out var kind))
            throw new ArgumentException("no piece \"" + piece + "\" in the content", nameof(piece));
        bool big = kind.Width > 1 || kind.Height > 1;
        foreach (var (tx, ty) in Footprint(kind, x, y))
        {
            // On a piece bigger than one tile, say which of its tiles is the trouble.
            string at = big ? "At (" + tx + ", " + ty + "): " : "";
            if (!ground.Inside(tx, ty))
                return new Placement(false, at + "It is outside the ground (" + ground.Width + " by " + ground.Height + " tiles).");
            if (ground.ObstacleAt(tx, ty) is string obstacle)
                return new Placement(false, at + "A " + obstacle + " is in the way.");
            if (ground.PieceAt(tx, ty) is string built)
                return new Placement(false, at + "There is already " + Article(content.Pieces[built].Name) + " there.");
            if (projects.Values.FirstOrDefault(p => Covers(p, tx, ty)) is Project planned)
                return new Placement(false, at + Article(content.Pieces[planned.Piece].Name, capital: true) + " is already planned there.");
            if (kind.Blocks && StandsOn(tx, ty) is string thing)
                return new Placement(false, at + thing + " there: " + Article(kind.Name) + " would block it.");
            if (kind.Blocks && projects.Values.FirstOrDefault(p => AccessOf(p) == (tx, ty)) is Project used)
                return new Placement(false, at + "The planned " + content.Pieces[used.Piece].Name.ToLowerInvariant() + " is to be used from there.");
        }
        // A structure is built and used from its access tile: that tile must be open and stay so.
        if (kind.Access is var (dx, dy))
        {
            int ax = x + dx, ay = y + dy;
            if (!ground.Open(ax, ay) || projects.Values.Any(p => Covers(p, ax, ay) && content.Pieces[p.Piece].Blocks))
                return new Placement(false, "It is used from (" + ax + ", " + ay + "), which must be open ground.");
        }
        string? warning = null;
        var (cx, cy) = WorkSpot(kind, x, y);
        if (workers.Count > 0 && workers.Values.All(w => double.IsPositiveInfinity(Far(w.X, w.Y, cx, cy))))
            warning = "No colonist can reach it now; it waits until there is a way.";
        return new Placement(true, Warning: warning);
    }

    /// <summary>The tiles a piece on (x, y) covers: just that one, or a structure's whole footprint.</summary>
    static IEnumerable<(int X, int Y)> Footprint(PieceKind kind, int x, int y) =>
        Enumerable.Range(y, kind.Height).SelectMany(ty => Enumerable.Range(x, kind.Width).Select(tx => (tx, ty)));

    bool Covers(Project p, int x, int y)
    {
        var kind = content.Pieces[p.Piece];
        return x >= p.X && y >= p.Y && x < p.X + kind.Width && y < p.Y + kind.Height;
    }

    (int X, int Y)? AccessOf(Project p) =>
        content.Pieces[p.Piece].Access is var (dx, dy) && !p.Removing ? (p.X + dx, p.Y + dy) : null;

    /// <summary>Where a piece on (x, y) is worked from and its materials are put down: a structure's access tile,
    /// or the tile itself.</summary>
    static (double X, double Y) WorkSpot(PieceKind kind, int x, int y) =>
        kind.Access is var (dx, dy) ? Ground.Centre(x + dx, y + dy) : Ground.Centre(x, y);

    /// <summary>The minutes of work a project takes: a piece's own; taking one down, half of that (at least 1).</summary>
    int Minutes(Project p) =>
        p.Removing ? Math.Max(1, content.Pieces[p.Piece].WorkMinutes / 2) : content.Pieces[p.Piece].WorkMinutes;

    /// <summary>What stands on a tile and must stay reachable (for the player), or null.</summary>
    string? StandsOn(int x, int y)
    {
        bool On(double px, double py) => Ground.TileOf(px, py) == (x, y);
        if (sites.Values.FirstOrDefault(s => On(s.X, s.Y)) is Site site)
            return "The " + content.Workshops[site.Workshop].Name.ToLowerInvariant() + " stands";
        if (bedrolls.Values.Any(b => On(b.X, b.Y)))
            return "A bedroll is laid";
        if (Stocks.Inventories.FirstOrDefault(i => i.Holder == Holder.Store && (i.Area?.Covers(x, y) ?? On(i.X, i.Y))) is Inventory store)
            return "A store (" + store.Name + ") is";
        return null;
    }

    static string Article(string name, bool capital = false) =>
        (capital ? "A" : "a") + ("aeiouAEIOU".Contains(name[0]) ? "n " : " ") + name.ToLowerInvariant();

    /// <summary>Plans `piece` on tile (x, y), if it may be (see `Check`): the new project, or null and why not.</summary>
    public (Project? Project, Placement Placement) Plan(string piece, int x, int y)
    {
        var placement = Check(piece, x, y);
        if (!placement.Ok)
            return (null, placement);
        var (cx, cy) = WorkSpot(content.Pieces[piece], x, y);
        var stock = Stocks.Open(content.Pieces[piece].Name + " at (" + x + ", " + y + ") (site)", Holder.Construction, cx, cy);
        var project = new Project(colony.Ids.Next<BuildingId>(), piece, x, y, stock.Id);
        projects[project.Id.Value] = project;
        return (project, placement);
    }

    /// <summary>
    /// Plans a whole template (library Construction §3.3) with its top left corner on tile (x, y), turned
    /// `turns` quarter turns clockwise and, if `mirror`, flipped left to right first. All or nothing: if any piece
    /// may not go where it would, nothing is planned and the first such piece is named. The pieces are then
    /// ordinary projects, so the player may still cancel or add to them one by one.
    /// </summary>
    public (IReadOnlyList<Project> Projects, Placement Placement) PlanTemplate(string template, int x, int y, int turns = 0, bool mirror = false)
    {
        if (!content.Templates.TryGetValue(template, out var t))
            throw new ArgumentException("no template \"" + template + "\" in the content", nameof(template));
        var laid = Lay(t, turns, mirror).Select(p => (p.Piece, X: x + p.X, Y: y + p.Y)).ToList();
        var warnings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (piece, px, py) in laid)
        {
            var check = Check(piece, px, py);
            if (!check.Ok)
                return (Array.Empty<Project>(), new Placement(false, "At (" + px + ", " + py + "): " + check.Why));
            if (check.Warning != null)
                warnings.Add(check.Warning);
        }
        var planned = laid.Select(p => Plan(p.Piece, p.X, p.Y).Project!).ToList();
        return (planned, new Placement(true, Warning: warnings.Count == 0 ? null : string.Join(" ", warnings)));
    }

    /// <summary>A template's pieces, flipped and turned, still counted from the top left (0, 0).</summary>
    internal static IEnumerable<(string Piece, int X, int Y)> Lay(Template t, int turns, bool mirror)
    {
        int quarter = ((turns % 4) + 4) % 4;
        foreach (var (piece, px, py) in t.Pieces)
        {
            int x = mirror ? t.Width - 1 - px : px, y = py, w = t.Width, h = t.Height;
            for (int i = 0; i < quarter; i++)
                (x, y, w, h) = (h - 1 - y, x, h, w);         // a quarter turn clockwise
            yield return (piece, x, y);
        }
    }

    /// <summary>
    /// Takes down the piece on tile (x, y) (step 15c, library Construction §5.4): a project like a building, of half
    /// the piece's minutes, with no materials to bring. When it is done the tile is as it was beneath, and half the
    /// piece's materials, rounded down, are left there and carried to a store. A structure bigger than one tile (a
    /// well) cannot be taken down yet.
    /// </summary>
    public (Project? Project, Placement Placement) TakeDown(int x, int y)
    {
        var ground = Ground ?? throw new InvalidOperationException("the colony has no ground to build on");
        if (ground.PieceAt(x, y) is not string piece)
            return (null, new Placement(false, "There is nothing built there to take down."));
        var kind = content.Pieces[piece];
        if (kind.Width > 1 || kind.Height > 1)
            return (null, new Placement(false, Article(kind.Name, capital: true) + " cannot be taken down yet."));
        if (projects.Values.Any(p => p.X == x && p.Y == y))
            return (null, new Placement(false, "It is already being taken down."));
        var (cx, cy) = Ground.Centre(x, y);
        var stock = Stocks.Open(kind.Name + " at (" + x + ", " + y + ") (taking down)", Holder.Construction, cx, cy);
        var project = new Project(colony.Ids.Next<BuildingId>(), piece, x, y, stock.Id) { Removing = true };
        projects[project.Id.Value] = project;
        return (project, new Placement(true));
    }

    /// <summary>What taking a piece down gives back: half of each material, rounded down (library Review 33).</summary>
    public static IReadOnlyDictionary<string, int> Recovered(PieceKind kind) =>
        kind.Materials.Where(m => m.Value / 2 > 0).ToDictionary(m => m.Key, m => m.Value / 2);

    /// <summary>Pauses or resumes a project. Paused, nothing more is brought to it and its building is put down
    /// (keeping its progress); goods already on their way are still delivered.</summary>
    public void Pause(BuildingId id, bool paused)
    {
        var project = projects[id.Value];
        project.Paused = paused;
        if (paused && BuildJob(project) is Job job && job.Worker is ColonistId w)
            Reopen(job, workers[w.Value]);
    }

    /// <summary>Cancels a project: its building job is dropped (letting its materials go), hauls not yet carrying
    /// are dropped, hauls carrying are taken to a store instead, and what was delivered is carried back.</summary>
    public void CancelBuild(BuildingId id)
    {
        var project = projects[id.Value];
        project.Cancelled = true;
        project.Paused = false;
        foreach (var job in jobs.Values.Where(j => (j.Kind == JobKind.Build && j.Site == project.Id)
                                                   || (j.Kind == JobKind.Haul && j.To == project.Stock)).ToList())
            Cancel(job.Id);
    }

    Job? BuildJob(Project project) => jobs.Values.FirstOrDefault(j => j.Kind == JobKind.Build && j.Site == project.Id);

    /// <summary>A project's minute: asks for what it lacks, or makes its building job; a cancelled one carries
    /// its materials back, and is gone once they are.</summary>
    void Supply(Project project)
    {
        (project.Blocked, project.NextStep) = (null, null);
        var kind = content.Pieces[project.Piece];
        var (cx, cy) = (Stocks[project.Stock].X, Stocks[project.Stock].Y);
        if (project.Cancelled)
        {
            foreach (var (good, _) in Stocks[project.Stock].Goods.ToList())
            {
                int free = Stocks.Free(project.Stock, good);
                if (free == 0)
                    continue;
                var store = NearestStore(good, cx, cy);
                if (store == null)
                {
                    (project.Blocked, project.NextStep) = ("no store takes " + Lower(good) + " back", "Let a store take " + Lower(good) + ".");
                    continue;
                }
                NewHaul(project.Stock, store.Id, good, Math.Min(free, CarryUnits(good)), "take " + Lower(good) + " back to a store");
            }
            if (Stocks[project.Stock].Goods.Count == 0 && !jobs.Values.Any(j => j.From == project.Stock || j.To == project.Stock))
            {
                projects.Remove(project.Id.Value);
                Stocks.Close(project.Stock);
            }
            return;
        }
        if (project.Paused)
        {
            (project.Blocked, project.NextStep) = ("it is paused", "Resume it.");
            return;
        }
        if (BuildJob(project) != null)
            return;
        // Nobody can get to it (walled in): nothing is brought until there is a way.
        if (workers.Count > 0 && workers.Values.All(w => double.IsPositiveInfinity(Far(w.X, w.Y, cx, cy))))
        {
            (project.Blocked, project.NextStep) = ("no colonist can reach it", "Open a way to it: leave a gap or put a door in the wall.");
            return;
        }
        if (project.Removing || kind.Materials.All(m => Stocks.Free(project.Stock, m.Key) >= m.Value))
        {
            var claims = project.Removing ? new List<ReservationId>()
                : kind.Materials.Select(m => Stocks.Reserve(project.Stock, m.Key, m.Value, "build " + Article(kind.Name)).Reservation!.Id).ToList();
            var job = new Job
            {
                Id = colony.Ids.Next<TaskId>(), Kind = JobKind.Build, Site = project.Id, Claims = claims,
                First = colony.Ids.Next<OperationId>(), Second = colony.Ids.Next<OperationId>(),
                From = project.Stock, To = project.Stock,
                Purpose = (project.Removing ? "take down " : "build ") + Article(kind.Name) + " at (" + project.X + ", " + project.Y + ")",
            };
            jobs[job.Id.Value] = job;
            return;
        }
        foreach (var (good, needed) in kind.Materials)
            if (Bring(project.Stock, cx, cy, good, needed, "bring " + Lower(good) + " to " + Article(kind.Name) + " at (" + project.X + ", " + project.Y + ")") is string why)
                (project.Blocked, project.NextStep) = (why, "Bring " + Lower(good) + " to a store, or let some free.");
    }

    string Lower(string good) => content.Goods[good].Name.ToLowerInvariant();

    /// <summary>The builder's last minute: the materials are used up and the piece stands.</summary>
    void Complete(Job job, long now)
    {
        var project = projects[job.Site!.Value.Value];
        var kind = content.Pieces[project.Piece];
        if (project.Removing)
        {
            // Taken down: the tile is bare again, and what can be used again is left there to be carried away.
            Ground!.RemovePiece(project.X, project.Y);
            var back = Recovered(kind);
            if (back.Count > 0)
                Stocks.Make(job.First, now, Array.Empty<ReservationId>(), project.Stock, back);
            project.Cancelled = true;                          // from here on it only carries its materials away
            if (back.Count == 0)
            {
                projects.Remove(project.Id.Value);
                Stocks.Close(project.Stock);
            }
            return;
        }
        Stocks.Make(job.First, now, job.Claims, project.Stock, new Dictionary<string, int>());
        job.Claims.Clear();
        foreach (var (tx, ty) in Footprint(kind, project.X, project.Y))
            Ground!.AddPiece(project.Piece, kind.Blocks, tx, ty, kind.Encloses, kind.Speed);
        projects.Remove(project.Id.Value);
        Stocks.Close(project.Stock);
        // A well gives water from its access tile, every morning.
        if (kind.WaterPerDay > 0 && kind.Access is var (dx, dy))
        {
            var (wx, wy) = Ground.Centre(project.X + dx, project.Y + dy);
            AddSpring(wx, wy, kind.Name, kind.WaterPerDay);
        }
    }

    /// <summary>Why a project is waiting (FP-04, FP-06): for the player, a next step and a trace; null if it is
    /// being worked or does not exist.</summary>
    public Explanation? WhyBuild(BuildingId id)
    {
        if (!projects.TryGetValue(id.Value, out var project))
            return null;
        string name = (project.Removing ? "Taking down " + Article(content.Pieces[project.Piece].Name) : Article(content.Pieces[project.Piece].Name, capital: true))
                      + " at (" + project.X + ", " + project.Y + ")";
        string trace = "project " + id.Value + " (" + project.Piece + " at " + project.X + "," + project.Y + ") at tick "
                       + colony.Now.Tick.ToString(CultureInfo.InvariantCulture) + ": ";
        if (!project.Paused && !project.Cancelled && BuildJob(project) is Job job)
        {
            var why = Why(job.Id);
            return why == null ? null : why with { Trace = trace + why.Trace };
        }
        if (project.Blocked != null)
            return new Explanation(name + " is waiting: " + project.Blocked + ".", project.NextStep ?? "",
                                   trace + (project.Cancelled ? "cancelled; " : "") + (project.Paused ? "paused; " : "")
                                   + string.Join(", ", content.Pieces[project.Piece].Materials.Select(m => m.Key + " " + Stocks[project.Stock].Count(m.Key) + "/" + m.Value)));
        return new Explanation(name + " is waiting for its materials to be carried there.", "Wait: they are on their way.",
                               trace + "hauls " + jobs.Values.Count(j => j.To == project.Stock));
    }

    internal void Put(Project project) => projects[project.Id.Value] = project;
}
