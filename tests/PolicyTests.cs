// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 14b: who may do which work (FP-03: needs and permitted work shape the day without bypassing the player's
/// policy) and why a job is waiting (FP-06: a cause the player can act on, kept apart from the developer's trace).
/// </summary>
public class PolicyTests
{
    static readonly Content Real = Content.Load(Path.Combine(AppContext.BaseDirectory, "content"));
    static readonly Moment Nine = new(Calendar.TickOf(301, 1, 1, 9, 0));

    sealed class Camp
    {
        public readonly ColonyState Colony;
        public Work Work => Colony.Work!;
        public Stocks Stocks => Colony.Stocks;
        public readonly Inventory Carts, Granary;
        public readonly Site Quern;
        public readonly List<Worker> People = new();

        public Camp(int people = 2, Moment? start = null, int rations = 30)
        {
            Colony = new ColonyState(42, start ?? Nine, Real);
            Carts = Stocks.Open("The founders' carts", Holder.Store, 0, 0);
            Carts.Accepts.UnionWith(new[] { "wheat", "ration" });
            Granary = Stocks.Open("Granary", Holder.Store, 0, 70);
            Granary.Accepts.Add("flour");
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "wheat", 50);
            if (rations > 0)
                Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "ration", rations);
            Work.AddSpring(20, 0);
            Quern = Work.AddSite("quern", 140, 0);
            foreach (string name in new[] { "Ada", "Bram", "Cael" }.Take(people))
            {
                var w = Work.AddWorker(name, 0, 0);
                Work.AddBedroll(5 * People.Count, 10, w.Id);
                People.Add(w);
            }
        }

        public Job? JobOf(Worker w) => w.Job is TaskId t ? Work.JobOf(t) : null;
    }

    // ---- FP-03: permitted work --------------------------------------------------------------------------------

    [Fact]
    public void FP03_a_colonist_never_takes_work_they_are_not_allowed()
    {
        var camp = new Camp();
        var (ada, bram) = (camp.People[0], camp.People[1]);
        camp.Work.Permit(ada.Id, Work.HaulWork, false);
        camp.Work.Permit(bram.Id, "quern", false);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 3);
        camp.Colony.Run(2 * Calendar.TicksPerDay, _ =>
        {
            if (camp.JobOf(ada) is Job a)
                Assert.NotEqual(Work.HaulWork, camp.Work.KindOf(a));
            if (camp.JobOf(bram) is Job b)
                Assert.NotEqual("quern", camp.Work.KindOf(b));
        });
        // Bram carried, Ada ground: the order was done between them.
        Assert.Equal(12, camp.Stocks.Total("flour"));
    }

    [Fact]
    public void FP03_permission_does_not_overrule_needs_or_hours()
    {
        // Ada may do anything, but she is urgently hungry: she eats first. Bram is allowed nothing but hauling,
        // and it is evening: he waits for his work hours.
        var camp = new Camp();
        var (ada, bram) = (camp.People[0], camp.People[1]);
        ada.Needs["hunger"].Level = 70;
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        Assert.Equal(PersonalKind.Eat, ada.Personal?.Kind);
        Assert.NotNull(bram.Job);                            // so Bram, free and allowed, took the haul
        var evening = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 18, 0)));
        evening.Work.Order(evening.Quern.Id, "grind_wheat", 1);
        evening.Colony.Run(60);
        Assert.All(evening.People, p => Assert.Null(p.Job));
    }

    [Fact]
    public void Forbidding_work_in_hand_gives_it_to_someone_else()
    {
        var camp = new Camp();
        var (ada, bram) = (camp.People[0], camp.People[1]);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        var job = camp.JobOf(ada)!;
        Assert.Equal(Step.Going, job.Step);                  // taken, not yet picked up
        camp.Work.Permit(ada.Id, Work.HaulWork, false);
        Assert.Null(ada.Job);
        camp.Colony.Run(1);
        Assert.Equal(job.Id, bram.Job);
    }

    [Fact]
    public void Forbidding_a_delivery_in_hand_lets_it_be_finished()
    {
        var camp = new Camp(people: 1);
        var ada = camp.People[0];
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        while (camp.JobOf(ada)?.Step != Step.Carrying)
            camp.Colony.Run(1);
        camp.Work.Permit(ada.Id, Work.HaulWork, false);
        Assert.NotNull(ada.Job);                             // the wheat in her arms is still hers to deliver
        while (camp.Stocks[camp.Quern.Input].Count("wheat") < 5)
            camp.Colony.Run(1);
        Assert.Equal(0, camp.Stocks[ada.Arms].Count("wheat"));
    }

    [Fact]
    public void Only_kinds_of_work_that_exist_can_be_allowed_or_forbidden()
    {
        var camp = new Camp();
        Assert.Equal(new[] { "care", "haul", "quern" }, camp.Work.Kinds);
        var e = Assert.Throws<ArgumentException>(() => camp.Work.Permit(camp.People[0].Id, "baking", false));
        Assert.Contains("there are: care, haul, quern", e.Message);
        camp.Work.Permit(camp.People[0].Id, "quern", false);
        camp.Work.Permit(camp.People[0].Id, Work.HaulWork, false);
        camp.Work.Permit(camp.People[0].Id, "quern", true);
        Assert.Equal(new[] { "haul" }, camp.People[0].Forbidden);   // allowing one leaves the other forbidden
    }

    [Fact]
    public void Caring_is_a_kind_of_its_own()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var (ada, bram) = (camp.People[0], camp.People[1]);
        ada.Needs["thirst"].Level = 100;
        ada.Needs["thirst"].MinutesAtLimit = 24 * 60 - 1;
        camp.Work.Permit(bram.Id, Work.CareWork, false);
        camp.Colony.Run(2);
        Assert.Equal("thirst", ada.Collapsed);
        var care = camp.Work.Jobs.Single(j => j.To == ada.Arms);
        Assert.Equal(Work.CareWork, camp.Work.KindOf(care));
        Assert.Null(care.Worker);
        Assert.Equal("Nobody is allowed to bring food or water to the fallen.", camp.Work.Why(care.Id)!.Player);
        camp.Work.Permit(bram.Id, Work.CareWork, true);
        while (ada.Collapsed != null)
            camp.Colony.Run(1);
    }

    // ---- FP-06: why a job waits -------------------------------------------------------------------------------

    [Fact]
    public void FP06_nobody_allowed_says_so_and_what_to_do()
    {
        var camp = new Camp();
        foreach (var p in camp.People)
            camp.Work.Permit(p.Id, Work.HaulWork, false);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        var why = camp.Work.Why(Assert.Single(camp.Work.Jobs).Id)!;
        Assert.Equal("Nobody is allowed to carry goods.", why.Player);
        Assert.Equal("Allow someone to carry goods in their work settings.", why.NextStep);
        Assert.StartsWith("task ", why.Trace);
        Assert.Contains("no worker permits 'haul' (2 workers)", why.Trace);
    }

    [Fact]
    public void FP06_out_of_work_hours_says_until_when()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 18, 0)));
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        var why = camp.Work.Why(Assert.Single(camp.Work.Jobs).Id)!;
        Assert.Equal("Waiting for someone to carry goods: Ada is not in work hours (recreation) until 08:00; "
                     + "Bram is not in work hours (recreation) until 08:00.", why.Player);
        Assert.Equal("Wait: it is taken in the next minute.", why.NextStep);
        Assert.Contains("block Recreation, work at 08:00", why.Trace);
    }

    [Fact]
    public void FP06_too_hungry_with_nothing_to_eat_says_to_bring_rations()
    {
        var camp = new Camp(people: 1, rations: 0);
        var ada = camp.People[0];
        ada.Needs["hunger"].Level = 70;
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        var why = camp.Work.Why(Assert.Single(camp.Work.Jobs).Id)!;
        Assert.Equal("Waiting for someone to carry goods: Ada is too hungry to work (no travel ration free in any store).", why.Player);
        Assert.Equal("Bring rations to a store.", why.NextStep);
        Assert.Contains("worker 1: urgent hunger 70", why.Trace);
    }

    [Fact]
    public void FP06_busy_stopped_and_collapsed_are_named()
    {
        // Ada takes the first quern's haul; then a second quern asks for wheat, with Bram stopped and Cael fallen.
        var camp = new Camp(people: 3);
        var (ada, bram, cael) = (camp.People[0], camp.People[1], camp.People[2]);
        camp.Work.Stop(bram.Id);
        cael.Needs["fatigue"].Level = 99.99;
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        Assert.NotNull(ada.Job);
        var second = camp.Work.AddSite("quern", 300, 0);
        camp.Work.Order(second.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        var waiting = camp.Work.Jobs.Single(j => j.Worker == null && j.To == second.Input);
        var why = camp.Work.Why(waiting.Id)!;
        Assert.Equal("Waiting for someone to carry goods: Ada is busy (bring wheat to the hand quern); Bram is stopped; "
                     + "Cael has collapsed (fatigue).", why.Player);
        Assert.Equal("Resume Bram.", why.NextStep);
        Assert.Contains("worker 1: busy task", why.Trace);
        Assert.Contains("worker 2: stopped", why.Trace);
        Assert.Contains("worker 3: collapsed fatigue", why.Trace);
    }

    [Fact]
    public void FP06_asleep_and_seeing_to_themselves_are_named()
    {
        var camp = new Camp(people: 2, start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var (ada, bram) = (camp.People[0], camp.People[1]);
        ada.Needs["fatigue"].Level = 90;                      // urgent: she goes to sleep
        bram.X = -500;
        bram.Needs["thirst"].Level = 70;                      // urgent: he walks to the spring
        camp.Colony.Run(1);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);                                   // the quern asks; nobody can take it
        Assert.True(ada.Asleep);
        var why = camp.Work.Why(Assert.Single(camp.Work.Jobs).Id)!;
        Assert.Equal("Waiting for someone to carry goods: Ada is asleep; Bram is going to drink.", why.Player);
    }

    [Fact]
    public void A_job_with_a_worker_or_none_at_all_needs_no_explanation()
    {
        var camp = new Camp();
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        Assert.Null(camp.Work.Why(camp.JobOf(camp.People[0])!.Id));
        Assert.Null(camp.Work.Why(new TaskId(999)));
    }

    [Fact]
    public void The_players_words_carry_no_numbers_of_the_developers()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 18, 0)));
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Colony.Run(1);
        var why = camp.Work.Why(Assert.Single(camp.Work.Jobs).Id)!;
        Assert.DoesNotContain("task", why.Player);
        Assert.DoesNotContain("worker 1", why.Player);
        Assert.DoesNotContain("tick", why.Player);
        Assert.Contains("tick", why.Trace);
    }

    // ---- The save ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_save_keeps_what_each_colonist_may_not_do()
    {
        var camp = new Camp();
        camp.Work.Permit(camp.People[0].Id, "quern", false);
        var loaded = ColonySave.Read(ColonySave.Text(camp.Colony), Real);
        Assert.Equal(new[] { "quern" }, loaded.Work!.Worker(camp.People[0].Id).Forbidden);
        var save = ColonySave.Write(camp.Colony);
        save["work"]!["workers"]![0]!["forbidden"] = new JsonArray("baking");
        Assert.Contains("forbidden names \"baking\", which is no kind of work",
            Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), Real)).Message);
        save["work"]!["workers"]![0]!["forbidden"] = new JsonArray(3);
        Assert.Contains("forbidden holds something that is not text",
            Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), Real)).Message);
    }
}
