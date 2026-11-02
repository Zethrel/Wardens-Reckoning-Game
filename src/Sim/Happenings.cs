// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;

namespace WardensReckoning.Sim;

/// <summary>A happening's lasting effects in one town: from its first year to its last, each factor (growth, harvest,
/// trade, war) multiplying that part of the town's year.</summary>
public sealed record HappeningLasting(string Name, int First, int Last, IReadOnlyList<(string Key, double Factor)> Effects);

/// <summary>
/// Happenings, tempers and eras (the generator's north/happenings.py; yours are in happening_settings.py), played on
/// in the game's years (step 9c). Each year, after attunement and before the threats:
///
/// 1. **Spreading.** A happening that reaches "routes" and struck a town last year may move on along each route from
///    it, to a town its outbreak has not struck yet, with its "spread" chance. It dies out when it strikes nowhere new.
/// 2. **Starting.** A "world" happening rolls once for the whole world and strikes every lived-in town. Every other
///    happening rolls in every lived-in town it has not struck this year, with its "chance" times the era's temper:
///    "town" strikes there, "region" every town within "radius", "routes" there and then spreads.
/// 3. **Striking**, at once: "population" gained or lost, "driven_off" to the towns on its routes, a "belief" its
///    faction may come to, "blame" of the faction it started in; and for the "years" after, "growth", "harvest",
///    "trade" and "war" multiplied by their factors.
///
/// **Eras.** With two tempers or more, an era begins when the last is over: its temper drawn by weight, its length
/// from ERA_YEARS. One of ERA_ENDERS may end it early, with ERA_END_CHANCE. A temper multiplies every happening's
/// chance, every faction's readiness for war and its chance of breaking apart.
///
/// Happenings roll their own dice ("happenings"), and the eras theirs ("eras"), as in the generator.
/// </summary>
public static class Happenings
{
    /// <summary>The era's temper: the last era's, or the only temper (with one, there are no eras).</summary>
    public static GameSettings.Temper TemperNow(Game game) =>
        game.Eras.Count > 0 ? game.Settings.TemperOf(game.Eras[^1].Temper) : game.Settings.Tempers[0];

    /// <summary>How much `key` ("growth", "harvest", "trade" or "war") is multiplied by in `town` this year
    /// (happenings.factor): every lasting effect of that key whose years include this one, multiplied together.</summary>
    public static double Factor(Game game, int town, int year, string key)
    {
        double factor = 1.0;
        if (game.HappeningsLasting.TryGetValue(town, out var entries))
            foreach (var entry in entries)
                if (entry.First <= year && year <= entry.Last)
                    foreach (var (k, value) in entry.Effects)
                        if (k == key)
                            factor *= value;
        return factor;
    }

    /// <summary>
    /// {faction: its readiness for war} (happenings.war_factors): its people's "war" factors, weighed by each town's
    /// people, times the era's temper; only for factions where that is not 1.
    /// </summary>
    public static Dictionary<int, double> WarFactors(Game game, int year)
    {
        var people = new Dictionary<int, double>();
        var weighed = new Dictionary<int, double>();
        var order = new List<int>();
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int f)
                continue;
            if (!people.ContainsKey(f))
                order.Add(f);
            people[f] = people.GetValueOrDefault(f) + town.Population;
            weighed[f] = weighed.GetValueOrDefault(f) + town.Population * Factor(game, i, year, "war");
        }
        double war = TemperNow(game).War;
        var found = new Dictionary<int, double>();
        foreach (int f in order)
            if (people[f] > 0 && (weighed[f] != people[f] || war != 1.0))
                found[f] = weighed[f] / people[f] * war;
        return found;
    }

    /// <summary>Begin a new era if the last one is over, with two tempers or more (happenings.begin_year).</summary>
    public static void BeginYear(Game game, Func<double> random, int year)
    {
        var tempers = game.Settings.Tempers;
        if (tempers.Count < 2 || (game.Eras.Count > 0 && year <= game.Eras[^1].Last))
            return;
        // random.choices by weight (as Threats.DrawKind), then random.randint(low, high).
        var totals = new double[tempers.Count];
        double total = 0.0;
        for (int i = 0; i < tempers.Count; i++)
            totals[i] = total += tempers[i].Weight;
        double draw = random() * total;
        int at = 0;
        while (at < tempers.Count - 1 && totals[at] <= draw)
            at++;
        var temper = tempers[at];
        var (low, high) = game.Settings.EraYears;
        int length = low + (int)(random() * (high - low + 1));
        game.Eras.Add((temper.Name, year, year + length - 1));
        game.Record(new GameEvent(year, "era", "era_begins", "a " + temper.Name.Replace("_", " ") + " era begins",
            Array.Empty<int>(), new Dictionary<string, string> { ["temper"] = temper.Name, ["years"] = length.ToString() }));
    }

    /// <summary>One of ERA_ENDERS this year may end the era early, with ERA_END_CHANCE (happenings.end_year).
    /// `thisYear` is this year's events.</summary>
    public static void EndYear(Game game, Func<double> random, int year, IEnumerable<GameEvent> thisYear)
    {
        var settings = game.Settings;
        if (game.Eras.Count == 0 || settings.EraEndChance <= 0 || game.Eras[^1].Last <= year)
            return;
        if (thisYear.Any(e => settings.EraEnders.Contains(e.What)) && random() < settings.EraEndChance)
            game.Eras[^1] = (game.Eras[^1].Temper, game.Eras[^1].First, year);
    }

    /// <summary>This year's chance that a happening starts: its own, times the era's temper (and its mood).</summary>
    public static double ChanceOf(Game game, GameSettings.Happening about)
    {
        var temper = TemperNow(game);
        return about.Chance * temper.Chance * temper.Moods.GetValueOrDefault(about.Name, 1.0);
    }

    static bool LivedIn(TownNow town) => !town.Ruined && town.Holder != null;

    /// <summary>
    /// Spread, start and strike this year's happenings (happenings.Happenings.year), with `random` for their dice
    /// (the game passes `game.DiceFor("happenings").Random`). Returns {town: people they killed}: they count as
    /// the town's people before its year, for a mass death. `blame` is the year's, as for your rules.
    /// </summary>
    public static Dictionary<int, double> Year(Game game, Func<double> random, int year, Dictionary<(int, int), double> blame)
    {
        var killed = new Dictionary<int, double>();
        var table = game.Settings.Happenings;
        if (table.Count == 0)
            return killed;
        var struck = new HashSet<(int, string)>();
        foreach (var about in table)
        {
            var spreading = game.Frontier.GetValueOrDefault(about.Name) ?? new List<(int, int, int)>();
            var next = new List<(int Town, int Source, int Outbreak)>();
            game.Frontier[about.Name] = next;
            foreach (var (town, source, outbreak) in spreading)
                foreach (int other in Neighbours(game, town))
                    if (!struck.Contains((other, about.Name)) && !game.Outbreaks[outbreak].Contains(other)
                        && random() < about.Spread)
                    {
                        Strike(game, about, other, source, random, year, blame, killed, "spreads to");
                        struck.Add((other, about.Name));
                        game.Outbreaks[outbreak].Add(other);
                        next.Add((other, source, outbreak));
                    }
            var alive = next.Select(n => n.Outbreak).ToHashSet();
            foreach (var (_, _, outbreak) in spreading)
                if (!alive.Contains(outbreak))
                    game.Outbreaks.Remove(outbreak);
        }
        foreach (var about in table)
        {
            if (about.Reach != "world" || about.Chance <= 0 || random() >= ChanceOf(game, about))
                continue;
            // A world-wide happening rolls once a year for the whole world, and strikes every lived-in town.
            for (int town = 0; town < game.Towns.Count; town++)
                if (LivedIn(game.Towns[town]) && !struck.Contains((town, about.Name)))
                {
                    Strike(game, about, town, null, random, year, blame, killed, "strikes");
                    struck.Add((town, about.Name));
                }
        }
        for (int town = 0; town < game.Towns.Count; town++)
        {
            if (!LivedIn(game.Towns[town]))
                continue;
            foreach (var about in table)
            {
                if (struck.Contains((town, about.Name)) || about.Reach == "world")
                    continue;
                if (about.Chance <= 0 || random() >= ChanceOf(game, about))
                    continue;
                // A "when" rule would be asked here; the game has none (Python code is not exported).
                int source = game.Towns[town].Holder!.Value;
                foreach (int other in Reached(game, about, town))
                {
                    if (struck.Contains((other, about.Name)))
                        continue;
                    Strike(game, about, other, source, random, year, blame, killed, other == town ? "at" : "reaches");
                    struck.Add((other, about.Name));
                }
                if (about.Reach == "routes")
                {
                    game.Outbreaks[game.NextOutbreak] = new HashSet<int> { town };
                    game.Frontier[about.Name].Add((town, source, game.NextOutbreak));
                    game.NextOutbreak++;
                }
            }
        }
        return killed;
    }

    /// <summary>The towns a happening starting in `town` strikes at once (happenings.reached): a region's every
    /// lived-in town within its radius (on the history's grid), else the town alone.</summary>
    static List<int> Reached(Game game, GameSettings.Happening about, int town)
    {
        var found = new List<int> { town };
        if (about.Reach != "region")
            return found;
        double reach = about.Radius * game.Grid.Columns / 256.0;
        var here = game.Towns[town];
        for (int other = 0; other < game.Towns.Count; other++)
        {
            var place = game.Towns[other];
            if (other == town || !LivedIn(place))
                continue;
            int dx = place.HX - here.HX, dy = place.HY - here.HY;
            if (Math.Pow(dx * dx + dy * dy, 0.5) <= reach)
                found.Add(other);
        }
        return found;
    }

    /// <summary>The lived-in towns on a route from `town`, in the routes' order (happenings.neighbours).</summary>
    public static List<int> Neighbours(Game game, int town)
    {
        var found = new List<int>();
        foreach (var route in game.Routes)
        {
            if (route.A != town && route.B != town)
                continue;
            int other = route.A == town ? route.B : route.A;
            if (LivedIn(game.Towns[other]) && !found.Contains(other))
                found.Add(other);
        }
        return found;
    }

    /// <summary>A happening strikes `town` (happenings.Happenings.strike): what happens at once, and what lasts.</summary>
    static void Strike(Game game, GameSettings.Happening about, int town, int? source, Func<double> random, int year,
                       Dictionary<(int, int), double> blame, Dictionary<int, double> killed, string how)
    {
        var place = game.Towns[town];
        int holder = place.Holder!.Value;
        var faction = game.Factions[holder];
        string name = game.World.Towns[town].Name;
        var details = new List<string>();
        if (about.Population != 0)
        {
            double before = place.People;
            place.People = Math.Max(0.0, place.People * (1 + about.Population));
            double change = place.People - before;
            if (change < 0)
                killed[town] = killed.GetValueOrDefault(town) - change;
            // Python's "%+d": the sign always, then the whole number.
            int whole = Py.Round(change);
            details.Add((whole >= 0 ? "+" : "") + whole + " people");
        }
        if (about.DrivenOff != 0)
        {
            double leaving = place.People * about.DrivenOff;
            place.People -= leaving;
            var targets = Neighbours(game, town);
            foreach (int target in targets)
                game.Towns[target].People += leaving / targets.Count;
            details.Add(Py.Round(leaving) + " driven off" + (targets.Count > 0
                ? " to " + string.Join(", ", targets.Select(t => game.World.Towns[t].Name)) : ", lost"));
        }
        if (about.Belief is var (belief, chance))
            if (faction.Belief != belief && random() < Death.FaithShift(faction, chance))
            {
                faction.Belief = belief;
                game.Record(new GameEvent(year, "belief", "belief_after_happening",
                    "after the " + about.Name.Replace("_", " ") + ", the " + faction.Name + " come to " + belief
                    + " the god of death", new[] { holder },
                    new Dictionary<string, string> { ["faction"] = faction.Name, ["belief"] = belief, ["happening"] = about.Name }));
            }
        if (about.Blame != 0 && source is int from && from != holder)
        {
            // history.add_blame: a faction blames as strongly as its angriest town.
            blame[(holder, from)] = Math.Max(blame.GetValueOrDefault((holder, from)), about.Blame);
            details.Add("blames the " + game.Factions[from].Name);
        }
        if (about.Lasting.Count > 0 && about.Years > 0)
        {
            if (!game.HappeningsLasting.TryGetValue(town, out var lasting))
                game.HappeningsLasting[town] = lasting = new List<HappeningLasting>();
            lasting.Add(new HappeningLasting(about.Name, year + 1, year + about.Years, about.Lasting));
        }
        if (!game.HappeningsLast.TryGetValue(town, out var last))
            game.HappeningsLast[town] = last = new Dictionary<string, int>();
        last[about.Name] = year;
        string text = about.Name.Replace("_", " ") + " " + how + " " + name;
        game.Record(new GameEvent(year, "happening", about.Name,
            text + (details.Count > 0 ? " (" + string.Join("; ", details) + ")" : ""), new[] { holder },
            new Dictionary<string, string>
            {
                ["place"] = name, ["faction"] = faction.Name, ["dead"] = Py.Round(killed.GetValueOrDefault(town)).ToString(),
            }));
    }

    /// <summary>Multiply what town `index` makes and can craft by its "trade" factor (history.scale_trade); crafting
    /// that is not done needs no metal.</summary>
    public static void ScaleTrade(Market market, double factor)
    {
        if (factor == 1.0)
            return;
        foreach (var good in market.Made.Keys.ToList())
            market.Made[good] *= factor;
        foreach (var good in market.CanCraft.Keys.ToList())
        {
            market.CanCraft[good] *= factor;
            double before = market.CraftingMetal.GetValueOrDefault(good);
            market.CraftingMetal[good] = before * factor;
            market.Needed[Trade.Metal] = market.Needed.GetValueOrDefault(Trade.Metal) - (before - market.CraftingMetal[good]);
        }
    }

    /// <summary>What the happenings carry from year to year, as an export's "continue"."happenings" or a save holds it.</summary>
    public static JsonObject Write(Game game) => new()
    {
        ["lasting"] = new JsonObject(game.HappeningsLasting.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(p.Key.ToString(),
            (JsonNode?)new JsonArray(p.Value.Select(e => (JsonNode)new JsonArray(e.Name, e.First, e.Last,
                new JsonObject(e.Effects.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Factor))))).ToArray())))),
        ["last"] = new JsonObject(game.HappeningsLast.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(p.Key.ToString(),
            (JsonNode?)new JsonObject(p.Value.Select(l => KeyValuePair.Create(l.Key, (JsonNode?)l.Value)))))),
        ["frontier"] = new JsonObject(game.Frontier.Where(p => p.Value.Count > 0).Select(p => KeyValuePair.Create(p.Key,
            (JsonNode?)new JsonArray(p.Value.Select(f => (JsonNode)new JsonArray(f.Town, f.Source, f.Outbreak)).ToArray())))),
        ["outbreaks"] = new JsonObject(game.Outbreaks.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(p.Key.ToString(),
            (JsonNode?)new JsonArray(p.Value.Order().Select(t => (JsonNode)t).ToArray())))),
        ["next_outbreak"] = game.NextOutbreak,
        ["eras"] = new JsonArray(game.Eras.Select(e => (JsonNode)new JsonArray(e.Temper, e.First, e.Last)).ToArray()),
    };

    /// <summary>Puts back what `node` holds (nothing, if it is missing), replacing what the game held.</summary>
    public static void Read(Game game, JsonNode? node)
    {
        game.HappeningsLasting.Clear();
        game.HappeningsLast.Clear();
        game.Frontier.Clear();
        game.Outbreaks.Clear();
        game.Eras.Clear();
        game.NextOutbreak = 0;
        if (node is not JsonObject kept)
            return;
        foreach (var (town, entries) in kept["lasting"] as JsonObject ?? new JsonObject())
            game.HappeningsLasting[int.Parse(town)] = entries!.AsArray().Select(e => new HappeningLasting((string)e![0]!,
                (int)e[1]!, (int)e[2]!, e[3]!.AsObject().Select(f => (f.Key, (double)f.Value!)).ToList())).ToList();
        foreach (var (town, struck) in kept["last"] as JsonObject ?? new JsonObject())
            game.HappeningsLast[int.Parse(town)] = struck!.AsObject().ToDictionary(l => l.Key, l => (int)l.Value!);
        foreach (var (name, entries) in kept["frontier"] as JsonObject ?? new JsonObject())
            game.Frontier[name] = entries!.AsArray().Select(f => ((int)f![0]!, (int)f[1]!, (int)f[2]!)).ToList();
        foreach (var (outbreak, towns) in kept["outbreaks"] as JsonObject ?? new JsonObject())
            game.Outbreaks[int.Parse(outbreak)] = towns!.AsArray().Select(t => (int)t!).ToHashSet();
        game.NextOutbreak = (int?)kept["next_outbreak"] ?? 0;
        foreach (var era in kept["eras"] as JsonArray ?? new JsonArray())
            game.Eras.Add(((string)era![0]!, (int)era[1]!, (int)era[2]!));
    }
}
