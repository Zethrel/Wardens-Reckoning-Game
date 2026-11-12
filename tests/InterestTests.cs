// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 23c: interest (design library: Skills, Traits and Knowledge Progression §6.2 to §6.3; the maintainer, 12
/// November 2026: it steers which job a colonist takes, and is shown openly). A colonist may enjoy or dislike the work
/// a skill is used for. Work they enjoy is offered to them first, work they dislike last; it never makes them work
/// better or faster, and never makes them refuse.
/// </summary>
public class InterestTests
{
    static readonly Moment Start = new(Calendar.TickOf(1, 1, 1, 9, 0));

    // Three founders on open ground, at the usual proficiency, with no interests yet: the first at (10, 15), the
    // second at (30, 15), the third stopped. A wall at (12, 15) with its timber there, its building open.
    static (Colony.Colony Colony, Worker Near, Worker Far, Job Build) Two()
    {
        var colony = Founding.Found(7, Start, SanitationTests.Tweaked(), SanitationTests.Map(40, 30)).Colony;
        var work = colony.Work!;
        var all = work.Workers.ToList();
        foreach (var w in all)
            w.Interests.Clear();
        var (near, far) = (all[0], all[1]);
        foreach (var w in all)
            work.Stop(w.Id);                                   // nobody takes it before the stage is set
        var (wall, placed) = work.Plan("wall", 12, 15);
        Assert.True(placed.Ok, placed.Why);
        TestContent.Deliver(colony, wall!, "timber", 2);
        Job? Build() => work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Build && j.Site == wall!.Id);
        for (int i = 0; i < 120 && Build() == null; i++)
            colony.Run(1);                                     // the site sees its timber and asks for building
        var build = Build()!;
        Assert.Equal(Step.Open, build.Step);
        (near.X, near.Y) = (10.5, 15.5);
        (far.X, far.Y) = (30.5, 15.5);
        work.Resume(near.Id);
        work.Resume(far.Id);
        return (colony, near, far, build);
    }

    static ColonistId? TakenBy(Colony.Colony colony, Job build)
    {
        for (int i = 0; i < 600 && build.Worker == null; i++)
            colony.Run(1);
        return build.Worker;
    }

    [Fact]
    public void Without_interests_the_nearest_takes_the_work()
    {
        var (colony, near, _, build) = Two();
        Assert.Equal(near.Id, TakenBy(colony, build));
    }

    [Fact]
    public void Work_a_colonist_enjoys_is_offered_to_them_first()
    {
        var (colony, near, far, build) = Two();
        far.Interests["construction"] = Interest.Enjoys;
        Assert.Equal(far.Id, TakenBy(colony, build));
    }

    [Fact]
    public void Work_a_colonist_dislikes_goes_to_someone_else_if_anyone_is_free()
    {
        var (colony, near, far, build) = Two();
        near.Interests["construction"] = Interest.Dislikes;
        Assert.Equal(far.Id, TakenBy(colony, build));
    }

    [Fact]
    public void Disliked_work_is_still_done_when_nobody_else_is_free()
    {
        // Never a refusal (library §6.3).
        var (colony, near, far, build) = Two();
        near.Interests["construction"] = Interest.Dislikes;
        colony.Work!.Stop(far.Id);
        Assert.Equal(near.Id, TakenBy(colony, build));
    }

    [Fact]
    public void Interest_never_changes_how_fast_the_work_goes()
    {
        long Built(Interest interest)
        {
            var (colony, near, far, build) = Two();
            colony.Work!.Stop(far.Id);
            near.Interests["construction"] = interest;
            TakenBy(colony, build);
            while (build.Step != Step.Working)
                colony.Run(1);
            long started = colony.Now.Tick;
            while (colony.Work.Jobs.Contains(build))
                colony.Run(1);
            return colony.Now.Tick - started;
        }
        long usual = Built(Interest.None);
        Assert.Equal(usual, Built(Interest.Enjoys));
        Assert.Equal(usual, Built(Interest.Dislikes));
    }

    [Fact]
    public void Founders_draw_interests_on_dice_of_their_own()
    {
        // The real chances: some founders across a few seeds enjoy something, some dislike something; the same seed
        // draws the same; and the rest of the founding (names, skills) is what it was without interests.
        var content = TestContent.Real;
        string Interests(int seed) => string.Join(";", Founding.Found(seed, Start, content, SanitationTests.Map(40, 30)).Colony.Work!.Workers
            .Select(w => w.Name + ":" + string.Join(",", w.Interests.Select(i => i.Key + "=" + i.Value))));
        var drawn = Enumerable.Range(1, 8).Select(Interests).ToList();
        Assert.Contains(drawn, d => d.Contains("=Enjoys"));
        Assert.Contains(drawn, d => d.Contains("=Dislikes"));
        Assert.Equal(drawn[0], Interests(1));
        var none = TestContent.Files();
        none["founding.json"] = none["founding.json"]!.Replace("\"enjoys\": 0.3, \"dislikes\": 0.15", "\"enjoys\": 0, \"dislikes\": 0");
        var without = Content.Read(none);
        string Rest(Content c) => string.Join(";", Founding.Found(3, Start, c, SanitationTests.Map(40, 30)).Colony.Work!.Workers
            .Select(w => w.Name + ":" + string.Join(",", w.Skills.Values.Select(v => v.Proficiency + "/" + v.Aptitude))));
        Assert.Equal(Rest(without), Rest(content));
        Assert.All(Founding.Found(3, Start, without, SanitationTests.Map(40, 30)).Colony.Work!.Workers, w => Assert.Empty(w.Interests));
    }

    [Fact]
    public void The_inspector_says_it_openly_and_a_save_keeps_it()
    {
        var (colony, near, far, _) = Two();
        near.Interests["construction"] = Interest.Enjoys;
        far.Interests["food_preparation"] = Interest.Dislikes;
        Assert.EndsWith("; enjoys it", Inspect.Worker(colony, near.Id).Lines.Single(l => l.Label == "Construction").Text);
        Assert.EndsWith("; dislikes it", Inspect.Worker(colony, far.Id).Lines.Single(l => l.Label == "Food preparation").Text);
        Assert.DoesNotContain("it", Inspect.Worker(colony, near.Id).Lines.Single(l => l.Label == "Food preparation").Text.Split(';').Skip(1));
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, colony.Content!);
        Assert.Equal(text, ColonySave.Text(loaded));
        Assert.Equal(Interest.Enjoys, loaded.Work!.Workers.Single(w => w.Id == near.Id).Interests["construction"]);
        Assert.Contains("\"construction\": \"enjoys\"", text);
        Assert.Contains("which should be \"enjoys\" or \"dislikes\"", Assert.Throws<ColonySaveException>(
            () => ColonySave.Read(text.Replace("\"construction\": \"enjoys\"", "\"construction\": \"loves\""), colony.Content!)).Message);
    }
}
