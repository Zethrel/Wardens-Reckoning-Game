// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Capitals (the generator's north/capitals.py; your settings are CAPITAL_CHOICE and CAPITAL_MOVE_CHANCE in
/// split_settings.py): where a faction's seat of power is, and how it moves. Twice a year (before the splits, which
/// measure distances from the capital, and at the end of the year) every faction's capital is kept a town it holds:
///
/// - A new capital: a faction that no longer holds its capital (taken, ruined, lost to a breakaway) picks another of
///   its towns. With CAPITAL_CHOICE "weighted" every town's chance is its share of the faction's people: usually a
///   big town, now and then a small one. With "largest" it is the largest.
/// - A breakaway's (or a risen faction's) first capital is picked the same way among its towns.
/// - Moving: at the end of the year, a faction with more than one town may move its capital to another of them
///   (CAPITAL_MOVE_CHANCE), picked the same way.
///
/// Capitals have their own dice ("capitals"). With "largest" and no moves there are no capitals to look after,
/// and Splits.CapitalOf keeps a faction's largest town as its capital, as before.
/// </summary>
public static class Capitals
{
    /// <summary>Whether capitals are looked after at all (capitals.from_settings).</summary>
    public static bool On(GameSettings settings) => settings.CapitalChoice != "largest" || settings.CapitalMoveChance > 0;

    /// <summary>The towns a faction holds that are not ruins, in order. capitals.holdings.</summary>
    public static List<int> Holdings(Game game, int faction) =>
        Enumerable.Range(0, game.Towns.Count).Where(t => game.Towns[t].Holder == faction && !game.Towns[t].Ruined).ToList();

    /// <summary>The town with the most people, the first on a tie. capitals.largest.</summary>
    public static int Largest(Game game, IReadOnlyList<int> towns) =>
        towns.OrderByDescending(t => game.Towns[t].Population).ThenBy(t => t).First();

    /// <summary>One of `towns`, by CAPITAL_CHOICE: the largest, or drawn with each town's chance its share of the
    /// people (a town of fewer than 1 counted as 1). capitals.Capitals.pick.</summary>
    public static int Pick(Game game, IReadOnlyList<int> towns)
    {
        if (game.Settings.CapitalChoice != "weighted")
            return Largest(game, towns);
        var weights = towns.Select(t => Math.Max(1, game.Towns[t].Population)).ToList();
        double roll = game.DiceFor("capitals").Random() * weights.Sum();
        for (int i = 0; i < towns.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return towns[i];
        }
        return towns[^1];
    }

    /// <summary>
    /// Give every faction a capital it holds; with `moving`, perhaps move one (capitals.Capitals.mend). A faction
    /// whose capital has never been looked after (one that came to be this year) picks its first.
    /// </summary>
    public static void Mend(Game game, int year, bool moving = false)
    {
        var settings = game.Settings;
        if (!On(settings))
            return;
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            var held = Holdings(game, f);
            if (held.Count == 0)
                continue;
            if (!game.CapitalsKnown.Contains(f) || faction.Capital == null)
            {
                game.CapitalsKnown.Add(f);
                faction.Capital = Pick(game, held);
                continue;
            }
            if (!held.Contains(faction.Capital.Value))
            {
                string old = game.World.Towns[faction.Capital.Value].Name;
                faction.Capital = Pick(game, held);
                string now = game.World.Towns[faction.Capital.Value].Name;
                game.Record(new GameEvent(year, "capital", "new_capital",
                    "the " + faction.Name + " make " + now + " their capital, " + old + " being lost", new[] { f },
                    new Dictionary<string, string> { ["faction"] = faction.Name, ["place"] = now, ["old"] = old }));
                continue;
            }
            if (moving && settings.CapitalMoveChance > 0 && held.Count > 1
                && game.DiceFor("capitals").Random() < settings.CapitalMoveChance)
            {
                int was = faction.Capital.Value;
                faction.Capital = Pick(game, held.Where(t => t != was).ToList());
                string old = game.World.Towns[was].Name, now = game.World.Towns[faction.Capital.Value].Name;
                game.Record(new GameEvent(year, "capital", "capital_moved",
                    "the " + faction.Name + " move their capital from " + old + " to " + now, new[] { f },
                    new Dictionary<string, string> { ["faction"] = faction.Name, ["place"] = now, ["old"] = old }));
            }
        }
    }
}
