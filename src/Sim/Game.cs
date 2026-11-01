// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.World;

namespace WardensReckoning.Sim;

/// <summary>What the years carry for one settlement (the generator's run_years keeps the same, per settlement).</summary>
public sealed class TownNow
{
    /// <summary>Its people as a fraction: the population shown is this rounded.</summary>
    public double People { get; set; }
    /// <summary>The size it counts as (a place in SETTLEMENT_SIZES): it moves down only past a margin.</summary>
    public int Size { get; set; }
    /// <summary>Years in a row below RUIN_POPULATION; years in a row not fully fed.</summary>
    public int LowYears { get; set; }
    public int Hungry { get; set; }
    public bool Ruined { get; set; }
    /// <summary>The faction holding it, or null.</summary>
    public int? Holder { get; set; }
    /// <summary>What its land grows of each good in an ordinary year.</summary>
    public Dictionary<string, double> Yields { get; set; } = new();
    /// <summary>The kinds of place it is near (a good "made_on" one is only made here if it is near one).</summary>
    public HashSet<string> Near { get; set; } = new();
    /// <summary>The faction whose people founded it (it resents another holder), and the year it was last taken.</summary>
    public int? Origin { get; set; }
    public int? ConqueredIn { get; set; }
    /// <summary>The town whose migrants founded or resettled it, or null: a colony far from the capital leaves in peace.</summary>
    public int? FoundedFrom { get; set; }
    /// <summary>Its place on the map (map cells), for the nearest town to a mine; and its region of the history's
    /// grid, where migrants measure from.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int HX { get; set; }
    public int HY { get; set; }
    /// <summary>What its land grows of food, and the people that can feed (both follow who holds it: Feeding.cs).</summary>
    public double Food { get; set; }
    public double Capacity { get; set; }
    /// <summary>The (people, path) its food and harvest were last worked out for, or null if not yet (Feeding.cs).</summary>
    public (int? People, string? Path)? FedAs { get; set; }
    /// <summary>The regions of the history's grid it farms (not sea or lake), row after row.</summary>
    public List<int> Cells { get; set; } = new();

    /// <summary>How much of its food need was met last year, 0 to 1 (for the map and the book; not carried by the rules).</summary>
    public double Fed { get; set; } = 1.0;

    public int Population => (int)Math.Round(People, MidpointRounding.ToEven);
}

/// <summary>What the years carry for one faction, so far.</summary>
public sealed class FactionNow
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string Government { get; set; } = "";
    /// <summary>Its ruler, or null when it has none (no people left).</summary>
    public Ruler? Ruler { get; set; }
    /// <summary>Its ruler's competence, 0 to 1, or null with no ruler.</summary>
    public double? Competence => Ruler?.Traits.GetValueOrDefault("competence");
    public int People { get; set; }
    /// <summary>The people it belongs to (by its place in the export's "peoples"): its rulers' names are theirs.</summary>
    public int Folk { get; set; }
    /// <summary>Its ways now (planning, risk, greed, 0 to 1), which rulers pull; and its people's own, which they
    /// pull back toward.</summary>
    public Dictionary<string, double> Traits { get; set; } = new();
    public Dictionary<string, double> Values { get; set; } = new();
    /// <summary>How much its people hold against their rulers; how many rulers, revolutions and changes of path it has had.</summary>
    public double Grievance { get; set; }
    public int RulerCount { get; set; }
    public int Revolutions { get; set; }
    public int PathChanges { get; set; }
    /// <summary>What it thinks of each path (it changes slowly after a change of path).</summary>
    public Dictionary<string, double> Prejudice { get; set; } = new();
    /// <summary>A dead ruler's lingering pull, or null; whether a strong ruler's death left it on the edge of splitting.</summary>
    public Cult? Cult { get; set; }
    public bool SplitPending { get; set; }
    /// <summary>Other faction → how many cells of border they share.</summary>
    public Dictionary<int, double> Border { get; set; } = new();
    /// <summary>What it believes of the god of death.</summary>
    public string Belief { get; set; } = "unknowing";
    /// <summary>How likely its conquests are to rise (below 1 for an empire that holds together; splits come with step 6).</summary>
    public double Hold { get; set; } = 1.0;
    /// <summary>The paths it holds besides its main one (paths as skills), or null when paths are not skills.</summary>
    public HashSet<string>? Held { get; set; }
    /// <summary>The faction it comes from in the settings' FACTIONS (its goods), and its traits (greed digs more).</summary>
    public string? Lineage { get; set; }
    /// <summary>Its capital (a town), or null; the faction it broke away from (or null) and the year it came to be.</summary>
    public int? Capital { get; set; }
    public int? Parent { get; set; }
    public int Founded { get; set; }
    /// <summary>Its people's nature: their traits before any land shaped them (a new faction takes its kin's).</summary>
    public Dictionary<string, double> Nature { get; set; } = new();
    public double Greed { get => Traits.GetValueOrDefault("greed"); set => Traits["greed"] = value; }
    /// <summary>Goods it has learned to make (chokepoints), and the paths and hybrids it has learned and how well.</summary>
    public HashSet<string> Learned { get; set; } = new();
    public Dictionary<string, double> Skills { get; set; } = new();
    /// <summary>The hybrids it holds both halves of (paths as skills, step 7).</summary>
    public HashSet<string> Unlocked { get; set; } = new();
    /// <summary>What it thinks of each other faction, -1 to 1 (it trades with those it does not think too ill of).
    /// Opinions do not change until step 4.</summary>
    public Dictionary<int, double> Opinions { get; set; } = new();
    /// <summary>The metal its towns make, and what its people need.</summary>
    public double MetalSupply { get; set; }
    public double MetalNeed { get; set; }
}

/// <summary>A metal deposit, and how much is left in it (the generator's factions.Deposit).</summary>
public sealed class Mine
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public double Metal { get; set; }
    /// <summary>How much it held when found: it runs dry measured against this.</summary>
    public double First { get; set; }
    /// <summary>The faction holding its land, or null.</summary>
    public int? Owner { get; set; }
    /// <summary>The settlement whose land it is on, or null; and its place on the history's grid.</summary>
    public int? LandTown { get; set; }
    public int HX { get; set; }
    public int HY { get; set; }
}

/// <summary>A route between two neighbouring settlements: what the journey costs, and how much passed along it this year.</summary>
public sealed class Route
{
    public int A { get; init; }
    public int B { get; init; }
    public double Cost { get; init; }
    public double Volume { get; set; }
}

/// <summary>One thing that happened in a year the game ran: as the generator's history.Event, plus the factions
/// it concerns by number, so the book can show a faction its own entries.</summary>
public sealed record GameEvent(int Year, string Kind, string What, string Text, IReadOnlyList<int> Factions,
                               IReadOnlyDictionary<string, string> Facts);

/// <summary>
/// A game: an exported world and the years run on from its end. Everything that changes from year to year is
/// here (and in the world's growing records), so that a save holds it all.
/// </summary>
public sealed class Game
{
    public ExportedWorld World { get; }
    public GameSettings Settings { get; }

    /// <summary>The game's own seed, chosen when it starts: with the same export and seed the years always
    /// play out the same.</summary>
    public long Seed { get; }

    /// <summary>The last year run (at the start, the export's last year).</summary>
    public int Year { get; set; }

    public List<TownNow> Towns { get; }
    public List<FactionNow> Factions { get; }
    public List<GameEvent> Events { get; } = new();
    public List<Mine> Mines { get; }
    public List<Route> Routes { get; }

    /// <summary>What each faction remembers of each other, -1 to 1: [faction, other] (the generator's memory).</summary>
    public Dictionary<(int, int), double> Memory { get; }

    /// <summary>How many regions wide the history's grid is: a route's cost is counted on it.</summary>
    public int HistoryColumns { get; }

    /// <summary>Every war, ended or not, and each pair of allies (the lower number first).</summary>
    public List<WarNow> Wars { get; } = new();
    public HashSet<(int, int)> Alliances { get; } = new();

    /// <summary>
    /// The factions whose capital has been looked after since they came to be (Capitals.Mend): one that is not yet
    /// picks its first capital at the next mend. Every faction of an export or a save is: the generator and the game
    /// look after every faction's capital at the end of each year, and a faction comes to be with towns of its own.
    /// </summary>
    public HashSet<int> CapitalsKnown { get; } = new();
    /// <summary>How many towns were taken in battle, and how many rose back, in the game's years.</summary>
    public int Conquests { get; set; }
    public int Rebellions { get; set; }
    /// <summary>Whether towns changed hands since the land was last shared out (War.RedrawBorders).</summary>
    public bool BordersChanged { get; set; }

    /// <summary>The history's grid as land (the export's "continue" → "grid"): the town farming each region (-1: none)
    /// and the sea, row after row.</summary>
    public sealed record LandGrid(int Columns, int Rows, int[] Owner, bool[] Ocean);
    public LandGrid Grid { get; }

    /// <summary>The history's land, region by region (the export's "continue" → "land"): each region's biome (by its
    /// place in BiomeNames, -1 for none), whether it is a lake, and its kind of land (1 land, + 2 coast, + 4 river,
    /// + 8 highland; 0 sea).</summary>
    public int[] LandBiome { get; }
    public bool[] Lake { get; }
    public int[] LandKinds { get; }
    public IReadOnlyList<string> BiomeNames { get; }
    /// <summary>Each people's name (the export's "peoples"), which their tastes are found under.</summary>
    public IReadOnlyList<string> PeopleNamesOf { get; }

    /// <summary>What each faction's land encourages, as last looked at (Drift.cs).</summary>
    public Dictionary<int, Dictionary<string, double>> DriftTargets { get; } = new();

    /// <summary>Who holds each region of the history's grid now (-1: no one), as the borders show it.</summary>
    public int[] Regions { get; }

    /// <summary>The band of RELATIONS each faction last spoke of each other in: [faction, other].</summary>
    public Dictionary<(int, int), int> Words { get; } = new();

    /// <summary>Names for the rulers the game crowns, per people (the export's "names"), and how many of each are used.</summary>
    public List<List<string>> Names { get; }
    public int[] NamesUsed { get; set; }
    /// <summary>Names for the towns the game's migrants found, per people, and how many of each are used.</summary>
    public List<List<string>> SettlementNames { get; }
    public int[] SettlementNamesUsed { get; set; }

    /// <summary>What migration reads and remembers (Migration.cs).</summary>
    public Movements Moving { get; }

    /// <summary>Names for the factions the game founds, per people, and how many of each are used.</summary>
    public List<List<string>> PeopleNames { get; }
    public int[] PeopleNamesUsed { get; set; }

    /// <summary>The factions of the settings' FACTIONS that have not risen yet, in order (the export's "waiting"):
    /// the next new faction of a people is the first of them of that people.</summary>
    public List<(string Key, int Folk)> Waiting { get; } = new();
    /// <summary>How many of each path's SPLIT_NAMES have been given out (the export's "newcomers").</summary>
    public Dictionary<string, int> SplitNamesGiven { get; } = new();
    /// <summary>Every faction name and town name in the world: a name from SPLIT_NAMES is not given twice.</summary>
    public HashSet<string> UsedNames { get; } = new();

    List<Trade.Journeys>? journeys;

    /// <summary>Every town's cheapest journeys over the routes, worked out once and kept until the routes change.</summary>
    public List<Trade.Journeys> Journeys => journeys ??= Trade.AllJourneys(this);

    /// <summary>Forget the journeys, after the routes changed (they are worked out again when next needed).</summary>
    public void RoutesChanged() => journeys = null;

    readonly Dictionary<string, Dice> dice = new();

    /// <summary>The dice of one system ("rulers", "rules"...): made from the game's seed and the system's name the
    /// first time, kept after, and kept in a save.</summary>
    public Dice DiceFor(string system)
    {
        if (!dice.TryGetValue(system, out var found))
            dice[system] = found = new Dice(Seed, system);
        return found;
    }

    /// <summary>Every system's dice made so far, by name (for a save).</summary>
    public IReadOnlyDictionary<string, Dice> AllDice => dice;

    /// <summary>Puts back the dice a save kept.</summary>
    public void SetDice(string system, Dice saved) => dice[system] = saved;

    /// <summary>The next new name among people `folk`: the export's names in turn, then the same again numbered.</summary>
    public string NextName(int folk)
    {
        var listed = folk < Names.Count ? Names[folk] : new List<string>();
        if (listed.Count == 0)
            return "Ruler " + (NamesUsed.Sum() + 1);
        int used = folk < NamesUsed.Length ? NamesUsed[folk]++ : 0;
        string name = listed[used % listed.Count];
        return used < listed.Count ? name : name + " " + (used / listed.Count + 1);
    }

    /// <summary>The next name for a new faction of people `folk`: the export's names in turn, then numbered.</summary>
    public string NextPeopleName(int folk)
    {
        var listed = folk < PeopleNames.Count ? PeopleNames[folk] : new List<string>();
        if (listed.Count == 0)
            return "Faction " + (Factions.Count + 1);
        int used = folk < PeopleNamesUsed.Length ? PeopleNamesUsed[folk]++ : 0;
        string name = listed[used % listed.Count];
        return used < listed.Count ? name : name + " " + (used / listed.Count + 1);
    }

    /// <summary>The next name for a new town of people `folk`: the export's names in turn, then numbered.</summary>
    public string NextSettlementName(int folk)
    {
        var listed = folk < SettlementNames.Count ? SettlementNames[folk] : new List<string>();
        if (listed.Count == 0)
            return "Town " + (Towns.Count + 1);
        int used = folk < SettlementNamesUsed.Length ? SettlementNamesUsed[folk]++ : 0;
        string name = listed[used % listed.Count];
        return used < listed.Count ? name : name + " " + (used / listed.Count + 1);
    }

    /// <summary>A town the game's migrants founded: added to Towns and to the world's records from the year in
    /// `record`, with its site score.</summary>
    public void AddTown(TownNow town, TownRecord record, double score)
    {
        Towns.Add(town);
        World.AddTown(record);
        Moving.TownScores.Add(score);
        UsedNames.Add(record.Name);
    }

    /// <summary>A faction the game founded: added to Factions and to the world's records from `year`, with the
    /// colour and sprite sets in `info`.</summary>
    public void AddFaction(FactionNow faction, FactionInfo info, int year)
    {
        Factions.Add(faction);
        World.AddFaction(info, year);
        UsedNames.Add(faction.Name);
    }

    /// <summary>The book: the export's events, then the game's, in the order they happened (World/Book.cs).</summary>
    public List<BookEntry> BookEntries { get; }

    /// <summary>Writes down something that happened in the game's years: in Events (which a save keeps) and in the book.</summary>
    public void Record(GameEvent happened)
    {
        Events.Add(happened);
        BookEntries.Add(new BookEntry(happened.Year, happened.What, happened.Text, null, happened.Factions));
    }

    public Game(ExportedWorld world, long seed)
    {
        World = world;
        Seed = seed;
        Settings = GameSettings.Of(world);
        Year = world.Years;
        BookEntries = Book.FromExport(world);
        var going = GameSettings.Continuation(world);
        var places = world.Data["settlements"]!.AsArray();
        var kept = going["towns"]!.AsArray();
        if (kept.Count != places.Count)
            throw new WorldFileException("\"continue\" has " + kept.Count + " towns for " + places.Count + " settlements");
        Towns = new List<TownNow>();
        for (int i = 0; i < places.Count; i++)
        {
            var place = places[i]!.AsObject();
            var town = kept[i]!.AsObject();
            Towns.Add(new TownNow
            {
                People = (double)town["people"]!,
                Size = (int)town["size"]!,
                LowYears = (int)town["low_years"]!,
                Hungry = (int)town["hungry"]!,
                Ruined = (bool?)place["ruined"] ?? false,
                Holder = (int?)place["culture"],
                Yields = town["yields"]!.AsObject().ToDictionary(p => p.Key, p => (double)p.Value!),
                Near = town["near"] is JsonArray near ? near.Select(k => (string)k!).ToHashSet()
                       : throw Older("the towns' \"near\""),
                Origin = (int?)place["origin"],
                ConqueredIn = (int?)place["conquered_in"],
                FoundedFrom = (int?)place["founded_from"],
                Food = (double?)place["food"] ?? 0.0,
                Capacity = (double?)place["capacity"] ?? 0.0,
                FedAs = town["fed_as"] is JsonArray fed ? ((int?)fed[0], (string?)fed[1]) : null,
                X = (int)place["x"]!,
                Y = (int)place["y"]!,
                HX = (int)place["x"]! / world.HistoryFactor,
                HY = (int)place["y"]! / world.HistoryFactor,
            });
        }
        HistoryColumns = (int?)going["history_columns"] ?? throw Older("\"history_columns\"");
        var keptMines = going["mines"] as JsonArray ?? throw Older("\"mines\"");
        Mines = world.Data["mines"]!.AsArray().Select((node, k) =>
        {
            var m = node!.AsObject();
            return new Mine
            {
                Name = (string?)m["name"] ?? "", X = (int)m["x"]!, Y = (int)m["y"]!,
                Metal = (double)m["metal"]!, First = (double)m["first"]!, Owner = (int?)m["owner"],
                LandTown = (int?)keptMines[k]!["land_town"],
                HX = (int?)keptMines[k]!["x"] ?? throw Older("the mines' places on the history's grid"),
                HY = (int)keptMines[k]!["y"]!,
            };
        }).ToList();
        Routes = world.Data["routes"]!.AsArray()
            .Select(r => new Route { A = (int)r!["a"]!, B = (int)r["b"]!, Cost = (double)r["cost"]! }).ToList();
        Memory = new Dictionary<(int, int), double>();
        foreach (var pair in going["memory"]!.AsArray())
            Memory[((int)pair![0]!, (int)pair[1]!)] = (double)pair[2]!;
        var keptFactions = going["factions"] as JsonArray ?? throw Older("\"factions\"");
        foreach (var pair in going["words"]!.AsArray())
            Words[((int)pair![0]!, (int)pair[1]!)] = (int)pair[2]!;
        var names = going["names"]?["person"] as JsonArray ?? throw Older("\"names\"");
        var land = going["grid"] as JsonObject ?? throw Older("\"grid\"");
        Grid = new LandGrid((int)land["columns"]!, (int)land["rows"]!, Unrun(land["owner"]!.AsArray()),
                            Unrun(land["ocean"]!.AsArray()).Select(v => v == 1).ToArray());
        Regions = world.Borders(world.Years) ?? Enumerable.Repeat(-1, Grid.Columns * Grid.Rows).ToArray();
        var ground = going["land"] as JsonObject ?? throw Older("\"land\"");
        LandBiome = Unrun(ground["biome"]!.AsArray());
        Lake = Unrun(ground["lake"]!.AsArray()).Select(v => v == 1).ToArray();
        LandKinds = Unrun(ground["kinds"]!.AsArray());
        BiomeNames = world.Data["biomes"]!.AsArray().Select(b => (string)b!).ToList();
        PeopleNamesOf = world.Data["peoples"]!.AsArray().Select(p => (string)p!["name"]!).ToList();
        for (int cell = 0; cell < Grid.Owner.Length; cell++)
            if (Grid.Owner[cell] is int owner and >= 0 && owner < Towns.Count && !Grid.Ocean[cell] && !Lake[cell])
                Towns[owner].Cells.Add(cell);
        foreach (var (faction, traits) in going["drift_targets"] as JsonObject ?? throw Older("\"drift_targets\""))
            DriftTargets[int.Parse(faction)] = Numbers(traits);
        foreach (var node in world.Data["wars"]?.AsArray() ?? new JsonArray())
        {
            var w = node!.AsObject();
            Wars.Add(new WarNow
            {
                Attacker = (int)w["attacker"]!, Defender = (int)w["defender"]!, Since = (int)w["since"]!,
                Sides = w["sides"]!.AsArray().Select(side => side!.AsArray().Select(f => (int)f!).ToHashSet()).ToArray(),
                Dead = w["dead"]!.AsObject().ToDictionary(p => int.Parse(p.Key), p => (double)p.Value!),
                LastBattle = (int?)w["last_battle"] ?? 0, Ended = (int?)w["ended"], Endurance = (double?)w["endurance"],
            });
        }
        foreach (var pair in world.Data["alliances"]?.AsArray() ?? new JsonArray())
        {
            int a = (int)pair![0]!, b = (int)pair[1]!;
            Alliances.Add(a < b ? (a, b) : (b, a));
        }
        Names = names.Select(list => list!.AsArray().Select(n => (string)n!).ToList()).ToList();
        NamesUsed = new int[Names.Count];
        var peopleNames = going["names"]?["people"] as JsonArray ?? throw Older("names for new factions");
        PeopleNames = peopleNames.Select(list => list!.AsArray().Select(n => (string)n!).ToList()).ToList();
        PeopleNamesUsed = new int[PeopleNames.Count];
        var settlementNames = going["names"]?["settlement"] as JsonArray ?? throw Older("names for new towns");
        SettlementNames = settlementNames.Select(list => list!.AsArray().Select(n => (string)n!).ToList()).ToList();
        SettlementNamesUsed = new int[SettlementNames.Count];
        Moving = Movements.Read(going["migration"] as JsonObject ?? throw Older("\"migration\""));
        foreach (var entry in going["waiting"] as JsonArray ?? throw Older("\"waiting\""))
            Waiting.Add(((string)entry![0]!, (int)entry[1]!));
        foreach (var (path, given) in (going["newcomers"]?["given"] as JsonObject) ?? throw Older("\"newcomers\""))
            SplitNamesGiven[path] = (int)given!;
        Factions = world.Data["factions"]!.AsArray().Select((node, k) =>
        {
            var f = node!.AsObject();
            return new FactionNow
            {
                Name = (string)f["name"]!,
                Path = (string?)f["path"],
                Government = (string?)f["government"] ?? "",
                Ruler = ReadRuler(f["ruler"] as JsonObject),
                People = (int?)f["population"] ?? 0,
                Folk = (int?)f["people"] ?? k,
                Traits = Numbers(f["traits"]),
                Values = Numbers(f["values"]),
                Grievance = (double?)f["grievance"] ?? 0.0,
                RulerCount = (int?)f["rulers"] ?? 0,
                Revolutions = (int?)f["revolutions"] ?? 0,
                PathChanges = (int?)f["path_changes"] ?? 0,
                Prejudice = Numbers(f["prejudice"]),
                Cult = f["cult"] is JsonObject cult ? new Cult
                {
                    Traits = Numbers(cult["traits"]), Until = (int)cult["until"]!, Strength = (double)cult["strength"]!,
                    Name = (string?)cult["name"] ?? "",
                } : null,
                SplitPending = (bool?)f["split_pending"] ?? false,
                Border = f["border"] is JsonObject border ? border.ToDictionary(p => int.Parse(p.Key), p => (double)p.Value!) : new(),
                Belief = (string?)f["belief"] ?? "unknowing",
                Held = keptFactions[k]!["held"] is JsonArray held ? held.Select(p => (string)p!).ToHashSet() : null,
                Lineage = (string?)f["lineage"],
                Capital = (int?)f["capital"],
                Parent = (int?)f["parent"],
                Founded = (int?)f["founded"] ?? 0,
                Nature = Numbers(f["nature"]),
                Hold = (double?)f["hold"] ?? 1.0,
                Learned = f["learned"] is JsonArray learned ? learned.Select(g => (string)g!).ToHashSet() : new HashSet<string>(),
                Skills = keptFactions[k]!["skills"]!.AsObject().ToDictionary(p => p.Key, p => (double)p.Value!),
                Opinions = f["opinions"]!.AsObject().ToDictionary(p => int.Parse(p.Key), p => (double)p.Value!),
                MetalSupply = (double?)f["metal_supply"] ?? 0.0,
                MetalNeed = (double?)f["metal_need"] ?? 0.0,
            };
        }).ToList();
        NoteNames();
        CapitalsKnown.UnionWith(Enumerable.Range(0, Factions.Count));
        Learning.Settle(this);
    }

    // Python's names are case-sensitive and so are these.
    void NoteNames()
    {
        foreach (var faction in Factions)
            UsedNames.Add(faction.Name);
        foreach (var town in World.Towns)
            UsedNames.Add(town.Name);
    }

    /// <summary>Runs [value, count, value, count, ...] as the values one by one.</summary>
    static int[] Unrun(JsonArray runs)
    {
        var values = new List<int>();
        for (int i = 0; i < runs.Count; i += 2)
            values.AddRange(Enumerable.Repeat((int)runs[i]!, (int)runs[i + 1]!));
        return values.ToArray();
    }

    static Dictionary<string, double> Numbers(JsonNode? table) =>
        table is JsonObject o ? o.ToDictionary(p => p.Key, p => (double)p.Value!) : new Dictionary<string, double>();

    /// <summary>A ruler as the export (or a save) writes one, or null.</summary>
    public static Ruler? ReadRuler(JsonObject? r) => r == null ? null : new Ruler
    {
        Name = (string)r["name"]!, Age = (int)r["age"]!, Traits = Numbers(r["traits"]), Since = (int)r["since"]!,
        Path = (string?)r["path"] ?? "", Effort = (double?)r["effort"] ?? 0.0,
        Stances = r["stances"] is JsonObject s ? s.ToDictionary(p => int.Parse(p.Key), p => (double)p.Value!) : new(),
    };

    /// <summary>A ruler as a save writes it (as the export does).</summary>
    public static JsonObject? WriteRuler(Ruler? r) => r == null ? null : new JsonObject
    {
        ["name"] = r.Name, ["age"] = r.Age, ["since"] = r.Since, ["path"] = r.Path, ["effort"] = r.Effort,
        ["traits"] = new JsonObject(r.Traits.Select(t => KeyValuePair.Create(t.Key, (JsonNode?)JsonValue.Create(t.Value)))),
        ["stances"] = new JsonObject(r.Stances.Select(t => KeyValuePair.Create(t.Key.ToString(), (JsonNode?)JsonValue.Create(t.Value)))),
    };

    // The export lacks a part of "continue" this game needs: it came from an older generator.
    static WorldFileException Older(string part) =>
        new("this world's \"continue\" has no " + part + ": export it again with the newer generator");

    /// <summary>A new game's seed, drawn once from the operating system's randomness (it is then saved, so the
    /// game itself never needs randomness it cannot repeat).</summary>
    public static long NewSeed() => (long)(System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue));
}
