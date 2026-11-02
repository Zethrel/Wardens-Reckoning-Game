// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;

namespace WardensReckoning.Sim;

/// <summary>
/// Suspicion (the generator's north/suspicion.py; your settings are in history_settings.py): whom a shortage is
/// blamed on.
///
/// A hungry town (your rules' "blame") or a faction short of a good it needs (chokepoints) suspects:
/// 1. **Near producers**: factions with a town at most SUSPECT_REACH routes away (not through ruins) that ended the
///    year with some of the good to spare. Next door weighs 1, two routes away 1/2, three away 1/3; a faction is as
///    near as its nearest such town.
/// 2. **Regular traders**: the factions it has bought the good from, by their share of what it remembers buying of
///    it.
///
/// A faction's weight is the two added up. One to SUSPECTS_MOST of them (never more than there are) are drawn by
/// weight, one after another; each gets the whole blame. "Food" is one good: the staples together and each extra.
///
/// **Trade memory**: after each year's trade, each faction remembers how much of each good (and of food) it bought
/// from each other faction; what it remembers fades by half every TRADE_MEMORY_HALF_LIFE years.
///
/// Suspicion rolls its own dice ("suspicion").
/// </summary>
public static class Suspicion
{
    /// <summary>suspicion.FOOD: food as one good, in the trade memory and in a town's spare.</summary>
    public const string Food = "food";

    /// <summary>How much must be left for a town to have some to spare: food (as history.food_to_spare), and a good.</summary>
    public const double FoodSpare = 0.01;
    public const double GoodSpare = 1e-9;

    /// <summary>After the year's trade (suspicion.remember): what is remembered fades (gone below a millionth), and
    /// this year's purchases from other factions are added, food also as Food.</summary>
    public static void Remember(Game game, List<(string Good, int Seller, int Buyer, double Amount)> deals)
    {
        double fade = Math.Pow(0.5, 1 / game.Settings.TradeMemoryHalfLife);
        foreach (var key in game.Bought.Keys.ToList())
        {
            game.Bought[key] *= fade;
            if (game.Bought[key] < 1e-6)
                game.Bought.Remove(key);
        }
        var food = game.Settings.Staples.Concat(game.Settings.Extras).ToHashSet();
        foreach (var (good, seller, buyer, amount) in deals)
        {
            if (game.Towns[buyer].Holder is not int taker || game.Towns[seller].Holder is not int maker || taker == maker)
                continue;
            foreach (var key in food.Contains(good) ? new[] { good, Food } : new[] { good })
                game.Bought[(taker, key, maker)] = game.Bought.GetValueOrDefault((taker, key, maker)) + amount;
        }
    }

    /// <summary>{seller: its share of what `faction` remembers buying of `good`}, 0 to 1 (suspicion.shares).</summary>
    public static SortedDictionary<int, double> Shares(Game game, int faction, string good)
    {
        var theirs = new SortedDictionary<int, double>();
        foreach (var ((taker, key, maker), amount) in game.Bought)
            if (taker == faction && key == good)
                theirs[maker] = amount;
        double total = Py.Sum(theirs.Values);
        var shares = new SortedDictionary<int, double>();
        if (total > 0)
            foreach (var (maker, amount) in theirs)
                shares[maker] = amount / total;
        return shares;
    }

    /// <summary>{town: routes away} for every town at most SUSPECT_REACH routes from any of `starts`, not through
    /// ruins, the starts left out (suspicion.within).</summary>
    public static Dictionary<int, int> Within(Game game, IEnumerable<int> starts)
    {
        var links = new Dictionary<int, List<int>>();
        foreach (var route in game.Routes)
        {
            if (game.Towns[route.A].Ruined || game.Towns[route.B].Ruined)
                continue;
            if (!links.TryGetValue(route.A, out var a))
                links[route.A] = a = new List<int>();
            if (!links.TryGetValue(route.B, out var b))
                links[route.B] = b = new List<int>();
            a.Add(route.B);
            b.Add(route.A);
        }
        var hops = starts.ToDictionary(t => t, _ => 0);
        var frontier = hops.Keys.Order().ToList();
        for (int step = 1; step <= game.Settings.SuspectReach; step++)
        {
            var found = new List<int>();
            foreach (int here in frontier)
                foreach (int there in links.GetValueOrDefault(here) ?? new List<int>())
                    if (!hops.ContainsKey(there))
                    {
                        hops[there] = step;
                        found.Add(there);
                    }
            frontier = found.Order().ToList();
        }
        return hops.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value);
    }

    /// <summary>How much of `good` (or of Food) a market ended the year with to spare (suspicion.spare).</summary>
    public static double Spare(Game game, Market market, string good)
    {
        if (good == Food)
        {
            var staples = game.Settings.Staples;
            double left = market.PooledSurplus(staples) - Py.Sum(staples.Select(g => market.Sold.GetValueOrDefault(g)));
            left += Py.Sum(game.Settings.Extras.Select(g => Math.Max(0.0, market.Surplus(g) - market.Sold.GetValueOrDefault(g))));
            return left;
        }
        return market.Made.GetValueOrDefault(good) - market.Needed.GetValueOrDefault(good) - market.Sold.GetValueOrDefault(good);
    }

    /// <summary>
    /// The factions `faction` blames for its shortage of `good` (or Food) in `towns`, drawn by weight with `random`
    /// (the game passes `game.DiceFor("suspicion").Random`); in order of number (suspicion.suspects).
    /// </summary>
    public static List<int> Suspects(Game game, int faction, string good, IEnumerable<int> towns, Market[] markets,
                                     Func<double> random)
    {
        double least = good == Food ? FoodSpare : GoodSpare;
        var weight = new SortedDictionary<int, double>();
        foreach (var (town, away) in Within(game, towns).OrderBy(p => p.Key))
        {
            if (game.Towns[town].Holder is not int holder || holder == faction || Spare(game, markets[town], good) <= least)
                continue;
            weight[holder] = Math.Max(weight.GetValueOrDefault(holder), 1.0 / away);
        }
        foreach (var (maker, share) in Shares(game, faction, good))
            weight[maker] = weight.GetValueOrDefault(maker) + share;
        var candidates = weight.Where(p => p.Value > 0 && game.Factions[p.Key].People > 0).Select(p => p.Key).ToList();
        if (candidates.Count == 0)
            return new List<int>();
        // random.randint(1, SUSPECTS_MOST), never more than there are.
        int most = game.Settings.SuspectsMost;
        int count = Math.Min(candidates.Count, 1 + (int)(random() * most));
        var chosen = new List<int>();
        for (int k = 0; k < count; k++)
        {
            double total = Py.Sum(candidates.Select(f => weight[f]));
            double draw = random() * total;
            int pick = candidates[^1];
            double running = 0.0;
            foreach (int f in candidates)
            {
                running += weight[f];
                if (draw < running)
                {
                    pick = f;
                    break;
                }
            }
            chosen.Add(pick);
            candidates.Remove(pick);
        }
        chosen.Sort();
        return chosen;
    }

    /// <summary>The trade memory as an export's "continue"."suspicion" or a save holds it.</summary>
    public static JsonObject Write(Game game) => new()
    {
        ["bought"] = new JsonArray(game.Bought.OrderBy(p => p.Key.Buyer).ThenBy(p => p.Key.Good, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Seller).Select(p => (JsonNode)new JsonArray(p.Key.Buyer, p.Key.Good, p.Key.Seller, p.Value)).ToArray()),
    };

    /// <summary>Puts back the trade memory `node` holds (none, if it is missing).</summary>
    public static void Read(Game game, JsonNode? node)
    {
        game.Bought.Clear();
        foreach (var entry in node?["bought"] as JsonArray ?? new JsonArray())
            game.Bought[((int)entry![0]!, (string)entry[1]!, (int)entry[2]!)] = (double)entry[3]!;
    }
}
