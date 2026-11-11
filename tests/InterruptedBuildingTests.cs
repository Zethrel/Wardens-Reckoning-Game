// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// A piece its builder leaves half built, for whatever reason, is finished by someone else from where it was (the
/// maintainer, 11-11-2026; library Review 32: interrupted work keeps its progress). The building job goes back to be
/// taken with its work left; whoever takes it does only that, at their own skill (step 23a).
/// </summary>
public class InterruptedBuildingTests
{
    static readonly Moment Start = new(Calendar.TickOf(1, 1, 1, 9, 0));

    public enum Why { Stopped, Forbidden, Hungry, Collapsed }

    [Theory]
    [InlineData(Why.Stopped)]
    [InlineData(Why.Forbidden)]
    [InlineData(Why.Hungry)]
    [InlineData(Why.Collapsed)]
    public void A_wall_left_half_built_is_finished_by_another_from_where_it_was(Why why)
    {
        var colony = Founding.Found(7, Start, SanitationTests.Tweaked(), SanitationTests.Map(40, 30)).Colony;
        var work = colony.Work!;
        var (first, second) = (work.Workers.First(), work.Workers.Skip(1).First());
        foreach (var w in work.Workers.Skip(1))
            work.Stop(w.Id);                                   // the first builds alone
        var (wall, placed) = work.Plan("wall", 22, 14);
        Assert.True(placed.Ok, placed.Why);
        Job? Build() => work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Build && j.Site == wall!.Id);
        for (int i = 0; i < 3600 && Build()?.Step != Step.Working; i++)
            colony.Run(1);
        var job = Build()!;
        Assert.Equal(first.Id, job.Worker);
        // Twenty of its 45 minutes of work done.
        int whole = colony.Content!.Pieces["wall"].WorkMinutes * 60;
        while (job.Left > whole - 20 * 60)
            colony.Run(1);
        int left = job.Left;
        // The builder leaves it.
        switch (why)
        {
            case Why.Stopped: work.Stop(first.Id); break;
            case Why.Forbidden: work.Permit(first.Id, Work.BuildWork, false); break;
            case Why.Hungry: first.Needs["hunger"].Level = 95; break;           // critical: drops the job to eat
            case Why.Collapsed: first.Needs["fatigue"].Level = 100; break;      // collapses where they stand
        }
        for (int i = 0; i < 10 && job.Worker == first.Id; i++)
            colony.Run(1);
        Assert.Null(job.Worker);
        Assert.Equal(Step.Open, job.Step);
        Assert.InRange(job.Left, left - 7, left);              // what was done is kept (up to a think's work)
        // Someone else takes it up and does only what is left.
        if (why != Why.Stopped)
            work.Stop(first.Id);                               // so the second, not the first, finishes it
        work.Resume(second.Id);
        for (int i = 0; i < 600 && job.Step != Step.Working; i++)
            colony.Run(1);
        Assert.Equal(second.Id, job.Worker);
        int seconds = 0, rest = job.Left;
        while (work.Jobs.Contains(job))
        {
            colony.Run(1);
            seconds++;
        }
        Assert.Equal("wall", colony.Ground!.PieceAt(22, 14));
        Assert.InRange(rest, 24 * 60, 25 * 60 + 7);              // about 25 of the 45 minutes left
        Assert.InRange(seconds, rest - 1, rest + 7);            // at the usual pace: just those
    }

    [Fact]
    public void A_skilled_second_builder_finishes_what_is_left_faster()
    {
        var colony = Founding.Found(7, Start, SanitationTests.Tweaked(), SanitationTests.Map(40, 30)).Colony;
        var work = colony.Work!;
        var (first, second) = (work.Workers.First(), work.Workers.Skip(1).First());
        foreach (var w in work.Workers.Skip(1))
            work.Stop(w.Id);
        second.Skills["construction"] = 100;                   // a master: half again as fast
        var (wall, _) = work.Plan("wall", 22, 14);
        Job? Build() => work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Build && j.Site == wall!.Id);
        while (Build()?.Step != Step.Working)
            colony.Run(1);
        var job = Build()!;
        while (job.Left > 30 * 60)                              // 15 minutes done, 30 left
            colony.Run(1);
        work.Stop(first.Id);
        work.Resume(second.Id);
        while (job.Step != Step.Working || job.Worker != second.Id)
            colony.Run(1);
        int seconds = 0, rest = job.Left;
        while (work.Jobs.Contains(job))
        {
            colony.Run(1);
            seconds++;
        }
        Assert.InRange(seconds, rest / 1.5 - 1, rest / 1.5 + 7);
    }
}
