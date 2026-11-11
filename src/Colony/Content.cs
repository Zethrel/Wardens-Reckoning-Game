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

/// <summary>How one need grows and when it matters (needs.json), on a scale from 0 (none) to 100 (the limit). A
/// need that never fells anyone (hygiene) has `"collapse_hours": null`: CollapseHours is then infinity.</summary>
public sealed record NeedRule(string Name, double PerHour, double AsleepPerHour, double Peckish, double Wants, double Urgent,
                              double Critical, double CollapseHours);

/// <summary>Eating or drinking: the good, how many points of the need one unit eases, and how long it takes.</summary>
public sealed record Consumption(string Good, double Eases, int Minutes);

/// <summary>Washing (step 19a, library Review 37: Daily Life §7.2): the litres of `Good` a wash at a wash basin
/// uses (at the bank of fresh water: none), the points of hygiene it eases, and how long it takes.</summary>
public sealed record Washing(string Good, int Litres, double Eases, int Minutes);

/// <summary>
/// Fouled water (step 19b, library Review 37: Sanitation and Environmental Contamination §10, the Stage 2 choice;
/// sanitation.json): the good fouled water is; how many days apart, on average, a natural source fouls, and for how
/// many days (from, to); the share of foulings that show a sign, and the signs; and boiling: the litres boiled at
/// once, the fuel and how much of it, the minutes it takes.
/// </summary>
public sealed record SanitationRules(string FouledGood, double DaysBetween, int LastsFrom, int LastsTo, double SignShare,
                                     IReadOnlyList<string> Signs, int BoilLitres, string Fuel, int FuelUnits, int BoilMinutes);

/// <summary>
/// An illness (step 19b, library Review 37: Healthcare §10 item 8; health.json): its name; the chance that one
/// exposure makes someone ill; the hours before it shows (from, to) and that it lasts (from, to); and while it does,
/// the share of their usual work speed, and how many times as fast thirst and fatigue grow.
/// </summary>
public sealed record IllnessRule(string Name, double Chance, int OnsetFrom, int OnsetTo, int LastsFrom, int LastsTo,
                                 double Work, double Thirst, double Fatigue);

/// <summary>
/// The water rules (step 19a, library Review 37; needs.json "water"): the litres one load carries; how much a
/// river or a lake has (more than a colony draws: topped up every hour); a brook's litres a day for each month
/// (January first); the months a cold biome's water freezes; the biomes that are cold; and the piece that is the
/// ice hole.
/// </summary>
public sealed record WaterRules(int LoadLitres, int PlentyLitres, IReadOnlyList<int> BrookByMonth, IReadOnlySet<int> FreezingMonths,
                                IReadOnlySet<string> ColdBiomes, string IceHole);

/// <summary>The five schedule blocks of the design library (Daily Life §4.2).</summary>
public enum Block
{
    Sleep,
    Work,
    Meals,
    Recreation,
    Flexible,
}

/// <summary>A schedule template: what each of the day's 24 hours is for.</summary>
public sealed record Schedule(string Id, string Name, IReadOnlyList<Block> Hours);

/// <summary>A recipe: the workshop that works it, what goes in and comes out (whole units), and how long it takes.</summary>
public sealed record Recipe(string Id, string Name, string Workshop, IReadOnlyDictionary<string, int> Inputs,
                            IReadOnlyDictionary<string, int> Outputs, int WorkMinutes);

/// <summary>A construction piece (construction.json): a wall, a door, a floor. It takes one tile; what it is made
/// of and how long building it takes; whether it blocks walking (a wall does, a door and a floor do not); and
/// whether it closes a room in (a wall and a door do, a floor does not).</summary>
public sealed record PieceKind(string Id, string Name, IReadOnlyDictionary<string, int> Materials, int WorkMinutes, bool Blocks,
                               bool Encloses = false)
{
    /// <summary>The walking speed on it (1 = a road's), or null: whatever is beneath it (step 15c).</summary>
    public double? Speed { get; init; }
    /// <summary>How many tiles it covers, across and down, from its top left tile (a well: 2 by 2).</summary>
    public int Width { get; init; } = 1;
    public int Height { get; init; } = 1;
    /// <summary>For a structure with a fixed footprint: the tile it is used and built from, counted from its top
    /// left tile (the well's is just below its left half), or null: worked from any tile beside it.</summary>
    public (int X, int Y)? Access { get; init; }
    /// <summary>Litres of water it gives each morning (a well), or 0.</summary>
    public int WaterPerDay { get; init; }
    /// <summary>Litres of water it holds, carried to it (step 19a: a water barrel, a wash basin), or 0.</summary>
    public int HoldsLitres { get; init; }
    /// <summary>Whether colonists wash at it (a wash basin).</summary>
    public bool WashPlace { get; init; }
    /// <summary>Whether it is the work of opening frozen water (the ice hole): it opens the water it is planned on,
    /// leaves nothing standing, and is planned by the colony itself, not from the build panel.</summary>
    public bool OpensIce { get; init; }
}

/// <summary>What the ground is (construction.json): grass, rough ground, a road. Walking on it goes at `Speed` of
/// a road's speed (1 = 100%).</summary>
public sealed record Surface(string Id, string Name, double Speed)
{
    /// <summary>Whether the surface can be walked on (and built on): water has a speed of 0 and cannot.</summary>
    public bool CanWalk => Speed > 0;
}

/// <summary>A block of one surface on the test ground.</summary>
public sealed record Patch(string Surface, int X, int Y, int Width, int Height);

/// <summary>A building template (construction.json): pieces laid out on tiles, from the top left (0, 0), to plan
/// in one go (library Construction §3.3). The player may still change it after planning.</summary>
public sealed record Template(string Id, string Name, int Width, int Height, IReadOnlyList<(string Piece, int X, int Y)> Pieces);

/// <summary>Who arrives to found the colony and what they bring (founding.json; library Review 33).</summary>
public sealed record FoundingRules(int Founders, IReadOnlyList<string> Names, IReadOnlyDictionary<string, int> Supplies, bool BedrollEach);

/// <summary>Something in the way on the ground (a rock, a tree), on one tile.</summary>
public sealed record Obstacle(string Kind, int X, int Y);

/// <summary>The flat test ground the colony stands on until the world generator makes local terrain (Review 32):
/// its size in tiles (one tile is 1 m square) and what is in the way.</summary>
public sealed record TestGround(int Width, int Height, IReadOnlyList<Obstacle> Obstacles)
{
    /// <summary>The surface everywhere (step 15c), except where a patch says otherwise.</summary>
    public string Surface { get; init; } = "";
    public IReadOnlyList<Patch> Patches { get; init; } = Array.Empty<Patch>();
}

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
    /// <summary>Time to load a haul, and to unload it, in seconds (step 21c).</summary>
    public int PickupSeconds { get; }
    public int DropSeconds { get; }
    /// <summary>How often a colonist thinks (step 21c): every so many seconds, drawn once for each colonist from
    /// `ThinkFrom` to `ThinkTo` (colony.json think_seconds).</summary>
    public int ThinkFrom { get; private init; } = 1;
    public int ThinkTo { get; private init; } = 1;

    /// <summary>The needs: "hunger", "thirst", "fatigue", "hygiene".</summary>
    public IReadOnlyDictionary<string, NeedRule> Needs { get; private init; } = new Dictionary<string, NeedRule>();
    public NeedRule Hunger => Needs["hunger"];
    public NeedRule Thirst => Needs["thirst"];
    public NeedRule Fatigue => Needs["fatigue"];
    public NeedRule Hygiene => Needs["hygiene"];
    public Consumption Eat { get; private init; } = new("", 0, 0);
    public Consumption Drink { get; private init; } = new("", 0, 0);
    public Washing Wash { get; private init; } = new("", 0, 0, 0);
    public WaterRules Water { get; private init; } = new(1, 1, Array.Empty<int>(), new HashSet<int>(), new HashSet<string>(), "");
    /// <summary>Fouled water and boiling (sanitation.json, step 19b).</summary>
    public SanitationRules Sanitation { get; private init; } = new("", 1, 1, 1, 0, Array.Empty<string>(), 1, "", 1, 1);
    /// <summary>The upset stomach (health.json, step 19b): the illness fouled water gives.</summary>
    public IllnessRule UpsetStomach { get; private init; } = new("", 0, 0, 0, 0, 0, 1, 1, 1);

    /// <summary>The goods a colonist drinks: water, and fouled water, which looks the same (step 19b).</summary>
    public IReadOnlyList<string> Drinkable => Sanitation.FouledGood == "" ? new[] { Drink.Good } : new[] { Drink.Good, Sanitation.FouledGood };
    /// <summary>Points of fatigue an hour of sleep eases, on a bedroll and on the bare ground.</summary>
    public double BedrollPerHour { get; private init; }
    public double GroundPerHour { get; private init; }
    /// <summary>Litres the spring has each morning (a stand-in for rivers and wells).</summary>
    public int SpringPerDay { get; private init; }
    public IReadOnlyDictionary<string, Schedule> Schedules { get; private init; } = new Dictionary<string, Schedule>();
    public string DefaultSchedule { get; private init; } = "";
    /// <summary>The construction pieces, by id (step 15a).</summary>
    public IReadOnlyDictionary<string, PieceKind> Pieces { get; private init; } = new Dictionary<string, PieceKind>();
    public TestGround TestGround { get; private init; } = new(1, 1, Array.Empty<Obstacle>());
    /// <summary>
    /// A fingerprint of all the content (step 16): the SHA-256 of every file's JSON in a fixed form (spacing and
    /// line endings do not count, so a file checked out with Windows line endings has the same fingerprint). A save
    /// records it, so loading can say when the values were changed since.
    /// </summary>
    public string Fingerprint { get; private init; } = "";

    /// <summary>The surfaces, by id (step 15c).</summary>
    public IReadOnlyDictionary<string, Surface> Surfaces { get; private init; } = new Dictionary<string, Surface>();
    /// <summary>The building templates, by id (step 15b).</summary>
    public IReadOnlyDictionary<string, Template> Templates { get; private init; } = new Dictionary<string, Template>();
    public FoundingRules Founding { get; private init; } = new(0, Array.Empty<string>(), new Dictionary<string, int>(), false);

    Content(Dictionary<string, Good> goods, Dictionary<string, Workshop> workshops, Dictionary<string, Recipe> recipes,
            double carryKg, double walk, int pickup, int drop)
    {
        Goods = new SortedDictionary<string, Good>(goods, StringComparer.Ordinal);
        Workshops = new SortedDictionary<string, Workshop>(workshops, StringComparer.Ordinal);
        Recipes = new SortedDictionary<string, Recipe>(recipes, StringComparer.Ordinal);
        (CarryKg, WalkMetresPerMinute, PickupSeconds, DropSeconds) = (carryKg, walk, pickup, drop);
    }

    /// <summary>Reads and checks the content in `folder` (every file of `Files`).</summary>
    public static Content Load(string folder)
    {
        var texts = new Dictionary<string, string?>();
        foreach (string name in Files)
        {
            string path = Path.Combine(folder, name);
            texts[name] = File.Exists(path) ? File.ReadAllText(path) : null;
        }
        return Read(texts);
    }

    static readonly Regex IdShape = new("^[a-z][a-z0-9_]*$");

    /// <summary>The fingerprint of a set of content files that read without problems.</summary>
    static string FingerprintOf(IReadOnlyDictionary<string, string?> files)
    {
        var all = new System.Text.StringBuilder();
        foreach (string name in Files)
            all.Append(name).Append('\n').Append(JsonNode.Parse(files[name]!)!.ToJsonString()).Append('\n');
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(all.ToString()))).ToLowerInvariant();
    }

    /// <summary>The content's files.</summary>
    public static readonly string[] Files = { "goods.json", "recipes.json", "colony.json", "needs.json", "schedules.json", "construction.json", "founding.json",
                                              "sanitation.json", "health.json" };

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
        (int, int)? think = null;
        if (Open("colony.json", "carry_kg", "walk_m_per_minute", "pickup_seconds", "drop_seconds", "think_seconds") is JsonObject c)
        {
            think = Ordered(check.Pair(c, "colony.json", "think_seconds", 1), "colony.json.think_seconds");
            carry = check.Positive(c, "colony.json", "carry_kg") ?? 0;
            walk = check.Positive(c, "colony.json", "walk_m_per_minute") ?? 0;
            pickup = check.Int(c, "colony.json", "pickup_seconds", 0) ?? 0;
            drop = check.Int(c, "colony.json", "drop_seconds", 0) ?? 0;
        }

        // Needs, eating, drinking, sleeping, the spring.
        var needs = new SortedDictionary<string, NeedRule>(StringComparer.Ordinal);
        Consumption? eat = null, drink = null;
        Washing? wash = null;
        WaterRules? waterRules = null;
        double bedroll = 0, ground = 0;
        int spring = 0;
        if (Open("needs.json", "needs", "eat", "drink", "wash", "sleep", "spring_litres_per_day", "water") is JsonObject n)
        {
            if (n["needs"] is JsonObject table)
            {
                check.Keys(table, "needs.json.needs", "hunger", "thirst", "fatigue", "hygiene");
                foreach (var (name, node) in table)
                {
                    string at = "needs.json.needs." + name;
                    if (node is not JsonObject rule)
                    {
                        problems.Add(at + ": should be an object { ... }");
                        continue;
                    }
                    check.Keys(rule, at, "per_hour", "asleep_per_hour", "peckish", "wants", "urgent", "critical", "collapse_hours");
                    // "collapse_hours": null is a need that never fells anyone (hygiene).
                    bool never = rule.ContainsKey("collapse_hours") && rule["collapse_hours"] is null;
                    double?[] v = { check.NotNegative(rule, at, "per_hour"), check.NotNegative(rule, at, "asleep_per_hour"),
                                    check.NotNegative(rule, at, "peckish"), check.NotNegative(rule, at, "wants"),
                                    check.NotNegative(rule, at, "urgent"), check.NotNegative(rule, at, "critical"),
                                    never ? double.PositiveInfinity : check.NotNegative(rule, at, "collapse_hours") };
                    if (v.Any(x => x == null))
                        continue;
                    if (!(v[2] < v[3] && v[3] < v[4] && v[4] < v[5] && v[5] <= 100))
                        problems.Add(at + ": should have peckish < wants < urgent < critical <= 100");
                    else if (table.ContainsKey(name) && new[] { "hunger", "thirst", "fatigue", "hygiene" }.Contains(name))
                        needs[name] = new NeedRule(name, v[0]!.Value, v[1]!.Value, v[2]!.Value, v[3]!.Value, v[4]!.Value, v[5]!.Value, v[6]!.Value);
                }
            }
            else if (n.ContainsKey("needs"))
                problems.Add("needs.json.needs: should be an object { ... }");
            Consumption? Use(string key)
            {
                string at = "needs.json." + key;
                if (n[key] is not JsonObject o)
                {
                    if (n.ContainsKey(key))
                        problems.Add(at + ": should be an object { ... }");
                    return null;
                }
                check.Keys(o, at, "good", "eases", "minutes");
                string? good = check.Text(o, at, "good");
                if (good != null && !goods.ContainsKey(good))
                {
                    problems.Add(at + ".good: no good \"" + good + "\"");
                    good = null;
                }
                double? eases = check.Positive(o, at, "eases");
                int? minutes = check.Int(o, at, "minutes", 1);
                return good != null && eases != null && minutes != null ? new Consumption(good, eases.Value, minutes.Value) : null;
            }
            eat = Use("eat");
            drink = Use("drink");
            if (n["wash"] is JsonObject wo)
            {
                const string at = "needs.json.wash";
                check.Keys(wo, at, "good", "litres", "eases", "minutes");
                string? good = check.Text(wo, at, "good");
                if (good != null && !goods.ContainsKey(good))
                {
                    problems.Add(at + ".good: no good \"" + good + "\"");
                    good = null;
                }
                int? litres = check.Int(wo, at, "litres", 1);
                double? eases = check.Positive(wo, at, "eases");
                int? minutes = check.Int(wo, at, "minutes", 1);
                if (good != null && litres != null && eases != null && minutes != null)
                    wash = new Washing(good, litres.Value, eases.Value, minutes.Value);
            }
            else if (n.ContainsKey("wash"))
                problems.Add("needs.json.wash: should be an object { ... }");
            if (n["water"] is JsonObject wr)
            {
                const string at = "needs.json.water";
                check.Keys(wr, at, "load_litres", "plenty_litres", "brook_litres_by_month", "freezing_months", "cold_biomes", "ice_hole");
                int? load = check.Int(wr, at, "load_litres", 1);
                int? plenty = check.Int(wr, at, "plenty_litres", 1);
                var brook = new List<int>();
                if (wr["brook_litres_by_month"] is JsonArray bl && bl.Count == Calendar.MonthsPerYear
                    && bl.All(x => x is JsonValue xv && xv.TryGetValue(out int l) && l >= 0))
                    brook.AddRange(bl.Select(x => (int)x!));
                else if (wr.ContainsKey("brook_litres_by_month"))
                    problems.Add(at + ".brook_litres_by_month: should be " + Calendar.MonthsPerYear + " whole numbers of 0 or more, January first");
                var freezing = new HashSet<int>();
                if (wr["freezing_months"] is JsonArray fm && fm.All(x => x is JsonValue xv && xv.TryGetValue(out int m) && m is >= 1 and <= Calendar.MonthsPerYear))
                    freezing.UnionWith(fm.Select(x => (int)x!));
                else if (wr.ContainsKey("freezing_months"))
                    problems.Add(at + ".freezing_months: should be a list of months, 1 to " + Calendar.MonthsPerYear);
                var cold = new HashSet<string>(StringComparer.Ordinal);
                if (wr["cold_biomes"] is JsonArray cb && cb.All(x => x is JsonValue xv && xv.TryGetValue(out string? _)))
                    cold.UnionWith(cb.Select(x => (string)x!));
                else if (wr.ContainsKey("cold_biomes"))
                    problems.Add(at + ".cold_biomes: should be a list of biome names");
                string? ice = check.Text(wr, at, "ice_hole");
                if (load != null && plenty != null && brook.Count == Calendar.MonthsPerYear && ice != null)
                    waterRules = new WaterRules(load.Value, plenty.Value, brook, freezing, cold, ice);
            }
            else if (n.ContainsKey("water"))
                problems.Add("needs.json.water: should be an object { ... }");
            if (n["sleep"] is JsonObject s)
            {
                check.Keys(s, "needs.json.sleep", "bedroll_per_hour", "ground_per_hour");
                bedroll = check.Positive(s, "needs.json.sleep", "bedroll_per_hour") ?? 0;
                ground = check.Positive(s, "needs.json.sleep", "ground_per_hour") ?? 0;
            }
            else if (n.ContainsKey("sleep"))
                problems.Add("needs.json.sleep: should be an object { ... }");
            spring = check.Int(n, "needs.json", "spring_litres_per_day", 0) ?? 0;
        }

        // Schedules.
        var schedules = new SortedDictionary<string, Schedule>(StringComparer.Ordinal);
        string? defaultSchedule = null;
        if (Open("schedules.json", "default", "templates") is JsonObject sc)
        {
            foreach (var (item, at) in check.List(sc, "schedules.json", "templates"))
            {
                check.Keys(item, at, "id", "name", "hours");
                string? id = check.Id(item, at, IdShape);
                string? name = check.Text(item, at, "name");
                var hours = new List<Block>();
                if (item["hours"] is JsonArray list && list.Count == Calendar.HoursPerDay)
                {
                    for (int h = 0; h < list.Count; h++)
                        if (list[h] is JsonValue hv && hv.TryGetValue(out string? word) && Enum.TryParse<Block>(word, ignoreCase: true, out var block)
                            && word == word.ToLowerInvariant() && Enum.IsDefined(block))
                            hours.Add(block);
                        else
                            problems.Add(at + ".hours[" + h + "]: should be one of sleep, work, meals, recreation, flexible");
                }
                else if (item.ContainsKey("hours"))
                    problems.Add(at + ".hours: should be a list of 24 blocks, one for each hour from midnight");
                if (id == null)
                    continue;
                if (schedules.ContainsKey(id))
                    problems.Add(at + ": a second schedule with the id \"" + id + "\"");
                else if (name != null && hours.Count == Calendar.HoursPerDay)
                    schedules[id] = new Schedule(id, name, hours);
            }
            defaultSchedule = check.Text(sc, "schedules.json", "default");
            if (defaultSchedule != null && !schedules.ContainsKey(defaultSchedule) && !problems.Any(p => p.StartsWith("schedules.json.templates")))
                problems.Add("schedules.json.default: no schedule \"" + defaultSchedule + "\"");
        }

        // Construction pieces and the test ground.
        var pieces = new SortedDictionary<string, PieceKind>(StringComparer.Ordinal);
        TestGround? testGround = null;
        var templates = new SortedDictionary<string, Template>(StringComparer.Ordinal);
        var surfaces = new SortedDictionary<string, Surface>(StringComparer.Ordinal);
        if (Open("construction.json", "surfaces", "pieces", "templates", "test_ground") is JsonObject k)
        {
            foreach (var (item, at) in check.List(k, "construction.json", "surfaces"))
            {
                check.Keys(item, at, "id", "name", "speed");
                string? id = check.Id(item, at, IdShape);
                string? name = check.Text(item, at, "name");
                double? speed = check.Share(item, at, "speed", zeroAllowed: true);
                if (id == null)
                    continue;
                if (surfaces.ContainsKey(id))
                    problems.Add(at + ": a second surface with the id \"" + id + "\"");
                else if (name != null && speed != null)
                    surfaces[id] = new Surface(id, name, speed.Value);
            }
            foreach (var (item, at) in check.List(k, "construction.json", "pieces"))
            {
                check.Keys(item, at, "id", "name", "materials", "work_minutes", "blocks", "encloses", "speed", "size", "access", "water_per_day",
                           "holds_litres", "wash_place", "opens_ice");
                string? id = check.Id(item, at, IdShape);
                string? name = check.Text(item, at, "name");
                var materials = check.Amounts(item, at, "materials", goods, emptyAllowed: true);
                double? pieceSpeed = item["speed"] is null ? null : check.Share(item, at, "speed");
                bool speedOk = item["speed"] is null || pieceSpeed != null;
                var size = check.Pair(item, at, "size", 1);
                (int, int)? access = item["access"] is null ? null : check.Pair(item, at, "access", -1);
                bool accessOk = item["access"] is null || access != null;
                int? water = check.Int(item, at, "water_per_day", 0);
                int? holds = check.Int(item, at, "holds_litres", 0);
                bool? washes = check.Bool(item, at, "wash_place");
                bool? opensIce = check.Bool(item, at, "opens_ice");
                if (washes == true && holds == 0)
                {
                    problems.Add(at + ".wash_place: a wash place holds water (holds_litres above 0)");
                    washes = null;
                }
                if (size is var (sw, sh) && access is var (ax, ay) && ax >= 0 && ay >= 0 && ax < sw && ay < sh)
                {
                    problems.Add(at + ".access: (" + ax + ", " + ay + ") is under the piece itself; it should be a tile beside it");
                    accessOk = false;
                }
                if (size is var (w2, h2) && (w2 > 1 || h2 > 1) && access == null && accessOk)
                {
                    problems.Add(at + ".access: a piece bigger than one tile needs a tile it is built and used from");
                    accessOk = false;
                }
                int? minutes = check.Int(item, at, "work_minutes", 1);
                bool? blocks = check.Bool(item, at, "blocks");
                bool? encloses = check.Bool(item, at, "encloses");
                if (id == null)
                    continue;
                if (pieces.ContainsKey(id))
                    problems.Add(at + ": a second piece with the id \"" + id + "\"");
                else if (name != null && materials != null && minutes != null && blocks != null && encloses != null
                         && speedOk && size != null && accessOk && water != null && holds != null && washes != null && opensIce != null)
                    pieces[id] = new PieceKind(id, name, materials, minutes.Value, blocks.Value, encloses.Value)
                    {
                        Speed = pieceSpeed, Width = size.Value.Item1, Height = size.Value.Item2, Access = access, WaterPerDay = water.Value,
                        HoldsLitres = holds.Value, WashPlace = washes.Value, OpensIce = opensIce.Value,
                    };
            }
            foreach (var (item, at) in check.List(k, "construction.json", "templates"))
            {
                check.Keys(item, at, "id", "name", "rows", "key");
                string? id = check.Id(item, at, IdShape);
                string? name = check.Text(item, at, "name");
                // The key: one letter for each piece ('.' is a tile left as it is).
                var key = new Dictionary<char, string>();
                if (item["key"] is JsonObject keys)
                    foreach (var (letter, node) in keys)
                    {
                        string? piece = node is JsonValue v && v.TryGetValue(out string? s) ? s : null;
                        if (letter.Length != 1 || letter == ".")
                            problems.Add(at + ".key: \"" + letter + "\" should be one letter, not \".\"");
                        else if (piece == null || !pieces.ContainsKey(piece))
                            problems.Add(at + ".key." + letter + ": no piece \"" + (piece ?? "") + "\"");
                        else
                            key[letter[0]] = piece;
                    }
                else if (item.ContainsKey("key"))
                    problems.Add(at + ".key: should be a table of letters and pieces");
                var laid = new List<(string, int, int)>();
                int width = 0, height = 0;
                bool rowsOk = item["rows"] is JsonArray rows && rows.Count > 0;
                if (rowsOk)
                {
                    var lines = ((JsonArray)item["rows"]!).Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s : null).ToList();
                    width = lines[0]?.Length ?? 0;
                    height = lines.Count;
                    for (int y = 0; y < lines.Count; y++)
                    {
                        if (lines[y] is not string line || line.Length != width || width == 0)
                        {
                            problems.Add(at + ".rows[" + y + "]: every row should be text of the same length, not empty");
                            rowsOk = false;
                            break;
                        }
                        for (int x = 0; x < line.Length; x++)
                            if (line[x] == '.')
                                continue;
                            else if (key.TryGetValue(line[x], out var piece))
                                laid.Add((piece, x, y));
                            else if (item["key"] is JsonObject && !((JsonObject)item["key"]!).ContainsKey(line[x].ToString()))
                            {
                                problems.Add(at + ".rows[" + y + "]: \"" + line[x] + "\" is not in the key");
                                rowsOk = false;
                            }
                    }
                }
                else if (item.ContainsKey("rows"))
                    problems.Add(at + ".rows: should be a list of text rows, not empty");
                if (id == null)
                    continue;
                if (templates.ContainsKey(id))
                    problems.Add(at + ": a second template with the id \"" + id + "\"");
                else if (name != null && rowsOk && laid.Count > 0)
                    templates[id] = new Template(id, name, width, height, laid);
                else if (name != null && rowsOk)
                    problems.Add(at + ".rows: no pieces at all");
            }
            if (k["test_ground"] is JsonObject tg)
            {
                const string at = "construction.json.test_ground";
                check.Keys(tg, at, "width", "height", "surface", "patches", "obstacles");
                int? width = check.Int(tg, at, "width", 1), height = check.Int(tg, at, "height", 1);
                string? surface = check.Text(tg, at, "surface");
                if (surface != null && !surfaces.ContainsKey(surface))
                    problems.Add(at + ".surface: no surface \"" + surface + "\"");
                else if (surface != null && !surfaces[surface].CanWalk)
                    problems.Add(at + ".surface: \"" + surface + "\" cannot be walked on, so the ground cannot be made of it");
                var patches = new List<Patch>();
                foreach (var (item, pat) in check.List(tg, at, "patches"))
                {
                    check.Keys(item, pat, "surface", "x", "y", "width", "height");
                    string? kind = check.Text(item, pat, "surface");
                    int? px = check.Int(item, pat, "x", 0), py = check.Int(item, pat, "y", 0);
                    int? pw = check.Int(item, pat, "width", 1), ph = check.Int(item, pat, "height", 1);
                    if (kind == null || px == null || py == null || pw == null || ph == null)
                        continue;
                    if (!surfaces.ContainsKey(kind))
                        problems.Add(pat + ".surface: no surface \"" + kind + "\"");
                    else if (width != null && height != null && (px + pw > width || py + ph > height))
                        problems.Add(pat + ": it reaches outside the ground (" + width + " by " + height + ")");
                    else
                        patches.Add(new Patch(kind, px.Value, py.Value, pw.Value, ph.Value));
                }
                var obstacles = new List<Obstacle>();
                var taken = new HashSet<(int, int)>();
                foreach (var (item, oat) in check.List(tg, at, "obstacles"))
                {
                    check.Keys(item, oat, "kind", "x", "y");
                    string? kind = check.Text(item, oat, "kind");
                    int? x = check.Int(item, oat, "x", 0), y = check.Int(item, oat, "y", 0);
                    if (kind == null || x == null || y == null)
                        continue;
                    if (!IdShape.IsMatch(kind))
                        problems.Add(oat + ".kind: \"" + kind + "\" should be lowercase letters, digits and _, starting with a letter");
                    else if (width != null && height != null && (x >= width || y >= height))
                        problems.Add(oat + ": (" + x + ", " + y + ") is outside the ground (" + width + " by " + height + ")");
                    else if (!taken.Add((x.Value, y.Value)))
                        problems.Add(oat + ": a second obstacle on (" + x + ", " + y + ")");
                    else
                        obstacles.Add(new Obstacle(kind, x.Value, y.Value));
                }
                if (width != null && height != null && surface != null)
                    testGround = new TestGround(width.Value, height.Value, obstacles) { Surface = surface, Patches = patches };
            }
            else if (k.ContainsKey("test_ground"))
                problems.Add("construction.json.test_ground: should be an object { ... }");
        }

        // Who founds the colony.
        FoundingRules? founding = null;
        if (Open("founding.json", "founders", "names", "supplies", "bedroll_each") is JsonObject f)
        {
            int? founders = check.Int(f, "founding.json", "founders", 1);
            var names = new List<string>();
            if (f["names"] is JsonArray list)
                for (int i = 0; i < list.Count; i++)
                    if (list[i] is JsonValue v && v.TryGetValue(out string? s) && s.Trim().Length > 0)
                    {
                        if (names.Contains(s))
                            problems.Add("founding.json.names[" + i + "]: \"" + s + "\" is there twice");
                        names.Add(s);
                    }
                    else
                        problems.Add("founding.json.names[" + i + "]: should be text that is not empty");
            else if (f.ContainsKey("names"))
                problems.Add("founding.json.names: should be a list of names");
            if (founders != null && names.Count < founders)
                problems.Add("founding.json.names: " + names.Count + " names for " + founders + " founders; there should be at least as many names");
            var supplies = check.Amounts(f, "founding.json", "supplies", goods);
            bool? each = check.Bool(f, "founding.json", "bedroll_each");
            if (founders != null && supplies != null && each != null)
                founding = new FoundingRules(founders.Value, names, supplies, each.Value);
        }

        // The ice hole named by the water rules is a piece that opens ice; the wash's good is the drink's.
        if (waterRules != null && pieces.Count > 0 && !(pieces.TryGetValue(waterRules.IceHole, out var hole) && hole.OpensIce))
            problems.Add("needs.json.water.ice_hole: \"" + waterRules.IceHole + "\" is not a piece of construction.json with \"opens_ice\": true");
        if (wash != null && drink != null && wash.Good != drink.Good)
            problems.Add("needs.json.wash.good: should be the drink's good, \"" + drink.Good + "\" (colonists wash in water)");
        // Fouled water and boiling (sanitation.json), the upset stomach (health.json): step 19b.
        SanitationRules? sanitation = null;
        if (Open("sanitation.json", "fouled_good", "fouling", "boil") is JsonObject sa)
        {
            string? fouled = check.Text(sa, "sanitation.json", "fouled_good");
            if (fouled != null && !goods.ContainsKey(fouled))
            {
                problems.Add("sanitation.json.fouled_good: no good \"" + fouled + "\"");
                fouled = null;
            }
            else if (fouled != null && drink != null && fouled == drink.Good)
            {
                problems.Add("sanitation.json.fouled_good: should not be the drink's good itself");
                fouled = null;
            }
            double? between = null, share = null;
            (int, int)? lasts = null;
            var signs = new List<string>();
            if (sa["fouling"] is JsonObject fo)
            {
                const string at = "sanitation.json.fouling";
                check.Keys(fo, at, "days_between", "lasts_days", "sign_share", "signs");
                between = check.Positive(fo, at, "days_between");
                lasts = Ordered(check.Pair(fo, at, "lasts_days", 1), at + ".lasts_days");
                share = fo["sign_share"] is JsonValue sv && sv.TryGetValue(out double s) && s >= 0 && s <= 1 ? s
                    : Bad<double?>(at + ".sign_share: should be a number from 0 to 1");
                if (fo["signs"] is JsonArray sl && sl.Count > 0 && sl.All(x => x is JsonValue xv && xv.TryGetValue(out string? w) && w.Length > 0))
                    signs.AddRange(sl.Select(x => (string)x!));
                else
                    problems.Add(at + ".signs: should be a list of at least one sign, in words");
            }
            else
                problems.Add("sanitation.json.fouling: should be an object { ... }");
            int? litres = null, fuelUnits = null, minutes = null;
            string? fuel = null;
            if (sa["boil"] is JsonObject bo)
            {
                const string at = "sanitation.json.boil";
                check.Keys(bo, at, "litres", "fuel", "fuel_units", "minutes");
                litres = check.Int(bo, at, "litres", 1);
                fuel = check.Text(bo, at, "fuel");
                if (fuel != null && !goods.ContainsKey(fuel))
                {
                    problems.Add(at + ".fuel: no good \"" + fuel + "\"");
                    fuel = null;
                }
                fuelUnits = check.Int(bo, at, "fuel_units", 1);
                minutes = check.Int(bo, at, "minutes", 1);
            }
            else
                problems.Add("sanitation.json.boil: should be an object { ... }");
            if (fouled != null && between != null && lasts is var (lf, lt) && share != null && signs.Count > 0
                && litres != null && fuel != null && fuelUnits != null && minutes != null)
                sanitation = new SanitationRules(fouled, between.Value, lf, lt, share.Value, signs, litres.Value, fuel, fuelUnits.Value, minutes.Value);
        }
        IllnessRule? stomach = null;
        if (Open("health.json", "upset_stomach") is JsonObject he)
        {
            if (he["upset_stomach"] is JsonObject us)
            {
                const string at = "health.json.upset_stomach";
                check.Keys(us, at, "name", "chance", "onset_hours", "lasts_hours", "work", "thirst", "fatigue");
                string? name = check.Text(us, at, "name");
                double? chance = us["chance"] is JsonValue cv && cv.TryGetValue(out double odds) && odds >= 0 && odds <= 1 ? odds
                    : Bad<double?>(at + ".chance: should be a number from 0 to 1");
                var onset = Ordered(check.Pair(us, at, "onset_hours", 0), at + ".onset_hours");
                var lastsHours = Ordered(check.Pair(us, at, "lasts_hours", 1), at + ".lasts_hours");
                double? work = check.Share(us, at, "work");
                double? thirst = check.Positive(us, at, "thirst");
                double? fatigue = check.Positive(us, at, "fatigue");
                if (name != null && chance != null && onset is var (of, ot) && lastsHours is var (hf, ht) && work != null && thirst != null && fatigue != null)
                    stomach = new IllnessRule(name, chance.Value, of, ot, hf, ht, work.Value, thirst.Value, fatigue.Value);
            }
            else
                problems.Add("health.json.upset_stomach: should be an object { ... }");
        }
        // A pair [from, to] with from no more than to.
        (int, int)? Ordered((int, int)? pair, string at)
        {
            if (pair is var (a, b) && a > b)
            {
                problems.Add(at + ": the first should not be more than the second");
                return null;
            }
            return pair;
        }
        T? Bad<T>(string problem)
        {
            problems.Add(problem);
            return default;
        }

        if (problems.Count > 0)
            throw new ContentException(problems);
        return new Content(goods, workshops, recipes, carry, walk, pickup, drop)
        {
            Needs = needs, Eat = eat!, Drink = drink!, Wash = wash!, Water = waterRules!, Sanitation = sanitation!, UpsetStomach = stomach!, BedrollPerHour = bedroll, GroundPerHour = ground, SpringPerDay = spring,
            Schedules = schedules, DefaultSchedule = defaultSchedule!, Pieces = pieces, TestGround = testGround!,
            Templates = templates, Founding = founding!, Surfaces = surfaces,
            ThinkFrom = think!.Value.Item1, ThinkTo = think.Value.Item2,
            Fingerprint = FingerprintOf(files),
        };
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

        public double? NotNegative(JsonObject item, string at, string key)
        {
            if (!item.ContainsKey(key))
                return null;
            if (item[key] is JsonValue v && v.TryGetValue(out double d) && d >= 0 && double.IsFinite(d))
                return d;
            problems.Add(at + "." + key + ": should be a number of 0 or more");
            return null;
        }

        /// <summary>A share above 0 and at most 1 (a speed: 0.8 is 80%); with `zeroAllowed`, 0 too (a surface that
        /// cannot be walked on, such as water).</summary>
        public double? Share(JsonObject item, string at, string key, bool zeroAllowed = false)
        {
            if (!item.ContainsKey(key))
                return null;
            if (item[key] is JsonValue v && v.TryGetValue(out double d) && (d > 0 || (zeroAllowed && d == 0)) && d <= 1)
                return d;
            problems.Add(at + "." + key + ": should be a number " + (zeroAllowed ? "from 0 (cannot be walked on)" : "above 0")
                         + " and at most 1 (1 is a road's speed)");
            return null;
        }

        /// <summary>Two whole numbers [a, b], each at least `least`.</summary>
        public (int, int)? Pair(JsonObject item, string at, string key, int least)
        {
            if (!item.ContainsKey(key))
                return null;
            if (item[key] is JsonArray a && a.Count == 2 && a[0] is JsonValue v0 && v0.TryGetValue(out int x) && a[1] is JsonValue v1
                && v1.TryGetValue(out int y) && x >= least && y >= least)
                return (x, y);
            problems.Add(at + "." + key + ": should be two whole numbers [a, b] of " + least + " or more");
            return null;
        }

        public bool? Bool(JsonObject item, string at, string key)
        {
            if (!item.ContainsKey(key))
                return null;
            if (item[key] is JsonValue v && v.TryGetValue(out bool b))
                return b;
            problems.Add(at + "." + key + ": should be true or false");
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
        public IReadOnlyDictionary<string, int>? Amounts(JsonObject item, string at, string key, Dictionary<string, Good> goods, bool emptyAllowed = false)
        {
            if (!item.ContainsKey(key))
                return null;
            if (item[key] is not JsonObject table || (table.Count == 0 && !emptyAllowed))
            {
                problems.Add(at + "." + key + ": should be a table of goods and amounts" + (emptyAllowed ? "" : ", not empty"));
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
