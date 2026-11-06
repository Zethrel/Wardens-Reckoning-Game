// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WardensReckoning.Colony;

/// <summary>Content that cannot be used: every problem found, each naming its file, its place and what is wrong.</summary>
public sealed class ContentException(IReadOnlyList<string> problems)
    : Exception("The colony's content has " + problems.Count + " problem" + (problems.Count == 1 ? "" : "s") + ":\n"
                + string.Join("\n", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>One kind of goods: how heavy and how big one unit is.</summary>
public sealed record Good(string Id, string Name, double MassKg, double VolumeL);

/// <summary>A kind of workshop (a hand quern...).</summary>
public sealed record Workshop(string Id, string Name);

/// <summary>A recipe: the workshop that works it, what goes in and comes out (whole units), and how long it takes.</summary>
public sealed record Recipe(string Id, string Name, string Workshop, IReadOnlyDictionary<string, int> Inputs,
                            IReadOnlyDictionary<string, int> Outputs, int WorkMinutes);

/// <summary>
/// The colony's content (the design library calls it "content definitions"): the goods, workshops and recipes the
/// colony knows, and its everyday numbers, read from the JSON files in `content/` (see content/README.md).
///
/// Everything is checked before the colony may use it (FP-10): broken JSON, a missing or misspelt key, a number
/// that is not allowed, two things with the same id, a recipe naming a good or workshop that does not exist, or a
/// good made from itself. All problems are gathered and reported together, so one run shows every mistake.
/// </summary>
public sealed class Content
{
    /// <summary>The shape of the content files this game reads.</summary>
    public const int Version = 1;

    public IReadOnlyDictionary<string, Good> Goods { get; }
    public IReadOnlyDictionary<string, Workshop> Workshops { get; }
    public IReadOnlyDictionary<string, Recipe> Recipes { get; }
    /// <summary>What one person carries by hand, in kg.</summary>
    public double CarryKg { get; }
    /// <summary>Walking speed, in metres a minute (a tick).</summary>
    public double WalkMetresPerMinute { get; }
    public int PickupMinutes { get; }
    public int DropMinutes { get; }

    Content(Dictionary<string, Good> goods, Dictionary<string, Workshop> workshops, Dictionary<string, Recipe> recipes,
            double carryKg, double walk, int pickup, int drop)
    {
        Goods = new SortedDictionary<string, Good>(goods, StringComparer.Ordinal);
        Workshops = new SortedDictionary<string, Workshop>(workshops, StringComparer.Ordinal);
        Recipes = new SortedDictionary<string, Recipe>(recipes, StringComparer.Ordinal);
        (CarryKg, WalkMetresPerMinute, PickupMinutes, DropMinutes) = (carryKg, walk, pickup, drop);
    }

    /// <summary>Reads and checks the content in `folder` (goods.json, recipes.json, colony.json).</summary>
    public static Content Load(string folder)
    {
        var texts = new Dictionary<string, string?>();
        foreach (string name in new[] { "goods.json", "recipes.json", "colony.json" })
        {
            string path = Path.Combine(folder, name);
            texts[name] = File.Exists(path) ? File.ReadAllText(path) : null;
        }
        return Read(texts);
    }

    static readonly Regex IdShape = new("^[a-z][a-z0-9_]*$");

    /// <summary>Checks content given as file name to text (null: the file is missing).</summary>
    public static Content Read(IReadOnlyDictionary<string, string?> files)
    {
        var problems = new List<string>();
        var check = new Checker(problems);

        JsonObject? Open(string file, params string[] keys)
        {
            string? text = files.GetValueOrDefault(file);
            if (text == null)
            {
                problems.Add(file + ": the file is missing");
                return null;
            }
            JsonObject? root;
            try
            {
                root = JsonNode.Parse(text) as JsonObject;
            }
            catch (JsonException e)
            {
                problems.Add(file + ": not valid JSON (" + e.Message + ")");
                return null;
            }
            if (root == null)
            {
                problems.Add(file + ": should be one JSON object { ... }");
                return null;
            }
            check.Keys(root, file, keys.Append("version").ToArray());
            if (check.Int(root, file, "version", 0) is int v && v != Version)
                problems.Add(file + ": version " + v + "; this game reads version " + Version);
            return root;
        }

        // Goods.
        var goods = new Dictionary<string, Good>();
        if (Open("goods.json", "goods") is JsonObject g)
            foreach (var (item, at) in check.List(g, "goods.json", "goods"))
            {
                check.Keys(item, at, "id", "name", "mass_kg", "volume_l");
                string? id = check.Id(item, at, IdShape);
                string? name = check.Text(item, at, "name");
                double? mass = check.Positive(item, at, "mass_kg");
                double? volume = check.Positive(item, at, "volume_l");
                if (id == null)
                    continue;
                if (goods.ContainsKey(id))
                    problems.Add(at + ": a second good with the id \"" + id + "\"");
                else if (name != null && mass != null && volume != null)
                    goods[id] = new Good(id, name, mass.Value, volume.Value);
            }

        // Workshops and recipes.
        var workshops = new Dictionary<string, Workshop>();
        var recipes = new Dictionary<string, Recipe>();
        if (Open("recipes.json", "workshops", "recipes") is JsonObject r)
        {
            foreach (var (item, at) in check.List(r, "recipes.json", "workshops"))
            {
                check.Keys(item, at, "id", "name");
                string? id = check.Id(item, at, IdShape);
                string? name = check.Text(item, at, "name");
                if (id == null)
                    continue;
                if (workshops.ContainsKey(id))
                    problems.Add(at + ": a second workshop with the id \"" + id + "\"");
                else if (name != null)
                    workshops[id] = new Workshop(id, name);
            }
            foreach (var (item, at) in check.List(r, "recipes.json", "recipes"))
            {
                check.Keys(item, at, "id", "name", "workshop", "inputs", "outputs", "work_minutes");
                string? id = check.Id(item, at, IdShape);
                string? name = check.Text(item, at, "name");
                string? workshop = check.Text(item, at, "workshop");
                if (workshop != null && !workshops.ContainsKey(workshop))
                    problems.Add(at + ".workshop: no workshop \"" + workshop + "\"");
                var inputs = check.Amounts(item, at, "inputs", goods);
                var outputs = check.Amounts(item, at, "outputs", goods);
                int? minutes = check.Int(item, at, "work_minutes", 1);
                if (id == null)
                    continue;
                if (recipes.ContainsKey(id))
                    problems.Add(at + ": a second recipe with the id \"" + id + "\"");
                else if (name != null && workshop != null && inputs != null && outputs != null && minutes != null)
                    recipes[id] = new Recipe(id, name, workshop, inputs, outputs, minutes.Value);
            }
            foreach (string loop in Loops(recipes.Values))
                problems.Add("recipes.json: " + loop);
        }

        // Everyday numbers.
        double carry = 0, walk = 0;
        int pickup = 0, drop = 0;
        if (Open("colony.json", "carry_kg", "walk_m_per_minute", "pickup_minutes", "drop_minutes") is JsonObject c)
        {
            carry = check.Positive(c, "colony.json", "carry_kg") ?? 0;
            walk = check.Positive(c, "colony.json", "walk_m_per_minute") ?? 0;
            pickup = check.Int(c, "colony.json", "pickup_minutes", 0) ?? 0;
            drop = check.Int(c, "colony.json", "drop_minutes", 0) ?? 0;
        }

        if (problems.Count > 0)
            throw new ContentException(problems);
        return new Content(goods, workshops, recipes, carry, walk, pickup, drop);
    }

    /// <summary>
    /// Every good that can be made from itself, through one recipe or a chain of them ("prohibited dependency cycles"
    /// in the library): a good leads to every good a recipe makes from it, and none may lead back to itself.
    /// </summary>
    static IEnumerable<string> Loops(IEnumerable<Recipe> recipes)
    {
        var leadsTo = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var recipe in recipes.OrderBy(x => x.Id, StringComparer.Ordinal))
            foreach (string input in recipe.Inputs.Keys)
                foreach (string output in recipe.Outputs.Keys)
                {
                    if (!leadsTo.TryGetValue(input, out var set))
                        leadsTo[input] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(output);
                }
        foreach (string start in leadsTo.Keys)
        {
            // Walk every good reachable from `start`; if `start` is among them, it is made from itself.
            var seen = new HashSet<string>();
            var waiting = new Stack<string>(leadsTo[start]);
            while (waiting.Count > 0)
            {
                string good = waiting.Pop();
                if (good == start)
                {
                    yield return "\"" + start + "\" is made from itself (through the recipes)";
                    break;
                }
                if (seen.Add(good) && leadsTo.TryGetValue(good, out var next))
                    foreach (string n in next)
                        waiting.Push(n);
            }
        }
    }

    /// <summary>Small checks of one JSON object's parts, each adding a problem when something is wrong.</summary>
    sealed class Checker(List<string> problems)
    {
        public void Keys(JsonObject item, string at, params string[] allowed)
        {
            foreach (var (key, _) in item)
                if (!allowed.Contains(key))
                    problems.Add(at + ": unknown key \"" + key + "\" (allowed: " + string.Join(", ", allowed) + ")");
            foreach (string key in allowed.Where(k => !item.ContainsKey(k)))
                problems.Add(at + ": \"" + key + "\" is missing");
        }

        public IEnumerable<(JsonObject Item, string At)> List(JsonObject root, string file, string key)
        {
            if (root[key] is not JsonArray list)
            {
                if (root.ContainsKey(key))
                    problems.Add(file + "." + key + ": should be a list [ ... ]");
                yield break;
            }
            for (int i = 0; i < list.Count; i++)
                if (list[i] is JsonObject o)
                    yield return (o, file + "." + key + "[" + i + "]");
                else
                    problems.Add(file + "." + key + "[" + i + "]: should be an object { ... }");
        }

        public string? Id(JsonObject item, string at, Regex shape)
        {
            string? id = Text(item, at, "id");
            if (id != null && !shape.IsMatch(id))
            {
                problems.Add(at + ".id: \"" + id + "\" should be lowercase letters, digits and _, starting with a letter");
                return null;
            }
            return id;
        }

        public string? Text(JsonObject item, string at, string key)
        {
            if (!item.ContainsKey(key))
                return null;                         // reported by Keys
            if (item[key] is JsonValue v && v.TryGetValue(out string? s) && s.Trim().Length > 0)
                return s;
            problems.Add(at + "." + key + ": should be text that is not empty");
            return null;
        }

        public double? Positive(JsonObject item, string at, string key)
        {
            if (!item.ContainsKey(key))
                return null;
            if (item[key] is JsonValue v && v.TryGetValue(out double d) && d > 0 && double.IsFinite(d))
                return d;
            problems.Add(at + "." + key + ": should be a number above 0");
            return null;
        }

        public int? Int(JsonObject item, string at, string key, int least)
        {
            if (!item.ContainsKey(key))
                return null;
            if (item[key] is JsonValue v && v.TryGetValue(out int n) && n >= least)
                return n;
            problems.Add(at + "." + key + ": should be a whole number of " + least + " or more");
            return null;
        }

        /// <summary>A table of good id to whole units (1 or more), every good known.</summary>
        public IReadOnlyDictionary<string, int>? Amounts(JsonObject item, string at, string key, Dictionary<string, Good> goods)
        {
            if (!item.ContainsKey(key))
                return null;
            if (item[key] is not JsonObject table || table.Count == 0)
            {
                problems.Add(at + "." + key + ": should be a table of goods and amounts, not empty");
                return null;
            }
            var amounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            bool ok = true;
            foreach (var (good, amount) in table)
            {
                if (!goods.ContainsKey(good))
                {
                    problems.Add(at + "." + key + "." + good + ": no good \"" + good + "\"");
                    ok = false;
                }
                if (amount is JsonValue v && v.TryGetValue(out int n) && n >= 1)
                    amounts[good] = n;
                else
                {
                    problems.Add(at + "." + key + "." + good + ": should be a whole number of 1 or more");
                    ok = false;
                }
            }
            return ok ? amounts : null;
        }
    }
}
