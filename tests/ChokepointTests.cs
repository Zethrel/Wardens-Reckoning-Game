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
            Chokepoints.AfterTrade(game, markets, blame, year);

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
    public void A_faction_short_of_a_good_blames_the_living_who_refused_it_and_a_neighbour_with_some_to_spare()
    {
        var game = TestGame();
        var alive = Enumerable.Range(0, game.Factions.Count).Where(f => game.Factions[f].People > 0).ToList();
        int wanting = alive[0], refuser = alive[1], neighbour = alive[2];
        int dead = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].People <= 0);
        var markets = Empty(game);
        var mine = markets[TownOf(game, wanting)];
        mine.Needs.Add("crystals");
        mine.Needed["crystals"] = 10.0;
        mine.Made["crystals"] = 4.0;                                        // six tenths short
        markets[TownOf(game, neighbour)].Made["crystals"] = 3.0;            // three to spare
        game.Factions[wanting].Border.Clear();
        game.Factions[wanting].Border[neighbour] = 5;
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
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 2);
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
