// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Trade (src/Sim/Trade.cs) against the generator's own trade.make_markets, trade_goods, fed_share and dig.</summary>
public class TradeTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static Game NewGame() => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 1);
    static readonly JsonObject Expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["trade"]!.AsObject();

    // One year's markets and trade with an ordinary year's weather, as fixtures/make_expected.py works it out.
    static (Game, Market[], List<(string Good, int Seller, int Buyer, double Amount)>) OneYear()
    {
        var game = NewGame();
        var harvest = Enumerable.Range(0, game.Towns.Count).Select(i =>
        {
            var town = game.Towns[i];
            var faction = town.Holder is int h ? game.Factions[h] : null;
            return YearCore.PathHarvest(1.0, faction?.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings);
        }).ToList();
        var markets = Trade.MakeMarkets(game, harvest);
        var deals = Trade.TradeGoods(game, markets);
        return (game, markets, deals);
    }

    static void SameTable(JsonNode? want, Dictionary<string, double> got, string what)
    {
        var table = want!.AsObject();
        foreach (var (good, amount) in table)
            Assert.True(Math.Abs((double)amount! - got.GetValueOrDefault(good)) < 1e-9,
                        what + " " + good + ": " + (double)amount! + " but " + got.GetValueOrDefault(good));
        foreach (var (good, amount) in got)
            if (Math.Abs(amount) > 1e-12)
                Assert.True(table.ContainsKey(good), what + " has " + good + " the generator does not");
    }

    [Fact]
    public void Every_market_and_every_sale_agree_with_the_generator()
    {
        var (_, markets, deals) = OneYear();
        var want = Expected["markets"]!.AsArray();
        Assert.Equal(want.Count, markets.Length);
        for (int i = 0; i < markets.Length; i++)
        {
            SameTable(want[i]!["made"], markets[i].Made, "town " + i + " made");
            SameTable(want[i]!["needed"], markets[i].Needed, "town " + i + " needed");
            SameTable(want[i]!["bought"], markets[i].Bought, "town " + i + " bought");
            SameTable(want[i]!["sold"], markets[i].Sold, "town " + i + " sold");
        }
        Assert.Equal((int)Expected["deals"]!, deals.Count);
        Assert.True(deals.Count > 0);
    }

    [Fact]
    public void The_routes_carry_what_the_generator_s_carry()
    {
        var (game, _, _) = OneYear();
        var want = Expected["volumes"]!.AsArray();
        Assert.Equal(want.Count, game.Routes.Count);
        for (int r = 0; r < want.Count; r++)
            Assert.Equal((double)want[r]!, game.Routes[r].Volume, 9);
        Assert.Contains(game.Routes, route => route.Volume > 0);
    }

    [Fact]
    public void Towns_are_fed_after_trade_as_the_generator_feeds_them()
    {
        var (game, markets, _) = OneYear();
        var want = Expected["fed"]!.AsArray();
        int helped = 0;
        for (int i = 0; i < markets.Length; i++)
        {
            if (want[i] is null)
                continue;
            double fed = Trade.FedShare(markets[i], game.Settings.Staples, game.Settings.Extras);
            Assert.Equal((double)want[i]!, fed, 9);
            var faction = game.Towns[i].Holder is int h ? game.Factions[h] : null;
            double alone = YearCore.FedShare(game.Towns[i], faction?.Path,
                YearCore.PathHarvest(1.0, faction?.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings), game.Settings);
            if (fed > alone + 1e-9)
                helped++;
        }
        Assert.True(helped > 0, "trade feeds some towns their own land could not");
    }

    [Fact]
    public void The_mines_are_dug_as_the_generator_digs_them()
    {
        var (game, markets, _) = OneYear();
        double before = game.Mines.Sum(m => m.Metal);
        Trade.Dig(game, markets, game.Year + 1);
        var want = Expected["metal"]!.AsArray();
        for (int k = 0; k < want.Count; k++)
            Assert.Equal((double)want[k]!, game.Mines[k].Metal, 9);
        Assert.True(game.Mines.Sum(m => m.Metal) < before, "some metal was dug");
    }

    [Fact]
    public void A_mine_below_its_last_twentieth_runs_dry_and_says_so()
    {
        var (game, markets, _) = OneYear();
        var mine = game.Mines.First(m => m.Metal > 0 && Trade.DepositHolder(game, m) is int h
                                          && markets[h].Made.GetValueOrDefault(Trade.Metal) > 0);
        mine.Metal = mine.First * Trade.MineEmpty * 1.001;
        Trade.Dig(game, markets, 999);
        Assert.Equal(0.0, mine.Metal);
        Assert.Contains(game.Events, e => e.What == "mine_dry" && e.Text.Contains(mine.Name) && e.Factions.Count == 1);
    }

    [Fact]
    public void A_mine_whose_town_fell_is_worked_by_the_nearest_town_of_its_faction()
    {
        var game = NewGame();
        var mine = game.Mines.First(m => m.LandTown is int t && !game.Towns[t].Ruined && m.Owner != null
                                          && game.Towns[t].Holder == m.Owner
                                          && game.Towns.Count(x => x.Holder == m.Owner && !x.Ruined) > 1);
        int land = mine.LandTown!.Value;
        Assert.Equal(land, Trade.DepositHolder(game, mine));
        game.Towns[land].Ruined = true;
        int? next = Trade.DepositHolder(game, mine);
        Assert.NotNull(next);
        Assert.NotEqual(land, next);
        Assert.Equal(mine.Owner, game.Towns[next!.Value].Holder);
        double nearest = game.Towns.Where(t => t.Holder == mine.Owner && !t.Ruined)
                                   .Min(t => Math.Sqrt(Math.Pow(t.X - mine.X, 2) + Math.Pow(t.Y - mine.Y, 2)));
        var chosen = game.Towns[next.Value];
        Assert.Equal(nearest, Math.Sqrt(Math.Pow(chosen.X - mine.X, 2) + Math.Pow(chosen.Y - mine.Y, 2)), 9);
    }

    [Fact]
    public void Factions_that_think_too_ill_of_each_other_do_not_trade()
    {
        var game = NewGame();
        foreach (var f in game.Factions)
            foreach (var other in f.Opinions.Keys.ToList())
                f.Opinions[other] = -1.0;                         // everyone hates everyone
        var harvest = game.Towns.Select(_ => 1.0).ToList();
        var markets = Trade.MakeMarkets(game, harvest);
        var deals = Trade.TradeGoods(game, markets);
        Assert.All(deals, d => Assert.Equal(game.Towns[d.Seller].Holder, game.Towns[d.Buyer].Holder));
        Assert.True(Trade.WillTrade(game, game.Towns[0], game.Towns[0]));
    }

    [Fact]
    public void Trade_warms_the_memories_of_those_who_trade()
    {
        var (game, markets, deals) = OneYear();
        var pair = deals.Select(d => (game.Towns[d.Seller].Holder, game.Towns[d.Buyer].Holder))
                        .First(p => p.Item1 != null && p.Item2 != null && p.Item1 != p.Item2);
        var key = (pair.Item1!.Value, pair.Item2!.Value);
        var quiet = game.Memory.Keys.First(k => !deals.Any(d =>
            (game.Towns[d.Seller].Holder == k.Item1 && game.Towns[d.Buyer].Holder == k.Item2) ||
            (game.Towns[d.Seller].Holder == k.Item2 && game.Towns[d.Buyer].Holder == k.Item1)));
        double tradedBefore = game.Memory[key], quietBefore = game.Memory[quiet];
        YearCore.UpdateFactions(game, markets, deals, game.Year + 1);
        double fade = Math.Pow(0.5, 1 / game.Settings.MemoryHalfLife);
        Assert.Equal(quietBefore * fade, game.Memory[quiet], 12);
        Assert.True(game.Memory[key] > tradedBefore * fade);
    }

    [Fact]
    public void The_harvest_changes_food_only()
    {
        var game = NewGame();
        int i = game.Towns.FindIndex(t => !t.Ruined);
        game.Towns[i].Yields["timber"] = 2.0;
        game.Towns[i].Yields["grain"] = 3.0;
        var harvest = game.Towns.Select(_ => 0.5).ToList();
        var markets = Trade.MakeMarkets(game, harvest);
        Assert.Equal(2.0, markets[i].Made["timber"]);
        Assert.Equal(1.5, markets[i].Made["grain"]);
    }

    [Fact]
    public void Both_factions_must_think_well_enough_of_each_other_to_trade()
    {
        var game = NewGame();
        var a = game.Towns.First(t => t.Holder == 0);
        var b = game.Towns.First(t => t.Holder == 1);
        game.Factions[0].Opinions[1] = 1.0;
        game.Factions[1].Opinions[0] = -1.0;
        Assert.False(Trade.WillTrade(game, a, b));
        Assert.False(Trade.WillTrade(game, b, a));
        game.Factions[1].Opinions[0] = game.Settings.MinOpinionToTrade;
        Assert.True(Trade.WillTrade(game, a, b));
    }

    [Fact]
    public void A_greedy_faction_digs_more_than_it_uses()
    {
        double DugWith(double greed)
        {
            var game = NewGame();
            var harvest = game.Towns.Select(_ => 1.0).ToList();
            var mine = game.Mines.First(m => m.Metal > 0 && Trade.DepositHolder(game, m) is int);
            int holder = Trade.DepositHolder(game, mine)!.Value;
            game.Factions[game.Towns[holder].Holder!.Value].Greed = greed;
            mine.Metal = mine.First;                              // plenty: more than its town can use
            var markets = Trade.MakeMarkets(game, harvest);
            markets[holder].Needed[Trade.Metal] = 0;              // and it needs none of it
            double before = mine.Metal;
            Trade.Dig(game, markets, game.Year + 1);
            return before - mine.Metal;
        }
        Assert.Equal(0.0, DugWith(0.0), 12);
        Assert.True(DugWith(1.0) > 0);
        Assert.True(DugWith(0.5) < DugWith(1.0));
    }

    [Fact]
    public void A_year_feeds_a_town_from_its_neighbours()
    {
        var game = NewGame();
        int i = game.Towns.FindIndex(t => !t.Ruined && t.Population > 300 && t.Holder != null);
        game.Towns[i].Yields.Clear();                        // its own land grows nothing
        YearCore.RunYear(game);
        Assert.True(game.Towns[i].Fed > 0, "its neighbours sold it food");
    }
}
