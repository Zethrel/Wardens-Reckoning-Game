// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Opinions, rulers, grievance and your rules (step 4), against the generator's own functions where they
/// do not roll dice, and by what must follow where they do.</summary>
public class RulerTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static Game NewGame(long seed = 1) => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), seed);
    static readonly JsonObject Expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["trade"]!.AsObject();

    static Market[] OneYearOfTrade(Game game)
    {
        var harvest = Enumerable.Range(0, game.Towns.Count).Select(i =>
        {
            var faction = game.Towns[i].Holder is int h ? game.Factions[h] : null;
            return YearCore.PathHarvest(1.0, faction?.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings);
        }).ToList();
        var markets = Trade.MakeMarkets(game, harvest);
        Trade.TradeGoods(game, markets);
        return markets;
    }

    [Fact]
    public void Every_opinion_agrees_with_the_generator_s()
    {
        // fixtures/make_expected.py: factions.opinion for every pair with people, after one year's trade (for envy).
        var game = NewGame();
        var supply = Opinions.Supplies(game, OneYearOfTrade(game));
        int free = 0;
        foreach (var node in Expected["opinions"]!.AsArray())
        {
            int a = (int)node![0]!, b = (int)node[1]!;
            double want = (double)node[2]!;
            double got = Opinions.Opinion(game, a, b, game.Memory.GetValueOrDefault((a, b)), Opinions.Covet(supply, a, b));
            Assert.True(Math.Abs(want - got) < 1e-6, $"{a} of {b}: {want} but {got}");
            if (Math.Abs(want) < 1) free++;
        }
        Assert.True(free > 20, "most opinions are not at an end, so each part of them is checked");
    }

    [Fact]
    public void Paths_fit_a_people_s_values_as_the_generator_says()
    {
        var game = NewGame();
        var want = Expected["path_fit"]!.AsArray();
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var fit = Rulers.PathFit(game, game.Factions[f].Values);
            foreach (var (path, value) in want[f]!.AsObject())
                Assert.Equal((double)value!, fit[path], 6);
        }
    }

    [Fact]
    public void A_ruler_against_the_people_s_ways_as_the_generator_measures_it()
    {
        var game = NewGame();
        var want = Expected["against_values"]!.AsArray();
        for (int f = 0; f < game.Factions.Count; f++)
        {
            if (want[f] is null)
            {
                Assert.Null(game.Factions[f].Ruler);
                continue;
            }
            Assert.Equal((double)want[f]!, Rulers.AgainstValues(game.Factions[f].Ruler!, game.Factions[f].Values), 6);
        }
    }

    [Fact]
    public void The_same_seed_rolls_the_same_and_a_saved_state_rolls_on_alike()
    {
        var a = new Dice(42, "rulers");
        var b = new Dice(42, "rulers");
        var other = new Dice(42, "rules");
        var rolls = Enumerable.Range(0, 5).Select(_ => a.Random()).ToList();
        Assert.Equal(rolls, Enumerable.Range(0, 5).Select(_ => b.Random()).ToList());
        Assert.NotEqual(rolls, Enumerable.Range(0, 5).Select(_ => other.Random()).ToList());
        var kept = new Dice(a.State);
        Assert.Equal(a.Next(), kept.Next());
        Assert.All(Enumerable.Range(0, 1000).Select(_ => a.Int(18, 40)), n => Assert.InRange(n, 18, 40));
        Assert.Contains(18, Enumerable.Range(0, 2000).Select(_ => a.Int(18, 40)));
        Assert.All(Enumerable.Range(0, 1000).Select(_ => a.Random()), x => Assert.InRange(x, 0.0, 0.9999999999));
    }

    [Fact]
    public void New_rulers_take_new_names_of_their_people()
    {
        var game = NewGame();
        int folk = game.Factions[0].Folk;
        var given = Enumerable.Range(0, 3).Select(_ => game.NextName(folk)).ToList();
        Assert.Equal(game.Names[folk].Take(3), given);
        Assert.Equal(3, game.NamesUsed[folk]);
        game.NamesUsed[folk] = game.Names[folk].Count;            // all used: the first again, numbered
        Assert.Equal(game.Names[folk][0] + " 2", game.NextName(folk));
    }

    [Fact]
    public void An_heir_is_much_like_the_old_ruler_and_keeps_their_path_and_stances()
    {
        var game = NewGame(3);
        int f = game.Factions.FindIndex(x => x.Ruler != null);
        var old = game.Factions[f].Ruler!;
        old.Stances[(f + 1) % game.Factions.Count] = 0.7;
        for (int i = 0; i < 20; i++)
        {
            var heir = Rulers.NewRuler(game, "heir", f, old, 400);
            Assert.Contains(heir.Name, game.Names[game.Factions[f].Folk]);
            Assert.All(Rulers.RulerTraits, t => Assert.InRange(heir.Traits[t], Math.Max(0, old.Traits[t] - 0.15), Math.Min(1, old.Traits[t] + 0.15)));
            Assert.InRange(heir.Age, 18, 40);
            Assert.Equal(old.Path != "" ? old.Path : game.Factions[f].Path, heir.Path);
            Assert.Equal(old.Stances, heir.Stances);
            Assert.Equal(400, heir.Since);
        }
        var strongest = Rulers.NewRuler(game, "strongest", f, old, 400);
        Assert.True(strongest.Traits["charisma"] >= 0.5 && strongest.Traits["ambition"] >= 0.5);
    }

    [Fact]
    public void Hardship_raises_grievance_and_a_charismatic_ruler_dampens_it()
    {
        double After(double charisma)
        {
            var game = NewGame();
            int f = game.Factions.FindIndex(x => x.Ruler != null && x.People > 0);
            game.Factions[f].Grievance = 0;
            game.Factions[f].Ruler!.Traits["charisma"] = charisma;
            game.Factions[f].Ruler!.Age = 0;                          // it will not die this year
            game.Factions[f].Traits["greed"] = 0;                     // nor will the grievance rule strike
            var felt = game.Factions.Select(_ => new Rulers.Felt(1.0, 1.0, 1.0, 0.0, 1)).ToList();
            Rulers.Year(game, felt, 301);
            return game.Factions[f].Grievance;
        }
        Assert.True(After(0.0) > 0);
        Assert.Equal(After(0.0) * 0.5, After(1.0), 9);
    }

    [Fact]
    public void A_charismatic_ruler_of_another_path_turns_the_faction_in_time()
    {
        var game = NewGame(5);
        int f = game.Factions.FindIndex(x => x.Ruler != null && x.People > 0
                                             && Rulers.Choosable(game, x).Any(p => p != x.Path));
        var faction = game.Factions[f];
        string other = Rulers.Choosable(game, faction).First(p => p != faction.Path);
        var ruler = faction.Ruler!;
        ruler.Path = other;
        ruler.Traits["charisma"] = 1.0;
        ruler.Effort = Rulers.ConversionEffort - 0.001;
        ruler.Age = 0;
        faction.Grievance = 0;
        faction.Traits["greed"] = 0;
        string before = faction.Path!;
        Rulers.Year(game, game.Factions.Select(_ => new Rulers.Felt(0, 0, 0, 1, 1)).ToList(), 301);
        Assert.Equal(other, faction.Path);
        Assert.Equal(1, faction.PathChanges);
        if (faction.Held != null)
            Assert.Contains(before, faction.Held);
        Assert.Contains(game.Events, e => e.What == "path_by_ruler" && e.Factions.Contains(f));
    }

    [Fact]
    public void Prejudices_drift_toward_the_new_path_s()
    {
        var game = NewGame();
        var faction = game.Factions.First(x => x.Prejudice.Count > 0);
        string path = game.Settings.PathSuits.Keys.First(p => p != faction.Path);
        double target = game.Settings.PathOpinion(faction.Path, path);
        faction.Prejudice[path] = target + 1.0;
        Rulers.DriftPrejudice(game, faction);
        Assert.Equal(target + Math.Pow(0.5, 1 / Rulers.PrejudiceHalfLife), faction.Prejudice[path], 9);
    }

    [Fact]
    public void Years_of_rulers_come_and_go_with_events_and_play_out_the_same()
    {
        string Run()
        {
            var game = NewGame(9);
            for (int i = 0; i < 80; i++)
                YearCore.RunYear(game);
            var kinds = game.Events.Select(e => e.What).ToHashSet();
            Assert.Contains("succession", kinds);
            Assert.All(game.Factions.Where(f => f.People > 0 && f.Ruler != null), f => Assert.InRange(f.Grievance, 0, 10));
            return string.Join("|", game.Events.Select(e => e.Year + e.Text));
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Opinions_change_with_the_years_and_their_words_are_told()
    {
        var game = NewGame(2);
        var before = game.Factions.Select(f => new Dictionary<int, double>(f.Opinions)).ToList();
        for (int i = 0; i < 40; i++)
            YearCore.RunYear(game);
        Assert.Contains(Enumerable.Range(0, game.Factions.Count),
            f => game.Factions[f].Opinions.Any(o => before[f].TryGetValue(o.Key, out double was) && Math.Abs(was - o.Value) > 1e-6));
        Assert.Contains(game.Events, e => e.What == "opinion" && e.Factions.Count == 2);
    }

    [Fact]
    public void A_rule_s_blame_sours_memory_of_those_with_food_to_spare()
    {
        var game = NewGame();
        var markets = OneYearOfTrade(game);
        var spare = YearCore.FoodToSpare(game, markets);
        Assert.NotEmpty(spare);
        var pair = game.Memory.Keys.First(p => spare.Contains(p.Item2) && p.Item1 != p.Item2);
        double before = game.Memory[pair];
        var blame = new Dictionary<(int, int), double> { [pair] = 0.2 };
        YearCore.UpdateFactions(game, markets, new List<(string, int, int, double)>(), game.Year + 1, blame);
        double fade = Math.Pow(0.5, 1 / game.Settings.MemoryHalfLife);
        Assert.Equal(Math.Clamp(before * fade - 0.2, -1, 1), game.Memory[pair], 12);
    }

    [Fact]
    public void Shortages_count_the_staples_together()
    {
        var market = new Market();
        market.Needed["grain"] = 2; market.Needed["herbs"] = 2; market.Needed["metal"] = 1;
        market.Made["grain"] = 3;
        var missing = YearCore.Shortages(market, new[] { "grain", "herbs" });
        Assert.Equal(0.25, missing["staples"]);
        Assert.Equal(0.25, missing["herbs"]);
        Assert.Equal(1.0, missing["metal"]);
        Assert.Equal(1.0, YearCore.GoodsShort(market, new[] { "grain", "herbs" }));
    }

    [Fact]
    public void A_planner_short_of_metal_hopes_to_trade_and_the_greedy_covet()
    {
        var shortOf = new FactionNow { MetalSupply = 0, MetalNeed = 1,
                                       Traits = new() { ["planning"] = 1.0, ["greed"] = 0.0, ["risk"] = 0.5 } };
        var rich = new FactionNow { MetalSupply = 3, MetalNeed = 1, Traits = new() { ["planning"] = 1.0, ["greed"] = 0.0, ["risk"] = 0.5 } };
        Assert.Equal(Opinions.TradeHope, Opinions.MetalPart(shortOf, rich), 12);
        shortOf.Traits["greed"] = 1.0;
        Assert.Equal(-Opinions.Coveting, Opinions.MetalPart(shortOf, rich), 12);
        shortOf.Traits["greed"] = 0.5;
        Assert.Equal(Opinions.TradeHope * 0.5 - Opinions.Coveting * 0.5, Opinions.MetalPart(shortOf, rich), 12);
        Assert.Equal(0.0, Opinions.MetalPart(rich, shortOf), 12);
    }

    [Fact]
    public void A_word_changes_downwards_only_past_the_margin()
    {
        var lows = new[] { -1.0, -0.6, -0.3, 0.0, 0.5, 0.8 };
        Assert.Equal(4, Opinions.KeepBand(0.47, lows, 4, Opinions.OpinionMargin));
        Assert.Equal(3, Opinions.KeepBand(0.44, lows, 4, Opinions.OpinionMargin));
        Assert.Equal(5, Opinions.KeepBand(0.8, lows, 4, Opinions.OpinionMargin));
    }

    [Fact]
    public void A_faction_with_no_people_neither_thinks_of_others_nor_is_thought_of()
    {
        var game = NewGame();
        int dead = game.Factions.FindIndex(f => f.People <= 0);
        int alive = game.Factions.FindIndex(f => f.People > 0);
        var before = new Dictionary<int, double>(game.Factions[dead].Opinions);
        double aliveOfDead = game.Factions[alive].Opinions.GetValueOrDefault(dead);
        YearCore.RunYear(game);
        Assert.Equal(before, game.Factions[dead].Opinions);
        Assert.Equal(aliveOfDead, game.Factions[alive].Opinions.GetValueOrDefault(dead));
    }

    [Fact]
    public void A_ruler_pulls_the_faction_s_ways_a_tenth_of_the_way_a_year()
    {
        var game = NewGame();
        int f = game.Factions.FindIndex(x => x.Ruler != null && x.People > 0);
        var faction = game.Factions[f];
        faction.Cult = null;
        faction.Ruler!.Age = 0;
        faction.Traits["greed"] = 0;                                  // and the grievance rule will not strike
        double power = game.Settings.Governments[faction.Government].RulerPower;
        double pull = power * faction.Ruler.Traits["charisma"];
        double was = faction.Traits["planning"];
        double target = faction.Values["planning"] + pull * (faction.Ruler.Traits["planning"] - faction.Values["planning"]);
        Rulers.Year(game, game.Factions.Select(_ => new Rulers.Felt(0, 0, 0, 1, 1)).ToList(), 301);
        Assert.Equal(Math.Clamp(was + Rulers.TraitDrift * (target - was), 0, 1), faction.Traits["planning"], 12);
    }

    [Fact]
    public void A_starving_year_angers_the_people_more_than_a_fed_one()
    {
        // A copy of the test world whose people weigh only hunger (GRIEVANCE_WEIGHTS), so nothing else can tell
        // the two years apart.
        string folder = Path.Combine(Scratch.Root, "wr-hunger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!.AsObject();
        data["continue"]!["settings"]!["leaders"]!["GRIEVANCE_WEIGHTS"] = new JsonObject
        {
            ["hunger"] = 3.0, ["shortages"] = 0.0, ["losses"] = 0.0, ["ruler_against_values"] = 0.0,
        };
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        double GrievanceAfter(bool starve)
        {
            var game = new Game(ExportedWorld.Read(folder), 4);
            if (starve)
                foreach (var town in game.Towns)
                    town.Yields.Clear();
            int f = game.Factions.FindIndex(x => x.Ruler != null && x.People > 1000);
            YearCore.RunYear(game);
            return game.Factions[f].Grievance;
        }
        Assert.True(GrievanceAfter(true) > GrievanceAfter(false));
    }

    [Fact]
    public void A_greedy_faction_s_people_grumble_now_and_then()
    {
        var game = NewGame(6);
        int f = game.Factions.FindIndex(x => x.Ruler != null && x.People > 0);
        game.Factions[f].Traits["greed"] = 0.9;
        for (int i = 0; i < 200 && !game.Events.Any(e => e.What == "grievance"); i++)
            Rules.FactionRules(game, f, new Rulers.Felt(0, 0, 0, 1, 1), 301 + i);
        Assert.Contains(game.Events, e => e.What == "grievance" && e.Factions.Contains(f));
    }

    [Fact]
    public void A_faction_s_card_shows_the_game_s_ruler_and_government_after_loading_too()
    {
        var game = NewGame(9);
        for (int i = 0; i < 60; i++)
            YearCore.RunYear(game);
        int f = game.Events.First(e => e.What == "succession").Factions[0];
        var card = Book.Card(game.World, f, game.Year);
        Assert.Equal(game.Factions[f].Ruler?.Name, card.Ruler);
        Assert.Equal(game.Factions[f].Government, card.Government);
        Assert.Equal(game.Factions[f].Path, card.Path);
        string path = Path.Combine(Scratch.Root, "wr-card-" + Guid.NewGuid().ToString("N"), "game.json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Assert.Equal(card, Book.Card(loaded.World, f, loaded.Year) with { PeopleByYear = card.PeopleByYear });
        Assert.Equal(card.PeopleByYear, Book.Card(loaded.World, f, loaded.Year).PeopleByYear);
    }
}

/// <summary>The lore of 1 November 2026 in the game: rulers age by their people's lifespan, peoples think of each other by
/// race.</summary>
public class LoreTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static Game NewGame(long seed = 1) => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), seed);
    static readonly System.Text.Json.Nodes.JsonObject Expected =
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["trade"]!.AsObject();

    [Fact]
    public void Each_faction_s_rulers_age_as_the_generator_says()
    {
        var game = NewGame();
        var want = Expected["age_scales"]!.AsArray();
        Assert.Equal(want.Count, game.Factions.Count);
        for (int f = 0; f < game.Factions.Count; f++)
            Assert.Equal((double)want[f]!, Rulers.AgeScale(game, f));
        Assert.Contains(Enumerable.Range(0, game.Factions.Count), f => Rulers.AgeScale(game, f) > 1.0);
    }

    [Fact]
    public void A_long_lived_people_crowns_older_rulers_who_die_later()
    {
        var game = NewGame();
        int elder = Enumerable.Range(0, game.Factions.Count).First(f => Rulers.AgeScale(game, f) > 2.0);
        int human = Enumerable.Range(0, game.Factions.Count).First(f => Rulers.AgeScale(game, f) == 1.0);
        double scale = Rulers.AgeScale(game, elder);
        Assert.Equal(Rulers.DeathChanceAt(40), Rulers.DeathChanceAt((int)Math.Round(40 * scale), scale), 2);
        Assert.True(Rulers.DeathChanceAt(200, scale) < Rulers.DeathChanceAt(200));
        var young = new List<int>();
        var old = new List<int>();
        for (long seed = 1; seed <= 20; seed++)
        {
            var g = NewGame(seed);
            old.Add(Rulers.NewRuler(g, "chosen", elder, null, g.Year).Age);
            young.Add(Rulers.NewRuler(NewGame(seed), "chosen", human, null, g.Year).Age);
        }
        Assert.All(young, a => Assert.InRange(a, 35, 60));
        Assert.All(old, a => Assert.InRange(a, Py.Round(35 * scale), Py.Round(60 * scale)));
    }

    /// <summary>A game on a copy of the fixture with leader_settings.py's LIFESPAN_SCALING set to `scaling`.</summary>
    static Game WithScaling(double scaling)
    {
        string folder = Path.Combine(Scratch.Root, "wr-lore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        data["continue"]!["settings"]!["leaders"]!["LIFESPAN_SCALING"] = scaling;
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), 1);
    }

    [Fact]
    public void LIFESPAN_SCALING_sets_how_fully_a_lifespan_counts()
    {
        var full = NewGame();
        var none = WithScaling(0.0);
        var half = WithScaling(0.5);
        for (int f = 0; f < full.Factions.Count; f++)
        {
            Assert.Equal(1.0, Rulers.AgeScale(none, f));
            Assert.Equal(1.0 + 0.5 * (Rulers.AgeScale(full, f) - 1.0), Rulers.AgeScale(half, f), 12);
        }
    }

    [Fact]
    public void A_long_lived_ruler_is_not_sure_to_die_at_an_age_that_would_end_a_human()
    {
        // At 300 a human's chance of death is past one; an elder people's ruler of 300 counts as a man of 300 / scale.
        int survived = 0;
        for (long seed = 1; seed <= 10; seed++)
        {
            var game = NewGame(seed);
            int elder = Enumerable.Range(0, game.Factions.Count)
                .First(f => Rulers.AgeScale(game, f) > 2.0 && game.Factions[f].Ruler != null);
            var ruler = game.Factions[elder].Ruler!;
            ruler.Age = 299;
            Rulers.Year(game, game.Factions.Select(_ => new Rulers.Felt(0, 0, 0, 1, 1)).ToList(), 301);
            if (game.Factions[elder].Ruler == ruler)
                survived++;
        }
        Assert.True(survived > 0);
    }

    [Fact]
    public void A_split_s_rulers_age_as_its_kin_s()
    {
        var game = NewGame();
        game.Waiting.Clear();
        int parent = Enumerable.Range(0, game.Factions.Count).First(f => Rulers.AgeScale(game, f) > 2.0 && Splits.Holdings(game, f).Count >= 2);
        var town = Splits.Holdings(game, parent).Last();
        game.Towns[town].Origin = parent;
        int child = Splits.Secede(game, new List<int> { town }, parent, "divided from their rulers",
                                  game.Factions.Select(_ => new YearCore.Hardship()).ToList(), game.Year + 1);
        Assert.Equal(Rulers.AgeScale(game, parent), Rulers.AgeScale(game, child));
        Assert.True(game.Factions[child].Ruler!.Age > 60);
    }
}

