// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// A person who works (step 13b: no needs yet; those come in step 14). They stand somewhere, carry goods in their
/// arms (an inventory of their own, so whatever they carry always has a keeper: them), and do one job at a time.
/// </summary>
public sealed class Worker(ColonistId id, string name, double x, double y, InventoryId arms)
{
    public ColonistId Id { get; } = id;
    public string Name { get; } = name;
    public double X { get; internal set; } = x;
    public double Y { get; internal set; } = y;
    public InventoryId Arms { get; } = arms;
    /// <summary>The job they are doing, if any.</summary>
    public TaskId? Job { get; internal set; }
    /// <summary>Stopped by the player (interrupted): they do nothing until resumed.</summary>
    public bool Stopped { get; internal set; }

    /// <summary>Their needs (step 14a), each from 0 (none) to 100 (the limit), by name: "hunger", "thirst",
    /// "fatigue".</summary>
    public SortedDictionary<string, NeedState> Needs { get; } = new(StringComparer.Ordinal)
    {
        ["fatigue"] = new(), ["hunger"] = new(), ["thirst"] = new(),
    };
    /// <summary>Their schedule template's id.</summary>
    public string Schedule { get; internal set; } = "";
    /// <summary>Asleep (in bed, on a bedroll or on the ground).</summary>
    public bool Asleep { get; internal set; }
    /// <summary>Asleep on a bedroll (sleep there eases fatigue faster).</summary>
    public bool OnBedroll { get; internal set; }
    /// <summary>The need they collapsed from ("hunger", "thirst" or "fatigue"), or null.</summary>
    public string? Collapsed { get; internal set; }
    /// <summary>What they are doing for themselves (eating, drinking, going to sleep), or null.</summary>
    public Personal? Personal { get; internal set; }
    /// <summary>A need they could not see to, and why (for the player), or null.</summary>
    public string? Lacking { get; internal set; }

    /// <summary>The kinds of work they are not allowed to do (the player's policy, step 14b): "haul", "care", or a
    /// workshop's id. Empty: they may do any work.</summary>
    public SortedSet<string> Forbidden { get; } = new(StringComparer.Ordinal);
}

/// <summary>One need of one colonist: its level, and how many minutes it has been at the limit.</summary>
public sealed class NeedState
{
    public double Level { get; internal set; }
    public int MinutesAtLimit { get; internal set; }
}

public enum PersonalKind
{
    Eat,
    Drink,
    Sleep,
}

/// <summary>Something a colonist does for themselves: walk to food, water or their bed, and eat, drink or lie down.</summary>
public sealed class Personal
{
    public PersonalKind Kind { get; init; }
    /// <summary>Where they are going.</summary>
    public double X { get; init; }
    public double Y { get; init; }
    /// <summary>The ration or water they claimed, and the operation that uses it up.</summary>
    public ReservationId? Claim { get; init; }
    public OperationId Operation { get; init; }
    /// <summary>For sleeping: whether they are going to their bedroll.</summary>
    public bool Bedroll { get; init; }
    /// <summary>Minutes left eating or drinking; null while still walking.</summary>
    public int? Left { get; internal set; }
}

/// <summary>A bedroll laid out at a place: a sleeping place, and whose it is.</summary>
public sealed record Bedroll(BuildingId Id, double X, double Y, ColonistId? Owner);

/// <summary>
/// A workshop where it stands: what it has at hand (`Input`), what it has made (`Output`), and its order: a recipe
/// and how many more times to work it.
/// </summary>
public sealed class Site(BuildingId id, string workshop, InventoryId input, InventoryId output, double x, double y)
{
    public BuildingId Id { get; } = id;
    public string Workshop { get; } = workshop;
    public InventoryId Input { get; } = input;
    public InventoryId Output { get; } = output;
    public double X { get; } = x;
    public double Y { get; } = y;
    /// <summary>The recipe ordered, or null: no order.</summary>
    public string? Recipe { get; internal set; }
    /// <summary>How many more times to work the recipe.</summary>
    public int Batches { get; internal set; }
    /// <summary>Why the workshop is waiting, in words for the player (null: it is not).</summary>
    public string? Blocked { get; internal set; }
}

public enum JobKind
{
    /// <summary>Carry goods from one inventory to another.</summary>
    Haul,
    /// <summary>Work a recipe at a workshop.</summary>
    Craft,
    /// <summary>Build a construction piece (step 15a).</summary>
    Build,
}

/// <summary>Where a job is.</summary>
public enum Step
{
    /// <summary>Waiting for someone to take it.</summary>
    Open,
    /// <summary>Walking to where the goods are (or to the workshop).</summary>
    Going,
    /// <summary>Picking the goods up.</summary>
    Loading,
    /// <summary>Carrying them to where they go.</summary>
    Carrying,
    /// <summary>Putting them down.</summary>
    Unloading,
    /// <summary>Working the recipe, or building.</summary>
    Working,
}

/// <summary>
/// A job: a haul (carry `Amount` of `Good` from `From` to `To`) or a craft (work `Recipe` at `Site`). Its claims
/// hold the goods it will take, so no other job can take them; its operations are numbered when it is made, so
/// that each of its changes to the goods can only ever happen once.
/// </summary>
public sealed class Job
{
    public TaskId Id { get; init; }
    public JobKind Kind { get; init; }
    public string Good { get; init; } = "";
    public int Amount { get; init; }
    public InventoryId From { get; init; }
    public InventoryId To { get; internal set; }
    /// <summary>For a craft: the workshop and the recipe. For a build: the project (in `Site`).</summary>
    public BuildingId? Site { get; init; }
    public string? Recipe { get; init; }
    /// <summary>The claims it holds: on the goods to haul, or on a recipe's inputs.</summary>
    public List<ReservationId> Claims { get; init; } = new();
    /// <summary>Its operations: a haul's pick-up and put-down, a craft's work.</summary>
    public OperationId First { get; init; }
    public OperationId Second { get; init; }
    public ColonistId? Worker { get; internal set; }
    public Step Step { get; internal set; }
    /// <summary>Minutes left in the step (loading, unloading, working).</summary>
    public int Left { get; internal set; }
    /// <summary>What it is for, in words.</summary>
    public string Purpose { get; init; } = "";
}

/// <summary>
/// The colony's work (the design library's jobs and tasks, hauling and workshop replenishment; step 13b), one tick
/// (minute) at a time, always in the same order, so the same colony always works out the same:
///
/// 1. **Workers act**, in the order of their numbers: walk 70 m a minute towards where their job is; load (a
///    minute), carry, unload (a minute); or work a recipe (the quern: 180 minutes). Picking up moves the claimed
///    goods into the worker's arms; putting down moves them on. Each is one numbered operation of the ledger.
/// 2. **Workshops ask** (the library's "demand-driven replenishment"): a workshop with an order and not enough at
///    hand for its next batch asks for the rest, counting what is already on its way; the goods are claimed in the
///    nearest store that has them free, no more than one person can carry. What a workshop has made is carried to
///    the nearest store that takes it. With the inputs at hand, the recipe is worked.
/// 3. **Workers take jobs**: each open job, oldest first, goes to the nearest free worker.
///
/// The player can **stop** a worker (and resume them) or **cancel** a job. Goods are never left without a keeper:
/// goods not yet picked up stay claimed in their store (or are let go, if the job is cancelled); goods being
/// carried stay in the carrier's arms: a stopped carrier keeps them until resumed, and a cancelled haul takes them
/// to the nearest store instead. An interrupted craft keeps its progress (since step 14b); a cancelled one is gone.
/// </summary>
public sealed partial class Work
{
    readonly Colony colony;
    readonly Content content;
    readonly SortedDictionary<long, Worker> workers = new();
    readonly SortedDictionary<long, Site> sites = new();
    readonly SortedDictionary<long, Job> jobs = new();
    readonly SortedDictionary<long, Bedroll> bedrolls = new();
    readonly SortedSet<long> springs = new();

    public Work(Colony colony, Content content)
    {
        this.colony = colony;
        this.content = content;
    }

    public IReadOnlyCollection<Worker> Workers => workers.Values;
    public IReadOnlyCollection<Site> Sites => sites.Values;
    public IReadOnlyCollection<Job> Jobs => jobs.Values;
    public Worker Worker(ColonistId id) => workers[id.Value];
    public Site Site(BuildingId id) => sites[id.Value];
    public Job? JobOf(TaskId id) => jobs.GetValueOrDefault(id.Value);

    Stocks Stocks => colony.Stocks;

    /// <summary>A new worker at (`x`, `y`), with empty arms.</summary>
    public Worker AddWorker(string name, double x, double y)
    {
        OnOpenGround(x, y);
        var arms = Stocks.Open(name + "'s arms", Holder.Carrier, x, y);
        var worker = new Worker(colony.Ids.Next<ColonistId>(), name, x, y, arms.Id) { Schedule = content.DefaultSchedule };
        workers[worker.Id.Value] = worker;
        return worker;
    }

    /// <summary>A new workshop of kind `workshop` at (`x`, `y`).</summary>
    public Site AddSite(string workshop, double x, double y)
    {
        if (!content.Workshops.TryGetValue(workshop, out var kind))
            throw new ArgumentException("no workshop \"" + workshop + "\" in the content", nameof(workshop));
        OnOpenGround(x, y);
        var input = Stocks.Open(kind.Name + " (at hand)", Holder.WorkshopInput, x, y);
        var output = Stocks.Open(kind.Name + " (made)", Holder.WorkshopOutput, x, y);
        var site = new Site(colony.Ids.Next<BuildingId>(), workshop, input.Id, output.Id, x, y);
        sites[site.Id.Value] = site;
        return site;
    }

    /// <summary>With a ground, a place must be on it, on a tile nothing blocks.</summary>
    void OnOpenGround(double x, double y)
    {
        var (tx, ty) = Ground.TileOf(x, y);
        if (colony.Ground is Ground g && !g.Open(tx, ty))
            throw new ArgumentException("(" + x + ", " + y + ") is not on open ground");
    }

    /// <summary>Orders `site` to work `recipe` `batches` more times (0: no order).</summary>
    public void Order(BuildingId site, string recipe, int batches)
    {
        var here = sites[site.Value];
        if (!content.Recipes.TryGetValue(recipe, out var r) || r.Workshop != here.Workshop)
            throw new ArgumentException("a " + here.Workshop + " cannot work \"" + recipe + "\"", nameof(recipe));
        if (batches < 0)
            throw new ArgumentOutOfRangeException(nameof(batches), "an order cannot be below 0");
        here.Recipe = batches == 0 ? null : recipe;
        here.Batches = batches;
    }

    // ---- The player's controls ------------------------------------------------------------------------

    /// <summary>Stops `worker` (an interruption). A job not yet carrying goods goes back to be taken by someone
    /// else; goods being carried stay in their arms, with the job, until they are resumed.</summary>
    public void Stop(ColonistId id)
    {
        var worker = workers[id.Value];
        worker.Stopped = true;
        if (worker.Job is TaskId t && jobs[t.Value] is var job && job.Step is not (Step.Carrying or Step.Unloading))
            Reopen(job, worker);
    }

    /// <summary>Lets a stopped worker go on.</summary>
    public void Resume(ColonistId id) => workers[id.Value].Stopped = false;

    /// <summary>
    /// Cancels a job. Goods not picked up are let go where they are; goods being carried are taken to the nearest
    /// store instead (the job stays, with a new destination, until they are put down).
    /// </summary>
    public void Cancel(TaskId id)
    {
        if (!jobs.TryGetValue(id.Value, out var job))
            return;
        if (job.Kind == JobKind.Haul && job.Step is Step.Carrying or Step.Unloading)
        {
            var worker = workers[job.Worker!.Value.Value];
            job.To = NearestStore(job.Good, worker.X, worker.Y, except: job.To)?.Id
                     ?? NearestStore(job.Good, worker.X, worker.Y)!.Id;
            job.Step = Step.Carrying;
            return;
        }
        foreach (var claim in job.Claims)
            Stocks.Release(claim);
        if (job.Worker is ColonistId w)
            workers[w.Value].Job = null;
        jobs.Remove(id.Value);
    }

    /// <summary>A job given back: it waits for someone to take it. A grinding or a building keeps its progress
    /// (the minutes still to work), so whoever takes it carries on where it was left; a haul starts its loading
    /// again.</summary>
    void Reopen(Job job, Worker worker)
    {
        worker.Job = null;
        job.Worker = null;
        job.Step = Step.Open;
        if (job.Kind == JobKind.Haul)
            job.Left = 0;
    }

    // ---- One minute ---------------------------------------------------------------------------------

    /// <summary>One tick: needs grow and are seen to (Life.cs), workers act, the spring fills in the morning,
    /// workshops ask, construction projects ask (Build.cs), the collapsed are cared for, workers take jobs.</summary>
    public void Tick()
    {
        foreach (var worker in workers.Values)
            if (Live(worker) && !worker.Stopped && worker.Job is TaskId t)
                Act(worker, jobs[t.Value]);
        if (colony.Now.MinuteOfDay == Autosave.MorningMinute)
            foreach (long spring in springs)
                Fill(new InventoryId(spring));
        foreach (var site in sites.Values)
            Ask(site);
        foreach (var project in projects.Values.ToList())
            Supply(project);
        Tidy();
        Care();
        Assign();
    }

    void Act(Worker worker, Job job)
    {
        long now = colony.Now.Tick;
        switch (job.Step)
        {
            case Step.Going:
                var (gx, gy) = Spot(job);
                // Nobody can get there any more (a wall was built in the way): the job goes back.
                if (double.IsPositiveInfinity(Far(worker.X, worker.Y, gx, gy)))
                {
                    Reopen(job, worker);
                    break;
                }
                // Someone already standing there starts at once; anyone else spends this minute walking. A builder
                // works from beside the tile.
                bool near = job.Kind == JobKind.Build;
                bool there = Arrived(worker, gx, gy, near);
                if (Walk(worker, gx, gy, near))
                {
                    job.Step = job.Kind == JobKind.Haul ? Step.Loading : Step.Working;
                    job.Left = job.Kind switch
                    {
                        JobKind.Haul => content.PickupMinutes,
                        _ when job.Left > 0 => job.Left,
                        JobKind.Craft => content.Recipes[job.Recipe!].WorkMinutes,
                        _ => content.Pieces[projects[job.Site!.Value.Value].Piece].WorkMinutes,
                    };
                    if (there || job.Left == 0)
                        Act(worker, job);
                }
                break;
            case Step.Loading:
                if (--job.Left > 0)
                    break;
                // The claimed goods move into the worker's arms: one operation, done once.
                Stocks.Move(job.First, now, job.Claims[0], worker.Arms);
                job.Claims.Clear();
                job.Step = Step.Carrying;
                break;
            case Step.Carrying:
                var (tx, ty) = Place(job.To);
                if (Walk(worker, tx, ty))
                {
                    job.Step = Step.Unloading;
                    job.Left = content.DropMinutes;
                    if (job.Left == 0)
                        Act(worker, job);
                }
                break;
            case Step.Unloading:
                if (--job.Left > 0)
                    break;
                var (claim, _) = Stocks.Reserve(worker.Arms, job.Good, job.Amount, job.Purpose);
                Stocks.Move(job.Second, now, claim!.Id, job.To);
                Finish(worker, job);
                break;
            case Step.Working:
                if (--job.Left > 0)
                    break;
                if (job.Kind == JobKind.Build)
                {
                    Complete(job, now);
                    Finish(worker, job);
                    break;
                }
                var site = sites[job.Site!.Value.Value];
                var recipe = content.Recipes[job.Recipe!];
                Stocks.Make(job.First, now, job.Claims, site.Output, recipe.Outputs);
                job.Claims.Clear();
                if (site.Recipe == recipe.Id && site.Batches > 0 && --site.Batches == 0)
                    site.Recipe = null;
                Finish(worker, job);
                break;
        }
        Stocks[worker.Arms].X = worker.X;
        Stocks[worker.Arms].Y = worker.Y;
    }

    void Finish(Worker worker, Job job)
    {
        worker.Job = null;
        jobs.Remove(job.Id.Value);
    }

    /// <summary>
    /// A minute of walking towards (x, y): true once there. Without a ground (as in the tests of steps 13 and 14)
    /// a colonist walks straight. On a ground they walk from tile to tile on the way the ground finds, through the
    /// middle of each tile, and in the last tile straight to the spot. `near`: arrived as soon as they stand on a
    /// tile beside the spot's tile (or on it), as a builder does.
    /// </summary>
    bool Walk(Worker worker, double x, double y, bool near = false)
    {
        double left = content.WalkMetresPerMinute;
        while (true)
        {
            if (near && Arrived(worker, x, y, near))
                return true;
            double px = x, py = y;
            if (Ground is Ground ground)
            {
                var (tx, ty) = Ground.TileOf(worker.X, worker.Y);
                var (gx, gy) = Ground.TileOf(x, y);
                if ((tx, ty) != (gx, gy))
                {
                    if (ground.Next(tx, ty, gx, gy) is not var (nx, ny))
                        return false;                                  // no way there: they stay where they are
                    (px, py) = Ground.Centre(nx, ny);
                }
            }
            double dx = px - worker.X, dy = py - worker.Y, far = Math.Sqrt(dx * dx + dy * dy);
            if (far > left)
            {
                (worker.X, worker.Y) = (worker.X + dx * left / far, worker.Y + dy * left / far);
                return false;
            }
            (worker.X, worker.Y) = (px, py);
            left -= far;
            if (px == x && py == y)
                return true;
        }
    }

    /// <summary>Whether a worker is at (x, y); with `near` on a ground, on its tile or a tile beside it.</summary>
    bool Arrived(Worker worker, double x, double y, bool near)
    {
        if (!near || Ground == null)
            return worker.X == x && worker.Y == y;
        var (tx, ty) = Ground.TileOf(worker.X, worker.Y);
        var (gx, gy) = Ground.TileOf(x, y);
        return Math.Abs(tx - gx) <= 1 && Math.Abs(ty - gy) <= 1;
    }

    (double X, double Y) Place(InventoryId id) => (Stocks[id].X, Stocks[id].Y);

    /// <summary>Where a job is done: a craft at its workshop, a build at its tile, a haul where the goods are.</summary>
    (double X, double Y) Spot(Job job) => job.Kind switch
    {
        JobKind.Craft => (sites[job.Site!.Value.Value].X, sites[job.Site!.Value.Value].Y),
        JobKind.Build => Ground.Centre(projects[job.Site!.Value.Value].X, projects[job.Site!.Value.Value].Y),
        _ => Place(job.From),
    };

    static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>How far it is to walk from (ax, ay) to (bx, by), in metres: straight without a ground; on a
    /// ground, the length of the way between their tiles, or infinity if there is none.</summary>
    double Far(double ax, double ay, double bx, double by)
    {
        if (Ground is not Ground ground)
            return Distance(ax, ay, bx, by);
        var (tx, ty) = Ground.TileOf(ax, ay);
        var (gx, gy) = Ground.TileOf(bx, by);
        return ground.Metres(tx, ty, gx, gy);
    }

    /// <summary>The nearest of `places` by walking from (x, y) that can be reached (the lower number on a tie).</summary>
    Inventory? Nearest(IEnumerable<Inventory> places, double x, double y) =>
        places.Select(i => (Place: i, Far: Far(x, y, i.X, i.Y))).Where(p => !double.IsPositiveInfinity(p.Far))
              .OrderBy(p => p.Far).ThenBy(p => p.Place.Id.Value).Select(p => p.Place).FirstOrDefault();

    /// <summary>The nearest store taking `good` (the lower number on a tie), or null.</summary>
    Inventory? NearestStore(string good, double x, double y, InventoryId? except = null) =>
        Nearest(Stocks.Inventories.Where(i => i.Takes(good) && i.Id != except), x, y);

    /// <summary>Units of `good` one person can carry.</summary>
    int CarryUnits(string good) => Math.Max(1, (int)Math.Floor(content.CarryKg / content.Goods[good].MassKg));

    /// <summary>A stockpile (step 15b, library Construction §13.1): a store laid over a block of tiles, `width` by
    /// `height` from tile (x, y), all of them open ground. It takes every good until told otherwise
    /// (`Accepts`).</summary>
    public Inventory AddStockpile(string name, int x, int y, int width, int height)
    {
        var ground = Ground ?? throw new InvalidOperationException("a stockpile needs ground to lie on");
        if (width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "a stockpile covers at least one tile");
        var area = new Area(x, y, width, height);
        foreach (var (tx, ty) in area.Tiles)
            if (!ground.Open(tx, ty))
                throw new ArgumentException("tile (" + tx + ", " + ty + ") is not open ground");
        var pile = Stocks.Open(name, Holder.Store, x + width / 2.0, y + height / 2.0);
        pile.Area = area;
        return pile;
    }

    /// <summary>
    /// Empties stores marked `Emptying` (the founders' carts, once there is a stockpile): when nobody has other work
    /// waiting, the goods are carried to the nearest store that takes them, a load at a time (one waiting haul per
    /// good and store, so goods are not claimed long before anyone carries them, and stay free to eat).
    /// </summary>
    void Tidy()
    {
        if (jobs.Values.Any(j => j.Step == Step.Open))
            return;
        foreach (var store in Stocks.Inventories.Where(i => i.Holder == Holder.Store && i.Emptying).ToList())
            foreach (var (good, _) in store.Goods.ToList())
            {
                int free = Stocks.Free(store.Id, good);
                if (free == 0)
                    continue;
                var to = NearestStore(good, store.X, store.Y);
                if (to == null)
                    continue;
                NewHaul(store.Id, to.Id, good, Math.Min(free, CarryUnits(good)), "move " + content.Goods[good].Name.ToLowerInvariant() + " to " + to.Name);
                return;                                          // one at a time
            }
    }

    // ---- What workshops ask for ---------------------------------------------------------------------------

    void Ask(Site site)
    {
        site.Blocked = null;
        // What it has made goes to a store.
        foreach (var (good, _) in Stocks[site.Output].Goods.ToList())
        {
            int free = Stocks.Free(site.Output, good);
            if (free == 0)
                continue;
            var store = NearestStore(good, site.X, site.Y);
            if (store == null)
            {
                site.Blocked = "no store takes " + good;
                continue;
            }
            NewHaul(site.Output, store.Id, good, Math.Min(free, CarryUnits(good)), "store the " + good);
        }
        if (site.Recipe is not string id)
            return;
        var recipe = content.Recipes[id];
        if (jobs.Values.Any(j => j.Kind == JobKind.Craft && j.Site == site.Id))
            return;                                       // a batch is being worked
        // With everything at hand, the recipe is worked.
        if (recipe.Inputs.All(p => Stocks.Free(site.Input, p.Key) >= p.Value))
        {
            var claims = recipe.Inputs.Select(p => Stocks.Reserve(site.Input, p.Key, p.Value, "work " + recipe.Name).Reservation!.Id).ToList();
            var job = new Job
            {
                Id = colony.Ids.Next<TaskId>(), Kind = JobKind.Craft, Site = site.Id, Recipe = recipe.Id, Claims = claims,
                First = colony.Ids.Next<OperationId>(), Second = colony.Ids.Next<OperationId>(),
                From = site.Input, To = site.Output, Purpose = recipe.Name,
            };
            jobs[job.Id.Value] = job;
            return;
        }
        // Otherwise it asks for what is missing, counting what is already on its way.
        foreach (var (good, needed) in recipe.Inputs)
            if (Bring(site.Input, site.X, site.Y, good, needed, "bring " + good + " to the " + content.Workshops[site.Workshop].Name.ToLowerInvariant()) is string why)
                site.Blocked = why;
    }

    /// <summary>
    /// Asks for goods to be brought to `to` (at x, y) until it has `needed` of `good`, counting what it has free and
    /// what is already on its way: hauls from the nearest stores with some free, each no more than a person can
    /// carry. Returns why not all could be asked for (for the player), or null.
    /// </summary>
    string? Bring(InventoryId to, double x, double y, string good, int needed, string purpose)
    {
        int coming = jobs.Values.Where(j => j.Kind == JobKind.Haul && j.To == to && j.Good == good).Sum(j => j.Amount);
        int missing = needed - Stocks.Free(to, good) - coming;
        while (missing > 0)
        {
            var stores = Stocks.Inventories.Where(i => i.Holder == Holder.Store && Stocks.Free(i.Id, good) > 0).ToList();
            var source = Nearest(stores, x, y);
            if (source == null)
                return "waiting for " + good + ": " + missing + " more needed, and "
                       + (stores.Count > 0 ? "no store with some free can be reached from here" : "no store has any free" + Held(good));
            int amount = Math.Min(Math.Min(missing, Stocks.Free(source.Id, good)), CarryUnits(good));
            NewHaul(source.Id, to, good, amount, purpose);
            missing -= amount;
        }
        return null;
    }

    /// <summary>Why goods that are there are not free (for the player): claimed, or kept as a reserve.</summary>
    string Held(string good)
    {
        int held = Stocks.Inventories.Where(i => i.Holder == Holder.Store).Sum(i => i.Count(good));
        if (held == 0)
            return "";
        var stores = Stocks.Inventories.Where(i => i.Holder == Holder.Store).ToList();
        int kept = stores.Sum(i => Math.Min(i.Count(good), i.Kept.GetValueOrDefault(good)));
        int claimed = stores.Sum(i => Stocks.Reserved(i.Id, good));
        var parts = new List<string>();
        if (kept > 0)
            parts.Add(kept + " kept as a reserve");
        if (claimed > 0)
            parts.Add(claimed + " claimed for other work");
        return " (" + held + " in the stores: " + string.Join(", ", parts) + ")";
    }

    void NewHaul(InventoryId from, InventoryId to, string good, int amount, string purpose)
    {
        var (claim, outcome) = Stocks.Reserve(from, good, amount, purpose);
        if (claim == null)
            throw new InvalidOperationException(outcome.Why);     // never: the caller checked what is free
        var job = new Job
        {
            Id = colony.Ids.Next<TaskId>(), Kind = JobKind.Haul, Good = good, Amount = amount, From = from, To = to,
            Claims = new List<ReservationId> { claim.Id }, First = colony.Ids.Next<OperationId>(),
            Second = colony.Ids.Next<OperationId>(), Purpose = purpose,
        };
        jobs[job.Id.Value] = job;
    }

    void Assign()
    {
        foreach (var job in jobs.Values.Where(j => j.Step == Step.Open))
        {
            // A paused project's building waits.
            if (job.Kind == JobKind.Build && projects[job.Site!.Value.Value].Paused)
                continue;
            var (x, y) = Spot(job);
            // The nearest by walking of those free, allowed, and able to get there.
            var worker = workers.Values.Where(w => Available(w) && !w.Forbidden.Contains(KindOf(job)))
                                .Select(w => (Worker: w, Far: Far(w.X, w.Y, x, y))).Where(p => !double.IsPositiveInfinity(p.Far))
                                .OrderBy(p => p.Far).ThenBy(p => p.Worker.Id.Value).Select(p => p.Worker).FirstOrDefault();
            if (worker == null)
                continue;
            worker.Job = job.Id;
            job.Worker = worker.Id;
            job.Step = Step.Going;
        }
    }

    // ---- For loading a save ------------------------------------------------------------------------------

    internal void Put(Worker worker) => workers[worker.Id.Value] = worker;
    internal void Put(Site site) => sites[site.Id.Value] = site;
    internal void Put(Job job) => jobs[job.Id.Value] = job;
    internal void Put(Bedroll bedroll) => bedrolls[bedroll.Id.Value] = bedroll;
    internal void PutSpring(InventoryId spring) => springs.Add(spring.Value);
}
