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

    /// <summary>
    /// Attunement's mark (attunement_settings.py, step 8): how much metal used marks the land (MARK_PER_METAL), how
    /// fast it fades (MARK_HALF_LIFE), how much rivers and animals carry on (RIVER_SHARE, ANIMAL_SHARE); the marks
    /// at which the land is uneasy, preyed upon, given up and never fades (FAINT, STRONG, SEVERE, PERMANENT); the
    /// harvest lost at STRONG (HARVEST_LOSS), the people things take a year (PREY); the blame for another's mark
    /// (BLAME, and PATH_BLAME by path). The harvest ritual: HARVEST_RITUAL_CHANCE, the harvest it brings
    /// (HARVEST_RITUAL, a range), how much the ruler's competence moves it (HARVEST_RITUAL_SKILL), the metal it takes
    /// (HARVEST_RITUAL_METAL), the mark when it goes wrong (HARVEST_RITUAL_MARK), how much less others think of the
    /// faction (HARVEST_RITUAL_UNEASE), and the poor harvest that frightens them (HARVEST_RITUAL_POOR).
    /// </summary>
    public bool Attuning { get; }
    public double MarkPerMetal { get; }
    public double MarkHalfLife { get; }
    public double RiverShare { get; }
    public double AnimalShare { get; }
    public double Faint { get; }
    public double Strong { get; }
    public double Severe { get; }
    public double Permanent { get; }
    public double HarvestLoss { get; }
    public double Prey { get; }
    public double Blame { get; }
    public IReadOnlyDictionary<string, double> PathBlame { get; }
    public double HarvestRitualChance { get; }
    public (double Low, double High) HarvestRitual { get; }
    public double HarvestRitualSkill { get; }
    public double HarvestRitualMetal { get; }
    public double HarvestRitualMark { get; }
    public double HarvestRitualUnease { get; }
    public double HarvestRitualPoor { get; }
    /// <summary>The great ritual: how weary a losing side must be (RITUAL_DESPERATION), the yearly chance
    /// (RITUAL_CHANCE), the share of the metal it takes (RITUAL_METAL), the share of the enemy's people it kills
    /// (RITUAL_DEATHS, or a range), and the crater's size (CRATER_RADIUS, on a 256 map).</summary>
    public double RitualDesperation { get; }
    public double RitualChance { get; }
    public double RitualMetal { get; }
    public (double Low, double High) RitualDeaths { get; }
    public double CraterRadius { get; }

    /// <summary>
    /// Offshoot paths (split_settings.py, step 8c): OFFSHOOT_CHANCE (above) is the chance a new faction founds a path
    /// of its own, PATH_SPREAD how far each of its values may move from its parent's; faction_settings.py PATH_NAMES
    /// the names for the paths that branch from each root path, in turn; split_settings.py STANCES the stances a
    /// founder may take towards all others, in order: (name, chance, how much its memory of every other moves).
    /// </summary>
    public double PathSpread { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PathNames { get; }
    public IReadOnlyList<(string Name, double Chance, double Memory)> Stances { get; }
    /// <summary>Each path's ritual factor ("rituals" in PATHS: 1 for your own paths), which multiplies the chances of
    /// its factions' rituals (paths.ritual_factor).</summary>
    public IReadOnlyDictionary<string, double> PathRituals { get; }
    /// <summary>Whether the tables of paths by name are there (as the generator's book copies only those that are).</summary>
    bool hasPathOpinions;

    /// <summary>One path the game founded, with the values it was given (for a save: they are put back in order).</summary>
    public sealed record FoundedPath(string Name, string Parent, double Rituals, double? MetalNeed, double? Harvest,
                                     double? Steadiness, IReadOnlyList<(string Path, double Value)> Row,
                                     IReadOnlyList<(string Path, double Value)> Column, double? Blame);

    /// <summary>The paths the game founded, in order.</summary>
    public List<FoundedPath> Founded { get; } = new();

    /// <summary>A path's ritual factor (paths.ritual_factor): 1 for a path without one.</summary>
    public double RitualFactor(string? path) => path != null ? PathRituals.GetValueOrDefault(path, 1.0) : 1.0;

    /// <summary>
    /// A new path branching from `parent`, founded by `faction` (the generator's paths.Book.found): named from
    /// PATH_NAMES for its root, or "&lt;faction&gt; way"; every value its parent's moved by a draw of `draw` (a number
    /// between -PATH_SPREAD and PATH_SPREAD), in the generator's order: its ritual factor, METAL_NEED, PATH_HARVEST,
    /// PATH_STEADINESS (0 to 1), what it thinks of every other path and of itself, what every other path thinks of it
    /// (-1 to 1), and PATH_BLAME. Its suits, abilities and PATH_LAND are its parent's. Returns its name.
    /// </summary>
    public string FoundPath(string parent, string faction, Func<double> draw)
    {
        string root = PathRoot.GetValueOrDefault(parent, parent);
        string name = PathNameFor(root, faction);
        double Moved(double value, double? low = null, double? high = null)
        {
            value += draw();
            if (low is double l)
                value = Math.Max(l, value);
            if (high is double h)
                value = Math.Min(h, value);
            return value;
        }
        double rituals = Moved(PathRituals.GetValueOrDefault(parent, 1.0), 0.0);
        double? metal = MetalNeed.TryGetValue(parent, out double m) ? Moved(m, 0.0) : null;
        double? harvest = PathHarvest.TryGetValue(parent, out double hv) ? Moved(hv, 0.0) : null;
        double? steady = PathSteadiness.TryGetValue(parent, out double st) ? Moved(st, 0.0, 1.0) : null;
        var row = new List<(string, double)>();
        var column = new List<(string, double)>();
        if (hasPathOpinions)
        {
            var others = PathSuits.Keys.ToList();                   // every path before this one, in order
            foreach (string other in others)
                row.Add((other, Moved(PathOpinion(parent, other), -1.0, 1.0)));
            row.Add((name, Moved(PathOpinion(parent, parent), -1.0, 1.0)));
            foreach (string other in others)
                column.Add((other, Moved(PathOpinion(other, parent), -1.0, 1.0)));
        }
        double? blame = Attuning && PathBlame.TryGetValue(parent, out double b) ? Moved(b) : null;
        var founded = new FoundedPath(name, parent, rituals, metal, harvest, steady, row, column, blame);
        AddPath(founded);
        return name;
    }

    /// <summary>The name for a path branching from `root` (paths.Book.name_for): the first of PATH_NAMES[root] not yet
    /// a path, or "&lt;faction&gt; way", then "&lt;faction&gt; way 2" and on.</summary>
    string PathNameFor(string root, string faction)
    {
        foreach (string listed in PathNames.GetValueOrDefault(root) ?? new List<string>())
            if (!PathSuits.ContainsKey(listed))
                return listed;
        string plain = Splits.WithoutArticle(faction);
        string name = plain + " way";
        for (int number = 2; PathSuits.ContainsKey(name); number++)
            name = plain + " way " + number;
        return name;
    }

    /// <summary>Put a founded path into every table by path (for FoundPath, and for a save being read).</summary>
    public void AddPath(FoundedPath p)
    {
        static void Put<T>(IReadOnlyDictionary<string, T> table, string key, T value) => ((Dictionary<string, T>)table)[key] = value;
        Put(PathSuits, p.Name, PathSuits[p.Parent]);
        Put(PathAbilities, p.Name, PathAbilities[p.Parent]);
        Put(PathRoot, p.Name, PathRoot.GetValueOrDefault(p.Parent, p.Parent));
        Put(PathRituals, p.Name, p.Rituals);
        if (p.MetalNeed is double metal)
            Put(MetalNeed, p.Name, metal);
        if (p.Harvest is double harvest)
            Put(PathHarvest, p.Name, harvest);
        if (p.Steadiness is double steady)
            Put(PathSteadiness, p.Name, steady);
        if (PathLand.TryGetValue(p.Parent, out var land))
            Put(PathLand, p.Name, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(land));
        foreach (var (other, value) in p.Column)
        {
            if (!PathOpinions.TryGetValue(other, out var theirs))
                Put(PathOpinions, other, theirs = new Dictionary<string, double>());
            ((Dictionary<string, double>)theirs)[p.Name] = value;
        }
        if (p.Row.Count > 0)
            Put(PathOpinions, p.Name, (IReadOnlyDictionary<string, double>)p.Row.ToDictionary(r => r.Path, r => r.Value));
        if (p.Blame is double blame)
            Put(PathBlame, p.Name, blame);
        Founded.Add(p);
    }

    /// <summary>
    /// The god of death (death_settings.py, step 9a): METAL_DELAY, how many years the metal takes to form, and
    /// METAL_PER_DEATH, how much forms for each of the dead (each a number, or a range drawn for every site);
    /// DIG_ANGER, how much less those who fear or worship her think of a finder; SIGNS, what is seen after a great death,
    /// in order.
    /// </summary>
    public (double Low, double High, bool IsRange) MetalDelay { get; }
    public (double Low, double High, bool IsRange) MetalPerDeath { get; }
    public double DigAnger { get; }
    public sealed record Sign(string Name, double Chance, double Dead, double Reach, (string Belief, double Chance)? Belief,
                              double Harvest, int Years);
    public IReadOnlyList<Sign> Signs { get; }

    /// <summary>
    /// One kind of named threat (threat_settings.py THREAT_KINDS, step 9b), with threats.DEFAULTS for every key it
    /// leaves out: how likely it is against the others ("weight"); the biomes its lair may be in (none: any land);
    /// its strength and the share of a town's people it kills (each a number, or a range drawn each time); how far
    /// it raids, in cells of a 256 map ("reach"); the chances a year that it raids, moves, sleeps or fades; how long
    /// it sleeps; the raided town's harvest the next year as a factor; the chance it is slain when beaten off; the
    /// names it is given in turn; and whether it is undead (it rises where magic's mark is strong, if it can).
    /// </summary>
    public sealed record ThreatKind(string Name, double Weight, IReadOnlyList<string> Biomes,
                                    (double Low, double High, bool IsRange) Strength, double Reach, double Raid,
                                    (double Low, double High, bool IsRange) Deaths, double Harvest, double Slain,
                                    double Move, double Sleep, (int Low, int High) SleepYears, double Fade,
                                    IReadOnlyList<string> Names, bool Undead);
    /// <summary>The kinds of threat, in your order (a new threat's kind is drawn by their weights).</summary>
    public IReadOnlyList<ThreatKind> ThreatKinds { get; }
    /// <summary>threat_settings.py RISE_CHANCE: the chance a year that a new threat rises.</summary>
    public double RiseChance { get; }

    /// <summary>
    /// One happening (happening_settings.py HAPPENINGS, step 9c), with happenings.DEFAULTS for every key it leaves out:
    /// its chance a year (in each town, or for the whole world with reach "world"), its reach ("town", "region",
    /// "routes" or "world"), the radius of a region in cells of a 256 map, the chance a year it spreads along each
    /// route, and how many years its lasting effects last. What it does at once: a share of the people gained or lost
    /// ("population"), a share driven off along the routes ("driven_off"), a belief its faction may come to
    /// ([belief, chance]), and blame of the faction it started in. What lasts, in the generator's order (growth,
    /// harvest, trade, war): a factor for each. A "when" rule is Python code, so it cannot be exported: the game has none.
    /// </summary>
    public sealed record Happening(string Name, double Chance, string Reach, double Radius, double Spread, int Years,
                                   double Population, double DrivenOff, (string Belief, double Chance)? Belief, double Blame,
                                   IReadOnlyList<(string Key, double Factor)> Lasting);
    /// <summary>The happenings, in your order (the order they roll in).</summary>
    public IReadOnlyList<Happening> Happenings { get; }

    /// <summary>A temper (TEMPERS): how likely it is drawn when an era begins ("weight"), and in its eras every
    /// happening's chance times "chance" (and some happenings' once more, "happenings"), every faction's readiness for
    /// war times "war", and every faction's chance of breaking apart times "splits".</summary>
    public sealed record Temper(string Name, double Weight, double Chance, IReadOnlyDictionary<string, double> Moods,
                                double War, double Splits);
    /// <summary>The tempers in your order (happenings.STEADY when you give none); ERA_YEARS, how long an era lasts;
    /// ERA_END_CHANCE and ERA_ENDERS, the events that may end an era early.</summary>
    public IReadOnlyList<Temper> Tempers { get; }
    public (int Low, int High) EraYears { get; }
    public double EraEndChance { get; }
    public IReadOnlySet<string> EraEnders { get; }

    /// <summary>The temper named `name`.</summary>
    public Temper TemperOf(string name) => Tempers.First(t => t.Name == name);

    /// <summary>happenings.LASTING: the effects that last, in this order.</summary>
    public static readonly string[] LastingEffects = { "growth", "harvest", "trade", "war" };

    static Happening ReadHappening(string name, JsonNode? about)
    {
        // happenings.DEFAULTS: chance 0, reach "town", radius 20, spread 0.3, years 1, no effects.
        var effects = about?["effects"] as JsonObject ?? new JsonObject();
        return new Happening(name, (double?)about?["chance"] ?? 0.0, (string?)about?["reach"] ?? "town",
            (double?)about?["radius"] ?? 20, (double?)about?["spread"] ?? 0.3, (int?)about?["years"] ?? 1,
            (double?)effects["population"] ?? 0.0, (double?)effects["driven_off"] ?? 0.0,
            effects["belief"] is JsonArray b ? ((string)b[0]!, (double)b[1]!) : null, (double?)effects["blame"] ?? 0.0,
            LastingEffects.Where(key => effects[key] != null).Select(key => (key, (double)effects[key]!)).ToList());
    }

    /// <summary>The kind of threat named `name`.</summary>
    public ThreatKind KindOf(string name) => ThreatKinds.First(kind => kind.Name == name);

    static ThreatKind ReadThreatKind(string name, JsonNode? about)
    {
        // threats.DEFAULTS: weight 1, any land, strength 1000, reach 30, raid 0.1, deaths 0.1, harvest 1, slain 0.2,
        // move 0, sleep 0, sleep_years 20 to 60, fade 0, no names, not undead.
        var sleepYears = about?["sleep_years"] as JsonArray;
        return new ThreatKind(name, (double?)about?["weight"] ?? 1.0,
            (about?["biomes"] as JsonArray)?.Select(b => (string)b!).ToList() ?? new List<string>(),
            about?["strength"] == null ? (1000.0, 1000.0, false) : Setting(about["strength"]),
            (double?)about?["reach"] ?? 30, (double?)about?["raid"] ?? 0.1,
            about?["deaths"] == null ? (0.1, 0.1, false) : Setting(about["deaths"]),
            (double?)about?["harvest"] ?? 1.0, (double?)about?["slain"] ?? 0.2, (double?)about?["move"] ?? 0.0,
            (double?)about?["sleep"] ?? 0.0, sleepYears == null ? (20, 60) : ((int)sleepYears[0]!, (int)sleepYears[1]!),
            (double?)about?["fade"] ?? 0.0,
            (about?["names"] as JsonArray)?.Select(n => (string)n!).ToList() ?? new List<string>(),
            (bool?)about?["undead"] ?? false);
    }

    static (double, double, bool) Setting(JsonNode? value) => value switch
    {
        JsonArray pair => ((double)pair[0]!, (double)pair[1]!, true),
        null => (0.0, 0.0, false),
        _ => ((double)value, (double)value, false),
    };

    /// <summary>What each path can do, by name ("marks_land", "works_rituals": faction_settings.py PATHS).</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> PathAbilities { get; }

    /// <summary>Whether `path` has an ability in PATHS (factions.path_can).</summary>
    public bool PathCan(string? path, string ability) =>
        path != null && PathAbilities.TryGetValue(path, out var can) && can.Contains(ability);

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
        hasPathOpinions = factions["PATH_OPINIONS"] != null;
        PathRituals = factions["PATHS"]!.AsObject().ToDictionary(p => p.Key, p => (double?)p.Value!["rituals"] ?? 1.0);
        PathNames = factions["PATH_NAMES"] is JsonObject pathNames
            ? pathNames.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value!.AsArray().Select(n => (string)n!).ToList())
            : new Dictionary<string, IReadOnlyList<string>>();
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
        PathSpread = (double?)splits["PATH_SPREAD"] ?? 0.0;
        Stances = splits["STANCES"] is JsonObject stances
            ? stances.Select(p => (p.Key, (double?)p.Value!["chance"] ?? 0.0, (double?)p.Value["memory"] ?? 0.0)).ToList()
            : new List<(string, double, double)>();
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

        var death = settings["death"] as JsonObject ?? new JsonObject();
        MetalDelay = Setting(death["METAL_DELAY"]);
        MetalPerDeath = Setting(death["METAL_PER_DEATH"]);
        DigAnger = (double?)death["DIG_ANGER"] ?? 0.0;
        // signs.DEFAULTS: chance 0, dead 500, reach 20, no belief, harvest 1.0, years 5.
        Signs = death["SIGNS"] is JsonObject signs
            ? signs.Select(p => new Sign(p.Key, (double?)p.Value!["chance"] ?? 0.0, (double?)p.Value["dead"] ?? 500,
                (double?)p.Value["reach"] ?? 20, p.Value["belief"] is JsonArray b ? ((string)b[0]!, (double)b[1]!) : null,
                (double?)p.Value["harvest"] ?? 1.0, (int?)p.Value["years"] ?? 5)).ToList()
            : new List<Sign>();
        var happening = settings["happenings"] as JsonObject ?? new JsonObject();
        Happenings = happening["HAPPENINGS"] is JsonObject table
            ? table.Select(p => ReadHappening(p.Key, p.Value)).ToList() : new List<Happening>();
        // happenings.STEADY when there are no tempers: one, at chance 1.
        Tempers = happening["TEMPERS"] is JsonObject tempers && tempers.Count > 0
            ? tempers.Select(p => new Temper(p.Key, (double?)p.Value?["weight"] ?? 1.0, (double?)p.Value?["chance"] ?? 1.0,
                  p.Value?["happenings"] is JsonObject moods ? moods.ToDictionary(m => m.Key, m => (double)m.Value!)
                                                             : new Dictionary<string, double>(),
                  (double?)p.Value?["war"] ?? 1.0, (double?)p.Value?["splits"] ?? 1.0)).ToList()
            : new List<Temper> { new("steady", 1.0, 1.0, new Dictionary<string, double>(), 1.0, 1.0) };
        EraYears = happening["ERA_YEARS"] is JsonArray years ? ((int)years[0]!, (int)years[1]!) : (40, 120);
        EraEndChance = (double?)happening["ERA_END_CHANCE"] ?? 0.0;
        EraEnders = (happening["ERA_ENDERS"] as JsonArray)?.Select(e => (string)e!).ToHashSet() ?? new HashSet<string>();
        var threats = settings["threats"] as JsonObject ?? new JsonObject();
        ThreatKinds = threats["THREAT_KINDS"] is JsonObject kinds
            ? kinds.Select(p => ReadThreatKind(p.Key, p.Value)).ToList() : new List<ThreatKind>();
        RiseChance = (double?)threats["RISE_CHANCE"] ?? 0.0;
        var attuning = settings["attunement"] as JsonObject;
        Attuning = attuning != null;
        attuning ??= new JsonObject();
        MarkPerMetal = (double?)attuning["MARK_PER_METAL"] ?? 0.0;
        MarkHalfLife = (double?)attuning["MARK_HALF_LIFE"] ?? double.PositiveInfinity;
        RiverShare = (double?)attuning["RIVER_SHARE"] ?? 0.0;
        AnimalShare = (double?)attuning["ANIMAL_SHARE"] ?? 0.0;
        Faint = (double?)attuning["FAINT"] ?? double.PositiveInfinity;
        Strong = (double?)attuning["STRONG"] ?? double.PositiveInfinity;
        Severe = (double?)attuning["SEVERE"] ?? double.PositiveInfinity;
        Permanent = (double?)attuning["PERMANENT"] ?? double.PositiveInfinity;
        HarvestLoss = (double?)attuning["HARVEST_LOSS"] ?? 0.0;
        Prey = (double?)attuning["PREY"] ?? 0.0;
        Blame = (double?)attuning["BLAME"] ?? 0.0;
        PathBlame = Numbers(attuning["PATH_BLAME"]);
        HarvestRitualChance = (double?)attuning["HARVEST_RITUAL_CHANCE"] ?? 0.0;
        HarvestRitual = Range(attuning["HARVEST_RITUAL"]);
        HarvestRitualSkill = (double?)attuning["HARVEST_RITUAL_SKILL"] ?? 0.0;
        HarvestRitualMetal = (double?)attuning["HARVEST_RITUAL_METAL"] ?? 0.0;
        HarvestRitualMark = (double?)attuning["HARVEST_RITUAL_MARK"] ?? 0.0;
        HarvestRitualUnease = (double?)attuning["HARVEST_RITUAL_UNEASE"] ?? 0.0;
        HarvestRitualPoor = (double?)attuning["HARVEST_RITUAL_POOR"] ?? 0.0;
        RitualDesperation = (double?)attuning["RITUAL_DESPERATION"] ?? double.PositiveInfinity;
        RitualChance = (double?)attuning["RITUAL_CHANCE"] ?? 0.0;
        RitualMetal = (double?)attuning["RITUAL_METAL"] ?? 0.0;
        RitualDeaths = Range(attuning["RITUAL_DEATHS"]);
        CraterRadius = (double?)attuning["CRATER_RADIUS"] ?? 0.0;
        PathAbilities = factions["PATHS"]!.AsObject().ToDictionary(p => p.Key, p => (IReadOnlySet<string>)p.Value!.AsObject()
            .Where(a => a.Value is JsonValue v && v.TryGetValue(out bool yes) && yes).Select(a => a.Key).ToHashSet());

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
