// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 24a, the hybrid clock (library Review 41): waking and deciding are kept apart. A colonist's timed step
/// (loading, unloading, working) ends on its own tick, worked out from the work left at their pace, whatever their
/// rhythm of deciding; arriving wakes them the tick after; loading carries straight on into carrying; a wake-up never
/// chooses new work; a pace that changes, or a need turning critical, wakes them; each step happens once; and a save
/// keeps both deadlines.
///
/// The camp is that of WorkTests (carts at the camp, a quern 140 m away, no ground, no needs, work all day), at the
/// old walking pace (see TestContent), but deciding at the real rhythm: every 2 to 7 real seconds, 48 to 168 ticks.
/// So a thing that happens on the tick it should cannot be a think that happened to fall there.
/// </summary>
public class ClockTests
{
    static readonly Moment Start = new(Calendar.TickOf(1, 1, 1, 9, 0));

    // `needs`: whether needs grow (as the real content has them); without, they stay where they are put.
    static Content Slow(bool needs = false)
    {
        var files = TestContent.NotLearning(TestContent.Files());
        var colony = JsonNode.Parse(files["colony.json"]!)!.AsObject();
        colony["think_real_seconds"] = new JsonArray(2, 7);           // 48 to 168 ticks: the real rhythm
        files["colony.json"] = colony.ToJsonString();
        if (!needs)
        {
            var n = JsonNode.Parse(files["needs.json"]!)!.AsObject();
            foreach (var (_, rule) in n["needs"]!.AsObject())
                rule!["per_hour"] = 0;
            files["needs.json"] = n.ToJsonString();
        }
        var schedules = JsonNode.Parse(files["schedules.json"]!)!.AsObject();
        schedules["templates"]![0]!["hours"] = new JsonArray(Enumerable.Repeat((JsonNode?)"work", 24).Select(h => h?.DeepClone()).ToArray());
        files["schedules.json"] = schedules.ToJsonString();
        return Content.Read(files);
    }

    sealed class Camp
    {
        public readonly ColonyState Colony;
        public Work Work => Colony.Work!;
        public Stocks Stocks => Colony.Stocks;
        public readonly Inventory Carts, Granary;
        public readonly Site Quern;
        public readonly Worker Ada;

        public Camp(bool needs = false)
        {
            Colony = new ColonyState(42, Start, Slow(needs));
            Carts = Stocks.Open("The founders' carts", Holder.Store, 0, 0);
            Carts.Accepts.UnionWith(new[] { "wheat", "ration" });
            Granary = Stocks.Open("Granary", Holder.Store, 0, 70);
            Granary.Accepts.Add("flour");
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Start.Tick, Carts.Id, "wheat", 50);
            Stocks.Arrive(Colony.Ids.Next<OperationId>(), Start.Tick, Carts.Id, "ration", 10);
            Quern = Work.AddSite("quern", 140, 0);
            Ada = Work.AddWorker("Ada", 0, 0);
        }

        public Job? JobOf(Worker w) => w.Job is TaskId t ? Work.JobOf(t) : null;

        /// <summary>Runs a tick at a time until `done` (at most `most` ticks); the tick it was first true.</summary>
        public long Until(Func<bool> done, int most = 4 * Calendar.TicksPerHour)
        {
            for (int i = 0; i < most && !done(); i++)
                Colony.Run(1);
            Assert.True(done(), "not done in " + most + " ticks");
            return Colony.Now.Tick;
        }
    }

    [Fact]
    public void The_rhythm_is_the_real_one()
    {
        var camp = new Camp();
        Assert.InRange(camp.Ada.ThinkEvery, 48, 168);
    }

    [Fact]
    public void Each_step_of_a_haul_happens_on_its_own_tick()
    {
        var camp = new Camp();
        var ada = camp.Ada;
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        // She takes the haul at her think, standing at the carts: loading begins at once, 5 ticks.
        long loading = camp.Until(() => camp.JobOf(ada)?.Step == Step.Loading);
        Assert.Equal(ada.Thought, loading);
        Assert.Equal(loading + 5, ada.ActionDue);
        // Loaded on its tick, and carrying straight on: the wheat in her arms and her heading set, in the same tick.
        long loaded = camp.Until(() => camp.Stocks[ada.Arms].Count("wheat") > 0);
        Assert.Equal(loading + 5, loaded);
        Assert.Equal(Step.Carrying, camp.JobOf(ada)!.Step);
        Assert.Equal((140.0, 0.0, false), ada.Heading);
        Assert.Null(ada.ActionDue);                                     // walking: her arrival will wake her
        // 140 m at the old pace (7/6 m a tick) take 120 ticks; arriving wakes her the tick after; unloading, 5 more.
        long arrived = camp.Until(() => ada.Heading == null);
        Assert.Equal(loaded + 120, arrived);
        Assert.Equal(arrived + 1, ada.ActionDue);
        long unloaded = camp.Until(() => camp.Stocks[camp.Quern.Input].Count("wheat") == 5);
        Assert.Equal(arrived + 1 + 5, unloaded);
        Assert.Null(ada.Job);
        // The quern asks at once; but she was only woken to finish her haul, so she takes the grinding at her think.
        var grind = Assert.Single(camp.Work.Jobs, j => j.Kind == JobKind.Craft);
        Assert.Equal(Step.Open, grind.Step);
        long think = ada.NextThink;
        Assert.True(think > unloaded, "her think fell on the tick she unloaded: the test proves less (" + think + ")");
        camp.Colony.Run(think - unloaded - 1);
        Assert.Equal(Step.Open, grind.Step);
        camp.Colony.Run(1);
        Assert.Equal(ada.Id, grind.Worker);
    }

    [Theory]
    [InlineData(40, 10800)]                                            // the usual pace: 180 minutes
    [InlineData(70, 8640)]                                             // 1.25 times as fast
    [InlineData(0, 21600)]                                             // half as fast
    public void Work_ends_on_the_tick_its_pace_says(double proficiency, long ticks)
    {
        var camp = new Camp();
        camp.Ada.Skills["food_preparation"].Proficiency = proficiency;
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        long working = camp.Until(() => camp.JobOf(camp.Ada)?.Step == Step.Working);
        Assert.Equal(working + ticks, camp.Ada.ActionDue);
        long made = camp.Until(() => camp.Stocks[camp.Quern.Output].Count("flour") > 0, 30000);
        Assert.Equal(working + ticks, made);
    }

    [Fact]
    public void A_pace_that_changes_wakes_her_and_keeps_the_work_done()
    {
        var camp = new Camp();
        var ada = camp.Ada;
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        long working = camp.Until(() => camp.JobOf(ada)?.Step == Step.Working);
        // An illness that shows on a whole minute an hour in, while she works, and lasts a day.
        long shows = (working / 60 + 60) * 60;
        ada.Illness = new Illness(camp.Colony.Content!.UpsetStomach.Name, shows, shows + Calendar.TicksPerDay);
        Assert.Equal(working + 10800, ada.ActionDue);                   // worked out while she was well
        camp.Colony.Run(shows - camp.Colony.Now.Tick);
        Assert.Equal(shows, ada.Thought);                               // woken as it showed
        var grind = camp.JobOf(ada)!;
        Assert.Equal(10800 - (shows - working), grind.Left);          // the hour before, at the usual pace
        // The rest at 70%: the fewest ticks whose 0.7 make up what is left.
        long rest = (long)Math.Ceiling(grind.Left / 0.7 - 1e-9);
        Assert.Equal(shows + rest, ada.ActionDue);
        long made = camp.Until(() => camp.Stocks[camp.Quern.Output].Count("flour") > 0, 30000);
        Assert.Equal(shows + rest, made);
    }

    [Fact]
    public void A_need_turning_critical_wakes_her_at_once()
    {
        var camp = new Camp(needs: true);
        var ada = camp.Ada;
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 1);
        long working = camp.Until(() => camp.JobOf(ada)?.Step == Step.Working);
        var hunger = camp.Colony.Content!.Hunger;
        // Just below critical: the next whole minute takes her over it.
        ada.Needs["hunger"].Level = hunger.Critical - 0.01;
        long minute = (camp.Colony.Now.Tick / 60 + 1) * 60;
        Assert.True(ada.NextThink > minute + 1, "her think falls too soon for the test to prove anything");
        camp.Colony.Run(minute - camp.Colony.Now.Tick);
        Assert.Equal(minute + 1, ada.NextThink);                        // urged
        camp.Colony.Run(1);
        Assert.Null(ada.Job);                                           // the grinding put down (with its work kept)
        Assert.NotNull(ada.Personal);                                   // and off to eat
        // Every tick worked up to the one she put it down on counts, though she was last processed some ticks before.
        Assert.Equal(10800 - (minute + 1 - working), camp.Work.Jobs.Single(j => j.Kind == JobKind.Craft).Left);
    }

    [Fact]
    public void Each_move_of_goods_is_recorded_once()
    {
        var camp = new Camp();
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 2);
        camp.Until(() => camp.Granary.Count("flour") == 8 && camp.Work.Jobs.Count == 0, 40000);
        var moves = camp.Stocks.Ledger.Where(e => e.Change == Change.Moved).ToList();
        // Two hauls of wheat (carts to arms, arms to quern) and two of flour (quern to arms, arms to granary).
        Assert.Equal(2, moves.Count(e => e.Good == "wheat" && e.From == camp.Carts.Id));
        Assert.Equal(2, moves.Count(e => e.Good == "wheat" && e.To == camp.Quern.Input));
        Assert.Equal(2, moves.Count(e => e.Good == "flour" && e.From == camp.Quern.Output));
        Assert.Equal(2, moves.Count(e => e.Good == "flour" && e.To == camp.Granary.Id));
        Assert.Equal(camp.Stocks.Ledger.Count, camp.Stocks.Ledger.Select(e => (e.Operation, e.Good, e.From, e.To)).Distinct().Count());
        Assert.Equal(40, camp.Carts.Count("wheat"));
    }

    [Fact]
    public void A_save_keeps_both_deadlines_and_goes_on_as_if_never_saved()
    {
        // Saved every 11 ticks through two batches: loading, walking, unloading and grinding all caught.
        var camp = new Camp();
        camp.Work.Order(camp.Quern.Id, "grind_wheat", 2);
        long end = Start.Tick + 8 * Calendar.TicksPerHour;
        var copies = new List<Colony.Colony>();
        bool apart = false;
        camp.Colony.Run(end - Start.Tick, c =>
        {
            if ((c.Now.Tick - Start.Tick) % 11 != 0)
                return;
            var w = c.Work!.Workers.First();
            apart |= w.ActionDue is long due && due != w.NextThink;
            string text = ColonySave.Text(c);
            var copy = ColonySave.Read(text, c.Content!);
            Assert.Equal(text, ColonySave.Text(copy));
            Assert.Equal(w.ActionDue, copy.Work!.Workers.First().ActionDue);
            copies.Add(copy);
        });
        Assert.True(apart, "no save caught an action deadline apart from the think");
        string fingerprint = ColonySave.Fingerprint(camp.Colony);
        foreach (var copy in copies.Where((_, i) => i % 9 == 0))
        {
            copy.Run(end - copy.Now.Tick);
            Assert.Equal(fingerprint, ColonySave.Fingerprint(copy));
        }
    }

    [Theory]
    [InlineData(5.0, 120)]
    [InlineData(0.5, 12)]
    [InlineData(1.0 / 48, 1)]                                          // half a tick: rounded away from zero
    [InlineData(0.01, 1)]                                              // above 0: at least a tick
    [InlineData(0.0, 0)]
    [InlineData(7.0, 168)]
    public void Real_seconds_become_ticks_as_the_library_says(double seconds, int ticks) =>
        Assert.Equal(ticks, Calendar.TicksOf(seconds));

    [Fact]
    public void The_content_as_shipped_walks_and_loads_at_the_hybrid_pace()
    {
        var real = Content.Read(TestContent.RealFiles());
        Assert.Equal(1.2 / 24, real.WalkMetresPerTick);
        Assert.Equal((120, 120, 48, 168), (real.PickupTicks, real.DropTicks, real.ThinkFrom, real.ThinkTo));
    }
}
