// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;

namespace WardensReckoning.Sim;

/// <summary>One war between two sides, each a set of factions (the generator's war.War).</summary>
public sealed class WarNow
{
    public int Attacker { get; set; }
    public int Defender { get; set; }
    public int Since { get; set; }
    /// <summary>[the attacker's side, the defender's side].</summary>
    public HashSet<int>[] Sides { get; set; } = { new(), new() };
    /// <summary>Faction → the people it lost in this war, dead or taken.</summary>
    public Dictionary<int, double> Dead { get; set; } = new();
    public int LastBattle { get; set; }
    /// <summary>The year it ended, or null while it lasts.</summary>
    public int? Ended { get; set; }
    /// <summary>The weariness at which a side will make peace (drawn once from PEACE_WEARINESS).</summary>
    public double? Endurance { get; set; }
}

/// <summary>
/// War and alliance (step 5): the generator's north/war.py, function by function. Once a year, after the opinions
/// and before the rulers: alliances made and broken, wars declared (allies asked to join, or betraying), a battle
/// between every two enemies that meet along a route (and towns taken), peace when a side is weary or the war goes
/// quiet, conquered towns rising, and every BORDER_YEARS the land between the towns shared out again.
///
/// Not yet in the game's wars: happenings that stir or calm a faction (step 9), great rituals (8), and a rising
/// whose faction is gone founding a faction of its own (splits, step 6): until then such a faction rises again.
/// The land between towns goes to the nearest town over land, not along ridges and rivers as in the generator.
/// </summary>
public static class War
{
    // Engine values from the generator's war.py (and death.py's WORSHIP_WAR), copied as they are there.
    public const double BattleBitterness = 0.05;
    public const double ConquestBitterness = 0.2;
    public const double Betrayal = 0.3;
    public const double AllyMargin = 0.1;
    public const double PeaceChance = 0.5;
    public const int QuietYears = 5;
    public const double WinnerLosses = 0.5;
    public const double Reinforcements = 0.5;
    public const double DefenderAdvantage = 1.5;
    public const double TakeChance = 0.5;
    public const double Rebellion = 0.05;
    public const double ResentmentHalfLife = 50;
    public const int BorderYears = 10;
    public const double WarCompetence = 0.3;
    public const double WorshipWar = 1.25;

    static Dice DiceOf(Game game) => game.DiceFor("war");

    /// <summary>Whether factions a and b are on opposite sides of a war that is not over. war.enemies.</summary>
    public static bool Enemies(Game game, int a, int b) =>
        game.Wars.Any(w => w.Ended == null && ((w.Sides[0].Contains(a) && w.Sides[1].Contains(b))
                                               || (w.Sides[1].Contains(a) && w.Sides[0].Contains(b))));

    public static bool Allied(Game game, int a, int b) => game.Alliances.Contains(Pair(a, b));

    static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>The routes joining a town of faction a to one of faction b, as (a's, b's), in order. war.fronts.</summary>
    public static List<(int Mine, int Theirs)> Fronts(Game game, int a, int b)
    {
        var found = new List<(int, int)>();
        foreach (var route in game.Routes)
        {
            var x = game.Towns[route.A];
            var y = game.Towns[route.B];
            if (x.Ruined || y.Ruined)
                continue;
            if (x.Holder == a && y.Holder == b)
                found.Add((route.A, route.B));
            else if (x.Holder == b && y.Holder == a)
                found.Add((route.B, route.A));
        }
        found.Sort();
        return found;
    }

    /// <summary>A setting that is a number, or a range [lowest, highest] to draw one from (without dice: its middle).
    /// war.drawn.</summary>
    public static double Drawn((double Low, double High) setting, Dice? dice) =>
        setting.Low == setting.High ? setting.Low
        : dice == null ? (setting.Low + setting.High) / 2 : dice.Uniform(setting.Low, setting.High);

    /// <summary>Why `a` might go to war with `b`, each 0 to 1: hatred, want, ambition, and belief when the settings
    /// weigh it. war.reasons.</summary>
    public static List<(string Reason, double Value)> Reasons(Game game, int a, int b, List<Dictionary<string, double[]>> supply)
    {
        var faction = game.Factions[a];
        var other = game.Factions[b];
        double hatred = Math.Max(0.0, -faction.Opinions.GetValueOrDefault(b));
        double want = Opinions.Covet(supply, a, b) * faction.Traits["greed"];
        double readiness = faction.Traits.TryGetValue("war_readiness", out double ready) ? ready : faction.Traits["risk"];
        double ambition = (faction.Ruler?.Traits["ambition"] ?? 0.0) * readiness;
        var why = new List<(string, double)> { ("hatred", hatred), ("want", want), ("ambition", ambition) };
        if (game.Settings.WarWeights.ContainsKey("belief"))
        {
            double belief = game.Settings.BeliefOpinions.TryGetValue(faction.Belief, out var row) ? row.GetValueOrDefault(other.Belief) : 0.0;
            why.Add(("belief", Math.Max(0.0, -belief)));
        }
        return why;
    }

    /// <summary>An army's strength: its people (the town fighting and reinforcements, or the whole faction), armed
    /// with metal (half strength with none) and led with competence. war.strength.</summary>
    public static double Strength(Game game, int index, List<Dictionary<string, double[]>> supply, double? local = null,
                                  int towns = 1, double size = 0.0)
    {
        var faction = game.Factions[index];
        int many = Math.Max(1, towns);
        double people = local == null ? faction.People
                        : local.Value + Reinforcements * faction.People / many * Math.Pow(many, size);
        double armed = 1.0;
        if (supply[index].TryGetValue(Trade.Metal, out var metal) && metal[1] > 0)
            armed = 1.0 - metal[0] / metal[1];
        double led = 1.0;
        if (faction.Ruler != null && game.Settings.Governments.TryGetValue(faction.Government, out var government))
            led = 1.0 + WarCompetence * government.RulerPower * (2 * faction.Ruler.Traits["competence"] - 1);
        return people * (0.5 + 0.5 * armed) * led;
    }

    /// <summary>One year of alliances, declarations, battles, peace and rebellion. war.year_of_war.</summary>
    public static void Year(Game game, Market[] markets, List<YearCore.Hardship> hardship, int year)
    {
        var settings = game.Settings;
        var dice = DiceOf(game);
        var alive = Enumerable.Range(0, game.Factions.Count).Where(f => game.Factions[f].People > 0).ToList();
        var living = alive.ToHashSet();
        var supply = Opinions.Supplies(game, markets);

        foreach (var war in game.Wars)
            if (war.Ended == null && !(war.Sides[0].Overlaps(living) && war.Sides[1].Overlaps(living)))
            {
                war.Ended = year;
                string attacker = game.Factions[war.Attacker].Name, defender = game.Factions[war.Defender].Name;
                game.Record(new GameEvent(year, "peace", "war_ends_one_side_gone",
                    "the war of the " + attacker + " against the " + defender + " ends: one side is no more",
                    new[] { war.Attacker, war.Defender },
                    new Dictionary<string, string> { ["attacker"] = attacker, ["defender"] = defender }));
            }

        foreach (int a in alive)
            foreach (int b in alive)
            {
                if (a >= b)
                    continue;
                var pair = (a, b);
                double likeAb = game.Factions[a].Opinions.GetValueOrDefault(b), likeBa = game.Factions[b].Opinions.GetValueOrDefault(a);
                string first = game.Factions[a].Name, second = game.Factions[b].Name;
                if (game.Alliances.Contains(pair))
                {
                    if (Math.Min(likeAb, likeBa) < settings.AllyOpinion - AllyMargin)
                    {
                        game.Alliances.Remove(pair);
                        game.Record(new GameEvent(year, "alliance", "alliance_ends",
                            "the alliance of the " + first + " and the " + second + " ends", new[] { a, b },
                            new Dictionary<string, string> { ["first"] = first, ["second"] = second }));
                    }
                }
                else if (Math.Min(likeAb, likeBa) >= settings.AllyOpinion && !Enemies(game, a, b))
                {
                    game.Alliances.Add(pair);
                    game.Record(new GameEvent(year, "alliance", "alliance",
                        "the " + first + " and the " + second + " become allies", new[] { a, b },
                        new Dictionary<string, string> { ["first"] = first, ["second"] = second }));
                }
            }

        foreach (int a in alive)
            foreach (int b in alive)
            {
                if (a == b || Allied(game, a, b) || Enemies(game, a, b))
                    continue;
                if (Fronts(game, a, b).Count == 0)
                    continue;
                var (chance, reason) = WarChance(game, a, b, supply);
                if (dice.Random() < chance)
                    Declare(game, a, b, reason, year);
            }

        foreach (var war in game.Wars.ToList())
        {
            if (war.Ended != null)
                continue;
            foreach (int x in war.Sides[0].OrderBy(f => f).ToList())
                foreach (int y in war.Sides[1].OrderBy(f => f).ToList())
                {
                    var front = Fronts(game, x, y);
                    if (front.Count > 0 && living.Contains(x) && living.Contains(y))
                        Battle(game, war, x, y, front, supply, hardship, year);
                }
            if (war.Ended == null)
                MakePeace(game, war, year);
        }

        for (int town = 0; town < game.Towns.Count; town++)
            Rebel(game, town, hardship, year);
        if (year % BorderYears == 0)
            RedrawBorders(game);
    }

    /// <summary>
    /// How likely `a` is to declare war on `b` this year, and the reason it would give: each reason weighed by
    /// WAR_WEIGHTS, added up, times WARLIKE (and WORSHIP_WAR for a faction that worships death). The reason is the
    /// strongest one, the first by name on a tie (as `max(sorted(weighed), ...)` in war.year_of_war).
    /// </summary>
    public static (double Chance, string Reason) WarChance(Game game, int a, int b, List<Dictionary<string, double[]>> supply)
    {
        var settings = game.Settings;
        var weighed = Reasons(game, a, b, supply)
            .Select(r => (r.Reason, Value: settings.WarWeights.GetValueOrDefault(r.Reason) * r.Value)).ToList();
        double ready = game.Factions[a].Belief == "worship" ? WorshipWar : 1.0;
        string reason = weighed.OrderBy(w => w.Reason, StringComparer.Ordinal)
                               .Aggregate((best, w) => w.Value > best.Value ? w : best).Reason;
        return (settings.Warlike * ready * weighed.Sum(w => w.Value), reason);
    }

    /// <summary>
    /// The two armies in a battle over the route from `here` (the attacker's town) to `there` (the defender's): each
    /// town's people with reinforcements from the rest of its faction, the defender DEFENDER_ADVANTAGE times
    /// stronger behind its walls. The attacker wins with a chance of its strength over both. From war.battle.
    /// </summary>
    public static (double Attacker, double Defender) Odds(Game game, int attacker, int defender, int here, int there,
                                                         List<Dictionary<string, double[]>> supply)
    {
        double size = game.Settings.SizeInBattle;
        return (Strength(game, attacker, supply, game.Towns[here].Population, Held(game, attacker), size),
                Strength(game, defender, supply, game.Towns[there].Population, Held(game, defender), size) * DefenderAdvantage);
    }

    /// <summary>`a` declares war on `b`; their allies are asked to join. war.declare.</summary>
    public static void Declare(Game game, int a, int b, string reason, int year)
    {
        var dice = DiceOf(game);
        var war = new WarNow { Attacker = a, Defender = b, Since = year, LastBattle = year,
                               Sides = new[] { new HashSet<int> { a }, new HashSet<int> { b } },
                               Endurance = Drawn(game.Settings.PeaceWeariness, dice) };
        var words = new Dictionary<string, string>
        {
            ["hatred"] = "out of hatred", ["want"] = "for what they have", ["ambition"] = "out of ambition",
            ["belief"] = "over the god of death",
        };
        string attacker = game.Factions[a].Name, defender = game.Factions[b].Name;
        game.Record(new GameEvent(year, "war", "war_declared",
            "the " + attacker + " declare war on the " + defender + ", " + words[reason], new[] { a, b },
            new Dictionary<string, string> { ["attacker"] = attacker, ["defender"] = defender, ["reason"] = reason }));
        foreach (var (side, partner, enemy) in new[] { (0, a, b), (1, b, a) })
            foreach (var pair in game.Alliances.OrderBy(p => p).ToList())
            {
                if (pair.Item1 != partner && pair.Item2 != partner)
                    continue;
                int ally = pair.Item1 == partner ? pair.Item2 : pair.Item1;
                if (war.Sides[0].Contains(ally) || war.Sides[1].Contains(ally) || Allied(game, ally, enemy))
                    continue;
                var facts = new Dictionary<string, string>
                {
                    ["ally"] = game.Factions[ally].Name, ["partner"] = game.Factions[partner].Name,
                    ["enemy"] = game.Factions[enemy].Name,
                };
                if (dice.Random() < game.Factions[ally].Traits["risk"])
                {
                    war.Sides[side].Add(ally);
                    game.Record(new GameEvent(year, "war", "war_joined",
                        "the " + game.Factions[ally].Name + " join the war beside the " + game.Factions[partner].Name,
                        new[] { ally, partner, enemy }, facts));
                }
                else
                {
                    game.Alliances.Remove(pair);
                    var key = (partner, ally);
                    if (game.Memory.TryGetValue(key, out double remembered))
                        game.Memory[key] = Math.Max(-1.0, remembered - Betrayal);
                    game.Record(new GameEvent(year, "betrayal", "betrayal",
                        "the " + game.Factions[ally].Name + " refuse to fight beside the " + game.Factions[partner].Name
                        + "; the alliance ends", new[] { ally, partner }, facts));
                }
            }
        game.Wars.Add(war);
    }

    /// <summary>How many towns a faction holds that are not ruins. war.held.</summary>
    public static int Held(Game game, int faction) => game.Towns.Count(t => t.Holder == faction && !t.Ruined);

    /// <summary>One battle between factions x and y, over one of the routes between their lands. war.battle.</summary>
    public static void Battle(Game game, WarNow war, int x, int y, List<(int Mine, int Theirs)> front,
                              List<Dictionary<string, double[]>> supply, List<YearCore.Hardship> hardship, int year)
    {
        var dice = DiceOf(game);
        double strongX = Strength(game, x, supply), strongY = Strength(game, y, supply);
        if (strongX + strongY <= 0)
            return;
        int attacker, defender, here, there;
        if (dice.Random() < strongX / (strongX + strongY))
        {
            (attacker, defender) = (x, y);
            (here, there) = front[(int)(dice.Random() * front.Count)];
        }
        else
        {
            (attacker, defender) = (y, x);
            (there, here) = front[(int)(dice.Random() * front.Count)];
        }
        var (strongA, strongD) = Odds(game, attacker, defender, here, there, supply);
        if (strongA + strongD <= 0)
            return;
        bool won = dice.Random() < strongA / (strongA + strongD);
        var losses = new Dictionary<int, double>();
        double bloodiness = Drawn(game.Settings.BattleDeaths, dice);
        foreach (var (town, lost) in new[] { (here, !won), (there, won) })
        {
            double share = bloodiness * (lost ? 1.0 : WinnerLosses);
            var t = game.Towns[town];
            double dead = t.People * share;
            t.People -= dead;
            int holder = t.Holder ?? -1;
            losses[holder] = losses.GetValueOrDefault(holder) + dead;
        }
        foreach (var (side, dead) in losses)
            war.Dead[side] = war.Dead.GetValueOrDefault(side) + dead;
        war.LastBattle = year;
        foreach (var key in new[] { (x, y), (y, x) })
            if (game.Memory.TryGetValue(key, out double remembered))
                game.Memory[key] = Math.Max(-1.0, remembered - BattleBitterness);
        string place = game.World.Towns[there].Name;
        int deadAll = (int)Math.Round(losses.Values.Sum(), MidpointRounding.ToEven);
        string attackerName = game.Factions[attacker].Name, defenderName = game.Factions[defender].Name;
        string text = string.Format("battle at {0}: the {1} {2} the {3} ({4} dead)", place, attackerName,
                                    won ? "beat" : "are beaten by", defenderName, deadAll);
        string what = "battle";
        var target = game.Towns[there];
        if (won && target.Population > 0 && dice.Random() < TakeChance)
        {
            war.Dead[defender] = war.Dead.GetValueOrDefault(defender) + target.Population;
            Conquer(game, there, attacker, defender, hardship, counted: true);
            target.ConqueredIn = year;
            text += "; " + place + " falls to the " + attackerName;
            what = "conquest";
        }
        int winner = won ? attacker : defender, loser = won ? defender : attacker;
        game.Record(new GameEvent(year, "battle", what, text, new[] { attacker, defender }, new Dictionary<string, string>
        {
            ["place"] = place, ["attacker"] = attackerName, ["defender"] = defenderName,
            ["winner"] = game.Factions[winner].Name, ["loser"] = game.Factions[loser].Name,
            ["dead"] = deadAll.ToString(CultureInfo.InvariantCulture),
        }));
    }

    /// <summary>Town `town` joins `conqueror`, with its land and mines; `counted` says whether it counts as a
    /// conquest (a rising does not). war.conquer.</summary>
    public static void Conquer(Game game, int town, int conqueror, int loser, List<YearCore.Hardship>? hardship, bool counted)
    {
        game.Towns[town].Holder = conqueror;
        if (counted)
            game.Conquests++;
        var key = (loser, conqueror);
        if (game.Memory.TryGetValue(key, out double remembered))
            game.Memory[key] = Math.Max(-1.0, remembered - ConquestBitterness);
        if (hardship != null && loser >= 0 && loser < hardship.Count)
            hardship[loser].Lost += 2;
        foreach (var mine in game.Mines)
            if (mine.LandTown == town)
                mine.Owner = conqueror;
        for (int cell = 0; cell < game.Grid.Owner.Length; cell++)
            if (game.Grid.Owner[cell] == town)
                game.Regions[cell] = conqueror;
        game.BordersChanged = true;
    }

    /// <summary>How much a town resents its rulers: 0 if they are its own people, 1 just after a conquest, halving every
    /// RESENTMENT_HALF_LIFE years. war.resentment.</summary>
    public static double Resentment(Game game, TownNow town, int year)
    {
        if (town.Ruined || town.Origin == null || town.Holder == town.Origin || town.ConqueredIn == null)
            return 0.0;
        return Math.Pow(0.5, (year - town.ConqueredIn.Value) / game.Settings.ResentmentHalfLife);
    }

    /// <summary>What makes a resentful town likelier to rise, 1 to 3: a troubled conqueror (grievance against its
    /// tolerance, and a half at war) and its own people's faction next to it. war.unrest.</summary>
    public static double Unrest(Game game, int town)
    {
        var place = game.Towns[town];
        int holder = place.Holder!.Value;
        var ruler = game.Factions[holder];
        double troubled = 0.0;
        double tolerance = game.Settings.Governments.TryGetValue(ruler.Government, out var g) ? g.Tolerance : 0;
        if (tolerance > 0)
            troubled = Math.Min(1.0, ruler.Grievance / tolerance);
        if (game.Wars.Any(w => w.Ended == null && (w.Sides[0].Contains(holder) || w.Sides[1].Contains(holder))))
            troubled = Math.Min(1.0, troubled + 0.5);
        double friends = 0.0;
        foreach (var route in game.Routes)
            if (route.A == town || route.B == town)
            {
                var other = game.Towns[route.A == town ? route.B : route.A];
                if (!other.Ruined && other.Holder == place.Origin)
                    friends = 1.0;
            }
        return 1.0 + troubled + friends;
    }

    /// <summary>Perhaps a conquered town rises and returns to its own people's faction. war.rebel.</summary>
    public static void Rebel(Game game, int town, List<YearCore.Hardship> hardship, int year)
    {
        var place = game.Towns[town];
        double resented = Resentment(game, place, year);
        if (resented <= 0)
            return;
        var dice = DiceOf(game);
        int holder = place.Holder!.Value, origin = place.Origin!.Value;
        double chance = game.Settings.Rebellion * resented * Unrest(game, town) * game.Factions[holder].Hold;
        if (dice.Random() >= chance)
            return;
        var ruler = game.Factions[holder];
        var own = game.Factions[origin];
        double dead = place.People * Drawn(game.Settings.BattleDeaths, dice);
        place.People -= dead;
        game.Rebellions++;
        string name = game.World.Towns[town].Name;
        int deadNow = (int)Math.Round(dead, MidpointRounding.ToEven);
        var facts = new Dictionary<string, string>
        {
            ["place"] = name, ["conqueror"] = ruler.Name, ["people"] = own.Name, ["dead"] = deadNow.ToString(CultureInfo.InvariantCulture),
        };
        if (own.People <= 0 && !RisesAgain(game, dice))
        {
            var kin = KinOnARoute(game, town, own.Folk, holder);
            if (kin is int joined && dice.Random() < game.Settings.JoinKin)
            {
                Conquer(game, town, joined, holder, hardship, counted: false);
                place.Origin = joined;
                place.ConqueredIn = null;
                ruler.People = Math.Max(0, ruler.People - place.Population);
                game.Factions[joined].People += place.Population;
                facts["faction"] = game.Factions[joined].Name;
                game.Record(new GameEvent(year, "rebellion", "rebellion_joins_kin", string.Format(
                    "{0} rises against the {1}; the {2} are no more, and it joins the {3} ({4} dead)",
                    name, ruler.Name, own.Name, game.Factions[joined].Name, deadNow), new[] { holder, origin, joined }, facts));
                return;
            }
            // Otherwise it founds a faction of its own people, as a split would (war.break_free, splits.secede).
            var later = new List<GameEvent>();
            int child = Splits.Secede(game, new List<int> { town }, holder, "rising against them", hardship, year,
                                      toldLater: later);
            var founded = game.Factions[child];
            facts["faction"] = founded.Name;
            facts["ruler"] = founded.Ruler!.Name;
            game.Record(new GameEvent(year, "rebellion", "rebellion_new_faction", string.Format(
                "{0} rises against the {1}; the {2} are no more, and it founds the {3}, ruled by {4} ({5} dead)",
                name, ruler.Name, own.Name, founded.Name, founded.Ruler.Name, deadNow), new[] { holder, origin, child }, facts));
            foreach (var e in later)
                game.Record(e);
            return;
        }
        Conquer(game, town, origin, holder, hardship, counted: false);
        string text = string.Format("{0} rises against the {1} and returns to the {2} ({3} dead)", name, ruler.Name, own.Name, deadNow);
        string what = "rebellion";
        ruler.People = Math.Max(0, ruler.People - place.Population);
        if (own.People <= 0)
        {
            own.People = place.Population;
            if (own.Ruler == null && game.Settings.Governments.TryGetValue(own.Government, out var government))
            {
                own.Ruler = Rulers.NewRuler(game, government.Succession, origin, null, year);
                own.RulerCount++;
                text += "; the " + own.Name + " rise again, ruled by " + own.Ruler.Name;
                what = "rebellion_rises_again";
                facts["ruler"] = own.Ruler.Name;
            }
        }
        else
            own.People += place.Population;
        game.Record(new GameEvent(year, "rebellion", what, text, new[] { holder, origin }, facts));
    }

    /// <summary>Whether a faction with no town left comes back when one of its towns rises (RISE_AGAIN). war.rises_again.</summary>
    static bool RisesAgain(Game game, Dice dice) => game.Settings.RiseAgain >= 1 || dice.Random() < game.Settings.RiseAgain;

    /// <summary>The largest faction of people `folk`, other than `ruler`, holding a town on a route from `town`, or null
    /// (the lower number on a tie). war.kin_on_a_route.</summary>
    public static int? KinOnARoute(Game game, int town, int folk, int ruler)
    {
        int? best = null;
        foreach (var route in game.Routes)
        {
            if (route.A != town && route.B != town)
                continue;
            var other = game.Towns[route.A == town ? route.B : route.A];
            if (other.Ruined || other.Holder is not int f)
                continue;
            var faction = game.Factions[f];
            if (f == ruler || faction.Folk != folk || faction.People <= 0)
                continue;
            if (best == null || faction.People > game.Factions[best.Value].People
                || (faction.People == game.Factions[best.Value].People && f < best))
                best = f;
        }
        return best;
    }

    /// <summary>Ends a war if a side is weary enough, or if it has gone quiet. war.make_peace.</summary>
    public static void MakePeace(Game game, WarNow war, int year)
    {
        string attacker = game.Factions[war.Attacker].Name, defender = game.Factions[war.Defender].Name;
        var facts = new Dictionary<string, string> { ["attacker"] = attacker, ["defender"] = defender };
        var both = new[] { war.Attacker, war.Defender };
        if (year - war.LastBattle >= QuietYears)
        {
            war.Ended = year;
            game.Record(new GameEvent(year, "peace", "war_fizzles",
                "the war of the " + attacker + " against the " + defender + " fizzles out", both, facts));
            return;
        }
        var weary = war.Sides.Select(side =>
        {
            double people = side.Sum(f => (double)game.Factions[f].People);
            double dead = side.Sum(f => war.Dead.GetValueOrDefault(f));
            return people + dead > 0 ? dead / (people + dead) : 0.0;
        }).ToList();
        double endurance = war.Endurance ?? Drawn(game.Settings.PeaceWeariness, null);
        if (weary.Max() >= endurance && DiceOf(game).Random() < PeaceChance)
        {
            war.Ended = year;
            int length = year - war.Since;
            facts["years"] = length.ToString(CultureInfo.InvariantCulture);
            game.Record(new GameEvent(year, "peace", "peace", "the " + attacker + " and the " + defender + " make peace "
                + (length == 0 ? "within the year" : length == 1 ? "after 1 year" : "after " + length + " years"), both, facts));
        }
    }

    /// <summary>
    /// The land shared out again after conquests (war.redraw_borders with factions.spread_territory): every region a
    /// lived-in town farms is its holder's, and every other region of land goes to the first holder to reach it over
    /// land, going out a region at a time (eight ways); then each faction's borders are counted again
    /// (factions.count_borders: a region of land touching, four ways, land held by another).
    /// </summary>
    public static void RedrawBorders(Game game)
    {
        if (!game.BordersChanged)
            return;
        var grid = game.Grid;
        int columns = grid.Columns, rows = grid.Rows;
        var held = new int[columns * rows];
        Array.Fill(held, -1);
        var waiting = new Queue<int>();
        for (int cell = 0; cell < held.Length; cell++)
        {
            int town = grid.Owner[cell];
            if (town >= 0 && !game.Towns[town].Ruined && game.Towns[town].Holder is int holder)
            {
                held[cell] = holder;
                waiting.Enqueue(cell);
            }
        }
        while (waiting.Count > 0)
        {
            int cell = waiting.Dequeue();
            int y = cell / columns, x = cell % columns;
            for (int ny = y - 1; ny <= y + 1; ny++)
            {
                if (ny < 0 || ny >= rows)
                    continue;
                for (int nx = x - 1; nx <= x + 1; nx++)
                {
                    int there = ny * columns + nx;
                    if (nx >= 0 && nx < columns && held[there] < 0 && !grid.Ocean[there])
                    {
                        held[there] = held[cell];
                        waiting.Enqueue(there);
                    }
                }
            }
        }
        Array.Copy(held, game.Regions, held.Length);
        CountBorders(game);
        game.BordersChanged = false;
    }

    /// <summary>Each faction's border cells with each other one, counted again. factions.count_borders.</summary>
    public static void CountBorders(Game game)
    {
        var grid = game.Grid;
        int columns = grid.Columns, rows = grid.Rows;
        foreach (var faction in game.Factions)
            faction.Border = new Dictionary<int, double>();
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int owner = game.Regions[y * columns + x];
                if (owner < 0 || grid.Ocean[y * columns + x])
                    continue;
                var touching = new SortedSet<int>();
                foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                    if (nx >= 0 && nx < columns && ny >= 0 && ny < rows && !grid.Ocean[ny * columns + nx])
                    {
                        int other = game.Regions[ny * columns + nx];
                        if (other >= 0 && other != owner)
                            touching.Add(other);
                    }
                foreach (int other in touching)
                    game.Factions[owner].Border[other] = game.Factions[owner].Border.GetValueOrDefault(other) + 1;
            }
    }
}
