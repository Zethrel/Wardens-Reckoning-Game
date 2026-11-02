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
/// Part 2 (step 9e), after the year's trade (`AfterTrade`):
/// 6. **Tech leak**: the mechanism factions that take artifact cores apart may learn to make resonance engines.
/// 7. **Debased coin**: the Coinhouse at war may debase its coin; less of it is crafted, and its buyers resent it.
/// 8. **Supply contracts**: a faction may sign with a maker it is on good terms with; the maker then sells to it first
///    and never embargoes it.
/// 9. **Sabotage**: at war, a faction may wreck an enemy town that makes a needed good, for some years.
/// 10. **Labour displacement**: constructs at work in towns of the displaced paths slow their growth.
/// 11. **Secrets**: a faction that buys secrets may learn one about a rival; sold twice, an alliance may break.
/// 12. **Legitimacy**: a faction supplied with ancestor consultation feels less grievance.
///
/// The leak and debasement roll the chokepoints' dice ("chokepoints"), the rest their own ("chokepoint parts"), as in
/// the generator.
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

    /// <summary>After the year's trade, with no deals and the game's own dice (for the tests of part 1).</summary>
    public static void AfterTrade(Game game, Market[] markets, Dictionary<(int, int), double> blame, int year) =>
        AfterTrade(game, markets, new List<(string, int, int, double)>(), blame, year,
                   game.DiceFor("chokepoints").Random, game.DiceFor("chokepoint parts").Random);

    /// <summary>
    /// After the year's trade (chokepoints.after_trade): this year's shortages (for next year), resentment into
    /// `blame`, the news of embargoes begun and lifted; then part 2 (step 9e): the tech leak and debasement (with
    /// `rng`, the chokepoints' dice), and supply contracts, sabotage, labour displacement, secrets and legitimacy
    /// (with `dice`, their own).
    /// </summary>
    public static void AfterTrade(Game game, Market[] markets, List<(string Good, int Seller, int Buyer, double Amount)> deals,
                                  Dictionary<(int, int), double> blame, int year, Func<double> rng, Func<double> dice)
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
        Leak(game, markets, rng, year);
        Debase(game, deals, blame, rng, year);
        Contract(game, markets, dice, year);
        Sabotage(game, markets, blame, dice, year);
        Displace(game, markets, blame);
        Secrets(game, markets, deals, blame, dice, year);
        Legitimacy(game, markets, deals, blame);
    }

    static string Plain(string good) => good.Replace("_", " ");

    static void Tell(Game game, int year, string what, string text, IReadOnlyList<int> factions, Dictionary<string, string> facts) =>
        game.Record(new GameEvent(year, "chokepoint", what, text, factions, facts));

    /// <summary>
    /// The tech leak (chokepoints.leak): a living LEAK_PATH faction that has not learned LEAK_GOOD, and whose towns have
    /// some LEAK_FROM (made or bought), learns it with LEAK_CHANCE times how well its need of LEAK_FROM was met.
    /// </summary>
    static void Leak(Game game, Market[] markets, Func<double> rng, int year)
    {
        var settings = game.Settings;
        if (settings.LeakChance <= 0 || settings.LeakFrom is not { } source || settings.LeakGood is not { } learned)
            return;
        var have = new Dictionary<int, double>();
        var need = new Dictionary<int, double>();
        for (int i = 0; i < markets.Length; i++)
        {
            if (game.Towns[i].Ruined || game.Towns[i].Holder is not int f)
                continue;
            have[f] = have.GetValueOrDefault(f) + (markets[i].Made.GetValueOrDefault(source) + markets[i].Bought.GetValueOrDefault(source));
            need[f] = need.GetValueOrDefault(f) + markets[i].Needed.GetValueOrDefault(source);
        }
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            if (faction.Path != settings.LeakPath || faction.Learned.Contains(learned) || faction.People <= 0)
                continue;
            double got = have.GetValueOrDefault(f);
            if (got <= 0)
                continue;
            double supply = need.GetValueOrDefault(f) > 0 ? Math.Min(1.0, got / need[f]) : 1.0;
            if (rng() < settings.LeakChance * supply)
            {
                faction.Learned.Add(learned);
                game.Leaks.Add((year, faction.Name));
                Tell(game, year, "tech_leak", "the " + faction.Name + " learn to make " + Plain(learned) + " from the "
                     + Plain(source) + " they take apart", new[] { f },
                     new() { ["faction"] = faction.Name, ["good"] = Plain(learned), ["source"] = Plain(source) });
            }
        }
    }

    /// <summary>
    /// Debased coin (chokepoints.debase): every debased coin recovers DEBASE_RECOVERY (gone below 0.01); a living
    /// DEBASER faction at war debases its coin with DEBASE_CHANCE, by DEBASE_STEP (at most 0.9); every other faction
    /// that bought its DEBASED_GOOD this year blames it, DEBASE_RESENTMENT times how debased.
    /// </summary>
    static void Debase(Game game, List<(string Good, int Seller, int Buyer, double Amount)> deals,
                       Dictionary<(int, int), double> blame, Func<double> rng, int year)
    {
        var settings = game.Settings;
        if (settings.Debaser is not { } debaser)
            return;
        foreach (int f in game.Debased.Keys.ToList())
        {
            game.Debased[f] *= 1.0 - settings.DebaseRecovery;
            if (game.Debased[f] < 0.01)
                game.Debased.Remove(f);
        }
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            if (faction.Lineage != debaser || faction.People <= 0 || settings.DebaseChance <= 0)
                continue;
            bool atWar = game.Wars.Any(w => w.Ended == null && (w.Sides[0].Contains(f) || w.Sides[1].Contains(f)));
            if (atWar && rng() < settings.DebaseChance)
            {
                game.Debased[f] = Math.Min(0.9, game.Debased.GetValueOrDefault(f) + settings.DebaseStep);
                Tell(game, year, "debased", "the " + faction.Name + " debase their coin to pay for the war", new[] { f },
                     new() { ["faction"] = faction.Name, ["good"] = Plain(settings.DebasedGood ?? "") });
            }
        }
        if (settings.DebaseResentment <= 0 || game.Debased.Count == 0)
            return;
        var bought = new SortedSet<(int Taker, int Maker)>();
        foreach (var (good, seller, buyer, _) in deals)
        {
            if (good != settings.DebasedGood)
                continue;
            if (game.Towns[seller].Holder is int maker && game.Debased.ContainsKey(maker)
                && game.Towns[buyer].Holder is int taker && taker != maker)
                bought.Add((taker, maker));
        }
        foreach (var (taker, maker) in bought)
            blame[(taker, maker)] = blame.GetValueOrDefault((taker, maker)) + settings.DebaseResentment * game.Debased[maker];
    }

    /// <summary>
    /// Supply contracts (chokepoints.contract): those end when either side has no people left, either thinks ill of
    /// the other (below 0), or the maker's towns no longer make the good. Then each living faction, for each good it
    /// needs (by name) that it has no contract for, signs one with CONTRACT_CHANCE: with the faction that makes most
    /// of it (the first by number on a tie) of those both think CONTRACT_OPINION of.
    /// </summary>
    static void Contract(Game game, Market[] markets, Func<double> dice, int year)
    {
        var made = new Dictionary<string, Dictionary<int, double>>();
        for (int i = 0; i < markets.Length; i++)
            if (!game.Towns[i].Ruined && game.Towns[i].Holder is int f)
                foreach (var (good, amount) in markets[i].Made)
                    if (amount > 0)
                    {
                        if (!made.TryGetValue(good, out var makers))
                            made[good] = makers = new Dictionary<int, double>();
                        makers[f] = makers.GetValueOrDefault(f) + amount;
                    }
        EndContracts(game, made, year);
        double chance = game.Settings.ContractChance, opinion = game.Settings.ContractOpinion;
        if (chance <= 0)
            return;
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            if (faction.People <= 0)
                continue;
            foreach (var good in NeedsOf(game, faction).Select(n => n.Good).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList())
            {
                if (game.Contracts.ContainsKey((f, good)) || dice() >= chance)
                    continue;
                // min((-amount, maker)): the one that makes most, the first by number on a tie.
                var makers = (made.GetValueOrDefault(good) ?? new Dictionary<int, double>())
                    .Where(m => m.Key != f && faction.Opinions.GetValueOrDefault(m.Key) >= opinion
                                && game.Factions[m.Key].Opinions.GetValueOrDefault(f) >= opinion)
                    .OrderBy(m => -m.Value).ThenBy(m => m.Key).ToList();
                if (makers.Count == 0)
                    continue;
                int maker = makers[0].Key;
                game.Contracts[(f, good)] = maker;
                game.ChokeCounts["contracts"] = game.ChokeCounts.GetValueOrDefault("contracts") + 1;
                Tell(game, year, "contract", "the " + faction.Name + " sign to buy " + Plain(good) + " from the "
                     + game.Factions[maker].Name + " first", new[] { f, maker },
                     new() { ["buyer"] = faction.Name, ["maker"] = game.Factions[maker].Name, ["good"] = Plain(good) });
            }
        }
    }

    /// <summary>
    /// Sabotage (chokepoints.sabotage): a sabotaged town makes its good again after its last year of none. Then in
    /// every war, each living faction of each side, in order, may with SABOTAGE_CHANCE wreck a town of the other side
    /// (not one wrecked already) that makes a good someone needs: chosen at random of all such (town, good); it makes
    /// none for SABOTAGE_YEARS, and its holder blames the saboteur by SABOTAGE_RESENTMENT.
    /// </summary>
    static void Sabotage(Game game, Market[] markets, Dictionary<(int, int), double> blame, Func<double> dice, int year)
    {
        foreach (var town in game.Sabotaged.Where(p => p.Value.Until <= year).Select(p => p.Key).ToList())
            game.Sabotaged.Remove(town);
        var settings = game.Settings;
        if (settings.SabotageChance <= 0)
            return;
        var chokeGoods = settings.Needs.Select(n => n.Good).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
        foreach (var war in game.Wars)
        {
            if (war.Ended != null)
                continue;
            foreach (var (ours, theirs) in new[] { (war.Sides[0], war.Sides[1]), (war.Sides[1], war.Sides[0]) })
                foreach (int saboteur in ours.Order().ToList())
                {
                    if (game.Factions[saboteur].People <= 0 || dice() >= settings.SabotageChance)
                        continue;
                    var targets = new List<(int Town, string Good)>();
                    for (int t = 0; t < markets.Length; t++)
                        if (!game.Towns[t].Ruined && game.Towns[t].Holder is int h && theirs.Contains(h) && !game.Sabotaged.ContainsKey(t))
                            foreach (var good in chokeGoods)
                                if (markets[t].Made.GetValueOrDefault(good) > 0)
                                    targets.Add((t, good));
                    if (targets.Count == 0)
                        continue;
                    // random.choice, then random.randint(low, high).
                    var (town, wrecked) = targets[(int)(dice() * targets.Count)];
                    var (low, high) = settings.SabotageYears;
                    int years = low + (int)(dice() * (high - low + 1));
                    game.Sabotaged[town] = (wrecked, year + years);
                    int victim = game.Towns[town].Holder!.Value;
                    blame[(victim, saboteur)] = blame.GetValueOrDefault((victim, saboteur)) + settings.SabotageResentment;
                    game.ChokeCounts["sabotage"] = game.ChokeCounts.GetValueOrDefault("sabotage") + 1;
                    string place = game.World.Towns[town].Name;
                    Tell(game, year, "sabotage", "the " + game.Factions[saboteur].Name + " wreck the " + Plain(wrecked) + " of "
                         + place + " (" + years + " years)", new[] { saboteur, victim }, new()
                         {
                             ["saboteur"] = game.Factions[saboteur].Name, ["place"] = place,
                             ["faction"] = game.Factions[victim].Name, ["good"] = Plain(wrecked), ["years"] = years.ToString(),
                         });
                }
        }
    }

    /// <summary>
    /// Labour displacement (chokepoints.displace): in each town of a DISPLACED_PATHS faction (not of the
    /// CONSTRUCT_MAKER), the CONSTRUCTS at work (what it made and bought less what it sold, by weight) for every
    /// thousand people, over DISPLACE_FULL, is the share displaced: its growth this year is DISPLACE_GROWTH times that
    /// less. Each displaced faction blames the makers (DISPLACE_RESENTMENT) and warms to the other displaced
    /// (DISPLACE_KINSHIP), by its share.
    /// </summary>
    static void Displace(Game game, Market[] markets, Dictionary<(int, int), double> blame)
    {
        var settings = game.Settings;
        game.DisplacedGrowth.Clear();
        if (settings.Constructs.Count == 0 || settings.DisplaceFull <= 0)
            return;
        var shares = new Dictionary<int, double[]>();
        for (int t = 0; t < markets.Length; t++)
        {
            var town = game.Towns[t];
            if (town.Ruined || town.Holder is not int f || town.Population <= 0)
                continue;
            var faction = game.Factions[f];
            if (faction.Path == null || !settings.DisplacedPaths.Contains(faction.Path) || faction.Lineage == settings.ConstructMaker)
                continue;
            var market = markets[t];
            double atWork = Py.Sum(settings.Constructs.Select(c => c.Weight * Math.Max(0.0,
                market.Made.GetValueOrDefault(c.Good) + market.Bought.GetValueOrDefault(c.Good) - market.Sold.GetValueOrDefault(c.Good))));
            double share = Math.Min(1.0, atWork / (town.Population / 1000.0) / settings.DisplaceFull);
            if (share > 0)
                game.DisplacedGrowth[t] = 1.0 - settings.DisplaceGrowth * share;
            if (!shares.TryGetValue(f, out var entry))
                shares[f] = entry = new double[2];
            entry[0] += town.Population * share;
            entry[1] += town.Population;
        }
        var displaced = shares.Where(p => p.Value[1] > 0 && p.Value[0] > 0).OrderBy(p => p.Key)
                              .Select(p => (Faction: p.Key, Share: p.Value[0] / p.Value[1])).ToList();
        var makers = Enumerable.Range(0, game.Factions.Count)
                               .Where(f => game.Factions[f].Lineage == settings.ConstructMaker && game.Factions[f].People > 0).ToList();
        foreach (var (index, share) in displaced)
        {
            foreach (int other in makers)
                blame[(index, other)] = blame.GetValueOrDefault((index, other)) + settings.DisplaceResentment * share;
            foreach (var (other, theirs) in displaced)
                if (other != index)
                    blame[(index, other)] = blame.GetValueOrDefault((index, other)) - settings.DisplaceKinship * Math.Min(share, theirs);
        }
    }

    /// <summary>This year's growth factor of a town where constructs displace labour (chokepoints.growth_factor).</summary>
    public static double GrowthFactor(Game game, int town) => game.DisplacedGrowth.GetValueOrDefault(town, 1.0);

    /// <summary>
    /// Secrets (chokepoints.secrets): each faction that bought SECRETS this year (in order), with SECRET_CHANCE times
    /// how well its need was met, learns a secret about a rival (a living neighbour, or else any living faction, at
    /// random; never the buyer or the seller, the lowest-numbered of those it bought from) and blames it by
    /// SECRET_SOURING. With DOUBLE_SALE the same secret was sold to the rival, who blames the buyer too; then if the
    /// two are allies, the alliance breaks with BETRAYAL.
    /// </summary>
    static void Secrets(Game game, Market[] markets, List<(string Good, int Seller, int Buyer, double Amount)> deals,
                        Dictionary<(int, int), double> blame, Func<double> dice, int year)
    {
        var settings = game.Settings;
        if (settings.Secrets.Count == 0 || settings.SecretChance <= 0)
            return;
        var bought = new Dictionary<int, double>();
        var needed = new Dictionary<int, double>();
        var sellers = new Dictionary<int, HashSet<int>>();
        foreach (var (good, seller, buyer, amount) in deals)
            if (settings.Secrets.Contains(good) && game.Towns[buyer].Holder is int b && game.Towns[seller].Holder is int s)
            {
                bought[b] = bought.GetValueOrDefault(b) + amount;
                if (!sellers.TryGetValue(b, out var from))
                    sellers[b] = from = new HashSet<int>();
                from.Add(s);
            }
        // sum(market.needed.get(g) for g in goods) over a set of goods: added in order of name here.
        var secretGoods = settings.Secrets.OrderBy(g => g, StringComparer.Ordinal).ToList();
        for (int i = 0; i < markets.Length; i++)
            if (!game.Towns[i].Ruined && game.Towns[i].Holder is int f)
                needed[f] = needed.GetValueOrDefault(f) + Py.Sum(secretGoods.Select(g => markets[i].Needed.GetValueOrDefault(g)));
        foreach (int buyer in bought.Keys.Order().ToList())
        {
            double share = needed.GetValueOrDefault(buyer) > 0 ? Math.Min(1.0, bought[buyer] / needed[buyer]) : 1.0;
            if (dice() >= settings.SecretChance * share)
                continue;
            int seller = sellers[buyer].Min();
            var living = Enumerable.Range(0, game.Factions.Count)
                                   .Where(f => game.Factions[f].People > 0 && f != buyer && f != seller).ToList();
            var rivals = living.Where(f => game.Factions[buyer].Border.GetValueOrDefault(f) > 0).ToList();
            if (rivals.Count == 0)
                rivals = living;
            if (rivals.Count == 0)
                continue;
            int about = rivals[(int)(dice() * rivals.Count)];          // random.choice
            blame[(buyer, about)] = blame.GetValueOrDefault((buyer, about)) + settings.SecretSouring;
            game.ChokeCounts["secrets"] = game.ChokeCounts.GetValueOrDefault("secrets") + 1;
            string b = game.Factions[buyer].Name, a = game.Factions[about].Name, s = game.Factions[seller].Name;
            Tell(game, year, "secret", "the " + b + " buy a secret of the " + a + " from the " + s, new[] { buyer, about, seller },
                 new() { ["buyer"] = b, ["about"] = a, ["seller"] = s });
            if (dice() >= settings.DoubleSale)
                continue;
            blame[(about, buyer)] = blame.GetValueOrDefault((about, buyer)) + settings.SecretSouring;
            var pair = buyer < about ? (buyer, about) : (about, buyer);
            if (game.Alliances.Contains(pair) && dice() < settings.Betrayal)
            {
                game.Alliances.Remove(pair);
                game.ChokeCounts["betrayals"] = game.ChokeCounts.GetValueOrDefault("betrayals") + 1;
                Tell(game, year, "secret_betrayal", "the " + s + " sold the same secret to both: the alliance of the " + b
                     + " and the " + a + " breaks", new[] { buyer, about, seller },
                     new() { ["first"] = b, ["second"] = a, ["seller"] = s });
            }
        }
    }

    /// <summary>
    /// Legitimacy (chokepoints.legitimacy): how well each faction's towns were supplied with LEGITIMACY_GOOD lowers its
    /// grievance this year, by LEGITIMACY when in full (none needed: not at all); and every faction that bought it, or
    /// what stands in for it, from another warms to the seller by SOFT_POWER.
    /// </summary>
    static void Legitimacy(Game game, Market[] markets, List<(string Good, int Seller, int Buyer, double Amount)> deals,
                           Dictionary<(int, int), double> blame)
    {
        var settings = game.Settings;
        if (settings.LegitimacyGood is not { } good || settings.Legitimacy <= 0)
            return;
        var had = new Dictionary<int, double>();
        var needed = new Dictionary<int, double>();
        for (int i = 0; i < markets.Length; i++)
        {
            if (game.Towns[i].Ruined || game.Towns[i].Holder is not int f)
                continue;
            double need = markets[i].Needed.GetValueOrDefault(good);
            needed[f] = needed.GetValueOrDefault(f) + need;
            had[f] = had.GetValueOrDefault(f) + (need - markets[i].Shortage(good));
        }
        for (int f = 0; f < game.Factions.Count; f++)
        {
            double supplied = needed.GetValueOrDefault(f) > 0 ? had.GetValueOrDefault(f) / needed[f] : 0.0;
            game.GrievanceHindered[f] = 1.0 - settings.Legitimacy * supplied;
        }
        var kinds = new HashSet<string> { good };
        foreach (var (stood, others) in settings.StandIns)
            if (stood == good)
                kinds.UnionWith(others);
        var pairs = new SortedSet<(int Buyer, int Seller)>();
        foreach (var (sold, seller, buyer, _) in deals)
            if (kinds.Contains(sold) && game.Towns[buyer].Holder is int b && game.Towns[seller].Holder is int s && b != s)
                pairs.Add((b, s));
        foreach (var pair in pairs)
            blame[pair] = blame.GetValueOrDefault(pair) - settings.SoftPower;
    }

    /// <summary>
    /// The supply contracts that end (part of chokepoints.contract): when either side has no people left, either
    /// thinks ill of the other (below 0), or the maker's towns no longer make the good.
    /// </summary>
    static void EndContracts(Game game, Dictionary<string, Dictionary<int, double>> made, int year)
    {
        if (game.Contracts.Count == 0)
            return;
        foreach (var ((buyer, good), maker) in game.Contracts.OrderBy(p => p.Key.Buyer)
                     .ThenBy(p => p.Key.Good, StringComparer.Ordinal).ToList())
        {
            var (b, m) = (game.Factions[buyer], game.Factions[maker]);
            bool gone = b.People <= 0 || m.People <= 0;
            bool fallenOut = b.Opinions.GetValueOrDefault(maker) < 0 || m.Opinions.GetValueOrDefault(buyer) < 0;
            if (gone || fallenOut || !(made.GetValueOrDefault(good)?.ContainsKey(maker) ?? false))
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
