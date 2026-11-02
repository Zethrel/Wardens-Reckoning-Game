// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Rituals (the generator's north/rituals.py; your settings are in attunement_settings.py), for the factions whose
/// path works rituals (attunement).
///
/// The harvest ritual (`HarvestYear`): a faction with a town that went hungry last year gambles on a ritual for this
/// year's harvest with HARVEST_RITUAL_CHANCE, if it has metal; one whose towns' harvest this year averages below
/// HARVEST_RITUAL_POOR may too, though none went hungry, the less the better it plans. It spends
/// HARVEST_RITUAL_METAL of its metal; its towns' harvest is multiplied by a number drawn from HARVEST_RITUAL, moved by
/// up to HARVEST_RITUAL_SKILL by its ruler's competence. Every other faction thinks HARVEST_RITUAL_UNEASE less of it.
/// When it brings less than an ordinary harvest, it marks its towns' land.
///
/// The great ritual (`WarYear`): a faction whose path works rituals, on a side losing a war (wearier than the other,
/// and at least RITUAL_DESPERATION of its people lost), may in desperation work one great ritual in that war:
/// RITUAL_CHANCE a year, twice that for the most reckless ruler, none for the most careful. It spends RITUAL_METAL of
/// its metal and strikes from its town nearest the enemy at the enemy's town nearest to it: that town, and every
/// other enemy town within twice CRATER_RADIUS of the crater halfway between, lose RITUAL_DEATHS of their people.
/// The enemy remembers it bitterly, and the land within CRATER_RADIUS of the crater is marked for good. In the
/// generator the crater also reshapes the land (heights, water, climate, routes); the game leaves the land as it is
/// (the maintainer's choice, 1 November 2026).
///
/// Rituals roll their own dice ("rituals").
/// </summary>
public static class Rituals
{
    /// <summary>
    /// This year's harvest rituals (rituals.year_of_harvest_rituals): returns each faction that worked one and its
    /// harvest factor. `weather` is each town's harvest this year as a share of an ordinary one, before its path and
    /// ruler change it (a poor season frightens them).
    /// </summary>
    public static Dictionary<int, double> HarvestYear(Game game, IReadOnlyList<double> weather, int year)
    {
        var settings = game.Settings;
        var worked = new Dictionary<int, double>();
        double chance = settings.HarvestRitualChance;
        if (!settings.Attuning || chance <= 0)
            return worked;
        var dice = game.DiceFor("rituals");
        var wanting = new HashSet<int>();
        for (int i = 0; i < game.Towns.Count; i++)
            if (!game.Towns[i].Ruined && game.Towns[i].Holder is int holder && game.Towns[i].Hungry > 0)
                wanting.Add(holder);
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            if (!settings.PathCan(faction.Path, "works_rituals") || faction.People <= 0)
                continue;
            string why;
            double odds;
            if (wanting.Contains(f))
                (why, odds) = ("hungry", chance);
            else if (settings.HarvestRitualPoor > 0 && PoorYear(game, f, weather) < settings.HarvestRitualPoor)
                (why, odds) = ("poor harvest", chance * (1.0 - faction.Traits["planning"]));
            else
                continue;
            if (dice.Random() >= ByPath(game, odds, faction) || MetalOf(game, f) <= 0)
                continue;
            foreach (var mine in game.Mines)
                if (mine.Owner == f)
                    mine.Metal -= mine.Metal * settings.HarvestRitualMetal;
            double competence = faction.Ruler?.Traits["competence"] ?? 0.5;
            var (low, high) = settings.HarvestRitual;
            double outcome = Math.Max(0.0, dice.Uniform(low, high) + settings.HarvestRitualSkill * (2 * competence - 1));
            worked[f] = outcome;
            for (int other = 0; other < game.Factions.Count; other++)
                if (game.Memory.TryGetValue((other, f), out double remembered))
                    game.Memory[(other, f)] = Math.Max(-1.0, remembered - settings.HarvestRitualUnease);
            if (outcome < 1.0 && game.Marks != null)
                MarkTowns(game, f, settings.HarvestRitualMark * (1.0 - outcome) * 10);
            string ruler = faction.Ruler?.Name ?? "no one";
            int change = Py.Round((outcome - 1.0) * 100);
            string who = why == "hungry" ? faction.Name : faction.Name + ", fearing a poor harvest,";
            string text = outcome >= 1.0
                ? "the " + who + " work a harvest ritual under " + ruler + ": the harvest is " + change + "% richer"
                : "the " + who + " work a harvest ritual under " + ruler + ", and it goes wrong: the harvest is "
                  + -change + "% poorer, and the land is marked";
            game.Record(new GameEvent(year, "attunement", outcome >= 1.0 ? "harvest_ritual" : "harvest_ritual_failed",
                text, new[] { f }, new Dictionary<string, string>
                {
                    ["faction"] = faction.Name, ["ruler"] = ruler, ["harvest"] = Math.Abs(change).ToString(),
                    ["why"] = why,
                }));
        }
        return worked;
    }

    /// <summary>How much the enemy's memory of a faction sours when it works a great ritual against them
    /// (rituals.RITUAL_BITTERNESS).</summary>
    public const double RitualBitterness = 0.3;

    /// <summary>
    /// The great rituals of this year's wars (rituals.year_of_rituals): in every war going on, every faction of each
    /// side, in order, that works rituals, has people, has not worked one in this war and is losing it by
    /// RITUAL_DESPERATION or more, works one with its chance, if it has metal and an enemy town to strike.
    /// </summary>
    public static void WarYear(Game game, int year)
    {
        var settings = game.Settings;
        if (!settings.Attuning)
            return;
        var dice = game.DiceFor("rituals");
        double scale = game.Grid.Columns / 256.0;
        foreach (var war in game.Wars)
        {
            if (war.Ended != null)
                continue;
            foreach (var (side, other) in new[] { (0, 1), (1, 0) })
                foreach (int f in war.Sides[side].Order().ToList())
                {
                    var faction = game.Factions[f];
                    if (!settings.PathCan(faction.Path, "works_rituals") || faction.People <= 0 || war.Rituals.Contains(f))
                        continue;
                    if (Losing(game, war, f) < settings.RitualDesperation)
                        continue;
                    if (dice.Random() >= ByPath(game, settings.RitualChance * 2 * Recklessness(faction.Ruler), faction))
                        continue;
                    double left = MetalOf(game, f);
                    var pair = NearestPair(game, f, war.Sides[other]);
                    if (left <= 0 || pair == null)
                        continue;
                    foreach (var mine in game.Mines)
                        if (mine.Owner == f)
                            mine.Metal -= mine.Metal * settings.RitualMetal;
                    Strike(game, war, f, pair.Value, scale, dice, year);
                    war.Rituals.Add(f);
                }
        }
    }

    /// <summary>How weary a faction's side of a war is, if it is losing (wearier than the other side), else 0: each
    /// side's dead over its people and dead (rituals.losing).</summary>
    public static double Losing(Game game, WarNow war, int faction)
    {
        var weary = war.Sides.Select(side =>
        {
            var members = side.Order().ToList();
            double people = members.Sum(f => (double)game.Factions[f].People);
            double dead = Py.Sum(members.Select(f => war.Dead.GetValueOrDefault(f)));
            return people + dead > 0 ? dead / (people + dead) : 0.0;
        }).ToArray();
        int mine = war.Sides[0].Contains(faction) ? 0 : 1;
        return weary[mine] > weary[1 - mine] ? weary[mine] : 0.0;
    }

    /// <summary>0 for the most careful ruler, 1 for the most reckless: low planning, high ambition (no ruler: 0).
    /// rituals.recklessness.</summary>
    public static double Recklessness(Ruler? ruler) =>
        ruler == null ? 0.0 : ((1.0 - ruler.Traits["planning"]) + ruler.Traits["ambition"]) / 2;

    /// <summary>The faction's town and the enemy's town closest together (the first by number on a tie), or null
    /// (rituals.nearest_pair). On the history's grid.</summary>
    public static (int Source, int Target)? NearestPair(Game game, int faction, IReadOnlySet<int> enemies)
    {
        (double Distance, int A, int B)? best = null;
        for (int a = 0; a < game.Towns.Count; a++)
        {
            if (game.Towns[a].Ruined || game.Towns[a].Holder != faction)
                continue;
            for (int b = 0; b < game.Towns.Count; b++)
            {
                if (game.Towns[b].Ruined || game.Towns[b].Holder is not int h || !enemies.Contains(h))
                    continue;
                double distance = Migration.Hypot(game.Towns[a].HX - game.Towns[b].HX, game.Towns[a].HY - game.Towns[b].HY);
                if (best == null || distance < best.Value.Distance)
                    best = (distance, a, b);
            }
        }
        return best is { } found ? (found.A, found.B) : null;
    }

    /// <summary>
    /// The great ritual's blow (rituals.strike): the crater halfway between the two towns; the target, and every
    /// other town of the enemy's side within twice CRATER_RADIUS of the crater, lose RITUAL_DEATHS of their people,
    /// who count as the war's dead; the enemy remembers it bitterly; the land within CRATER_RADIUS is marked for good.
    /// </summary>
    static void Strike(Game game, WarNow war, int faction, (int Source, int Target) pair, double scale, Dice dice, int year)
    {
        var settings = game.Settings;
        var (source, target) = pair;
        int enemy = game.Towns[target].Holder!.Value;
        int x = Py.Round((game.Towns[source].HX + game.Towns[target].HX) / 2.0);
        int y = Py.Round((game.Towns[source].HY + game.Towns[target].HY) / 2.0);
        double radius = settings.CraterRadius * scale;
        double share = War.Drawn(settings.RitualDeaths, dice);
        var enemies = war.Sides.Where(side => side.Contains(enemy)).SelectMany(side => side).ToHashSet();
        double dead = 0.0;
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int holder || !enemies.Contains(holder))
                continue;
            if (i != target && Migration.Hypot(town.HX - x, town.HY - y) > 2 * radius)
                continue;
            double lost = town.People * share;
            town.People -= lost;
            war.Dead[holder] = war.Dead.GetValueOrDefault(holder) + lost;
            dead += lost;
        }
        if (game.Memory.TryGetValue((enemy, faction), out double remembered))
            game.Memory[(enemy, faction)] = Math.Max(-1.0, remembered - RitualBitterness);
        Scar(game, x, y, radius, faction);
        string name = game.Factions[faction].Name, against = game.Factions[enemy].Name;
        string from = game.World.Towns[source].Name, at = game.World.Towns[target].Name;
        int died = Py.Round(dead);
        game.Deaths.Add((year, game.Towns[target].HX, game.Towns[target].HY, died));
        game.Record(new GameEvent(year, "ritual", "great_ritual",
            "the " + name + " of " + from + " work a great ritual against the " + against + ": " + died + " die at " + at
            + ", and the land between is torn open", new[] { faction, enemy }, new Dictionary<string, string>
            {
                ["faction"] = name, ["source"] = from, ["enemy"] = against, ["dead"] = died.ToString(), ["target"] = at,
            }));
    }

    /// <summary>Mark the patches of land within `radius` of (x, y) (at least the patch it falls in) at PERMANENT for
    /// good, by `faction` (rituals.scar). Without marks, nothing.</summary>
    public static void Scar(Game game, int x, int y, double radius, int faction)
    {
        var marks = game.Marks;
        if (marks == null)
            return;
        foreach (var patch in marks.Land.OrderBy(p => p.Item1).ThenBy(p => p.Item2))
        {
            double mx = patch.Item1 * marks.Size + (marks.Size - 1) / 2.0, my = patch.Item2 * marks.Size + (marks.Size - 1) / 2.0;
            if (Migration.Hypot(mx - x, my - y) <= Math.Max(radius, marks.Size / 2.0))
            {
                marks.Mark[patch] = Math.Max(marks.Mark.GetValueOrDefault(patch, 0.0), game.Settings.Permanent);
                marks.Maker[patch] = faction;
                marks.Permanent.Add(patch);
            }
        }
    }

    /// <summary>A ritual's chance for a faction, times its path's ritual factor (an offshoot's; 1 for your own paths).
    /// rituals.by_path, without the chokepoints' part, which comes with step 9.</summary>
    public static double ByPath(Game game, double chance, FactionNow faction)
    {
        double factor = game.Settings.RitualFactor(faction.Path);
        return factor == 1.0 ? chance : chance * factor;
    }

    /// <summary>The average of this year's weather over a faction's towns; 1.0 without any (rituals.poor_year).</summary>
    public static double PoorYear(Game game, int faction, IReadOnlyList<double> weather)
    {
        var theirs = Enumerable.Range(0, game.Towns.Count)
                               .Where(i => !game.Towns[i].Ruined && game.Towns[i].Holder == faction)
                               .Select(i => weather[i]).ToList();
        return theirs.Count > 0 ? Py.Sum(theirs) / theirs.Count : 1.0;
    }

    /// <summary>The metal left in the mines a faction holds (rituals.metal_of).</summary>
    public static double MetalOf(Game game, int faction) =>
        Py.Sum(game.Mines.Where(m => m.Owner == faction && m.Metal > 0).Select(m => m.Metal));

    /// <summary>Add `amount` of mark to every patch of the land of a faction's towns; a patch with no maker gets this
    /// faction (rituals.mark_towns).</summary>
    public static void MarkTowns(Game game, int faction, double amount)
    {
        var marks = game.Marks!;
        var patches = new HashSet<(int, int)>();
        for (int i = 0; i < game.Towns.Count; i++)
            if (!game.Towns[i].Ruined && game.Towns[i].Holder == faction && i < marks.Lands.Count)
                patches.UnionWith(marks.Lands[i].Select(p => p.Patch));
        foreach (var patch in patches.OrderBy(p => p.Item1).ThenBy(p => p.Item2))
        {
            marks.Mark[patch] = marks.Mark.GetValueOrDefault(patch, 0.0) + amount;
            marks.Maker.TryAdd(patch, faction);
        }
    }
}
