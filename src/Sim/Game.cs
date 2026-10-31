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

    public Game(ExportedWorld world, long seed)
    {
        World = world;
        Seed = seed;
        Settings = GameSettings.Of(world);
        Year = world.Years;
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
            });
        }
        Factions = world.Data["factions"]!.AsArray().Select(node =>
        {
            var f = node!.AsObject();
            return new FactionNow
            {
                Name = (string)f["name"]!,
                Path = (string?)f["path"],
                Government = (string?)f["government"] ?? "",
                Competence = (double?)f["ruler"]?["traits"]?["competence"],
                People = (int?)f["population"] ?? 0,
            };
        }).ToList();
    }

    /// <summary>A new game's seed, drawn once from the operating system's randomness (it is then saved, so the
    /// game itself never needs randomness it cannot repeat).</summary>
    public static long NewSeed() => (long)(System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue));
}
