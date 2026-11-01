// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.World;

namespace WardensReckoning.Sim;

/// <summary>
/// What migration reads and remembers (the export's "continue" → "migration", and migration.Movements): the history's
/// site scores and their parts, heights, travel costs and near places, the free land left to settle, each people's own
/// site scores, and the rifts where no one settles. Grids are flat, row after row, on the history's grid.
/// </summary>
public sealed class Movements
{
    public double[] Scores { get; init; } = Array.Empty<double>();
    public Dictionary<string, double[]> Parts { get; init; } = new();
    public double[] Heights { get; init; } = Array.Empty<double>();
    public double SeaLevel { get; init; }
    public double[] Costs { get; init; } = Array.Empty<double>();
    public Dictionary<string, bool[]> Near { get; init; } = new();
    /// <summary>The free land left to settle by the usual score, best first; null until first needed.</summary>
    public List<(double Score, int X, int Y)>? Sites { get; set; }
    /// <summary>Free land by a people's own score on a path, best first.</summary>
    public Dictionary<(int People, string? Path), List<(double Score, int X, int Y)>> OwnSites { get; } = new();
    /// <summary>Each people's own site score on a path, worked out the first time it was needed and kept.</summary>
    public Dictionary<(int People, string? Path), double[]> Tastes { get; } = new();
    /// <summary>How many cells wide a patch of the mark is, and the patches that are rifts now (null: no attunement).</summary>
    public int RiftSize { get; init; } = 1;
    public HashSet<(int, int)>? Rifts { get; init; }
    /// <summary>Each town's site score, exactly (a ruin is resettled by it).</summary>
    public List<double> TownScores { get; init; } = new();

    /// <summary>An exact grid as the export writes it: base64 of 8-byte little-endian numbers.</summary>
    public static double[] Exact(string text)
    {
        var bytes = Convert.FromBase64String(text);
        var values = new double[bytes.Length / 8];
        for (int i = 0; i < values.Length; i++)
            values[i] = BitConverter.ToDouble(bytes, i * 8);    // little-endian on every PC and phone .NET runs on
        return values;
    }

    /// <summary>A grid as exact text again (for a save).</summary>
    public static string Exact(IReadOnlyList<double> values)
    {
        var bytes = new byte[values.Count * 8];
        for (int i = 0; i < values.Count; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), values[i]);
        return Convert.ToBase64String(bytes);
    }

    static List<(double, int, int)>? ReadSites(JsonNode? node) =>
        node is JsonArray list ? list.Select(s => ((double)s![0]!, (int)s[1]!, (int)s[2]!)).ToList() : null;

    public static JsonArray WriteSites(IEnumerable<(double Score, int X, int Y)> sites) =>
        new(sites.Select(s => (JsonNode?)new JsonArray(s.Score, s.X, s.Y)).ToArray());

    /// <summary>From the export's "continue" → "migration".</summary>
    public static Movements Read(JsonObject node)
    {
        var moving = new Movements
        {
            Scores = Exact((string)node["scores"]!),
            Parts = node["parts"]!.AsObject().ToDictionary(p => p.Key, p => Exact((string)p.Value!)),
            Heights = Exact((string)node["heights"]!),
            SeaLevel = (double)node["sea_level"]!,
            Costs = Exact((string)node["costs"]!),
            Near = node["near"]!.AsObject().ToDictionary(p => p.Key, p =>
            {
                var flat = new List<bool>();
                var runs = p.Value!.AsArray();
                for (int i = 0; i < runs.Count; i += 2)
                    flat.AddRange(Enumerable.Repeat((int)runs[i]! == 1, (int)runs[i + 1]!));
                return flat.ToArray();
            }),
            Sites = ReadSites(node["sites"]),
            RiftSize = (int?)node["rifts"]?["size"] ?? 1,
            Rifts = node["rifts"] is JsonObject rifts
                ? rifts["severe"]!.AsArray().Select(p => ((int)p![0]!, (int)p[1]!)).ToHashSet() : null,
            TownScores = Exact((string)node["town_scores"]!).ToList(),
        };
        foreach (var entry in node["own_sites"]!.AsArray())
            moving.OwnSites[((int)entry![0]!, (string?)entry[1])] = ReadSites(entry[2])!;
        foreach (var entry in node["tastes"]!.AsArray())
            moving.Tastes[((int)entry![0]!, (string?)entry[1])] = Exact((string)entry[2]!);
        return moving;
    }
}

/// <summary>
/// Migration (the generator's north/migration.py): people move to new land. A town that has filled its land
/// (CROWDED of its capacity) or gone hungry HUNGRY_YEARS years in a row may send some of its people away: a chance of
/// MIGRATION a year, and MIGRANTS of its people leave, to the best place within REACH of home, by their people's own
/// site score where it has one (tastes.py): free land, where they found a new town with land of its own and a route
/// home, or a ruin, which they bring back to life. Either way it is a colony of their own faction; one far from the
/// capital may in time go its own way in peace (Splits.cs). People settle only where no other people stands in their
/// way: land that is their own faction's, no one's, or a faction's with no towns left; never in a rift.
/// </summary>
public static class Migration
{
    /// <summary>How far "near metal" and "away from others" reach, in cells of a 256 map (tastes.METAL_REACH,
    /// OTHERS_REACH).</summary>
    public const double MetalReach = 30;
    public const double OthersReach = 30;

    // The eight neighbours, in the generator's order (water.NEIGHBOURS).
    static readonly (int Dx, int Dy)[] Neighbours = { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    static Dice DiceOf(Game game) => game.DiceFor("migration");

    /// <summary>A distance in cells of a 256 map, as cells of the history's grid (at least 1). settlements.distance_cells.</summary>
    public static int DistanceCells(Game game, double distance) =>
        Math.Max(1, Py.Round(distance * Math.Max(game.Grid.Columns, game.Grid.Rows) / Feeding.ReferenceSize));

    /// <summary>Whether (x, y) lies in a rift, where no one settles. migration.is_rift.</summary>
    public static bool IsRift(Game game, int x, int y) =>
        game.Moving.Rifts is { } rifts && rifts.Contains((x / game.Moving.RiftSize, y / game.Moving.RiftSize));

    /// <summary>1 on a source cell, falling to 0 at `reach` cells (of a 256 map) away, as eight-way steps
    /// (settlements.nearness: a breadth-first search over the eight neighbours is the larger of the two distances).</summary>
    public static double[] Nearness(Game game, IEnumerable<(int X, int Y)> sources, double reach)
    {
        int columns = game.Grid.Columns, rows = game.Grid.Rows;
        int limit = DistanceCells(game, reach);
        var steps = new int[columns * rows];
        Array.Fill(steps, -1);
        var waiting = new Queue<int>();
        foreach (var (x, y) in sources)
        {
            int i = y * columns + x;
            if (steps[i] < 0)
            {
                steps[i] = 0;
                waiting.Enqueue(i);
            }
        }
        while (waiting.Count > 0)
        {
            int i = waiting.Dequeue();
            if (steps[i] >= limit)
                continue;
            int x = i % columns, y = i / columns;
            foreach (var (dx, dy) in Neighbours)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= columns || ny < 0 || ny >= rows || steps[ny * columns + nx] >= 0)
                    continue;
                steps[ny * columns + nx] = steps[i] + 1;
                waiting.Enqueue(ny * columns + nx);
            }
        }
        return steps.Select(s => s < 0 ? 0.0 : Math.Max(0.0, 1.0 - (double)s / limit)).ToArray();
    }

    /// <summary>Whether a people on a path chooses sites by a score of its own: its own weights, or land its path
    /// makes livable. tastes.has_own_score.</summary>
    public static bool HasOwnScore(Game game, int people, string? path) =>
        game.Settings.PeopleSites.ContainsKey(game.PeopleNamesOf[people])
        || (path != null && game.Settings.PathLand.TryGetValue(path, out var land) && land.Count > 0);

    /// <summary>
    /// A people's own site score of every region on a path (0 on the sea and lakes): by its own weights, or the usual
    /// ones, with its own food on its path; worked out the first time it is needed and kept (tastes.score_grid).
    /// </summary>
    public static double[] ScoreGrid(Game game, int people, string? path)
    {
        var moving = game.Moving;
        if (moving.Tastes.TryGetValue((people, path), out var kept))
            return kept;
        var settings = game.Settings;
        int columns = game.Grid.Columns, rows = game.Grid.Rows, count = columns * rows;
        var weights = settings.PeopleSites.TryGetValue(game.PeopleNamesOf[people], out var own) ? own : settings.SiteWeights;
        var parts = new Dictionary<string, double[]>(moving.Parts);
        var food = new Dictionary<string, double>();
        var foodGrid = new double[count];
        for (int c = 0; c < count; c++)
        {
            if (game.LandBiome[c] < 0)
                continue;
            string biome = game.BiomeNames[game.LandBiome[c]];
            if (!food.TryGetValue(biome, out double f))
                food[biome] = f = Feeding.FoodOf(game, people, biome, path);
            foodGrid[c] = f;
        }
        parts["food"] = foodGrid;
        double Weight(string part) => weights.FirstOrDefault(w => w.Part == part).Weight;
        if (Weight("high_ground") != 0)
        {
            double span = 1.0 - moving.SeaLevel;
            parts["high_ground"] = moving.Heights.Select(h => Math.Max(0.0, (h - moving.SeaLevel) / span)).ToArray();
        }
        if (Weight("near_metal") != 0)
            parts["near_metal"] = Nearness(game, game.Mines.Select(m => (m.HX, m.HY)), MetalReach);
        if (Weight("away_from_others") != 0)
        {
            var others = Enumerable.Range(0, game.Towns.Count)
                .Where(t => !game.Towns[t].Ruined && game.Towns[t].Holder is int h && game.Factions[h].Folk != people)
                .Select(t => (game.Towns[t].HX, game.Towns[t].HY));
            parts["away_from_others"] = Nearness(game, others, OthersReach).Select(n => 1.0 - n).ToArray();
        }
        double total = Py.Sum(weights.Select(w => w.Weight));
        var grid = new double[count];
        if (total != 0)
            for (int c = 0; c < count; c++)
                grid[c] = game.Grid.Ocean[c] || game.Lake[c] ? 0.0 : Py.Sum(weights.Select(w => w.Weight * parts[w.Part][c])) / total;
        moving.Tastes[(people, path)] = grid;
        return grid;
    }

    /// <summary>Free land good enough to settle, best first (the lower row, then the left, on a tie): no town farms it,
    /// and none is too close. By the usual site scores, or by `scores`. migration.free_sites.</summary>
    public static List<(double Score, int X, int Y)> FreeSites(Game game, double[]? scores = null)
    {
        int columns = game.Grid.Columns;
        double spacing = DistanceCells(game, game.Settings.MinDistance);
        var grid = scores ?? game.Moving.Scores;
        var sites = new List<(double, int, int)>();
        for (int c = 0; c < grid.Length; c++)
        {
            int x = c % columns, y = c / columns;
            if (grid[c] < game.Settings.MinSiteScore || game.Grid.Owner[c] >= 0)
                continue;
            if (game.Towns.All(t => Hypot(x - t.HX, y - t.HY) >= spacing))
                sites.Add((grid[c], x, y));
        }
        return sites.OrderBy(s => -s.Item1).ThenBy(s => s.Item3).ThenBy(s => s.Item2).ToList();
    }

    /// <summary>The length of (dx, dy), as Python's math.hypot gives it (exact to the last digit).</summary>
    public static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    /// <summary>
    /// The free land a new town at (x, y) farms: every unowned region of land it reaches within `reach` steps, and no
    /// further than `reach` from it as the crow flies, in the order the search finds them. migration.claim.
    /// </summary>
    public static List<int> Claim(Game game, int x, int y, int reach)
    {
        int columns = game.Grid.Columns, rows = game.Grid.Rows;
        var steps = new Dictionary<int, int> { [y * columns + x] = 0 };
        var cells = new List<int> { y * columns + x };
        var waiting = new Queue<int>();
        waiting.Enqueue(y * columns + x);
        while (waiting.Count > 0)
        {
            int here = waiting.Dequeue();
            if (steps[here] >= reach)
                continue;
            int cx = here % columns, cy = here / columns;
            foreach (var (dx, dy) in Neighbours)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || nx >= columns || ny < 0 || ny >= rows)
                    continue;
                int there = ny * columns + nx;
                if (steps.ContainsKey(there) || game.Grid.Owner[there] >= 0 || game.Grid.Ocean[there] || game.Lake[there])
                    continue;
                if ((nx - x) * (nx - x) + (ny - y) * (ny - y) > reach * reach)
                    continue;                       // beyond the circle
                steps[there] = steps[here] + 1;
                cells.Add(there);
                waiting.Enqueue(there);
            }
        }
        return cells;
    }

    /// <summary>
    /// The cheapest journey's cost from town `b` to town `a` over the history's grid (trade.new_route with
    /// grids.cheapest_from): a step onto a region costs that region's cost, 1.414 times as much on a diagonal; ties go
    /// to the way that joined the queue first. Null if `a` cannot be reached.
    /// </summary>
    public static double? RouteCost(Game game, int a, int b) =>
        RouteCost(game, (game.Towns[a].HX, game.Towns[a].HY), (game.Towns[b].HX, game.Towns[b].HY));

    /// <summary>The cheapest journey's cost from region `to` back to region `from` (see the other RouteCost).</summary>
    public static double? RouteCost(Game game, (int X, int Y) from, (int X, int Y) to)
    {
        int columns = game.Grid.Columns, rows = game.Grid.Rows;
        var costs = game.Moving.Costs;
        int goal = from.Y * columns + from.X;
        int start = to.Y * columns + to.X;
        var spent = new double?[columns * rows];
        var queued = new double[columns * rows];
        Array.Fill(queued, double.PositiveInfinity);
        var waiting = new PriorityQueue<int, (double, long)>();
        long order = 0;
        waiting.Enqueue(start, (0.0, order++));
        queued[start] = 0.0;
        while (waiting.TryDequeue(out int i, out var key))
        {
            if (spent[i] != null)
                continue;
            spent[i] = key.Item1;
            if (i == goal)
                break;
            int x = i % columns, y = i / columns;
            foreach (var (dx, dy) in Neighbours)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= columns || ny < 0 || ny >= rows)
                    continue;
                int j = ny * columns + nx;
                if (spent[j] != null)
                    continue;
                double reach = key.Item1 + costs[j] * (dx != 0 && dy != 0 ? 1.414 : 1.0);
                if (reach < queued[j])
                {
                    queued[j] = reach;
                    waiting.Enqueue(j, (reach, order++));
                }
            }
        }
        return spent[goal];
    }

    /// <summary>
    /// One year of migration: crowded and hungry towns send migrants to free land and ruins
    /// (migration.year_of_migration). Fewer migrants than RUIN_POPULATION found or resettle nothing.
    /// </summary>
    public static void Year(Game game, int year)
    {
        var settings = game.Settings;
        var moving = game.Moving;
        var dice = DiceOf(game);
        moving.Sites ??= FreeSites(game);
        double reach = settings.Reach * game.Grid.Columns / 256.0;
        var living = game.Towns.Where(t => !t.Ruined && t.Holder != null).Select(t => t.Holder!.Value).ToHashSet();
        int count = game.Towns.Count;              // the towns at the start: a new one sends no one this year
        // The towns settled this year, and how many settled each: their people, size and years of want are set once
        // every town has had its turn (as the generator does after year_of_migration), so a ruin resettled this
        // year still counts as empty when its own turn comes.
        var settledThisYear = new List<(int Town, int People)>();
        for (int index = 0; index < count; index++)
        {
            var place = game.Towns[index];
            if (place.Ruined || place.Holder is not int holder || game.Factions[holder].Ruler == null)
                continue;
            bool crowded = place.Capacity > 0 && place.People >= settings.Crowded * place.Capacity;
            bool starving = place.Hungry >= settings.HungryYears;
            if (!(crowded || starving) || dice.Random() >= settings.MigrationChance)
                continue;
            double share = settings.MigrantsDrawn ? dice.Uniform(settings.Migrants.Low, settings.Migrants.High) : settings.Migrants.Low;
            int leaving = Py.Round(place.People * share);
            if (leaving < settings.RuinPopulation)
                continue;
            var faction = game.Factions[holder];
            bool Welcome(int? h) => h == null || h == holder || !living.Contains(h.Value);

            double[]? scores = HasOwnScore(game, faction.Folk, faction.Path) ? ScoreGrid(game, faction.Folk, faction.Path) : null;
            List<(double Score, int X, int Y)> sites;
            if (scores == null)
                sites = moving.Sites;
            else if (!moving.OwnSites.TryGetValue((faction.Folk, faction.Path), out sites!))
                moving.OwnSites[(faction.Folk, faction.Path)] = sites = FreeSites(game, scores);

            // The best ruin within reach (the lower number on a tie, as Python's max of (score, -i, i)).
            (double Score, int Index)? ruin = null;
            for (int i = 0; i < game.Towns.Count; i++)
            {
                var p = game.Towns[i];
                if (!p.Ruined || !Welcome(p.Holder) || Hypot(p.HX - place.HX, p.HY - place.HY) > reach || IsRift(game, p.HX, p.HY))
                    continue;
                double score = scores == null ? moving.TownScores[i] : scores[p.HY * game.Grid.Columns + p.HX];
                if (ruin == null || score > ruin.Value.Score || (score == ruin.Value.Score && -i > -ruin.Value.Index))
                    ruin = (score, i);
            }
            (double Score, int X, int Y)? free = null;
            if (game.Towns.Count < settings.MostSettlements)
                foreach (var site in sites)
                    if (Hypot(site.X - place.HX, site.Y - place.HY) <= reach
                        && Welcome(game.Regions[site.Y * game.Grid.Columns + site.X] is int r and >= 0 ? r : null)
                        && !IsRift(game, site.X, site.Y))
                    {
                        free = site;
                        break;
                    }
            if (ruin == null && free == null)
                continue;

            place.People -= leaving;
            string why = crowded ? "crowded" : "hungry";
            string from = game.World.Towns[index].Name;
            int target;
            string text, what;
            if (free is { } site2 && (ruin == null || site2.Score > ruin.Value.Score))
            {
                target = Found(game, site2, index, holder, leaving, year);
                text = string.Format("{0} people leave {1} {2} to found {3}", leaving, why, from, game.World.Towns[target].Name);
                what = "colony_founded";
            }
            else
            {
                target = ruin!.Value.Index;
                Resettle(game, target, index, holder, leaving);
                text = string.Format("{0} people leave {1} {2} to resettle the ruins of {3}", leaving, why, from,
                                     game.World.Towns[target].Name);
                what = "ruin_resettled";
            }
            // The town left behind keeps its size unless it falls past the margin.
            place.Size = YearCore.KeepBand(place.Population, settings.SizeLows, place.Size,
                                           settings.SizeLows[place.Size] * YearCore.SizeMargin);
            settledThisYear.Add((target, leaving));
            living.Add(holder);
            game.Record(new GameEvent(year, "colony", what, text + " (the " + faction.Name + ")", new[] { holder },
                new Dictionary<string, string>
                {
                    ["people"] = leaving.ToString(System.Globalization.CultureInfo.InvariantCulture), ["why"] = why,
                    ["source"] = from, ["target"] = game.World.Towns[target].Name, ["faction"] = faction.Name,
                }));
        }
        foreach (var (target, people) in settledThisYear)
        {
            var settled = game.Towns[target];
            settled.People = people;
            settled.Size = YearCore.BandOf(settled.Population, settings.SizeLows);
            settled.LowYears = settled.Hungry = 0;
        }
    }

    /// <summary>
    /// A new town of `faction` on free land, farming the free land around it, with a route to the town its migrants
    /// came from; returns its number. migration.found.
    /// </summary>
    public static int Found(Game game, (double Score, int X, int Y) site, int source, int faction, int leaving, int year)
    {
        var settings = game.Settings;
        var moving = game.Moving;
        int columns = game.Grid.Columns, rows = game.Grid.Rows;
        int index = game.Towns.Count;
        int spacing = DistanceCells(game, settings.MinDistance);
        var cells = Claim(game, site.X, site.Y, spacing);
        var holder = game.Factions[faction];
        double food = settings.FeedsItsOwn
            ? Feeding.CapacityOf(game, cells, holder.Folk, holder.Path).Food
            : Py.Sum(cells.Select(c => moving.Parts["food"][c])) * Feeding.ReferenceSize * Feeding.ReferenceSize / (rows * columns);
        string name = game.NextSettlementName(holder.Folk);
        int factor = game.World.HistoryFactor;
        // On the map, the middle of its region (the generator places a history's town on the best cell of its region,
        // which needs the map's own site scores: the middle is close enough to draw it).
        int mapX = Math.Min(game.World.Size - 1, (site.X * factor + Math.Min(game.World.Size, (site.X + 1) * factor)) / 2);
        int mapY = Math.Min(game.World.Size - 1, (site.Y * factor + Math.Min(game.World.Size, (site.Y + 1) * factor)) / 2);
        foreach (int c in cells)
            game.Grid.Owner[c] = index;
        var town = new TownNow
        {
            People = leaving, Holder = faction, Origin = faction, FoundedFrom = source, X = mapX, Y = mapY,
            HX = site.X, HY = site.Y, Food = food, Capacity = Py.Round(food * settings.PeoplePerFood),
            Yields = Feeding.YieldsOf(game, cells, null, null), FedAs = null,
            Cells = cells.OrderBy(c => c).ToList(),
            Near = moving.Near.Where(n => n.Value[site.Y * columns + site.X]).Select(n => n.Key).ToHashSet(),
        };
        game.AddTown(town, new TownRecord(year, name, mapX, mapY, new List<int>(), new List<(int, int?)>(),
                                          new List<(int, int?)>()), site.Score);
        foreach (var mine in game.Mines)
            if (game.Grid.Owner[mine.HY * columns + mine.HX] == index)
                mine.LandTown = index;
        Splits.HandOver(game, new[] { index }, faction);
        List<(double, int, int)> StillFree(List<(double Score, int X, int Y)> listed) =>
            listed.Where(s => game.Grid.Owner[s.Y * columns + s.X] < 0 && Hypot(s.X - site.X, s.Y - site.Y) >= spacing).ToList();
        moving.Sites = StillFree(moving.Sites!);
        foreach (var key in moving.OwnSites.Keys.ToList())
            moving.OwnSites[key] = StillFree(moving.OwnSites[key]);
        if (RouteCost(game, source, index) is double cost)
        {
            game.Routes.Add(new Route { A = source, B = index, Cost = cost });
            game.RoutesChanged();
        }
        return index;
    }

    /// <summary>Migrants of `faction` from town `source` bring the ruin `target` back to life. migration.resettle.</summary>
    public static void Resettle(Game game, int target, int source, int faction, int leaving)
    {
        var place = game.Towns[target];
        place.Ruined = false;                       // its people are counted once the year's migrations are done
        place.Holder = place.Origin = faction;
        place.ConqueredIn = null;
        place.FoundedFrom = source;
        Splits.HandOver(game, new[] { target }, faction);
    }
}
