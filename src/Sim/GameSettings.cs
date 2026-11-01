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

/// <summary>One government of leader_settings.py GOVERNMENTS.</summary>
public sealed record Government(string Succession, double Tolerance, double RulerPower, IReadOnlyList<string> AfterRevolution);

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

    /// <summary>leader_settings.py GOVERNMENTS in full: how the next ruler is chosen, how much grievance the people
    /// bear before they may rise, how much a ruler matters, and what a revolution may turn it into.</summary>
    public IReadOnlyDictionary<string, Government> Governments { get; }
    /// <summary>leader_settings.py GRIEVANCE_HALF_LIFE and GRIEVANCE_WEIGHTS.</summary>
    public double GrievanceHalfLife { get; }
    public IReadOnlyDictionary<string, double> GrievanceWeights { get; }

    /// <summary>faction_settings.py PATHS, in their order: what each path suits ("planning": "high"...).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(string Trait, string Want)>> PathSuits { get; }
    /// <summary>faction_settings.py PATH_OPINIONS and PATH_OPINION_DEFAULT: what one path thinks of another.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> PathOpinions { get; }
    public double PathOpinionDefault { get; }
    /// <summary>faction_settings.py BELIEF_OPINIONS and RELATIONS (lowest opinion, word), low to high.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> BeliefOpinions { get; }
    public IReadOnlyList<(double Low, string Name)> Relations { get; }

    /// <summary>What path `mine` thinks of path `theirs`: PATH_OPINIONS, or PATH_OPINION_DEFAULT. factions.path_opinion.</summary>
    public double PathOpinion(string? mine, string? theirs) =>
        mine != null && theirs != null && PathOpinions.TryGetValue(mine, out var row) && row.TryGetValue(theirs, out double value)
            ? value : PathOpinionDefault;

    /// <summary>war_settings.py: WAR_WEIGHTS (hatred, want, ambition, belief), WARLIKE, BATTLE_DEATHS and
    /// PEACE_WEARINESS (each a number, or a range to draw from), ALLY_OPINION, SIZE_IN_BATTLE, RISE_AGAIN, JOIN_KIN,
    /// and REBELLION and RESENTMENT_HALF_LIFE (the engine's values when the settings do not say).</summary>
    public IReadOnlyDictionary<string, double> WarWeights { get; }
    public double Warlike { get; }
    public (double Low, double High) BattleDeaths { get; }
    public (double Low, double High) PeaceWeariness { get; }
    public double AllyOpinion { get; }
    public double SizeInBattle { get; }
    public double RiseAgain { get; }
    public double JoinKin { get; }
    public double Rebellion { get; }
    public double ResentmentHalfLife { get; }

    /// <summary>split_settings.py: how readily factions break apart (DIVISION, FAR, DISTANCE, LEADER_SPLIT), at most
    /// MAX_FACTIONS with land at once; merging (MERGE_OPINION); how new factions differ from their kin (TRAIT_SPREAD,
    /// BELIEF_CHANCE, GOVERNMENT_CHANCE, SPLIT_NAMES by path, OFFSHOOT_CHANCE); and empires (EMPIRE_SHARE,
    /// EMPIRE_HOLD, EMPIRE_SMALL, EMPIRE_JOIN_OPINION, EMPIRE_JOIN). A setting the export lacks is the engine's
    /// default, as in splits.py.</summary>
    public double Division { get; }
    public double Far { get; }
    public double Distance { get; }
    public double LeaderSplit { get; }
    public int MaxFactions { get; }
    public double MergeOpinion { get; }
    public double TraitSpread { get; }
    public double BeliefChance { get; }
    public double GovernmentChance { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SplitNames { get; }
    public double OffshootChance { get; }
    public double EmpireShare { get; }
    public double EmpireHold { get; }
    public int EmpireSmall { get; }
    public double EmpireJoinOpinion { get; }
    public double EmpireJoin { get; }

    /// <summary>One faction of faction_settings.py FACTIONS, as it starts: its name, colour, path and race, and its
    /// traits (planning, risk, greed, and those of the four more it has).</summary>
    public sealed record Start(string Name, byte Red, byte Green, byte Blue, string Path, string? Race,
                               IReadOnlyDictionary<string, double> Traits);
    public IReadOnlyDictionary<string, Start> Starts { get; }

    /// <summary>Feeding by tastes (tastes.py): settlement_settings.py BIOME_FOOD, DEFAULT_FOOD, PEOPLE_PER_FOOD and
    /// PEOPLE_FOOD (a people's own food by biome); trade_settings.py GOODS' "biomes" (what a biome yields of each good,
    /// in the order of GOODS) and PEOPLE_GOODS; faction_settings.py PATH_LAND (the biome a path farms another as).</summary>
    public IReadOnlyDictionary<string, double> BiomeFood { get; }
    public double DefaultFood { get; }
    public double PeoplePerFood { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> PeopleFood { get; }
    public IReadOnlyList<(string Good, IReadOnlyDictionary<string, double> Biomes)> GoodBiomes { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>> PeopleGoods { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> PathLand { get; }

    /// <summary>Whether any people or path grows food or goods its own way: then every town is fed by the people
    /// holding it now (tastes.Tastes.feeds_its_own).</summary>
    public bool FeedsItsOwn => PeopleFood.Count > 0 || PeopleGoods.Count > 0 || PathLand.Values.Any(v => v.Count > 0);

    /// <summary>migration_settings.py DRIFT_HALF_LIFE, and faction_settings.py LAND_INFLUENCES as (kind of land, trait,
    /// change): how the land a faction holds slowly changes its ways.</summary>
    public double DriftHalfLife { get; }
    public IReadOnlyList<(string Kind, string Trait, double Change)> LandInfluences { get; }

    /// <summary>migration_settings.py: when a town is crowded (CROWDED of its capacity) or hungry (HUNGRY_YEARS in a
    /// row), the chance a year it sends migrants (MIGRATION), the share who go (MIGRANTS, a number or a range), how
    /// far they go (REACH, cells of a 256 map) and the most towns the world holds (MOST_SETTLEMENTS).</summary>
    public double Crowded { get; }
    public int HungryYears { get; }
    public double MigrationChance { get; }
    public (double Low, double High) Migrants { get; }
    /// <summary>Whether MIGRANTS is a range (each migration then draws its own share) or one number.</summary>
    public bool MigrantsDrawn { get; }
    public double Reach { get; }
    public int MostSettlements { get; }

    /// <summary>settlement_settings.py: how far apart towns must be (MIN_DISTANCE, cells of a 256 map), the lowest site
    /// score worth settling (MIN_SITE_SCORE), the usual site weights (SITE_WEIGHTS) and each people's own
    /// (PEOPLE_SITES), in the order they are listed.</summary>
    public double MinDistance { get; }
    public double MinSiteScore { get; }
    public IReadOnlyList<(string Part, double Weight)> SiteWeights { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<(string Part, double Weight)>> PeopleSites { get; }

    /// <summary>The lore of 1 November 2026: leader_settings.py LIFESPAN_SCALING (how fully a people's lifespan sets its
    /// rulers' ages), faction_settings.py RACE_OPINIONS (what one race thinks of another), and the export's races
    /// (race_settings.py RACES: each race's lifespan, and the race a kind belongs to, "of").</summary>
    public double LifespanScaling { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> RaceOpinions { get; }
    public IReadOnlyDictionary<string, (double? Lifespan, string? Of)> Races { get; }

    /// <summary>
    /// Paths as skills (learning_settings.py, step 7): PATHS_AS_SKILLS switches it on; START_PATHS the paths each
    /// faction of FACTIONS starts holding besides its main one; HYBRIDS the two paths each hybrid joins; LEARNED_SKILL
    /// how well a learned path's goods are made; the yearly chances to learn a path by trade, alliance, a shared
    /// border, conquest and effort; and FORGET_CHANCE, the yearly chance to forget one no one in touch holds.
    /// </summary>
    public bool PathsAsSkills { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> StartPaths { get; }
    public IReadOnlyDictionary<string, (string First, string Second)> Hybrids { get; }
    public double LearnedSkill { get; }
    public double LearnByTrade { get; }
    public double LearnByAlliance { get; }
    public double LearnByBorder { get; }
    public double LearnByConquest { get; }
    public double LearnByEffort { get; }
    public double ForgetChance { get; }

    /// <summary>Capitals (split_settings.py, step 7): CAPITAL_CHOICE, "weighted" (a new capital drawn, bigger towns
    /// likelier) or "largest"; CAPITAL_MOVE_CHANCE, the yearly chance a faction moves its capital.</summary>
    public string CapitalChoice { get; }
    public double CapitalMoveChance { get; }

    static (double, double) Range(JsonNode? value) => value switch
    {
        JsonArray pair => ((double)pair[0]!, (double)pair[1]!),
        null => (0.0, 0.0),
        _ => ((double)value, (double)value),
    };

    /// <summary>settlement_settings.py SETTLEMENT_SIZES: the people each size starts at, smallest first.</summary>
    public IReadOnlyList<int> SizeLows { get; }
    public IReadOnlyList<string> SizeNames { get; }

    public GameSettings(JsonObject settings, JsonObject? races = null)
    {
        Races = races?.ToDictionary(r => r.Key, r => ((double?)r.Value?["lifespan"], (string?)r.Value?["of"]))
                ?? new Dictionary<string, (double?, string?)>();
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

        var leaders = settings["leaders"]!.AsObject();
        Governments = leaders["GOVERNMENTS"]!.AsObject().ToDictionary(g => g.Key, g => new Government(
            (string?)g.Value!["succession"] ?? "chosen", (double?)g.Value["tolerance"] ?? 1.0,
            (double?)g.Value["ruler_power"] ?? 0.0,
            g.Value["after_revolution"] is JsonArray after ? after.Select(a => (string)a!).ToList() : new List<string>()));
        GrievanceHalfLife = (double)leaders["GRIEVANCE_HALF_LIFE"]!;
        GrievanceWeights = Numbers(leaders["GRIEVANCE_WEIGHTS"]);
        // A Dictionary keeps the order things were added in (while nothing is removed): the order of PATHS.
        PathSuits = factions["PATHS"]!.AsObject().ToDictionary(p => p.Key, p => (IReadOnlyList<(string, string)>)(
            p.Value!["suits"] is JsonObject suits ? suits.Select(t => (t.Key, (string)t.Value!)).ToList() : new List<(string, string)>()));
        PathOpinions = Tables(factions["PATH_OPINIONS"]);
        PathOpinionDefault = (double?)factions["PATH_OPINION_DEFAULT"] ?? 0.0;
        BeliefOpinions = Tables(factions["BELIEF_OPINIONS"]);
        Relations = factions["RELATIONS"]!.AsArray().Select(r => ((double)r![0]!, (string)r[1]!)).ToList();

        var war = settings["war"]!.AsObject();
        WarWeights = Numbers(war["WAR_WEIGHTS"]);
        Warlike = (double?)war["WARLIKE"] ?? 0.0;
        BattleDeaths = Range(war["BATTLE_DEATHS"]);
        PeaceWeariness = Range(war["PEACE_WEARINESS"]);
        AllyOpinion = (double?)war["ALLY_OPINION"] ?? 1.0;
        SizeInBattle = (double?)war["SIZE_IN_BATTLE"] ?? 0.0;
        RiseAgain = (double?)war["RISE_AGAIN"] ?? 1.0;
        JoinKin = (double?)war["JOIN_KIN"] ?? 0.5;
        Rebellion = (double?)war["REBELLION"] ?? Sim.War.Rebellion;
        ResentmentHalfLife = (double?)war["RESENTMENT_HALF_LIFE"] ?? Sim.War.ResentmentHalfLife;

        var splits = settings["splits"] as JsonObject ?? new JsonObject();
        Division = (double?)splits["DIVISION"] ?? 0.0;
        Far = (double?)splits["FAR"] ?? double.PositiveInfinity;
        Distance = (double?)splits["DISTANCE"] ?? 0.0;
        LeaderSplit = (double?)splits["LEADER_SPLIT"] ?? 0.0;
        MaxFactions = (int?)splits["MAX_FACTIONS"] ?? int.MaxValue;
        MergeOpinion = (double?)splits["MERGE_OPINION"] ?? double.PositiveInfinity;
        TraitSpread = (double?)splits["TRAIT_SPREAD"] ?? 0.0;
        BeliefChance = (double?)splits["BELIEF_CHANCE"] ?? 0.0;
        GovernmentChance = (double?)splits["GOVERNMENT_CHANCE"] ?? 0.0;
        SplitNames = splits["SPLIT_NAMES"] is JsonObject names
            ? names.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value!.AsArray().Select(n => (string)n!).ToList())
            : new Dictionary<string, IReadOnlyList<string>>();
        OffshootChance = (double?)splits["OFFSHOOT_CHANCE"] ?? 0.0;
        CapitalChoice = (string?)splits["CAPITAL_CHOICE"] ?? "largest";
        CapitalMoveChance = (double?)splits["CAPITAL_MOVE_CHANCE"] ?? 0.0;
        EmpireShare = (double?)splits["EMPIRE_SHARE"] ?? 2.0;
        EmpireHold = (double?)splits["EMPIRE_HOLD"] ?? 1.0;
        EmpireSmall = (int?)splits["EMPIRE_SMALL"] ?? 0;
        EmpireJoinOpinion = (double?)splits["EMPIRE_JOIN_OPINION"] ?? 0.0;
        EmpireJoin = (double?)splits["EMPIRE_JOIN"] ?? 0.0;
        Starts = factions["FACTIONS"] is JsonObject starts
            ? starts.ToDictionary(p => p.Key, p =>
            {
                var f = p.Value!.AsObject();
                var c = f["colour"]!.AsArray();
                var traits = Splits.Traits.Concat(Splits.ExtraTraits).Where(t => f[t] != null)
                    .ToDictionary(t => t, t => (double)f[t]!);
                return new Start((string)f["name"]!, (byte)(int)c[0]!, (byte)(int)c[1]!, (byte)(int)c[2]!,
                                 (string)f["path"]!, (string?)f["race"], traits);
            })
            : new Dictionary<string, Start>();

        var settlement = settings["settlement"]!.AsObject();
        BiomeFood = Numbers(settlement["BIOME_FOOD"]);
        DefaultFood = (double?)settlement["DEFAULT_FOOD"] ?? 0.0;
        PeoplePerFood = (double?)settlement["PEOPLE_PER_FOOD"] ?? 0.0;
        PeopleFood = Tables(settlement["PEOPLE_FOOD"]);
        GoodBiomes = trading["GOODS"]!.AsObject()
            .Select(g => (g.Key, (IReadOnlyDictionary<string, double>)Numbers(g.Value!["biomes"]))).ToList();
        PeopleGoods = trading["PEOPLE_GOODS"] is JsonObject peopleGoods
            ? peopleGoods.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>)
                p.Value!.AsObject().ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, double>)Numbers(g.Value)))
            : new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>();
        PathLand = factions["PATH_LAND"] is JsonObject pathLand
            ? pathLand.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)
                p.Value!.AsObject().ToDictionary(b => b.Key, b => (string)b.Value!))
            : new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var moving = settings["migration"] as JsonObject ?? new JsonObject();
        Crowded = (double?)moving["CROWDED"] ?? double.PositiveInfinity;
        HungryYears = (int?)moving["HUNGRY_YEARS"] ?? int.MaxValue;
        MigrationChance = (double?)moving["MIGRATION"] ?? 0.0;
        Migrants = Range(moving["MIGRANTS"]);
        MigrantsDrawn = moving["MIGRANTS"] is JsonArray;
        Reach = (double?)moving["REACH"] ?? 0.0;
        MostSettlements = (int?)moving["MOST_SETTLEMENTS"] ?? 0;
        MinDistance = (double)settlement["MIN_DISTANCE"]!;
        MinSiteScore = (double)settlement["MIN_SITE_SCORE"]!;
        static IReadOnlyList<(string, double)> Weights(JsonNode? node) =>
            node is JsonObject o ? o.Select(p => (p.Key, (double)p.Value!)).ToList() : new List<(string, double)>();
        SiteWeights = Weights(settlement["SITE_WEIGHTS"]);
        PeopleSites = settlement["PEOPLE_SITES"] is JsonObject sites
            ? sites.Where(p => p.Value?["weights"] is JsonObject)
                   .ToDictionary(p => p.Key, p => Weights(p.Value!["weights"]))
            : new Dictionary<string, IReadOnlyList<(string, double)>>();
        DriftHalfLife = (double?)moving["DRIFT_HALF_LIFE"] ?? double.PositiveInfinity;
        LandInfluences = factions["LAND_INFLUENCES"] is JsonArray influences
            ? influences.Select(i => ((string)i![0]!, (string)i[1]!, (double)i[2]!)).ToList()
            : new List<(string, string, double)>();

        LifespanScaling = (double?)leaders["LIFESPAN_SCALING"] ?? 0.0;

        var learning = settings["learning"] as JsonObject ?? new JsonObject();
        PathsAsSkills = (bool?)learning["PATHS_AS_SKILLS"] ?? false;
        StartPaths = learning["START_PATHS"] is JsonObject startPaths
            ? startPaths.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value!.AsArray().Select(n => (string)n!).ToList())
            : new Dictionary<string, IReadOnlyList<string>>();
        Hybrids = learning["HYBRIDS"] is JsonObject hybrids
            ? hybrids.ToDictionary(p => p.Key, p => ((string)p.Value![0]!, (string)p.Value[1]!))
            : new Dictionary<string, (string, string)>();
        LearnedSkill = (double?)learning["LEARNED_SKILL"] ?? 0.0;
        LearnByTrade = (double?)learning["LEARN_BY_TRADE"] ?? 0.0;
        LearnByAlliance = (double?)learning["LEARN_BY_ALLIANCE"] ?? 0.0;
        LearnByBorder = (double?)learning["LEARN_BY_BORDER"] ?? 0.0;
        LearnByConquest = (double?)learning["LEARN_BY_CONQUEST"] ?? 0.0;
        LearnByEffort = (double?)learning["LEARN_BY_EFFORT"] ?? 0.0;
        ForgetChance = (double?)learning["FORGET_CHANCE"] ?? 0.0;
        RaceOpinions = Tables(factions["RACE_OPINIONS"]);

        var sizes = settlement["SETTLEMENT_SIZES"]!.AsArray();
        SizeLows = sizes.Select(s => (int)s![0]!).ToList();
        SizeNames = sizes.Select(s => (string)s![1]!).ToList();
    }

    static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Tables(JsonNode? table) =>
        table is JsonObject o
            ? o.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, double>)Numbers(p.Value))
            : new Dictionary<string, IReadOnlyDictionary<string, double>>();

    static Dictionary<string, double> Numbers(JsonNode? table) =>
        table is JsonObject o ? o.ToDictionary(p => p.Key, p => (double)p.Value!) : new Dictionary<string, double>();

    /// <summary>The settings of an exported world, or a WorldFileException if it has no "continue" this game can read.</summary>
    public static GameSettings Of(ExportedWorld world) =>
        new(Continuation(world)["settings"]!.AsObject(), world.Data["races"]?["races"] as JsonObject);

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
