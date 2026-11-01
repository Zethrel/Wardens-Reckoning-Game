// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// What each faction thinks of each other, -1 to 1 (step 4): the generator's factions.opinion, worked out every
/// year from the paths, the metal, the borders, the memory, envy and belief, and the word each uses for the other
/// (RELATIONS), told when it changes.
/// </summary>
public static class Opinions
{
    // Engine values from the generator's factions.py and history.py, copied as they are there.
    public const double TradeHope = 0.4;
    public const double Coveting = 0.4;
    public const double BorderRivalry = 0.5;
    public const double Envy = 0.4;
    /// <summary>history.py OPINION_MARGIN: how far past a band's edge an opinion must fall before its word changes.</summary>
    public const double OpinionMargin = 0.05;

    /// <summary>What faction `a` thinks of faction `b`, -1 to 1. factions.opinion.</summary>
    public static double Opinion(Game game, int a, int b, double memory, double coveted)
    {
        var faction = game.Factions[a];
        var other = game.Factions[b];
        double envy = Envy * faction.Traits["greed"] * coveted;
        return Math.Min(1.0, Math.Max(-1.0, PathPart(game, faction, other) + MetalPart(faction, other)
                                            + BorderPart(faction, b) + memory - envy + BeliefPart(game, faction, other)
                                            + RacePart(game, a, b)));
    }

    /// <summary>
    /// What faction `a`'s race thinks of `b`'s (RACE_OPINIONS, decided 1 November 2026); 0 without it. The first the
    /// table gives: the row of its race, then of the race it is a kind of (elf_wood is an elf), then "*" (every race);
    /// in that row, the other's race, then the race it is a kind of, then "*". factions.race_part.
    /// </summary>
    public static double RacePart(Game game, int a, int b)
    {
        var table = game.Settings.RaceOpinions;
        var races = game.World.Factions;
        string? mine = a < races.Count ? races[a].Race : null, theirs = b < races.Count ? races[b].Race : null;
        if (table.Count == 0 || mine == null || theirs == null)
            return 0.0;
        IEnumerable<string> Keys(string race)
        {
            yield return race;
            if (game.Settings.Races.TryGetValue(race, out var about) && about.Of != null)
                yield return about.Of;
            yield return "*";
        }
        foreach (string row in Keys(mine))
            if (table.TryGetValue(row, out var columns))
                foreach (string column in Keys(theirs))
                    if (columns.TryGetValue(column, out double value))
                        return value;
        return 0.0;
    }

    /// <summary>What `faction` thinks of `other`'s path: its own prejudice, or else PATH_OPINIONS. factions.path_part.</summary>
    public static double PathPart(Game game, FactionNow faction, FactionNow other)
    {
        double table = game.Settings.PathOpinion(faction.Path, other.Path);
        return faction.Prejudice.Count > 0 && other.Path != null ? faction.Prejudice.GetValueOrDefault(other.Path, table) : table;
    }

    /// <summary>Supply divided by need: above 1 is plenty. factions.Faction.metal_balance.</summary>
    public static double MetalBalance(FactionNow f) =>
        f.MetalNeed == 0 ? (f.MetalSupply > 0 ? double.PositiveInfinity : 1.0) : f.MetalSupply / f.MetalNeed;

    /// <summary>A faction short of metal facing one with metal to spare: a planner hopes to trade, the greedy covet.
    /// factions.metal_part.</summary>
    public static double MetalPart(FactionNow faction, FactionNow other)
    {
        double shortBy = Math.Max(0.0, 1.0 - MetalBalance(faction));
        double spare = Math.Min(1.0, Math.Max(0.0, MetalBalance(other) - 1.0));
        double hope = TradeHope * faction.Traits["planning"] * (1.0 - faction.Traits["greed"]);
        double want = Coveting * faction.Traits["greed"];
        return shortBy * spare * (hope - want);
    }

    /// <summary>Sharing a border: a bold or a cautious faction feels it most. factions.border_part.</summary>
    public static double BorderPart(FactionNow faction, int other)
    {
        double total = faction.Border.Values.Sum();
        if (total == 0)
            return 0.0;
        double shared = faction.Border.GetValueOrDefault(other) / total;
        double unease = Math.Abs(faction.Traits["risk"] - 0.5) * 2;
        return -BorderRivalry * shared * unease;
    }

    /// <summary>What a faction's belief about the god of death thinks of another's (BELIEF_OPINIONS). factions.belief_part.</summary>
    public static double BeliefPart(Game game, FactionNow faction, FactionNow other) =>
        game.Settings.BeliefOpinions.TryGetValue(faction.Belief, out var row) ? row.GetValueOrDefault(other.Belief) : 0.0;

    /// <summary>
    /// For each faction and good it needs (but only wants: not): how much it lacks after trade, how much it needs,
    /// and how much it has left to spare, the staples together as "staples". war.supplies.
    /// </summary>
    public static List<Dictionary<string, double[]>> Supplies(Game game, Market[] markets)
    {
        var staples = game.Settings.Staples;
        var found = game.Factions.Select(_ => new Dictionary<string, double[]>()).ToList();
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int f)
                continue;
            var mine = found[f];
            var market = markets[i];
            foreach (var (good, need) in market.Needed)
            {
                if (staples.Contains(good) || market.Wanted.Contains(good))
                    continue;
                if (!mine.TryGetValue(good, out var entry))
                    mine[good] = entry = new double[3];
                entry[0] += market.Shortage(good);
                entry[1] += need;
                entry[2] += Math.Max(0.0, market.Surplus(good) - market.Sold.GetValueOrDefault(good));
            }
            if (staples.Count > 0)
            {
                if (!mine.TryGetValue("staples", out var entry))
                    mine["staples"] = entry = new double[3];
                entry[0] += market.PooledShortage(staples);
                entry[1] += staples.Sum(g => market.Needed.GetValueOrDefault(g));
                entry[2] += Math.Max(0.0, market.PooledSurplus(staples) - staples.Sum(g => market.Sold.GetValueOrDefault(g)));
            }
        }
        return found;
    }

    /// <summary>How much faction `a` covets what faction `b` has, 0 to 1: the good `a` lacks most that `b` is better
    /// supplied in. war.covet.</summary>
    public static double Covet(List<Dictionary<string, double[]>> supply, int a, int b)
    {
        double coveted = 0.0;
        var theirs = supply[b];
        foreach (var (good, entry) in supply[a])
        {
            double missing = entry[0], needed = entry[1];
            if (missing <= 0 || needed <= 0)
                continue;
            double theirMissing = 0, theirNeeded = 0;
            if (theirs.TryGetValue(good, out var t))
                (theirMissing, theirNeeded) = (t[0], t[1]);
            if (theirNeeded <= 0)
                continue;
            double ours = 1.0 - missing / needed, theirShare = 1.0 - theirMissing / theirNeeded;
            coveted = Math.Max(coveted, missing / needed * Math.Max(0.0, theirShare - ours));
        }
        return coveted;
    }

    /// <summary>The last band whose lowest value `value` reaches (RELATIONS). history.band_of.</summary>
    public static int Band(double value, IReadOnlyList<double> lows)
    {
        int index = 0;
        for (int i = 0; i < lows.Count; i++)
            if (value >= lows[i])
                index = i;
        return index;
    }

    /// <summary>Like Band, but leaving `current` downwards only once `margin` below its edge. history.keep_band.</summary>
    public static int KeepBand(double value, IReadOnlyList<double> lows, int current, double margin)
    {
        int band = Band(value, lows);
        if (band > current)
            return band;
        if (band < current && value < lows[current] - margin)
            return band;
        return current;
    }

    /// <summary>
    /// Every opinion worked out again (history.update_factions, after the memories): only between factions that both
    /// have people; the first word between a pair is not news, a later change of word is.
    /// </summary>
    public static void Year(Game game, Market[] markets, int year)
    {
        var supply = Supplies(game, markets);
        var lows = game.Settings.Relations.Select(r => r.Low).ToList();
        foreach (var pair in game.Memory.Keys.OrderBy(p => p).ToList())
        {
            var (a, b) = pair;
            if (game.Factions[a].People <= 0 || game.Factions[b].People <= 0)
                continue;
            double value = Opinion(game, a, b, game.Memory[pair], Covet(supply, a, b));
            game.Factions[a].Opinions[b] = value;
            if (!game.Words.ContainsKey(pair))
                game.Words[pair] = Band(value, lows);
            int word = KeepBand(value, lows, game.Words[pair], OpinionMargin);
            if (word != game.Words[pair])
            {
                game.Words[pair] = word;
                string name = game.Settings.Relations[word].Name;
                game.Record(new GameEvent(year, "opinion", "opinion",
                    "the " + game.Factions[a].Name + " now see the " + game.Factions[b].Name + " as " + name + " ("
                    + value.ToString("+0.00;-0.00") + ")", new[] { a, b },
                    new Dictionary<string, string>
                    {
                        ["faction"] = game.Factions[a].Name, ["other"] = game.Factions[b].Name, ["word"] = name,
                    }));
            }
        }
    }
}
