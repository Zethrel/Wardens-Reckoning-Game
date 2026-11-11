// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 23b: learning by practice and forgetting (design library: Skills, Traits and Knowledge Progression §5.3, §6.1,
/// §9, §10). Work teaches the skill it uses, most when it is above the colonist, nothing once they are well past it;
/// aptitude speeds or slows it and shows only once seen; an unpractised skill fades, never below half the best; a
/// former expert learns back faster.
/// </summary>
public class PracticeTests
{
    static readonly Moment Start = new(Calendar.TickOf(1, 1, 1, 9, 0));
    // The real content, everyone thinking every second, and learning (unlike TestContent.EverySecond).
    static readonly Content Learning = Content.Read(TestContent.ThinkingEverySecond(TestContent.Files()));
    static LearningRules Rules => Learning.Learning;

    static (Colony.Colony Colony, Work Work, Worker Ada) Founded()
    {
        var colony = Founding.Found(7, Start, Learning, SanitationTests.Map(40, 30)).Colony;
        return (colony, colony.Work!, colony.Work!.Workers.First());
    }

    // A founded colony, everyone stopped, and the building job of a `piece` planned and supplied there (its challenge
    // known; nobody takes it).
    static (Colony.Colony, Work, Worker, Job) WithJob(string piece)
    {
        var (colony, work, ada) = Founded();
        foreach (var w in work.Workers)
            work.Stop(w.Id);
        var (project, placed) = work.Plan(piece, 22, 14);
        Assert.True(placed.Ok, placed.Why);
        TestContent.Deliver(colony, project!, "timber", Learning.Pieces[piece].Materials.GetValueOrDefault("timber"));
        colony.RunMinutes(1);
        return (colony, work, ada, work.Jobs.Single(j => j.Kind == JobKind.Build && j.Site == project!.Id));
    }

    [Theory]
    [InlineData(25, 25, 1.0)]
    [InlineData(25, 15, 1.5)]
    [InlineData(25, 5, 2.0)]                                 // at most twice
    [InlineData(25, 0, 2.0)]
    [InlineData(25, 35, 0.5)]
    [InlineData(25, 45, 0.0)]                                // 20 past it: nothing
    [InlineData(25, 80, 0.0)]
    [InlineData(55, 60, 0.75)]
    public void Work_teaches_most_above_the_colonist_and_nothing_well_past_them(double challenge, double proficiency, double share)
    {
        var (_, work, _) = Founded();
        Assert.Equal(share, work.Teaches(challenge, proficiency), 9);
    }

    [Fact]
    public void An_hour_at_a_wall_teaches_construction_by_the_challenge_and_the_aptitude()
    {
        var (colony, work, ada, wall) = WithJob("wall");             // challenge 25
        var state = ada.Skills["construction"];
        (state.Peak, state.Proficiency, state.Aptitude) = (0, 15, 1);   // never better than 15
        state.LastPractised = -100 * Calendar.TicksPerDay;          // long ago
        work.Practise(ada, wall, 3600);
        Assert.Equal(15 + Rules.PointsPerHour * 1.5, state.Proficiency, 9);   // 1.5: ten below the challenge
        Assert.Equal(3600, state.Practised);
        Assert.Equal(colony.Now.Tick, state.LastPractised);   // practised now: nothing fades tomorrow
        double now = state.Proficiency;
        work.Forget();
        Assert.Equal(now, state.Proficiency);
        // Food preparation is not touched by building.
        double food = ada.Skills["food_preparation"].Proficiency;
        work.Practise(ada, wall, 3600);
        Assert.Equal(food, ada.Skills["food_preparation"].Proficiency);
        // Aptitude: three times as fast at 1.5 as at 0.5.
        state.Proficiency = 15;
        state.Peak = 15;
        state.Aptitude = 1.5;
        work.Practise(ada, wall, 3600);
        double quick = state.Proficiency - 15;
        state.Proficiency = 15;
        state.Peak = 15;
        state.Aptitude = 0.5;
        work.Practise(ada, wall, 3600);
        Assert.Equal(3 * (state.Proficiency - 15), quick, 9);
    }

    [Fact]
    public void Carrying_teaches_nothing()
    {
        var (colony, work, ada) = Founded();
        var quern = work.AddSite("quern", 24.5, 14.5);
        work.Order(quern.Id, "grind_wheat", 1);
        var haul = work.Jobs.First(j => j.Kind == JobKind.Haul);
        string before = string.Join(",", ada.Skills.Select(s => s.Value.Proficiency + "/" + s.Value.Practised));
        work.Practise(ada, haul, 3600);
        Assert.Equal(before, string.Join(",", ada.Skills.Select(s => s.Value.Proficiency + "/" + s.Value.Practised)));
    }

    [Fact]
    public void Walls_alone_never_make_a_master()
    {
        var (_, work, ada, wall) = WithJob("wall");
        var state = ada.Skills["construction"];
        (state.Peak, state.Proficiency, state.Aptitude) = (0, 0, 1.5);
        for (int hour = 0; hour < 5000; hour++)
            work.Practise(ada, wall, 3600);
        // A wall's challenge is 25: it teaches nothing past 45.
        Assert.InRange(state.Proficiency, 44, 45);
        Assert.Equal("Journeyman", Learning.RankOf(state.Proficiency));
    }

    [Fact]
    public void A_former_expert_learns_back_three_times_as_fast_up_to_their_best()
    {
        var (_, work, ada, well) = WithJob("well");                  // challenge 55
        var state = ada.Skills["construction"];
        state.Aptitude = 1;
        (state.Peak, state.Proficiency) = (0, 60);                   // their best
        state.Lose(30);                                              // faded to 30
        Assert.Equal((30.0, 60.0), (state.Proficiency, state.Peak));
        work.Practise(ada, well, 3600);
        Assert.Equal(30 + 2.0 * Rules.Recovery, state.Proficiency, 9);  // twice (the most) times three
        // Someone at 30 who was never better: three times slower.
        var (_, work2, bram, well2) = WithJob("well");
        var fresh = bram.Skills["construction"];
        fresh.Aptitude = 1;
        fresh.Peak = 0;
        fresh.Proficiency = 30;
        work2.Practise(bram, well2, 3600);
        Assert.Equal(32, fresh.Proficiency, 9);
        // Near their best, not past it at the faster pace.
        state.Proficiency = 59.5;
        work.Practise(ada, well, 3600);
        Assert.Equal(59.5 + work.Teaches(55, 59.5), state.Proficiency, 9);
    }

    [Fact]
    public void An_unpractised_skill_fades_after_a_fortnight_never_below_half_the_best()
    {
        var (colony, work, ada) = Founded();
        var state = ada.Skills["construction"];
        (state.Peak, state.Proficiency) = (0, 60);                   // their best
        long fortnight = (long)(Rules.DecayAfterDays * Calendar.TicksPerDay);
        state.LastPractised = colony.Now.Tick - fortnight + 1;      // a second short of a fortnight
        work.Forget();
        Assert.Equal(60, state.Proficiency);
        state.LastPractised = colony.Now.Tick - fortnight;
        work.Forget();
        Assert.Equal(60 - Rules.DecayPerDay, state.Proficiency, 9);
        for (int morning = 0; morning < 200; morning++)
            work.Forget();
        Assert.Equal(60 * Rules.KeptShare, state.Proficiency, 9);
        Assert.Equal(60, state.Peak);
    }

    [Fact]
    public void Each_morning_the_colony_forgets_what_nobody_practised()
    {
        // A founded colony, nothing to do: three weeks in, everyone's skills have faded a little, none below half.
        var (colony, work, _) = Founded();
        var before = work.Workers.SelectMany(w => w.Skills.Values.Select(s => s.Proficiency)).ToList();
        colony.RunMinutes(21 * 24 * 60);
        var after = work.Workers.SelectMany(w => w.Skills.Values.Select(s => s.Proficiency)).ToList();
        foreach (var (was, now) in before.Zip(after))
        {
            Assert.True(now < was || was == 0, was + " to " + now);
            Assert.True(now >= was * Rules.KeptShare);
            Assert.InRange(was - now, Rules.DecayPerDay * 5, Rules.DecayPerDay * 8 + 1e-9);   // 6 or 7 mornings past the fortnight
        }
    }

    [Fact]
    public void Aptitude_is_shown_only_once_seen_in_practice()
    {
        var (colony, work, ada, wall) = WithJob("wall");
        var state = ada.Skills["construction"];
        state.Proficiency = 0;
        state.Aptitude = 1.4;
        string Line() => Inspect.Worker(colony, ada.Id).Lines.Single(l => l.Label == "Construction").Text;
        work.Practise(ada, wall, (int)(Rules.AptitudeSeenHours * 3600) - 1);
        Assert.DoesNotContain("learns", Line());
        work.Practise(ada, wall, 1);
        Assert.EndsWith("; learns quickly", Line());
        state.Aptitude = 0.6;
        Assert.EndsWith("; learns slowly", Line());
        state.Aptitude = 1.0;
        Assert.DoesNotContain("learns", Line());
        // The developer's trace has the number all along.
        Assert.Contains("aptitude 1.00", Inspect.Worker(colony, ada.Id, trace: true).Lines.Single(l => l.Label == "Construction").Text);
    }

    [Fact]
    public void Founders_aptitude_is_drawn_from_its_range_the_same_by_the_same_seed()
    {
        var (_, work, _) = Founded();
        var drawn = work.Workers.SelectMany(w => w.Skills.Values.Select(s => s.Aptitude)).ToList();
        Assert.All(drawn, a => Assert.InRange(a, Rules.AptitudeFrom, Rules.AptitudeTo));
        Assert.True(drawn.Distinct().Count() > 1);
        Assert.Equal(drawn, Founded().Work.Workers.SelectMany(w => w.Skills.Values.Select(s => s.Aptitude)));
    }

    [Fact]
    public void Building_all_day_makes_a_builder_better_and_faster()
    {
        var colony = Founding.Found(7, Start, Learning, SanitationTests.Map(40, 30)).Colony;
        var work = colony.Work!;
        foreach (var w in work.Workers.Skip(1))
            work.Stop(w.Id);
        var ada = work.Workers.First();
        var state = ada.Skills["construction"];
        state.Proficiency = 10;
        double pace = colony.Content!.SpeedAt(10);
        for (int x = 15; x < 25; x++)
            work.Plan("floor", x, 12);                                // challenge 15: teaches a novice
        colony.RunMinutes(8 * 60);
        Assert.True(state.Proficiency > 11, "proficiency " + state.Proficiency);
        Assert.True(colony.Content.SpeedAt(state.Proficiency) > pace);
        Assert.True(state.Practised > 0);
    }

    [Fact]
    public void A_save_keeps_every_skills_learning()
    {
        var (colony, work, ada, wall) = WithJob("wall");
        work.Practise(ada, wall, 5000);
        ada.Skills["food_preparation"].Lose(3);
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, colony.Content!);
        string Of(Colony.Colony c) => string.Join(";", c.Work!.Workers.SelectMany(w => w.Skills.Select(s =>
            s.Key + s.Value.Proficiency + "/" + s.Value.Peak + "/" + s.Value.Aptitude + "/" + s.Value.Practised + "/" + s.Value.LastPractised)));
        Assert.Equal(Of(colony), Of(loaded));
        Assert.Equal(text, ColonySave.Text(loaded));
    }

    [Theory]
    [InlineData("[50, 40, 1, 0, 0]")]                        // best below now
    [InlineData("[50, 60, 0, 0, 0]")]                        // no aptitude
    [InlineData("[50, 60, 1, -5, 0]")]                       // practised less than nothing
    [InlineData("[50, 60, 1, 0, 9999999999]")]               // practised after now
    [InlineData("[50, 101, 1, 0, 0]")]
    [InlineData("[50, 60, 1, 0]")]
    public void A_save_with_a_broken_skill_state_is_refused(string state)
    {
        var (colony, _, _) = Founded();
        var save = ColonySave.Write(colony);
        save["work"]!["workers"]![0]!["skills"]!["construction"] = JsonNode.Parse(state);
        Assert.Contains("skills.construction is not [proficiency", Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), colony.Content!)).Message);
    }
}
