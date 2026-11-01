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
    /// <summary>Its place on the map (map cells), for the nearest town to a mine.</summary>
    public int X { get; set; }
    public int Y { get; set; }
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
    /// <summary>Its ruler's competence, 0 to 1, or null with no ruler. Rulers do not change until step 4.</summary>
    public double? Competence { get; set; }
    public int People { get; set; }
    /// <summary>The faction it comes from in the settings' FACTIONS (its goods), and its traits (greed digs more).</summary>
    public string? Lineage { get; set; }
    public double Greed { get; set; }
    /// <summary>Goods it has learned to make (chokepoints), and the paths and hybrids it has learned and how well.</summary>
    public HashSet<string> Learned { get; set; } = new();
    public Dictionary<string, double> Skills { get; set; } = new();
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
    /// <summary>The settlement whose land it is on, or null.</summary>
    public int? LandTown { get; set; }
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
                X = (int)place["x"]!,
                Y = (int)place["y"]!,
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
            };
        }).ToList();
        Routes = world.Data["routes"]!.AsArray()
            .Select(r => new Route { A = (int)r!["a"]!, B = (int)r["b"]!, Cost = (double)r["cost"]! }).ToList();
        Memory = new Dictionary<(int, int), double>();
        foreach (var pair in going["memory"]!.AsArray())
            Memory[((int)pair![0]!, (int)pair[1]!)] = (double)pair[2]!;
        var keptFactions = going["factions"] as JsonArray ?? throw Older("\"factions\"");
        Factions = world.Data["factions"]!.AsArray().Select((node, k) =>
        {
            var f = node!.AsObject();
            return new FactionNow
            {
                Name = (string)f["name"]!,
                Path = (string?)f["path"],
                Government = (string?)f["government"] ?? "",
                Competence = (double?)f["ruler"]?["traits"]?["competence"],
                People = (int?)f["population"] ?? 0,
                Lineage = (string?)f["lineage"],
                Greed = (double?)f["traits"]?["greed"] ?? 0.0,
                Learned = f["learned"] is JsonArray learned ? learned.Select(g => (string)g!).ToHashSet() : new HashSet<string>(),
                Skills = keptFactions[k]!["skills"]!.AsObject().ToDictionary(p => p.Key, p => (double)p.Value!),
                Opinions = f["opinions"]!.AsObject().ToDictionary(p => int.Parse(p.Key), p => (double)p.Value!),
                MetalSupply = (double?)f["metal_supply"] ?? 0.0,
                MetalNeed = (double?)f["metal_need"] ?? 0.0,
            };
        }).ToList();
    }

    // The export lacks a part of "continue" this game needs: it came from an older generator.
    static WorldFileException Older(string part) =>
        new("this world's \"continue\" has no " + part + ": export it again with the newer generator");

    /// <summary>A new game's seed, drawn once from the operating system's randomness (it is then saved, so the
    /// game itself never needs randomness it cannot repeat).</summary>
    public static long NewSeed() => (long)(System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue));
}
