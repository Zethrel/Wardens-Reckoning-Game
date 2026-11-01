// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>One ruler of one faction (the generator's leaders.Ruler).</summary>
public sealed class Ruler
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    /// <summary>charisma, competence, ambition, planning, greed: 0 to 1.</summary>
    public Dictionary<string, double> Traits { get; set; } = new();
    /// <summary>The year they came to power.</summary>
    public int Since { get; set; }
    /// <summary>The path they favour.</summary>
    public string Path { get; set; } = "";
    /// <summary>Other faction → -1 (hostile) to 1 (warm).</summary>
    public Dictionary<int, double> Stances { get; set; } = new();
    /// <summary>Their work so far to turn the faction to their path.</summary>
    public double Effort { get; set; }
}

/// <summary>A dead ruler's lingering pull on the faction's ways (leaders.vacuum's cult).</summary>
public sealed class Cult
{
    public Dictionary<string, double> Traits { get; set; } = new();
    public int Until { get; set; }
    public double Strength { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Rulers, grievance and revolutions (step 4): the generator's north/leaders.py, function by function. Once a year
/// for every faction with people: grievance (hunger, shortages, losses, a ruler against the people's ways, eased by
/// charisma), the ruler's pull on the faction's ways, prejudices drifting after a change of path, a charismatic
/// ruler's work to turn the faction to their path, their stances warming or souring memories, age and death (and
/// the vacuum a strong ruler leaves), and revolution when grievance is past what the government tolerates.
/// </summary>
public static class Rulers
{
    // Engine values from the generator's leaders.py, copied as they are there.
    public static readonly string[] RulerTraits = { "charisma", "competence", "ambition", "planning", "greed" };
    /// <summary>Which of the faction's traits each ruler trait pulls: an ambitious ruler makes a bold faction.</summary>
    public static readonly (string Mine, string Theirs)[] Pulls = { ("planning", "planning"), ("greed", "greed"), ("ambition", "risk") };
    public static readonly string[] Grievances = { "hunger", "shortages", "losses", "ruler_against_values" };
    public const double TraitDrift = 0.1;
    public const double CharismaDamping = 0.5;
    public const double RevolutionChance = 0.3;
    public const double RevolutionRelief = 0.5;
    public const double DeathChance = 0.01;
    public const double DeathChancePerYearPast40 = 0.004;
    public const double OtherPathChance = 0.1;
    public const double ConvertCharisma = 0.7;
    public const double ConversionEffort = 10.0;
    public const double PathMargin = 0.1;
    public const double RevolutionPathChance = 0.5;
    public const double PrejudiceHalfLife = 30;
    public const int StrongReign = 20;
    public const double Strong = 0.5;
    public const double SuccessionFight = 0.3;
    public const double WeakerSuccessor = 0.7;
    public const int CultYears = 25;
    public const double StanceChance = 0.3;
    public const double SwayPerYear = 0.05;

    static double Clamp(double value) => Math.Min(1.0, Math.Max(0.0, value));

    /// <summary>How well each path suits a people's values, 0 to 1 ("suits" in PATHS): a value wanted "high" counts
    /// as it is, one wanted "low" as 1 minus it; a path naming none suits everyone halfway. leaders.path_fit.</summary>
    public static Dictionary<string, double> PathFit(Game game, IReadOnlyDictionary<string, double> values)
    {
        var fit = new Dictionary<string, double>();
        foreach (var (path, suits) in game.Settings.PathSuits)
        {
            var terms = suits.Select(s => s.Want == "high" ? values[s.Trait] : 1 - values[s.Trait]).ToList();
            fit[path] = terms.Count > 0 ? terms.Sum() / terms.Count : 0.5;
        }
        return fit;
    }

    /// <summary>The paths a faction may turn to: every path, or with paths as skills only its main and held ones.
    /// leaders.choosable.</summary>
    public static List<string> Choosable(Game game, FactionNow faction) =>
        faction.Held == null ? game.Settings.PathSuits.Keys.ToList()
            : game.Settings.PathSuits.Keys.Where(p => p == faction.Path || faction.Held.Contains(p)).ToList();

    /// <summary>The choosable path the people's values fit best, the first on a tie. leaders.best_held.</summary>
    public static string BestHeld(Game game, FactionNow faction, IReadOnlyDictionary<string, double> values)
    {
        var fit = PathFit(game, values);
        string best = "";
        double most = double.NegativeInfinity;
        foreach (var path in Choosable(game, faction))
            if (fit[path] > most)
                (best, most) = (path, fit[path]);
        return best;
    }

    /// <summary>A new ruler, chosen by a succession method ("heir", "chosen", "strongest"). leaders.new_ruler.</summary>
    public static Ruler NewRuler(Game game, string method, int index, Ruler? previous, int year)
    {
        var dice = game.DiceFor("rulers");
        var faction = game.Factions[index];
        double Near(double value, double spread) => Clamp(value + dice.Uniform(-spread, spread));
        var values = faction.Values;
        Dictionary<string, double> traits;
        int age;
        if (method == "heir" && previous != null)
        {
            traits = RulerTraits.ToDictionary(t => t, t => Near(previous.Traits[t], 0.15));
            age = dice.Int(18, 40);
        }
        else if (method == "strongest")
        {
            traits = RulerTraits.ToDictionary(t => t, _ => dice.Random());
            traits["charisma"] = 0.5 + 0.5 * dice.Random();
            traits["ambition"] = 0.5 + 0.5 * dice.Random();
            age = dice.Int(25, 50);
        }
        else
        {
            traits = new Dictionary<string, double>
            {
                ["charisma"] = dice.Random(), ["competence"] = dice.Random(),
                ["planning"] = Near(values["planning"], 0.15), ["greed"] = Near(values["greed"], 0.15),
                ["ambition"] = Near(values["risk"], 0.15),
            };
            age = dice.Int(35, 60);
        }
        // Older for a long-lived people, younger for a short-lived one (as a human's age, times its scale).
        double scale = AgeScale(game, index);
        if (scale != 1.0)
            age = Py.Round(age * scale);
        string path;
        Dictionary<int, double> stances;
        if (method == "heir" && previous != null)
        {
            path = previous.Path != "" ? previous.Path : faction.Path ?? "";
            stances = new Dictionary<int, double>(previous.Stances);
        }
        else
        {
            if (method == "chosen" && previous != null)
                path = BestHeld(game, faction, values);
            else if (dice.Random() < OtherPathChance)
            {
                var turns = Choosable(game, faction).Where(p => p != faction.Path).ToList();
                path = turns.Count > 0 ? dice.Choice(turns) : faction.Path ?? "";
            }
            else
                path = faction.Path ?? "";
            stances = new Dictionary<int, double>();
            for (int other = 0; other < game.Factions.Count; other++)
                if (other != index && dice.Random() < StanceChance)
                    stances[other] = dice.Choice(new[] { -1, 1 }) * (0.5 + 0.5 * dice.Random());
        }
        return new Ruler { Name = game.NextName(faction.Folk), Age = age, Traits = traits, Since = year, Path = path,
                           Stances = stances };
    }

    /// <summary>How far a ruler's ways are from the people's, 0 (the same) to 1 (opposite). leaders.against_values.</summary>
    public static double AgainstValues(Ruler ruler, IReadOnlyDictionary<string, double> values) =>
        Pulls.Sum(p => Math.Abs(ruler.Traits[p.Mine] - values[p.Theirs])) / Pulls.Length;

    public static double DeathChanceAt(int age) => DeathChance + DeathChancePerYearPast40 * Math.Max(0, age - 40);

    /// <summary>The chance a ruler of `age` dies this year, their people aging by `scale`: an elf of 375 is as old as a
    /// human of 40. leaders.death_chance.</summary>
    public static double DeathChanceAt(int age, double scale) =>
        scale == 1.0 ? DeathChanceAt(age) : DeathChance + DeathChancePerYearPast40 * Math.Max(0, age / scale - 40);

    /// <summary>The lifespan (race_settings.py) the ages here are counted for: a human's.</summary>
    public const double HumanLifespan = 80;

    /// <summary>How much longer than a human a people of `race` lives, as LIFESPAN_SCALING says to count it: 1 for a
    /// human, a people whose lifespan is not given, or a scaling of 0; a kind of a race (elf_wood) lives as its race.
    /// leaders.age_scale.</summary>
    public static double AgeScale(GameSettings settings, string? race)
    {
        if (settings.LifespanScaling == 0 || race == null || !settings.Races.TryGetValue(race, out var about))
            return 1.0;
        if (about.Of != null && settings.Races.TryGetValue(about.Of, out var of))
            about = of;
        if (about.Lifespan is not double lifespan || lifespan == 0)
            return 1.0;
        return 1.0 + settings.LifespanScaling * (lifespan / HumanLifespan - 1.0);
    }

    /// <summary>How faction `index`'s rulers age, by its race (the world's records keep it).</summary>
    public static double AgeScale(Game game, int index) =>
        AgeScale(game.Settings, index < game.World.Factions.Count ? game.World.Factions[index].Race : null);

    /// <summary>What is worth knowing about a new ruler's ways: another path, and stances. leaders.describe.</summary>
    public static string Describe(Game game, Ruler ruler, FactionNow faction)
    {
        var parts = new List<string>();
        if (ruler.Path != "" && ruler.Path != faction.Path)
            parts.Add("favours " + ruler.Path);
        foreach (var (other, stance) in ruler.Stances.OrderBy(s => s.Key))
            parts.Add((stance > 0 ? "warm toward" : "hostile to") + " the " + game.Factions[other].Name);
        return parts.Count > 0 ? "; " + string.Join(", ", parts) : "";
    }

    /// <summary>Turns a faction to another path; its prejudices follow slowly. leaders.change_path.</summary>
    public static void ChangePath(Game game, int index, string path, int year, string why, string? ruler)
    {
        var faction = game.Factions[index];
        var facts = new Dictionary<string, string>
        {
            ["faction"] = faction.Name, ["old_path"] = faction.Path ?? "", ["new_path"] = path,
        };
        if (ruler != null)
            facts["ruler"] = ruler;
        game.Record(new GameEvent(year, "path", ruler != null ? "path_by_ruler" : "path_by_revolution",
            "the " + faction.Name + " turn from " + faction.Path + " to " + path + " " + why, new[] { index }, facts));
        if (faction.Held != null)
        {
            faction.Held.Remove(path);
            if (faction.Path != null)
                faction.Held.Add(faction.Path);
        }
        faction.Path = path;
        faction.PathChanges++;
        if (faction.Ruler != null)
            faction.Ruler.Effort = 0.0;
    }

    /// <summary>Moves a faction's prejudices a year's step toward what its path thinks. leaders.drift_prejudice.</summary>
    public static void DriftPrejudice(Game game, FactionNow faction)
    {
        double keep = Math.Pow(0.5, 1 / PrejudiceHalfLife);
        foreach (var path in game.Settings.PathSuits.Keys)
        {
            double target = game.Settings.PathOpinion(faction.Path, path);
            faction.Prejudice[path] = target + (faction.Prejudice.GetValueOrDefault(path, target) - target) * keep;
        }
    }

    /// <summary>How strong a ruler was: charisma times how long they ruled. leaders.strength_of.</summary>
    public static double StrengthOf(Ruler ruler, int year) =>
        ruler.Traits["charisma"] * Math.Min(1.0, (year - ruler.Since) / (double)StrongReign);

    /// <summary>What each faction felt this year, from the towns (history.felt_hardship): hunger and shortages
    /// averaged over its people, losses (a town shrinking a size counts once, falling into ruin twice) over its towns.</summary>
    public sealed record Felt(double Hunger, double Shortages, double Losses, double Fed, int Settlements);

    /// <summary>
    /// One year of grievance, pull, paths, stances, age, death and revolution for every faction. `felt` is each
    /// faction's hardship this year. leaders.year_of_rulers (your faction rules run in it: Rules.cs).
    /// </summary>
    public static void Year(Game game, IReadOnlyList<Felt> felt, int year)
    {
        var settings = game.Settings;
        double fade = Math.Pow(0.5, 1 / settings.GrievanceHalfLife);
        for (int index = 0; index < game.Factions.Count; index++)
        {
            var faction = game.Factions[index];
            var ruler = faction.Ruler;
            if (ruler == null || faction.People <= 0)
            {
                faction.Ruler = null;
                continue;
            }
            var government = settings.Governments[faction.Government];
            var hard = felt[index];
            double weighed = settings.GrievanceWeights.GetValueOrDefault("hunger") * hard.Hunger
                             + settings.GrievanceWeights.GetValueOrDefault("shortages") * hard.Shortages
                             + settings.GrievanceWeights.GetValueOrDefault("losses") * hard.Losses
                             + settings.GrievanceWeights.GetValueOrDefault("ruler_against_values") * AgainstValues(ruler, faction.Values);
            weighed *= 1 - CharismaDamping * ruler.Traits["charisma"];
            faction.Grievance = faction.Grievance * fade + (1 - fade) * weighed;

            Rules.FactionRules(game, index, hard, year);

            double power = government.RulerPower;
            IReadOnlyDictionary<string, double> pulling = ruler.Traits;
            double pull = power * ruler.Traits["charisma"];
            if (faction.Cult != null && year >= faction.Cult.Until)
                faction.Cult = null;
            if (faction.Cult != null)
            {
                double lingering = faction.Cult.Strength * (faction.Cult.Until - year) / CultYears;
                var cult = faction.Cult;
                pulling = Pulls.ToDictionary(p => p.Mine, p => (1 - lingering) * ruler.Traits[p.Mine] + lingering * cult.Traits[p.Mine]);
                pull = power * Math.Max(ruler.Traits["charisma"], lingering);
            }
            foreach (var (mine, theirs) in Pulls)
            {
                double target = faction.Values[theirs] + pull * (pulling[mine] - faction.Values[theirs]);
                faction.Traits[theirs] = Clamp(faction.Traits[theirs] + TraitDrift * (target - faction.Traits[theirs]));
            }

            if (faction.Prejudice.Count > 0)
                DriftPrejudice(game, faction);
            if (ruler.Path != "" && ruler.Path != faction.Path && ruler.Traits["charisma"] >= ConvertCharisma
                && Choosable(game, faction).Contains(ruler.Path))
            {
                ruler.Effort += ruler.Traits["charisma"] * power;
                if (ruler.Effort >= ConversionEffort)
                    ChangePath(game, index, ruler.Path, year, "under " + ruler.Name, ruler.Name);
            }
            foreach (var (other, stance) in ruler.Stances)
            {
                var key = (index, other);
                if (game.Memory.TryGetValue(key, out double remembered))
                    game.Memory[key] = Math.Min(1.0, Math.Max(-1.0, remembered + SwayPerYear * stance * ruler.Traits["charisma"] * power));
            }

            var dice = game.DiceFor("rulers");
            ruler.Age++;
            if (dice.Random() < DeathChanceAt(ruler.Age, AgeScale(game, index)))
            {
                double strength = StrengthOf(ruler, year);
                var successor = NewRuler(game, government.Succession, index, ruler, year);
                game.Record(new GameEvent(year, "ruler", "succession", string.Format(
                    "{0}, ruler of the {1}, died at {2} after {3}; {4} rules (age {5}{6})", ruler.Name, faction.Name,
                    ruler.Age, YearsWord(year - ruler.Since), successor.Name, successor.Age, Describe(game, successor, faction)),
                    new[] { index }, new Dictionary<string, string>
                    {
                        ["ruler"] = ruler.Name, ["faction"] = faction.Name, ["age"] = ruler.Age.ToString(),
                        ["years"] = (year - ruler.Since).ToString(), ["successor"] = successor.Name,
                        ["successor_age"] = successor.Age.ToString(),
                    }));
                if (strength >= Strong)
                    Vacuum(game, index, ruler, successor, strength, year);
                faction.Ruler = ruler = successor;
                faction.RulerCount++;
            }

            if (faction.Grievance > government.Tolerance && dice.Random() < RevolutionChance)
            {
                var successor = NewRuler(game, "chosen", index, ruler, year);
                string text = string.Format("revolution among the {0}: {1} is overthrown after {2}; {3} rules (age {4}{5})",
                    faction.Name, ruler.Name, YearsWord(year - ruler.Since), successor.Name, successor.Age,
                    Describe(game, successor, faction));
                var facts = new Dictionary<string, string>
                {
                    ["faction"] = faction.Name, ["ruler"] = ruler.Name, ["years"] = (year - ruler.Since).ToString(),
                    ["successor"] = successor.Name,
                };
                string what = "revolution";
                faction.Ruler = successor;
                faction.RulerCount++;
                faction.Revolutions++;
                faction.Grievance *= RevolutionRelief;
                if (government.AfterRevolution.Count > 0)
                {
                    string next = dice.Choice(government.AfterRevolution);
                    if (next != faction.Government)
                    {
                        text += "; the government is now " + next;
                        faction.Government = next;
                        what = "revolution_new_government";
                        facts["government"] = next;
                    }
                }
                game.Record(new GameEvent(year, "revolution", what, text, new[] { index }, facts));
                var fit = PathFit(game, faction.Values);
                string best = BestHeld(game, faction, faction.Values);
                if (faction.Path != null && best != faction.Path && fit[best] - fit[faction.Path] >= PathMargin
                    && dice.Random() < RevolutionPathChance)
                    ChangePath(game, index, best, year, "after the revolution", null);
            }
        }
    }

    /// <summary>What a strong ruler's death leaves behind: a fight, a cult, or a near split. leaders.vacuum.</summary>
    static void Vacuum(Game game, int index, Ruler ruler, Ruler successor, double strength, int year)
    {
        var faction = game.Factions[index];
        string outcome = game.DiceFor("rulers").Choice(new[] { "fight", "cult", "split" });
        string text, what;
        if (outcome == "fight")
        {
            faction.Grievance += SuccessionFight * strength;
            successor.Traits["charisma"] *= WeakerSuccessor;
            successor.Traits["competence"] *= WeakerSuccessor;
            text = "a fight over " + ruler.Name + "'s succession weakens the " + faction.Name;
            what = "vacuum_fight";
        }
        else if (outcome == "cult")
        {
            faction.Cult = new Cult { Traits = new Dictionary<string, double>(ruler.Traits), Until = year + CultYears,
                                      Strength = strength, Name = ruler.Name };
            text = "a cult of " + ruler.Name + "'s memory grows among the " + faction.Name;
            what = "vacuum_cult";
        }
        else
        {
            faction.SplitPending = true;
            text = "the " + faction.Name + " nearly split after " + ruler.Name + "'s death";
            what = "vacuum_near_split";
        }
        game.Record(new GameEvent(year, "vacuum", what, text, new[] { index }, new Dictionary<string, string>
        {
            ["faction"] = faction.Name, ["ruler"] = ruler.Name, ["successor"] = successor.Name,
        }));
    }

    public static string YearsWord(int count) => count + (count == 1 ? " year" : " years");
}
