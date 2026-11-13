// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 21c: each colonist thinks (decides what to do next) on their own rhythm, every 2 to 7 seconds
/// (colony.json "think_seconds"), drawn once when they join. An order the player gives is thought over the next second.
/// </summary>
public class ThinkTests
{
    static readonly Moment Start = new(Calendar.TickOf(1, 1, 1, 9, 0));

    static Colony.Colony Founded(int seed = 7) =>
        Founding.Found(seed, Start, SanitationTests.Tweaked(), SanitationTests.Map(40, 30)).Colony;

    [Fact]
    public void Each_has_their_own_rhythm_from_two_to_seven_seconds_and_keeps_it()
    {
        var colony = Founded();
        var work = colony.Work!;
        var rhythm = work.Workers.ToDictionary(w => w.Id, w => w.ThinkEvery);
        Assert.All(rhythm.Values, every => Assert.InRange(every, 2, 7));
        // Over five minutes, each thinks exactly on their rhythm (an order would hurry them; none is given here). A think
        // sets the next one a rhythm ahead; `Thought` alone would also count wake-ups (an arrival: since step 25c, idling
        // between jobs walks them about).
        var thoughts = work.Workers.ToDictionary(w => w.Id, _ => new List<long>());
        colony.Run(5 * 60, c =>
        {
            foreach (var w in c.Work!.Workers)
                if (w.NextThink == c.Now.Tick + w.ThinkEvery)
                    thoughts[w.Id].Add(c.Now.Tick);
        });
        foreach (var w in work.Workers)
        {
            Assert.Equal(rhythm[w.Id], w.ThinkEvery);
            var times = thoughts[w.Id];
            Assert.True(times.Count >= 5 * 60 / 7, w.Name + " thought " + times.Count + " times");
            Assert.All(times.Zip(times.Skip(1)), t => Assert.Equal(w.ThinkEvery, t.Second - t.First));
        }
    }

    [Fact]
    public void Not_everyone_thinks_in_the_same_second()
    {
        // The first thinks are spread out, so the whole camp does not decide at once.
        var colony = Founded();
        var firsts = colony.Work!.Workers.Select(w => w.NextThink).ToList();
        Assert.True(firsts.Distinct().Count() > 1, "all first think at " + firsts[0]);
        Assert.All(firsts, t => Assert.InRange(t, Start.Tick + 1, Start.Tick + 7));
    }

    [Fact]
    public void The_same_seed_draws_the_same_rhythms_and_another_seed_others()
    {
        int[] Rhythms(int seed) => Founded(seed).Work!.Workers.Select(w => w.ThinkEvery).ToArray();
        Assert.Equal(Rhythms(7), Rhythms(7));
        Assert.True(Enumerable.Range(8, 5).Any(seed => !Rhythms(seed).SequenceEqual(Rhythms(7))),
                    "five other seeds all drew the rhythms of seed 7");
    }

    [Fact]
    public void Every_order_is_thought_over_the_next_second()
    {
        var colony = Founded();
        var work = colony.Work!;
        var ada = work.Workers.First();
        void Hurried(Action order)
        {
            // Wait until she has just thought, so her next think is at least two seconds off.
            long last = ada.Thought;
            while (ada.Thought == last)
                colony.Run(1);
            Assert.True(ada.NextThink >= colony.Now.Tick + 2);
            order();
            Assert.Equal(colony.Now.Tick + 1, ada.NextThink);
            colony.Run(1);
            Assert.Equal(colony.Now.Tick, ada.Thought);
            Assert.Equal(colony.Now.Tick + ada.ThinkEvery, ada.NextThink);   // and then her own rhythm again
        }
        Hurried(() => work.Stop(ada.Id));
        Hurried(() => work.Resume(ada.Id));
        Hurried(() => work.Permit(ada.Id, work.Kinds.First(), false));
        Hurried(() => work.SetSchedule(ada.Id, colony.Content!.Schedules.Keys.First()));
    }

    [Fact]
    public void A_new_order_is_taken_up_within_a_colonists_rhythm_not_on_the_whole_minute()
    {
        // Planned a few seconds after the minute: the wall asks for its timber at once, and someone takes the haul
        // at their next think (7 seconds at the most).
        var colony = Founded();
        var work = colony.Work!;
        colony.Run(3);
        var (wall, placed) = work.Plan("wall", 22, 14);
        Assert.True(placed.Ok, placed.Why);
        Assert.Contains(work.Jobs, j => j.Kind == JobKind.Haul && j.To == wall!.Stock);
        long planned = colony.Now.Tick;
        while (!work.Jobs.Any(j => j.Kind == JobKind.Haul && j.To == wall!.Stock && j.Worker != null))
            colony.Run(1);
        Assert.InRange(colony.Now.Tick - planned, 1, 7);
    }

    [Fact]
    public void A_save_keeps_each_ones_rhythm_and_where_they_are_in_it()
    {
        var colony = Founded();
        colony.Run(123);
        var copy = ColonySave.Read(ColonySave.Text(colony), colony.Content!);
        Assert.Equal(colony.Work!.Workers.Select(w => (w.ThinkEvery, w.NextThink, w.Thought)),
                     copy.Work!.Workers.Select(w => (w.ThinkEvery, w.NextThink, w.Thought)));
    }

    [Theory]
    [InlineData("[0, 1, 0]")]          // no rhythm
    [InlineData("[3, 0, 0]")]          // next think before now
    [InlineData("[3, 1, 9999999999]")] // last think after now
    [InlineData("[3, 1]")]
    [InlineData("\"soon\"")]
    public void A_save_with_a_broken_think_is_refused(string think)
    {
        var colony = Founded();
        var save = ColonySave.Write(colony);
        var parsed = JsonNode.Parse(think)!;
        if (parsed is JsonArray a && a.Count == 3 && (long)a[1]! == 1)
            a[1] = colony.Now.Tick + 1;                                       // a good next think, to test the others
        save["work"]!["workers"]![0]!["think"] = parsed;
        Assert.Contains(".think is not [every", Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), colony.Content!)).Message);
    }
}
