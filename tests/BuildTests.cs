// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 15a: the ground and construction (FP-04: a basic construction order, with its reservations, hauling, work
/// and finished piece consistent all the way, and clear reasons when something it needs is missing).
/// </summary>
public class BuildTests
{
    static readonly Content Real = Content.Load(Path.Combine(AppContext.BaseDirectory, "content"));
    static readonly Moment Nine = new(Calendar.TickOf(301, 1, 1, 9, 0));

    /// <summary>A camp on the content's test ground: the founders' carts with timber, a spring, a quern, colonists
    /// with bedrolls.</summary>
    sealed class Camp
    {
        public readonly ColonyState Colony;
        public Work Work => Colony.Work!;
        public Stocks Stocks => Colony.Stocks;
        public Ground Ground => Colony.Ground!;
        public readonly Inventory Carts;
        public readonly Site Quern;
        public readonly List<Worker> People = new();

        public Camp(int timber = 40, int people = 2, Ground? ground = null)
        {
            Colony = new ColonyState(42, Nine, Real);
            Colony.Lay(ground ?? Ground.Test(Real));
            Carts = Stocks.Open("The founders' carts", Holder.Store, 2.5, 2.5);
            if (timber > 0)
                Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "timber", timber);
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "ration", 30);
            Work.AddSpring(3.5, 9.5);
            Quern = Work.AddSite("quern", 9.5, 2.5);
            foreach (string name in new[] { "Ada", "Bram", "Cael" }.Take(people))
            {
                var w = Work.AddWorker(name, 3.5, 3.5);
                Work.AddBedroll(1.5 + 2 * People.Count, 10.5, w.Id);
                People.Add(w);
            }
        }

        public Project Plan(string piece, int x, int y)
        {
            var (project, placement) = Work.Plan(piece, x, y);
            Assert.True(placement.Ok, placement.Why);
            return project!;
        }

        /// <summary>Runs on a minute at a time until `done` (failing after `most` minutes, so a test never hangs).</summary>
        public void Until(Func<bool> done, int most = 600)
        {
            for (int i = 0; !done(); i++)
            {
                Assert.True(i < most, "it never happened");
                Run(1);
            }
        }

        public Job? Build(Project p) => Work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Build && j.Site == p.Id);

        /// <summary>Runs on, checking every minute that no timber is lost or made from nothing.</summary>
        public void Run(long ticks, Action? each = null) => Colony.Run(ticks, _ =>
        {
            Assert.Equal(Stocks.Accounted("timber"), Stocks.Total("timber"));
            each?.Invoke();
        });
    }

    // ---- The ground and its ways -----------------------------------------------------------------------------

    [Fact]
    public void The_way_goes_around_what_blocks_and_never_cuts_a_corner()
    {
        var ground = new Ground(10, 10);
        Assert.Equal(5, ground.Metres(0, 5, 5, 5));                            // straight across: 5 m
        // On the slant, in all four directions; and the shortest mix: 3 on the slant and 2 across.
        foreach (var (ax, ay, bx, by) in new[] { (0, 0, 5, 5), (5, 5, 0, 0), (0, 5, 5, 0), (5, 0, 0, 5) })
            Assert.Equal(5 * Math.Sqrt(2), ground.Metres(ax, ay, bx, by), 9);
        Assert.Equal(3 * Math.Sqrt(2) + 2, ground.Metres(0, 0, 5, 3), 9);
        Assert.Equal(3 * Math.Sqrt(2) + 2, ground.Metres(5, 3, 0, 0), 9);
        for (int y = 0; y < 9; y++)
            ground.AddPiece("wall", true, 5, y);                               // a wall with a gap at the bottom
        // Around the end of the wall, not through it: down 9, across 2 below the wall, up 9 (no slant past its end).
        Assert.Equal(20, ground.Metres(4, 0, 6, 0), 9);
        Assert.Equal(ground.Metres(4, 0, 6, 0), ground.Metres(6, 0, 4, 0), 9);  // the same either way
        // A slanting step past the wall's end would cut its corner: from (4, 8) the way to (6, 9) goes down first.
        Assert.Equal((4, 9), ground.Next(4, 8, 6, 9));
        ground.AddPiece("wall", true, 5, 9);
        Assert.True(double.IsPositiveInfinity(ground.Metres(4, 0, 6, 0)));     // closed: no way at all
        Assert.Null(ground.Next(4, 0, 6, 0));
    }

    [Fact]
    public void The_way_is_the_shortest_there_is()
    {
        // A ground with rocks at random places (seeded, so always the same), checked against a slow, simple
        // reckoning: keep shortening every tile's distance through its neighbours until nothing changes.
        const int size = 16;
        var random = new Random(15);
        var ground = new Ground(size, size);
        for (int i = 0; i < 70; i++)
        {
            int x = random.Next(size), y = random.Next(size);
            if (ground.ObstacleAt(x, y) == null && (x, y) != (0, 0))
                ground.AddObstacle("rock", x, y);
        }
        bool Open(int x, int y) => ground.Open(x, y);
        var far = new double[size, size];
        foreach (var (x, y) in Enumerable.Range(0, size * size).Select(i => (i % size, i / size)))
            far[x, y] = double.PositiveInfinity;
        far[0, 0] = 0;
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if ((dx, dy) == (0, 0) || !Open(x, y) || !Open(x + dx, y + dy)
                                || (dx != 0 && dy != 0 && !(Open(x + dx, y) && Open(x, y + dy))))
                                continue;
                            double through = far[x + dx, y + dy] + (dx != 0 && dy != 0 ? Math.Sqrt(2) : 1);
                            if (through < far[x, y] - 1e-12)
                                (far[x, y], changed) = (through, true);
                        }
        }
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                if (Open(x, y))
                    Assert.Equal(far[x, y], ground.Metres(x, y, 0, 0), 9);
    }

    [Fact]
    public void Someone_on_a_tile_that_becomes_blocked_can_still_step_off_it()
    {
        var ground = new Ground(5, 5);
        ground.AddPiece("wall", true, 2, 2);
        Assert.Equal(1, ground.Metres(2, 2, 2, 3));
        Assert.True(double.IsPositiveInfinity(ground.Metres(0, 0, 9, 9)));     // off the ground: no way
    }

    [Fact]
    public void A_colonist_walks_the_way_around_a_wall()
    {
        var ground = new Ground(48, 48);
        for (int y = 0; y < 40; y++)
            ground.AddPiece("wall", true, 20, y);
        var camp = new Camp(ground: ground);
        var ada = camp.People[0];
        var bram = camp.People[1];
        camp.Work.Stop(bram.Id);
        var store = camp.Stocks.Open("Far store", Holder.Store, 30.5, 3.5);
        store.Accepts.Add("ration");
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), camp.Colony.Now.Tick, store.Id, "ration", 1);
        camp.Stocks.Reserve(camp.Carts.Id, "ration", 30, "kept away for the test");
        // Straight it is 27 m, under a minute's walk; around the wall's end at y = 40 it is about 90 m.
        Assert.True(camp.Ground.Metres(3, 3, 30, 3) > 80);
        ada.Needs["hunger"].Level = 90;
        camp.Run(1);
        Assert.Equal(PersonalKind.Eat, ada.Personal?.Kind);
        Assert.Null(ada.Personal!.Left);                                   // still walking after a minute
        camp.Run(1);
        Assert.NotNull(ada.Personal?.Left);                                // there in the second, eating
        Assert.Equal((30, 3), Ground.TileOf(ada.X, ada.Y));
    }

    [Fact]
    public void A_ground_is_laid_once_and_before_anything_stands_on_it()
    {
        var camp = new Camp();
        Assert.Throws<InvalidOperationException>(() => camp.Colony.Lay(new Ground(5, 5)));
        var late = new ColonyState(1, Nine, Real);
        late.Stocks.Open("A store", Holder.Store, 1, 1);
        Assert.Throws<InvalidOperationException>(() => late.Lay(new Ground(5, 5)));
        Assert.Throws<ArgumentException>(() => camp.Work.AddWorker("Dag", 30.5, 8.5));   // a rock is there
        Assert.Throws<ArgumentException>(() => camp.Work.AddSite("quern", 60, 1));       // off the ground
    }

    // ---- FP-04: a construction order, from plan to piece -------------------------------------------------------

    [Fact]
    public void FP04_a_wall_is_planned_supplied_built_and_stands()
    {
        var camp = new Camp();
        var wall = camp.Plan("wall", 20, 20);
        var site = wall.Stock;
        bool sawHaul = false, sawClaim = false, sawWork = false;
        camp.Run(120, () =>
        {
            var p = camp.Work.Project(wall.Id);
            if (p == null)
                return;
            // On its way: a haul of 2 timber from the carts, claimed there, to the building site.
            if (camp.Work.Jobs.Any(j => j.Kind == JobKind.Haul && j.To == site && j.Amount == 2))
                sawHaul = true;
            // Delivered: the build job claims both units at the site, and nobody else can take them.
            if (camp.Work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Build) is Job b)
            {
                sawClaim = true;
                Assert.Equal(2, camp.Stocks.Reserved(site, "timber"));
                Assert.Equal(0, camp.Stocks.Free(site, "timber"));
                if (b.Step == Step.Working)
                {
                    sawWork = true;
                    var builder = camp.Work.Worker(b.Worker!.Value);
                    var (tx, ty) = Ground.TileOf(builder.X, builder.Y);
                    Assert.True(Math.Abs(tx - 20) <= 1 && Math.Abs(ty - 20) <= 1, "the builder works from beside the tile");
                }
            }
        });
        Assert.True(sawHaul && sawClaim && sawWork);
        Assert.Equal("wall", camp.Ground.PieceAt(20, 20));
        Assert.False(camp.Ground.Open(20, 20));
        Assert.Null(camp.Work.Project(wall.Id));
        Assert.DoesNotContain(camp.Stocks.Inventories, i => i.Id == site);      // the building site's pile is gone
        Assert.Empty(camp.Stocks.Reservations.Where(r => r.Good == "timber"));
        Assert.Equal(38, camp.Stocks.Total("timber"));
        var used = Assert.Single(camp.Stocks.Ledger, e => e.Change == Change.Used && e.Good == "timber");
        Assert.Equal((2, site), (used.Amount, used.From!.Value));
    }

    [Fact]
    public void FP04_the_minutes_are_the_pieces_own()
    {
        var camp = new Camp(people: 1);
        camp.Plan("floor", 4, 3);                                     // next to Ada, so she walks almost nothing
        camp.Until(() => camp.Work.Jobs.Any(j => j.Kind == JobKind.Build && j.Step == Step.Working));
        long first = camp.Colony.Now.Tick;                            // the first minute of building is done
        camp.Until(() => camp.Ground.PieceAt(4, 3) != null);
        Assert.Equal("floor", camp.Ground.PieceAt(4, 3));
        Assert.Equal(Real.Pieces["floor"].WorkMinutes, camp.Colony.Now.Tick - first + 1);
        Assert.True(camp.Ground.Open(4, 3));                          // a floor does not block
    }

    [Fact]
    public void FP04_a_piece_can_be_planned_before_its_materials_exist_and_says_what_it_lacks()
    {
        var camp = new Camp(timber: 0);
        var wall = camp.Plan("wall", 20, 20);
        camp.Run(5);
        var why = camp.Work.WhyBuild(wall.Id)!;
        Assert.Equal("A timber wall at (20, 20) is waiting: waiting for timber: 2 more needed, and no store has any free.", why.Player);
        Assert.Equal("Bring timber to a store, or let some free.", why.NextStep);
        Assert.Contains("timber 0/2", why.Trace);
        Assert.DoesNotContain("project", why.Player);
        // Timber kept as a reserve is named as the reason.
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), camp.Colony.Now.Tick, camp.Carts.Id, "timber", 2);
        camp.Carts.Keep("timber", 2);
        camp.Run(1);
        Assert.Contains("2 kept as a reserve", camp.Work.WhyBuild(wall.Id)!.Player);
        camp.Carts.Keep("timber", 0);
        camp.Run(120);
        Assert.Equal("wall", camp.Ground.PieceAt(20, 20));
        Assert.Null(camp.Work.WhyBuild(wall.Id));
    }

    [Fact]
    public void FP04_a_bad_place_is_refused_with_its_reason()
    {
        var camp = new Camp();
        string? Why(string piece, int x, int y) => camp.Work.Check(piece, x, y).Why;
        Assert.Equal("It is outside the ground (48 by 48 tiles).", Why("wall", 48, 3));
        Assert.Equal("A rock is in the way.", Why("wall", 30, 8));
        Assert.Equal("A tree is in the way.", Why("floor", 6, 6));
        Assert.Equal("The hand quern stands there: a timber wall would block it.", Why("wall", 9, 2));
        Assert.Equal("A bedroll is laid there: a timber wall would block it.", Why("wall", 1, 10));
        Assert.Equal("A store (The founders' carts) is there: a timber wall would block it.", Why("wall", 2, 2));
        Assert.True(camp.Work.Check("floor", 2, 2).Ok);                          // a floor under the carts is fine
        camp.Plan("door", 20, 20);
        Assert.Equal("A timber door is already planned there.", Why("wall", 20, 20));
        Assert.True(camp.Work.Check("wall", 20, 21).Ok);                         // the tile below is free
        Assert.True(camp.Work.Check("wall", 19, 20).Ok);
        camp.Ground.AddPiece("wall", true, 21, 20);
        Assert.Equal("There is already a timber wall there.", Why("floor", 21, 20));
        var (none, refused) = camp.Work.Plan("wall", 30, 8);
        Assert.Null(none);
        Assert.False(refused.Ok);
        Assert.Throws<ArgumentException>(() => camp.Work.Check("tower", 20, 20));
        Assert.Throws<InvalidOperationException>(() => new ColonyState(1, Nine, Real).Work!.Check("wall", 1, 1));
    }

    /// <summary>A ground with a closed ring of walls from (4, 4) to (8, 8).</summary>
    static Ground Ringed()
    {
        var ground = new Ground(12, 12);
        for (int i = 4; i <= 8; i++)
            foreach (var (x, y) in new[] { (i, 4), (i, 8), (4, i), (8, i) })
                if (ground.PieceAt(x, y) == null)
                    ground.AddPiece("wall", true, x, y);
        return ground;
    }

    [Fact]
    public void FP04_a_place_nobody_can_reach_is_planned_with_a_warning_and_says_why_it_waits()
    {
        var camp = new Camp(ground: Ringed());
        var (inside, placement) = camp.Work.Plan("floor", 6, 6);
        Assert.True(placement.Ok);
        Assert.Equal("No colonist can reach it now; it waits until there is a way.", placement.Warning);
        Assert.Null(camp.Work.Check("floor", 10, 10).Warning);
        camp.Run(30);
        Assert.Null(camp.Ground.PieceAt(6, 6));
        // Nothing is brought to a place nobody can get to: no haul, no claim on the timber.
        Assert.DoesNotContain(camp.Work.Jobs, j => j.To == inside!.Stock);
        Assert.Equal(40, camp.Stocks.Free(camp.Carts.Id, "timber"));
        var why = camp.Work.WhyBuild(inside!.Id)!;
        Assert.Equal("A plank floor at (6, 6) is waiting: no colonist can reach it.", why.Player);
        Assert.Equal("Open a way to it: leave a gap or put a door in the wall.", why.NextStep);
    }

    [Fact]
    public void FP04_a_build_nobody_can_get_to_is_explained()
    {
        // A ring with a gap at (6, 4); the timber is already at the site inside. Both colonists are stopped while
        // the build job is made, then the gap is walled up.
        var ground = new Ground(12, 12);
        for (int i = 4; i <= 8; i++)
            foreach (var (x, y) in new[] { (i, 4), (i, 8), (4, i), (8, i) })
                if (ground.PieceAt(x, y) == null && (x, y) != (6, 4))
                    ground.AddPiece("wall", true, x, y);
        var camp = new Camp(ground: ground);
        var inside = camp.Plan("floor", 6, 6);
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), camp.Colony.Now.Tick, inside.Stock, "timber", 1);
        foreach (var w in camp.People)
            camp.Work.Stop(w.Id);
        camp.Run(1);
        camp.Ground.AddPiece("wall", true, 6, 4);
        foreach (var w in camp.People)
            camp.Work.Resume(w.Id);
        camp.Run(3);
        var job = Assert.Single(camp.Work.Jobs, j => j.Kind == JobKind.Build);
        Assert.Equal(Step.Open, job.Step);
        var why = camp.Work.WhyBuild(inside.Id)!;
        Assert.Equal("Waiting for someone to build: Ada cannot get there; Bram cannot get there.", why.Player);
        Assert.Equal("Open a way: leave a gap or put a door in the wall.", why.NextStep);
        Assert.Contains("no way from", why.Trace);
    }

    [Fact]
    public void FP04_nobody_allowed_to_build_is_explained()
    {
        var camp = new Camp();
        foreach (var w in camp.People)
            camp.Work.Permit(w.Id, Work.BuildWork, false);
        var floor = camp.Plan("floor", 20, 20);
        camp.Run(30);
        Assert.Null(camp.Ground.PieceAt(20, 20));
        var why = camp.Work.WhyBuild(floor.Id)!;
        Assert.Equal("Nobody is allowed to build.", why.Player);
        Assert.Equal("Allow someone to build in their work settings.", why.NextStep);
        Assert.StartsWith("project " + floor.Id.Value, why.Trace);
    }

    [Fact]
    public void FP04_a_paused_build_waits_and_keeps_its_progress()
    {
        var camp = new Camp(people: 1);
        var wall = camp.Plan("wall", 5, 3);
        camp.Until(() => camp.Build(wall)?.Step == Step.Working);
        camp.Run(20);
        int left = camp.Work.Jobs.Single(j => j.Kind == JobKind.Build).Left;
        camp.Work.Pause(wall.Id, true);
        camp.Run(30);
        var job = camp.Work.Jobs.Single(j => j.Kind == JobKind.Build);
        Assert.Equal((Step.Open, left), (job.Step, job.Left));
        Assert.Equal("A timber wall at (5, 3) is waiting: it is paused.", camp.Work.WhyBuild(wall.Id)!.Player);
        Assert.Equal("Resume it.", camp.Work.WhyBuild(wall.Id)!.NextStep);
        camp.Work.Pause(wall.Id, false);
        camp.Until(() => camp.Build(wall)?.Step == Step.Working);
        long again = camp.Colony.Now.Tick;
        camp.Until(() => camp.Ground.PieceAt(5, 3) != null);
        Assert.Equal("wall", camp.Ground.PieceAt(5, 3));
        Assert.Equal(left, camp.Colony.Now.Tick - again + 1);              // it carried on; it did not start again
    }

    [Fact]
    public void FP04_a_project_paused_before_it_is_supplied_takes_nothing()
    {
        var camp = new Camp();
        var wall = camp.Plan("wall", 20, 20);
        camp.Work.Pause(wall.Id, true);
        camp.Run(30);
        Assert.Empty(camp.Work.Jobs);
        Assert.Equal(40, camp.Stocks.Free(camp.Carts.Id, "timber"));
        camp.Work.Pause(wall.Id, false);
        camp.Run(120);
        Assert.Equal("wall", camp.Ground.PieceAt(20, 20));
    }

    [Fact]
    public void FP04_a_cancelled_project_keeps_its_timber_until_a_store_takes_it_back()
    {
        var camp = new Camp();
        var wall = camp.Plan("wall", 20, 20);
        camp.Until(() => camp.Stocks[wall.Stock].Count("timber") == 2);
        camp.Carts.Accepts.Add("ration");                                  // the carts no longer take timber
        camp.Work.CancelBuild(wall.Id);
        camp.Run(30);
        Assert.NotNull(camp.Work.Project(wall.Id));                        // still there, holding the timber
        Assert.Equal(2, camp.Stocks[wall.Stock].Count("timber"));
        Assert.Equal("A timber wall at (20, 20) is waiting: no store takes timber back.", camp.Work.WhyBuild(wall.Id)!.Player);
        Assert.Equal("Let a store take timber.", camp.Work.WhyBuild(wall.Id)!.NextStep);
        camp.Carts.Accepts.Add("timber");
        camp.Run(30);
        Assert.Null(camp.Work.Project(wall.Id));
        Assert.Equal(40, camp.Stocks[camp.Carts.Id].Count("timber"));
    }

    [Fact]
    public void FP04_a_builder_who_comes_from_elsewhere_works_from_beside_the_tile()
    {
        var camp = new Camp(people: 1);
        var wall = camp.Plan("wall", 20, 20);
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), camp.Colony.Now.Tick, wall.Stock, "timber", 2);
        camp.Until(() => camp.Build(wall)?.Step == Step.Working);
        var ada = camp.People[0];
        var (tx, ty) = Ground.TileOf(ada.X, ada.Y);
        Assert.True(Math.Max(Math.Abs(tx - 20), Math.Abs(ty - 20)) == 1, "she works from the next tile, not on it: " + (tx, ty));
    }

    /// <summary>A big ground with a ring of walls from (150, 150) to (154, 154) and a gap at (152, 150): far
    /// enough that walking there takes several minutes.</summary>
    static Ground RingedFar()
    {
        var ground = new Ground(200, 200);
        for (int i = 150; i <= 154; i++)
            foreach (var (x, y) in new[] { (i, 150), (i, 154), (150, i), (154, i) })
                if (ground.PieceAt(x, y) == null && (x, y) != (152, 150))
                    ground.AddPiece("wall", true, x, y);
        return ground;
    }

    [Fact]
    public void A_colonist_whose_way_is_walled_up_gives_the_job_back()
    {
        var camp = new Camp(people: 1, ground: RingedFar());
        var floor = camp.Plan("floor", 152, 152);
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), camp.Colony.Now.Tick, floor.Stock, "timber", 1);
        camp.Run(1);
        var ada = camp.People[0];
        Assert.Equal(Step.Going, camp.Build(floor)!.Step);
        camp.Ground.AddPiece("wall", true, 152, 150);                      // the gap is closed before she gets there
        camp.Run(1);
        Assert.Null(ada.Job);
        Assert.Null(camp.Build(floor)!.Worker);
        Assert.Equal("Waiting for someone to build: Ada cannot get there.", camp.Work.WhyBuild(floor.Id)!.Player);
    }

    [Fact]
    public void A_colonist_whose_way_to_food_is_walled_up_gives_up_and_says_why()
    {
        var camp = new Camp(people: 1, ground: RingedFar());
        var pantry = camp.Stocks.Open("Pantry", Holder.Store, 152.5, 152.5);
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), camp.Colony.Now.Tick, pantry.Id, "ration", 1);
        camp.Stocks.Reserve(camp.Carts.Id, "ration", 30, "kept away for the test");
        var ada = camp.People[0];
        ada.Needs["hunger"].Level = 90;
        camp.Run(1);
        Assert.Equal(PersonalKind.Eat, ada.Personal?.Kind);
        Assert.Equal(1, camp.Stocks.Reserved(pantry.Id, "ration"));
        camp.Ground.AddPiece("wall", true, 152, 150);
        camp.Run(1);
        Assert.Null(ada.Personal);
        Assert.Equal(0, camp.Stocks.Reserved(pantry.Id, "ration"));        // the ration is let go
        Assert.Equal("cannot reach the store", ada.Lacking);
    }

    [Fact]
    public void FP04_cancelling_gives_every_unit_back()
    {
        var camp = new Camp();
        var far = camp.Plan("door", 40, 40);                               // far away: cancelled while carried
        var near = camp.Plan("wall", 5, 3);                                // near: cancelled while being built
        camp.Until(() => camp.Work.Jobs.Any(j => j.Kind == JobKind.Haul && j.To == far.Stock && j.Step == Step.Carrying));
        camp.Work.CancelBuild(far.Id);
        camp.Until(() => camp.Build(near)?.Step == Step.Working);
        camp.Work.CancelBuild(near.Id);
        Assert.True(camp.Work.Project(near.Id)!.Cancelled);
        camp.Run(120);
        Assert.Null(camp.Work.Project(far.Id));
        Assert.Null(camp.Work.Project(near.Id));
        Assert.Null(camp.Ground.PieceAt(5, 3));
        Assert.Null(camp.Ground.PieceAt(40, 40));
        Assert.Equal(40, camp.Stocks.Total("timber"));
        Assert.Equal(40, camp.Stocks[camp.Carts.Id].Count("timber"));     // all back in the carts
        Assert.DoesNotContain(camp.Stocks.Ledger, e => e.Change == Change.Used && e.Good == "timber");
        Assert.Empty(camp.Stocks.Reservations.Where(r => r.Good == "timber"));
        Assert.Empty(camp.Work.Jobs);
    }

    [Fact]
    public void A_colonist_standing_where_a_wall_is_finished_steps_off_it()
    {
        var camp = new Camp(people: 2);
        var (ada, bram) = (camp.People[0], camp.People[1]);
        camp.Work.Stop(bram.Id);
        camp.Plan("wall", 3, 3);                                           // where both of them stand
        camp.Run(120, () =>
        {
            if (camp.Ground.PieceAt(3, 3) == "wall" && !bram.Stopped)
                return;
            if (camp.Ground.PieceAt(3, 3) == "wall")
                camp.Work.Resume(bram.Id);
        });
        Assert.Equal("wall", camp.Ground.PieceAt(3, 3));
        bram.Needs["thirst"].Level = 90;                                   // he must walk to the spring
        camp.Run(10);
        Assert.NotEqual((3, 3), Ground.TileOf(bram.X, bram.Y));
    }

    [Fact]
    public void A_wall_with_a_door_sends_colonists_through_the_door()
    {
        // A wall across the whole ground at x = 20 with a door at y = 30: the only way to the far side.
        var ground = new Ground(48, 48);
        for (int row = 0; row < 48; row++)
            if (row != 30)
                ground.AddPiece("wall", true, 20, row);
        ground.AddPiece("door", false, 20, 30);
        var camp = new Camp(ground: ground, people: 1);
        // The way from the carts to the far side goes down to the door and back up: over 60 m, not 28.
        double way = camp.Ground.Metres(2, 2, 30, 3);
        Assert.True(way > 60, "the way is " + way + " m");
        // Every step of it: from (2, 2), tile by tile, it passes x = 20 only at the door.
        var (x, y) = (2, 2);
        while (camp.Ground.Next(x, y, 30, 3) is var (nx, ny))
        {
            Assert.True(nx != 20 || ny == 30, "through the wall at (" + nx + ", " + ny + ")");
            (x, y) = (nx, ny);
        }
        Assert.Equal((30, 3), (x, y));
        camp.Plan("floor", 30, 3);
        camp.Run(120);
        Assert.Equal("floor", camp.Ground.PieceAt(30, 3));
    }

    // ---- The save ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_save_keeps_the_ground_and_the_projects_and_goes_on_the_same()
    {
        var camp = new Camp();
        camp.Plan("wall", 20, 20);
        camp.Plan("floor", 40, 40);
        var paused = camp.Plan("door", 10, 20);
        camp.Work.Pause(paused.Id, true);
        camp.Ground.AddPiece("wall", true, 21, 21);
        camp.Until(() => camp.Work.Jobs.Any(j => j.Kind == JobKind.Build && j.Step == Step.Working));
        camp.Run(10);
        string text = ColonySave.Text(camp.Colony);
        var loaded = ColonySave.Read(text, Real);
        Assert.Equal(text, ColonySave.Text(loaded));
        Assert.Equal("wall", loaded.Ground!.PieceAt(21, 21));
        Assert.Equal("rock", loaded.Ground.ObstacleAt(30, 8));
        Assert.True(loaded.Work!.Project(paused.Id)!.Paused);
        camp.Run(300);
        loaded.Run(300);
        Assert.Equal(ColonySave.Fingerprint(camp.Colony), ColonySave.Fingerprint(loaded));
        Assert.Equal("wall", loaded.Ground.PieceAt(20, 20));
    }

    public static IEnumerable<object[]> BrokenGrounds()
    {
        var camp = new Camp();
        camp.Plan("wall", 20, 20);
        camp.Run(1);
        string good = ColonySave.Text(camp.Colony);
        object[] Case(Action<JsonObject> change, string reason)
        {
            var save = JsonNode.Parse(good)!.AsObject();
            change(save);
            return new object[] { save.ToJsonString(), reason };
        }
        JsonObject Project(JsonObject s) => s["work"]!["projects"]![0]!.AsObject();
        yield return Case(s => s["ground"]!["width"] = 0, "its ground is 0 by 48 tiles");
        yield return Case(s => s["ground"]!["obstacle_rows"]![0] = "...", "ground.obstacle_rows[0] should be 48 letters");
        yield return Case(s => s["ground"]!["obstacle_rows"]!.AsArray().RemoveAt(0), "ground.obstacle_rows should be 48 rows");
        yield return Case(s => s["ground"]!["obstacle_rows"]![0] = new string('z', 48), "ground.obstacle_rows[0] has 'z', which is not the letter of a kind");
        yield return Case(s => s["ground"]!["obstacle_kinds"] = new JsonArray("rock", "rock"), "ground.obstacle_kinds[1] is not text, or is named twice");
        yield return Case(s => s["ground"]!["rows"]![3] = new string('z', 48), "ground.rows[3] has 'z', which is not the letter of a surface");
        yield return Case(s => s["ground"]!["pieces"] = new JsonArray(new JsonArray("wall", 30, 8)), "ground.pieces[0] is off the ground, or on a tile that already holds something");
        yield return Case(s => s["ground"]!["pieces"] = new JsonArray(new JsonArray("wall", 48, 8)), "ground.pieces[0] is off the ground");
        yield return Case(s => s["ground"]!["pieces"] = new JsonArray(new JsonArray("wall", 1, 1), new JsonArray("door", 1, 1)), "ground.pieces[1] is off the ground, or on a tile that already holds something");
        yield return Case(s => s["ground"]!["pieces"] = new JsonArray(new JsonArray("tower", 1, 1)), "ground.pieces names \"tower\", which is not in the content");
        yield return Case(s => s.Remove("ground"), "its \"ground\" is missing");
        yield return Case(s => Project(s)["piece"] = "tower", "work.projects[0].piece \"tower\" is not in the content");
        yield return Case(s => { Project(s)["x"] = 30; Project(s)["y"] = 8; }, "work.projects[0] is planned where it cannot be (30, 8)");
        yield return Case(s => Project(s)["stock"] = s["work"]!["workers"]![0]!["arms"]!.DeepClone(), "work.projects[0].stock is not a building site's inventory");
        yield return Case(s => s["work"]!["projects"]!.AsArray().Add(Project(s).DeepClone()), "work.projects[1].id");
        yield return Case(s => s["work"]!["jobs"]!.AsArray().First(j => j!["kind"]!.ToString() == "Haul")!["site"] = 999, "does not exist");
    }

    [Theory]
    [MemberData(nameof(BrokenGrounds))]
    public void A_save_with_a_broken_ground_or_project_is_refused(string text, string reason)
    {
        var e = Assert.Throws<ColonySaveException>(() => ColonySave.Read(text, Real));
        Assert.Contains(reason, e.Message);
    }
}
