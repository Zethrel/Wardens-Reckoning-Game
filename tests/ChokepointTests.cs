// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Chokepoints, part 1 (step 9d): against the generator's own chokepoints.Chokepoints, trade.make_markets and
/// trade.trade_goods over two years (tests/fixtures/make_expected.py, "chokepoints"), and by what must follow.</summary>
public class ChokepointTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["chokepoints"]!.AsObject();

    /// <summary>make_expected.py's FIXED_DICE, handed out in turn, over and over.</summary>
    static readonly double[] FixedDice = { 0.2, 0.0005, 0.7, 0.35, 0.001, 0.9, 0.55, 0.003, 0.15, 0.8, 0.0002, 0.45 };

    /// <summary>A game on a copy of the fixture whose chokepoint settings are `settings`.</summary>
    static Game WithChokepoints(JsonNode settings)
    {
        string folder = Path.Combine(Scratch.Root, "wr-choke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        data["continue"]!["settings"]!["chokepoints"] = settings.DeepClone();
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), 1);
    }

    /// <summary>The fixture with make_expected.py's chokepoint settings, contracts and sabotaged town.</summary>
    static Game TestGame()
    {
        var game = WithChokepoints(Expected["settings"]!);
        foreach (var c in Expected["contracts"]!.AsArray())
            game.Contracts[((int)c![0]!, (string)c[1]!)] = (int)c[2]!;
        foreach (var t in Expected["sabotaged"]!.AsArray())
            game.Sabotaged[(int)t![0]!] = ((string)t[1]!, (int)t[2]!);
        return game;
    }

    static JsonObject Table(Dictionary<string, double> table) =>
        new(table.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)));

    [Fact]
    public void Two_years_of_chokepoints_agree_with_the_generator_s()
    {
        var game = TestGame();
        var years = Expected["years"]!.AsArray();
        int suspicionAt = 0;
        double Suspicion() => FixedDice[suspicionAt++ % FixedDice.Length];
        for (int y = 0; y < years.Count; y++)
        {
            var want = years[y]!;
            int year = 101 + y;
            int events = game.Events.Count;
            var blame = new Dictionary<(int, int), double>();
            var factors = Chokepoints.BeginYear(game);
            var harvest = Enumerable.Range(0, game.Towns.Count).Select(i =>
            {
                var faction = game.Towns[i].Holder is int h ? game.Factions[h] : null;
                return YearCore.PathHarvest(1.0, faction?.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings) * factors[i];
            }).ToList();
            var markets = Trade.MakeMarkets(game, harvest);
            var deals = Trade.TradeGoods(game, markets);
            Chokepoints.AfterTrade(game, markets, deals, blame, year, game.DiceFor("chokepoints").Random,
                                   game.DiceFor("chokepoint parts").Random, Suspicion);

            string at = "year " + year;
            Json.Same(want["factors"], new JsonArray(factors.Select(f => (JsonNode)f).ToArray()), at + " factors");
            Json.Same(want["rituals"], new JsonArray(game.RitualsHindered.OrderBy(p => p.Key)
                .Select(p => (JsonNode)new JsonArray(p.Key, p.Value)).ToArray()), at + " rituals");
            var wantMarkets = want["markets"]!.AsArray();
            for (int i = 0; i < markets.Length; i++)
            {
                var m = markets[i];
                var w = wantMarkets[i]!;
                Json.Same(w["made"], Table(m.Made), at + " made " + i);
                Json.Same(w["needed"], Table(m.Needed), at + " needed " + i);
                Json.Same(w["bought"], Table(m.Bought), at + " bought " + i);
                Json.Same(w["sold"], Table(m.Sold), at + " sold " + i);
                Json.Same(w["stood_in"], Table(m.StoodIn), at + " stood in " + i);
                Assert.Equal(w["needs"]!.AsArray().Select(g => (string)g!), m.Needs.OrderBy(g => g, StringComparer.Ordinal));
                Assert.Equal((double)w["trade_factor"]!, m.TradeFactor);
            }
            Json.Same(want["deals"], new JsonArray(deals.Select(d => (JsonNode)new JsonArray(d.Good, d.Seller, d.Buyer, d.Amount)).ToArray()),
                      at + " deals");
            Json.Same(want["short"], Chokepoints.Write(game)["short"], at + " short");
            Json.Same(want["embargoes"], Chokepoints.Write(game)["embargoes"], at + " embargoes");
            Json.Same(want["contracts"], Chokepoints.Write(game)["contracts"], at + " contracts");
            Assert.Equal((double)want["smuggled"]!, game.Smuggled);
            Assert.Equal((int)want["suspicion_used"]!, suspicionAt);
            // The blame of a shortage is added up over the goods; in the generator their order changes from run to run
            // (a set of names), so the last digits may differ.
            var wantBlame = want["blame"]!.AsArray();
            var gotBlame = blame.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2).ToList();
            Assert.Equal(wantBlame.Count, gotBlame.Count);
            for (int k = 0; k < gotBlame.Count; k++)
            {
                Assert.Equal(((int)wantBlame[k]![0]!, (int)wantBlame[k]![1]!), gotBlame[k].Key);
                Assert.Equal((double)wantBlame[k]![2]!, gotBlame[k].Value, 1e-12);
            }
            Assert.Equal(want["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                         game.Events.Skip(events).Select(e => (e.Text, e.What)));
            if (y == 0)
                foreach (var (holder, buyer, _) in game.Embargoes.ToList())
                    game.Factions[holder].Opinions[buyer] = 1.0;
        }
        var all = game.Events.Select(e => e.What).ToList();
        foreach (var what in new[] { "embargo", "embargo_lifted", "contract_ended" })
            Assert.Contains(what, all);
        Assert.True(game.Smuggled > 0);
    }

    [Fact]
    public void Three_years_of_chokepoints_part_2_agree_with_the_generator_s()
    {
        var part2 = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["chokepoints2"]!.AsObject();
        var game = WithChokepoints(part2["settings"]!);
        // make_expected.py's war between the two largest living factions, and every two living factions allies.
        int a = (int)part2["war"]![0]!, b = (int)part2["war"]![1]!;
        game.Wars.Clear();
        game.Wars.Add(new WarNow { Attacker = a, Defender = b, Since = 100, LastBattle = 100, Sides = new[] { new HashSet<int> { a }, new HashSet<int> { b } } });
        var alive = part2["alive"]!.AsArray().Select(f => (int)f!).ToList();
        game.Alliances.Clear();
        foreach (int x in alive)
            foreach (int y in alive)
                if (x < y)
                    game.Alliances.Add((x, y));
        int rngUsed = 0, diceUsed = 0, rngAt = 0, diceAt = 0, suspicionAt = 0;
        double Suspicion() => FixedDice[suspicionAt++ % FixedDice.Length];
        double Rng() { rngUsed++; return FixedDice[rngAt++ % FixedDice.Length]; }
        double Dice() { diceUsed++; return FixedDice[diceAt++ % FixedDice.Length]; }
        var years = part2["years"]!.AsArray();
        for (int y = 0; y < years.Count; y++)
        {
            var want = years[y]!;
            int year = 101 + y;
            string at = "year " + year;
            int events = game.Events.Count;
            var blame = new Dictionary<(int, int), double>();
            var factors = Chokepoints.BeginYear(game);
            var harvest = Enumerable.Range(0, game.Towns.Count).Select(i =>
            {
                var faction = game.Towns[i].Holder is int h ? game.Factions[h] : null;
                return YearCore.PathHarvest(1.0, faction?.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings) * factors[i];
            }).ToList();
            var markets = Trade.MakeMarkets(game, harvest);
            var deals = Trade.TradeGoods(game, markets);
            Chokepoints.AfterTrade(game, markets, deals, blame, year, Rng, Dice, Suspicion);

            var kept = Chokepoints.Write(game);
            Json.Same(want["contracts"], kept["contracts"], at + " contracts");
            Json.Same(want["sabotaged"], kept["sabotaged"], at + " sabotaged");
            Json.Same(want["debased"], kept["debased"], at + " debased");
            Json.Same(want["leaks"], kept["leaks"], at + " leaks");
            Json.Same(want["counts"], kept["counts"], at + " counts");
            Assert.Equal(want["learned"]!.AsArray().Select(l => string.Join(",", l!.AsArray().Select(g => (string)g!))),
                         game.Factions.Select(f => string.Join(",", f.Learned.OrderBy(g => g, StringComparer.Ordinal))));
            Assert.Equal(want["alliances"]!.AsArray().Select(p => ((int)p![0]!, (int)p[1]!)), game.Alliances.Order());
            Json.Same(want["growth"], new JsonObject(game.DisplacedGrowth.OrderBy(p => p.Key)
                .Select(p => KeyValuePair.Create(p.Key.ToString(), (JsonNode?)p.Value))), at + " growth");
            Json.Same(want["grievance"], new JsonArray(Enumerable.Range(0, game.Factions.Count)
                .Select(f => (JsonNode?)(game.GrievanceHindered.TryGetValue(f, out double g) ? g : null)).ToArray()), at + " grievance");
            var wantBlame = want["blame"]!.AsArray();
            var gotBlame = blame.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2).ToList();
            Assert.Equal(wantBlame.Select(w => ((int)w![0]!, (int)w[1]!)), gotBlame.Select(p => p.Key));
            for (int k = 0; k < gotBlame.Count; k++)
                Assert.Equal((double)wantBlame[k]![2]!, gotBlame[k].Value, 1e-12);
            Assert.Equal(want["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                         game.Events.Skip(events).Select(e => (e.Text, e.What)));
        }
        Assert.Equal((int)part2["dice_used"]![0]!, rngUsed);
        Assert.Equal((int)part2["dice_used"]![1]!, diceUsed);
        Assert.Equal((int)part2["dice_used"]![2]!, suspicionAt);
        var all = game.Events.Select(e => e.What).ToHashSet();
        foreach (var what in new[] { "tech_leak", "debased", "contract", "contract_ended", "sabotage", "secret", "secret_betrayal" })
            Assert.Contains(what, all);
        Assert.NotEmpty(game.DisplacedGrowth);
    }

    /// <summary>A game with the chokepoint settings given as JSON (and none other), no wars and no alliances.</summary>
    static Game Part2(string settings)
    {
        var game = WithChokepoints(JsonNode.Parse(settings)!);
        game.Wars.Clear();
        game.Alliances.Clear();
        return game;
    }

    static void After(Game game, Market[] markets, List<(string, int, int, double)> deals, Dictionary<(int, int), double> blame,
                      double rng = 0.0, double dice = 0.0) =>
        Chokepoints.AfterTrade(game, markets, deals, blame, 101, () => rng, () => dice);

    static List<int> Living(Game game) =>
        Enumerable.Range(0, game.Factions.Count).Where(f => game.Factions[f].People > 0 && game.Towns.Any(t => t.Holder == f && !t.Ruined)).ToList();

    [Fact]
    public void The_leak_s_chance_is_less_the_less_of_what_it_takes_apart_a_faction_has()
    {
        var game = Part2("""{"LEAK_CHANCE": 0.5, "LEAK_PATH": "mechanism", "LEAK_FROM": "gears", "LEAK_GOOD": "engines"}""");
        int f = Living(game)[0];
        game.Factions[f].Path = "mechanism";
        game.Factions[f].Learned.Remove("engines");
        var markets = Empty(game);
        markets[TownOf(game, f)].Made["gears"] = 1.0;
        markets[TownOf(game, f)].Needed["gears"] = 4.0;                 // a quarter: 0.5 x 0.25 = 0.125
        After(game, markets, new(), new(), rng: 0.2);
        Assert.DoesNotContain("engines", game.Factions[f].Learned);
        After(game, markets, new(), new(), rng: 0.1);
        Assert.Contains("engines", game.Factions[f].Learned);
    }

    [Fact]
    public void Coin_is_debased_only_at_war_at_most_nine_tenths_and_recovers_until_it_is_gone()
    {
        var game = Part2("""{"DEBASER": "x", "DEBASED_GOOD": "coin", "DEBASE_CHANCE": 1.0, "DEBASE_STEP": 0.2, "DEBASE_RECOVERY": 0.05}""");
        var living = Living(game);
        int f = living[0];
        game.Factions[f].Lineage = "x";
        After(game, Empty(game), new(), new());
        Assert.False(game.Debased.ContainsKey(f));                         // at peace
        game.Wars.Add(new WarNow { Attacker = f, Defender = living[1], Sides = new[] { new HashSet<int> { f }, new HashSet<int> { living[1] } } });
        game.Debased[f] = 0.85;
        After(game, Empty(game), new(), new());
        Assert.Equal(0.9, game.Debased[f]);                                // 0.85 recovers to 0.8075, + 0.2: at most 0.9
        game.Wars.Clear();
        game.Debased[f] = 0.0105;                                         // recovers to 0.009975: gone
        After(game, Empty(game), new(), new());
        Assert.False(game.Debased.ContainsKey(f));
    }

    [Fact]
    public void A_contract_is_signed_with_the_maker_of_most_and_never_twice()
    {
        var game = Part2("""{"NEEDS": [{"who": "all", "good": "salt", "amount": 0.1}], "CONTRACT_CHANCE": 1.0, "CONTRACT_OPINION": 0.5}""");
        var living = Living(game);
        int buyer = living[0], small = living[1], large = living[2];
        foreach (int x in living)
            foreach (int y in living)
                game.Factions[x].Opinions[y] = 1.0;
        var markets = Empty(game);
        markets[TownOf(game, small)].Made["salt"] = 5.0;
        markets[TownOf(game, large)].Made["salt"] = 10.0;
        After(game, markets, new(), new());
        Assert.Equal(large, game.Contracts[(buyer, "salt")]);
        game.Contracts[(buyer, "salt")] = small;                          // held already: not signed again
        After(game, markets, new(), new());
        Assert.Equal(small, game.Contracts[(buyer, "salt")]);
    }

    [Fact]
    public void A_town_wrecked_already_is_not_wrecked_again()
    {
        var game = Part2("""{"NEEDS": [{"who": "all", "good": "salt", "amount": 0.1}], "SABOTAGE_CHANCE": 1.0, "SABOTAGE_YEARS": [3, 3]}""");
        var living = Living(game);
        int a = living[0], b = living[1];
        game.Wars.Add(new WarNow { Attacker = a, Defender = b, Sides = new[] { new HashSet<int> { a }, new HashSet<int> { b } } });
        var markets = Empty(game);
        int target = TownOf(game, b);
        markets[target].Made["salt"] = 5.0;
        game.Sabotaged[target] = ("salt", 500);
        After(game, markets, new(), new());
        Assert.Equal(("salt", 500), game.Sabotaged[target]);
        Assert.DoesNotContain(game.Events, e => e.What == "sabotage");
    }

    [Fact]
    public void The_constructs_maker_is_not_displaced_by_its_own()
    {
        var game = Part2("""{"CONSTRUCTS": {"gears": 1.0}, "CONSTRUCT_MAKER": "maker", "DISPLACED_PATHS": ["handcraft"], "DISPLACE_FULL": 0.5, "DISPLACE_GROWTH": 0.3}""");
        var living = Living(game);
        int own = living[0], other = living[1];
        game.Factions[own].Path = game.Factions[other].Path = "handcraft";
        game.Factions[own].Lineage = "maker";
        var markets = Empty(game);
        markets[TownOf(game, own)].Made["gears"] = 100.0;
        markets[TownOf(game, other)].Made["gears"] = 100.0;
        After(game, markets, new(), new());
        Assert.False(game.DisplacedGrowth.ContainsKey(TownOf(game, own)));
        Assert.Equal(0.7, game.DisplacedGrowth[TownOf(game, other)], 12);  // wholly displaced: 1 - 0.3
    }

    [Fact]
    public void A_secret_is_bought_from_the_lowest_numbered_seller()
    {
        var game = Part2("""{"SECRETS": ["whispers"], "SECRET_CHANCE": 1.0}""");
        var living = Living(game);
        int buyer = living[0], low = living[1], high = living[2];
        var deals = new List<(string, int, int, double)>
        {
            ("whispers", TownOf(game, high), TownOf(game, buyer), 1.0), ("whispers", TownOf(game, low), TownOf(game, buyer), 1.0),
        };
        After(game, Empty(game), deals, new(), dice: 0.0);
        var secret = game.Events.Single(e => e.What == "secret");
        Assert.Equal(game.Factions[low].Name, secret.Facts["seller"]);
    }

    [Fact]
    public void Displaced_labour_slows_a_played_year_s_growth_and_legitimacy_its_grievance()
    {
        var part2 = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["chokepoints2"]!["settings"]!.AsObject();
        Game Played(double growth, double legitimacy)
        {
            var settings = part2.DeepClone().AsObject();
            settings["DISPLACE_GROWTH"] = growth;
            settings["LEGITIMACY"] = legitimacy;
            foreach (var key in new[] { "CONTRACT_CHANCE", "SABOTAGE_CHANCE", "LEAK_CHANCE", "DEBASE_CHANCE", "SECRET_CHANCE" })
                settings[key] = 0.0;
            var game = WithChokepoints(settings);
            YearCore.RunYear(game);
            return game;
        }
        var plain = Played(0.0, 0.0);
        var slowed = Played(1.0, 0.0);
        int town = slowed.DisplacedGrowth.First(p => p.Value < 1.0).Key;
        Assert.True(slowed.Towns[town].People < plain.Towns[town].People);
        var legitimate = Played(0.0, 0.9);
        Assert.Contains(Enumerable.Range(0, plain.Factions.Count), f => plain.Factions[f].Ruler != null
            && legitimate.GrievanceHindered.GetValueOrDefault(f, 1.0) < 1.0 && legitimate.Factions[f].Grievance < plain.Factions[f].Grievance);
    }

    [Fact]
    public void A_needed_good_is_refused_to_a_faction_thought_ill_of_but_not_past_a_contract_or_through_smugglers()
    {
        var game = TestGame();
        var a = game.Towns.First(t => !t.Ruined && t.Holder == 0);
        var b = game.Towns.First(t => !t.Ruined && t.Holder == 1);
        var needs = new HashSet<string> { "crystals" };
        game.Contracts.Clear();
        game.Factions[0].Opinions[1] = -0.5;
        Assert.Equal("refused", Chokepoints.Refuses(game, a, b, new[] { "crystals" }, false, needs));
        Assert.Contains((0, 1, "crystals"), game.Refused);
        Assert.Equal("smuggled", Chokepoints.Refuses(game, a, b, new[] { "crystals" }, true, needs));
        Assert.Null(Chokepoints.Refuses(game, a, b, new[] { "crystals" }, false, new HashSet<string>()));   // not needed there
        game.Factions[0].Opinions[1] = game.Settings.EmbargoOpinion;                                  // exactly the limit: sold
        Assert.Null(Chokepoints.Refuses(game, a, b, new[] { "crystals" }, false, needs));
        game.Factions[0].Opinions[1] = -0.5;
        Assert.Null(Chokepoints.Refuses(game, a, b, new[] { "grain" }, false, new HashSet<string> { "grain" }));  // no one needs grain
        game.Contracts[(1, "crystals")] = 0;
        Assert.Null(Chokepoints.Refuses(game, a, b, new[] { "crystals" }, false, needs));
        Assert.True(Chokepoints.ContractFirst(game, a, b, new[] { "crystals" }));
        Assert.False(Chokepoints.ContractFirst(game, b, a, new[] { "crystals" }));
        Assert.False(Chokepoints.ContractFirst(game, a, b, new[] { "crystals", "grain" }));    // a group of goods: never
    }

    static Market[] Empty(Game game) => game.Towns.Select(_ => new Market()).ToArray();

    static int TownOf(Game game, int faction) =>
        Enumerable.Range(0, game.Towns.Count).First(i => !game.Towns[i].Ruined && game.Towns[i].Holder == faction);

    [Fact]
    public void A_needed_good_is_no_longer_only_wanted()
    {
        var game = TestGame();
        Assert.True(game.Settings.GoodsInOrder.First(g => g.Name == "minted_currency").Wanted);
        var markets = Trade.MakeMarkets(game, Enumerable.Repeat(1.0, game.Towns.Count).ToList());
        Assert.Contains(markets, m => m.Needs.Contains("minted_currency"));
        Assert.All(markets, m => Assert.Empty(m.Needs.Intersect(m.Wanted)));
    }

    [Fact]
    public void Shortages_hinder_at_most_all_of_the_harvest()
    {
        var needs = JsonNode.Parse("""
            [{"who": "all", "good": "minted_currency", "amount": 0.1, "harvest": 0.8},
             {"who": "all", "good": "notarial_seals", "amount": 0.1, "harvest": 0.8}]
            """)!;
        var game = WithChokepoints(new JsonObject { ["NEEDS"] = needs });
        int town = TownOf(game, game.Towns.First(t => !t.Ruined && t.Holder != null).Holder!.Value);
        game.Short[town] = new Dictionary<string, double> { ["minted_currency"] = 1.0, ["notarial_seals"] = 1.0 };
        Assert.Equal(0.0, Chokepoints.BeginYear(game)[town]);
    }

    [Fact]
    public void A_shortage_s_harvest_comes_into_the_year_s_harvest()
    {
        var game = TestGame();
        int hearth = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].Lineage == "hearthfall_commons");
        int town = TownOf(game, hearth);
        game.Short.Clear();
        var plain = YearCore.Harvests(game, 101)[town];
        game.Short[town] = new Dictionary<string, double> { ["irrigation_rights"] = 1.0 };
        double hindered = game.Settings.Needs.Where(n => n.Who == "hearthfall_commons" && n.Good == "irrigation_rights").Sum(n => n.Harvest);
        Assert.Equal(plain * (1.0 - hindered), YearCore.Harvests(game, 101)[town], 12);
    }

    [Fact]
    public void A_town_that_needs_none_of_a_good_is_not_short_of_it()
    {
        var game = TestGame();
        int town = Enumerable.Range(0, game.Towns.Count).First(i => !game.Towns[i].Ruined && game.Towns[i].Holder != null);
        game.Towns[town].People = 0.4;                                      // no one left, not yet a ruin
        var markets = Trade.MakeMarkets(game, Enumerable.Repeat(1.0, game.Towns.Count).ToList());
        Trade.TradeGoods(game, markets);
        Chokepoints.AfterTrade(game, markets, new Dictionary<(int, int), double>(), 101);
        Assert.False(game.Short.ContainsKey(town));
        Assert.All(game.Short.Values.SelectMany(v => v.Values), share => Assert.False(double.IsNaN(share)));
    }

    [Fact]
    public void A_faction_short_of_a_good_blames_the_living_who_refused_it_and_a_near_town_with_some_to_spare()
    {
        var game = TestGame();
        game.Bought.Clear();                                                // no regular traders: only the near count
        var alive = Enumerable.Range(0, game.Factions.Count).Where(f => game.Factions[f].People > 0).ToList();
        int wanting = alive[0];
        int home = TownOf(game, wanting);
        // A town of another living faction a few routes away, the only one with crystals to spare: the suspect.
        int near = Suspicion.Within(game, new[] { home }).Keys.Order()
                            .First(t => game.Towns[t].Holder is int h && h != wanting && game.Factions[h].People > 0);
        int neighbour = game.Towns[near].Holder!.Value;
        int refuser = alive.First(f => f != wanting && f != neighbour);
        int dead = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].People <= 0);
        var markets = Empty(game);
        var mine = markets[home];
        mine.Needs.Add("crystals");
        mine.Needed["crystals"] = 10.0;
        mine.Made["crystals"] = 4.0;                                        // six tenths short
        markets[near].Made["crystals"] = 3.0;                               // three to spare
        game.Refused.Add((refuser, wanting, "crystals"));
        game.Refused.Add((dead, wanting, "crystals"));
        var blame = new Dictionary<(int, int), double>();
        Chokepoints.AfterTrade(game, markets, blame, 101);
        double rate = game.Settings.Resentment;
        Assert.Equal(rate * 0.6, blame[(wanting, refuser)], 12);
        Assert.Equal(rate * 0.6, blame[(wanting, neighbour)], 12);
        Assert.False(blame.ContainsKey((wanting, dead)));
        Assert.Equal(new[] { refuser, neighbour }.Order(), blame.Keys.Where(k => k.Item1 == wanting).Select(k => k.Item2).Order());
    }

    [Fact]
    public void An_embargo_is_told_lifted_only_past_the_margin()
    {
        var game = TestGame();                                               // EMBARGO_OPINION 0
        game.Embargoes.Add((0, 1, "crystals"));
        game.Factions[0].Opinions[1] = Chokepoints.LiftMargin / 2;
        Chokepoints.AfterTrade(game, Empty(game), new Dictionary<(int, int), double>(), 101);
        Assert.Contains((0, 1, "crystals"), game.Embargoes);
        game.Factions[0].Opinions[1] = Chokepoints.LiftMargin;
        Chokepoints.AfterTrade(game, Empty(game), new Dictionary<(int, int), double>(), 102);
        Assert.DoesNotContain((0, 1, "crystals"), game.Embargoes);
        Assert.Contains(game.Events, e => e.What == "embargo_lifted");
    }

    [Fact]
    public void A_contract_holds_while_its_maker_makes_the_good_and_the_two_think_well_of_each_other()
    {
        var game = TestGame();
        game.Contracts.Clear();
        game.Factions[0].Opinions[1] = game.Factions[1].Opinions[0] = 0.5;
        game.Contracts[(1, "crystals")] = 0;
        var markets = Empty(game);
        markets[TownOf(game, 0)].Made["crystals"] = 2.0;
        Chokepoints.AfterTrade(game, markets, new Dictionary<(int, int), double>(), 101);
        Assert.True(game.Contracts.ContainsKey((1, "crystals")));
        Chokepoints.AfterTrade(game, Empty(game), new Dictionary<(int, int), double>(), 102);   // it makes none now
        Assert.False(game.Contracts.ContainsKey((1, "crystals")));
        Assert.Contains(game.Events, e => e.What == "contract_ended");
    }

    [Fact]
    public void A_contract_s_sale_goes_before_a_cheaper_one()
    {
        // With every faction willing to trade: a sale of a good (not a staple), and a town of a third faction that
        // can reach the buyer at a higher cost than the seller, given plenty of the good. Without a contract the
        // cheaper seller's faction sells first; with the buyer's faction contracted to the dearer one, it does.
        static Game Friendly()
        {
            var g = TestGame();
            g.Contracts.Clear();
            foreach (var faction in g.Factions)
                foreach (var other in Enumerable.Range(0, g.Factions.Count))
                    faction.Opinions[other] = 1.0;                         // everyone trades with everyone
            return g;
        }
        var plain = Friendly();
        var harvest = Enumerable.Repeat(1.0, plain.Towns.Count).ToList();
        var staples = plain.Settings.Staples.ToHashSet();
        var journeys = plain.Settings.TradeHands > 0
            ? Trade.LocalJourneys(plain, plain.Settings.TradeHands, plain.Settings.TradeReach * plain.HistoryColumns / 256.0)
            : Trade.AllJourneys(plain);
        int? Dearer((string Good, int Seller, int Buyer, double Amount) d) =>
            Enumerable.Range(0, plain.Towns.Count).Cast<int?>().FirstOrDefault(t => !plain.Towns[t!.Value].Ruined
                && plain.Towns[t.Value].Holder is int h && h != plain.Towns[d.Seller].Holder && h != plain.Towns[d.Buyer].Holder
                && journeys[t.Value].Cost.TryGetValue(d.Buyer, out double c) && c > journeys[d.Seller].Cost[d.Buyer]);
        var deal = Trade.TradeGoods(plain, Trade.MakeMarkets(plain, harvest))
                        .First(d => !staples.Contains(d.Good) && d.Good != Trade.Metal && Dearer(d) != null);
        int? cheap = plain.Towns[deal.Seller].Holder, buying = plain.Towns[deal.Buyer].Holder;
        int dear = Dearer(deal)!.Value;

        int? FirstSeller(bool contracted)
        {
            var game = Friendly();
            if (contracted)
                game.Contracts[(buying!.Value, deal.Good)] = game.Towns[dear].Holder!.Value;
            var markets = Trade.MakeMarkets(game, harvest);
            markets[dear].Made[deal.Good] = markets[dear].Made.GetValueOrDefault(deal.Good) + 1000.0;
            var first = Trade.TradeGoods(game, markets).First(d => d.Good == deal.Good && d.Buyer == deal.Buyer);
            return game.Towns[first.Seller].Holder;
        }
        Assert.Equal(cheap, FirstSeller(false));
        Assert.Equal(plain.Towns[dear].Holder, FirstSeller(true));
    }

    [Fact]
    public void An_embargo_in_force_is_lifted_only_past_the_margin()
    {
        var game = TestGame();                                               // EMBARGO_OPINION 0
        var a = game.Towns.First(t => !t.Ruined && t.Holder == 0);
        var b = game.Towns.First(t => !t.Ruined && t.Holder == 1);
        var needs = new HashSet<string> { "crystals" };
        game.Embargoes.Add((0, 1, "crystals"));
        game.Factions[0].Opinions[1] = Chokepoints.LiftMargin / 2;           // above the limit, below limit + margin
        Assert.Equal("refused", Chokepoints.Refuses(game, a, b, new[] { "crystals" }, false, needs));
        game.Embargoes.Clear();
        game.Refused.Clear();
        Assert.Null(Chokepoints.Refuses(game, a, b, new[] { "crystals" }, false, needs));
    }

    [Fact]
    public void A_town_short_of_what_its_rituals_need_works_fewer_rituals()
    {
        var game = TestGame();
        int mage = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].Path == "attunement" && game.Factions[f].People > 0);
        game.Short.Clear();
        foreach (int town in Enumerable.Range(0, game.Towns.Count).Where(i => game.Towns[i].Holder == mage && !game.Towns[i].Ruined))
            game.Short[town] = new Dictionary<string, double> { ["crystals"] = 1.0 };
        Chokepoints.BeginYear(game);
        double need = game.Settings.Needs.Where(n => n.Who == "attunement").Sum(n => n.Good == "crystals" ? n.Rituals : 0.0);
        Assert.Equal(1.0 - Math.Min(1.0, need), game.RitualsHindered[mage], 12);
        Assert.Equal(0.1 * (1.0 - Math.Min(1.0, need)), Rituals.ByPath(game, 0.1, game.Factions[mage]), 12);
    }

    [Fact]
    public void A_sabotaged_town_makes_none_of_its_good_until_its_last_year()
    {
        var game = TestGame();
        var (town, (good, until)) = game.Sabotaged.First();
        var markets = Trade.MakeMarkets(game, Enumerable.Repeat(1.0, game.Towns.Count).ToList());
        Assert.Equal(0.0, markets[town].CanCraft[good]);
        Assert.False(markets[town].CraftingMetal.ContainsKey(good));
        Chokepoints.AfterTrade(game, markets, new Dictionary<(int, int), double>(), until);
        Assert.False(game.Sabotaged.ContainsKey(town));
    }

    [Fact]
    public void Without_needs_or_stand_ins_there_are_no_chokepoints()
    {
        var game = WithChokepoints(new JsonObject { ["NEEDS"] = new JsonArray(), ["STAND_INS"] = new JsonObject() });
        Assert.False(game.Settings.Choking);
        var markets = Trade.MakeMarkets(game, Enumerable.Repeat(1.0, game.Towns.Count).ToList());
        Assert.All(markets, m => Assert.Empty(m.Needs));
        Assert.All(markets, m => Assert.Equal(1.0, m.TradeFactor));
    }

    [Fact]
    public void The_export_s_chokepoints_are_read_exactly_and_a_save_plays_on_the_same()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 3);   // a seed with chokepoint news
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "world-7", "world.json")))!;
        Json.Same(data["continue"]!["chokepoints"], Chokepoints.Write(game), "chokepoints");
        for (int i = 0; i < 20; i++)
            YearCore.RunYear(game);
        Assert.Contains(game.Events, e => e.Kind == "chokepoint");
        string path = Path.Combine(Scratch.Root, "wr-choke-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Json.Same(Chokepoints.Write(game), Chokepoints.Write(loaded), "chokepoints");
        for (int i = 0; i < 15; i++)
        {
            YearCore.RunYear(game);
            YearCore.RunYear(loaded);
        }
        Assert.Equal(game.Events.Select(e => e.Text), loaded.Events.Select(e => e.Text));
    }
}
