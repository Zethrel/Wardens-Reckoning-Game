// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Drift: the land slowly changes a people (the generator's migration.year_of_drift). Every faction's values (its
/// people's own ways, which rulers pull its traits away from) move a little each year toward what the land it holds
/// encourages: its people's nature changed by your LAND_INFLUENCES for its shares of coast, rivers, highlands and
/// lowlands. They go halfway there every DRIFT_HALF_LIFE years; the land is looked at again every LAND_YEARS years.
/// </summary>
public static class Drift
{
    /// <summary>How often, in years, the land each faction holds is looked at again.</summary>
    public const int LandYears = 10;

    /// <summary>The kinds of land, in the generator's order (factions.HOMELANDS).</summary>
    public static readonly string[] Kinds = { "coast", "rivers", "highlands", "lowlands" };

    /// <summary>Each faction's share of its land that is coast, by rivers, highland and lowland. factions.shares_of.</summary>
    public static List<Dictionary<string, double>> SharesOf(Game game)
    {
        int count = game.Factions.Count;
        var totals = Enumerable.Range(0, count).Select(_ => Kinds.ToDictionary(k => k, _ => 0)).ToList();
        var land = new int[count];
        for (int cell = 0; cell < game.LandKinds.Length; cell++)
        {
            int kind = game.LandKinds[cell];
            int owner = game.Regions[cell];
            if (kind == 0 || owner < 0 || owner >= count)
                continue;
            land[owner]++;
            if ((kind & 2) != 0)
                totals[owner]["coast"]++;
            if ((kind & 4) != 0)
                totals[owner]["rivers"]++;
            totals[owner][(kind & 8) != 0 ? "highlands" : "lowlands"]++;
        }
        return Enumerable.Range(0, count)
            .Select(f => Kinds.ToDictionary(k => k, k => land[f] > 0 ? (double)totals[f][k] / land[f] : 0.0)).ToList();
    }

    /// <summary>The traits after the land's influence, each kept within 0 and 1. factions.influenced_traits.</summary>
    public static Dictionary<string, double> Influenced(IReadOnlyDictionary<string, double> traits,
                                                        IReadOnlyDictionary<string, double> shares,
                                                        IEnumerable<(string Kind, string Trait, double Change)> influences)
    {
        var changed = new Dictionary<string, double>(traits);
        foreach (var (kind, trait, change) in influences)
            changed[trait] += change * shares.GetValueOrDefault(kind);
        return changed.ToDictionary(p => p.Key, p => Math.Min(1.0, Math.Max(0.0, p.Value)));
    }

    /// <summary>A year's step of every faction's values toward what its land encourages. migration.year_of_drift.</summary>
    public static void Year(Game game, int year)
    {
        var factions = Enumerable.Range(0, game.Factions.Count)
            .Where(f => game.Factions[f].Nature.Count > 0 && Splits.Holdings(game, f).Count > 0).ToList();
        if ((year - 1) % LandYears == 0 || factions.Any(f => !game.DriftTargets.ContainsKey(f)))
        {
            var shares = SharesOf(game);
            foreach (int f in factions)
                game.DriftTargets[f] = Influenced(game.Factions[f].Nature, shares[f], game.Settings.LandInfluences);
        }
        double step = 1.0 - Math.Pow(0.5, 1.0 / game.Settings.DriftHalfLife);
        foreach (int f in factions)
        {
            var values = game.Factions[f].Values;
            var target = game.DriftTargets[f];
            foreach (string trait in Splits.Traits)
                values[trait] += (target[trait] - values[trait]) * step;
        }
    }
}
