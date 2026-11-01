// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WardensReckoning.World;

/// <summary>A faction as the export lists it at the end of the history, or as the game founded it: its name, colour
/// and sprite sets (which never change), the faction it broke away from (or null) and the year it came to be.</summary>
public sealed record FactionInfo(string Name, byte Red, byte Green, byte Blue, string? Lineage, string? Race,
                                 int? Parent = null, int Founded = 0);

/// <summary>One settlement's whole history: the year it was founded, its people every year from then,
/// its holders as (year, faction) each time that changed, and its ruins as (year it fell, year it was lived in again).
/// The lists grow as the game runs the years on (ExportedWorld.AddYear).</summary>
public sealed record TownRecord(int From, string Name, int X, int Y, List<int> People,
                                List<(int Year, int? Faction)> Owners, List<(int Fell, int? Back)> Ruins);

/// <summary>A settlement in one year: who held it, its people, and whether it lay in ruins.</summary>
public sealed record TownState(int? Holder, int People, bool Ruined);

/// <summary>
/// An exported world, read from the folder `python worldgen.py --export` writes.
///
/// It does what the generator's north/world_file.py does (the reference reader;
/// the tests check that both agree on a real export), plus what the map needs:
/// the factions' colours and sprite sets, the settlements' places and the sizes.
/// Every key is described in the generator's docs/export-format.md.
///
///     var world = ExportedWorld.Read(@"D:\...\output\world-42\h42\export");
///     world.Grid("biomes")[20, 10];   // the biome number of map cell x 20, y 10
///     world.Town(5, 120);             // settlement 5 in year 120
///     world.Borders(120);             // who held each region of the history's grid in year 120
/// </summary>
public sealed class ExportedWorld
{
    // The export's history, read from world.json, followed by every year the game has run since: the game adds
    // each year it runs with AddYear, so the map, the slider and the legend show the new years as they do the old.

    public const string Format = "the-north-world";

    /// <summary>The newest version this reader understands; a newer world is refused rather than half read.</summary>
    public const int SupportedVersion = 2;

    public string Folder { get; }

    /// <summary>world.json as it was read, for anything this class does not read itself.</summary>
    public JsonObject Data { get; }

    public int Seed { get; }
    public int? HistorySeed { get; }
    public int Size { get; }
    /// <summary>The last year there is a record of: the export's last year, then the game's.</summary>
    public int Years { get; private set; }

    /// <summary>The export's own last year (where the generator's history ended and the game's began).</summary>
    public int ExportYears { get; }

    /// <summary>How many map cells wide each region of the history's grid is.</summary>
    public int HistoryFactor { get; }

    public IReadOnlyList<FactionInfo> Factions => factions;
    readonly List<FactionInfo> factions;
    public IReadOnlyList<TownRecord> Towns { get; }

    /// <summary>Each faction's people every year from 0 (empty for a faction not in the list).</summary>
    public IReadOnlyList<List<int>> Population { get; }

    /// <summary>The settlement sizes, smallest first: (the people it takes, the size's name, which is also the sprite's).</summary>
    public IReadOnlyList<(int People, string Name)> SettlementSizes { get; }

    /// <summary>How grey and dark a ruin is drawn, from 0 (as drawn) to 1.</summary>
    public double RuinDim { get; }

    public int BorderColumns { get; }
    public int BorderRows { get; }

    readonly List<(int Year, JsonObject Snapshot)> snapshots = new();
    readonly List<List<(int Year, JsonObject Changed)>> factionChanges = new();
    readonly List<int> factionFrom = new();
    readonly Dictionary<string, Grid> grids = new();

    ExportedWorld(string folder, JsonObject data)
    {
        Folder = folder;
        Data = data;
        Seed = (int)data["seed"]!;
        HistorySeed = (int?)data["history_seed"];
        Size = (int)data["size"]!;
        Years = (int)data["years"]!;
        ExportYears = Years;
        HistoryFactor = (int?)data["history_factor"] ?? 1;

        var races = data["faction_races"] as JsonArray;
        factions = new List<FactionInfo>();
        var list = data["factions"]!.AsArray();
        for (int i = 0; i < list.Count; i++)
        {
            var f = list[i]!.AsObject();
            var c = f["colour"]!.AsArray();
            factions.Add(new FactionInfo((string)f["name"]!, (byte)(int)c[0]!, (byte)(int)c[1]!, (byte)(int)c[2]!,
                                         (string?)f["lineage"], races != null && i < races.Count ? (string?)races[i] : null,
                                         (int?)f["parent"], (int?)f["founded"] ?? 0));
        }

        Population = data["population"]!.AsArray().Select(row => row!.AsArray().Select(v => (int)v!).ToList()).ToList();

        // "pictures" came with the game's first screen; an older export has none, so it gets the generator's sizes.
        if (data["pictures"] is JsonObject pictures)
        {
            SettlementSizes = pictures["settlement_sizes"]!.AsArray()
                .Select(s => ((int)s![0]!, (string)s[1]!)).ToList();
            RuinDim = (double?)pictures["ruin_dim"] ?? 0.6;
        }
        else
        {
            SettlementSizes = new List<(int, string)> { (0, "hamlet"), (1000, "settlement"), (2000, "village"),
                                                        (3000, "town"), (5000, "city") };
            RuinDim = 0.6;
        }

        var history = data["history"]?.AsObject()
            ?? throw new WorldFileException("this world has no \"history\": export it again with the newer generator");
        var places = data["settlements"]!.AsArray();
        var towns = new List<TownRecord>();
        var townList = history["towns"]!.AsArray();
        for (int i = 0; i < townList.Count; i++)
        {
            var t = townList[i]!.AsObject();
            var place = places[i]!.AsObject();
            towns.Add(new TownRecord(
                (int)t["from"]!, (string)t["name"]!, (int)place["x"]!, (int)place["y"]!,
                t["people"]!.AsArray().Select(v => (int)v!).ToList(),
                t["owners"]!.AsArray().Select(o => ((int)o![0]!, (int?)o[1])).ToList(),
                t["ruins"]!.AsArray().Select(r => ((int)r![0]!, (int?)r[1])).ToList()));
        }
        Towns = towns;

        foreach (var f in history["factions"]!.AsArray())
        {
            factionFrom.Add((int)f!["from"]!);
            factionChanges.Add(f["changes"]!.AsArray().Select(c => ((int)c![0]!, c[1]!.AsObject())).ToList());
        }

        var borders = history["borders"]!.AsObject();
        BorderColumns = (int)borders["columns"]!;
        BorderRows = (int)borders["rows"]!;
        foreach (var s in borders["snapshots"]!.AsArray())
            snapshots.Add(((int)s![0]!, s[1]!.AsObject()));
    }

    /// <summary>The exported world in `folder` (or the folder of a world.json given itself): checks its format and version.</summary>
    public static ExportedWorld Read(string folder)
    {
        if (File.Exists(folder) && Path.GetFileName(folder) == "world.json")
            folder = Path.GetDirectoryName(Path.GetFullPath(folder))!;
        string path = Path.Combine(folder, "world.json");
        if (!File.Exists(path))
            throw new WorldFileException(folder + " has no world.json: export a world with worldgen.py --export");
        JsonObject data;
        try
        {
            data = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new WorldFileException(path + " is not JSON a world is written in: " + e.Message);
        }
        if ((string?)data["format"] != Format)
            throw new WorldFileException(path + " is not a world of the generator (format \"" + data["format"] + "\")");
        if (data["version"] is not JsonValue v || !v.TryGetValue(out int version) || version > SupportedVersion)
            throw new WorldFileException(path + " is version " + data["version"] + "; this game reads up to " + SupportedVersion);
        return new ExportedWorld(folder, data);
    }

    /// <summary>A file of the export, by its name in world.json (for example "terrain.png").</summary>
    public string PathOf(string file) => Path.Combine(Folder, file);

    /// <summary>One of the grids by its name in world.json's "grids" (height, water, biomes...), read once and kept.</summary>
    public Grid Grid(string name)
    {
        if (!grids.TryGetValue(name, out var grid))
        {
            var info = Data["grids"]![name] ?? throw new WorldFileException("no grid \"" + name + "\" in this world");
            grid = World.Grid.Read(PathOf((string)info["file"]!));
            grids[name] = grid;
        }
        return grid;
    }

    /// <summary>Settlement `index` in `year`, or null if it was not yet founded.</summary>
    public TownState? Town(int index, int year)
    {
        var town = Towns[index];
        if (year < town.From)
            return null;
        int? holder = null;
        foreach (var (since, faction) in town.Owners)
        {
            if (since > year)
                break;
            holder = faction;
        }
        return new TownState(holder, PeopleAt(town, year), RuinedAt(town, year));
    }

    /// <summary>Its people that year; after its record ends, its last year's.</summary>
    public static int PeopleAt(TownRecord town, int year) =>
        town.People.Count == 0 ? 0 : town.People[Math.Clamp(year - town.From, 0, town.People.Count - 1)];

    /// <summary>The most people it ever had, up to `year` (a ruin is drawn at that size).</summary>
    public static int PeakTo(TownRecord town, int year)
    {
        int peak = 0;
        for (int i = 0; i <= Math.Min(year - town.From, town.People.Count - 1); i++)
            peak = Math.Max(peak, town.People[i]);
        return peak;
    }

    public static bool RuinedAt(TownRecord town, int year) =>
        town.Ruins.Any(r => r.Fell <= year && (r.Back == null || year < r.Back));

    /// <summary>
    /// Faction `index` in `year`: its path, ruler, belief, government and capital, each as last changed,
    /// or null if it had not yet come to be. The values are JSON, as the export holds them.
    /// </summary>
    public JsonObject? Faction(int index, int year)
    {
        if (year < factionFrom[index])
            return null;
        var state = new JsonObject();
        foreach (var (since, changed) in factionChanges[index])
        {
            if (since > year)
                break;
            foreach (var (key, value) in changed)
                state[key] = value?.DeepClone();
        }
        return state;
    }

    /// <summary>The first year faction `index` is in the records (the year it came to be, or 0).</summary>
    public int FromOf(int index) => factionFrom[index];

    /// <summary>
    /// A faction the game founded (a split, a town that rose), from `year` on: added to Factions, with no people
    /// before that year, and its path, ruler and the rest written down by the AddYear of that year.
    /// </summary>
    public void AddFaction(FactionInfo info, int year)
    {
        factions.Add(info);
        ((List<List<int>>)Population).Add(new List<int>());
        factionFrom.Add(year);
        factionChanges.Add(new List<(int, JsonObject)>());
    }

    /// <summary>Faction `index`'s capital in `year`, or null (none, or not yet come to be).</summary>
    public int? CapitalOf(int index, int year)
    {
        if (year < factionFrom[index])
            return null;
        int? capital = null;
        foreach (var (since, changed) in factionChanges[index])
        {
            if (since > year)
                break;
            if (changed.TryGetPropertyValue("capital", out var value))
                capital = (int?)value;
        }
        return capital;
    }

    /// <summary>Faction `index`'s people in `year`.</summary>
    public int PeopleOf(int index, int year) =>
        index < Population.Count && year >= 0 && year < Population[index].Count ? Population[index][year] : 0;

    /// <summary>
    /// Who held each region of the history's grid in `year`, row after row (BorderColumns wide), -1 for no one;
    /// or null before the first snapshot.
    /// The first snapshot is the whole grid as runs ([holder, count, ...]); every later one only the stretches
    /// that changed ([start, count, holder, ...]), so the borders of a year are the first snapshot with every
    /// change up to that year applied.
    /// </summary>
    public int[]? Borders(int year)
    {
        int last = -1;                          // the last snapshot taken by `year`
        while (last + 1 < snapshots.Count && snapshots[last + 1].Year <= year)
            last++;
        if (last < 0)
            return null;
        // Applying every change from the first snapshot gets slow late in a long history, so a copy is
        // kept every CheckEvery snapshots and the borders start from the nearest copy before `last`.
        int from = last / CheckEvery;
        int[] flat = (int[])Checkpoint(from).Clone();
        for (int i = from * CheckEvery + 1; i <= last; i++)
            Apply(flat, snapshots[i].Snapshot);
        return flat;
    }

    const int CheckEvery = 16;
    readonly List<int[]> checkpoints = new();

    // The borders as they were at snapshot number `n` * CheckEvery, made once and kept.
    int[] Checkpoint(int n)
    {
        while (checkpoints.Count <= n)
        {
            int k = checkpoints.Count;
            int[] flat = k == 0 ? new int[BorderColumns * BorderRows] : (int[])checkpoints[k - 1].Clone();
            for (int i = k == 0 ? 0 : (k - 1) * CheckEvery + 1; i <= k * CheckEvery; i++)
                Apply(flat, snapshots[i].Snapshot);
            checkpoints.Add(flat);
        }
        return checkpoints[n];
    }

    void Apply(int[] flat, JsonObject snapshot)
    {
        if (snapshot["full"] is JsonArray full)
        {
            int at = 0;
            for (int i = 0; i < full.Count; i += 2)
            {
                int holder = (int)full[i]!, count = (int)full[i + 1]!;
                Array.Fill(flat, holder, at, count);
                at += count;
            }
        }
        else
        {
            var changes = snapshot["changes"]!.AsArray();
            for (int i = 0; i < changes.Count; i += 3)
                Array.Fill(flat, (int)changes[i + 2]!, (int)changes[i]!, (int)changes[i + 1]!);
        }
    }

    /// <summary>
    /// Adds the next year (Years + 1) as the game ran it: each settlement's people, holder and whether it lies
    /// in ruins, in the order of Towns, and each faction's people, in the order of Factions. A holder or a ruin
    /// is only written down when it changes, as the export writes them. The borders are not changed: until war
    /// comes to the game (step 5), every year keeps the borders of the last.
    /// </summary>
    public void AddYear(IReadOnlyList<int> townPeople, IReadOnlyList<int?> holders, IReadOnlyList<bool> ruined,
                        IReadOnlyList<int> factionPeople, IReadOnlyList<JsonObject?>? factionStates = null,
                        IReadOnlyList<int>? regions = null)
    {
        if (townPeople.Count != Towns.Count || holders.Count != Towns.Count || ruined.Count != Towns.Count)
            throw new ArgumentException("one entry for every settlement");
        if (factionPeople.Count != Factions.Count)
            throw new ArgumentException("one entry for every faction");
        int year = Years + 1;
        for (int i = 0; i < Towns.Count; i++)
        {
            var town = Towns[i];
            if (year < town.From)
                continue;
            while (town.People.Count < year - town.From)            // a record that ended early: carry its last year
                town.People.Add(town.People.Count > 0 ? town.People[^1] : 0);
            town.People.Add(townPeople[i]);
            int? before = town.Owners.Count > 0 ? town.Owners[^1].Faction : null;
            if (holders[i] != before)
                town.Owners.Add((year, holders[i]));
            bool was = RuinedAt(town, year - 1);
            if (ruined[i] && !was)
                town.Ruins.Add((year, null));
            else if (!ruined[i] && was)
                town.Ruins[^1] = (town.Ruins[^1].Fell, year);
        }
        for (int f = 0; f < Factions.Count; f++)
        {
            var row = Population[f];
            while (row.Count < year)
                row.Add(row.Count > 0 ? row[^1] : 0);
            row.Add(factionPeople[f]);
        }
        // Each faction's path, ruler, belief, government and capital: written down only where they changed, as the
        // export writes them, so Faction(index, year) and the book's card show the game's years as the export's.
        if (factionStates != null)
            for (int f = 0; f < Factions.Count && f < factionStates.Count; f++)
            {
                if (factionStates[f] is not JsonObject now)
                    continue;
                var was = Faction(f, year - 1) ?? new JsonObject();
                var changed = new JsonObject();
                foreach (var (key, value) in now)
                    if (!JsonNode.DeepEquals(was[key], value))
                        changed[key] = value?.DeepClone();
                if (changed.Count > 0)
                    factionChanges[f].Add((year, changed));
            }
        // The borders, when they changed: only the stretches that changed, as the export's later snapshots are.
        if (regions != null && snapshots.Count > 0)
        {
            var was = Borders(year - 1)!;
            if (regions.Count != was.Length)
                throw new ArgumentException("one region for every region of the history's grid");
            var changes = new JsonArray();
            int at = 0;
            while (at < was.Length)
            {
                if (regions[at] == was[at])
                {
                    at++;
                    continue;
                }
                int start = at, holder = regions[at];
                while (at < was.Length && regions[at] == holder && regions[at] != was[at])
                    at++;
                changes.Add(start);
                changes.Add(at - start);
                changes.Add(holder);
            }
            if (changes.Count > 0)
                snapshots.Add((year, new JsonObject { ["changes"] = changes }));
        }
        Years = year;
    }

    /// <summary>The fields of a faction a year's record keeps (as the export's "history" → "factions" → "changes").</summary>
    public static JsonObject FactionState(string? path, string? ruler, string? belief, string? government, int? capital) =>
        new() { ["path"] = path, ["ruler"] = ruler, ["belief"] = belief, ["government"] = government, ["capital"] = capital };

    /// <summary>The name of the size a town of `people` has: the largest whose people it has reached.</summary>
    public string SizeOf(int people)
    {
        string name = SettlementSizes[0].Name;
        foreach (var (low, n) in SettlementSizes)
            if (people >= low)
                name = n;
        return name;
    }
}
