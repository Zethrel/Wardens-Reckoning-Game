// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 23a: skills (design library: Skills, Traits and Knowledge Progression, §4, §5; R21-08). A colonist's
/// proficiency at a skill, from 0 to 100, sets how fast they do the work that uses it; the player sees a rank, not
/// the number; founders differ.
/// </summary>
public class SkillTests
{
    static readonly Moment Start = new(Calendar.TickOf(1, 1, 1, 9, 0));
    static readonly Content Real = TestContent.EverySecond;

    // A colony founded by the real content's founders (their skills drawn), and one at the usual proficiency.
    static Colony.Colony Drawn(int seed) => Founding.Found(seed, Start, Real, SanitationTests.Map(40, 30)).Colony;
    static Colony.Colony Usual() => Founding.Found(7, Start, SanitationTests.Tweaked(), SanitationTests.Map(40, 30)).Colony;

    [Theory]
    [InlineData(0, "Untrained", 0.5)]
    [InlineData(9.99, "Untrained", 0.5 + 0.5 * 9.99 / 40)]
    [InlineData(10, "Novice", 0.625)]
    [InlineData(39.9, "Apprentice", 0.99875)]
    [InlineData(40, "Journeyman", 1.0)]
    [InlineData(70, "Adept", 1.25)]
    [InlineData(89, "Expert", 1 + 0.5 * 49 / 60)]
    [InlineData(100, "Master", 1.5)]
    public void A_proficiency_has_a_rank_and_a_pace(double proficiency, string rank, double pace)
    {
        Assert.Equal(rank, Real.RankOf(proficiency));
        Assert.Equal(pace, Real.SpeedAt(proficiency), 9);
    }

    [Fact]
    public void Founders_differ_in_skill_drawn_from_their_ranges_and_the_same_seed_draws_the_same()
    {
        var colony = Drawn(7);
        var founders = colony.Work!.Workers.ToList();
        Assert.All(founders, w =>
        {
            Assert.Equal(new[] { "construction", "food_preparation" }, w.Skills.Keys);
            Assert.All(w.Skills.Values, p => Assert.InRange(p.Proficiency, 5, 65));
        });
        // They differ (library R21-08: at least two colonists observably differ in competence).
        Assert.True(founders.Select(w => w.Skills["construction"].Proficiency).Distinct().Count() > 1
                    || founders.Select(w => w.Skills["food_preparation"].Proficiency).Distinct().Count() > 1);
        string Skills(Colony.Colony c) => string.Join(";", c.Work!.Workers.Select(w => string.Join(",", w.Skills.Values.Select(v => v.Proficiency))));
        Assert.Equal(Skills(colony), Skills(Drawn(7)));
        Assert.Contains(Enumerable.Range(8, 5), seed => Skills(Drawn(seed)) != Skills(colony));
    }

    [Fact]
    public void Each_kind_of_work_uses_its_skill_and_carrying_none()
    {
        Assert.Equal("construction", Real.SkillFor["build"]);
        Assert.Equal("food_preparation", Real.SkillFor["quern"]);
        Assert.Equal("food_preparation", Real.SkillFor["boil"]);
        Assert.False(Real.SkillFor.ContainsKey("haul"));
        Assert.False(Real.SkillFor.ContainsKey("care"));
        // A haul's pace is the usual one whatever the carrier's skills.
        var colony = Usual();
        var work = colony.Work!;
        var ada = work.Workers.First();
        ada.Skills["construction"].Proficiency = 100;
        ada.Skills["food_preparation"].Proficiency = 0;
        var quern = work.AddSite("quern", 24.5, 14.5);
        work.Order(quern.Id, "grind_wheat", 1);
        var haul = Assert.Single(work.Jobs, j => j.Kind == JobKind.Haul);
        Assert.Null(work.SkillOf(haul));
        Assert.Equal(1, work.Pace(ada, haul));
    }

    // Seconds from the moment the wall's building begins until it is finished, by a lone builder of `proficiency`.
    static int Building(double proficiency, bool ill = false)
    {
        var colony = Usual();
        var work = colony.Work!;
        foreach (var w in work.Workers.Skip(1))
            work.Stop(w.Id);                              // one builder
        var builder = work.Workers.First();
        builder.Skills["construction"].Proficiency = proficiency;
        if (ill)
            builder.Illness = new Illness(colony.Content!.UpsetStomach.Name, colony.Now.Tick, colony.Now.Tick + 10 * Calendar.TicksPerDay);
        var (wall, placed) = work.Plan("wall", 22, 14);
        Assert.True(placed.Ok, placed.Why);
        Job? Build() => work.Jobs.FirstOrDefault(j => j.Kind == JobKind.Build && j.Site == wall!.Id);
        for (int i = 0; i < 3600 && Build()?.Step != Step.Working; i++)
            colony.Run(1);
        var job = Build()!;
        Assert.Equal(Step.Working, job.Step);
        int seconds = 0;
        while (work.Jobs.Contains(job))
        {
            colony.Run(1);
            seconds++;
        }
        return seconds;
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(40, 1.0)]
    [InlineData(100, 1.5)]
    public void A_wall_takes_its_minutes_at_the_usual_pace_longer_unskilled_and_shorter_skilled(double proficiency, double pace)
    {
        int usual = Real.Pieces["wall"].WorkMinutes * 60;      // 45 minutes of work at the usual pace
        // Seen at the builder's first think after it is done (every 2 to 7 seconds).
        Assert.InRange(Building(proficiency), usual / pace - 1, usual / pace + 7);
    }

    [Fact]
    public void Boiling_water_uses_food_preparation()
    {
        var colony = Usual();
        var work = colony.Work!;
        var brook = colony.Stocks[work.Springs.Single()];
        work.SetMark(brook.Id, WaterMark.BoilFirst);
        var boil = Assert.Single(work.Jobs, j => j.Kind == JobKind.Boil);
        Assert.Equal("food_preparation", work.SkillOf(boil));
        var ada = work.Workers.First();
        ada.Skills["food_preparation"].Proficiency = 100;
        ada.Skills["construction"].Proficiency = 0;
        Assert.Equal(1.5, work.Pace(ada, boil), 9);
    }

    [Fact]
    public void Skill_and_illness_together()
    {
        int usual = Real.Pieces["wall"].WorkMinutes * 60;
        double pace = 1.5 * Real.UpsetStomach.Work;                  // a master, ill: 1.05
        Assert.InRange(Building(100, ill: true), usual / pace - 1, usual / pace + 7);
    }

    [Fact]
    public void The_inspector_shows_the_rank_and_the_pace_and_the_number_only_in_the_trace()
    {
        var colony = Usual();
        var ada = colony.Work!.Workers.First();
        ada.Skills["construction"].Proficiency = 72.4;
        ada.Skills["construction"].Aptitude = 1;                 // the founders' are drawn (step 23b)
        ada.Skills["food_preparation"].Proficiency = 3;
        var lines = Inspect.Worker(colony, ada.Id).Lines;
        Assert.Equal("Adept, works at 127% of the usual pace", lines.Single(l => l.Label == "Construction").Text);
        Assert.Equal("Untrained, works at 54% of the usual pace", lines.Single(l => l.Label == "Food preparation").Text);
        Assert.DoesNotContain(lines, l => l.Text.Contains("72.4"));
        var traced = Inspect.Worker(colony, ada.Id, trace: true).Lines;
        Assert.Equal("Adept, works at 127% of the usual pace (72.4, best 72.4, aptitude 1.00, practised 0.0 h)", traced.Single(l => l.Label == "Construction").Text);
    }

    [Fact]
    public void A_save_keeps_every_colonists_skills()
    {
        var colony = Drawn(7);
        colony.Work!.Workers.First().Skills["construction"].Proficiency = 33.25;
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, colony.Content!);
        Assert.Equal(colony.Work.Workers.Select(w => string.Join(",", w.Skills)), loaded.Work!.Workers.Select(w => string.Join(",", w.Skills)));
        Assert.Equal(text, ColonySave.Text(loaded));
    }

    [Theory]
    [InlineData("smithing", "12", "skills.smithing is not a skill the content knows")]
    [InlineData("construction", "101", "skills.construction is not [proficiency (0 to 100)")]
    [InlineData("construction", "-1", "skills.construction is not [proficiency (0 to 100)")]
    [InlineData("construction", "\"good\"", "skills.construction is not [proficiency (0 to 100)")]
    public void A_save_with_a_broken_skill_is_refused(string skill, string value, string reason)
    {
        var colony = Drawn(7);
        var save = ColonySave.Write(colony);
        save["work"]!["workers"]![0]!["skills"]![skill] = JsonNode.Parse(value);
        Assert.Contains(reason, Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), colony.Content!)).Message);
        save["work"]!["workers"]![0]!["skills"] = 5;
        Assert.Contains("skills is not a table of skill and [proficiency", Assert.Throws<ColonySaveException>(() => ColonySave.Read(save.ToJsonString(), colony.Content!)).Message);
    }

    [Fact]
    public void A_skill_added_to_the_content_since_a_save_starts_at_the_usual_proficiency()
    {
        var colony = Drawn(7);
        var save = ColonySave.Write(colony);
        save["work"]!["workers"]![0]!["skills"]!.AsObject().Remove("food_preparation");
        var loaded = ColonySave.Read(save.ToJsonString(), colony.Content!, acceptChanged: true);
        Assert.Equal(Real.StandardProficiency, loaded.Work!.Workers.First().Skills["food_preparation"].Proficiency);
    }
}
