// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Rituals (the generator's north/rituals.py; your settings are in attunement_settings.py), for the factions whose
/// path works rituals (attunement).
///
/// The harvest ritual (`HarvestYear`): a faction with a town that went hungry last year gambles on a ritual for this
/// year's harvest with HARVEST_RITUAL_CHANCE, if it has metal; one whose towns' harvest this year averages below
/// HARVEST_RITUAL_POOR may too, though none went hungry, the less the better it plans. It spends
/// HARVEST_RITUAL_METAL of its metal; its towns' harvest is multiplied by a number drawn from HARVEST_RITUAL, moved by
/// up to HARVEST_RITUAL_SKILL by its ruler's competence. Every other faction thinks HARVEST_RITUAL_UNEASE less of it.
/// When it brings less than an ordinary harvest, it marks its towns' land.
///
/// Rituals roll their own dice ("rituals").
/// </summary>
public static class Rituals
{
    /// <summary>
    /// This year's harvest rituals (rituals.year_of_harvest_rituals): returns each faction that worked one and its
    /// harvest factor. `weather` is each town's harvest this year as a share of an ordinary one, before its path and
    /// ruler change it (a poor season frightens them).
    /// </summary>
    public static Dictionary<int, double> HarvestYear(Game game, IReadOnlyList<double> weather, int year)
    {
        var settings = game.Settings;
        var worked = new Dictionary<int, double>();
        double chance = settings.HarvestRitualChance;
        if (!settings.Attuning || chance <= 0)
            return worked;
        var dice = game.DiceFor("rituals");
        var wanting = new HashSet<int>();
        for (int i = 0; i < game.Towns.Count; i++)
            if (!game.Towns[i].Ruined && game.Towns[i].Holder is int holder && game.Towns[i].Hungry > 0)
                wanting.Add(holder);
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            if (!settings.PathCan(faction.Path, "works_rituals") || faction.People <= 0)
                continue;
            string why;
            double odds;
            if (wanting.Contains(f))
                (why, odds) = ("hungry", chance);
            else if (settings.HarvestRitualPoor > 0 && PoorYear(game, f, weather) < settings.HarvestRitualPoor)
                (why, odds) = ("poor harvest", chance * (1.0 - faction.Traits["planning"]));
            else
                continue;
            if (dice.Random() >= odds || MetalOf(game, f) <= 0)
                continue;
            foreach (var mine in game.Mines)
                if (mine.Owner == f)
                    mine.Metal -= mine.Metal * settings.HarvestRitualMetal;
            double competence = faction.Ruler?.Traits["competence"] ?? 0.5;
            var (low, high) = settings.HarvestRitual;
            double outcome = Math.Max(0.0, dice.Uniform(low, high) + settings.HarvestRitualSkill * (2 * competence - 1));
            worked[f] = outcome;
            for (int other = 0; other < game.Factions.Count; other++)
                if (game.Memory.TryGetValue((other, f), out double remembered))
                    game.Memory[(other, f)] = Math.Max(-1.0, remembered - settings.HarvestRitualUnease);
            if (outcome < 1.0 && game.Marks != null)
                MarkTowns(game, f, settings.HarvestRitualMark * (1.0 - outcome) * 10);
            string ruler = faction.Ruler?.Name ?? "no one";
            int change = Py.Round((outcome - 1.0) * 100);
            string who = why == "hungry" ? faction.Name : faction.Name + ", fearing a poor harvest,";
            string text = outcome >= 1.0
                ? "the " + who + " work a harvest ritual under " + ruler + ": the harvest is " + change + "% richer"
                : "the " + who + " work a harvest ritual under " + ruler + ", and it goes wrong: the harvest is "
                  + -change + "% poorer, and the land is marked";
            game.Record(new GameEvent(year, "attunement", outcome >= 1.0 ? "harvest_ritual" : "harvest_ritual_failed",
                text, new[] { f }, new Dictionary<string, string>
                {
                    ["faction"] = faction.Name, ["ruler"] = ruler, ["harvest"] = Math.Abs(change).ToString(),
                    ["why"] = why,
                }));
        }
        return worked;
    }

    /// <summary>The average of this year's weather over a faction's towns; 1.0 without any (rituals.poor_year).</summary>
    public static double PoorYear(Game game, int faction, IReadOnlyList<double> weather)
    {
        var theirs = Enumerable.Range(0, game.Towns.Count)
                               .Where(i => !game.Towns[i].Ruined && game.Towns[i].Holder == faction)
                               .Select(i => weather[i]).ToList();
        return theirs.Count > 0 ? Py.Sum(theirs) / theirs.Count : 1.0;
    }

    /// <summary>The metal left in the mines a faction holds (rituals.metal_of).</summary>
    public static double MetalOf(Game game, int faction) =>
        Py.Sum(game.Mines.Where(m => m.Owner == faction && m.Metal > 0).Select(m => m.Metal));

    /// <summary>Add `amount` of mark to every patch of the land of a faction's towns; a patch with no maker gets this
    /// faction (rituals.mark_towns).</summary>
    public static void MarkTowns(Game game, int faction, double amount)
    {
        var marks = game.Marks!;
        var patches = new HashSet<(int, int)>();
        for (int i = 0; i < game.Towns.Count; i++)
            if (!game.Towns[i].Ruined && game.Towns[i].Holder == faction && i < marks.Lands.Count)
                patches.UnionWith(marks.Lands[i].Select(p => p.Patch));
        foreach (var patch in patches.OrderBy(p => p.Item1).ThenBy(p => p.Item2))
        {
            marks.Mark[patch] = marks.Mark.GetValueOrDefault(patch, 0.0) + amount;
            marks.Maker.TryAdd(patch, faction);
        }
    }
}
