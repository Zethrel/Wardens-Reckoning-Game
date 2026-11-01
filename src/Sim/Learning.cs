// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Paths as skills (the generator's north/learning.py; your settings are in learning_settings.py). Every faction
/// keeps its main path (`Path`) and holds learned paths (`Held`), worked out at the end of each year:
///
/// 1. Welcome: a faction holding nothing yet (one that has just risen) gets its START_PATHS.
/// 2. Conquest: for every town taken this year, the conqueror may learn each path the loser held.
/// 3. Contact: for each path a faction does not hold, every way it is in touch with a faction that holds it
///    (trade, alliance, a shared border) adds its chance; one roll for the sum.
/// 4. Effort: a small chance of working a path out alone, more for a path that suits its people.
/// 5. Forgetting: a learned path no faction in touch holds may be forgotten.
/// 6. Skills: what its towns make besides their main path's goods (`Skills`, read by Trade.MakeMarkets): each
///    learned path's goods at LEARNED_SKILL, and the goods of each hybrid it holds both halves of.
///
/// Learning has its own dice ("learning"). With PATHS_AS_SKILLS off there is no learning at all.
/// </summary>
public static class Learning
{
    /// <summary>What a year of learning needs to know of how the year began: the paths each faction held, and who
    /// held each town (null for a ruin). learning.Learning.remember, done as the year begins.</summary>
    public sealed record Start(IReadOnlyDictionary<int, HashSet<string>> Before, IReadOnlyList<int?> Holder);

    /// <summary>The ways of being in touch, in the order the generator tries them (learning.CONTACTS).</summary>
    static readonly string[] Ways = { "trade", "alliance", "border" };

    static double ChanceOf(GameSettings settings, string way) => way switch
    {
        "trade" => settings.LearnByTrade,
        "alliance" => settings.LearnByAlliance,
        _ => settings.LearnByBorder,
    };

    /// <summary>The path a path grew from: itself, or an offshoot's root. learning.family.</summary>
    public static string Family(GameSettings settings, string path) => settings.PathRoot.GetValueOrDefault(path, path);

    /// <summary>Whether a faction holds a path: its main path or a learned one. learning.holds.</summary>
    public static bool Holds(FactionNow faction, string path) =>
        path == faction.Path || (faction.Held?.Contains(path) ?? false);

    /// <summary>Who holds what as the year begins (call it before anything else in the year).</summary>
    public static Start Remember(Game game) => new(
        Enumerable.Range(0, game.Factions.Count).ToDictionary(f => f, f =>
        {
            var held = new HashSet<string>(game.Factions[f].Held ?? new HashSet<string>());
            if (game.Factions[f].Path is string path)
                held.Add(path);
            return held;
        }),
        game.Towns.Select(t => t.Ruined ? null : t.Holder).ToList());

    /// <summary>
    /// Every faction's skills and hybrids worked out again without telling anything (for a world just opened or a
    /// save just loaded): the generator's are the same, in the same order, as nothing has changed what a faction
    /// holds since its last year of learning.
    /// </summary>
    public static void Settle(Game game)
    {
        if (!game.Settings.PathsAsSkills)
            return;
        foreach (var faction in game.Factions)
            if (faction.Held != null)
            {
                faction.Unlocked = Unlock(game.Settings, faction);
                faction.Skills = SkillsOf(game.Settings, faction, faction.Unlocked);
            }
    }

    /// <summary>One year of learning: steps 1 to 6 for every faction (learning.Learning.year). `start` is what
    /// Remember gave as the year began, `deals` the year's trade (good, seller town, buyer town, amount).</summary>
    public static void Year(Game game, Start start, IReadOnlyList<(string Good, int Seller, int Buyer, double Amount)> deals,
                            int year)
    {
        var settings = game.Settings;
        if (!settings.PathsAsSkills)
            return;
        var dice = game.DiceFor("learning");
        foreach (var faction in game.Factions)
            Welcome(game, faction);
        Conquests(game, start, dice, year);
        var touch = Contacts(game, deals);
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            if (faction.People <= 0)
                continue;
            var mine = touch.GetValueOrDefault(f) ?? new Dictionary<string, SortedSet<int>>();
            ByContact(game, f, mine, dice, year);
            ByEffort(game, f, dice, year);
            Forget(game, f, mine, dice, year);
            UpdateSkills(game, f, year);
        }
    }

    /// <summary>Step 1: a faction that holds nothing yet gets its START_PATHS, those of another family than its main
    /// path's (learning.Learning.welcome).</summary>
    static void Welcome(Game game, FactionNow faction)
    {
        if (faction.Held != null)
            return;
        var start = faction.Lineage != null ? game.Settings.StartPaths.GetValueOrDefault(faction.Lineage) : null;
        string? main = faction.Path == null ? null : Family(game.Settings, faction.Path);
        faction.Held = (start ?? new List<string>()).Where(p => Family(game.Settings, p) != main).ToHashSet();
        faction.Unlocked = Unlock(game.Settings, faction);
        faction.Skills = SkillsOf(game.Settings, faction, faction.Unlocked);
    }

    /// <summary>Step 2: the conqueror of each town taken this year may learn each path the loser held as the year
    /// began, LEARN_BY_CONQUEST for each (learning.Learning.conquests).</summary>
    static void Conquests(Game game, Start start, Dice dice, int year)
    {
        for (int t = 0; t < game.Towns.Count; t++)
        {
            var town = game.Towns[t];
            if (town.ConqueredIn != year || t >= start.Holder.Count)
                continue;
            if (start.Holder[t] is not int loser || town.Holder is not int winner || loser == winner)
                continue;
            var conqueror = game.Factions[winner];
            foreach (string path in Sorted(start.Before.GetValueOrDefault(loser) ?? new HashSet<string>()))
            {
                if (Holds(conqueror, path) || !game.Settings.PathSuits.ContainsKey(path))
                    continue;
                if (dice.Random() < game.Settings.LearnByConquest)
                    Gain(game, winner, path, "conquest", loser, year);
            }
        }
    }

    /// <summary>For each faction, the factions it is in touch with this year, by way (learning.Learning.contacts):
    /// the holders of the two towns of each deal, the allies, and the neighbours sharing a border.</summary>
    public static Dictionary<int, Dictionary<string, SortedSet<int>>> Contacts(
        Game game, IReadOnlyList<(string Good, int Seller, int Buyer, double Amount)> deals)
    {
        var touch = new Dictionary<int, Dictionary<string, SortedSet<int>>>();
        void Add(int? a, int? b, string way)
        {
            if (a is not int x || b is not int y || x == y)
                return;
            foreach (var (one, other) in new[] { (x, y), (y, x) })
            {
                if (!touch.TryGetValue(one, out var ways))
                    touch[one] = ways = new Dictionary<string, SortedSet<int>>();
                if (!ways.TryGetValue(way, out var set))
                    ways[way] = set = new SortedSet<int>();
                set.Add(other);
            }
        }
        foreach (var (_, seller, buyer, _) in deals)
            Add(game.Towns[seller].Holder, game.Towns[buyer].Holder, "trade");
        foreach (var (a, b) in game.Alliances)
            Add(a, b, "alliance");
        for (int f = 0; f < game.Factions.Count; f++)
            foreach (var (other, cells) in game.Factions[f].Border)
                if (cells > 0)
                    Add(f, other, "border");
        return touch;
    }

    /// <summary>Step 3: learn a path from the factions in touch, one roll for each path at the sum of the chances of
    /// every way it is offered (learning.Learning.by_contact). The teacher named is the first, by way and number.</summary>
    static void ByContact(Game game, int f, Dictionary<string, SortedSet<int>> touch, Dice dice, int year)
    {
        var faction = game.Factions[f];
        var offered = new SortedDictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (string way in Ways)
            foreach (int other in touch.GetValueOrDefault(way) ?? new SortedSet<int>())
            {
                var teacher = game.Factions[other];
                if (teacher.People <= 0)
                    continue;
                var theirs = new HashSet<string>(teacher.Held ?? new HashSet<string>());
                if (teacher.Path != null)
                    theirs.Add(teacher.Path);
                foreach (string path in Sorted(theirs))
                {
                    if (Holds(faction, path) || !game.Settings.PathSuits.ContainsKey(path))
                        continue;
                    if (!offered.TryGetValue(path, out var ways))
                        offered[path] = ways = new Dictionary<string, int>();
                    ways.TryAdd(way, other);
                }
            }
        foreach (var (path, ways) in offered)
        {
            double chance = Py.Sum(Ways.Where(ways.ContainsKey).Select(w => ChanceOf(game.Settings, w)));
            if (chance > 0 && dice.Random() < chance)
            {
                string way = Ways.First(ways.ContainsKey);
                Gain(game, f, path, way, ways[way], year);
            }
        }
    }

    /// <summary>Step 4: work a path out alone, LEARN_BY_EFFORT times how well it suits the people (one roll for each
    /// path it does not hold; learning.Learning.by_effort).</summary>
    static void ByEffort(Game game, int f, Dice dice, int year)
    {
        double chance = game.Settings.LearnByEffort;
        if (chance <= 0)
            return;
        var faction = game.Factions[f];
        var fit = Rulers.PathFit(game, faction.Values.Count > 0 ? faction.Values : faction.Traits);
        foreach (string path in Sorted(game.Settings.PathSuits.Keys))
        {
            if (Holds(faction, path))
                continue;
            if (dice.Random() < chance * fit.GetValueOrDefault(path))
                Gain(game, f, path, "effort", null, year);
        }
    }

    /// <summary>Step 5: a learned path that no living faction in touch holds is forgotten with FORGET_CHANCE
    /// (learning.Learning.forget).</summary>
    static void Forget(Game game, int f, Dictionary<string, SortedSet<int>> touch, Dice dice, int year)
    {
        double chance = game.Settings.ForgetChance;
        if (chance <= 0)
            return;
        var faction = game.Factions[f];
        var near = touch.Values.SelectMany(set => set).ToHashSet();
        foreach (string path in Sorted(faction.Held!))
        {
            if (near.Any(other => Holds(game.Factions[other], path) && game.Factions[other].People > 0))
                continue;
            if (dice.Random() < chance)
            {
                faction.Held!.Remove(path);
                game.Record(new GameEvent(year, "path_learning", "path_forgotten",
                    "the " + faction.Name + " forget the ways of " + path, new[] { f },
                    new Dictionary<string, string> { ["faction"] = faction.Name, ["path"] = path }));
            }
        }
    }

    /// <summary>A faction learns a path, and the book tells it (learning.Learning.gain).</summary>
    static void Gain(Game game, int f, string path, string how, int? teacher, int year)
    {
        var faction = game.Factions[f];
        faction.Held!.Add(path);
        var facts = new Dictionary<string, string> { ["faction"] = faction.Name, ["path"] = path };
        string text = "the " + faction.Name + " learn " + path;
        if (teacher is int t)
        {
            facts["teacher"] = game.Factions[t].Name;
            text += " from the " + game.Factions[t].Name + " (" + how + ")";
        }
        else
            text += " by their own effort";
        game.Record(new GameEvent(year, "path_learning", "path_learned_" + how, text,
                                  teacher is int by ? new[] { f, by } : new[] { f }, facts));
    }

    /// <summary>Step 6: the faction's skills and hybrids, and the news of each hybrid it holds both halves of for the
    /// first time (learning.Learning.update_skills).</summary>
    static void UpdateSkills(Game game, int f, int year)
    {
        var faction = game.Factions[f];
        var unlocked = Unlock(game.Settings, faction);
        foreach (string name in Sorted(unlocked))
        {
            if (faction.Unlocked.Contains(name))
                continue;
            var (first, second) = game.Settings.Hybrids[name];
            string shown = name.Replace("_", " ");                // threshold_physic is told as "threshold physic"
            game.Record(new GameEvent(year, "path_learning", "hybrid_unlocked",
                "the " + faction.Name + " join " + first + " and " + second + " into " + shown, new[] { f },
                new Dictionary<string, string>
                {
                    ["faction"] = faction.Name, ["hybrid"] = shown, ["first"] = first, ["second"] = second,
                }));
        }
        faction.Unlocked = unlocked;
        faction.Skills = SkillsOf(game.Settings, faction, unlocked);
    }

    /// <summary>The hybrids whose two halves are both among the families of the paths a faction holds.</summary>
    static HashSet<string> Unlock(GameSettings settings, FactionNow faction)
    {
        var owned = (faction.Held ?? new HashSet<string>()).Select(p => Family(settings, p)).ToHashSet();
        if (faction.Path != null)
            owned.Add(Family(settings, faction.Path));
        return settings.Hybrids.Where(h => owned.Contains(h.Value.First) && owned.Contains(h.Value.Second))
                               .Select(h => h.Key).ToHashSet();
    }

    /// <summary>
    /// What a faction makes besides its main path's goods: each learned path of another family at LEARNED_SKILL, in
    /// order of name, then each hybrid at full rate, in order of name. The order is the generator's, so that the
    /// goods add up to the last digit.
    /// </summary>
    static Dictionary<string, double> SkillsOf(GameSettings settings, FactionNow faction, HashSet<string> unlocked)
    {
        string? main = faction.Path == null ? null : Family(settings, faction.Path);
        var skills = new Dictionary<string, double>();
        foreach (string path in Sorted(faction.Held ?? new HashSet<string>()))
            if (Family(settings, path) != main && settings.LearnedSkill > 0)
                skills[Family(settings, path)] = settings.LearnedSkill;
        foreach (string name in Sorted(unlocked))
            skills[name] = 1.0;
        return skills;
    }

    /// <summary>Python's sorted() for names: by the characters' codes.</summary>
    static List<string> Sorted(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal).ToList();
}
