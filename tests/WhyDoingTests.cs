// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 26c (the UI plan, docs/ui-plan.md, UI-03): why a colonist is doing what they do, from the colony's own state.
/// Between jobs, the plan's cases are told apart: no work, only paused work, work they are not allowed, work out of
/// their reach, the work they may do taken by others. Founders on the free-time meadow (a stream down x = 35) at
/// 09:00, in their Work hours.
/// </summary>
public class WhyDoingTests
{
    static (Colony.Colony Colony, Work Work) Morning()
    {
        var colony = FreeTimeTests.Found(9);
        colony.Run(5 * Calendar.TicksPerMinute);             // everyone has thought: between jobs
        Assert.All(colony.Work!.Workers, w => Assert.NotNull(w.BetweenSince));
        return (colony, colony.Work);
    }

    [Fact]
    public void With_nothing_to_do_it_says_so_and_what_would_help()
    {
        var (_, work) = Morning();
        var why = work.WhyDoing(work.Workers.First().Id);
        Assert.StartsWith("In work hours · no work to do; pottering about, and from 09:", why.Player);
        Assert.Contains("free time near their work", why.Player);
        Assert.StartsWith("Plan something to build", why.NextStep);
        Assert.Contains("open 0", why.Trace);
    }

    [Fact]
    public void Work_they_are_not_allowed_is_named()
    {
        var (_, work) = Morning();
        foreach (var w in work.Workers)
            work.Permit(w.Id, Work.BuildWork, false);
        work.Plan("road", 20, 20);
        var why = work.WhyDoing(work.Workers.First().Id);
        Assert.Contains("no work they are allowed (there is work to build)", why.Player);
        Assert.StartsWith("Allow them", why.NextStep);
    }

    [Fact]
    public void Work_taken_by_someone_else_is_told_apart_from_no_work()
    {
        var (colony, work) = Morning();
        work.Plan("road", 20, 20);
        colony.Run(1);
        var builder = work.Workers.Single(w => w.Job != null);
        Assert.StartsWith("In work hours: build", work.WhyDoing(builder.Id).Player);
        Assert.All(work.Workers.Where(w => w != builder),
                   w => Assert.Contains("the work they may do is taken by others", work.WhyDoing(w.Id).Player));
    }

    [Fact]
    public void Paused_work_is_told_apart_from_no_work()
    {
        var (_, work) = Morning();
        foreach (var w in work.Workers)
            work.Permit(w.Id, Work.BuildWork, false);      // nobody takes it before it is paused
        var (project, placement) = work.Plan("road", 20, 20);
        Assert.True(placement.Ok, placement.Why);
        work.Pause(project!.Id, true);
        foreach (var w in work.Workers)
            work.Permit(w.Id, Work.BuildWork, true);
        var why = work.WhyDoing(work.Workers.First().Id);
        Assert.Contains("the only work is paused", why.Player);
        Assert.Equal("Resume a paused building.", why.NextStep);
    }

    [Fact]
    public void Building_nobody_can_reach_is_told_apart_from_no_work()
    {
        var (_, work) = Morning();
        var (_, placement) = work.Plan("road", 38, 10);   // beyond the stream at x = 35: no way leads there
        Assert.True(placement.Ok, placement.Why);
        Assert.NotNull(placement.Warning);                 // the player was warned when planning it
        var why = work.WhyDoing(work.Workers.First().Id);
        Assert.Contains("the building planned waits: no colonist can reach it", why.Player);
        Assert.StartsWith("Open a way to it", why.NextStep);
    }

    [Fact]
    public void In_free_time_and_on_the_card()
    {
        var colony = FreeTimeTests.Found(17);
        var work = colony.Work!;
        for (int i = 0; i < 600 && work.Workers.All(w => w.Outing == null); i++)
            colony.Run(1);
        var w = work.Workers.First(x => x.Outing != null);
        var why = work.WhyDoing(w.Id);
        Assert.StartsWith("Their free time (recreation): ", why.Player);
        Assert.EndsWith("chosen by their needs and what they enjoy.", why.Player);
        // The card carries it, and the developer's trace with F3.
        Assert.Equal(why, Inspect.Card(colony, w.Id).Why);
        Assert.Contains(Inspect.Card(colony, w.Id, trace: true).Diagnostics, l => l.Text == why.Trace);
    }
}
