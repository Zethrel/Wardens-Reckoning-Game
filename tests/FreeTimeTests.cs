// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 25a: free time (design library Review 42: Colonist Daily Life and Needs §11.1, with its verification table).
/// Founders on a small meadow with a few trees and a stream, in their Recreation hours: they choose activities of
/// their own, ease their Recreation only while at them, stop cleanly when the block ends, wait before starting an
/// interrupted activity again, and do it all the same way every run and across a save.
/// </summary>
public class FreeTimeTests
{
    internal static Moment At(int hour) => new(Calendar.TickOf(1, 1, 1, hour, 0));

    // A meadow of 40 x 30: trees at (10..12, 5) if `trees`, a stream down x = 35 if `water`.
    static LocalMap Meadow(bool trees = true, bool water = true)
    {
        const int size = 40;
        var ground = Enumerable.Range(0, size).Select(_ => new string(Enumerable.Range(0, size).Select(x => water && x == 35 ? 'w' : 'g').ToArray()));
        var things = Enumerable.Range(0, size).Select(y => new string(Enumerable.Range(0, size).Select(x => trees && y == 5 && x is >= 10 and <= 12 ? 't' : '.').ToArray()));
        return LocalMap.Parse(new JsonObject
        {
            ["format"] = "the-north-local", ["version"] = 1, ["size"] = size, ["biome"] = "meadow", ["water"] = water ? "stream" : null,
            ["surfaces"] = new JsonObject { ["g"] = "grass", ["w"] = "fresh_water" },
            ["things"] = new JsonObject { ["t"] = "tree" },
            ["ground"] = new JsonArray(ground.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
            ["on_ground"] = new JsonArray(things.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
        }, "meadow");
    }

    internal static Colony.Colony Found(int hour, bool trees = true, bool water = true, int seed = 7) =>
        Founding.Found(seed, At(hour), TestContent.Real, Meadow(trees, water)).Colony;

    // Runs tick by tick for `ticks`, noting each outing begun: (tick, colonist, activity).
    static List<(long Tick, string Who, string Activity)> Watch(Colony.Colony colony, int ticks)
    {
        var seen = new List<(long, string, string)>();
        var was = new Dictionary<ColonistId, Outing?>();
        for (int i = 0; i < ticks; i++)
        {
            colony.Run(1);
            foreach (var w in colony.Work!.Workers)
            {
                if (w.Outing != null && !ReferenceEquals(was.GetValueOrDefault(w.Id), w.Outing))
                    seen.Add((colony.Now.Tick, w.Name, w.Outing.Activity));
                was[w.Id] = w.Outing;
            }
        }
        return seen;
    }

    [Fact]
    public void In_their_recreation_hours_idle_colonists_choose_something_to_do()
    {
        var colony = Found(17);
        var seen = Watch(colony, 3 * Calendar.TicksPerHour);
        // Every founder went out at least once, within their first few thinks.
        Assert.All(colony.Work!.Workers, w => Assert.Contains(seen, s => s.Who == w.Name));
        Assert.All(colony.Work.Workers, w => Assert.True(seen.First(s => s.Who == w.Name).Tick < At(17).Tick + 60, w.Name + " started late"));
        // More than one kind of thing done in an evening.
        Assert.True(seen.Select(s => s.Activity).Distinct().Count() >= 3, string.Join(", ", seen.Select(s => s.Activity)));
    }

    [Fact]
    public void Without_trees_or_water_they_choose_other_things()
    {
        var colony = Found(17, trees: false, water: false);
        var seen = Watch(colony, 3 * Calendar.TicksPerHour);
        Assert.NotEmpty(seen);
        Assert.DoesNotContain(seen, s => s.Activity is "nature_walk" or "by_the_water");
    }

    [Fact]
    public void Recreation_is_eased_only_while_at_a_stop_never_on_the_way()
    {
        var colony = Found(17);
        var w = colony.Work!.Workers.First();
        w.FreeNeeds["recreation"] = 60;
        bool walked = false, stayed = false;
        double before = w.FreeNeeds["recreation"];
        bool wasAtStop = false;
        for (int i = 0; i < 3 * Calendar.TicksPerHour; i++)
        {
            colony.Run(1);
            double now = w.FreeNeeds["recreation"];
            bool atStop = w.Outing?.Left != null;
            if (!atStop && !wasAtStop && w.Outing != null)
            {
                Assert.True(now >= before, "eased on the way: " + before + " to " + now);
                walked = true;
            }
            if (atStop && wasAtStop && now < before)
                stayed = true;
            (before, wasAtStop) = (now, atStop);
        }
        Assert.True(walked && stayed, "the test saw no walk or no stay");
        Assert.True(w.FreeNeeds["recreation"] < 60, "an evening of free time eased nothing");
    }

    [Fact]
    public void When_their_free_time_ends_the_activity_ends_cleanly_and_waits_before_it_is_chosen_again()
    {
        // From 22:00: Flexible Time until 23:00, then Sleep. At 22:59:59 someone is out (sky gazing, in the dark).
        var colony = Found(22);
        var work = colony.Work!;
        colony.Run(At(23).Tick - colony.Now.Tick - 1);
        var w = work.Workers.FirstOrDefault(x => x.Outing != null);
        Assert.NotNull(w);
        var outing = w!.Outing!;
        for (int i = 0; i < 200 && w.Outing != null; i++)
            colony.Run(1);
        Assert.Null(w.Outing);                             // ended at their next think after the hour
        Assert.True(w.Personal != null || w.Asleep, "they did not go to sleep");
        Assert.True(w.NotBefore.TryGetValue(outing.Activity, out long until));
        Assert.InRange(until, colony.Now.Tick, colony.Now.Tick + colony.Content!.Leisure.RetryMinutes * Calendar.TicksPerMinute);
        Assert.DoesNotContain(work.Workers, o => o.Outing is { } h && h.X == outing.X && h.Y == outing.Y);   // the spot let go
    }

    [Fact]
    public void An_activity_waiting_after_an_interruption_is_not_chosen_until_its_wait_is_over()
    {
        var colony = Found(17);
        long until = colony.Now.Tick + 2 * Calendar.TicksPerHour;
        foreach (var w in colony.Work!.Workers)
            foreach (var activity in new[] { "nature_walk", "in_the_sun" })
                w.NotBefore[activity] = until;
        var seen = Watch(colony, 3 * Calendar.TicksPerHour);
        Assert.DoesNotContain(seen, s => s.Activity is "nature_walk" or "in_the_sun" && s.Tick < until);
        Assert.Contains(seen, s => s.Tick < until);        // they did other things meanwhile
    }

    [Fact]
    public void Finding_nothing_to_do_they_look_again_only_after_a_while()
    {
        // Everything waiting: nothing is offered, and they do not look at every think (step 25a).
        var colony = Found(17);
        var w = colony.Work!.Workers.First();
        foreach (var activity in colony.Content!.Leisure.Activities)
            w.NotBefore[activity.Id] = long.MaxValue;
        for (int i = 0; i < 600 && w.LookAgain == 0; i++)
            colony.Run(1);
        long looked = colony.Now.Tick;
        Assert.Equal(looked + colony.Content.Leisure.LookAgainMinutes * Calendar.TicksPerMinute, w.LookAgain);
        w.NotBefore.Clear();                               // now there is something; still, not before the while is over
        colony.Run(w.LookAgain - colony.Now.Tick - 1);
        Assert.Null(w.Outing);
        for (int i = 0; i < 600 && w.Outing == null; i++)
            colony.Run(1);
        Assert.NotNull(w.Outing);
        Assert.True(colony.Now.Tick >= w.LookAgain);
    }

    [Fact]
    public void Practice_is_offered_only_for_work_they_enjoy_and_teaches_a_little()
    {
        var colony = Found(17);
        var work = colony.Work!;
        var quern = work.AddSite("quern", 20.5, 20.5);
        var (keen, cool) = (work.Workers.First(), work.Workers.Skip(1).First());
        foreach (var w in work.Workers)
        {
            w.Interests.Clear();
            foreach (var activity in colony.Content!.Leisure.Activities.Where(a => a.Id != "practice"))
                w.NotBefore[activity.Id] = long.MaxValue;
        }
        keen.Interests["food_preparation"] = Interest.Enjoys;
        double skill = keen.Skills["food_preparation"].Proficiency;
        var seen = Watch(colony, 2 * Calendar.TicksPerHour);
        Assert.Contains(seen, s => s.Who == keen.Name && s.Activity == "practice");
        Assert.DoesNotContain(seen, s => s.Who == cool.Name);
        Assert.True(keen.Skills["food_preparation"].Proficiency > skill || colony.Content.Learning.PointsPerHour == 0);
    }

    [Fact]
    public void The_same_seeds_give_the_same_activities_at_the_same_ticks()
    {
        var a = Watch(Found(17), 3 * Calendar.TicksPerHour);
        var b = Watch(Found(17), 3 * Calendar.TicksPerHour);
        Assert.Equal(a, b);
        Assert.NotEqual(a, Watch(Found(17, seed: 8), 3 * Calendar.TicksPerHour));
    }

    [Fact]
    public void A_save_during_free_time_goes_on_as_if_never_saved()
    {
        var colony = Found(17);
        long end = colony.Now.Tick + 3 * Calendar.TicksPerHour;
        var copies = new List<Colony.Colony>();
        bool caught = false;
        colony.Run(end - colony.Now.Tick, c =>
        {
            if (c.Now.Tick % 97 != 0)
                return;
            caught |= c.Work!.Workers.Any(w => w.Outing != null) && c.Work.Workers.Any(w => w.NotBefore.Count > 0 || w.LastActivity != null);
            string text = ColonySave.Text(c);
            var copy = ColonySave.Read(text, c.Content!);
            Assert.Equal(text, ColonySave.Text(copy));
            copies.Add(copy);
        });
        Assert.True(caught, "no save caught an outing");
        string fingerprint = ColonySave.Fingerprint(colony);
        foreach (var copy in copies.Where((_, i) => i % 15 == 0))
        {
            copy.Run(end - copy.Now.Tick);
            Assert.Equal(fingerprint, ColonySave.Fingerprint(copy));
        }
    }

    [Fact]
    public void The_inspector_says_what_they_are_doing_their_free_time_needs_and_broad_interests()
    {
        var colony = Found(17);
        var w = colony.Work!.Workers.First();
        w.Interests.Clear();
        w.Interests["nature"] = Interest.Enjoys;
        w.Interests["company"] = Interest.Dislikes;
        w.FreeNeeds["social"] = 70;
        for (int i = 0; i < 600 && w.Outing == null; i++)
            colony.Run(1);
        var lines = Inspect.Worker(colony, w.Id).Lines;
        string doing = lines.Single(l => l.Label == "Doing").Text;
        Assert.Contains(colony.Content!.Leisure.Activities, a => doing.StartsWith(a.Doing));
        Assert.StartsWith("70 of 100, lonely", lines.Single(l => l.Label == "Social").Text);
        Assert.Equal("enjoys nature; dislikes company", lines.Single(l => l.Label == "Interests").Text);
    }
}
