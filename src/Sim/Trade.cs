// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>One town's goods this year: made, needed, bought and sold, by good (the generator's trade.Market).</summary>
public sealed class Market
{
    public Dictionary<string, double> Made { get; } = new();
    public Dictionary<string, double> Needed { get; } = new();
    public Dictionary<string, double> Bought { get; } = new();
    public Dictionary<string, double> Sold { get; } = new();
    /// <summary>Crafted good → the most it could make; and the metal that would take.</summary>
    public Dictionary<string, double> CanCraft { get; } = new();
    public Dictionary<string, double> CraftingMetal { get; } = new();
    /// <summary>Goods wanted, not needed: a shortage of one is no hardship.</summary>
    public HashSet<string> Wanted { get; } = new();

    static double Get(Dictionary<string, double> table, string good) => table.GetValueOrDefault(good);

    public double Surplus(string good) => Get(Made, good) - Get(Needed, good);

    /// <summary>What is still missing of a good after trading.</summary>
    public double Shortage(string good) =>
        Math.Max(0.0, Get(Needed, good) - (Get(Made, good) + Get(Bought, good) - Get(Sold, good)));

    /// <summary>The surplus of several goods that stand in for each other (the staples), counted as one.</summary>
    public double PooledSurplus(IReadOnlyList<string> goods) => goods.Sum(Surplus);

    /// <summary>What is still missing of several goods that stand in for each other.</summary>
    public double PooledShortage(IReadOnlyList<string> goods)
    {
        double have = goods.Sum(g => Get(Made, g) + Get(Bought, g) - Get(Sold, g));
        return Math.Max(0.0, goods.Sum(g => Get(Needed, g)) - have);
    }

    public void Add(Dictionary<string, double> table, string good, double amount) =>
        table[good] = Get(table, good) + amount;
}

/// <summary>
/// Trade (step 3): every town's market, and the goods that change hands along the routes between towns. Each part
/// follows the generator's north/trade.py and is named after the Python function it ports.
///
/// The order of a year's trading: metal first, then the land's goods, then the crafters work with the metal they
/// have, then crafted goods, and last the staple foods all together (any staple fills a stomach). For each good,
/// sellers and buyers are paired cheapest journey first, and as much changes hands as the seller can spare and the
/// buyer needs. With TRADE_HANDS set (the generator's default), goods go hand to hand: along at most TRADE_HANDS
/// routes, none longer than TRADE_REACH, between towns whose factions will trade.
///
/// Not yet in the game's trade: the chokepoints' embargoes, smugglers, stand-ins and supply contracts (step 9).
/// </summary>
public static class Trade
{
    public const string Metal = "metal";

    /// <summary>factions.py DIG_PER_YEAR: the share of a deposit's metal a year of digging takes.</summary>
    public const double DigPerYear = 1.0 / 50;

    /// <summary>history.py MINE_EMPTY: a mine runs dry below this share of what it first held.</summary>
    public const double MineEmpty = 0.05;

    /// <summary>
    /// What every town makes and needs this year (trade.make_markets). `harvest` is, for each town, how this year's
    /// harvest compares with an ordinary one; it changes food goods only. A ruin makes and needs nothing.
    /// </summary>
    public static Market[] MakeMarkets(Game game, IReadOnlyList<double> harvest)
    {
        var settings = game.Settings;
        var towns = game.Towns;
        var markets = towns.Select(_ => new Market()).ToArray();
        var food = new HashSet<string>(settings.Staples.Concat(settings.Extras));
        for (int i = 0; i < towns.Count; i++)
        {
            if (towns[i].Ruined)
                continue;
            foreach (var (good, grown) in towns[i].Yields)
                markets[i].Made[good] = food.Contains(good) ? grown * harvest[i] : grown;
        }
        for (int k = 0; k < game.Mines.Count; k++)
        {
            var mine = game.Mines[k];
            if (DepositHolder(game, mine) is int holder && mine.Metal > 0)
                markets[holder].Add(markets[holder].Made, Metal, mine.Metal * DigPerYear);
        }

        var goods = PresentGoods(game);
        for (int i = 0; i < towns.Count; i++)
        {
            var town = towns[i];
            if (town.Ruined)
                continue;
            var market = markets[i];
            double thousands = town.Population / 1000.0;
            var holder = town.Holder is int h ? game.Factions[h] : null;
            string? path = holder?.Path;
            string? lineage = holder?.Lineage;
            foreach (var good in goods)
            {
                market.Needed[good.Name] = thousands * good.NeedOf(path);
                if (good.Wanted)
                    market.Wanted.Add(good.Name);
            }
            market.Needed[Metal] = path != null ? thousands * settings.MetalNeed.GetValueOrDefault(path) : 0.0;
            string? root = path == null ? null : settings.PathRoot.GetValueOrDefault(path, path);
            foreach (var good in goods)
            {
                var makers = good.MadeBy;
                double made = path != null && makers.TryGetValue(path, out double byPath) ? byPath
                              : root != null ? makers.GetValueOrDefault(root) : 0.0;
                double most = thousands * (made + (lineage != path && lineage != null ? makers.GetValueOrDefault(lineage) : 0.0));
                foreach (var (skill, rate) in holder?.Skills ?? new Dictionary<string, double>())
                    most += thousands * makers.GetValueOrDefault(skill) * rate;    // learned paths and hybrids
                if (holder != null && holder.Learned.Contains(good.Name))
                    most += thousands * (good.Learned ?? 0.0);
                if (most > 0 && good.MadeOn.Count > 0 && !good.MadeOn.Any(town.Near.Contains))
                    most = 0.0;                                                  // not near the places it is made on
                if (most > 0)
                {
                    market.CanCraft[good.Name] = most;
                    market.CraftingMetal[good.Name] = most * good.UsesMetal;
                    market.Needed[Metal] += market.CraftingMetal[good.Name];
                }
            }
        }
        return markets;
    }

    /// <summary>
    /// The goods in the world (trade.present_goods): all but those only factions make while none of them holds a
    /// town, and those made only by learning while no one holding a town has learned them.
    /// </summary>
    public static List<Good> PresentGoods(Game game)
    {
        var holders = game.Towns.Where(t => !t.Ruined && t.Holder != null).Select(t => t.Holder!.Value).ToHashSet();
        var alive = new HashSet<string>();
        var known = new HashSet<string>();
        foreach (int h in holders)
        {
            var f = game.Factions[h];
            if (f.Path != null) alive.Add(f.Path);
            if (f.Lineage != null) alive.Add(f.Lineage);
            known.UnionWith(f.Learned);
            alive.UnionWith(f.Skills.Keys);
        }
        var paths = new HashSet<string>(game.Settings.PathRoot.Keys);
        foreach (var f in game.Factions)
            if (f.Path != null) paths.Add(f.Path);
        var present = new List<Good>();
        foreach (var good in game.Settings.GoodsInOrder)
        {
            if (good.Learned != null && !known.Contains(good.Name) && good.MadeBy.Count == 0)
                continue;
            var makers = good.MadeBy.Keys;
            if (good.MadeBy.Count > 0 && !makers.Any(paths.Contains) && !makers.Any(alive.Contains) && !known.Contains(good.Name))
                continue;
            present.Add(good);
        }
        return present;
    }

    /// <summary>
    /// The town that works a mine (trade.deposit_holder): the one whose land it is on, if that town belongs to the
    /// faction holding the mine; or else the nearest living town of that faction. Ruins work nothing.
    /// </summary>
    public static int? DepositHolder(Game game, Mine mine)
    {
        if (mine.LandTown is int land && !game.Towns[land].Ruined && (mine.Owner == null || game.Towns[land].Holder == mine.Owner))
            return land;
        if (mine.Owner == null)
            return null;
        int? best = null;
        double nearest = double.PositiveInfinity;
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Holder != mine.Owner || town.Ruined)
                continue;
            double distance = Math.Sqrt((double)(town.X - mine.X) * (town.X - mine.X) + (double)(town.Y - mine.Y) * (town.Y - mine.Y));
            if (distance < nearest)                          // the first of equally near towns, as min() of (distance, i)
                (nearest, best) = (distance, i);
        }
        return best;
    }

    /// <summary>Whether two towns' factions will trade with each other (trade.will_trade).</summary>
    public static bool WillTrade(Game game, TownNow seller, TownNow buyer)
    {
        if (seller.Holder == buyer.Holder)
            return true;
        if (seller.Holder is not int a || buyer.Holder is not int b)
            return false;
        double min = game.Settings.MinOpinionToTrade;
        return game.Factions[a].Opinions.GetValueOrDefault(b) >= min && game.Factions[b].Opinions.GetValueOrDefault(a) >= min;
    }

    /// <summary>The cheapest journeys from one town: the cost to each town it reaches, and the routes taken.</summary>
    public sealed record Journeys(Dictionary<int, double> Cost, Dictionary<int, List<Route>> Used);

    /// <summary>
    /// Matches sellers to buyers for every good, in the order above, and writes what changed hands into the markets;
    /// returns every sale as (good, seller, buyer, amount). trade.trade_goods.
    /// </summary>
    public static List<(string Good, int Seller, int Buyer, double Amount)> TradeGoods(Game game, Market[] markets)
    {
        var settings = game.Settings;
        foreach (var route in game.Routes)
            route.Volume = 0.0;
        bool local = settings.TradeHands > 0;
        var journeys = local
            ? LocalJourneys(game, settings.TradeHands, settings.TradeReach * game.HistoryColumns / 256.0)
            : AllJourneys(game);
        var deals = new List<(string, int, int, double)>();
        var staples = settings.Staples;
        var landGoods = settings.GoodsInOrder.Where(g => !g.IsCrafted && !staples.Contains(g.Name))
                                             .Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var craftedGoods = settings.GoodsInOrder.Where(g => g.IsCrafted && !staples.Contains(g.Name))
                                                .Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        foreach (var good in new[] { Metal }.Concat(landGoods))
            Match(game, new[] { good }, markets, journeys, deals, local);
        Craft(markets);
        foreach (var good in craftedGoods)
            Match(game, new[] { good }, markets, journeys, deals, local);
        if (staples.Count > 0)
            Match(game, staples, markets, journeys, deals, local);
        return deals;
    }

    /// <summary>
    /// Every town's journeys hand to hand (trade.local_journeys): at most `hands` routes long, every route at most
    /// `reach`, every two towns it joins lived in and willing to trade. Worked out in rounds: after round k, every
    /// town is known by its cheapest journey of at most k routes.
    /// </summary>
    public static List<Journeys> LocalJourneys(Game game, int hands, double reach)
    {
        var towns = game.Towns;
        var links = towns.Select(_ => new List<(int There, Route Route)>()).ToArray();
        var willing = new Dictionary<(int?, int?), bool>();
        foreach (var route in game.Routes)
        {
            if (route.Cost > reach)
                continue;
            var a = towns[route.A];
            var b = towns[route.B];
            if (a.Ruined || b.Ruined)
                continue;
            var pair = (a.Holder, b.Holder);
            if (!willing.TryGetValue(pair, out bool will))
            {
                will = WillTrade(game, a, b);
                willing[pair] = willing[(b.Holder, a.Holder)] = will;
            }
            if (will)
            {
                links[route.A].Add((route.B, route));
                links[route.B].Add((route.A, route));
            }
        }
        var all = new List<Journeys>();
        for (int start = 0; start < towns.Count; start++)
        {
            var best = new Dictionary<int, double> { [start] = 0.0 };
            var used = new Dictionary<int, List<Route>> { [start] = new() };
            var frontier = new List<int> { start };
            for (int round = 0; round < hands; round++)
            {
                var found = new Dictionary<int, (double Total, List<Route> Path)>();
                foreach (int here in frontier)
                    foreach (var (there, route) in links[here])
                    {
                        double total = best[here] + route.Cost;
                        if (total < best.GetValueOrDefault(there, double.PositiveInfinity)
                            && total < (found.TryGetValue(there, out var f) ? f.Total : double.PositiveInfinity))
                            found[there] = (total, new List<Route>(used[here]) { route });
                    }
                foreach (var (there, (total, path)) in found)
                {
                    best[there] = total;
                    used[there] = path;
                }
                frontier = found.Keys.OrderBy(k => k).ToList();
                if (frontier.Count == 0)
                    break;
            }
            all.Add(new Journeys(best, used));
        }
        return all;
    }

    /// <summary>Every town's cheapest journeys across the whole network, for any distance (trade.all_journeys and
    /// network_costs, Dijkstra's algorithm), used when TRADE_HANDS is not set.</summary>
    public static List<Journeys> AllJourneys(Game game)
    {
        int count = game.Towns.Count;
        var links = Enumerable.Range(0, count).Select(_ => new List<(int There, Route Route)>()).ToArray();
        foreach (var route in game.Routes)
        {
            links[route.A].Add((route.B, route));
            links[route.B].Add((route.A, route));
        }
        var all = new List<Journeys>();
        for (int start = 0; start < count; start++)
        {
            var best = new Dictionary<int, double> { [start] = 0.0 };
            var used = new Dictionary<int, List<Route>> { [start] = new() };
            // As Python's heapq of (cost, town): the cheapest first, the lower town number on a tie.
            var waiting = new PriorityQueue<int, (double, int)>();
            waiting.Enqueue(start, (0.0, start));
            while (waiting.TryDequeue(out int here, out var key))
            {
                if (key.Item1 > best.GetValueOrDefault(here, double.PositiveInfinity))
                    continue;
                foreach (var (there, route) in links[here])
                {
                    double total = key.Item1 + route.Cost;
                    if (total < best.GetValueOrDefault(there, double.PositiveInfinity))
                    {
                        best[there] = total;
                        used[there] = new List<Route>(used[here]) { route };
                        waiting.Enqueue(there, (total, there));
                    }
                }
            }
            all.Add(new Journeys(best, used));
        }
        return all;
    }

    /// <summary>
    /// Trades one good, or several that stand in for each other (the staples), as one (trade.match). A town's surplus
    /// of several goods is all of them made less all of them needed; a seller sends whichever it has most of, and
    /// each good's sale is written down on its own.
    /// </summary>
    static void Match(Game game, IReadOnlyList<string> goods, Market[] markets, List<Journeys> journeys,
                      List<(string, int, int, double)> deals, bool local)
    {
        double Spare(Market m) => m.PooledSurplus(goods) - goods.Sum(g => m.Sold.GetValueOrDefault(g));
        double Wanted(Market m) => -m.PooledSurplus(goods) - goods.Sum(g => m.Bought.GetValueOrDefault(g));

        var surplus = markets.Select(m => m.PooledSurplus(goods)).ToArray();
        var offers = new List<(double Cost, int Seller, int Buyer)>();
        for (int seller = 0; seller < markets.Length; seller++)
        {
            if (surplus[seller] <= 0)
                continue;
            foreach (var (buyer, cost) in journeys[seller].Cost)
                if (buyer != seller && surplus[buyer] < 0
                    && (local || WillTrade(game, game.Towns[seller], game.Towns[buyer])))
                    offers.Add((cost, seller, buyer));
        }
        offers.Sort();                       // cheapest journey first; then by seller, then buyer (as Python sorts tuples)
        foreach (var (_, seller, buyer) in offers)
        {
            double amount = Math.Min(Spare(markets[seller]), Wanted(markets[buyer]));
            if (amount <= 1e-9)
                continue;
            foreach (var route in journeys[seller].Used[buyer])
                route.Volume += amount;
            var stock = markets[seller];
            // The goods it has most of left go first; equal ones in the order given (a stable sort, as Python's).
            var order = goods.Select((g, i) => (g, i))
                             .OrderBy(p => -(stock.Made.GetValueOrDefault(p.g) - stock.Sold.GetValueOrDefault(p.g)))
                             .ThenBy(p => p.i).Select(p => p.g).ToList();
            foreach (var good in order)
            {
                double part = Math.Min(amount, stock.Made.GetValueOrDefault(good) - stock.Sold.GetValueOrDefault(good));
                if (part <= 1e-9)
                    continue;
                stock.Add(stock.Sold, good, part);
                markets[buyer].Add(markets[buyer].Bought, good, part);
                deals.Add((good, seller, buyer, part));
                amount -= part;
                if (amount <= 1e-9)
                    break;
            }
        }
    }

    /// <summary>
    /// The crafters work with the metal their town has left (trade.craft): its people's own need comes first; with all
    /// the metal they need they make as much as they can, with half of it half as much.
    /// </summary>
    public static void Craft(Market[] markets)
    {
        foreach (var market in markets)
        {
            if (market.CanCraft.Count == 0)
                continue;
            double crafting = market.CraftingMetal.Values.Sum();
            double have = market.Made.GetValueOrDefault(Metal) + market.Bought.GetValueOrDefault(Metal) - market.Sold.GetValueOrDefault(Metal);
            double ownNeed = market.Needed.GetValueOrDefault(Metal) - crafting;
            double left = Math.Max(0.0, have - ownNeed);
            double share = crafting == 0 ? 1.0 : Math.Min(1.0, left / crafting);
            foreach (var (good, most) in market.CanCraft)
            {
                bool usesMetal = market.CraftingMetal.GetValueOrDefault(good) > 0;
                market.Add(market.Made, good, usesMetal ? most * share : most);
            }
        }
    }

    /// <summary>
    /// Takes this year's metal out of every worked mine (history.dig): what the town used (its need and its sales,
    /// never more than it offered), and its faction's greed digs that share of the rest as well. A mine below
    /// MineEmpty of what it first held runs dry.
    /// </summary>
    public static void Dig(Game game, Market[] markets, int year)
    {
        var worked = new Dictionary<int, List<Mine>>();
        var order = new List<int>();
        foreach (var mine in game.Mines)
            if (mine.Metal > 0 && DepositHolder(game, mine) is int holder)
            {
                if (!worked.ContainsKey(holder))
                {
                    worked[holder] = new List<Mine>();
                    order.Add(holder);
                }
                worked[holder].Add(mine);
            }
        foreach (int holder in order)
        {
            var market = markets[holder];
            double offered = market.Made.GetValueOrDefault(Metal);
            if (offered <= 0)
                continue;
            double used = Math.Min(offered, market.Needed.GetValueOrDefault(Metal) + market.Sold.GetValueOrDefault(Metal));
            double greed = game.Towns[holder].Holder is int f ? game.Factions[f].Greed : 0.0;
            double share = (used + greed * (offered - used)) / offered;
            foreach (var mine in worked[holder])
            {
                mine.Metal -= mine.Metal * DigPerYear * share;
                if (mine.Metal < MineEmpty * mine.First)
                {
                    mine.Metal = 0.0;
                    string name = mine.Name != "" ? mine.Name : "(unnamed)";
                    game.Record(new GameEvent(year, "mine", "mine_dry", "the mine " + name + " ran dry",
                        game.Towns[holder].Holder is int h ? new[] { h } : Array.Empty<int>(),
                        new Dictionary<string, string> { ["mine"] = name }));
                }
            }
        }
    }

    /// <summary>How much of a town's food need was met after trading, 0 to 1 (1 if it needs no food). The staples
    /// count together; each extra on its own. history.py fed_share.</summary>
    public static double FedShare(Market market, IReadOnlyList<string> staples, IReadOnlyList<string> extras)
    {
        double needed = staples.Concat(extras).Sum(g => market.Needed.GetValueOrDefault(g));
        if (needed <= 0)
            return 1.0;
        double missing = market.PooledShortage(staples) + extras.Sum(market.Shortage);
        return (needed - missing) / needed;
    }
}
