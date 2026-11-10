// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 13b: people carry and grind. FP-02 (the economy loop: wheat stored, carried to the quern, ground, the flour
/// carried to a store, nothing made or lost) and FP-05 (an interrupted, cancelled and retried haul: every unit
/// always has a keeper, every change happens once), and the work's part of FP-07 (determinism) and of the save.
/// </summary>
public class WorkTests
{
    static readonly Moment Start = new(Calendar.TickOf(301, 1, 1, 6, 0));
    /// <summary>The real content, but with nobody getting hungry, thirsty or tired, and every hour a Work hour: these
    /// tests are about the work alone (the needs and the day have their own, LifeTests).</summary>
    static readonly Content Real = NoNeeds();

    static Content NoNeeds() => Content.Read(NoNeedsFiles());

    static Dictionary<string, string?> NoNeedsFiles()
    {
        var files = TestContent.ThinkingEverySecond(TestContent.Files());   // thinking every second: see TestContent
        var needs = JsonNode.Parse(files["needs.json"]!)!.AsObject();
        foreach (var (_, rule) in needs["needs"]!.AsObject())
            rule!["per_hour"] = 0;
        files["needs.json"] = needs.ToJsonString();
        var schedules = JsonNode.Parse(files["schedules.json"]!)!.AsObject();
        schedules["templates"]![0]!["hours"] = new JsonArray(Enumerable.Repeat((JsonNode?)"work", 24).Select(n => n?.DeepClone()).ToArray());
        files["schedules.json"] = schedules.ToJsonString();
        return files;
    }

    /// <summary>The founders' carts (50 wheat) by the camp, a quern 140 m away (two minutes' walk), a granary for
    /// the flour, and workers at the camp.</summary>
    sealed class Camp
    {
        public readonly ColonyState Colony;
        public Work Work => Colony.Work!;
        public Stocks Stocks => Colony.Stocks;
        public readonly Inventory Carts, Granary;
        public readonly Site Quern;
        public readonly List<Worker> Workers = new();

        public Camp(int workers = 1, Content? content = null, long seed = 42)
        {
            Colony = new ColonyState(seed, Start, content ?? Real);
            Carts = Stocks.Open("The founders' carts", Holder.Store, 0, 0);
            Carts.Accepts.Add("wheat");
            Granary = Stocks.Open("Granary", Holder.Store, 0, 70);
            Granary.Accepts.Add("flour");
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Start.Tick, Carts.Id, "wheat", 50);
            Quern = Work.AddSite("quern", 140, 0);
            foreach (string name in new[] { "Ada", "Bram", "Cael" }.Take(workers))
                Workers.Add(Work.AddWorker(name, 0, 0));
        }

        public void RunMinutes(int ticks, Action<ColonyState>? each = null) => Colony.RunMinutes(ticks, each);

        /// <summary>Runs until `done` (at most `most` minutes); the minutes it took.</summary>
        public int RunUntil(Func<bool> done, int most = 3 * Calendar.MinutesPerDay)
        {
            for (int i = 1; i <= most; i++)
            {
                Colony.RunMinutes(1);
                if (done())
                    return i;
            }
            throw new Xunit.Sdk.XunitException("not done in " + most + " minutes");
        }

        public Job? JobOf(Worker w) => w.Job is TaskId t ? Work.JobOf(t) : null;

        public void Conserved()
        {
            foreach (string good in new[] { "wheat", "flour" })
                Assert.Equal(Stocks.Accounted(good), Stocks.Total(good));
            Assert.Equal(50, Stocks.Total("wheat") + Stocks.Total("flour") * 5 / 4);
        }
    }

    // ---- FP-02: the economy loop ----------------------------------------------------------------------------

    [Fact]
    public void FP02_wheat_is_carried_ground_and_its_flour_stored()
    {
        var camp = new Camp();
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 3);
        camp.RunUntil(() => camp.Granary.Count("flour") == 12 && camp.Work.Jobs.Count == 0);
        Assert.Equal(35, camp.Carts.Count("wheat"));
        Assert.Empty(camp.Stocks[camp.Quern.Input].Goods);
        Assert.Empty(camp.Stocks[camp.Quern.Output].Goods);
        Assert.Null(camp.Quern.Recipe);
        Assert.Equal(0, camp.Quern.Batches);
        camp.Conserved();
        // The ledger tells how: wheat into Ada's arms, to the quern, used; flour made, into her arms, to the granary.
        var arms = camp.Workers[0].Arms;
        var ledger = camp.Stocks.Ledger;
        Assert.Contains(ledger, e => e.Change == Change.Moved && e.Good == "wheat" && e.From == camp.Carts.Id && e.To == arms);
        Assert.Contains(ledger, e => e.Change == Change.Moved && e.Good == "wheat" && e.From == arms && e.To == camp.Quern.Input);
        Assert.Equal(3, ledger.Count(e => e.Change == Change.Used && e.Good == "wheat" && e.Amount == 5));
        Assert.Equal(3, ledger.Count(e => e.Change == Change.Made && e.Good == "flour" && e.Amount == 4));
        Assert.Contains(ledger, e => e.Change == Change.Moved && e.Good == "flour" && e.From == arms && e.To == camp.Granary.Id);
    }

    [Fact]
    public void A_batch_takes_its_walks_loading_and_grinding()
    {
        // Minute 1: the quern asks for wheat and Ada takes the haul. She stands at the carts, so she loads at once
        // (1 minute), walks 140 m (2), unloads (1): done a second into minute 5 (she thinks every second here, and
        // set off a second after loading). The quern asks once a minute, so the grinding job is made at minute 6;
        // she stands at the quern, and it turns for 180 minutes. The flour is made in minute 6 + 180 = 186.
        var camp = new Camp();
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        int minutes = camp.RunUntil(() => camp.Stocks.Total("flour") == 4);
        Assert.Equal(1 + 1 + 2 + 1 + 1 + 180, minutes);
    }

    [Fact]
    public void A_walker_goes_seventy_metres_a_minute()
    {
        var camp = new Camp();
        var ada = camp.Workers[0];
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        while (camp.JobOf(ada)?.Step != Step.Carrying)
            camp.Colony.Run(1);
        camp.Colony.Run(1);                                             // her next think: she sets off
        Assert.Equal((0.0, 0.0), (ada.X, ada.Y));
        Assert.Equal((140.0, 0.0, false), ada.Heading);
        camp.Colony.Run(30);                                            // half a minute: second by second
        Assert.Equal(35.0, ada.X, 9);
        camp.Colony.Run(30);
        Assert.Equal(70.0, ada.X, 9);
        Assert.Equal(0.0, ada.Y);
        Assert.Equal((ada.X, 0.0), (camp.Stocks[ada.Arms].X, camp.Stocks[ada.Arms].Y));
    }

    [Fact]
    public void Loading_and_unloading_can_take_no_time()
    {
        var files = NoNeedsFiles();
        var colony = JsonNode.Parse(files["colony.json"]!)!.AsObject();
        colony["pickup_minutes"] = 0;
        colony["drop_minutes"] = 0;
        files["colony.json"] = colony.ToJsonString();
        var camp = new Camp(content: Content.Read(files));
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        // As above, with loading and unloading taking no time: she loads in minute 1 and sets off a second later,
        // walks two minutes and unloads as she arrives, a second into minute 3; the quern asks at minute 4, and turns
        // its 180 minutes: done in minute 184.
        Assert.Equal(1 + 2 + 1 + 180, camp.RunUntil(() => camp.Stocks.Total("flour") == 4));
    }

    [Fact]
    public void No_two_workers_ever_take_the_same_job()
    {
        var camp = new Camp(workers: 3);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 6);
        camp.RunUntil(() => camp.Granary.Count("flour") == 24 && camp.Work.Jobs.Count == 0, most: 4 * Calendar.MinutesPerDay);
        camp.Conserved();
        var test = new Camp(workers: 3);
        test.Work.Order(test.Quern.Id, "grind_wheat", 6);
        test.RunMinutes(Calendar.MinutesPerDay, _ =>
        {
            var held = test.Workers.Where(w => w.Job != null).Select(w => w.Job!.Value).ToList();
            Assert.Equal(held.Count, held.Distinct().Count());
            foreach (var job in test.Work.Jobs.Where(j => j.Worker != null))
                Assert.Equal(job.Id, test.Work.Worker(job.Worker!.Value).Job);
        });
    }

    [Fact]
    public void A_workshop_with_no_order_asks_for_nothing()
    {
        var camp = new Camp();
        camp.RunMinutes(60);
        Assert.Empty(camp.Work.Jobs);
        Assert.Equal(50, camp.Carts.Count("wheat"));
    }

    [Fact]
    public void A_workshop_asks_only_for_what_is_missing()
    {
        // With 3 wheat already at the quern, it asks for 2.
        var camp = new Camp();
        var at = camp.Stocks.Reserve(camp.Carts.Id, "wheat", 3, "test").Reservation!;
        camp.Stocks.Move(camp.Colony.Ids.Next<OperationId>(), Start.Tick, at.Id, camp.Quern.Input);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunMinutes(1);
        var haul = Assert.Single(camp.Work.Jobs);
        Assert.Equal((JobKind.Haul, "wheat", 2), (haul.Kind, haul.Good, haul.Amount));
    }

    [Fact]
    public void One_person_carries_no_more_than_twenty_five_kilos()
    {
        // A recipe wanting 60 wheat at once is fetched in three loads: 25, 25 and 10.
        var files = NoNeedsFiles();
        var recipes = JsonNode.Parse(files["recipes.json"]!)!.AsObject();
        recipes["recipes"]![0]!["inputs"]!["wheat"] = 60;
        files["recipes.json"] = recipes.ToJsonString();
        var camp = new Camp(content: Content.Read(files));
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), Start.Tick, camp.Carts.Id, "wheat", 20);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunMinutes(1);
        Assert.Equal(new[] { 25, 25, 10 }, camp.Work.Jobs.Select(j => j.Amount));
    }

    // ---- Why a workshop waits -----------------------------------------------------------------------------

    [Fact]
    public void A_workshop_with_no_free_wheat_says_why()
    {
        var camp = new Camp();
        camp.Carts.Keep("wheat", 48);
        camp.Stocks.Reserve(camp.Carts.Id, "wheat", 1, "seed corn");
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunMinutes(1);
        // One wheat was free: it is fetched, and the quern waits for the other four.
        Assert.Equal("waiting for wheat: 4 more needed, and no store has any free (50 in the stores: 48 kept as a reserve, 2 claimed for other work)",
                     camp.Quern.Blocked);
    }

    [Fact]
    public void Made_goods_with_no_store_to_take_them_are_named()
    {
        var camp = new Camp();
        camp.Granary.Accepts.Clear();
        camp.Granary.Accepts.Add("wheat");
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunUntil(() => camp.Stocks[camp.Quern.Output].Count("flour") == 4);
        camp.RunMinutes(1);
        Assert.Equal("no store takes flour", camp.Quern.Blocked);
    }

    // ---- FP-05: an interrupted, cancelled and retried haul ----------------------------------------------------

    [Fact]
    public void FP05_a_stopped_carrier_keeps_the_goods_until_resumed()
    {
        var camp = new Camp(workers: 2);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunUntil(() => camp.Workers.Any(w => camp.JobOf(w)?.Step == Step.Carrying));
        var carrier = camp.Workers.Single(w => camp.JobOf(w)?.Step == Step.Carrying);
        var job = camp.JobOf(carrier)!;
        camp.Work.Stop(carrier.Id);
        camp.RunMinutes(120);
        // The wheat is still in the carrier's arms, the job still theirs, and nobody else took it.
        Assert.Equal(5, camp.Stocks[carrier.Arms].Count("wheat"));
        Assert.Equal(job.Id, carrier.Job);
        Assert.Equal(carrier.Id, job.Worker);
        Assert.Equal(0, camp.Stocks.Total("flour"));
        camp.Conserved();
        camp.Work.Resume(carrier.Id);
        camp.RunUntil(() => camp.Granary.Count("flour") == 4);
        camp.Conserved();
    }

    [Fact]
    public void FP05_a_job_stopped_before_pickup_is_taken_by_someone_else()
    {
        var camp = new Camp(workers: 2);
        var (ada, bram) = (camp.Workers[0], camp.Workers[1]);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunMinutes(1);                                      // Ada (the lower number, as near) takes the haul
        var job = camp.JobOf(ada)!;
        Assert.Equal(Step.Loading, job.Step);                    // taken, not yet picked up (she stands at the carts)
        camp.Work.Stop(ada.Id);
        Assert.Null(ada.Job);
        Assert.Equal(Step.Open, job.Step);
        Assert.Single(job.Claims);                        // the wheat stays claimed for the job
        camp.RunMinutes(1);
        Assert.Equal(job.Id, bram.Job);
        camp.RunUntil(() => camp.Granary.Count("flour") == 4);
        camp.Conserved();
    }

    [Fact]
    public void FP05_a_cancelled_haul_takes_its_cargo_to_a_store_and_is_tried_again()
    {
        var camp = new Camp();
        var ada = camp.Workers[0];
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunUntil(() => camp.JobOf(ada)?.Step == Step.Carrying);
        var job = camp.JobOf(ada)!;
        camp.RunMinutes(1);                                      // she is on the way, the wheat in her arms
        camp.Work.Cancel(job.Id);
        Assert.Equal(camp.Carts.Id, job.To);              // the nearest store taking wheat
        camp.RunUntil(() => ada.Job == null || camp.JobOf(ada)!.Id != job.Id);
        Assert.Equal(0, camp.Stocks[ada.Arms].Count("wheat"));
        Assert.Equal(50, camp.Carts.Count("wheat"));
        // The quern still wants its wheat, so a new haul is made and the order is finished after all.
        camp.RunUntil(() => camp.Granary.Count("flour") == 4);
        camp.Conserved();
    }

    [Fact]
    public void FP05_a_haul_cancelled_before_pickup_lets_its_goods_go()
    {
        var camp = new Camp();
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunMinutes(1);
        var job = Assert.Single(camp.Work.Jobs);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 0);
        camp.Work.Cancel(job.Id);
        Assert.Empty(camp.Work.Jobs);
        Assert.Empty(camp.Stocks.Reservations);
        Assert.Null(camp.Workers[0].Job);
        Assert.Equal(50, camp.Stocks.Free(camp.Carts.Id, "wheat"));
        camp.Work.Cancel(job.Id);                         // cancelling it again does nothing
        camp.RunMinutes(60);
        Assert.Empty(camp.Work.Jobs);
    }

    [Fact]
    public void FP05_a_stopped_grinding_is_carried_on_by_another()
    {
        var camp = new Camp(workers: 2);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunUntil(() => camp.Workers.Any(w => camp.JobOf(w)?.Step == Step.Working));
        camp.RunMinutes(100);
        var grinder = camp.Workers.Single(w => camp.JobOf(w)?.Step == Step.Working);
        var job = camp.JobOf(grinder)!;
        camp.Work.Stop(grinder.Id);
        Assert.Equal(Step.Open, job.Step);
        Assert.Single(job.Claims);                        // the five wheat stay claimed for the batch
        // The other worker walks over and carries on: the 80 minutes still to go, not 180 again.
        int minutes = camp.RunUntil(() => camp.Stocks.Total("flour") == 4);
        Assert.InRange(minutes, 80, 90);
        Assert.True(grinder.Stopped);                    // so the other worker ground it
        Assert.Null(grinder.Job);
        camp.Conserved();
    }

    /// <summary>The real content with `change` made to one file.</summary>
    static Content With(string file, Action<JsonObject> change)
    {
        var files = NoNeedsFiles();
        var root = JsonNode.Parse(files[file]!)!.AsObject();
        change(root);
        files[file] = root.ToJsonString();
        return Content.Read(files);
    }

    /// <summary>The real content and a second workshop, a mill, with a recipe of its own.</summary>
    static Content WithMill() => With("recipes.json", r =>
    {
        r["workshops"]!.AsArray().Add(new JsonObject { ["id"] = "mill", ["name"] = "Mill" });
        var mill = r["recipes"]![0]!.DeepClone().AsObject();
        mill["id"] = "mill_wheat";
        mill["workshop"] = "mill";
        r["recipes"]!.AsArray().Add(mill);
    });

    [Fact]
    public void Loading_and_unloading_take_their_minutes()
    {
        // Loading 3 minutes and unloading 2: minute 1 the haul is taken; loading to minute 4; walking two minutes
        // (from a second after); unloading two, done a second into minute 8; the quern asks at minute 9 and turns
        // to minute 189. Then the flour waits at the quern while it is loaded.
        var camp = new Camp(content: With("colony.json", c => { c["pickup_minutes"] = 3; c["drop_minutes"] = 2; }));
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 2);
        Assert.Equal(1 + 3 + 2 + 2 + 1 + 180, camp.RunUntil(() => camp.Stocks.Total("flour") == 4));
        camp.RunUntil(() => camp.Granary.Count("flour") == 8 && camp.Work.Jobs.Count == 0);
        camp.Conserved();
    }

    [Fact]
    public void Wheat_is_fetched_from_the_nearest_store_that_has_it_free()
    {
        var camp = new Camp();
        var shed = camp.Stocks.Open("Shed by the quern", Holder.Store, 130, 0);
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), Start.Tick, shed.Id, "wheat", 5);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunMinutes(1);
        Assert.Equal(shed.Id, Assert.Single(camp.Work.Jobs).From);
    }

    [Fact]
    public void A_job_goes_to_the_nearest_free_worker()
    {
        // Ada stands at the carts (0, 0), Bram 5 m off: Ada is nearer the wheat and takes the haul.
        var camp = new Camp();
        var (ada, bram) = (camp.Workers[0], camp.Work.AddWorker("Bram", 0, 5));
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunMinutes(1);
        Assert.NotNull(ada.Job);
        Assert.Null(bram.Job);
        // A stopped worker takes nothing, however near.
        var stopped = new Camp();
        var cara = stopped.Work.AddWorker("Cara", 0, 5);
        stopped.Work.Stop(stopped.Workers[0].Id);
        stopped.Work.Order(stopped.Quern.Id, "grind_wheat", 1);
        stopped.RunMinutes(1);
        Assert.NotNull(cara.Job);
        // Between two at the same distance, the lower number.
        var even = new Camp(workers: 2);
        even.Work.Order(even.Quern.Id, "grind_wheat", 1);
        even.RunMinutes(1);
        Assert.NotNull(even.Workers[0].Job);
        Assert.Null(even.Workers[1].Job);
    }

    [Fact]
    public void A_job_goes_to_the_worker_nearest_the_goods()
    {
        var camp = new Camp();
        var byTheQuern = camp.Work.AddWorker("Bram", 140, 0);
        var shed = camp.Stocks.Open("Shed by the quern", Holder.Store, 140, 10);
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), Start.Tick, shed.Id, "wheat", 5);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunMinutes(1);
        Assert.NotNull(byTheQuern.Job);
        Assert.Null(camp.Workers[0].Job);
    }

    [Fact]
    public void A_cancelled_haul_goes_to_another_store_than_its_own()
    {
        // Flour on its way to the granary, cancelled: it goes to the nearest other store taking flour, the cellar.
        var camp = new Camp();
        var cellar = camp.Stocks.Open("Cellar", Holder.Store, 0, 500);
        cellar.Accepts.Add("flour");
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunUntil(() => camp.Work.Jobs.Any(j => j.Good == "flour" && j.Step == Step.Carrying));
        var job = camp.Work.Jobs.Single(j => j.Good == "flour");
        camp.Work.Cancel(job.Id);
        Assert.Equal(cellar.Id, job.To);
        camp.RunUntil(() => cellar.Count("flour") == 4, most: Calendar.MinutesPerDay);
        Assert.Equal(0, camp.Granary.Count("flour"));
        camp.Conserved();
    }

    [Fact]
    public void A_workshop_cannot_be_ordered_another_workshops_recipe()
    {
        var camp = new Camp(content: WithMill());
        Assert.Throws<ArgumentException>(() => camp.Work.Order(camp.Quern.Id, "mill_wheat", 1));
        camp.Work.Order(camp.Work.AddSite("mill", 10, 10).Id, "mill_wheat", 1);
        var save = ColonySave.Write(camp.Colony);
        save["work"]!["sites"]![0]!["recipe"] = "mill_wheat";
        Assert.Contains("is not one this workshop can work",
            Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), WithMill())).Message);
    }

    [Fact]
    public void A_wrong_order_is_refused()
    {
        var camp = new Camp();
        Assert.Throws<ArgumentException>(() => camp.Work.Order(camp.Quern.Id, "bake_bread", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => camp.Work.Order(camp.Quern.Id, "grind_wheat", -1));
        Assert.Throws<ArgumentException>(() => camp.Work.AddSite("oven", 0, 0));
    }

    // ---- FP-07 and the save, with work going on -------------------------------------------------------------

    static List<string> Fingerprints(Camp camp, int ticks)
    {
        var prints = new List<string>();
        camp.RunMinutes(ticks, c =>
        {
            if (c.Now.MinuteOfDay % 60 == 0)
                prints.Add(ColonySave.Fingerprint(c));
        });
        return prints;
    }

    [Fact]
    public void FP07_the_same_camp_works_out_the_same()
    {
        Camp Make()
        {
            var camp = new Camp(workers: 3);
            camp.Work.Order(camp.Quern.Id, "grind_wheat", 8);
            return camp;
        }
        Assert.Equal(Fingerprints(Make(), Calendar.MinutesPerDay), Fingerprints(Make(), Calendar.MinutesPerDay));
    }

    [Fact]
    public void FP07_saving_and_loading_in_the_middle_of_a_haul_changes_nothing()
    {
        var straight = new Camp(workers: 2);
        straight.Work.Order(straight.Quern.Id, "grind_wheat", 4);
        var halves = new Camp(workers: 2);
        halves.Work.Order(halves.Quern.Id, "grind_wheat", 4);
        int minutes = halves.RunUntil(() => halves.Workers.Any(w => halves.JobOf(w)?.Step == Step.Carrying));
        halves.RunMinutes(1);
        straight.RunMinutes(minutes + 1);
        var loaded = ColonySave.Read(ColonySave.Text(halves.Colony), Real);
        Assert.Equal(ColonySave.Text(halves.Colony), ColonySave.Text(loaded));
        for (int i = 0; i < Calendar.MinutesPerDay; i++)
        {
            straight.Colony.RunMinutes(1);
            loaded.RunMinutes(1);
        }
        Assert.Equal(ColonySave.Fingerprint(straight.Colony), ColonySave.Fingerprint(loaded));
        Assert.Equal(16, loaded.Stocks.Total("flour"));
    }

    public static IEnumerable<object[]> BrokenWork()
    {
        object[] Case(Action<JsonObject> change, string reason)
        {
            var camp = new Camp(workers: 2);
            camp.Work.Order(camp.Quern.Id, "grind_wheat", 2);
            camp.RunMinutes(1);
            camp.Colony.Run(10);                                 // Ada has thought, and taken the first haul; its wheat is claimed
            var save = ColonySave.Write(camp.Colony);
            change(save);
            return new object[] { save.ToJsonString(), reason };
        }
        JsonObject Job(JsonObject s) => s["work"]!["jobs"]!.AsArray().Select(j => j!.AsObject()).First(j => j["claims"]!.AsArray().Count > 0);
        // The worker doing that job, and another.
        JsonObject Worker(JsonObject s, int i)
        {
            var all = s["work"]!["workers"]!.AsArray().Select(w => w!.AsObject()).ToList();
            var doer = all.First(w => w["job"]?.GetValue<long>() == Job(s)["id"]!.GetValue<long>());
            return i == 0 ? doer : all.First(w => w != doer);
        }
        yield return Case(s => Job(s)["claims"] = new JsonArray(), "belongs to no job");
        yield return Case(s => Job(s)["claims"] = new JsonArray(99), "holds claim 99, which does not exist");
        yield return Case(s => Job(s)["kind"] = "Dance", "is not a kind of job");
        yield return Case(s => Job(s)["step"] = "Open", "Open but has a worker");
        yield return Case(s => Job(s)["to"] = 99, "names inventory 99, which does not exist");
        yield return Case(s => Job(s)["good"] = "barley", "hauls an unknown good or nothing");
        yield return Case(s => Worker(s, 0)["job"] = null, "who is not doing it");
        yield return Case(s => Worker(s, 1)["job"] = Job(s)["id"]!.DeepClone(), "which is not theirs");
        yield return Case(s => Worker(s, 0)["arms"] = 1, "is not a carrier's inventory");
        yield return Case(s => Worker(s, 0)["stopped"] = "no", "is not true or false");
        yield return Case(s => s["work"]!["sites"]![0]!["workshop"] = "oven", "\"oven\" is not in the content");
        yield return Case(s => s["work"]!["sites"]![0]!["recipe"] = "bake_bread", "is not one this workshop can work");
        yield return Case(s => s["work"]!["sites"]![0]!["id"] = 99, "was never given out");
    }

    [Theory]
    [MemberData(nameof(BrokenWork))]
    public void A_save_with_broken_work_is_refused(string text, string reason) =>
        Assert.Contains(reason, Assert.Throws<ColonySaveException>(() => ColonySave.Read(text, Real)).Message);

    [Fact]
    public void A_save_with_work_needs_the_content()
    {
        var camp = new Camp();
        Assert.Contains("needs the colony's content", Assert.Throws<ColonySaveException>(
            () => ColonySave.Read(ColonySave.Text(camp.Colony))).Message);
    }
}
