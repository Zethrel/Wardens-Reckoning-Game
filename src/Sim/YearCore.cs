// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// A year: the harvest, the markets and trade (Trade.cs, step 3), the mines dug, food, growth and starvation, sizes
/// and ruin, and each faction's people, metal and memories. Each part follows the generator's north/history.py
/// run_years, named after the Python function it ports.
///
/// Then each faction's opinions of the others (Opinions.cs) and its rulers' year (Rulers.cs, step 4), with your
/// rules from history_rules.py (Rules.cs).
///
/// War and alliance come between the opinions and the rulers (War.cs, step 5).
///
/// Not yet in the game, and so not in its years: splits and migration (6), paths and learning (7),
/// attunement (8), happenings, threats, signs and chokepoints (9), and death's other ways (mass death, the god's
/// finds).
/// </summary>
public static class YearCore
{
    // Engine values from the generator's code (not its settings files), copied as they are there.
    /// <summary>history.py HARVEST_REGION: how many cells wide the weather's patches are on a map 256 cells wide.</summary>
    public const double HarvestRegion = 64;
    /// <summary>history.py SIZE_MARGIN: how far below a size's edge a town must fall, as a share of the edge,
    /// before it counts as the size below (so a town wobbling around an edge does not change size every year).</summary>
    public const double SizeMargin = 0.05;
    /// <summary>leaders.py COMPETENCE_EFFECT: how much a ruler's competence moves the harvest, at its strongest.</summary>
    public const double CompetenceEffect = 0.1;

    /// <summary>Runs the next year of `game`, and adds it to the world's records.</summary>
    public static void RunYear(Game game)
    {
        int year = game.Year + 1;
        var settings = game.Settings;
        var world = game.World;

        // The god of death looks at this year's dead only (step 9a): last year's were all looked at.
        game.Deaths.Clear();
        game.MassDeaths.Clear();
        game.DeathsSeen = game.SignsSeen = 0;

        // Who holds which paths and towns as the year begins (for learning at its end).
        var learningStart = Learning.Remember(game);

        // Every town fed by the people holding it now (tastes): worked out again where its holder changed.
        Feeding.Refresh(game);

        // The harvest (harvests, path_harvests, ruled_harvests), what every town makes and needs, the trade between
        // towns, and the metal dug (make_markets, trade_goods, dig).
        var harvests = Harvests(game, year);
        var markets = Trade.MakeMarkets(game, harvests);
        var deals = Trade.TradeGoods(game, markets);
        Trade.Dig(game, markets, year);

        // Who has food to spare (a hungry town's people may blame them), what each faction's people go through this
        // year (for grievance: rulers), and how much each faction blames each other (souring its memory).
        var spare = FoodToSpare(game, markets);
        var hardship = game.Factions.Select(_ => new Hardship()).ToList();   // a list: a faction founded this year adds its own
        var massLosses = new Dictionary<int, double>();
        var blame = new Dictionary<(int, int), double>();
        var food = settings.Staples.Concat(settings.Extras).ToList();

        // Attunement's mark: marking, spreading, fading, rifts, and what the mark does to the towns (step 8).
        Attunement.Year(game, markets, hardship, blame, year);

        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined)
                continue;
            var record = world.Towns[i];

            // Food: how much of its need its own land and its trade met (fed_share).
            double fed = Trade.FedShare(markets[i], settings.Staples, settings.Extras);
            if (town.Holder is int holder)
            {
                var hard = hardship[holder];
                hard.People += town.Population;
                hard.Hunger += town.Population * (1 - fed);
                hard.Shortages += town.Population * GoodsShort(markets[i], food);
                hard.Towns++;
            }
            town.Fed = fed;
            town.Hungry = fed < 0.999 ? town.Hungry + 1 : 0;
            var faction = town.Holder is int h ? game.Factions[h] : null;
            var view = new PlaceView(record.Name, settings.SizeNames[town.Size], town.Population, Capacity(i, game), fed,
                                     town.Hungry, Shortages(markets[i], settings.Staples), faction?.Name ?? "",
                                     faction?.Path ?? "", new Dictionary<string, double>(faction?.Traits ?? new()));
            double before = town.People;
            town.People = Grow(town.People, Capacity(i, game), fed, settings);
            Rules.SettlementRules(game, i, view, spare, blame, year);
            // Many dead at once, to hunger or a rule: a site of the god of death's (step 9a), and a loss its people feel.
            double massLost = Death.MassDeathOf(before, town.People);
            if (massLost > 0 && town.Holder is int mourner)
            {
                game.MassDeaths.Add((year, town.HX, town.HY, Py.Round(massLost)));
                massLosses[mourner] = massLosses.GetValueOrDefault(mourner) + massLost;
            }
            int population = town.Population;

            // Its size, with a margin downwards, and the news when it changes.
            int size = KeepBand(population, settings.SizeLows, town.Size, settings.SizeLows[town.Size] * SizeMargin);
            if (size != town.Size)
            {
                bool grew = size > town.Size;
                if (!grew && town.Holder is int shrank)
                    hardship[shrank].Lost += 1;
                game.Record(new GameEvent(year, "size", grew ? "grew" : "shrank",
                    string.Format("{0} {1} {2} ({3} people)", record.Name, grew ? "grew into" : "shrank to",
                                  Article(settings.SizeNames[size]), population),
                    Of(town.Holder), new Dictionary<string, string>
                    {
                        ["place"] = record.Name, ["size"] = settings.SizeNames[size], ["people"] = population.ToString(),
                    }));
                town.Size = size;
            }

            // Ruin: no one left, or too few for too long.
            town.LowYears = population < settings.RuinPopulation ? town.LowYears + 1 : 0;
            if (population < 1 || town.LowYears >= settings.RuinYears)
            {
                town.Ruined = true;
                if (town.Holder is int fell)
                    hardship[fell].Lost += 2;
                town.People = 0;
                game.Record(new GameEvent(year, "ruin", "ruin", record.Name + " was abandoned and fell into ruin",
                                              Of(town.Holder), new Dictionary<string, string> { ["place"] = record.Name }));
            }
        }

        var people = UpdateFactions(game, markets, deals, year, blame);
        Opinions.Year(game, markets, year);
        Splits.UpdateEmpires(game);                 // who holds together this year, before the wars
        War.Year(game, markets, hardship, year);
        Rituals.WarYear(game, year);                // desperate attunement factions strike their enemies (step 8)
        // The god of death: metal where many died, beliefs that shift, and her signs (step 9a).
        var deathDice = game.DiceFor("death");
        Death.Year(game, deathDice.Random, year);
        Death.Loss(game, massLosses, deathDice.Random, year);
        Signs.Year(game, game.DiceFor("signs").Random, year);
        Rulers.Year(game, hardship.Select(h => h.Felt()).ToList(), year);
        Capitals.Mend(game, year);                  // a capital lost this year, before the splits measure from it
        Splits.Year(game, hardship, year);
        Migration.Year(game, year);
        Drift.Year(game, year);
        Capitals.Mend(game, year, moving: true);    // and perhaps a capital moved
        Learning.Year(game, learningStart, deals, year);

        world.AddYear(game.Towns.Select(t => t.Ruined ? 0 : t.Population).ToList(),
                      game.Towns.Select(t => t.Holder).ToList(),
                      game.Towns.Select(t => t.Ruined).ToList(),
                      game.Factions.Select(f => f.People).ToList(), StatesOf(game, year), game.Regions);
        game.Year = year;
    }

    /// <summary>
    /// Each faction's people, metal and memories this year (history.update_factions, without the opinions, which
    /// come with step 4): its people and the metal its towns made, what its people need, the news when its last
    /// town falls; and every memory fading by MEMORY_HALF_LIFE, warmed by TRADE_WARMTH for each faction it traded
    /// with, by how much of its needs that trade met. Returns each faction's people.
    /// </summary>
    public static int[] UpdateFactions(Game game, Market[] markets, List<(string Good, int Seller, int Buyer, double Amount)> deals,
                                       int year, Dictionary<(int, int), double>? blame = null)
    {
        var settings = game.Settings;
        int count = game.Factions.Count;
        var people = new int[count];
        var needs = new double[count];
        for (int f = 0; f < count; f++)
        {
            var faction = game.Factions[f];
            double metal = 0;
            for (int i = 0; i < game.Towns.Count; i++)
            {
                var town = game.Towns[i];
                if (town.Ruined || town.Holder != f)
                    continue;
                people[f] += town.Population;
                metal += markets[i].Made.GetValueOrDefault(Trade.Metal);
                needs[f] += markets[i].Needed.Values.Sum();
            }
            faction.MetalSupply = metal;
            faction.MetalNeed = people[f] / 1000.0 * (faction.Path != null ? settings.MetalNeed.GetValueOrDefault(faction.Path) : 0.0);
            if (faction.People > 0 && people[f] == 0)
                game.Record(new GameEvent(year, "faction", "faction_fell",
                    "the last settlement of the " + faction.Name + " fell", new[] { f },
                    new Dictionary<string, string> { ["faction"] = faction.Name }));
            faction.People = people[f];
        }

        var exchanged = new Dictionary<(int, int), double>();
        foreach (var (_, seller, buyer, amount) in deals)
        {
            if (game.Towns[seller].Holder is not int a || game.Towns[buyer].Holder is not int b || a == b)
                continue;
            exchanged[(a, b)] = exchanged.GetValueOrDefault((a, b)) + amount;
            exchanged[(b, a)] = exchanged.GetValueOrDefault((b, a)) + amount;
        }
        double fade = Math.Pow(0.5, 1 / settings.MemoryHalfLife);
        foreach (var pair in game.Memory.Keys.ToList())
        {
            double share = needs[pair.Item1] > 0 ? Math.Min(1.0, exchanged.GetValueOrDefault(pair) / needs[pair.Item1]) : 0.0;
            game.Memory[pair] = Math.Clamp(game.Memory[pair] * fade + settings.TradeWarmth * share
                                           - (blame?.GetValueOrDefault(pair) ?? 0.0), -1.0, 1.0);
        }
        return people;
    }

    /// <summary>Each faction's path, ruler, belief, government and capital this year, for the world's record (the map's
    /// legend and the book's card).</summary>
    public static List<System.Text.Json.Nodes.JsonObject?> StatesOf(Game game, int year) =>
        game.Factions.Select(f => (System.Text.Json.Nodes.JsonObject?)World.ExportedWorld.FactionState(
            f.Path, f.Ruler?.Name, f.Belief, f.Government, f.Capital)).ToList();

    /// <summary>What one faction's people went through this year, added up over its towns (history.run_years' hardship).</summary>
    public sealed class Hardship
    {
        public double People, Hunger, Shortages;
        public int Towns, Lost;

        /// <summary>Hunger and shortages averaged over its people, losses over its towns. history.felt_hardship.</summary>
        public Rulers.Felt Felt() => new(People > 0 ? Hunger / People : 0.0, People > 0 ? Shortages / People : 0.0,
                                         Towns > 0 ? Math.Min(1.0, (double)Lost / Towns) : 0.0,
                                         People > 0 ? 1.0 - Hunger / People : 1.0, Towns);
    }

    /// <summary>The factions with a town that ended the year with food it did not need or sell. history.food_to_spare.</summary>
    public static HashSet<int> FoodToSpare(Game game, Market[] markets)
    {
        var staples = game.Settings.Staples;
        var found = new HashSet<int>();
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int f)
                continue;
            var m = markets[i];
            double left = m.PooledSurplus(staples) - staples.Sum(g => m.Sold.GetValueOrDefault(g));
            left += game.Settings.Extras.Sum(g => Math.Max(0.0, m.Surplus(g) - m.Sold.GetValueOrDefault(g)));
            if (left > 0.01)
                found.Add(f);
        }
        return found;
    }

    /// <summary>How short a town is, on average, of the goods it needs that are not food (only wanted goods do not
    /// count), 0 to 1. history.goods_short.</summary>
    public static double GoodsShort(Market market, IReadOnlyList<string> food)
    {
        var shares = market.Needed.Where(n => n.Value > 0 && !food.Contains(n.Key) && !market.Wanted.Contains(n.Key))
                                  .Select(n => market.Shortage(n.Key) / n.Value).ToList();
        return shares.Count > 0 ? shares.Sum() / shares.Count : 0.0;
    }

    /// <summary>Good → how much of the need is still missing, 0 to 1; every staple, and "staples", how short the town
    /// is of staples altogether. history.shortages.</summary>
    public static Dictionary<string, double> Shortages(Market market, IReadOnlyList<string> staples)
    {
        var missing = market.Needed.Where(n => n.Value > 0).ToDictionary(n => n.Key, n => market.Shortage(n.Key) / n.Value);
        double need = staples.Sum(g => market.Needed.GetValueOrDefault(g));
        if (need > 0)
        {
            double share = market.PooledShortage(staples) / need;
            foreach (var good in staples.Append("staples"))
                missing[good] = share;
        }
        return missing;
    }

    /// <summary>How town `index`'s harvest compares with an ordinary one (1.0) in `year`: the weather, then its
    /// faction's path and ruler. history.py harvests, path_harvests and ruled_harvests.</summary>
    /// <summary>
    /// Every town's harvest this year, as a share of an ordinary one: the weather, its path and its ruler (Harvest);
    /// then the harvest rituals the hungry or frightened factions work (step 8), and the mark on its land. A ruin's is
    /// 1.0. The harvest rituals are worked here, so this is called once a year.
    /// </summary>
    public static List<double> Harvests(Game game, int year)
    {
        var weather = Enumerable.Range(0, game.Towns.Count)
                                .Select(i => Weather(game, game.World.Towns[i].X, game.World.Towns[i].Y, year)).ToList();
        var harvests = Enumerable.Range(0, game.Towns.Count)
                                 .Select(i => game.Towns[i].Ruined ? 1.0 : Harvest(game, i, year)).ToList();
        var gambled = Rituals.HarvestYear(game, weather, year);
        for (int i = 0; i < harvests.Count; i++)
        {
            harvests[i] *= Signs.Factor(game, i, year);                 // a sign's lasting harvest (step 9a)
            if (game.Towns[i].Holder is int gambler && gambled.TryGetValue(gambler, out double factor))
                harvests[i] *= factor;
            if (game.Marks != null && game.Settings.Attuning)
                harvests[i] *= game.Marks.HarvestOf(i);
        }
        return harvests;
    }

    public static double Harvest(Game game, int index, int year)
    {
        var record = game.World.Towns[index];
        var town = game.Towns[index];
        var faction = town.Holder is int h ? game.Factions[h] : null;
        return PathHarvest(Weather(game, record.X, record.Y, year), faction?.Path, game.Settings)
               * RulerFactor(faction, game.Settings);
    }

    /// <summary>The year's weather at map cell (x, y): from 1 - HARVEST_VARIATION to 1 + HARVEST_VARIATION, the same
    /// for nearby towns, as the weather covers a whole region. history.py harvests, with the game's seed.</summary>
    public static double Weather(Game game, int x, int y, int year)
    {
        double region = HarvestRegion * game.World.Size / 256;
        long seed = game.Seed + 9001 + (long)year * 7919;
        return 1.0 + game.Settings.HarvestVariation * (2 * Noise.Value(x / region, y / region, seed) - 1);
    }

    static IReadOnlyList<int> Of(int? faction) => faction is int f ? new[] { f } : Array.Empty<int>();

    /// <summary>The harvest as a path changes it: steadier (PATH_STEADINESS pulls it towards an ordinary 1.0),
    /// then larger (PATH_HARVEST). history.py path_harvests.</summary>
    public static double PathHarvest(double harvest, string? path, GameSettings settings)
    {
        if (path != null && settings.PathSteadiness.TryGetValue(path, out double steady) && steady != 1.0)
            harvest = 1.0 + (harvest - 1.0) * steady;
        return harvest * (path != null && settings.PathHarvest.TryGetValue(path, out double larger) ? larger : 1.0);
    }

    /// <summary>How much the ruler's competence changes the harvest: up to CompetenceEffect either way, as much as
    /// the government lets a ruler matter. leaders.py harvest_factor.</summary>
    public static double RulerFactor(FactionNow? faction, GameSettings settings)
    {
        if (faction?.Competence is not double competence)
            return 1.0;
        double power = settings.RulerPower.TryGetValue(faction.Government, out double p) ? p : 0.0;
        return 1.0 + CompetenceEffect * power * (2 * competence - 1);
    }

    /// <summary>
    /// (Step 2's food, before trade: kept for the tests that check the year's core against the generator.)
    /// How much of a town's food need its own land meets this year, 0 to 1 (1 if it needs no food).
    /// Its land grows its yields of each food good, times the harvest; its people need NeedOf(path) of each
    /// for every 1000. The staples count together (any of them fills a stomach); each extra counts on its own.
    /// history.py fed_share with trade.make_markets, without trade.
    /// </summary>
    public static double FedShare(TownNow town, string? path, double harvest, GameSettings settings)
    {
        double thousands = town.Population / 1000.0;
        double needed = 0, missing;
        double stapleNeed = 0, stapleMade = 0;
        foreach (var good in settings.Staples)
        {
            stapleNeed += thousands * settings.Goods[good].NeedOf(path);
            stapleMade += town.Yields.GetValueOrDefault(good) * harvest;
        }
        needed += stapleNeed;
        missing = Math.Max(0.0, stapleNeed - stapleMade);
        foreach (var good in settings.Extras)
        {
            double need = thousands * settings.Goods[good].NeedOf(path);
            needed += need;
            missing += Math.Max(0.0, need - town.Yields.GetValueOrDefault(good) * harvest);
        }
        return needed <= 0 ? 1.0 : (needed - missing) / needed;
    }

    /// <summary>Next year's people: growth while fed and the land has room, deaths while hungry. history.py grow.</summary>
    public static double Grow(double people, double capacity, double fed, GameSettings settings, double growth = 1.0)
    {
        double room = capacity > 0 ? 1.0 - people / capacity : -1.0;
        double change = settings.GrowthRate * growth * fed * room - settings.Starvation * (1.0 - fed);
        return Math.Max(0.0, people * (1.0 + change));
    }

    /// <summary>How many people a town's land can feed (the export's settlement "capacity", then as Feeding.cs works it out).</summary>
    public static double Capacity(int town, Game game) => game.Towns[town].Capacity;

    /// <summary>The last band whose lowest value `value` reaches. history.py band_of.</summary>
    public static int BandOf(double value, IReadOnlyList<int> lows)
    {
        int index = 0;
        for (int i = 0; i < lows.Count; i++)
            if (value >= lows[i])
                index = i;
        return index;
    }

    /// <summary>Like BandOf, but leaving `current` downwards only once `margin` below its edge. history.py keep_band.</summary>
    public static int KeepBand(double value, IReadOnlyList<int> lows, int current, double margin)
    {
        int band = BandOf(value, lows);
        if (band > current)
            return band;
        if (band < current && value < lows[current] - margin)
            return band;
        return current;
    }

    /// <summary>"a" or "an" before a word. history.py article.</summary>
    public static string Article(string word) => ("aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an " : "a ") + word;
}
