// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Happenings, tempers and eras (step 9c): against the generator's own happenings.Happenings with the same
/// stand-in dice (tests/fixtures/make_expected.py, "happenings"), and by what must follow.</summary>
public class HappeningTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["happenings"]!.AsObject();

    /// <summary>make_expected.py's FIXED_DICE, handed out in turn, over and over; `used` counts them.</summary>
    static readonly double[] FixedDice = { 0.2, 0.0005, 0.7, 0.35, 0.001, 0.9, 0.55, 0.003, 0.15, 0.8, 0.0002, 0.45 };

    sealed class Counted
    {
        int at;
        public int Used => at;
        public double Next() => FixedDice[at++ % FixedDice.Length];
    }

    /// <summary>A game on a copy of the fixture whose settings ("continue"."settings") are changed by `change`.</summary>
    static Game WithSettings(Action<JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Scratch.Root, "wr-happen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        change(data["continue"]!["settings"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    /// <summary>make_expected.py's HAPPENING_TEST: its happenings, its tempers before the export's, its eras.</summary>
    static Game TestGame(long seed = 1) => WithSettings(settings =>
    {
        var test = Expected["test"]!;
        var happenings = settings["happenings"]!.AsObject();
        var tempers = test["TEMPERS"]!.DeepClone().AsObject();
        foreach (var (name, about) in happenings["TEMPERS"]!.AsObject())
            tempers[name] = about!.DeepClone();
        happenings["HAPPENINGS"] = test["HAPPENINGS"]!.DeepClone();
        happenings["TEMPERS"] = tempers;
        foreach (var key in new[] { "ERA_YEARS", "ERA_END_CHANCE", "ERA_ENDERS" })
            happenings[key] = test[key]!.DeepClone();
    }, seed);

    [Fact]
    public void Years_of_happenings_and_eras_agree_with_the_generator_s()
    {
        var game = TestGame();
        var dice = new Counted();
        var eraDice = new Counted();
        var blame = new Dictionary<(int, int), double>();
        var killed = new JsonArray();
        var factors = new JsonArray();
        var war = new JsonArray();
        int first = game.Year;
        for (int year = first + 1; year <= first + (int)Expected["years"]!; year++)
        {
            int start = game.Events.Count;
            Happenings.BeginYear(game, eraDice.Next, year);
            factors.Add(new JsonArray(Enumerable.Range(0, game.Towns.Count).Select(i => (JsonNode)new JsonArray(
                new[] { "growth", "harvest", "trade", "war" }.Select(k => (JsonNode)Happenings.Factor(game, i, year, k)).ToArray())).ToArray()));
            var hit = Happenings.Year(game, dice.Next, year, blame);
            killed.Add(new JsonArray(hit.OrderBy(p => p.Key).Select(p => (JsonNode)new JsonArray(p.Key, p.Value)).ToArray()));
            war.Add(new JsonArray(Happenings.WarFactors(game, year).OrderBy(p => p.Key)
                .Select(p => (JsonNode)new JsonArray(p.Key, p.Value)).ToArray()));
            Happenings.EndYear(game, eraDice.Next, year, game.Events.Skip(start));
        }

        Json.Same(Expected["people"], new JsonArray(game.Towns.Select(t => (JsonNode)t.People).ToArray()), "people");
        Assert.Equal(Expected["beliefs"]!.AsArray().Select(b => (string)b!), game.Factions.Select(f => f.Belief));
        Json.Same(Expected["blame"], new JsonArray(blame.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2)
            .Select(p => (JsonNode)new JsonArray(p.Key.Item1, p.Key.Item2, p.Value)).ToArray()), "blame");
        Json.Same(Expected["killed"], killed, "killed");
        Json.Same(Expected["factors"], factors, "factors");
        Json.Same(Expected["war"], war, "war");
        Assert.Equal((double)Expected["splits"]!, Happenings.TemperNow(game).Splits);
        Json.Same(Expected["state"], Happenings.Write(game), "state");
        Assert.Equal(Expected["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                     game.Events.Select(e => (e.Text, e.What)));
        Assert.Equal((int)Expected["dice_used"]![0]!, dice.Used);
        Assert.Equal((int)Expected["dice_used"]![1]!, eraDice.Used);
        // Every way a happening strikes is in it.
        foreach (var how in new[] { " spreads to ", " reaches ", " strikes ", " at ", "driven off", "blames" })
            Assert.Contains(game.Events, e => e.Kind == "happening" && e.Text.Contains(how));
        Assert.Contains(game.Events, e => e.What == "belief_after_happening");
        Assert.True(game.Events.Count(e => e.What == "era_begins") >= 2);
    }

    /// <summary>A game with only the happenings and tempers given (as JSON), eras of ERA_YEARS [5, 5].</summary>
    static Game Only(string happenings, string tempers, double endChance = 0.0, string enders = "[]", long seed = 1) =>
        WithSettings(s =>
        {
            var h = s["happenings"]!;
            h["HAPPENINGS"] = JsonNode.Parse(happenings);
            h["TEMPERS"] = JsonNode.Parse(tempers);
            h["ERA_YEARS"] = new JsonArray(5, 5);
            h["ERA_END_CHANCE"] = endChance;
            h["ERA_ENDERS"] = JsonNode.Parse(enders);
        }, seed);

    [Fact]
    public void An_era_lasts_its_drawn_years_and_the_next_begins_after_its_last()
    {
        var game = TestGame();
        game.Eras.Clear();
        Happenings.BeginYear(game, () => 0.0, 301);                          // the first temper, the shortest era
        Assert.Equal(("calm", 301, 303), game.Eras[^1]);
        Happenings.BeginYear(game, () => 0.0, 303);                          // its last year: still the same era
        Assert.Single(game.Eras);
        Happenings.BeginYear(game, () => 0.999, 304);                        // over: the last temper, the longest era
        Assert.Equal(game.Settings.Tempers[^1].Name, game.Eras[^1].Temper);
        Assert.Equal((304, 309), (game.Eras[^1].First, game.Eras[^1].Last));
    }

    [Fact]
    public void A_temper_s_mood_multiplies_one_happening_s_chance_once_more()
    {
        var game = TestGame();
        game.Eras.Clear();
        game.Eras.Add(("wild", 301, 310));
        var quake = game.Settings.Happenings.Single(h => h.Name == "quake");
        var blight = game.Settings.Happenings.Single(h => h.Name == "blight");
        Assert.Equal(0.0015 * 3.0 * 2.0, Happenings.ChanceOf(game, quake));
        Assert.Equal(0.003 * 3.0, Happenings.ChanceOf(game, blight));
    }

    [Fact]
    public void A_faction_that_holds_its_belief_fast_is_not_turned_by_a_happening()
    {
        var game = Only("""{"exodus": {"chance": 1.0, "years": 0, "effects": {"belief": ["worship", 1.0]}}}""",
                        """{"steady": {"weight": 1, "chance": 1.0}}""");
        foreach (var faction in game.Factions)
            faction.Traits["faith"] = 1.0;                                   // 2 x (1 - faith) = 0: never shifts
        var before = game.Factions.Select(f => f.Belief).ToList();
        Happenings.Year(game, () => 0.0, 301, new Dictionary<(int, int), double>());
        Assert.Equal(before, game.Factions.Select(f => f.Belief));
        Assert.Contains(game.Events, e => e.What == "exodus");
    }

    [Fact]
    public void A_happening_s_dead_count_for_a_mass_death_and_its_lasting_harvest_comes_the_year_after()
    {
        var game = Only("""{"dying": {"chance": 1.0, "reach": "world", "years": 1, "effects": {"population": -0.5, "harvest": 0.5}}}""",
                        """{"steady": {"weight": 1, "chance": 1.0}}""");
        YearCore.RunYear(game);
        Assert.Contains(game.MassDeaths, d => d.Dead > 0);                  // half of every town, at once
        int town = Enumerable.Range(0, game.Towns.Count).First(i => !game.Towns[i].Ruined && game.Towns[i].Holder != null);
        int year = game.Year + 1;
        Assert.Equal(0.5, Happenings.Factor(game, town, year, "harvest"));
        var lasting = game.HappeningsLasting[town];
        game.HappeningsLasting.Clear();
        double without = YearCore.Harvests(game, year)[town];
        game.HappeningsLasting[town] = lasting;
        Assert.Equal(without * 0.5, YearCore.Harvests(game, year)[town]);
    }

    [Fact]
    public void A_big_event_ends_the_era_with_its_chance()
    {
        var game = Only("""{"quake": {"chance": 1.0, "years": 0, "effects": {}}}""",
                        """{"calm": {"weight": 1, "chance": 1.0}, "wild": {"weight": 1, "chance": 1.0}}""",
                        endChance: 1.0, enders: """["quake"]""");
        game.Eras.Clear();
        for (int i = 0; i < 4; i++)
            YearCore.RunYear(game);
        Assert.Equal(4, game.Eras.Count);                                    // each ends the year it began
        Assert.All(game.Eras, era => Assert.Equal(era.First, era.Last));
    }

    [Fact]
    public void An_era_s_temper_makes_the_factions_more_or_less_brittle()
    {
        int Splits(double factor)
        {
            var game = Only("""{"nothing": {"chance": 0.0}}""",
                            "{\"held\": {\"weight\": 1, \"chance\": 1.0, \"splits\": " + factor + "}, \"other\": {\"weight\": 0}}",
                            seed: 3);
            game.Eras.Clear();
            game.Eras.Add(("held", game.Year + 1, game.Year + 1000));
            for (int i = 0; i < 30; i++)
                YearCore.RunYear(game);
            return game.Events.Count(e => e.Kind == "split");
        }
        Assert.Equal(0, Splits(0.0));
        Assert.True(Splits(20.0) > 0);
    }

    [Fact]
    public void The_export_s_happenings_are_read_exactly()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 1);
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "world-7", "world.json")))!;
        Json.Same(data["continue"]!["happenings"], Happenings.Write(game), "happenings");
        Assert.NotEmpty(game.Eras);
    }

    [Fact]
    public void A_happening_s_trade_scales_what_a_town_makes_and_crafts_and_the_metal_crafting_needs()
    {
        var market = new Market();
        market.Made["grain"] = 100;
        market.CanCraft["tools"] = 10;
        market.CraftingMetal["tools"] = 4;
        market.Needed[Trade.Metal] = 6;
        Happenings.ScaleTrade(market, 0.5);
        Assert.Equal(50, market.Made["grain"]);
        Assert.Equal(5, market.CanCraft["tools"]);
        Assert.Equal(2, market.CraftingMetal["tools"]);
        Assert.Equal(4, market.Needed[Trade.Metal]);             // 2 less metal needed for crafting
    }

    [Fact]
    public void A_faction_s_readiness_for_war_multiplies_its_chance_of_war()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 1);
        var supply = Opinions.Supplies(game, Trade.MakeMarkets(game, YearCore.Harvests(game, game.Year + 1)));
        int a = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].People > 0);
        int b = Enumerable.Range(0, game.Factions.Count).First(f => f != a && game.Factions[f].People > 0);
        double plain = War.WarChance(game, a, b, supply).Chance, back = War.WarChance(game, b, a, supply).Chance;
        game.WarReadiness = new Dictionary<int, double> { [a] = 2.0 };
        Assert.Equal(plain * 2, War.WarChance(game, a, b, supply).Chance, 12);
        Assert.Equal(back, War.WarChance(game, b, a, supply).Chance);      // only the stirred faction
    }

    [Fact]
    public void Fertility_multiplies_growth_and_one_half_is_as_before()
    {
        Game With(double? fertility)
        {
            var game = WithSettings(s => s["happenings"]!["HAPPENINGS"] = new JsonObject(), seed: 5);
            foreach (var faction in game.Factions)
            {
                faction.Traits.Remove("fertility");
                if (fertility is double f)
                    faction.Traits["fertility"] = f;
            }
            YearCore.RunYear(game);
            return game;
        }
        var none = With(null);
        var half = With(0.5);
        var full = With(1.0);
        Assert.Equal(none.Towns.Select(t => t.People), half.Towns.Select(t => t.People));
        Assert.True(full.Towns.Where(t => !t.Ruined).Sum(t => t.People) > half.Towns.Where(t => !t.Ruined).Sum(t => t.People));
    }

    [Fact]
    public void Without_happenings_there_are_no_eras_and_no_dice_are_rolled()
    {
        var game = WithSettings(s => s["happenings"]!["HAPPENINGS"] = new JsonObject());
        int eras = game.Eras.Count;
        for (int i = 0; i < 10; i++)
            YearCore.RunYear(game);
        Assert.Equal(eras, game.Eras.Count);
        Assert.False(game.AllDice.ContainsKey("happenings"));
        Assert.False(game.AllDice.ContainsKey("eras"));
        Assert.DoesNotContain(game.Events, e => e.Kind is "happening" or "era");
    }

    [Fact]
    public void Played_years_strike_and_a_save_plays_on_the_same()
    {
        var game = TestGame(seed: 6);
        for (int i = 0; i < 20; i++)
            YearCore.RunYear(game);
        Assert.Contains(game.Events, e => e.Kind == "happening");
        Assert.Contains(game.Events, e => e.What == "era_begins");
        string path = Path.Combine(Scratch.Root, "wr-happen-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Json.Same(Happenings.Write(game), Happenings.Write(loaded), "happenings");
        for (int i = 0; i < 15; i++)
        {
            YearCore.RunYear(game);
            YearCore.RunYear(loaded);
        }
        Assert.Equal(game.Events.Select(e => e.Text), loaded.Events.Select(e => e.Text));
        Json.Same(Happenings.Write(game), Happenings.Write(loaded), "happenings after");
    }
}
