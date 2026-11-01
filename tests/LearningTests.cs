// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Learning and capitals (step 7): against the generator's own learning.Learning and capitals.Capitals where
/// every chance is 0 or 1 (tests/fixtures/make_expected.py, "learning"), and by what must follow where dice decide.</summary>
public class LearningTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["learning"]!.AsObject();

    /// <summary>A game on a copy of the fixture whose learning and split settings are changed by `change`.</summary>
    static Game WithSettings(Action<JsonObject, JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Path.GetTempPath(), "wr-learning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        var settings = data["continue"]!["settings"]!;
        change(settings["learning"]!.AsObject(), settings["splits"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    /// <summary>Every chance to learn 1, to work a path out 0, to forget 1: no dice decide (as make_expected.py).</summary>
    static Game Certain() => WithSettings((learning, splits) =>
    {
        learning["LEARN_BY_TRADE"] = 1.0;
        learning["LEARN_BY_ALLIANCE"] = 1.0;
        learning["LEARN_BY_BORDER"] = 1.0;
        learning["LEARN_BY_CONQUEST"] = 1.0;
        learning["LEARN_BY_EFFORT"] = 0.0;
        learning["FORGET_CHANCE"] = 1.0;
        splits["CAPITAL_CHOICE"] = "largest";
        splits["CAPITAL_MOVE_CHANCE"] = 1.0;
    });

    /// <summary>No path learned or forgotten by chance (to test one way at a time).</summary>
    static void NoChances(JsonObject learning)
    {
        foreach (var name in new[] { "LEARN_BY_TRADE", "LEARN_BY_ALLIANCE", "LEARN_BY_BORDER", "LEARN_BY_CONQUEST",
                                     "LEARN_BY_EFFORT", "FORGET_CHANCE" })
            learning[name] = 0.0;
    }

    static List<(string Good, int Seller, int Buyer, double Amount)> OneYearOfTrade(Game game)
    {
        var harvest = Enumerable.Range(0, game.Towns.Count).Select(i =>
        {
            var faction = game.Towns[i].Holder is int h ? game.Factions[h] : null;
            return YearCore.PathHarvest(1.0, faction?.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings);
        }).ToList();
        return Trade.TradeGoods(game, Trade.MakeMarkets(game, harvest));
    }

    static List<List<object>> SkillsOf(FactionNow faction) =>
        faction.Skills.Select(p => new List<object> { p.Key, p.Value }).ToList();

    static List<List<object>> SkillsOf(JsonNode node) =>
        node.AsArray().Select(p => new List<object> { (string)p![0]!, (double)p[1]! }).ToList();

    static List<string> Sorted(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public void An_opened_world_holds_the_skills_and_hybrids_the_generator_works_out_in_its_order()
    {
        var game = Certain();
        var start = Expected["start"]!;
        for (int f = 0; f < game.Factions.Count; f++)
        {
            Assert.Equal(SkillsOf(start["skills"]![f]!), SkillsOf(game.Factions[f]));
            Assert.Equal(start["hybrids"]![f]!.AsArray().Select(h => (string)h!), Sorted(game.Factions[f].Unlocked));
        }
    }

    [Fact]
    public void A_year_of_learning_agrees_with_the_generator_s()
    {
        var game = Certain();
        var deals = OneYearOfTrade(game);
        var start = Learning.Remember(game);
        var allied = Expected["allied"]!.AsArray().Select(a => (int)a!).ToList();
        game.Alliances.Clear();
        game.Alliances.Add((allied[0], allied[1]));
        int taken = (int)Expected["taken"]!, winner = (int)Expected["winner"]!;
        Assert.Equal(taken, Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined && game.Towns[t].Holder != null));
        game.Towns[taken].Holder = winner;
        game.Towns[taken].ConqueredIn = 301;

        Learning.Year(game, start, deals, 301);

        Assert.Equal(Expected["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                     game.Events.Select(e => (e.Text, e.What)));
        for (int f = 0; f < game.Factions.Count; f++)
        {
            Assert.Equal(Expected["held"]![f]!.AsArray().Select(p => (string)p!), Sorted(game.Factions[f].Held ?? new()));
            Assert.Equal(SkillsOf(Expected["skills"]![f]!), SkillsOf(game.Factions[f]));
            Assert.Equal(Expected["hybrids"]![f]!.AsArray().Select(h => (string)h!), Sorted(game.Factions[f].Unlocked));
        }
        Assert.Contains(game.Events, e => e.What == "path_learned_conquest");
        Assert.Contains(game.Events, e => e.What == "path_learned_alliance");
        Assert.Contains(game.Events, e => e.What == "hybrid_unlocked");

        // Capitals, on the same towns: CAPITAL_CHOICE "largest", and with CAPITAL_MOVE_CHANCE 1 every faction of two
        // towns or more moves to its largest other town.
        int events = game.Events.Count;
        Capitals.Mend(game, 301);
        Assert.Equal(Expected["capitals"]!["mended"]!.AsArray().Select(c => (int?)c), game.Factions.Select(f => f.Capital));
        Capitals.Mend(game, 301, moving: true);
        Assert.Equal(Expected["capitals"]!["moved"]!.AsArray().Select(c => (int?)c), game.Factions.Select(f => f.Capital));
        Assert.Equal(Expected["capitals"]!["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                     game.Events.Skip(events).Select(e => (e.Text, e.What)));
    }

    [Fact]
    public void A_faction_that_has_just_risen_gets_its_START_PATHS_but_not_its_own_path_s_family()
    {
        var game = WithSettings((learning, _) =>
        {
            NoChances(learning);
            learning["START_PATHS"]!["glassmakers_enclave"] = new JsonArray("attunement", "transmutation");
        });
        int f = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].Lineage == "glassmakers_enclave");
        Assert.Equal("transmutation", game.Factions[f].Path);
        game.Factions[f].Held = null;
        game.Factions[f].Skills.Clear();
        Learning.Year(game, Learning.Remember(game), new List<(string, int, int, double)>(), 301);
        Assert.Equal(new[] { "attunement" }, game.Factions[f].Held!);
        Assert.Equal(game.Settings.LearnedSkill, game.Factions[f].Skills["attunement"]);
        Assert.DoesNotContain(game.Events, e => e.Factions.Contains(f));      // welcomed in silence
    }

    [Fact]
    public void A_people_works_out_the_paths_that_suit_them_by_their_own_effort()
    {
        var game = WithSettings((learning, _) => { NoChances(learning); learning["LEARN_BY_EFFORT"] = 1000.0; });
        int f = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].People > 0);
        var faction = game.Factions[f];
        var fit = Rulers.PathFit(game, faction.Values.Count > 0 ? faction.Values : faction.Traits);
        var expected = game.Settings.PathSuits.Keys.Where(p => !Learning.Holds(faction, p) && fit[p] * 1000.0 > 1.0).ToList();
        Learning.Year(game, Learning.Remember(game), new List<(string, int, int, double)>(), 301);
        Assert.All(expected, p => Assert.Contains(p, faction.Held!));
        var told = game.Events.Where(e => e.Factions.SequenceEqual(new[] { f })).ToList();
        Assert.Equal(expected.Count, told.Count(e => e.What == "path_learned_effort"));
        Assert.Contains(told, e => e.Text == "the " + faction.Name + " learn " + expected[0] + " by their own effort");
    }

    [Fact]
    public void Effort_follows_how_well_a_path_suits_the_people()
    {
        // Every path suits values of one half halfway: at LEARN_BY_EFFORT 1 each is learned with a chance of one half.
        var game = WithSettings((learning, _) => { NoChances(learning); learning["LEARN_BY_EFFORT"] = 1.0; });
        int f = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].People > 0);
        var faction = game.Factions[f];
        foreach (var trait in faction.Values.Keys.ToList())
            faction.Values[trait] = 0.5;
        int open = game.Settings.PathSuits.Keys.Count(p => !Learning.Holds(faction, p));
        Learning.Year(game, Learning.Remember(game), new List<(string, int, int, double)>(), 301);
        int learned = game.Events.Count(e => e.What == "path_learned_effort" && e.Factions.SequenceEqual(new[] { f }));
        Assert.InRange(learned, 1, open - 1);
    }

    [Fact]
    public void A_hybrid_is_told_once_when_its_halves_first_come_together()
    {
        var game = WithSettings((learning, _) => NoChances(learning));
        int f = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].People > 0
                                                                     && game.Factions[i].Path == "handcraft");
        game.Factions[f].Held!.Add("attunement");
        Learning.Year(game, Learning.Remember(game), new List<(string, int, int, double)>(), 301);
        bool Runecarving(GameEvent e) => e.What == "hybrid_unlocked" && e.Facts["hybrid"] == "runecarving";
        Assert.Equal("the " + game.Factions[f].Name + " join handcraft and attunement into runecarving",
                     game.Events.Single(Runecarving).Text);
        Assert.Equal(1.0, game.Factions[f].Skills["runecarving"]);
        int told = game.Events.Count(e => e.What == "hybrid_unlocked");
        Learning.Year(game, Learning.Remember(game), new List<(string, int, int, double)>(), 302);
        Assert.Equal(told, game.Events.Count(e => e.What == "hybrid_unlocked"));      // none told again
    }

    [Fact]
    public void A_learned_path_is_kept_while_someone_in_touch_holds_it()
    {
        var game = WithSettings((learning, _) => { NoChances(learning); learning["FORGET_CHANCE"] = 1.0; });
        int a = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].People > 0 && game.Factions[i].Held!.Count > 0);
        int b = Enumerable.Range(0, game.Factions.Count).First(i => i != a && game.Factions[i].People > 0);
        string kept = Sorted(game.Factions[a].Held!)[0];
        game.Factions[a].Held!.Add("gravecraft");
        game.Factions[b].Held!.Add(kept);
        foreach (var faction in game.Factions)
            faction.Border.Clear();
        game.Alliances.Clear();
        game.Alliances.Add((Math.Min(a, b), Math.Max(a, b)));
        game.Factions[b].Held!.Remove("gravecraft");
        Learning.Year(game, Learning.Remember(game), new List<(string, int, int, double)>(), 301);
        Assert.Contains(kept, game.Factions[a].Held!);
        if (game.Factions[b].Path != "gravecraft")
            Assert.DoesNotContain("gravecraft", game.Factions[a].Held!);
        Assert.Contains(game.Events, e => e.What == "path_forgotten" && e.Facts["faction"] == game.Factions[a].Name);
    }

    [Fact]
    public void With_PATHS_AS_SKILLS_off_no_one_learns()
    {
        var game = WithSettings((learning, _) => { learning["PATHS_AS_SKILLS"] = false; learning["LEARN_BY_EFFORT"] = 1000.0; });
        var before = game.Factions.Select(f => f.Held == null ? null : Sorted(f.Held)).ToList();
        Learning.Year(game, Learning.Remember(game), OneYearOfTrade(game), 301);
        Assert.Empty(game.Events);
        Assert.Equal(before, game.Factions.Select(f => f.Held == null ? null : Sorted(f.Held)).ToList());
    }

    [Fact]
    public void A_lost_capital_is_replaced_by_one_of_the_faction_s_towns_and_the_book_tells_it()
    {
        for (long seed = 1; seed <= 5; seed++)
        {
            var game = WithSettings((_, splits) => { splits["CAPITAL_CHOICE"] = "weighted"; splits["CAPITAL_MOVE_CHANCE"] = 0.0; }, seed);
            int f = Enumerable.Range(0, game.Factions.Count).First(i => Capitals.Holdings(game, i).Count >= 3);
            int lost = game.Factions[f].Capital!.Value;
            int other = Enumerable.Range(0, game.Factions.Count).First(i => i != f);
            game.Towns[lost].Holder = other;
            Capitals.Mend(game, 301, moving: true);
            Assert.Contains(game.Factions[f].Capital!.Value, Capitals.Holdings(game, f));
            var told = game.Events.Single(e => e.What == "new_capital" && e.Factions.SequenceEqual(new[] { f }));
            Assert.Equal(game.World.Towns[lost].Name, told.Facts["old"]);
            Assert.DoesNotContain(game.Events, e => e.What == "capital_moved");      // no moves at 0
        }
    }

    [Fact]
    public void A_weighted_capital_is_usually_but_not_always_the_largest_town()
    {
        var game = WithSettings((_, splits) => splits["CAPITAL_CHOICE"] = "weighted");
        int f = Enumerable.Range(0, game.Factions.Count).OrderByDescending(i => Capitals.Holdings(game, i).Count).First();
        var held = Capitals.Holdings(game, f);
        int largest = Capitals.Largest(game, held);
        var picks = Enumerable.Range(0, 400).Select(_ => Capitals.Pick(game, held)).ToList();
        double share = game.Towns[largest].Population / (double)held.Sum(t => Math.Max(1, game.Towns[t].Population));
        Assert.InRange(picks.Count(p => p == largest) / 400.0, share - 0.1, share + 0.1);
        Assert.All(picks, p => Assert.Contains(p, held));
        Assert.True(picks.Distinct().Count() > 1);
    }

    [Fact]
    public void A_breakaway_picks_its_first_capital_in_silence()
    {
        var game = WithSettings((_, splits) => { splits["CAPITAL_CHOICE"] = "largest"; splits["CAPITAL_MOVE_CHANCE"] = 0.5; });
        int f = Enumerable.Range(0, game.Factions.Count).First(i => Capitals.Holdings(game, i).Count >= 2
            && Capitals.Largest(game, Capitals.Holdings(game, i)) != Capitals.Holdings(game, i)[^1]);
        game.CapitalsKnown.Remove(f);
        game.Factions[f].Capital = Capitals.Holdings(game, f)[^1];         // the capital a split gave it: its last town
        Capitals.Mend(game, 301);
        Assert.Equal(Capitals.Largest(game, Capitals.Holdings(game, f)), game.Factions[f].Capital);
        Assert.Contains(f, game.CapitalsKnown);
        Assert.Empty(game.Events);
    }

    [Fact]
    public void No_one_learns_from_a_faction_that_is_no_more()
    {
        var game = WithSettings((learning, _) => { NoChances(learning); learning["LEARN_BY_ALLIANCE"] = 1.0; });
        int a = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].People > 0);
        int gone = Enumerable.Range(0, game.Factions.Count).First(i => i != a && game.Factions[i].People <= 0);
        foreach (var faction in game.Factions)
            faction.Border.Clear();
        game.Alliances.Clear();
        game.Alliances.Add((Math.Min(a, gone), Math.Max(a, gone)));
        string path = game.Settings.PathSuits.Keys.First(p => !Learning.Holds(game.Factions[a], p));
        game.Factions[gone].Held!.Add(path);
        Learning.Year(game, Learning.Remember(game), new List<(string, int, int, double)>(), 301);
        Assert.DoesNotContain(path, game.Factions[a].Held!);
    }

    [Fact]
    public void A_breakaway_keeps_its_kin_s_hybrids_and_is_not_told_them_again()
    {
        var game = WithSettings((learning, _) => NoChances(learning));
        game.Waiting.Clear();                       // no faction of the same people rises in its place
        int parent = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].Path == "handcraft"
                                                                          && Capitals.Holdings(game, i).Count >= 2);
        game.Factions[parent].Held!.Add("attunement");
        Learning.Settle(game);
        Assert.Contains("runecarving", game.Factions[parent].Unlocked);
        int town = Capitals.Holdings(game, parent)[^1];
        game.Towns[town].Origin = parent;
        int child = Splits.Secede(game, new List<int> { town }, parent, "divided from their rulers",
                                  game.Factions.Select(_ => new YearCore.Hardship()).ToList(), 301);
        Assert.Equal(game.Factions[parent].Unlocked, game.Factions[child].Unlocked);
        Learning.Year(game, Learning.Remember(game), new List<(string, int, int, double)>(), 301);
        Assert.DoesNotContain(game.Events, e => e.What == "hybrid_unlocked");
    }

    [Fact]
    public void The_largest_town_is_the_first_of_those_with_the_most_people()
    {
        var game = WithSettings((_, splits) => splits["CAPITAL_CHOICE"] = "largest");
        var held = Enumerable.Range(0, game.Towns.Count).Where(t => !game.Towns[t].Ruined).Take(3).ToList();
        foreach (int t in held)
            game.Towns[t].People = 500;
        Assert.Equal(held[0], Capitals.Largest(game, held));
        Assert.Equal(held[0], Capitals.Pick(game, held));
    }

    [Fact]
    public void A_weighted_pick_goes_by_people_not_by_town()
    {
        var game = WithSettings((_, splits) => splits["CAPITAL_CHOICE"] = "weighted");
        var towns = Enumerable.Range(0, game.Towns.Count).Where(t => !game.Towns[t].Ruined).Take(4).ToList();
        game.Towns[towns[0]].People = 9700;
        foreach (int t in towns.Skip(1))
            game.Towns[t].People = 100;        // the first holds 97 of every 100
        int first = Enumerable.Range(0, 300).Count(_ => Capitals.Pick(game, towns) == towns[0]);
        Assert.InRange(first, 270, 300);
    }

    [Fact]
    public void Capitals_move_at_the_end_of_a_played_year()
    {
        var game = WithSettings((_, splits) => { splits["CAPITAL_CHOICE"] = "largest"; splits["CAPITAL_MOVE_CHANCE"] = 1.0; });
        YearCore.RunYear(game);
        Assert.Contains(game.Events, e => e.What == "capital_moved");
    }

    [Fact]
    public void A_played_century_learns_and_moves_capitals_the_same_way_from_the_same_seed_and_a_save()
    {
        var first = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 3);
        var second = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 3);
        for (int i = 0; i < 50; i++)
            YearCore.RunYear(first);
        string path = Path.Combine(Path.GetTempPath(), "wr-learning-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, first);
        var loaded = SaveFile.Read(path);
        for (int i = 0; i < 50; i++)
        {
            YearCore.RunYear(first);
            YearCore.RunYear(loaded);
        }
        for (int i = 0; i < 100; i++)
            YearCore.RunYear(second);
        Assert.Contains(second.Events, e => e.Kind == "path_learning");
        Assert.Equal(second.Events.Select(e => e.Text), first.Events.Select(e => e.Text));
        Assert.Equal(first.Events.Select(e => e.Text), loaded.Events.Select(e => e.Text));
        Assert.Equal(first.Factions.Select(f => f.Capital), loaded.Factions.Select(f => f.Capital));
        Assert.Equal(first.Factions.Select(f => string.Join(",", Sorted(f.Unlocked))),
                     loaded.Factions.Select(f => string.Join(",", Sorted(f.Unlocked))));
    }
}
