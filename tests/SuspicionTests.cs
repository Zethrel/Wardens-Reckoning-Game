// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Suspicion: whom a shortage is blamed on. Against the generator's own suspicion.suspects with the same
/// stand-in dice (tests/fixtures/make_expected.py, "suspicion"), and by what must follow.</summary>
public class SuspicionTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["suspicion"]!.AsObject();

    /// <summary>make_expected.py's FIXED_DICE, handed out in turn, over and over.</summary>
    static readonly double[] FixedDice = { 0.2, 0.0005, 0.7, 0.35, 0.001, 0.9, 0.55, 0.003, 0.15, 0.8, 0.0002, 0.45 };

    static Game NewGame() => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 1);

    /// <summary>A plain year's markets and trade (as make_expected.py's trade_year).</summary>
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
    public void Suspects_agree_with_the_generator_s()
    {
        var game = NewGame();
        var markets = OneYearOfTrade(game);
        int at = 0;
        double Dice() => FixedDice[at++ % FixedDice.Length];
        var food = new JsonArray();
        for (int t = 0; t < game.Towns.Count; t++)
        {
            if (game.Towns[t].Ruined || game.Towns[t].Holder is not int f)
                continue;
            var m = markets[t];
            if (m.PooledShortage(game.Settings.Staples) + Py.Sum(game.Settings.Extras.Select(m.Shortage)) > 0)
                food.Add(new JsonArray(t, new JsonArray(Suspicion.Suspects(game, f, Suspicion.Food, new[] { t }, markets, Dice)
                    .Select(s => (JsonNode)s).ToArray())));
        }
        var shortOf = new SortedDictionary<(int, string), List<int>>();
        for (int t = 0; t < game.Towns.Count; t++)
        {
            if (game.Towns[t].Ruined || game.Towns[t].Holder is not int f)
                continue;
            foreach (var good in markets[t].Needs.OrderBy(g => g, StringComparer.Ordinal))
                if (markets[t].Shortage(good) > 0)
                {
                    if (!shortOf.TryGetValue((f, good), out var towns))
                        shortOf[(f, good)] = towns = new List<int>();
                    towns.Add(t);
                }
        }
        var goods = new JsonArray();
        foreach (var ((f, good), towns) in shortOf)
            goods.Add(new JsonArray(f, good, new JsonArray(towns.Select(t => (JsonNode)t).ToArray()),
                new JsonArray(Suspicion.Suspects(game, f, good, towns, markets, Dice).Select(s => (JsonNode)s).ToArray())));
        Json.Same(Expected["food"], food, "food");
        Json.Same(Expected["goods"], goods, "goods");
        Assert.Equal((int)Expected["dice_used"]!, at);
        Assert.Contains(food, f => f![1]!.AsArray().Count > 0);
    }

    [Fact]
    public void Purchases_from_others_are_remembered_food_as_one_and_fade_in_their_half_life()
    {
        var game = NewGame();
        game.Bought.Clear();
        var holders = Enumerable.Range(0, game.Towns.Count).Where(t => !game.Towns[t].Ruined && game.Towns[t].Holder != null).ToList();
        int a = holders.First(), b = holders.First(t => game.Towns[t].Holder != game.Towns[a].Holder);
        int same = holders.First(t => t != a && game.Towns[t].Holder == game.Towns[a].Holder);
        int fa = game.Towns[a].Holder!.Value, fb = game.Towns[b].Holder!.Value;
        string staple = game.Settings.Staples[0];
        Suspicion.Remember(game, new() { (staple, b, a, 4.0), ("salt", b, a, 2.0), (staple, same, a, 9.0) });
        Assert.Equal(4.0, game.Bought[(fa, staple, fb)]);
        Assert.Equal(4.0, game.Bought[(fa, Suspicion.Food, fb)]);
        Assert.Equal(2.0, game.Bought[(fa, "salt", fb)]);
        Assert.Equal(3, game.Bought.Count);                                 // a faction's own trade is not remembered
        for (int y = 0; y < (int)game.Settings.TradeMemoryHalfLife; y++)
            Suspicion.Remember(game, new());
        Assert.Equal(1.0, game.Bought[(fa, "salt", fb)], 9);
        Assert.Equal(1.0, Suspicion.Shares(game, fa, "salt")[fb]);
    }

    /// <summary>A game on a copy of the fixture whose history settings are changed by `change`, with no trade memory.</summary>
    static Game WithHistory(Action<JsonObject> change)
    {
        string folder = Path.Combine(Scratch.Root, "wr-suspicion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        change(data["continue"]!["settings"]!["history"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        var game = new Game(ExportedWorld.Read(folder), 1);
        game.Bought.Clear();
        return game;
    }

    static Market[] Empty(Game game) => game.Towns.Select(_ => new Market()).ToArray();

    /// <summary>A lived-in town, and for each distance 1 to 3 a town that far from it of another living faction (each
    /// of its own faction if it can be), or null.</summary>
    static (int Home, int Faction, int?[] At) Layout(Game game)
    {
        foreach (int home in Enumerable.Range(0, game.Towns.Count).Where(t => !game.Towns[t].Ruined && game.Towns[t].Holder != null))
        {
            int f = game.Towns[home].Holder!.Value;
            var near = Suspicion.Within(game, new[] { home });
            var at = new int?[4];
            var used = new HashSet<int>();
            for (int away = 1; away <= 3; away++)
                at[away] = near.Where(p => p.Value == away).Select(p => p.Key).OrderBy(t => t)
                               .Where(t => game.Towns[t].Holder is int h && h != f && game.Factions[h].People > 0)
                               .OrderBy(t => used.Contains(game.Towns[t].Holder!.Value) ? 1 : 0).Cast<int?>().FirstOrDefault();
            if (at[1] != null && at[3] != null && game.Towns[at[1]!.Value].Holder != game.Towns[at[3]!.Value].Holder)
                return (home, f, at);
        }
        throw new InvalidOperationException("the test world has no town with others one and three routes away");
    }

    [Fact]
    public void What_is_remembered_drops_out_below_a_millionth()
    {
        var game = NewGame();
        game.Bought.Clear();
        game.Bought[(0, "salt", 1)] = 1.5e-6;
        game.Bought[(0, "tin", 1)] = 1.05e-6;
        Suspicion.Remember(game, new());
        Assert.True(game.Bought.ContainsKey((0, "salt", 1)));
        Assert.False(game.Bought.ContainsKey((0, "tin", 1)));
    }

    [Fact]
    public void Food_to_spare_is_the_staples_together_and_must_be_a_hundredth()
    {
        // (Your trade settings have no extra foods, so only the staples are tried here.)
        var game = NewGame();
        game.Bought.Clear();                                                // no regular traders: only the near count
        string staple = game.Settings.Staples[0];
        var market = new Market();
        market.Made[staple] = 3.0;
        market.Needed[staple] = 1.0;
        Assert.Equal(2.0, Suspicion.Spare(game, market, Suspicion.Food));
        var (home, f, at) = Layout(game);
        var markets = Empty(game);
        markets[at[1]!.Value].Made[staple] = 0.005;                         // too little to count as food to spare
        Assert.Empty(Suspicion.Suspects(game, f, Suspicion.Food, new[] { home }, markets, () => 0.0));
        markets[at[1]!.Value].Made[staple] = 0.02;
        Assert.Equal(new[] { game.Towns[at[1]!.Value].Holder!.Value }, Suspicion.Suspects(game, f, Suspicion.Food, new[] { home }, markets, () => 0.0));
    }

    [Fact]
    public void The_nearer_the_heavier_and_a_draw_on_the_edge_goes_to_the_next()
    {
        var game = WithHistory(h => h["SUSPECTS_MOST"] = 1);
        var (home, f, at) = Layout(game);
        int near = game.Towns[at[1]!.Value].Holder!.Value, far = game.Towns[at[3]!.Value].Holder!.Value;
        var markets = Empty(game);
        markets[at[1]!.Value].Made["salt"] = 1.0;
        markets[at[3]!.Value].Made["salt"] = 1.0;
        // Weights: 1 for the near, 1/3 for the far; the draw is a share of the whole, 4/3.
        var order = new[] { near, far }.Order().ToArray();
        double first = order[0] == near ? 1.0 : 1.0 / 3;
        double Pick(double share) => share;
        int Draw(double share)
        {
            var dice = new Queue<double>(new[] { 0.0, share });
            return Suspicion.Suspects(game, f, "salt", new[] { home }, markets, () => dice.Dequeue()).Single();
        }
        Assert.Equal(order[0], Draw(first / (4.0 / 3) * 0.999));
        Assert.Equal(order[1], Draw(Pick(first / (4.0 / 3))));               // exactly the first's weight: the next
    }

    [Fact]
    public void Two_suspects_are_two_and_the_dead_are_never_suspected()
    {
        var game = WithHistory(h => h["SUSPECTS_MOST"] = 2);
        var (home, f, at) = Layout(game);
        int near = game.Towns[at[1]!.Value].Holder!.Value, far = game.Towns[at[3]!.Value].Holder!.Value;
        var markets = Empty(game);
        markets[at[1]!.Value].Made["salt"] = 1.0;
        markets[at[3]!.Value].Made["salt"] = 1.0;
        Assert.Equal(new[] { near, far }.Order(), Suspicion.Suspects(game, f, "salt", new[] { home }, markets, () => 0.99));
        game.Factions[far].People = 0;
        Assert.Equal(new[] { near }, Suspicion.Suspects(game, f, "salt", new[] { home }, markets, () => 0.99));
    }

    [Fact]
    public void The_reach_is_the_setting_s()
    {
        var game = WithHistory(h => h["SUSPECT_REACH"] = 1);
        int start = Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined);
        Assert.All(Suspicion.Within(game, new[] { start }).Values, away => Assert.Equal(1, away));
    }

    [Fact]
    public void Only_the_towns_short_of_a_good_look_around_for_suspects()
    {
        // A faction with two towns: the one short of salt has no one near with salt to spare; the other has. No blame.
        var game = NewGame();
        game.Bought.Clear();
        foreach (int f in Enumerable.Range(0, game.Factions.Count))
        {
            var mine = Enumerable.Range(0, game.Towns.Count).Where(t => !game.Towns[t].Ruined && game.Towns[t].Holder == f).ToList();
            foreach (int shortTown in mine)
                foreach (int fine in mine.Where(t => t != shortTown))
                {
                    var nearShort = Suspicion.Within(game, new[] { shortTown }).Keys.ToHashSet();
                    var spareAt = Suspicion.Within(game, new[] { fine }).Keys
                        .Where(t => !nearShort.Contains(t) && t != shortTown && game.Towns[t].Holder is int h && h != f && game.Factions[h].People > 0)
                        .Cast<int?>().FirstOrDefault();
                    if (spareAt is not int other)
                        continue;
                    var markets = Empty(game);
                    markets[shortTown].Needs.Add("crystals");
                    markets[shortTown].Needed["crystals"] = 10.0;
                    markets[fine].Needs.Add("crystals");
                    markets[fine].Needed["crystals"] = 1.0;
                    markets[fine].Made["crystals"] = 1.0;                  // not short
                    markets[other].Made["crystals"] = 5.0;
                    var blame = new Dictionary<(int, int), double>();
                    Chokepoints.AfterTrade(game, markets, new(), blame, 101, () => 0.0, () => 0.0, () => 0.0);
                    Assert.False(blame.ContainsKey((f, game.Towns[other].Holder!.Value)));
                    return;
                }
        }
        throw new InvalidOperationException("no faction in the test world has two towns to try this with");
    }

    [Fact]
    public void A_hungry_town_s_rule_blames_its_suspects()
    {
        var game = NewGame();
        game.Bought.Clear();
        var (home, f, at) = Layout(game);
        var markets = Empty(game);
        markets[at[1]!.Value].Made[game.Settings.Staples[0]] = 50.0;       // food to spare next door
        var view = new PlaceView("here", "village", 500, 1000, 0.0, 9, new Dictionary<string, double>(), "us", "", 
                                 new Dictionary<string, double> { ["greed"] = 0.9 });
        var blame = new Dictionary<(int, int), double>();
        for (int i = 0; i < 400 && blame.Count == 0; i++)                   // negative_memory: one in ten years
            Rules.SettlementRules(game, home, view, markets, blame, 101);
        Assert.Equal(new[] { (f, game.Towns[at[1]!.Value].Holder!.Value) }, blame.Keys);
        Assert.Contains(game.Events, e => e.Text.Contains("blames the " + game.Factions[game.Towns[at[1]!.Value].Holder!.Value].Name));
    }

    [Fact]
    public void Towns_are_counted_in_routes_away_up_to_the_reach_and_not_through_ruins()
    {
        var game = NewGame();
        int start = Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined);
        var near = Suspicion.Within(game, new[] { start });
        Assert.All(near.Values, away => Assert.InRange(away, 1, game.Settings.SuspectReach));
        Assert.DoesNotContain(start, near.Keys);
        int next = near.First(p => p.Value == 1).Key;
        game.Towns[next].Ruined = true;
        Assert.DoesNotContain(next, Suspicion.Within(game, new[] { start }).Keys);
    }

    [Fact]
    public void A_played_year_remembers_its_trade_and_a_save_keeps_it()
    {
        var game = NewGame();
        var before = Suspicion.Write(game).ToJsonString();
        YearCore.RunYear(game);
        Assert.NotEqual(before, Suspicion.Write(game).ToJsonString());
        string path = Path.Combine(Scratch.Root, "wr-suspicion-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        Json.Same(Suspicion.Write(game), Suspicion.Write(SaveFile.Read(path)), "suspicion");
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "world-7", "world.json")))!;
        Json.Same(data["continue"]!["suspicion"], Suspicion.Write(NewGame()), "export");
    }
}
