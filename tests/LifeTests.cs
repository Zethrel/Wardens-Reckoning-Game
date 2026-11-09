// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 14a: the colonists' needs and day. Hunger, thirst and fatigue; the Standard Day's blocks; eating rations,
/// drinking from the spring, sleeping on bedrolls; urgent needs waiting for a delivery in hand, critical ones not;
/// collapse and care. Also FP-01's schedule part: work goes on only in Work hours.
/// </summary>
public class LifeTests
{
    static readonly Content Real = Content.Load(Path.Combine(AppContext.BaseDirectory, "content"));
    static readonly Moment Morning = new(Calendar.TickOf(301, 1, 1, 7, 0));

    /// <summary>The founders' camp: carts with wheat and rations, a spring, a bedroll each, a quern.</summary>
    sealed class Camp
    {
        public readonly ColonyState Colony;
        public Work Work => Colony.Work!;
        public Stocks Stocks => Colony.Stocks;
        public readonly Inventory Carts, Granary, Spring;
        public readonly Site Quern;
        public readonly List<Worker> People = new();

        public Camp(int people = 1, int rations = 30, Content? content = null, Moment? start = null)
        {
            Colony = new ColonyState(42, start ?? Morning, content ?? Real);
            Carts = Stocks.Open("The founders' carts", Holder.Store, 0, 0);
            Carts.Accepts.UnionWith(new[] { "wheat", "ration" });
            Granary = Stocks.Open("Granary", Holder.Store, 0, 70);
            Granary.Accepts.Add("flour");
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "wheat", 50);
            if (rations > 0)
                Stocks.Arrive(Colony.Ids.Next<OperationId>(), Colony.Now.Tick, Carts.Id, "ration", rations);
            Spring = Work.AddSpring(20, 0);
            Quern = Work.AddSite("quern", 140, 0);
            foreach (string name in new[] { "Ada", "Bram", "Cael" }.Take(people))
            {
                var w = Work.AddWorker(name, 0, 0);
                Work.AddBedroll(5 * People.Count, 10, w.Id);
                People.Add(w);
            }
        }

        public void Run(int ticks, Action<ColonyState>? each = null) => Colony.Run(ticks, each);

        public int RunUntil(Func<bool> done, int most = 4 * Calendar.TicksPerDay)
        {
            for (int i = 1; i <= most; i++)
            {
                Colony.Run(1);
                if (done())
                    return i;
            }
            throw new Xunit.Sdk.XunitException("not done in " + most + " minutes");
        }

        public double Need(Worker w, string need) => w.Needs[need].Level;
        public void Set(Worker w, string need, double level, int atLimit = 0)
        {
            w.Needs[need].Level = level;
            w.Needs[need].MinutesAtLimit = atLimit;
        }

        public Job? JobOf(Worker w) => w.Job is TaskId t ? Work.JobOf(t) : null;

        public void Conserved()
        {
            foreach (string good in new[] { "wheat", "flour", "ration", "water" })
                Assert.Equal(Stocks.Accounted(good), Stocks.Total(good));
        }
    }

    // ---- The day --------------------------------------------------------------------------------------------

    [Fact]
    public void A_standard_day_has_two_or_three_meals_a_night_on_the_bedroll_and_work_in_work_hours()
    {
        var camp = new Camp();
        var ada = camp.People[0];
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 10);
        var meals = new Dictionary<long, int>();
        var drinks = new Dictionary<long, int>();
        int ledgerSeen = camp.Stocks.Ledger.Count;
        camp.Run(3 * Calendar.TicksPerDay, c =>
        {
            foreach (var e in camp.Stocks.Ledger.Skip(ledgerSeen).Where(e => e.Change == Change.Used))
            {
                var counts = e.Good == "ration" ? meals : e.Good == "water" ? drinks : null;
                if (counts != null)
                    counts[c.Now.AbsoluteDay] = counts.GetValueOrDefault(c.Now.AbsoluteDay) + 1;
            }
            ledgerSeen = camp.Stocks.Ledger.Count;
            // Work only in Work hours (FP-01's schedule part): a grinding goes on only then, and a job is taken only then.
            foreach (var job in camp.Work.Jobs.Where(j => j.Worker != null && j.Step == Step.Working))
                Assert.Equal(Block.Work, camp.Work.BlockOf(camp.Work.Worker(job.Worker!.Value)));
            if (c.Now.Hour == 3)
                Assert.Equal("asleep on a bedroll", camp.Work.Doing(ada.Id));
            Assert.Null(ada.Collapsed);
        });
        Assert.Equal(3, meals.Count);
        Assert.All(meals.Values, n => Assert.InRange(n, 2, 3));
        Assert.All(drinks.Values, n => Assert.InRange(n, 2, 6));
        Assert.True(camp.Stocks.Total("flour") >= 4, "work was done: " + camp.Stocks.Total("flour") + " flour");
        camp.Conserved();
    }

    [Fact]
    public void FP01_a_job_is_taken_only_in_a_work_hour()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 17, 0)));   // recreation
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Run(60);
        Assert.Single(camp.Work.Jobs);
        Assert.Null(camp.People[0].Job);
        // Next morning at 08:00 the haul is taken.
        camp.RunUntil(() => camp.People[0].Job != null);
        Assert.Equal((8, 0), (camp.Colony.Now.Hour, camp.Colony.Now.Minute));
    }

    [Fact]
    public void A_grinding_is_put_down_when_the_meal_hour_comes()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 10, 0)));
        var ada = camp.People[0];
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunUntil(() => camp.JobOf(ada)?.Step == Step.Working);
        camp.RunUntil(() => camp.Colony.Now.Hour == 12);
        var craft = camp.Work.Jobs.Single(j => j.Kind == JobKind.Craft);
        Assert.Null(craft.Worker);
        Assert.Equal(Step.Open, craft.Step);
        Assert.Single(craft.Claims);                         // its wheat stays claimed
        Assert.True(craft.Left is > 0 and < 180, "its progress is kept: " + craft.Left);
    }

    [Fact]
    public void The_spring_fills_each_morning_and_no_more()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 12, 0)));
        Assert.Equal(200, camp.Spring.Count("water"));
        camp.RunUntil(() => camp.Colony.Now.Hour == 5 && camp.Colony.Now.Minute == 59);
        int drunk = 200 - camp.Spring.Count("water");
        Assert.True(drunk > 0);
        camp.Run(1);
        Assert.Equal(200, camp.Spring.Count("water"));
        var arrivals = camp.Stocks.Ledger.Where(e => e.Change == Change.Arrived && e.Good == "water").ToList();
        Assert.Equal(new[] { 200, drunk }, arrivals.Select(e => e.Amount));
        camp.Conserved();
    }

    [Fact]
    public void Without_a_bedroll_they_sleep_on_the_ground_and_rest_slower()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 22, 0)));
        var ada = camp.People[0];
        var bram = camp.Work.AddWorker("Bram", 0, 0);          // no bedroll
        camp.Set(ada, "fatigue", 90);
        camp.Set(bram, "fatigue", 90);
        camp.RunUntil(() => camp.Colony.Now.Hour == 3);
        Assert.Equal("asleep on a bedroll", camp.Work.Doing(ada.Id));
        Assert.Equal("asleep on the ground", camp.Work.Doing(bram.Id));
        Assert.True(camp.Need(ada, "fatigue") < camp.Need(bram, "fatigue"));
    }

    [Fact]
    public void A_sleeper_wakes_after_the_sleep_hours_once_rested()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 23, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "fatigue", 60);
        camp.RunUntil(() => ada.Asleep);
        camp.RunUntil(() => !ada.Asleep);
        Assert.Equal((7, 0), (camp.Colony.Now.Hour, camp.Colony.Now.Minute));
    }

    [Fact]
    public void A_sleeper_wakes_for_critical_thirst()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 2, 1, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "fatigue", 60);
        camp.RunUntil(() => ada.Asleep);
        camp.Set(ada, "thirst", 95);
        camp.Run(1);
        Assert.False(ada.Asleep);
        camp.RunUntil(() => camp.Need(ada, "thirst") < 50);
        Assert.Equal(1, camp.Colony.Now.Hour);                // she drank in the night, without waiting for morning
    }

    // ---- Urgent and critical --------------------------------------------------------------------------------

    [Fact]
    public void Urgent_hunger_waits_for_the_delivery_in_hand()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "hunger", 20);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunUntil(() => camp.JobOf(ada)?.Step == Step.Carrying);
        camp.Set(ada, "hunger", 70);                          // urgent, not critical
        camp.Run(1);
        Assert.Equal(Step.Carrying, camp.JobOf(ada)!.Step);   // she carries on
        camp.RunUntil(() => ada.Personal?.Kind == PersonalKind.Eat);
        Assert.Equal(5, camp.Stocks[camp.Quern.Input].Count("wheat"));   // the wheat was delivered first
        Assert.Null(ada.Job);
    }

    [Fact]
    public void Critical_hunger_drops_the_work_but_the_cargo_stays_with_her()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var ada = camp.People[0];
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.RunUntil(() => camp.JobOf(ada)?.Step == Step.Carrying);
        var job = camp.JobOf(ada)!;
        camp.Set(ada, "hunger", 92);
        camp.Run(1);
        Assert.Equal(PersonalKind.Eat, ada.Personal?.Kind);
        Assert.Equal(job.Id, ada.Job);                        // still hers
        Assert.Equal(5, camp.Stocks[ada.Arms].Count("wheat"));
        camp.RunUntil(() => camp.Stocks[camp.Quern.Input].Count("wheat") == 5);
        Assert.True(camp.Need(ada, "hunger") < 92);
        camp.Conserved();
    }

    [Fact]
    public void Peckish_colonists_eat_at_mealtimes_but_not_at_work()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var ada = camp.People[0];
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 3);
        camp.Set(ada, "hunger", 20);                          // peckish
        camp.Run(60);
        Assert.Equal(0, camp.Stocks.Ledger.Count(e => e.Change == Change.Used && e.Good == "ration"));
        camp.RunUntil(() => camp.Stocks.Ledger.Any(e => e.Change == Change.Used && e.Good == "ration"));
        Assert.Equal(12, camp.Colony.Now.Hour);
    }

    [Fact]
    public void Nobody_takes_work_while_urgently_tired()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "fatigue", 87);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Run(1);
        Assert.Null(ada.Job);
        Assert.NotEqual(Block.Sleep, camp.Work.BlockOf(ada));
        camp.RunUntil(() => ada.Asleep);
        Assert.Equal("asleep on a bedroll", camp.Work.Doing(ada.Id));
    }

    [Fact]
    public void The_levels_of_a_need_begin_exactly_at_their_thresholds()
    {
        var hunger = Real.Hunger;                             // peckish 15, wants 35, urgent 65, critical 90
        Assert.Equal(new[] { 0, 1, 1, 2, 2, 3, 3, 4, 4 },
            new[] { 14.99, 15, 34.99, 35, 64.99, 65, 89.99, 90, 100 }.Select(l => Work.LevelOf(hunger, l)));
    }

    [Fact]
    public void Nobody_takes_work_while_urgently_hungry_even_with_nothing_to_eat()
    {
        var camp = new Camp(rations: 0, start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "hunger", 70);
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        camp.Run(30);
        Assert.Null(ada.Job);
        Assert.Equal("no travel ration free in any store", ada.Lacking);
    }

    [Fact]
    public void A_peckish_colonist_eats_when_the_meal_hour_comes()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 11, 30)));
        var ada = camp.People[0];
        camp.Set(ada, "hunger", 16);                          // peckish, and still below "wants" at noon
        camp.RunUntil(() => ada.Personal?.Kind == PersonalKind.Eat);
        Assert.Equal((12, 0), (camp.Colony.Now.Hour, camp.Colony.Now.Minute));
    }

    [Fact]
    public void A_colonist_with_no_work_eats_when_hungry_even_in_work_hours()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "hunger", 40);
        camp.Run(1);
        Assert.Equal(PersonalKind.Eat, ada.Personal?.Kind);
    }

    [Fact]
    public void A_tired_colonist_rests_in_free_time()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 18, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "fatigue", 75);                         // wants rest, not urgent
        camp.RunUntil(() => ada.Asleep, most: 60);
        // On her own bedroll, where it lies.
        var bed = camp.Work.Bedrolls.Single(b => b.Owner == ada.Id);
        Assert.Equal((bed.X, bed.Y), (ada.X, ada.Y));
        Assert.True(ada.OnBedroll);
    }

    [Fact]
    public void Eating_takes_its_twenty_minutes_after_the_walk()
    {
        // 50 m from the carts: a minute's walk; then 20 minutes eating: the ration is used in minute 21.
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 17, 0)));
        var ada = camp.People[0];
        ada.X = -50;
        camp.Set(ada, "hunger", 40);
        int minutes = camp.RunUntil(() => camp.Stocks.Ledger.Any(e => e.Change == Change.Used && e.Good == "ration"));
        Assert.Equal(21, minutes);
    }

    [Fact]
    public void Time_at_the_limit_starts_again_once_the_need_is_eased()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 17, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "hunger", 100, atLimit: 600);
        camp.RunUntil(() => camp.Need(ada, "hunger") < 100);
        camp.Run(1);                                          // counted each minute, as the need grows
        Assert.Equal(0, ada.Needs["hunger"].MinutesAtLimit);
    }

    // ---- Collapse and care ------------------------------------------------------------------------------------

    [Fact]
    public void Starved_for_three_days_she_collapses_and_is_fed_back_on_her_feet()
    {
        var camp = new Camp(people: 2, rations: 0);
        var (ada, bram) = (camp.People[0], camp.People[1]);
        camp.Set(ada, "hunger", 100, atLimit: 72 * 60 - 2);
        camp.Run(1);
        Assert.Null(ada.Collapsed);
        camp.Run(1);
        Assert.Equal("hunger", ada.Collapsed);
        Assert.Equal("collapsed (hunger)", camp.Work.Doing(ada.Id));
        // Rations arrive; in work hours Bram carries one to her, and she eats it at once.
        camp.Stocks.Arrive(camp.Colony.Ids.Next<OperationId>(), camp.Colony.Now.Tick, camp.Carts.Id, "ration", 10);
        camp.RunUntil(() => ada.Collapsed == null);
        Assert.Contains(camp.Stocks.Ledger, e => e.Change == Change.Moved && e.Good == "ration" && e.To == ada.Arms);
        Assert.True(camp.Need(ada, "hunger") < Real.Hunger.Urgent);
        camp.Conserved();
    }

    [Fact]
    public void Exhausted_she_falls_asleep_where_she_stands()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 9, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "fatigue", 99.99);
        camp.Run(1);
        Assert.Equal("fatigue", ada.Collapsed);
        Assert.Equal("collapsed (fatigue)", camp.Work.Doing(ada.Id));
        Assert.True(ada.Asleep);
        Assert.False(ada.OnBedroll);
        int minutes = camp.RunUntil(() => ada.Collapsed == null);
        // From 100 to below 70 on the ground, 10 an hour: three hours.
        Assert.InRange(minutes, 179, 181);
    }

    [Fact]
    public void With_nothing_to_eat_she_says_so()
    {
        var camp = new Camp(rations: 0, start: new Moment(Calendar.TickOf(301, 1, 1, 12, 0)));
        var ada = camp.People[0];
        camp.Set(ada, "hunger", 50);
        camp.Run(1);
        Assert.Equal("no travel ration free in any store", ada.Lacking);
    }

    // ---- Words, schedules and the save ----------------------------------------------------------------------

    [Fact]
    public void What_they_are_doing_is_said_in_words()
    {
        var camp = new Camp(start: new Moment(Calendar.TickOf(301, 1, 1, 17, 0)));
        var ada = camp.People[0];
        Assert.Equal("at leisure", camp.Work.Doing(ada.Id));
        ada.X = -200;                                         // far enough from the spring to be seen walking
        camp.Set(ada, "thirst", 40);
        camp.Run(1);
        Assert.Equal("going to drink", camp.Work.Doing(ada.Id));
        camp.RunUntil(() => ada.Personal?.Left != null);
        Assert.Equal("drinking", camp.Work.Doing(ada.Id));
        camp.Work.Stop(ada.Id);
        camp.RunUntil(() => ada.Personal == null);
        Assert.Equal("stopped", camp.Work.Doing(ada.Id));
    }

    [Fact]
    public void A_schedule_can_be_changed_but_only_to_one_that_exists()
    {
        var camp = new Camp();
        Assert.Throws<ArgumentException>(() => camp.Work.SetSchedule(camp.People[0].Id, "night_shift"));
        camp.Work.SetSchedule(camp.People[0].Id, "standard_day");
        Assert.Equal("standard_day", camp.People[0].Schedule);
    }

    [Fact]
    public void A_save_in_the_middle_of_a_meal_goes_on_the_same()
    {
        var straight = new Camp(people: 2);
        var halves = new Camp(people: 2);
        foreach (var camp in new[] { straight, halves })
            camp.Work.Order(camp.Quern.Id, "grind_wheat", 6);
        int minutes = halves.RunUntil(() => halves.People.Any(p => p.Personal?.Kind == PersonalKind.Eat && p.Personal.Left != null));
        straight.Run(minutes);
        var loaded = ColonySave.Read(ColonySave.Text(halves.Colony), Real);
        Assert.Equal(ColonySave.Text(halves.Colony), ColonySave.Text(loaded));
        for (int i = 0; i < 2 * Calendar.TicksPerDay; i++)
        {
            straight.Colony.Run(1);
            loaded.Run(1);
        }
        Assert.Equal(ColonySave.Fingerprint(straight.Colony), ColonySave.Fingerprint(loaded));
    }

    public static IEnumerable<object[]> BrokenLives()
    {
        object[] Case(Action<JsonObject> change, string reason)
        {
            var camp = new Camp(people: 2, start: new Moment(Calendar.TickOf(301, 1, 1, 17, 0)));
            camp.Set(camp.People[0], "thirst", 40);
            camp.Run(1);                                       // Ada goes to drink: she holds a claim
            var save = ColonySave.Write(camp.Colony);
            change(save);
            return new object[] { save.ToJsonString(), reason };
        }
        JsonObject Ada(JsonObject s) => s["work"]!["workers"]![0]!.AsObject();
        yield return Case(s => Ada(s)["schedule"] = "night_shift", "schedule \"night_shift\" is not in the content");
        yield return Case(s => Ada(s)["needs"]!["hunger"] = new JsonArray(150, 0), "needs.hunger is not a level");
        yield return Case(s => Ada(s)["collapsed"] = "boredom", "collapsed is not a need");
        yield return Case(s => Ada(s)["asleep"] = "yes", "asleep is not true or false");
        yield return Case(s => Ada(s)["personal"]!["kind"] = "Sing", "personal.kind is not eating, drinking, sleeping or washing");
        yield return Case(s => Ada(s)["personal"]!["claim"] = 99, "personal.claim 99 does not exist");
        yield return Case(s => Ada(s)["personal"]!["claim"] = null, "eating and drinking hold a claim");
        yield return Case(s => s["work"]!["bedrolls"]![0]!["owner"] = 99, "owner 99 is no colonist");
        yield return Case(s => s["work"]!["springs"] = new JsonArray(new JsonArray(99, "Spring", 200, false)), "work.springs names 99");
        yield return Case(s => s["work"]!["springs"]![0]![2] = -1, "litres a day, which cannot be");
        yield return Case(s => s["work"]!["springs"]![0]![1] = "Fountain", "the kind \"Fountain\", which is no kind of water");
        yield return Case(s => s["work"]!["springs"]![0]![3] = "no", "a frost that is not true or false");
        yield return Case(s => s["work"]!["springs"] = new JsonArray(5), "work.springs holds something that is not [store, kind, litres, frozen]");
        yield return Case(s => s["work"]!.AsObject().Remove("cold"), "work.cold is not true or false");
        yield return Case(s => Ada(s)["legs"] = 0, "legs is 0, which should be above 0 and at most 1");
        yield return Case(s => Ada(s)["legs"] = 1.5, "legs is 1.5");
    }

    [Theory]
    [MemberData(nameof(BrokenLives))]
    public void A_save_with_a_broken_life_is_refused(string text, string reason) =>
        Assert.Contains(reason, Assert.Throws<ColonySaveException>(() => ColonySave.Read(text, Real)).Message);
}
