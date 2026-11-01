// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.World;

namespace WardensReckoning.Sim;

/// <summary>
/// One good of the generator's GOODS (trade_settings.py): what it says about who needs and makes the good.
/// `MadeBy` is how many each 1000 people of a path or faction lineage make; `Uses` the metal each one made takes;
/// `Learned` how many each 1000 make once their faction has learned it; `MadeOn` the kinds of place it is only made
/// near; `Wanted` a good wanted, not needed (a shortage of it is no hardship).
/// </summary>
public sealed record Good(string Name, JsonNode? Need, string? Food, IReadOnlyDictionary<string, double> MadeBy,
                          double UsesMetal, double? Learned, IReadOnlyList<string> MadeOn, bool Wanted)
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

    /// <summary>Made by people (of a path, a lineage, or who learned it), not only grown on the land. trade.is_crafted.</summary>
    public bool IsCrafted => MadeBy.Count > 0 || Learned != null;
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

    /// <summary>trade_settings.py GOODS: every good by name, and in the order the settings list them (which is
    /// the order the generator goes through them).</summary>
    public IReadOnlyDictionary<string, Good> Goods { get; }
    public IReadOnlyList<Good> GoodsInOrder { get; }

    /// <summary>trade_settings.py MIN_OPINION_TO_TRADE, TRADE_HANDS and TRADE_REACH (see Trade.cs).</summary>
    public double MinOpinionToTrade { get; }
    public int TradeHands { get; }
    public double TradeReach { get; }

    /// <summary>faction_settings.py METAL_NEED: metal each 1000 people of a path need; PATHS: each path's root
    /// (an offshoot path makes its root's goods).</summary>
    public IReadOnlyDictionary<string, double> MetalNeed { get; }
    public IReadOnlyDictionary<string, string> PathRoot { get; }

    /// <summary>history_settings.py MEMORY_HALF_LIFE and TRADE_WARMTH: how fast memories fade, and how much trade
    /// warms them.</summary>
    public double MemoryHalfLife { get; }
    public double TradeWarmth { get; }
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

        var inOrder = new List<Good>();
        foreach (var (name, details) in settings["trade"]!["GOODS"]!.AsObject())
        {
            var food = details!["food"];
            string? kind = food switch
            {
                null => null,
                JsonValue v when v.TryGetValue(out bool b) => b ? "staple" : null,
                _ => (string?)food,
            };
            inOrder.Add(new Good(name, details["need"]?.DeepClone(), kind, Numbers(details["made_by"]),
                                 (double?)details["uses"]?["metal"] ?? 0.0, (double?)details["learned"],
                                 details["made_on"] is JsonArray on ? on.Select(k => (string)k!).ToList() : new List<string>(),
                                 (bool?)details["wanted"] ?? false));
        }
        GoodsInOrder = inOrder;
        Goods = inOrder.ToDictionary(g => g.Name);
        // Python's sorted(): by name, in the order of the characters' codes.
        Staples = inOrder.Where(g => g.Food == "staple").Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Extras = inOrder.Where(g => g.Food == "extra").Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        var trading = settings["trade"]!.AsObject();
        MinOpinionToTrade = (double?)trading["MIN_OPINION_TO_TRADE"] ?? -1.0;
        TradeHands = trading["TRADE_HANDS"] is JsonValue hands && hands.TryGetValue(out int h) ? h : 0;
        TradeReach = (double?)trading["TRADE_REACH"] ?? double.PositiveInfinity;
        MemoryHalfLife = (double)history["MEMORY_HALF_LIFE"]!;
        TradeWarmth = (double)history["TRADE_WARMTH"]!;

        var factions = settings["factions"]!.AsObject();
        PathHarvest = Numbers(factions["PATH_HARVEST"]);
        PathSteadiness = Numbers(factions["PATH_STEADINESS"]);
        MetalNeed = Numbers(factions["METAL_NEED"]);
        PathRoot = factions["PATHS"]!.AsObject().ToDictionary(p => p.Key, p => (string?)p.Value!["root"] ?? p.Key);

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
