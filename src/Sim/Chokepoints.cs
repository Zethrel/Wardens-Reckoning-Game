// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;

namespace WardensReckoning.Sim;

/// <summary>
/// Chokepoints (the generator's north/chokepoints.py; yours are in chokepoint_settings.py), part 1 (step 9d): who
/// needs whose goods, and what a shortage brings.
///
/// 1. **Need.** Every town of a faction that needs a good (by its path, its lineage, or "all") needs that much more of
///    it (`AddNeeds`), and running short of it is hardship, like any good it needs.
/// 2. **What a shortage does, the year after** (`BeginYear`, `Adjust`): a smaller harvest, less crafted, less bought
///    from other factions, fewer rituals. The year after, because a year's shortage is known only once its trade is done.
/// 3. **Embargo and resentment** (`Refuses`, `AfterTrade`): a faction will not sell a needed good to a faction it
///    thinks less of than EMBARGO_OPINION (and lifts it only when it thinks LIFT_MARGIN better); a faction short of a
///    good blames those who refused it, and neighbours who had some to spare (RESENTMENT).
/// 4. **Stand-ins** (Trade.StandIn): a town still short of a good after trade buys what stands in for it.
/// 5. **Smugglers**: towns of the SMUGGLERS hand goods on to anyone, and goods passing through them slip past an
///    embargo.
///
/// Part 2 (step 9e) brings new supply contracts, sabotage, labour displacement, secrets, legitimacy, the tech leak
/// and debased coin. Until then the game keeps what the history left of them and trades by it: a contract goes first
/// and is never embargoed, and ends as in the generator; a sabotaged town makes its good again after its years; debased
/// coin is crafted less. But none are made new.
/// </summary>
public static class Chokepoints
{
    /// <summary>chokepoints.py LIFT_MARGIN: an embargo in force is lifted only when the holder thinks this much better
    /// of the buyer than EMBARGO_OPINION.</summary>
    public const double LiftMargin = 0.1;

    /// <summary>The needs that hold for `faction`: "all", its path or its lineage (chokepoints.needs_of).</summary>
    public static IEnumerable<GameSettings.Need> NeedsOf(Game game, FactionNow? faction) =>
        faction == null ? Enumerable.Empty<GameSettings.Need>()
        : game.Settings.Needs.Where(n => n.Who == "all" || n.Who == faction.Path || n.Who == faction.Lineage);

    /// <summary>Add a town's needs to its market (chokepoints.add_needs); `present`, the goods in the world.</summary>
    public static void AddNeeds(Game game, Market market, TownNow town, FactionNow? faction, ISet<string> present)
    {
        double thousands = town.Population / 1000.0;
        foreach (var need in NeedsOf(game, faction))
        {
            if (!present.Contains(need.Good))
                continue;
            market.Needed[need.Good] = market.Needed.GetValueOrDefault(need.Good) + thousands * need.Amount;
            market.Wanted.Remove(need.Good);
            market.Needs.Add(need.Good);
        }
    }

    /// <summary>How much last year's shortages hinder `effect` ("harvest", "trade" or "rituals") at a town, 0 to 1
    /// (chokepoints.hindrance).</summary>
    public static double Hindrance(Game game, int town, FactionNow? faction, string effect)
    {
        var shortOf = game.Short.GetValueOrDefault(town);
        double lost = 0.0;
        foreach (var need in NeedsOf(game, faction))
            lost += need.Of(effect) * (shortOf?.GetValueOrDefault(need.Good) ?? 0.0);
        return Math.Min(1.0, lost);
    }

    /// <summary>How much of `good` a town can craft, 0 to 1, after last year's shortages of what it is made with
    /// (chokepoints.craft_factor).</summary>
    public static double CraftFactor(Game game, int town, FactionNow? faction, string good)
    {
        var shortOf = game.Short.GetValueOrDefault(town);
        // Python's sum() of a generator: Py.Sum, as Python 3.12 adds decimals.
        double lost = Py.Sum(NeedsOf(game, faction).Select(n =>
            n.Craft.GetValueOrDefault(good) * (shortOf?.GetValueOrDefault(n.Good) ?? 0.0)));
        return Math.Max(0.0, 1.0 - lost);
    }

    /// <summary>
    /// The year begins (chokepoints.begin_year): each town's harvest factor this year (a list), and each faction's
    /// ritual factor (the average over its towns) into game.RitualsHindered.
    /// </summary>
    public static List<double> BeginYear(Game game)
    {
        var factors = new List<double>();
        var rituals = new Dictionary<int, List<double>>();
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            var faction = town.Holder is int h ? game.Factions[h] : null;
            factors.Add(1.0 - Hindrance(game, i, faction, "harvest"));
            if (faction != null && !town.Ruined)
            {
                if (!rituals.TryGetValue(town.Holder!.Value, out var shares))
                    rituals[town.Holder.Value] = shares = new List<double>();
                shares.Add(1.0 - Hindrance(game, i, faction, "rituals"));
            }
        }
        game.RitualsHindered.Clear();
        foreach (var (f, shares) in rituals)
            game.RitualsHindered[f] = Py.Sum(shares) / shares.Count;
        return factors;
    }

    /// <summary>
    /// After the markets are made (chokepoints.adjust): crafting cut by last year's shortages and by debased coin, how
    /// much each town may buy from other factions this year, and nothing of a sabotaged good.
    /// </summary>
    public static void Adjust(Game game, Market[] markets)
    {
        string? debasedGood = game.Settings.DebasedGood;
        for (int i = 0; i < markets.Length; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int f)
                continue;
            var market = markets[i];
            var faction = game.Factions[f];
            foreach (var good in market.CanCraft.Keys.ToList())
            {
                double share = CraftFactor(game, i, faction, good);
                if (good == debasedGood)
                    share *= 1.0 - game.Debased.GetValueOrDefault(f);
                if (share < 1.0)
                {
                    market.CanCraft[good] *= share;
                    if (market.CraftingMetal.TryGetValue(good, out double metal))
                    {
                        double cut = metal * (1.0 - share);
                        market.CraftingMetal[good] = metal - cut;
                        market.Needed[Trade.Metal] = Math.Max(0.0, market.Needed.GetValueOrDefault(Trade.Metal) - cut);
                    }
                }
            }
            market.TradeFactor = 1.0 - Hindrance(game, i, faction, "trade");
        }
        foreach (var (town, (good, _)) in game.Sabotaged)
            if (town < markets.Length && markets[town].CanCraft.ContainsKey(good))
            {
                var market = markets[town];
                market.CanCraft[good] = 0.0;
                double cut = market.CraftingMetal.Remove(good, out double metal) ? metal : 0.0;
                market.Needed[Trade.Metal] = Math.Max(0.0, market.Needed.GetValueOrDefault(Trade.Metal) - cut);
            }
    }

    /// <summary>Whether a town is held by a smuggler faction, by its lineage (chokepoints.smuggler_town).</summary>
    public static bool SmugglerTown(Game game, TownNow town) =>
        town.Holder is int f && game.Settings.Smugglers.Count > 0 && game.Factions[f].Lineage is { } lineage
        && game.Settings.Smugglers.Contains(lineage);

    /// <summary>
    /// Whether the seller's faction sells the buyer's these goods (chokepoints.refuses): null if it does; "refused"
    /// if it will not (an embargo on a good the buying town needs, remembered); "smuggled" if it would not, but the
    /// goods pass through a smuggler's town. A supply contract is never embargoed.
    /// </summary>
    public static string? Refuses(Game game, TownNow seller, TownNow buyer, IReadOnlyList<string> goods,
                                  bool throughSmugglers, ISet<string> needed)
    {
        var choke = goods.Where(g => game.Settings.Needs.Any(n => n.Good == g) && needed.Contains(g)).ToList();
        if (choke.Count == 0 || seller.Holder is not int h || buyer.Holder is not int wanting || h == wanting)
            return null;
        if (choke.All(g => game.Contracts.TryGetValue((wanting, g), out int maker) && maker == h))
            return null;
        double limit = game.Settings.EmbargoOpinion;
        if (choke.Any(g => game.Embargoes.Contains((h, wanting, g))))
            limit += LiftMargin;
        if (game.Factions[h].Opinions.GetValueOrDefault(wanting) >= limit)
            return null;
        if (throughSmugglers)
            return "smuggled";
        foreach (var good in choke)
            game.Refused.Add((h, wanting, good));
        return "refused";
    }

    /// <summary>Whether a sale of one good goes before the others: the buyer's faction has a supply contract for it
    /// with the seller's (chokepoints.contract_first).</summary>
    public static bool ContractFirst(Game game, TownNow seller, TownNow buyer, IReadOnlyList<string> goods) =>
        goods.Count == 1 && seller.Holder is int maker && buyer.Holder is int taker
        && game.Contracts.TryGetValue((taker, goods[0]), out int m) && m == maker;

    /// <summary>
    /// After the year's trade (chokepoints.after_trade, part 1): this year's shortages (for next year), resentment
    /// into `blame`, and the news of embargoes begun and lifted.
    /// </summary>
    public static void AfterTrade(Game game, Market[] markets, Dictionary<(int, int), double> blame, int year)
    {
        game.Short.Clear();
        var lacking = new Dictionary<int, Dictionary<string, double[]>>();
        var lackingOrder = new List<int>();
        var spare = new Dictionary<int, Dictionary<string, double>>();
        var chokeGoods = game.Settings.Needs.Select(n => n.Good).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
        for (int i = 0; i < markets.Length; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int f)
                continue;
            var market = markets[i];
            var mine = new Dictionary<string, double>();
            // In the generator this walks a set of names, whose order changes from run to run; the game takes them in
            // order of name, so the blame below is added up in one order every time.
            foreach (var good in market.Needs.OrderBy(g => g, StringComparer.Ordinal))
            {
                double need = market.Needed.GetValueOrDefault(good);
                if (need <= 0)
                    continue;
                double missing = market.Shortage(good);
                mine[good] = missing / need;
                if (!lacking.TryGetValue(f, out var goods))
                {
                    lacking[f] = goods = new Dictionary<string, double[]>();
                    lackingOrder.Add(f);
                }
                if (!goods.TryGetValue(good, out var entry))
                    goods[good] = entry = new double[2];
                entry[0] += missing;
                entry[1] += need;
            }
            if (mine.Count > 0)
                game.Short[i] = mine;
            foreach (var good in chokeGoods)
            {
                double left = market.Made.GetValueOrDefault(good) - market.Needed.GetValueOrDefault(good) - market.Sold.GetValueOrDefault(good);
                if (left > 1e-9)
                {
                    if (!spare.TryGetValue(f, out var theirs))
                        spare[f] = theirs = new Dictionary<string, double>();
                    theirs[good] = theirs.GetValueOrDefault(good) + left;
                }
            }
        }
        Resent(game, lacking, lackingOrder, spare, blame);
        TellEmbargoes(game, year);
        EndContracts(game, markets, year);
        // A sabotaged town makes its good again after its last year of none (chokepoints.sabotage, before new sabotage).
        foreach (var town in game.Sabotaged.Where(p => p.Value.Until <= year).Select(p => p.Key).ToList())
            game.Sabotaged.Remove(town);
    }

    /// <summary>
    /// The supply contracts that end (chokepoints.contract, before new ones are signed in step 9e): when either side
    /// has no people left, either thinks ill of the other (below 0), or the maker's towns no longer make the good.
    /// </summary>
    static void EndContracts(Game game, Market[] markets, int year)
    {
        if (game.Contracts.Count == 0)
            return;
        var made = new HashSet<(string Good, int Faction)>();
        for (int i = 0; i < markets.Length; i++)
            if (!game.Towns[i].Ruined && game.Towns[i].Holder is int f)
                foreach (var (good, amount) in markets[i].Made)
                    if (amount > 0)
                        made.Add((good, f));
        foreach (var ((buyer, good), maker) in game.Contracts.OrderBy(p => p.Key.Buyer)
                     .ThenBy(p => p.Key.Good, StringComparer.Ordinal).ToList())
        {
            var (b, m) = (game.Factions[buyer], game.Factions[maker]);
            bool gone = b.People <= 0 || m.People <= 0;
            bool fallenOut = b.Opinions.GetValueOrDefault(maker) < 0 || m.Opinions.GetValueOrDefault(buyer) < 0;
            if (gone || fallenOut || !made.Contains((good, maker)))
            {
                game.Contracts.Remove((buyer, good));
                if (!gone)
                    game.Record(new GameEvent(year, "chokepoint", "contract_ended",
                        "the " + b.Name + " no longer buy " + good.Replace("_", " ") + " from the " + m.Name + " first",
                        new[] { buyer, maker }, new Dictionary<string, string>
                        {
                            ["buyer"] = b.Name, ["maker"] = m.Name, ["good"] = good.Replace("_", " "),
                        }));
            }
        }
    }

    /// <summary>A faction short of a good blames (RESENTMENT times how short) every living faction that refused it,
    /// and every neighbour with some to spare (chokepoints.resent).</summary>
    static void Resent(Game game, Dictionary<int, Dictionary<string, double[]>> lacking, List<int> order,
                       Dictionary<int, Dictionary<string, double>> spare, Dictionary<(int, int), double> blame)
    {
        double rate = game.Settings.Resentment;
        if (rate <= 0)
            return;
        foreach (int wanting in order)
            foreach (var (good, entry) in lacking[wanting])
            {
                double missing = entry[0], need = entry[1];
                if (missing <= 0 || need <= 0)
                    continue;
                double share = missing / need;
                for (int holder = 0; holder < game.Factions.Count; holder++)
                {
                    if (holder == wanting || game.Factions[holder].People <= 0)
                        continue;
                    bool refused = game.Refused.Contains((holder, wanting, good));
                    bool neighbour = game.Factions[wanting].Border.GetValueOrDefault(holder) > 0;
                    if (refused || (neighbour && (spare.GetValueOrDefault(holder)?.GetValueOrDefault(good) ?? 0.0) > 0))
                        blame[(wanting, holder)] = blame.GetValueOrDefault((wanting, holder)) + rate * share;
                }
            }
    }

    static IEnumerable<(int, int, string)> Sorted(IEnumerable<(int Holder, int Buyer, string Good)> set) =>
        set.OrderBy(e => e.Holder).ThenBy(e => e.Buyer).ThenBy(e => e.Good, StringComparer.Ordinal);

    /// <summary>News of every embargo begun this year, and of every one lifted when the holder thinks better of the
    /// buyer again; then this year's refusals are forgotten (chokepoints.tell_embargoes).</summary>
    static void TellEmbargoes(Game game, int year)
    {
        double limit = game.Settings.EmbargoOpinion;
        foreach (var (holder, buyer, good) in Sorted(game.Refused.Except(game.Embargoes)).ToList())
            Tell(game, year, "embargo", "the " + game.Factions[holder].Name + " refuse " + good.Replace("_", " ") + " to the "
                 + game.Factions[buyer].Name, holder, buyer, good);
        game.Embargoes.UnionWith(game.Refused);
        foreach (var (holder, buyer, good) in Sorted(game.Embargoes).ToList())
        {
            bool gone = game.Factions[holder].People <= 0 || game.Factions[buyer].People <= 0;
            if (gone || game.Factions[holder].Opinions.GetValueOrDefault(buyer) >= limit + LiftMargin)
            {
                game.Embargoes.Remove((holder, buyer, good));
                if (!gone)
                    Tell(game, year, "embargo_lifted", "the " + game.Factions[holder].Name + " sell " + good.Replace("_", " ")
                         + " to the " + game.Factions[buyer].Name + " again", holder, buyer, good);
            }
        }
        game.Refused.Clear();
    }

    static void Tell(Game game, int year, string what, string text, int holder, int buyer, string good) =>
        game.Record(new GameEvent(year, "chokepoint", what, text, new[] { holder, buyer }, new Dictionary<string, string>
        {
            ["holder"] = game.Factions[holder].Name, ["buyer"] = game.Factions[buyer].Name, ["good"] = good.Replace("_", " "),
        }));

    /// <summary>What the chokepoints carry from year to year, as the export's "continue"."chokepoints" or a save
    /// holds it.</summary>
    public static JsonObject Write(Game game) => new()
    {
        ["short"] = new JsonObject(game.Short.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(p.Key.ToString(),
            (JsonNode?)new JsonObject(p.Value.OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Value)))))),
        ["embargoes"] = new JsonArray(Sorted(game.Embargoes).Select(e => (JsonNode)new JsonArray(e.Item1, e.Item2, e.Item3)).ToArray()),
        ["debased"] = new JsonObject(game.Debased.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(p.Key.ToString(), (JsonNode?)p.Value))),
        ["smuggled"] = game.Smuggled,
        ["leaks"] = new JsonArray(game.Leaks.Select(l => (JsonNode)new JsonArray(l.Year, l.Faction)).ToArray()),
        ["contracts"] = new JsonArray(game.Contracts.OrderBy(p => p.Key.Buyer).ThenBy(p => p.Key.Good, StringComparer.Ordinal)
            .Select(p => (JsonNode)new JsonArray(p.Key.Buyer, p.Key.Good, p.Value)).ToArray()),
        ["sabotaged"] = new JsonObject(game.Sabotaged.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(p.Key.ToString(),
            (JsonNode?)new JsonArray(p.Value.Good, p.Value.Until)))),
        ["counts"] = new JsonObject(game.ChokeCounts.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
    };

    /// <summary>Puts back what `node` holds (nothing, if it is null or missing), replacing what the game held.</summary>
    public static void Read(Game game, JsonNode? node)
    {
        game.Short.Clear();
        game.Refused.Clear();
        game.Embargoes.Clear();
        game.Debased.Clear();
        game.Leaks.Clear();
        game.Contracts.Clear();
        game.Sabotaged.Clear();
        game.ChokeCounts.Clear();
        game.Smuggled = 0.0;
        if (node is not JsonObject kept)
            return;
        foreach (var (town, goods) in kept["short"] as JsonObject ?? new JsonObject())
            game.Short[int.Parse(town)] = goods!.AsObject().ToDictionary(g => g.Key, g => (double)g.Value!);
        foreach (var e in kept["embargoes"] as JsonArray ?? new JsonArray())
            game.Embargoes.Add(((int)e![0]!, (int)e[1]!, (string)e[2]!));
        foreach (var (faction, value) in kept["debased"] as JsonObject ?? new JsonObject())
            game.Debased[int.Parse(faction)] = (double)value!;
        game.Smuggled = (double?)kept["smuggled"] ?? 0.0;
        foreach (var l in kept["leaks"] as JsonArray ?? new JsonArray())
            game.Leaks.Add(((int)l![0]!, (string)l[1]!));
        foreach (var c in kept["contracts"] as JsonArray ?? new JsonArray())
            game.Contracts[((int)c![0]!, (string)c[1]!)] = (int)c[2]!;
        foreach (var (town, entry) in kept["sabotaged"] as JsonObject ?? new JsonObject())
            game.Sabotaged[int.Parse(town)] = ((string)entry![0]!, (int)entry[1]!);
        foreach (var (name, count) in kept["counts"] as JsonObject ?? new JsonObject())
            game.ChokeCounts[name] = (int)count!;
    }
}
