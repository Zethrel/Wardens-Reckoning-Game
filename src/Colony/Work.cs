// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// A person who works (step 13b: no needs yet; those come in step 14). They stand somewhere, carry goods in their
/// arms (an inventory of their own, so whatever they carry always has a keeper: them), and do one job at a time.
/// </summary>
/// <summary>
/// A colonist's state in one skill (steps 23a and 23b): how good they are now (`Proficiency`, 0 to 100), the best they
/// have ever been (`Peak`), how fast they learn it (`Aptitude`, 1 the usual; hidden from the player until seen in
/// practice), how long they have practised it in all (`Practised`, seconds) and when they last did (`LastPractised`,
/// a tick).
/// </summary>
public sealed class SkillState(double proficiency, double aptitude, long lastPractised)
{
    double proficiency = proficiency;

    /// <summary>How good they are now. Raising it raises the best ever reached with it.</summary>
    public double Proficiency
    {
        get => proficiency;
        set
        {
            proficiency = value;
            Peak = Math.Max(Peak, value);
        }
    }

    /// <summary>The best they have ever been (step 23b): decay never takes them below a share of it, and they learn
    /// faster again up to it.</summary>
    public double Peak { get; internal set; } = proficiency;

    public double Aptitude { get; internal set; } = aptitude;
    public long Practised { get; internal set; }
    public long LastPractised { get; internal set; } = lastPractised;

    /// <summary>Losing proficiency (decay, step 23b): the best ever reached stays.</summary>
    internal void Lose(double points) => proficiency = Math.Max(0, proficiency - points);
}

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
    /// <summary>Every how many ticks they decide (step 21c): drawn once, when they come, from colony.json
    /// think_real_seconds (2 to 7 real seconds: 48 to 168 ticks). Some are quick, some slow.</summary>
    public int ThinkEvery { get; internal set; } = 1;
    /// <summary>Their decision deadline: the tick of their next think, when they next decide what to do. Something
    /// urgent (an order, a need turning critical) brings it to the next tick.</summary>
    public long NextThink { get; internal set; }
    /// <summary>Their action deadline (step 24, library Review 41): the tick their timed step ends (loading,
    /// unloading, working, eating) at their present pace, or the tick after they arrive somewhere; null while
    /// nothing is due (walking, standing, asleep). It wakes them to get on with their work, never to choose new
    /// work.</summary>
    public long? ActionDue { get; internal set; }
    /// <summary>The tick they were last processed (a think or a wake-up): the time since is what their work and their
    /// eating have gone on for.</summary>
    public long Thought { get; internal set; }
    /// <summary>Where they are walking to (step 21a), or null: standing. `Near`: far enough once on a
    /// tile beside it. Set when they are processed by whatever they are doing; walked every tick.</summary>
    public (double X, double Y, bool Near)? Heading { get; internal set; }
    /// <summary>An illness caught (step 19b): it may not have shown yet (Work.Ill says whether it has).</summary>
    public Illness? Illness { get; internal set; }
    /// <summary>Work done but not yet a whole second's worth (Health.cs, Effort): a skilled colonist's extra and
    /// an ill or unskilled one's shortfall, carried over from one think to the next.</summary>
    public double Carry { get; internal set; }

    /// <summary>How good they are at each skill (step 23a) and how they learn it (step 23b), by skill id.</summary>
    public SortedDictionary<string, SkillState> Skills { get; } = new(StringComparer.Ordinal);

    double legs = 1;
    /// <summary>How well their legs carry them (step 15c), as a share of a healthy walk: 1 (100%) until body
    /// functions and injuries come; an injured leg will lower it. Above 0 and at most 1.</summary>
    public double Legs
    {
        get => legs;
        set => legs = value > 0 && value <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Legs), "legs carry from above 0 to 1 (100%)");
    }

    /// <summary>Their needs (step 14a), each from 0 (none) to 100 (the limit), by name: "hunger", "thirst",
    /// "fatigue", "hygiene" (step 19a).</summary>
    public SortedDictionary<string, NeedState> Needs { get; } = new(StringComparer.Ordinal)
    {
        ["fatigue"] = new(), ["hunger"] = new(), ["hygiene"] = new(), ["thirst"] = new(),
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
    /// <summary>Washing (step 19a): at a wash basin, or on the bank of fresh water.</summary>
    Wash,
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
    /// <summary>Ticks left eating, drinking or washing; null while still walking.</summary>
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
    /// <summary>Boil water (step 19b): fetch the fuel, carry it to the water marked "boil first", boil a batch
    /// there, carry the boiled water to a store.</summary>
    Boil,
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
    /// <summary>Carrying the fuel to the water to be boiled (a boiling, step 19b).</summary>
    Fetching,
    /// <summary>Working the recipe, building, or boiling.</summary>
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
    /// <summary>A boiling's third operation: the boiling itself (fuel and water in, boiled water out).</summary>
    public OperationId Third { get; init; }
    /// <summary>A boiling's water: the store marked "boil first" whose water is boiled.</summary>
    public InventoryId? Boiling { get; init; }
    public ColonistId? Worker { get; internal set; }
    public Step Step { get; internal set; }
    /// <summary>Work left in the step (loading, unloading, working), in standard seconds: what the usual pace does in a
    /// tick.</summary>
    public int Left { get; internal set; }
    /// <summary>What it is for, in words.</summary>
    public string Purpose { get; init; } = "";
}

/// <summary>
/// The colony's work (the design library's jobs and tasks, hauling and workshop replenishment; step 13b), one tick
/// (a game second) at a time, always in the same order, so the same colony always works out the same:
///
/// 1. **Workers act**, in the order of their numbers: walk towards where their job is (1.2 m a real second at ×1);
///    load (5 real seconds), carry, unload; or work a recipe (the quern: 180 minutes). Picking up moves the claimed
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
        // The usual proficiency in every skill, until something says otherwise (founding draws its own, step 23a).
        foreach (string skill in content.Skills.Keys)
            worker.Skills[skill] = new SkillState(content.StandardProficiency, 1, colony.Now.Tick);
        // Their rhythm (step 21c), on the colony's thinking dice: every so many seconds, and the first think within
        // that many, so that colonists who come together do not all think in the same second.
        worker.ThinkEvery = colony.DiceFor("thinking").Int(content.ThinkFrom, content.ThinkTo);
        worker.Thought = colony.Now.Tick;
        worker.NextThink = colony.Now.Tick + 1 + worker.Id.Value % worker.ThinkEvery;
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
        Ask(here);                                        // it asks at once (step 21c)
    }

    // ---- The player's controls ------------------------------------------------------------------------

    /// <summary>Stops `worker` (an interruption). A job not yet carrying goods goes back to be taken by someone
    /// else; goods being carried stay in their arms, with the job, until they are resumed.</summary>
    public void Stop(ColonistId id)
    {
        var worker = workers[id.Value];
        worker.Stopped = true;
        if (worker.Personal == null)
            worker.Heading = null;                              // an order counts at once: they stand still
        Urge(worker);
        if (worker.Job is TaskId t && jobs[t.Value] is var job && !InHand(job))
            Reopen(job, worker);
    }

    /// <summary>Lets a stopped worker go on.</summary>
    public void Resume(ColonistId id)
    {
        workers[id.Value].Stopped = false;
        Urge(workers[id.Value]);
    }

    /// <summary>Something urgent for a colonist (an order now; a fire or a raid later): they think the next
    /// second, whatever their rhythm (step 21c).</summary>
    internal void Urge(Worker worker) => worker.NextThink = Math.Min(worker.NextThink, colony.Now.Tick + 1);

    /// <summary>
    /// Cancels a job. Goods not picked up are let go where they are; goods being carried are taken to the nearest
    /// store instead (the job stays, with a new destination, until they are put down).
    /// </summary>
    public void Cancel(TaskId id)
    {
        if (!jobs.TryGetValue(id.Value, out var job))
            return;
        if (job.Kind == JobKind.Boil && InHand(job))
            return;                                       // a boiling under way is finished, and the water stored
        if (job.Kind == JobKind.Haul && job.Step is Step.Carrying or Step.Unloading)
        {
            var worker = workers[job.Worker!.Value.Value];
            job.To = NearestStore(job.Good, worker.X, worker.Y, except: job.To)?.Id
                     ?? NearestStore(job.Good, worker.X, worker.Y)!.Id;
            job.Step = Step.Carrying;
            worker.ActionDue = null;                        // an unloading cut short: its deadline goes with it
            Urge(worker);                                   // they set off for the new store at once
            return;
        }
        foreach (var claim in job.Claims)
            Stocks.Release(claim);
        if (job.Worker is ColonistId w)
        {
            workers[w.Value].Job = null;
            workers[w.Value].ActionDue = null;            // the deadline went with the job (Review 41)
            Urge(workers[w.Value]);                         // and they think again at once
        }
        jobs.Remove(id.Value);
    }

    /// <summary>A job given back: it waits for someone to take it. A grinding or a building keeps its progress
    /// (the minutes still to work), so whoever takes it carries on where it was left; a haul starts its loading
    /// again.</summary>
    /// <summary>Whether a job has goods in the worker's arms: a haul carrying, or a boiling once the fuel is picked
    /// up. Such a job stays with them (stopped, or seeing to a need) until the goods are put down.</summary>
    internal static bool InHand(Job job) =>
        job.Step is Step.Carrying or Step.Unloading || job.Kind == JobKind.Boil && job.Step is Step.Fetching or Step.Working;

    void Reopen(Job job, Worker worker)
    {
        worker.Job = null;
        worker.ActionDue = null;                          // the step's deadline goes with it (Review 41)
        job.Worker = null;
        job.Step = Step.Open;
        if (job.Kind == JobKind.Haul)
            job.Left = 0;
    }

    // ---- One second, one minute ----------------------------------------------------------------------

    /// <summary>
    /// One tick, a game second (library Reviews 39 and 41). Everyone walking takes a tick's steps towards where they
    /// are heading; whoever arrives is woken on the next tick. On the whole minutes the colony's own work is done
    /// (`Minute`): needs grow, water, building, fouling and the rest, which still run once a minute (their declared
    /// interval).
    ///
    /// Then the colonists due are **processed**, in the order of their numbers, each once (step 24, Review 41):
    /// those whose decision deadline has come (`NextThink`: their think, every 2 to 7 real seconds) and those whose
    /// action deadline has come (`ActionDue`: a timed step ending, or an arrival). Both kinds get on with what they
    /// are doing; only a think also decides: sees to needs, and, if free, takes a job. Last, each processed colonist's
    /// action deadline is worked out again from what they are doing now (`Schedule`).
    /// </summary>
    public void Tick()
    {
        stirred.Clear();
        long now = colony.Now.Tick;
        foreach (var worker in workers.Values)
            if (worker.Heading != null)
                Stride(worker);
        if (colony.Now.OnTheMinute)
            Minute();
        // (`ActionDue <= now` is false while it is null: C# compares a missing number with nothing.)
        var due = workers.Values.Where(w => w.NextThink <= now || w.ActionDue <= now).ToList();
        if (due.Count > 0)
        {
            foreach (var worker in due)
                Process(worker, decides: worker.NextThink <= now);
            // A job finished (goods brought, a piece built): the workplaces it took from or brought to ask for what
            // comes next at once, so the one who finished need not wait for the whole minute.
            if (stirred.Count > 0)
                AsksAfterFinishing();
            // Those thinking who are free for work take jobs, and set off at once. A colonist only woken to get on
            // with their work never chooses new work: that waits for their think (Review 41).
            var deciding = due.Where(w => w.NextThink <= now).ToList();
            foreach (var worker in Assign(deciding))
                Act(worker, jobs[worker.Job!.Value.Value], 0);
            foreach (var worker in deciding)
                worker.NextThink = now + worker.ThinkEvery;
            foreach (var worker in due)
                Schedule(worker);
        }
        // Illnesses over this tick are forgotten only now, after the work done while ill was counted at the ill
        // pace (Health.cs, `Sicken`).
        if (ended.Count > 0)
        {
            foreach (var worker in ended)
                worker.Illness = null;
            ended.Clear();
        }
    }

    /// <summary>
    /// A colonist is processed (step 24, library Review 41): what they were doing has gone on since they were last
    /// processed (their work, their eating), and goes on to its next step if it is done. With `decides` (their
    /// think) they then also see to their needs (`Live`), which may put their job down; without, they were only woken
    /// to get on with it, and decide nothing. Nobody walks on unless what they do now says so (`Walk`).
    /// </summary>
    void Process(Worker worker, bool decides)
    {
        long now = colony.Now.Tick;
        int elapsed = (int)Math.Min(now - worker.Thought, int.MaxValue);
        worker.Thought = now;
        worker.ActionDue = null;
        worker.Heading = null;
        // First the time that has passed: what they were doing goes on, and on to its next step if it is done. So a
        // think that puts the job down (a need) keeps the work done up to now (Review 41).
        if (worker.Collapsed == null && !worker.Asleep)
        {
            if (worker.Personal != null)
                Continue(worker, elapsed);
            else if (!worker.Stopped && worker.Job is TaskId t)
                Act(worker, jobs[t.Value], elapsed);
        }
        // Then, at a think, what to do now: their needs first; a job they keep goes on from where it is.
        if (decides && Live(worker) && !worker.Stopped && worker.Job is TaskId kept)
            Act(worker, jobs[kept.Value], 0);
    }

    /// <summary>
    /// A colonist's action deadline, worked out again after they are processed (step 24, Review 41): the tick their
    /// timed step ends from the work left (loading and unloading, eating, drinking and washing take their ticks;
    /// working goes at their pace, `TicksToFinish`). Walking has none: arriving wakes them (`Stride`). Collapsed,
    /// asleep or stopped: none either; their think sees to that.
    /// </summary>
    void Schedule(Worker w)
    {
        long now = colony.Now.Tick;
        w.ActionDue = null;
        if (w.Collapsed != null || w.Asleep || w.Heading != null)
            return;
        if (w.Personal is Personal p)
        {
            if (p.Left is int left)
                w.ActionDue = now + Math.Max(1, left);
            return;
        }
        if (w.Stopped || w.Job is not TaskId t || !jobs.TryGetValue(t.Value, out var job))
            return;
        w.ActionDue = job.Step switch
        {
            Step.Loading or Step.Unloading => now + Math.Max(1, job.Left),
            Step.Working => now + TicksToFinish(w, job),
            _ => null,
        };
    }

    /// <summary>
    /// How many ticks a colonist needs to finish the work left on a job at their present pace (step 24): the fewest
    /// ticks whose work (`Effort`, with what they carry over) reaches it. Worked out with the same sum `Effort` does,
    /// so that on the tick it says, the work is done: not a tick sooner, nor later.
    /// </summary>
    long TicksToFinish(Worker w, Job job)
    {
        double pace = Pace(w, job);
        if (pace == 1 && w.Carry == 0 || job.Left <= 0)
            return Math.Max(1, job.Left);
        long ticks = Math.Max(1, (long)Math.Ceiling((job.Left - w.Carry) / pace));
        // The division may be a hair off either way; the same sum as `Effort` decides.
        while (Math.Floor(w.Carry + ticks * pace + 1e-9) < job.Left)
            ticks++;
        while (ticks > 1 && Math.Floor(w.Carry + (ticks - 1) * pace + 1e-9) >= job.Left)
            ticks--;
        return ticks;
    }

    // The inventories that jobs finished during this tick took from or brought to (Finish); read and cleared in the
    // same tick, so never saved.
    readonly HashSet<long> stirred = new();

    /// <summary>
    /// The asking after a job is finished: the workshops and building projects it took from or brought to (not every
    /// one: a project far off asks for nothing new because a haul elsewhere was done, and asking all of them each
    /// time cost a search for a way per project and colonist, over a million an hour once walls closed colonists
    /// in), and the stores, barrels and boiling, which ask without searching for ways.
    /// </summary>
    void AsksAfterFinishing()
    {
        foreach (var site in sites.Values)
            if (stirred.Contains(site.Input.Value) || stirred.Contains(site.Output.Value))
                Ask(site);
        foreach (var project in projects.Values.Where(p => stirred.Contains(p.Stock.Value)).ToList())
            Supply(project);
        Tidy();
        ClearStockpiles();
        Fetch();
        BoilJobs();
    }

    /// <summary>
    /// The workplaces ask for what they need (step 13 on): workshops for their inputs and to have what they made
    /// stored, construction projects for their materials and their building, stores being emptied, barrels and
    /// basins for water, marked water for boiling. Each looks only at how things stand now, so asking again changes
    /// nothing that is already asked for. Done on every whole minute (and in part when a job is finished: AsksAfterFinishing).
    /// Also done when the colony is founded; and each of the player's orders makes the workplace it changes ask
    /// at once (a batch ordered, a building planned, paused or cancelled, a stockpile, a mark), so the colonists
    /// can take the work at their next think rather than on the whole minute. (Never when a save is loaded: a
    /// loaded colony goes on exactly as if it had never been saved.)
    /// </summary>
    internal void Asks()
    {
        foreach (var site in sites.Values)
            Ask(site);
        foreach (var project in projects.Values.ToList())
            Supply(project);
        Tidy();
        ClearStockpiles();
        Fetch();
        BoilJobs();
    }

    /// <summary>One minute of the colony's own work: needs grow (and may fell someone), the water sources fill and
    /// freeze in the morning and rivers top up each hour (Water.cs), workshops ask, construction projects ask
    /// (Build.cs), stores are emptied and emptied stockpiles removed (Placing.cs), barrels and basins are filled
    /// (Water.cs), fouling and illness (Sanitation.cs, Health.cs), the collapsed are cared for.</summary>
    void Minute()
    {
        foreach (var worker in workers.Values)
            Grow(worker);
        if (colony.Now.MinuteOfDay == Autosave.MorningMinute)
        {
            Foul();
            Morning();
            Forget();
        }
        if (colony.Now.MinuteOfDay % Calendar.MinutesPerHour == 0)
            TopUp();
        Asks();
        Mix();
        Notice();
        Sicken();
        Care();
    }

    /// <summary>
    /// A colonist's job goes on: `seconds` have passed since they were last processed (0: they go straight on from
    /// the step before, in the same processing). Loading, unloading and working take their ticks, from the tick they
    /// began. Each step moves on to the next in one place, guarded by the step it is in (`switch (job.Step)`), so
    /// each happens once, whatever woke the colonist (step 24, Review 41).
    /// </summary>
    void Act(Worker worker, Job job, int seconds)
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
                // Someone standing there starts at once; anyone else walks on. A builder works from beside the tile.
                bool near = job.Kind == JobKind.Build && content.Pieces[projects[job.Site!.Value.Value].Piece].Access == null;
                if (Walk(worker, gx, gy, near))
                {
                    job.Step = job.Kind is JobKind.Haul or JobKind.Boil ? Step.Loading : Step.Working;
                    job.Left = job.Kind switch
                    {
                        JobKind.Haul or JobKind.Boil => content.PickupTicks,
                        _ when job.Left > 0 => job.Left,
                        JobKind.Craft => content.Recipes[job.Recipe!].WorkMinutes * Calendar.TicksPerMinute,
                        _ => Minutes(projects[job.Site!.Value.Value]) * Calendar.TicksPerMinute,
                    };
                    Act(worker, job, 0);
                }
                break;
            case Step.Loading:
                if ((job.Left -= seconds) > 0)
                    break;
                // The claimed goods move into the worker's arms: one operation, done once. A boiling picks up its
                // fuel, and carries it to the water (its water stays claimed where it is).
                Stocks.Move(job.First, now, job.Claims[0], worker.Arms);
                job.Claims.RemoveAt(0);
                job.Step = job.Kind == JobKind.Boil ? Step.Fetching : Step.Carrying;
                Act(worker, job, 0);                      // and they set off with it at once (step 24)
                break;
            case Step.Fetching:
                var (bx, by) = Place(job.Boiling!.Value);
                if (Walk(worker, bx, by))
                {
                    job.Step = Step.Working;
                    job.Left = content.Sanitation.BoilMinutes * Calendar.TicksPerMinute;
                    Act(worker, job, 0);
                }
                break;
            case Step.Carrying:
                var (tx, ty) = Place(job.To);
                if (Walk(worker, tx, ty))
                {
                    job.Step = Step.Unloading;
                    job.Left = content.DropTicks;
                    Act(worker, job, 0);
                }
                break;
            case Step.Unloading:
                if ((job.Left -= seconds) > 0)
                    break;
                var (claim, _) = Stocks.Reserve(worker.Arms, job.Good, job.Amount, job.Purpose);
                Stocks.Move(job.Second, now, claim!.Id, job.To);
                Finish(worker, job);
                break;
            case Step.Working:
                // How much work the seconds are worth: by the colonist's skill at it (step 23a) and their health (step
                // 19b). The seconds teach them too (step 23b).
                int worth = Effort(worker, job, seconds);
                Practise(worker, job, seconds);
                if ((job.Left -= worth) > 0)
                    break;
                if (job.Kind == JobKind.Boil)
                {
                    Boiled(worker, job, now);
                    break;
                }
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
        stirred.Add(job.From.Value);                      // where it took from and brought to ask again, before
        stirred.Add(job.To.Value);                        // anyone takes a job
    }

    /// <summary>
    /// Walking towards (x, y), asked whenever a colonist is processed by whatever they are doing: true if they are
    /// there (and may get on with it); if not, they head there, and walk tick by tick
    /// (`Stride`). `near`: there as soon as they stand on a tile beside the spot's tile (or on it), as a builder
    /// does.
    /// </summary>
    bool Walk(Worker worker, double x, double y, bool near = false)
    {
        if (Arrived(worker, x, y, near))
        {
            worker.Heading = null;
            return true;
        }
        worker.Heading = (x, y, near);
        return false;
    }

    /// <summary>
    /// A tick of walking towards where they are heading (step 21a). Without a ground (as in the tests of steps
    /// 13 and 14) a colonist walks straight. On a ground they walk from tile to tile on the way the ground finds,
    /// through the middle of each tile, and in the last tile straight to the spot.
    ///
    /// The tick is spent stretch by stretch (step 15c): each stretch goes at the walking speed (colony.json, on
    /// a road, step 24: 0.05 m a tick) times the speed of the tile it leads onto (grass 0.8) times how well the colonist's legs carry them.
    /// Once there they stand, and get on with what they came for on the next tick (`Arrive`).
    /// </summary>
    void Stride(Worker worker)
    {
        var (x, y, near) = worker.Heading!.Value;
        double left = 1;                                  // of the tick, still to walk
        while (true)
        {
            if (near && Arrived(worker, x, y, near))
            {
                Arrive(worker);
                break;
            }
            double px = x, py = y, speed = 1;
            if (Ground is Ground ground)
            {
                var (tx, ty) = Ground.TileOf(worker.X, worker.Y);
                var (gx, gy) = Ground.TileOf(x, y);
                if ((tx, ty) != (gx, gy))
                {
                    if (ground.Next(tx, ty, gx, gy) is not var (nx, ny))
                    {
                        worker.Heading = null;                         // no way there: they stay where they are
                        break;
                    }
                    (px, py) = Ground.Centre(nx, ny);
                    speed = ground.Speed(nx, ny);
                }
                else
                    speed = ground.Speed(gx, gy);
            }
            double metresPerTick = content.WalkMetresPerTick * speed * worker.Legs;
            double dx = px - worker.X, dy = py - worker.Y, far = Math.Sqrt(dx * dx + dy * dy);
            double takes = far / metresPerTick;
            // (A stretch that ends within a billionth of a tick after it is walked whole: shares of a tick do not
            // always add up to exactly one, and a walk of exactly two ticks should take two.)
            if (takes > left + 1e-9)
            {
                double share = left * metresPerTick / far;
                (worker.X, worker.Y) = (worker.X + dx * share, worker.Y + dy * share);
                break;
            }
            (worker.X, worker.Y) = (px, py);
            left = Math.Max(0, left - takes);
            if (px == x && py == y)
            {
                Arrive(worker);
                break;
            }
        }
        Stocks[worker.Arms].X = worker.X;
        Stocks[worker.Arms].Y = worker.Y;
    }

    /// <summary>They are there: they stop, and are woken on the next tick to get on with what they came for (step 24,
    /// Review 41), whenever their think is. An earlier wake-up stays.</summary>
    void Arrive(Worker worker)
    {
        worker.Heading = null;
        long next = colony.Now.Tick + 1;
        if (worker.ActionDue is not long early || early > next)
            worker.ActionDue = next;
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
        JobKind.Build => Place(projects[job.Site!.Value.Value].Stock),
        _ => Place(job.From),
    };

    static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>How far it is to walk from (ax, ay) to (bx, by), in metres: straight without a ground; on a
    /// ground, the way between their tiles counted in metres of road (so the quickest way wins), or infinity if
    /// there is none.</summary>
    double Far(double ax, double ay, double bx, double by)
    {
        if (Ground is not Ground ground)
            return Distance(ax, ay, bx, by);
        var (tx, ty) = Ground.TileOf(ax, ay);
        var (gx, gy) = Ground.TileOf(bx, by);
        // No way at all (walled in, across water): known from the areas, without working out a way (step 22).
        if (!ground.CanReach(tx, ty, gx, gy))
            return double.PositiveInfinity;
        return ground.Metres(tx, ty, gx, gy);
    }

    /// <summary>The nearest of `places` by walking from (x, y) that can be reached (the lower number on a tie).</summary>
    Inventory? Nearest(IEnumerable<Inventory> places, double x, double y) =>
        places.Select(i => (Place: i, Far: Far(x, y, i.X, i.Y))).Where(p => !double.IsPositiveInfinity(p.Far))
              .OrderBy(p => p.Far).ThenBy(p => p.Place.Id.Value).Select(p => p.Place).FirstOrDefault();

    /// <summary>The nearest store taking `good` with room for it (the lower number on a tie), or null.</summary>
    Inventory? NearestStore(string good, double x, double y, InventoryId? except = null) =>
        Nearest(Stocks.Inventories.Where(i => i.Takes(good) && i.Id != except && RoomIn(i) > 0), x, y);

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
                NewHaul(store.Id, to.Id, good, Math.Min(Math.Min(free, CarryUnits(good)), RoomIn(to)), "move " + content.Goods[good].Name.ToLowerInvariant() + " to " + to.Name);
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
            NewHaul(site.Output, store.Id, good, Math.Min(Math.Min(free, CarryUnits(good)), RoomIn(store)), "store the " + good);
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

    /// <summary>Open jobs go to those of `thinking` who are free for work (step 21c: a colonist takes work when
    /// they think): each job, in turn, to the nearest by walking of them, allowed and able to get there. Returns
    /// who took one.</summary>
    List<Worker> Assign(List<Worker> thinking)
    {
        var took = new List<Worker>();
        var free = thinking.Where(Available).ToList();
        if (free.Count == 0)
            return took;
        foreach (var job in jobs.Values.Where(j => j.Step == Step.Open))
        {
            // A paused project's building waits.
            if (job.Kind == JobKind.Build && projects[job.Site!.Value.Value].Paused)
                continue;
            var (x, y) = Spot(job);
            // The nearest by walking of those free, allowed, and able to get there.
            var worker = free.Where(w => Available(w) && !w.Forbidden.Contains(KindOf(job)))
                                .Select(w => (Worker: w, Far: Far(w.X, w.Y, x, y))).Where(p => !double.IsPositiveInfinity(p.Far))
                                .OrderBy(p => p.Far).ThenBy(p => p.Worker.Id.Value).Select(p => p.Worker).FirstOrDefault();
            if (worker == null)
                continue;
            worker.Job = job.Id;
            job.Worker = worker.Id;
            job.Step = Step.Going;
            took.Add(worker);
        }
        return took;
    }

    // ---- For loading a save ------------------------------------------------------------------------------

    internal void Put(Worker worker) => workers[worker.Id.Value] = worker;
    internal void Put(Site site) => sites[site.Id.Value] = site;
    internal void Put(Job job) => jobs[job.Id.Value] = job;
    internal void Put(Bedroll bedroll) => bedrolls[bedroll.Id.Value] = bedroll;
}
