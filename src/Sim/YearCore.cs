// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// The core of a year (step 2): the harvest, food, growth and starvation, sizes and ruin. Each part follows the
/// generator's north/history.py run_years, named after the Python function it ports.
///
/// Not yet in the game, and so not in its years: trade (step 3: until then a town lives on its own land),
/// rulers coming and going (step 4: each faction keeps the ruler it ended with), war (5), splits and migration
/// (6), paths and learning (7), attunement (8), happenings, threats and signs (9), and your rules from
/// history_rules.py.
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

        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined)
                continue;
            var record = world.Towns[i];
            var faction = town.Holder is int h ? game.Factions[h] : null;
            double harvest = Harvest(game, i, year);

            // Food: what its own land grows this year against what its people need (make_markets, fed_share).
            double fed = FedShare(town, faction?.Path, harvest, settings);
            town.Fed = fed;
            town.Hungry = fed < 0.999 ? town.Hungry + 1 : 0;
            town.People = Grow(town.People, Capacity(i, game), fed, settings);
            int population = town.Population;

            // Its size, with a margin downwards, and the news when it changes.
            int size = KeepBand(population, settings.SizeLows, town.Size, settings.SizeLows[town.Size] * SizeMargin);
            if (size != town.Size)
            {
                bool grew = size > town.Size;
                game.Events.Add(new GameEvent(year, "size", grew ? "grew" : "shrank",
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
                town.People = 0;
                game.Events.Add(new GameEvent(year, "ruin", "ruin", record.Name + " was abandoned and fell into ruin",
                                              Of(town.Holder), new Dictionary<string, string> { ["place"] = record.Name }));
            }
        }

        // Each faction's people (update_factions), and the news when a faction's last town falls.
        var people = new int[game.Factions.Count];
        foreach (var town in game.Towns)
            if (!town.Ruined && town.Holder is int h)
                people[h] += town.Population;
        for (int f = 0; f < game.Factions.Count; f++)
        {
            if (game.Factions[f].People > 0 && people[f] == 0)
                game.Events.Add(new GameEvent(year, "faction", "faction_fell",
                    "the last settlement of the " + game.Factions[f].Name + " fell", new[] { f },
                    new Dictionary<string, string> { ["faction"] = game.Factions[f].Name }));
            game.Factions[f].People = people[f];
        }

        world.AddYear(game.Towns.Select(t => t.Ruined ? 0 : t.Population).ToList(),
                      game.Towns.Select(t => t.Holder).ToList(),
                      game.Towns.Select(t => t.Ruined).ToList(),
                      people);
        game.Year = year;
    }

    /// <summary>How town `index`'s harvest compares with an ordinary one (1.0) in `year`: the weather, then its
    /// faction's path and ruler. history.py harvests, path_harvests and ruled_harvests.</summary>
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

    /// <summary>How many people a town's land can feed (the export's settlement "capacity").</summary>
    public static double Capacity(int town, Game game) =>
        (double?)game.World.Data["settlements"]![town]!["capacity"] ?? 0.0;

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
