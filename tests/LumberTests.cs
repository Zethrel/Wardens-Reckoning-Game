// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 28b: timber into lumber (library Review 44: D5 C a one-tile sawhorse, D6 C 1 timber into 2 lumber of 3 kg and
/// 4 L in 20 minutes by Carpentry, D7 A doors, floors, water barrels and wash basins of lumber with a quarter less
/// work, walls and the well of timber, D8 B the founders' 50 timber, 20 lumber and a sawhorse). The checks V4 to V8.
/// </summary>
public class LumberTests
{
    static readonly Content Real = TestContent.EverySecond;              // thinking every second: see TestContent
    static readonly Moment Nine = new(Calendar.TickOf(301, 1, 1, 9, 0));

    sealed class Camp
    {
        public readonly ColonyState Colony;
        public Work Work => Colony.Work!;
        public Stocks Stocks => Colony.Stocks;
        public readonly Inventory Carts;
        public readonly Site Sawhorse;
        public readonly List<Worker> People = new();

        public Camp(int timber = 10, int lumber = 0, int people = 2)
        {
            Colony = new ColonyState(42, Nine, Real);
            Colony.Lay(Ground.Test(Real));
            Carts = Stocks.Open("The founders' carts", Holder.Store, 2.5, 2.5);
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "timber", timber);
            if (lumber > 0)
                Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "lumber", lumber);
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "ration", 30);
            Work.AddSpring(3.5, 9.5);
            Sawhorse = Work.AddSite("sawhorse", 6.5, 2.5);
            foreach (string name in new[] { "Ada", "Bram", "Cael" }.Take(people))
            {
                var w = Work.AddWorker(name, 3.5, 3.5);
                Work.AddBedroll(1.5 + 2 * People.Count, 10.5, w.Id);
                People.Add(w);
            }
        }

        /// <summary>Runs on a minute at a time until `done` (failing after `most` minutes), checking every minute that
        /// no timber or lumber is lost or made from nothing.</summary>
        public void Until(Func<bool> done, int most = 600)
        {
            for (int i = 0; !done(); i++)
            {
                Assert.True(i < most, "it never happened");
                Colony.RunMinutes(1, _ =>
                {
                    Assert.Equal(Stocks.Accounted("timber"), Stocks.Total("timber"));
                    Assert.Equal(Stocks.Accounted("lumber"), Stocks.Total("lumber"));
                });
            }
        }
    }

    [Fact]
    public void V4_sawing_one_timber_at_the_sawhorse_makes_two_lumber_once()
    {
        var camp = new Camp();
        camp.Work.Order(camp.Sawhorse.Id, "saw_timber", 1);
        bool sawWork = false;
        camp.Until(() =>
        {
            if (camp.Work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Craft) is { Step: Step.Working } job)
            {
                sawWork = true;
                Assert.Equal("sawhorse", camp.Work.KindOf(job));
            }
            return camp.Stocks.Total("lumber") == 2 && camp.Stocks[camp.Carts.Id].Count("lumber") == 2;
        });
        Assert.True(sawWork);
        Assert.Equal(9, camp.Stocks.Total("timber"));
        var used = Assert.Single(camp.Stocks.Ledger, e => e.Change == Change.Used && e.Good == "timber");
        var made = Assert.Single(camp.Stocks.Ledger, e => e.Change == Change.Made && e.Good == "lumber");
        Assert.Equal((1, 2), (used.Amount, made.Amount));
        Assert.Equal(used.Operation, made.Operation);                          // one operation: in and out together
        Assert.Null(camp.Sawhorse.Recipe);                                     // the order is done
    }

    [Fact]
    public void V4_the_recipe_and_the_goods_are_the_reviews()
    {
        var saw = Real.Recipes["saw_timber"];
        Assert.Equal(("sawhorse", 20), (saw.Workshop, saw.WorkMinutes));
        Assert.Equal(1, saw.Inputs["timber"]);
        Assert.Equal(2, saw.Outputs["lumber"]);
        Assert.Equal((3.0, 4.0), (Real.Goods["lumber"].MassKg, Real.Goods["lumber"].VolumeL));
        // 10 kg of timber in, 6 kg of lumber out: the 4 kg of bark and offcuts leave the colony's goods (D6 C).
        Assert.Equal(4.0, Real.Goods["timber"].MassKg - 2 * Real.Goods["lumber"].MassKg);
        Assert.Equal("carpentry", Real.SkillFor["sawhorse"]);
    }

    [Fact]
    public void V5_doors_floors_barrels_and_basins_are_lumber_with_a_quarter_less_work_and_walls_and_the_well_timber()
    {
        var p = Real.Pieces;
        Assert.Equal(6, p["door"].Materials["lumber"]);
        Assert.Equal(2, p["floor"].Materials["lumber"]);
        Assert.Equal(6, p["water_barrel"].Materials["lumber"]);
        Assert.Equal(4, p["wash_basin"].Materials["lumber"]);
        // A quarter less than in timber (45, 10, 40, 20 minutes), rounded up to whole minutes.
        Assert.Equal((34, 8, 30, 15), (p["door"].WorkMinutes, p["floor"].WorkMinutes, p["water_barrel"].WorkMinutes, p["wash_basin"].WorkMinutes));
        Assert.Equal(2, p["wall"].Materials["timber"]);
        Assert.Equal(8, p["well"].Materials["timber"]);
        Assert.Equal("timber", Real.Sanitation.Fuel);                          // timber stays the fuel
    }

    [Fact]
    public void V5_a_door_is_built_of_lumber_brought_from_the_store()
    {
        var camp = new Camp(timber: 0, lumber: 10);
        var (door, placement) = camp.Work.Plan("door", 20, 20);
        Assert.True(placement.Ok, placement.Why);
        camp.Until(() => camp.Colony.Ground!.PieceAt(20, 20) == "door");
        Assert.Equal(4, camp.Stocks.Total("lumber"));
        Assert.Null(camp.Work.Project(door!.Id));
    }

    [Fact]
    public void V6_a_colonist_carries_eight_lumber_or_two_timber()
    {
        Assert.Equal(8, (int)Math.Floor(Real.CarryKg / Real.Goods["lumber"].MassKg));
        Assert.Equal(2, (int)Math.Floor(Real.CarryKg / Real.Goods["timber"].MassKg));
    }

    [Fact]
    public void V7_the_founders_bring_fifty_timber_twenty_lumber_and_a_sawhorse()
    {
        var (colony, carts) = Founding.Found(7, Nine, Real, 20, 20);
        Assert.Equal(50, colony.Stocks[carts.Id].Count("timber"));
        Assert.Equal(20, colony.Stocks[carts.Id].Count("lumber"));
        var sawhorse = Assert.Single(colony.Work!.Sites);
        Assert.Equal("sawhorse", sawhorse.Workshop);
        Assert.True(colony.Ground!.Open(Ground.TileOf(sawhorse.X, sawhorse.Y).X, Ground.TileOf(sawhorse.X, sawhorse.Y).Y));
        Assert.All(colony.Work.Workers, w => Assert.InRange(w.Skills["carpentry"].Proficiency, 5, 65));
    }

    [Fact]
    public void The_inspector_orders_sawing_and_stops_it()
    {
        var camp = new Camp();
        var order = Assert.Single(Inspect.Commands(camp.Colony, 6, 2), c => c.Label == "Order 5 more");
        Assert.Equal('O', order.Key);
        order.Run!();
        Assert.Equal(("saw_timber", 5), (camp.Sawhorse.Recipe, camp.Sawhorse.Batches));
        Assert.Contains(Inspect.Tile(camp.Colony, 6, 2).Lines, l => l.Label == "Workplace" && l.Text == "Sawhorse: saw timber into lumber, 5 more");
        Assert.Single(Inspect.Commands(camp.Colony, 6, 2), c => c.Label == "Order 5 more").Run!();
        Assert.Equal(10, camp.Sawhorse.Batches);
        Assert.Single(Inspect.Commands(camp.Colony, 6, 2), c => c.Label == "Stop order").Run!();
        Assert.Null(camp.Sawhorse.Recipe);
    }

    [Fact]
    public void V8_a_save_in_the_middle_of_sawing_goes_on_the_same()
    {
        var camp = new Camp(timber: 10, lumber: 6);
        camp.Work.Order(camp.Sawhorse.Id, "saw_timber", 4);
        camp.Work.Plan("door", 20, 20);
        camp.Until(() => camp.Work.Jobs.Any(j => j.Kind == JobKind.Craft && j.Step == Step.Working));
        camp.Colony.RunMinutes(7);
        string text = ColonySave.Text(camp.Colony);
        var loaded = ColonySave.Read(text, Real);
        Assert.Equal(text, ColonySave.Text(loaded));
        camp.Colony.RunMinutes(240);
        loaded.RunMinutes(240);
        Assert.Equal(ColonySave.Fingerprint(camp.Colony), ColonySave.Fingerprint(loaded));
        Assert.Equal(6, loaded.Stocks.Total("timber"));
        Assert.Equal(6 + 8 - 6, loaded.Stocks.Total("lumber"));
        Assert.Equal("door", loaded.Ground!.PieceAt(20, 20));
    }
}
