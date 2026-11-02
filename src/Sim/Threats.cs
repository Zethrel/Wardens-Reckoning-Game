// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;

namespace WardensReckoning.Sim;

/// <summary>One named threat (threats.Threat): its name and kind, its lair on the history's grid, its strength, the
/// year it rose, the year it wakes while it sleeps, the year and way it ended ("slain", "faded" or "ritual"), the
/// raids it won and lost, the people it has killed (all, and by faction), the factions that worked a great ritual
/// against it, and every lair it has kept as (year, x, y).</summary>
public sealed class Threat
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public double Strength { get; set; }
    public int Risen { get; set; }
    public int? AsleepUntil { get; set; }
    public int? Ended { get; set; }
    public string? End { get; set; }
    public int Raids { get; set; }
    public int Beaten { get; set; }
    public double Dead { get; set; }
    public Dictionary<int, double> Harm { get; set; } = new();
    public HashSet<int> Rituals { get; set; } = new();
    public List<(int Year, int X, int Y)> Lairs { get; set; } = new();
}

/// <summary>
/// Named threats (the generator's north/threats.py; your kinds are in threat_settings.py), played on in the game's
/// years (step 9b). Each year, after attunement and before the towns grow, every threat in the order they rose:
///
/// - asleep: it wakes when its years of sleep are over;
/// - awake: it may fade for good ("fade"), fall asleep ("sleep", for "sleep_years"), move its lair ("move"), and raid
///   ("raid") a town within its reach, chosen at random.
///
/// A raid is a fight, like a battle: the town's people with reinforcements from its faction (War.Strength, with the
/// defender's advantage) against the threat's strength; the town wins with a chance of its strength over both. The
/// threat that wins kills "deaths" of the town's people and spoils its next harvest ("harvest"); the town that wins
/// loses WINNER_LOSSES as many, and the threat is slain with a chance of "slain", or beaten off. The dead lie where
/// they fell, for the god of death (step 9a). Then a new threat rises with RISE_CHANCE.
///
/// A lair is a cell of land of the kind's biomes that no town holds, with a town within its reach: up to LAIR_TRIES
/// such cells are tried at random. The undead try the land where magic's mark is strong first.
///
/// Threats roll their own dice ("threats"), as in the generator: adding a kind changes no other system's rolls.
/// </summary>
public static class Threats
{
    /// <summary>threats.py LAIR_TRIES: how many random cells to try for a lair.</summary>
    public const int LairTries = 200;

    /// <summary>A threat's reach on the history's grid: its kind's, scaled from a 256 map (threats.reach).</summary>
    public static double Reach(Game game, string kind) => game.Settings.KindOf(kind).Reach * game.Grid.Columns / 256.0;

    /// <summary>This year's harvest factor for town `town` after a raid the year before, 1 if none (threats.factor).</summary>
    public static double Factor(Game game, int town, int year) =>
        game.Spoiled.TryGetValue(town, out var spoiled) && spoiled.Year == year ? spoiled.Factor : 1.0;

    /// <summary>The living threats awake, in the order they rose (threats.awake).</summary>
    public static List<Threat> Awake(Game game) =>
        game.Threats.Where(t => t.Ended == null && t.AsleepUntil == null).ToList();

    /// <summary>
    /// One year of every threat (threats.Threats.year), with `random` for its dice (the game passes
    /// `game.DiceFor("threats").Random`; the tests a fixed list of numbers). `supply` is what each faction's towns
    /// are short of this year (Opinions.Supplies), for how well armed they fight.
    /// </summary>
    public static void Year(Game game, Func<double> random, List<Dictionary<string, double[]>> supply, int year)
    {
        if (game.Settings.ThreatKinds.Count == 0)
            return;
        foreach (var threat in game.Threats.ToList())
        {
            if (threat.Ended != null)
                continue;
            var about = game.Settings.KindOf(threat.Kind);
            if (threat.AsleepUntil is int waking)
            {
                if (year < waking)
                    continue;
                threat.AsleepUntil = null;
                Tell(game, year, "threat_wakes", threat.Name + " wakes", null, new() { ["threat"] = threat.Name });
            }
            if (random() < about.Fade)
            {
                (threat.Ended, threat.End) = (year, "faded");
                Tell(game, year, "threat_fades", threat.Name + " is seen no more", null, new() { ["threat"] = threat.Name });
                continue;
            }
            if (random() < about.Sleep)
            {
                // random.randint(low, high): a whole number from low to high, both included.
                int years = about.SleepYears.Low + (int)(random() * (about.SleepYears.High - about.SleepYears.Low + 1));
                threat.AsleepUntil = year + years;
                Tell(game, year, "threat_sleeps", threat.Name + " sleeps", null,
                     new() { ["threat"] = threat.Name, ["years"] = years.ToString() });
                continue;
            }
            if (random() < about.Move && FindLair(game, about, random) is { } lair)
            {
                (threat.X, threat.Y) = lair;
                threat.Lairs.Add((year, lair.X, lair.Y));
                int? near = Nearest(game, lair.X, lair.Y);
                string place = near is int n ? game.World.Towns[n].Name : "";
                Tell(game, year, "threat_moves", threat.Name + " makes a new lair near " + place,
                     near is int h ? game.Towns[h].Holder : null, new() { ["threat"] = threat.Name, ["place"] = place });
            }
            if (random() < about.Raid)
            {
                var targets = InReach(game, threat.X, threat.Y, Reach(game, threat.Kind));
                if (targets.Count > 0)
                    Raid(game, threat, targets[(int)(random() * targets.Count)], random, supply, year);
            }
        }
        if (random() < game.Settings.RiseChance)
            Rise(game, random, year);
    }

    /// <summary>`threat` raids town `town`: a fight (threats.Threats.raid).</summary>
    static void Raid(Game game, Threat threat, int town, Func<double> random, List<Dictionary<string, double[]>> supply, int year)
    {
        var about = game.Settings.KindOf(threat.Kind);
        var place = game.Towns[town];
        int? holder = place.Holder;
        double defence = Defence(game, town, supply);
        // As Python's `and`: no dice are rolled when neither side has any strength.
        bool won = defence + threat.Strength > 0 && random() < threat.Strength / (defence + threat.Strength);
        double share = Drawn(about.Deaths, random) * (won ? 1.0 : War.WinnerLosses);
        double dead = place.People * share;
        place.People -= dead;
        threat.Dead += dead;
        if (holder is int harmed)
            threat.Harm[harmed] = threat.Harm.GetValueOrDefault(harmed) + dead;
        int died = Py.Round(dead);
        game.Deaths.Add((year, place.HX, place.HY, died));      // the dead lie where they fell, as in a battle
        string name = game.World.Towns[town].Name;
        var facts = new Dictionary<string, string>
        {
            ["threat"] = threat.Name, ["place"] = name, ["faction"] = holder is int g ? game.Factions[g].Name : "",
            ["dead"] = died.ToString(),
        };
        if (won)
        {
            threat.Raids++;
            if (about.Harvest != 1.0)
                game.Spoiled[town] = (year + 1, about.Harvest);
            Tell(game, year, "threat_raids", threat.Name + " raids " + name + " (" + died + " dead)", holder, facts);
            return;
        }
        threat.Beaten++;
        if (random() < about.Slain)
        {
            (threat.Ended, threat.End) = (year, "slain");
            Tell(game, year, "threat_slain", threat.Name + " is slain at " + name + " (" + died + " dead)", holder, facts);
        }
        else
            Tell(game, year, "threat_beaten_off", threat.Name + " is beaten off at " + name + " (" + died + " dead)", holder, facts);
    }

    /// <summary>How strongly town `town` fights off a raid: its people with reinforcements from its faction
    /// (War.Strength), or its people alone if no faction holds it, DEFENDER_ADVANTAGE times stronger behind its walls.</summary>
    public static double Defence(Game game, int town, List<Dictionary<string, double[]>> supply)
    {
        var place = game.Towns[town];
        return place.Holder is int f
            ? War.Strength(game, f, supply, place.People, War.Held(game, f), game.Settings.SizeInBattle) * War.DefenderAdvantage
            : place.People * War.DefenderAdvantage;
    }

    /// <summary>A new threat of a kind drawn by weight, if a lair can be found for it (threats.Threats.rise).</summary>
    public static Threat? Rise(Game game, Func<double> random, int year)
    {
        var about = DrawKind(game.Settings.ThreatKinds, random);
        if (FindLair(game, about, random) is not { } lair)
            return null;
        int? near = Nearest(game, lair.X, lair.Y);
        var threat = new Threat
        {
            Name = NameFor(game, about, near), Kind = about.Name, X = lair.X, Y = lair.Y,
            Strength = Drawn(about.Strength, random), Risen = year,
        };
        threat.Lairs.Add((year, lair.X, lair.Y));
        game.Threats.Add(threat);
        string place = near is int n ? game.World.Towns[n].Name : "";
        string kind = about.Name.Replace("_", " ");
        Tell(game, year, "threat_rises", threat.Name + ", a " + kind + ", makes its lair near " + place,
             near is int h ? game.Towns[h].Holder : null,
             new() { ["threat"] = threat.Name, ["kind"] = about.Name, ["place"] = place });
        return threat;
    }

    /// <summary>A kind drawn by the kinds' weights, as Python's random.choices: the running total of the weights, and
    /// the first kind whose total is above a draw times the whole (bisect_right).</summary>
    public static GameSettings.ThreatKind DrawKind(IReadOnlyList<GameSettings.ThreatKind> kinds, Func<double> random)
    {
        var totals = new double[kinds.Count];
        double total = 0.0;
        for (int i = 0; i < kinds.Count; i++)
            totals[i] = total += kinds[i].Weight;
        double draw = random() * total;
        int at = 0;
        while (at < kinds.Count - 1 && totals[at] <= draw)
            at++;
        return kinds[at];
    }

    /// <summary>
    /// A threat's name (threats.Threats.name_for): the next of its kind's names that the world does not use yet; when
    /// they run out, the next of the export's names for the people of the faction holding the town nearest its lair;
    /// without them, "the &lt;kind&gt; &lt;number&gt;". The generator skips a listed name its namer has given anything;
    /// the game skips one a town, a faction or a threat has.
    /// </summary>
    static string NameFor(Game game, GameSettings.ThreatKind about, int? near)
    {
        while (game.ThreatNamesGiven.GetValueOrDefault(about.Name) < about.Names.Count)
        {
            string listed = about.Names[game.ThreatNamesGiven.GetValueOrDefault(about.Name)];
            game.ThreatNamesGiven[about.Name] = game.ThreatNamesGiven.GetValueOrDefault(about.Name) + 1;
            bool used = game.Threats.Any(t => t.Name == listed) || game.Factions.Any(f => f.Name == listed)
                        || game.World.Towns.Any(t => t.Name == listed);
            if (!used)
                return listed;
        }
        if (near is int n && game.Towns[n].Holder is int holder && game.NextThreatName(game.Factions[holder].Folk) is { } made)
            return made;
        return "the " + about.Name.Replace("_", " ") + " " + (game.Threats.Count + 1);
    }

    /// <summary>
    /// A cell for a lair of a kind, or null (threats.Threats.find_lair): every cell of land of its biomes that no town
    /// holds and no town stands on may be one; up to LAIR_TRIES of them are drawn, and the first with a town within
    /// reach is taken. The undead draw from the cells whose patch of magic's mark is strong first.
    /// </summary>
    public static (int X, int Y)? FindLair(Game game, GameSettings.ThreatKind about, Func<double> random)
    {
        var grid = game.Grid;
        var wanted = about.Biomes.ToHashSet();
        var taken = game.Towns.Select(t => (t.HX, t.HY)).ToHashSet();
        var cells = new List<(int X, int Y)>();
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Columns; x++)
            {
                int cell = y * grid.Columns + x;
                int biome = game.LandBiome[cell];
                if (biome < 0 || (wanted.Count > 0 && !wanted.Contains(game.BiomeNames[biome])))
                    continue;
                if (grid.Owner[cell] >= 0 || taken.Contains((x, y)))
                    continue;
                cells.Add((x, y));
            }
        double reach = about.Reach * grid.Columns / 256.0;
        var marks = game.Marks;
        if (about.Undead && marks != null)
        {
            var torn = cells.Where(c => marks.Mark.GetValueOrDefault((c.X / marks.Size, c.Y / marks.Size), 0.0) >= game.Settings.Strong)
                            .ToList();
            foreach (var (x, y) in Sample(torn, Math.Min(LairTries, torn.Count), random))
                if (InReach(game, x, y, reach).Count > 0)
                    return (x, y);
        }
        foreach (var (x, y) in Sample(cells, Math.Min(LairTries, cells.Count), random))
            if (InReach(game, x, y, reach).Count > 0)
                return (x, y);
        return null;
    }

    /// <summary>
    /// `count` different items of `items` in a random order (as Python's random.sample does for a short list): each
    /// in turn is drawn from those left, and the last of those left takes its place. All are drawn before any is
    /// used, as the generator does, so the dice used do not depend on which one is taken.
    /// </summary>
    public static List<T> Sample<T>(IReadOnlyList<T> items, int count, Func<double> random)
    {
        var pool = items.ToList();
        var chosen = new List<T>(count);
        int n = pool.Count;
        for (int i = 0; i < count; i++)
        {
            int j = (int)(random() * (n - i));
            chosen.Add(pool[j]);
            pool[j] = pool[n - i - 1];
        }
        return chosen;
    }

    /// <summary>The towns within `reach` of (x, y) on the history's grid that are not ruins and have people
    /// (threats.in_reach).</summary>
    public static List<int> InReach(Game game, int x, int y, double reach)
    {
        var found = new List<int>();
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (!town.Ruined && town.Population > 0 && Migration.Hypot(town.HX - x, town.HY - y) <= reach)
                found.Add(i);
        }
        return found;
    }

    /// <summary>The town nearest (x, y) that is not a ruin (the first by number on a tie), or null (threats.nearest).</summary>
    public static int? Nearest(Game game, int x, int y)
    {
        int? best = null;
        double distance = double.PositiveInfinity;
        for (int i = 0; i < game.Towns.Count; i++)
        {
            if (game.Towns[i].Ruined)
                continue;
            double d = Migration.Hypot(game.Towns[i].HX - x, game.Towns[i].HY - y);
            if (d < distance)
                (best, distance) = (i, d);
        }
        return best;
    }

    /// <summary>A setting that is a number, or a range drawn with `random` (war.drawn): a range is always drawn.</summary>
    static double Drawn((double Low, double High, bool IsRange) setting, Func<double> random) =>
        setting.IsRange ? setting.Low + (setting.High - setting.Low) * random() : setting.Low;

    static void Tell(Game game, int year, string what, string text, int? faction, Dictionary<string, string> facts) =>
        game.Record(new GameEvent(year, "threat", what, text, faction is int f ? new[] { f } : Array.Empty<int>(), facts));

    /// <summary>The threats as a save or an export holds them.</summary>
    public static JsonArray Write(IEnumerable<Threat> threats) => new(threats.Select(t => (JsonNode)new JsonObject
    {
        ["name"] = t.Name, ["kind"] = t.Kind, ["x"] = t.X, ["y"] = t.Y, ["strength"] = t.Strength, ["risen"] = t.Risen,
        ["asleep_until"] = t.AsleepUntil, ["ended"] = t.Ended, ["end"] = t.End, ["raids"] = t.Raids,
        ["beaten"] = t.Beaten, ["dead"] = t.Dead,
        ["harm"] = new JsonArray(t.Harm.OrderBy(p => p.Key).Select(p => (JsonNode)new JsonArray(p.Key, p.Value)).ToArray()),
        ["rituals"] = new JsonArray(t.Rituals.Order().Select(f => (JsonNode)f).ToArray()),
        ["lairs"] = new JsonArray(t.Lairs.Select(l => (JsonNode)new JsonArray(l.Year, l.X, l.Y)).ToArray()),
    }).ToArray());

    /// <summary>The threats a save or an export holds (none, if it holds none).</summary>
    public static List<Threat> Read(JsonNode? node) => node is JsonArray list
        ? list.Select(n => new Threat
        {
            Name = (string)n!["name"]!, Kind = (string)n["kind"]!, X = (int)n["x"]!, Y = (int)n["y"]!,
            Strength = (double)n["strength"]!, Risen = (int)n["risen"]!, AsleepUntil = (int?)n["asleep_until"],
            Ended = (int?)n["ended"], End = (string?)n["end"], Raids = (int)n["raids"]!, Beaten = (int)n["beaten"]!,
            Dead = (double)n["dead"]!,
            Harm = n["harm"]!.AsArray().ToDictionary(h => (int)h![0]!, h => (double)h![1]!),
            Rituals = n["rituals"]!.AsArray().Select(f => (int)f!).ToHashSet(),
            Lairs = n["lairs"]!.AsArray().Select(l => ((int)l![0]!, (int)l[1]!, (int)l[2]!)).ToList(),
        }).ToList()
        : new List<Threat>();
}
