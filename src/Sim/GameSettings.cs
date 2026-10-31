// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.World;

namespace WardensReckoning.Sim;

/// <summary>One good of the generator's GOODS (trade_settings.py), as far as the years need it so far.</summary>
public sealed record Good(string Name, JsonNode? Need, string? Food)
{
    /// <summary>How much every 1000 people of `path` need of it: its "need", one number or one per path. A town no
    /// faction holds needs as much as the path that needs the most (the generator's trade.need_of).</summary>
    public double NeedOf(string? path)
    {
        if (Need is JsonObject perPath)
        {
            if (path == null)
                return perPath.Select(p => (double)p.Value!).DefaultIfEmpty(0.0).Max();
            return perPath[path] is JsonNode n ? (double)n : 0.0;
        }
        return Need is null ? 0.0 : (double)Need;
    }
}

/// <summary>
/// The settings the history ran with, from the export's "continue" → "settings": the game uses the same values
/// as the generator, so a value changed in a Python settings file reaches the game with the next export.
/// Each property names the settings file and setting it comes from.
/// </summary>
public sealed class GameSettings
{
    /// <summary>history_settings.py GROWTH_RATE: a fed town grows by this share a year while it has room.</summary>
    public double GrowthRate { get; }
    /// <summary>history_settings.py STARVATION: a town with no food at all loses this share a year.</summary>
    public double Starvation { get; }
    /// <summary>history_settings.py HARVEST_VARIATION: how far a year's weather moves a harvest from an ordinary one.</summary>
    public double HarvestVariation { get; }
    /// <summary>history_settings.py RUIN_POPULATION and RUIN_YEARS: a town below the first for the second years falls.</summary>
    public int RuinPopulation { get; }
    public int RuinYears { get; }

    /// <summary>trade_settings.py GOODS: every good by name.</summary>
    public IReadOnlyDictionary<string, Good> Goods { get; }
    /// <summary>The staple foods (any of them fills a stomach) and the extra foods (each needed on its own), by name.</summary>
    public IReadOnlyList<string> Staples { get; }
    public IReadOnlyList<string> Extras { get; }

    /// <summary>faction_settings.py PATH_HARVEST and PATH_STEADINESS, by path.</summary>
    public IReadOnlyDictionary<string, double> PathHarvest { get; }
    public IReadOnlyDictionary<string, double> PathSteadiness { get; }

    /// <summary>leader_settings.py GOVERNMENTS: each government's "ruler_power".</summary>
    public IReadOnlyDictionary<string, double> RulerPower { get; }

    /// <summary>settlement_settings.py SETTLEMENT_SIZES: the people each size starts at, smallest first.</summary>
    public IReadOnlyList<int> SizeLows { get; }
    public IReadOnlyList<string> SizeNames { get; }

    public GameSettings(JsonObject settings)
    {
        var history = settings["history"]!.AsObject();
        GrowthRate = (double)history["GROWTH_RATE"]!;
        Starvation = (double)history["STARVATION"]!;
        HarvestVariation = (double)history["HARVEST_VARIATION"]!;
        RuinPopulation = (int)history["RUIN_POPULATION"]!;
        RuinYears = (int)history["RUIN_YEARS"]!;

        var goods = new SortedDictionary<string, Good>(StringComparer.Ordinal);
        foreach (var (name, details) in settings["trade"]!["GOODS"]!.AsObject())
        {
            var food = details!["food"];
            string? kind = food switch
            {
                null => null,
                JsonValue v when v.TryGetValue(out bool b) => b ? "staple" : null,
                _ => (string?)food,
            };
            goods[name] = new Good(name, details["need"]?.DeepClone(), kind);
        }
        Goods = goods;
        Staples = goods.Values.Where(g => g.Food == "staple").Select(g => g.Name).ToList();
        Extras = goods.Values.Where(g => g.Food == "extra").Select(g => g.Name).ToList();

        var factions = settings["factions"]!.AsObject();
        PathHarvest = Numbers(factions["PATH_HARVEST"]);
        PathSteadiness = Numbers(factions["PATH_STEADINESS"]);

        RulerPower = settings["leaders"]!["GOVERNMENTS"]!.AsObject()
            .ToDictionary(g => g.Key, g => (double?)g.Value!["ruler_power"] ?? 0.0);

        var sizes = settings["settlement"]!["SETTLEMENT_SIZES"]!.AsArray();
        SizeLows = sizes.Select(s => (int)s![0]!).ToList();
        SizeNames = sizes.Select(s => (string)s![1]!).ToList();
    }

    static Dictionary<string, double> Numbers(JsonNode? table) =>
        table is JsonObject o ? o.ToDictionary(p => p.Key, p => (double)p.Value!) : new Dictionary<string, double>();

    /// <summary>The settings of an exported world, or a WorldFileException if it has no "continue" this game can read.</summary>
    public static GameSettings Of(ExportedWorld world) => new(Continuation(world)["settings"]!.AsObject());

    /// <summary>The newest version of the export's "continue" this game understands.</summary>
    public const int SupportedContinue = 1;

    /// <summary>world.json's "continue": refused if missing or newer than this game knows.</summary>
    public static JsonObject Continuation(ExportedWorld world)
    {
        if (world.Data["continue"] is not JsonObject going)
            throw new WorldFileException("this world cannot be played on: it was exported without a history, " +
                                         "or by an older generator. Export it again with the newer generator.");
        if (going["version"] is not JsonValue v || !v.TryGetValue(out int version) || version > SupportedContinue)
            throw new WorldFileException("this world's \"continue\" is version " + going["version"] +
                                         "; this game reads up to " + SupportedContinue);
        return going;
    }
}
