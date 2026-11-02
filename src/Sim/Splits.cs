// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.World;

namespace WardensReckoning.Sim;

/// <summary>
/// Splits and merging (the generator's north/splits.py, with how new factions differ from their kin, north/newcomers.py).
///
/// A part of a faction can break away and become a faction of its own, of the same people, for three reasons:
/// - division: its people are aggrieved but have not risen, or their ruler's ways are far from theirs;
/// - distance: a town FAR or more from the capital along the routes drifts away (a colony leaves in peace);
/// - behind a leader: when a strong ruler dies and the faction "nearly splits" (Rulers.cs), it may really split.
/// What breaks away is a whole far province: the town where it starts, and the faction's towns further out along
/// the routes. The capital always stays. A town once taken from another people breaks away as that people's.
///
/// Factions can also merge (two small, friendly neighbours on the same path), a small faction may join an empire
/// next to it, and an empire (EMPIRE_SHARE of all the people or more) holds together better (its hold).
/// </summary>
public static class Splits
{
    /// <summary>The traits every faction has, and the four more a people may have (factions.TRAITS, EXTRA_TRAITS).</summary>
    public static readonly string[] Traits = { "planning", "risk", "greed" };
    public static readonly string[] ExtraTraits = { "war_readiness", "cohesion", "faith", "fertility" };

    /// <summary>The beliefs about the god of death, in the generator's order (death.BELIEFS).</summary>
    public static readonly string[] Beliefs = { "fear", "worship", "deny", "unknowing" };

    /// <summary>What a faction that broke away and the one it left remember of each other; and a colony that went its
    /// own way in peace and its homeland.</summary>
    public const double Bitter = -0.4;
    public const double Warm = 0.4;

    /// <summary>A faction this small (in towns) may merge with a friendly neighbour; the chance in a year that two
    /// ready to merge do.</summary>
    public const int Small = 3;
    public const double MergeChance = 0.2;

    /// <summary>The lowest and highest a faction's fragility can be.</summary>
    public const double FragilityLow = 0.25, FragilityHigh = 1.75;

    // What each cause of a split is called among the events.
    public static readonly Dictionary<string, string> SplitKinds = new()
    {
        ["behind a rival to the throne"] = "split_leader", ["divided from their rulers"] = "split_division",
        ["too far from the capital"] = "split_distance",
    };

    static Dice DiceOf(Game game) => game.DiceFor("splits");

    /// <summary>The towns a faction holds that are not ruins, in order. splits.holdings.</summary>
    public static List<int> Holdings(Game game, int faction) =>
        Enumerable.Range(0, game.Towns.Count).Where(t => game.Towns[t].Holder == faction && !game.Towns[t].Ruined).ToList();

    /// <summary>Its capital if it still holds it; otherwise its largest town (the lower number on a tie), which
    /// becomes the capital; null with no town. splits.capital_of.</summary>
    public static int? CapitalOf(Game game, int faction)
    {
        var held = Holdings(game, faction);
        if (held.Count == 0)
            return null;
        var f = game.Factions[faction];
        if (f.Capital is not int capital || !held.Contains(capital))
            f.Capital = held.OrderByDescending(t => game.Towns[t].Population).ThenBy(t => t).First();
        return f.Capital;
    }

    /// <summary>The town `start` and its fellow towns further out from the capital along the routes (`distances` is
    /// each town's journey from the capital): the far side of the faction. splits.province.</summary>
    public static List<int> Province(Game game, int start, int faction, IReadOnlyDictionary<int, double> distances)
    {
        var found = new HashSet<int> { start };
        var waiting = new Stack<int>();
        waiting.Push(start);
        while (waiting.Count > 0)
        {
            int here = waiting.Pop();
            foreach (var route in game.Routes)
            {
                if (route.A != here && route.B != here)
                    continue;
                int there = route.A == here ? route.B : route.A;
                var town = game.Towns[there];
                if (found.Contains(there) || town.Ruined || town.Holder != faction)
                    continue;
                if (distances.GetValueOrDefault(there) > distances.GetValueOrDefault(here))
                {
                    found.Add(there);
                    waiting.Push(there);
                }
            }
        }
        return found.OrderBy(t => t).ToList();
    }

    /// <summary>How readily a faction breaks apart, by its nature: 1 + risk - planning, or 2 x (1 - cohesion) for a
    /// people with a cohesion; kept within FRAGILITY_RANGE. splits.fragility.</summary>
    public static double Fragility(FactionNow faction)
    {
        double value = faction.Traits.TryGetValue("cohesion", out double cohesion)
            ? 2.0 * (1.0 - cohesion)
            : 1.0 + faction.Traits["risk"] - faction.Traits["planning"];
        return Math.Min(FragilityHigh, Math.Max(FragilityLow, value));
    }

    /// <summary>How divided a faction's people are, 0 to 1: aggrieved without rising, or far from their ruler.
    /// splits.divided.</summary>
    public static double Divided(Game game, FactionNow faction)
    {
        double tolerance = game.Settings.Governments.TryGetValue(faction.Government, out var g) ? g.Tolerance : 0;
        double aggrieved = tolerance > 0 ? Math.Min(1.0, faction.Grievance / tolerance) : 0.0;
        double estranged = faction.Ruler != null ? Rulers.AgainstValues(faction.Ruler, faction.Values) : 0.0;
        return Math.Max(aggrieved, estranged);
    }

    /// <summary>How many factions hold land. splits.living.</summary>
    public static int Living(Game game) =>
        game.Towns.Where(t => !t.Ruined && t.Holder != null).Select(t => t.Holder).Distinct().Count();

    /// <summary>
    /// One year of splits by division, distance and leaders, then merging, then small factions joining empires.
    /// splits.year_of_splits (the era's temper, step 9, is 1 until then).
    /// </summary>
    public static void Year(Game game, List<YearCore.Hardship> hardship, int year)
    {
        var settings = game.Settings;
        var dice = DiceOf(game);
        double scale = 256.0 / game.HistoryColumns;
        int count = game.Factions.Count;          // the factions at the start of the year: a new one waits for next year
        for (int f = 0; f < count; f++)
        {
            var faction = game.Factions[f];
            var held = Holdings(game, f);
            if (held.Count < 2 || faction.Ruler == null)
            {
                faction.SplitPending = false;
                continue;
            }
            int capital = CapitalOf(game, f)!.Value;
            var distances = game.Journeys[capital].Cost.ToDictionary(p => p.Key, p => p.Value * scale);
            var outer = held.Where(t => t != capital)
                            .OrderByDescending(t => distances.GetValueOrDefault(t)).ThenBy(t => t).ToList();
            string? cause = null;
            int start = -1;
            double fragile = Fragility(faction) * faction.Hold;
            if (faction.SplitPending)
            {
                faction.SplitPending = false;
                if (dice.Random() < settings.LeaderSplit * fragile)
                    (cause, start) = ("behind a rival to the throne", outer[0]);
            }
            if (cause == null && dice.Random() < settings.Division * Divided(game, faction) * fragile)
                (cause, start) = ("divided from their rulers", outer[0]);
            bool peaceful = false;
            if (cause == null)
                // The nearest far town first: it takes the rest of the far side with it.
                for (int i = outer.Count - 1; i >= 0; i--)
                {
                    int town = outer[i];
                    if (distances.GetValueOrDefault(town) >= settings.Far && dice.Random() < settings.Distance * fragile)
                    {
                        (cause, start) = ("too far from the capital", town);
                        peaceful = game.Towns[town].FoundedFrom != null;
                        if (peaceful)
                            cause += ", in peace";
                        break;
                    }
                }
            if (cause == null || Living(game) >= settings.MaxFactions)
                continue;
            var part = Province(game, start, f, distances);
            Secede(game, part, f, cause, hardship, year, peaceful);
        }
        Merge(game, year);
        JoinEmpires(game, year);
    }

    /// <summary>Every faction's hold: EMPIRE_HOLD for an empire (at least EMPIRE_SHARE of all the people), else 1.
    /// splits.update_empires (run before the wars, as in the generator).</summary>
    public static void UpdateEmpires(Game game)
    {
        var people = new Dictionary<int, int>();
        foreach (var town in game.Towns)
            if (!town.Ruined && town.Holder is int h)
                people[h] = people.GetValueOrDefault(h) + town.Population;
        int total = people.Values.Sum();
        for (int f = 0; f < game.Factions.Count; f++)
            game.Factions[f].Hold = total > 0 && (double)people.GetValueOrDefault(f) / total >= game.Settings.EmpireShare
                ? game.Settings.EmpireHold : 1.0;
    }

    /// <summary>A small faction next to an empire, at peace with it and thinking well enough of it, may join it (one a
    /// year at most). splits.join_empires.</summary>
    public static void JoinEmpires(Game game, int year)
    {
        var settings = game.Settings;
        double chance = settings.EmpireJoin;
        if (chance <= 0)
            return;
        var dice = DiceOf(game);
        var empires = Enumerable.Range(0, game.Factions.Count)
            .Where(f => game.Factions[f].Hold < 1.0 && Holdings(game, f).Count > 0).ToList();
        foreach (int empire in empires)
            for (int small = 0; small < game.Factions.Count; small++)
            {
                var towns = Holdings(game, small);
                var smaller = game.Factions[small];
                if (small == empire || towns.Count == 0 || smaller.Hold < 1.0 || towns.Count > settings.EmpireSmall)
                    continue;
                if (War.Enemies(game, small, empire))
                    continue;
                if (smaller.Opinions.GetValueOrDefault(empire) < settings.EmpireJoinOpinion)
                    continue;
                if (War.Fronts(game, small, empire).Count == 0 || dice.Random() >= chance)
                    continue;
                Absorb(game, empire, small, towns);
                string a = smaller.Name, b = game.Factions[empire].Name;
                game.Record(new GameEvent(year, "merge", "joins_empire", "the " + a + " join the empire of the " + b,
                    new[] { small, empire }, new Dictionary<string, string> { ["smaller"] = a, ["larger"] = b }));
                return;
            }
    }

    /// <summary>`smaller` gives all its towns to `larger`, in peace, and is no more. splits.absorb.</summary>
    public static void Absorb(Game game, int larger, int smaller, List<int> towns)
    {
        foreach (int town in towns)
        {
            game.Towns[town].Holder = larger;
            game.Towns[town].Origin = larger;
            game.Towns[town].ConqueredIn = null;
        }
        game.Factions[larger].People += game.Factions[smaller].People;
        game.Factions[smaller].People = 0;
        game.Factions[smaller].Ruler = null;
        game.Alliances.RemoveWhere(pair => pair.Item1 == smaller || pair.Item2 == smaller);
        HandOver(game, towns, larger);
    }

    /// <summary>Gives `faction` the land and mines of `towns` (splits.hand_over); the borders are counted again.</summary>
    public static void HandOver(Game game, IEnumerable<int> towns, int faction)
    {
        var those = towns.ToHashSet();
        foreach (var mine in game.Mines)
            if (mine.LandTown is int t && those.Contains(t))
                mine.Owner = faction;
        for (int cell = 0; cell < game.Grid.Owner.Length; cell++)
            if (those.Contains(game.Grid.Owner[cell]))
                game.Regions[cell] = faction;
        game.BordersChanged = true;
    }

    /// <summary>Small, friendly neighbours on the same path join, the smaller into the larger (one pair a year at
    /// most). splits.merge.</summary>
    public static void Merge(Game game, int year)
    {
        var dice = DiceOf(game);
        var alive = Enumerable.Range(0, game.Factions.Count).Where(f => Holdings(game, f).Count > 0).ToList();
        foreach (int a in alive)
            foreach (int b in alive)
            {
                var first = game.Factions[a];
                var second = game.Factions[b];
                if (a >= b || first.Path != second.Path)
                    continue;
                if (Holdings(game, a).Count > Small || Holdings(game, b).Count > Small)
                    continue;
                if (War.Enemies(game, a, b))
                    continue;
                if (Math.Min(first.Opinions.GetValueOrDefault(b), second.Opinions.GetValueOrDefault(a)) < game.Settings.MergeOpinion)
                    continue;
                if (War.Fronts(game, a, b).Count == 0 || dice.Random() >= MergeChance)
                    continue;
                // The larger by people, the lower number on a tie.
                var (larger, smaller) = first.People >= second.People ? (a, b) : (b, a);
                var towns = Holdings(game, smaller);
                if (towns.Count == 0)
                    continue;
                string small = game.Factions[smaller].Name, large = game.Factions[larger].Name;
                Absorb(game, larger, smaller, towns);
                game.Record(new GameEvent(year, "merge", "merge", "the " + small + " join the " + large,
                    new[] { smaller, larger }, new Dictionary<string, string> { ["smaller"] = small, ["larger"] = large }));
                return;
            }
    }

    /// <summary>A name without a leading "The " (the game says "the Vernier Guild" itself). factions.without_article.</summary>
    public static string WithoutArticle(string name) => name.StartsWith("The ", StringComparison.Ordinal) ? name[4..] : name;

    /// <summary>
    /// The towns in `part` leave `parent` and become a new faction, which is returned (splits.secede). It is of the
    /// people who founded the first of them: usually the parent's own, but a town once taken from another people
    /// breaks away as theirs, with their ways. If a faction of the settings' FACTIONS of that people has not risen
    /// yet, it is the one that rises, with its own name, colour, path and traits. If it leaves `peaceful`ly it and
    /// its parent start warm instead of bitter. With `toldLater`, the caller tells it (a rising, War.Rebel): the
    /// events of how the new faction differs from its kin are put in `toldLater`, to be told after.
    /// </summary>
    public static int Secede(Game game, List<int> part, int parent, string cause, List<YearCore.Hardship> hardship,
                             int year, bool peaceful = false, List<GameEvent>? toldLater = null)
    {
        var settings = game.Settings;
        int index = game.Factions.Count;
        var mother = game.Factions[parent];
        int? founder = game.Towns[part[0]].Origin;
        int kinIndex = founder is int k && k != parent ? k : parent;
        var kin = game.Factions[kinIndex];
        var others = Enumerable.Range(0, index).Where(f => Holdings(game, f).Count > 0)
            .Select(f => ((int)game.World.Factions[f].Red, (int)game.World.Factions[f].Green, (int)game.World.Factions[f].Blue))
            .ToList();
        int rising = game.Waiting.FindIndex(w => w.Folk == kin.Folk);
        FactionNow child;
        FactionInfo info;
        var told = new List<GameEvent>();
        if (rising >= 0)
        {
            // A faction of FACTIONS of the same people that has not started yet rises: with its own name, colour,
            // path and traits, and the kin's government, belief and prejudices.
            var (key, _) = game.Waiting[rising];
            game.Waiting.RemoveAt(rising);
            var start = settings.Starts[key];
            child = new FactionNow
            {
                Name = WithoutArticle(start.Name), Path = start.Path, Folk = kin.Folk, Lineage = key,
                Traits = new Dictionary<string, double>(start.Traits),
                Values = new Dictionary<string, double>(start.Traits),
                Nature = Traits.ToDictionary(t => t, t => start.Traits[t]),
                Learned = new HashSet<string>(), Held = null, Skills = new Dictionary<string, double>(),
            };
            info = new FactionInfo(child.Name, start.Red, start.Green, start.Blue, key, start.Race, parent, year);
        }
        else
        {
            string name = NameFor(game, kin) ?? game.NextPeopleName(kin.Folk);
            var kinInfo = game.World.Factions[kinIndex];
            var (r, g, b) = Colours.KinColour((kinInfo.Red, kinInfo.Green, kinInfo.Blue), others);
            child = new FactionNow
            {
                Name = name, Path = kin.Path, Folk = kin.Folk, Lineage = kin.Lineage,
                Traits = new Dictionary<string, double>(kin.Traits),
                Values = new Dictionary<string, double>(kin.Values),
                Nature = new Dictionary<string, double>(kin.Nature),
                Learned = new HashSet<string>(kin.Learned),
                Held = kin.Held == null ? null : new HashSet<string>(kin.Held),
                Skills = new Dictionary<string, double>(kin.Skills),
                Unlocked = new HashSet<string>(kin.Unlocked),
            };
            info = new FactionInfo(name, (byte)r, (byte)g, (byte)b, kinInfo.Lineage, kinInfo.Race, parent, year);
        }
        child.Prejudice = new Dictionary<string, double>(kin.Prejudice);
        child.Government = kin.Government;
        child.Belief = kin.Belief;
        child.Capital = part[0];
        child.Parent = parent;
        child.Founded = year;
        if (rising < 0)
            Vary(game, child, year, told);
        child.People = part.Sum(t => game.Towns[t].Population);
        mother.People = Math.Max(0, mother.People - child.People);
        game.AddFaction(child, info, year);
        hardship.Add(new YearCore.Hardship());     // born this year: it has felt nothing yet

        for (int other = 0; other < index; other++)
        {
            var them = game.Factions[other];
            game.Memory[(index, other)] = game.Memory.GetValueOrDefault((parent, other));
            game.Memory[(other, index)] = game.Memory.GetValueOrDefault((other, parent));
            if (mother.Opinions.TryGetValue(other, out double mine))
                child.Opinions[other] = mine;
            if (them.Opinions.TryGetValue(parent, out double theirs))
                them.Opinions[index] = theirs;
        }
        double parting = peaceful ? Warm : Bitter;
        game.Memory[(index, parent)] = game.Memory[(parent, index)] = parting;
        child.Opinions[parent] = mother.Opinions[index] = parting;
        if (rising < 0)
            Settle(game, index, year, told);

        string succession = settings.Governments.TryGetValue(child.Government, out var government) ? government.Succession : "chosen";
        child.Ruler = Rulers.NewRuler(game, succession, index, null, year);
        child.RulerCount = 1;

        foreach (int town in part)
        {
            game.Towns[town].Holder = index;
            game.Towns[town].Origin = index;
            game.Towns[town].ConqueredIn = null;
        }
        HandOver(game, part, index);
        if (toldLater != null)
        {
            toldLater.AddRange(told);
            return index;
        }
        string what = peaceful ? "split_in_peace" : SplitKinds.GetValueOrDefault(cause.Split(',')[0], "split");
        string places = string.Join(", ", part.Select(t => game.World.Towns[t].Name));
        string settlements = part.Count + (part.Count == 1 ? " settlement" : " settlements");
        game.Record(new GameEvent(year, "split", what, string.Format("{0} break away from the {1}, {2}: the {3} ({4}), ruled by {5}",
            places, mother.Name, cause, child.Name, settlements, child.Ruler.Name), new[] { parent, index },
            new Dictionary<string, string>
            {
                ["places"] = places, ["parent"] = mother.Name, ["faction"] = child.Name,
                ["settlements"] = part.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ruler"] = child.Ruler.Name,
            }));
        foreach (var e in told)
            game.Record(e);
        return index;
    }

    /// <summary>The next unused name from SPLIT_NAMES for `kin`'s path, or null. newcomers.name_for.</summary>
    public static string? NameFor(Game game, FactionNow kin)
    {
        if (kin.Path == null || !game.Settings.SplitNames.TryGetValue(kin.Path, out var listed))
            return null;
        while (game.SplitNamesGiven.GetValueOrDefault(kin.Path) < listed.Count)
        {
            string name = WithoutArticle(listed[game.SplitNamesGiven.GetValueOrDefault(kin.Path)]);
            game.SplitNamesGiven[kin.Path] = game.SplitNamesGiven.GetValueOrDefault(kin.Path) + 1;
            if (game.UsedNames.Add(name))
                return name;
        }
        return null;
    }

    /// <summary>
    /// How a new faction may differ from its kin, by the settings (newcomers.vary), with its own dice: each trait
    /// (and the same trait of its values and nature) moved by up to TRAIT_SPREAD, another belief with BELIEF_CHANCE,
    /// another government with GOVERNMENT_CHANCE. A path of its own (OFFSHOOT_CHANCE) is not in the game yet (lesson 7).
    /// </summary>
    static void Vary(Game game, FactionNow child, int year, List<GameEvent> told)
    {
        var settings = game.Settings;
        var dice = game.DiceFor("newcomers");
        if (settings.TraitSpread > 0)
            foreach (string trait in child.Traits.Keys.OrderBy(t => t, StringComparer.Ordinal).ToList())
            {
                double change = dice.Uniform(-settings.TraitSpread, settings.TraitSpread);
                child.Traits[trait] = Math.Min(1.0, Math.Max(0.0, child.Traits[trait] + change));
                foreach (var kept in new[] { child.Values, child.Nature })
                    if (kept.TryGetValue(trait, out double value))
                        kept[trait] = Math.Min(1.0, Math.Max(0.0, value + change));
            }
        if (settings.BeliefChance > 0 && dice.Random() < settings.BeliefChance)
        {
            var others = Beliefs.Where(b => b != child.Belief).ToList();
            child.Belief = others[(int)(dice.Random() * others.Count)];
            told.Add(new GameEvent(year, "split", "split_new_belief",
                "the " + child.Name + " come to " + child.Belief + " the god of death, unlike their kin", new[] { game.Factions.Count },
                new Dictionary<string, string> { ["faction"] = child.Name, ["belief"] = child.Belief }));
        }
        if (settings.GovernmentChance > 0 && dice.Random() < settings.GovernmentChance)
        {
            var others = settings.Governments.Keys.Where(g => g != child.Government).ToList();
            if (others.Count > 0)
            {
                child.Government = others[(int)(dice.Random() * others.Count)];
                told.Add(new GameEvent(year, "split", "split_new_government",
                    "the " + child.Name + " choose to be ruled as a " + child.Government + ", unlike their kin",
                    new[] { game.Factions.Count },
                    new Dictionary<string, string> { ["faction"] = child.Name, ["government"] = child.Government }));
            }
        }
        // A path of its own (step 8c): with OFFSHOOT_CHANCE, branching from its kin's.
        if (settings.OffshootChance > 0 && child.Path != null && dice.Random() < settings.OffshootChance)
        {
            string parent = child.Path;
            child.Path = settings.FoundPath(parent, child.Name, () => dice.Uniform(-settings.PathSpread, settings.PathSpread));
            game.Founders.Add(game.Factions.Count);
            told.Add(new GameEvent(year, "path", "path_founded",
                "the " + child.Name + " found a path of their own, the " + child.Path + ", from the " + parent,
                new[] { game.Factions.Count },
                new Dictionary<string, string> { ["faction"] = child.Name, ["path"] = child.Path, ["parent"] = parent }));
        }
    }

    /// <summary>
    /// A faction that founded a path may take a stance towards all others (newcomers.settle), once its memories are
    /// set: one draw against STANCES' chances in order; it remembers every other faction by the stance's "memory"
    /// more (or less) kindly. Told after the split.
    /// </summary>
    static void Settle(Game game, int child, int year, List<GameEvent> told)
    {
        var stances = game.Settings.Stances;
        if (!game.Founders.Contains(child) || stances.Count == 0)
            return;
        double roll = game.DiceFor("newcomers").Random();
        (string Name, double Chance, double Memory)? stance = null;
        foreach (var candidate in stances)
        {
            if (roll < candidate.Chance)
            {
                stance = candidate;
                break;
            }
            roll -= candidate.Chance;
        }
        if (stance is not { } taken)
            return;
        for (int other = 0; other < game.Factions.Count; other++)
            if (other != child && game.Memory.TryGetValue((child, other), out double remembered))
                game.Memory[(child, other)] = Math.Min(1.0, Math.Max(-1.0, remembered + taken.Memory));
        string name = game.Factions[child].Name;
        told.Add(new GameEvent(year, "split", "split_stance", "the " + name + " are " + taken.Name + " towards all others",
            new[] { child }, new Dictionary<string, string> { ["faction"] = name, ["stance"] = taken.Name }));
    }
}
