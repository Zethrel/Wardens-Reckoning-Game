// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>What a settlement rule sees of one town in one year (the generator's history.Place).</summary>
public sealed record PlaceView(string Name, string Size, int Population, double Capacity, double Fed, int HungryYears,
                               IReadOnlyDictionary<string, double> Shortages, string Faction, string Path,
                               IReadOnlyDictionary<string, double> Traits)
{
    public double Crowding => Capacity > 0 ? Population / Capacity : 1.0;
    /// <summary>How much of its need for `good` is still missing after trade, 0 to 1 ("staples" for all staples).</summary>
    public double ShortOf(string good) => Shortages.GetValueOrDefault(good);
}

/// <summary>What a rule returns: a share of the people gained or lost, and how much the faction's memory of every
/// faction with food to spare sours (0 to 1).</summary>
public sealed record RuleEffect(double Population = 0, double Blame = 0);

/// <summary>
/// Your rules, from the generator's north/history_rules.py, written again in C#: what happens to a settlement in a
/// year (RULES), and to a faction (FACTION_RULES).
///
/// They are code, not settings, so the game cannot read them from the export: when you change a rule in
/// history_rules.py, change it here too (or tell Claude to). Each rule below names the Python function it copies.
/// `chance` is the rules' own dice (Game.DiceFor("rules")): world.chance in the Python.
/// </summary>
public static class Rules
{
    public delegate RuleEffect? SettlementRule(PlaceView place, Func<double, bool> chance);

    /// <summary>RULES, in the order the Python lists them: the order they are asked in matters, as each may change
    /// the people the next one sees.</summary>
    public static readonly (string Name, SettlementRule Rule)[] Settlement =
    {
        ("famine", (place, chance) => place.HungryYears >= 3 && chance(0.2) ? new RuleEffect(Population: -0.1) : null),
        ("negative_memory", (place, chance) =>
            place.Traits.GetValueOrDefault("greed") > 0.3 && place.HungryYears >= 3 && chance(0.1) ? new RuleEffect(Blame: 0.2) : null),
        ("drought", (place, chance) => place.Fed < 0.5 && chance(0.1) ? new RuleEffect(Population: -0.05) : null),
        ("disease", (place, chance) => place.Population > 1000 && chance(0.003) ? new RuleEffect(Population: -0.1) : null),
        ("trade_disruption", (place, chance) => place.ShortOf("metal") > 0.2 && chance(0.01) ? new RuleEffect(Population: -0.05) : null),
        ("natural_disaster", (place, chance) => chance(0.002) ? new RuleEffect(Population: -0.2) : null),
        ("economic_boom", (place, chance) => place.Fed > 0.8 && chance(0.01) ? new RuleEffect(Population: 0.1) : null),
        ("resource_depletion", (place, chance) => place.ShortOf("staples") > 0.3 && chance(0.1) ? new RuleEffect(Population: -0.1) : null),
        ("migration", (place, chance) => place.Population > 500 && chance(0.03) ? new RuleEffect(Population: -0.04) : null),
    };

    /// <summary>
    /// Runs every settlement rule on town `index` (history.run_years): a population effect changes its people at once
    /// (so the next rule sees it); blame sours its faction's memory of every other faction with food to spare.
    /// </summary>
    public static void SettlementRules(Game game, int index, PlaceView view, HashSet<int> spare,
                                       Dictionary<(int, int), double> blame, int year)
    {
        var dice = game.DiceFor("rules");
        var town = game.Towns[index];
        string place = game.World.Towns[index].Name;
        foreach (var (name, rule) in Settlement)
        {
            var effect = rule(view, dice.Chance);
            if (effect == null)
                continue;
            var details = new List<string>();
            if (effect.Population != 0)
            {
                double before = town.People;
                town.People = Math.Max(0.0, town.People * (1 + effect.Population));
                details.Add(Signed((int)Math.Round(town.People - before, MidpointRounding.ToEven)) + " people");
            }
            if (effect.Blame != 0 && town.Holder is int faction)
            {
                var blamed = spare.Where(f => f != faction).OrderBy(f => f).ToList();
                foreach (int other in blamed)
                    blame[(faction, other)] = Math.Max(blame.GetValueOrDefault((faction, other)), effect.Blame);
                if (blamed.Count > 0)
                    details.Add("blames the " + string.Join(", ", blamed.Select(f => game.Factions[f].Name)));
            }
            string text = name.Replace('_', ' ') + " at " + place + (details.Count > 0 ? " (" + string.Join("; ", details) + ")" : "");
            game.Record(new GameEvent(year, name, name, text,
                town.Holder is int h ? new[] { h } : Array.Empty<int>(),
                new Dictionary<string, string> { ["place"] = place, ["faction"] = view.Faction }));
        }
    }

    /// <summary>
    /// FACTION_RULES: grievance (history_rules.grievance): a greedy faction's people grumble now and then. Run in the
    /// rulers' year, after the grievance is worked out (leaders.year_of_rulers).
    /// </summary>
    public static void FactionRules(Game game, int index, Rulers.Felt felt, int year)
    {
        var faction = game.Factions[index];
        var dice = game.DiceFor("rules");
        if (faction.Traits["greed"] > 0.05 && dice.Chance(0.05))
        {
            double change = 0.2;
            faction.Grievance = Math.Max(0.0, faction.Grievance + change);
            game.Record(new GameEvent(year, "grievance", "grievance",
                "grievance among the " + faction.Name + " (grievance " + change.ToString("+0.00;-0.00") + ")",
                new[] { index }, new Dictionary<string, string> { ["faction"] = faction.Name }));
        }
    }

    // Python's "%+d": a sign always.
    static string Signed(int value) => (value >= 0 ? "+" : "") + value;
}
