// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 28a: felling trees (library Review 44: D1 A marked singly or by area, D2 C 4 timber and 30 minutes, the
/// timber left where the tree stood, no stump, D3 A the Forestry skill, D4 C no regrowth). The checks V1 to V3 and
/// V8 of the review: a marked tree is felled by someone allowed to, the tile is open ground, 4 timber lie there and
/// are carried to a store; unmarking cancels; an interrupted felling keeps its progress; the same save goes on the same.
/// </summary>
public class FellingTests
{
    static readonly Content Real = TestContent.EverySecond;              // thinking every second: see TestContent
    static readonly Moment Nine = new(Calendar.TickOf(301, 1, 1, 9, 0));

    /// <summary>The content's test ground (trees at (6, 6), (40, 20), (41, 22) and (22, 42)), the founders' carts
    /// (a store for everything) and two colonists by them.</summary>
    sealed class Camp
    {
        public readonly ColonyState Colony;
        public Work Work => Colony.Work!;
        public Stocks Stocks => Colony.Stocks;
        public Ground Ground => Colony.Ground!;
        public readonly Inventory Carts;
        public readonly List<Worker> People = new();

        public Camp(int people = 2)
        {
            Colony = new ColonyState(42, Nine, Real);
            Colony.Lay(Ground.Test(Real));
            Carts = Stocks.Open("The founders' carts", Holder.Store, 2.5, 2.5);
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "ration", 30);
            Work.AddSpring(3.5, 9.5);
            foreach (string name in new[] { "Ada", "Bram", "Cael" }.Take(people))
            {
                var w = Work.AddWorker(name, 3.5, 3.5);
                Work.AddBedroll(1.5 + 2 * People.Count, 10.5, w.Id);
                People.Add(w);
            }
        }

        public Project Fell(int x, int y)
        {
            var (project, placement) = Work.Fell(x, y);
            Assert.True(placement.Ok, placement.Why);
            return project!;
        }

        public Job? Felling(Project p) => Work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Build && j.Site == p.Id);

        /// <summary>Runs on a minute at a time until `done` (failing after `most` minutes), checking every minute that
        /// no timber is lost or made from nothing.</summary>
        public void Until(Func<bool> done, int most = 600)
        {
            for (int i = 0; !done(); i++)
            {
                Assert.True(i < most, "it never happened");
                Colony.RunMinutes(1, _ => Assert.Equal(Stocks.Accounted("timber"), Stocks.Total("timber")));
            }
        }
    }

    [Fact]
    public void V1_a_marked_tree_is_felled_its_tile_opens_and_its_timber_is_carried_to_a_store()
    {
        var camp = new Camp();
        Assert.Equal("tree", camp.Ground.ObstacleAt(6, 6));
        var tree = camp.Fell(6, 6);
        var site = tree.Stock;
        bool sawWork = false;
        camp.Colony.Run(40 * 60, _ =>
        {
            if (camp.Felling(tree) is { Step: Step.Working } job)
            {
                sawWork = true;
                // Worked from beside the tree, by the Forestry skill.
                var feller = camp.Work.Worker(job.Worker!.Value);
                var (tx, ty) = Ground.TileOf(feller.X, feller.Y);
                Assert.True(Math.Abs(tx - 6) <= 1 && Math.Abs(ty - 6) <= 1, "the feller works from beside the tree");
                Assert.Equal(Work.FellWork, camp.Work.KindOf(job));
            }
        });
        Assert.True(sawWork);
        // Felled: no tree, no stump, the tile open and buildable.
        Assert.Null(camp.Ground.ObstacleAt(6, 6));
        Assert.True(camp.Ground.Open(6, 6));
        // The 4 timber were made once, where the tree stood, and are carried to the carts.
        var made = Assert.Single(camp.Stocks.Ledger, e => e.Change == Change.Made && e.Good == "timber");
        Assert.Equal((4, site), (made.Amount, made.To!.Value));
        camp.Until(() => camp.Work.Project(tree.Id) == null);
        Assert.Equal(4, camp.Stocks[camp.Carts.Id].Count("timber"));
        Assert.Equal(4, camp.Stocks.Total("timber"));
        Assert.DoesNotContain(camp.Stocks.Inventories, i => i.Id == site);
        Assert.True(camp.Work.Check("wall", 6, 6).Ok);
    }

    [Fact]
    public void V1_felling_takes_the_trees_minutes_at_the_usual_pace()
    {
        var camp = new Camp(people: 1);
        var tree = camp.Fell(6, 6);
        camp.Colony.Run(10 * 60, _ => { });
        var job = camp.Felling(tree)!;
        Assert.Equal(Step.Working, job.Step);
        // 30 minutes, at standard proficiency (Forestry 40: pace 1).
        Assert.Equal(40, camp.People[0].Skills["forestry"].Proficiency);
        long started = camp.Colony.Now.Tick;
        int left = job.Left;
        Assert.InRange(left, 30 * Calendar.TicksPerMinute - 10 * 60, 30 * Calendar.TicksPerMinute);
        camp.Colony.Run(left, _ => { });
        Assert.Null(camp.Ground.ObstacleAt(6, 6));
        Assert.Equal(started + left, camp.Colony.Now.Tick);
    }

    [Fact]
    public void V2_unmarking_a_tree_before_it_is_felled_leaves_it_standing()
    {
        var camp = new Camp();
        var tree = camp.Fell(40, 20);
        camp.Work.CancelBuild(tree.Id);
        Assert.Null(camp.Work.Project(tree.Id));
        Assert.DoesNotContain(camp.Work.Jobs, j => j.Kind == JobKind.Build);
        camp.Colony.RunMinutes(60);
        Assert.Equal("tree", camp.Ground.ObstacleAt(40, 20));
    }

    [Fact]
    public void V2_a_felling_put_down_part_way_keeps_its_progress()
    {
        var camp = new Camp(people: 1);
        var tree = camp.Fell(6, 6);
        camp.Until(() => camp.Felling(tree) is { Step: Step.Working });
        camp.Colony.RunMinutes(10);
        int left = camp.Felling(tree)!.Left;
        Assert.InRange(left, 1, 30 * Calendar.TicksPerMinute - 9 * 60);
        camp.Work.Pause(tree.Id, true);
        camp.Colony.RunMinutes(20);
        Assert.Equal("tree", camp.Ground.ObstacleAt(6, 6));
        Assert.Equal(left, camp.Felling(tree)!.Left);
        camp.Work.Pause(tree.Id, false);
        camp.Until(() => camp.Ground.ObstacleAt(6, 6) == null, most: 25);
    }

    [Fact]
    public void V3_nobody_allowed_to_fell_trees_is_explained()
    {
        var camp = new Camp();
        foreach (var w in camp.People)
            camp.Work.Permit(w.Id, Work.FellWork, false);
        var tree = camp.Fell(6, 6);
        camp.Colony.RunMinutes(60);
        Assert.Equal("tree", camp.Ground.ObstacleAt(6, 6));
        var why = camp.Work.WhyBuild(tree.Id)!;
        Assert.Contains("fell trees", why.Player);
    }

    [Fact]
    public void Only_a_tree_can_be_marked_and_only_once()
    {
        var camp = new Camp();
        Assert.Equal("There is no tree there to fell.", camp.Work.CheckFell(30, 8).Why);   // a rock
        Assert.Equal("There is no tree there to fell.", camp.Work.CheckFell(20, 20).Why);  // open grass
        camp.Fell(6, 6);
        Assert.Equal("It is already marked for felling.", camp.Work.CheckFell(6, 6).Why);
        // Nothing can be built on a tree, marked or not.
        Assert.False(camp.Work.Check("wall", 6, 6).Ok);
    }

    [Fact]
    public void The_inspector_offers_to_fell_a_tree_and_to_leave_a_marked_one_standing()
    {
        var camp = new Camp();
        var fell = Assert.Single(Inspect.Commands(camp.Colony, 6, 6), c => c.Label == "Fell");
        Assert.Equal('F', fell.Key);
        fell.Run!();
        var commands = Inspect.Commands(camp.Colony, 6, 6);
        Assert.DoesNotContain(commands, c => c.Label == "Fell");
        Assert.Contains(commands, c => c.Label == "Leave standing");
        Assert.Contains(Inspect.Tile(camp.Colony, 6, 6).Lines, l => l.Text == "Felling: Tree");
        Assert.DoesNotContain(Inspect.Commands(camp.Colony, 20, 20), c => c.Label == "Fell");
    }

    [Fact]
    public void V8_a_save_in_the_middle_of_felling_goes_on_the_same()
    {
        var camp = new Camp();
        camp.Fell(6, 6);
        camp.Fell(40, 20);
        var paused = camp.Fell(41, 22);
        camp.Work.Pause(paused.Id, true);
        camp.Until(() => camp.Work.Jobs.Any(j => j.Kind == JobKind.Build && j.Step == Step.Working));
        camp.Colony.RunMinutes(5);
        string text = ColonySave.Text(camp.Colony);
        var loaded = ColonySave.Read(text, Real);
        Assert.Equal(text, ColonySave.Text(loaded));
        Assert.True(loaded.Work!.Project(paused.Id)!.Paused);
        Assert.True(loaded.Work.Project(paused.Id)!.Felling);
        camp.Colony.RunMinutes(200);
        loaded.RunMinutes(200);
        Assert.Equal(ColonySave.Fingerprint(camp.Colony), ColonySave.Fingerprint(loaded));
        Assert.Null(loaded.Ground!.ObstacleAt(6, 6));
        Assert.Null(loaded.Ground.ObstacleAt(40, 20));
        Assert.Equal("tree", loaded.Ground.ObstacleAt(41, 22));
        Assert.Equal(8, loaded.Stocks.Total("timber"));
    }

    [Fact]
    public void A_save_with_a_felling_where_no_tree_stands_is_refused()
    {
        var camp = new Camp();
        camp.Fell(6, 6);
        var save = JsonNode.Parse(ColonySave.Text(camp.Colony))!.AsObject();
        var project = save["work"]!["projects"]![0]!.AsObject();
        project["x"] = 20;
        project["y"] = 20;
        var e = Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), Real));
        Assert.Contains("is planned where it cannot be (20, 20)", e.Message);
    }

    [Fact]
    public void Every_kind_of_tree_needs_a_challenge_and_its_own_id()
    {
        var files = TestContent.RealFiles();
        var skills = JsonNode.Parse(files["skills.json"]!)!.AsObject();
        skills["challenge"]!.AsObject().Remove("tree");
        files["skills.json"] = skills.ToJsonString();
        var construction = JsonNode.Parse(files["construction.json"]!)!.AsObject();
        construction["trees"]!.AsArray().Add(JsonNode.Parse("""{ "id": "wall", "name": "Wall tree", "yield": {}, "work_minutes": 5 }"""));
        files["construction.json"] = construction.ToJsonString();
        var e = Assert.Throws<ContentException>(() => Content.Read(files));
        Assert.Contains(e.Problems, p => p.Contains("no challenge for \"tree\""));
        Assert.Contains(e.Problems, p => p.Contains("\"wall\" is already a piece's id"));
    }
}
