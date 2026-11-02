// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;

namespace WardensReckoning.Sim;

/// <summary>Metal formed where people died, not yet found (death.Hidden): its place on the history's grid, its metal,
/// the year it has formed, the year they died and how many.</summary>
public sealed class Hidden
{
    public int X, Y;
    public double Metal;
    public int Ready, Died, Dead;
}

/// <summary>
/// The god of death (the generator's north/death.py; your settings are in death_settings.py): metal where many died,
/// and what the peoples make of it. Once a year, after the wars and the great rituals:
///
/// 1. Death sites: every battle, rising and great ritual this year, and every town that lost at least MASS_DEATH of
///    its people (and at least MASS_DEATH_PEOPLE of them) to anything but war.
/// 2. The metal: after METAL_DELAY years, METAL_PER_DEATH metal for each of the dead forms, hidden, a few steps from
///    where they fell (SCATTER); metal forming near metal already hidden joins it (GATHER).
/// 3. Exploring: each year every town within EXPLORE_REACH of formed metal may find it: EXPLORE_CHANCE times its
///    people's risk, times how its belief lets it dig (BELIEF_DIGS). The finder's faction gets it, as a new mine or
///    added to a mine of its own close by.
/// 4. Beliefs: a people that does not know her and finds metal where the dead lie comes to deny her (the bold) or
///    worship her (the rest); one that loses GREAT_DEATH of its own in a year to hunger or plague may come to fear her.
/// 5. Digging angers: every faction that fears or worships her thinks DIG_ANGER less of a finder.
///
/// The god of death rolls its own dice ("death"). The book reports only what the peoples believe and do.
/// </summary>
public static class Death
{
    // The engine's values (death.py), copied as they are there.
    public const double MassDeath = 0.1;
    public const int MassDeathPeople = 50;
    public const int Scatter = 3;
    public const double Gather = 6;
    public const double ExploreReach = 16;
    public const double ExploreChance = 0.05;
    public static readonly IReadOnlyDictionary<string, double> BeliefDigs =
        new Dictionary<string, double> { ["deny"] = 1.5, ["unknowing"] = 1.0, ["fear"] = 0.3, ["worship"] = 0.2 };
    public const double GreatDeath = 0.1;
    public const double FearChance = 0.5;

    /// <summary>How many died at a town this year, if it was many at once; else 0 (death.mass_death).</summary>
    public static double MassDeathOf(double before, double after)
    {
        double lost = before - after;
        return lost >= MassDeathPeople && lost >= MassDeath * before ? lost : 0.0;
    }

    /// <summary>A setting that is a number, or a range to draw one from with `random` (death.drawn): a range is always
    /// drawn, even when its two ends are the same, as the generator's is.</summary>
    static double Drawn((double Low, double High, bool IsRange) setting, Func<double> random) =>
        setting.IsRange ? setting.Low + (setting.High - setting.Low) * random() : setting.Low;

    /// <summary>`chance` that a faction's belief shifts, after its faith (factions.faith_shift).</summary>
    public static double FaithShift(FactionNow faction, double chance) =>
        faction.Traits.TryGetValue("faith", out double faith) ? Math.Min(1.0, chance * 2 * (1.0 - faith)) : chance;

    /// <summary>`anger` at digging where the dead lie, after a faction's faith (factions.faith_anger).</summary>
    public static double FaithAnger(FactionNow faction, double anger) =>
        faction.Traits.TryGetValue("faith", out double faith) ? anger * 2 * faith : anger;

    /// <summary>
    /// New death sites, metal that forms, metal that is found, and beliefs that shift (death.year_of_death).
    /// `random` is the god of death's dice (a number from 0 to 1 each call); the tests hand it the generator's.
    /// </summary>
    public static void Year(Game game, Func<double> random, int year)
    {
        var settings = game.Settings;
        int columns = game.Grid.Columns, rows = game.Grid.Rows;
        double scale = columns / 256.0;
        var sites = game.Deaths.Skip(game.DeathsSeen).ToList();
        game.DeathsSeen = game.Deaths.Count;
        sites.AddRange(game.MassDeaths.Where(m => m.Year == year));
        foreach (var (when, x, y, dead) in sites)
        {
            if (dead <= 0)
                continue;
            int spread = Math.Max(1, Py.Round(Scatter * scale));
            int hx = Math.Min(columns - 1, Math.Max(0, x + (int)(-spread + (spread + 1 + spread) * random())));
            int hy = Math.Min(rows - 1, Math.Max(0, y + (int)(-spread + (spread + 1 + spread) * random())));
            if (game.Grid.Ocean[hy * columns + hx])
                (hx, hy) = (x, y);
            int delay = Py.Round(Drawn(settings.MetalDelay, random));
            double metal = dead * Drawn(settings.MetalPerDeath, random);
            if (metal <= 0)
                continue;
            double gather = Gather * scale;
            var close = game.Hidden.Where(h => Migration.Hypot(h.X - hx, h.Y - hy) <= gather).ToList();
            if (close.Count > 0)
            {
                var site = close.OrderBy(h => Migration.Hypot(h.X - hx, h.Y - hy)).ThenBy(h => h.Ready).First();
                site.Metal += metal;
                site.Dead += dead;
                site.Ready = Math.Max(site.Ready, when + delay);
            }
            else
                game.Hidden.Add(new Hidden { X = hx, Y = hy, Metal = metal, Ready = when + delay, Died = when, Dead = dead });
        }

        double reach = ExploreReach * scale;
        foreach (var site in game.Hidden.ToList())
        {
            if (site.Ready > year)
                continue;
            var near = Enumerable.Range(0, game.Towns.Count)
                .Where(i => !game.Towns[i].Ruined && game.Towns[i].Holder != null)
                .Select(i => (Distance: Migration.Hypot(game.Towns[i].HX - site.X, game.Towns[i].HY - site.Y), Town: i))
                .OrderBy(p => p.Distance).ThenBy(p => p.Town).ToList();
            foreach (var (distance, town) in near)
            {
                if (distance > reach)
                    break;
                int f = game.Towns[town].Holder!.Value;
                var finder = game.Factions[f];
                double chance = ExploreChance * finder.Traits["risk"] * BeliefDigs.GetValueOrDefault(finder.Belief, 1.0);
                if (random() >= chance)
                    continue;
                game.Hidden.Remove(site);
                Find(game, site, town, f, year);
                break;
            }
        }
    }

    /// <summary>A town of faction `f` finds `site`: a mine of the faction's close by grows, or a new mine is opened;
    /// the book tells it; diggers anger the faithful; a people that did not know her come to a belief.</summary>
    static void Find(Game game, Hidden site, int town, int f, int year)
    {
        var finder = game.Factions[f];
        double gather = Gather * game.Grid.Columns / 256.0;
        var theirs = game.Mines.Where(m => m.Owner == f && m.Metal > 0
                                           && Migration.Hypot(m.HX - site.X, m.HY - site.Y) <= gather).ToList();
        Mine mine;
        string told, what;
        if (theirs.Count > 0)
        {
            mine = theirs.OrderBy(m => Migration.Hypot(m.HX - site.X, m.HY - site.Y)).First();
            mine.Metal += site.Metal;
            mine.First += site.Metal;
            (told, what) = ("the mine " + mine.Name + " grows", "metal_found_mine_grows");
        }
        else
        {
            mine = NewMine(game, site.X, site.Y, site.Metal, f, game.NextMineName(finder.Folk, year));
            (told, what) = ("the mine " + mine.Name, "metal_found");
        }
        string place = game.World.Towns[town].Name;
        game.Record(new GameEvent(year, "metal", what,
            "the " + finder.Name + " of " + place + " find metal where " + site.Dead + " died in year " + site.Died + ": " + told,
            new[] { f }, new Dictionary<string, string>
            {
                ["finder"] = finder.Name, ["place"] = place, ["dead"] = site.Dead.ToString(), ["died"] = site.Died.ToString(),
                ["mine"] = mine.Name,
            }));
        double anger = game.Settings.DigAnger;
        if (anger > 0)
            Angered(game, f, anger, year);
        if (finder.Belief == "unknowing")
        {
            finder.Belief = finder.Traits["risk"] >= 0.5 ? "deny" : "worship";
            game.Record(new GameEvent(year, "belief", "belief_after_find",
                "the " + finder.Name + " come to " + finder.Belief + " the god of death", new[] { f },
                new Dictionary<string, string> { ["faction"] = finder.Name, ["belief"] = finder.Belief }));
        }
    }

    /// <summary>A mine opened where the dead lie, on the history's grid at (hx, hy) (drawn in the middle of that region
    /// on the map), on the land of whichever town farms it.</summary>
    public static Mine NewMine(Game game, int hx, int hy, double metal, int owner, string name)
    {
        int factor = game.World.HistoryFactor, size = game.World.Size;
        int owned = game.Grid.Owner[hy * game.Grid.Columns + hx];
        var mine = new Mine
        {
            Name = name, HX = hx, HY = hy, Metal = metal, First = metal, Owner = owner,
            X = Math.Min(size - 1, (hx * factor + Math.Min(size, (hx + 1) * factor)) / 2),
            Y = Math.Min(size - 1, (hy * factor + Math.Min(size, (hy + 1) * factor)) / 2),
            LandTown = owned >= 0 ? owned : null,
        };
        game.Mines.Add(mine);
        game.FoundMines++;
        return mine;
    }

    /// <summary>Every faction that fears or worships her thinks `anger` less of the finder (death.angered).</summary>
    static void Angered(Game game, int finder, double anger, int year)
    {
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            if (f == finder || faction.People <= 0 || faction.Belief is not ("fear" or "worship")
                || !game.Memory.TryGetValue((f, finder), out double remembered))
                continue;
            game.Memory[(f, finder)] = Math.Max(-1.0, remembered - FaithAnger(faction, anger));
            game.Record(new GameEvent(year, "belief", "dig_angers",
                "the " + faction.Name + " are angered that the " + game.Factions[finder].Name + " dig where the dead lie",
                new[] { f, finder }, new Dictionary<string, string> { ["faction"] = faction.Name, ["finder"] = game.Factions[finder].Name }));
        }
    }

    /// <summary>Perhaps turn a people who lost many to hunger or plague this year to fear (death.year_of_loss).
    /// `losses` is how many of each faction's people died that way this year.</summary>
    public static void Loss(Game game, IReadOnlyDictionary<int, double> losses, Func<double> random, int year)
    {
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var faction = game.Factions[f];
            double lost = losses.GetValueOrDefault(f);
            double people = faction.People + lost;
            if (people <= 0 || lost < GreatDeath * people)
                continue;
            if (faction.Belief is "deny" or "unknowing" && random() < FaithShift(faction, FearChance))
            {
                faction.Belief = "fear";
                int dead = Py.Round(lost);
                game.Record(new GameEvent(year, "belief", "belief_fear",
                    "the " + faction.Name + " come to fear the god of death after losing " + dead + " of their own",
                    new[] { f }, new Dictionary<string, string> { ["faction"] = faction.Name, ["dead"] = dead.ToString() }));
            }
        }
    }

    /// <summary>The hidden metal as the export writes it ([x, y, metal, ready, died, dead]).</summary>
    public static JsonArray Write(IEnumerable<Hidden> hidden) => new(hidden.Select(h =>
        (JsonNode)new JsonArray(h.X, h.Y, h.Metal, h.Ready, h.Died, h.Dead)).ToArray());

    public static List<Hidden> Read(JsonNode? node) => node is JsonArray list
        ? list.Select(h => new Hidden { X = (int)h![0]!, Y = (int)h[1]!, Metal = (double)h[2]!, Ready = (int)h[3]!,
                                         Died = (int)h[4]!, Dead = (int)h[5]! }).ToList()
        : new List<Hidden>();
}

/// <summary>
/// The god of death's signs (the generator's north/signs.py; your SIGNS are in death_settings.py): what is seen
/// after a great death. Every great death this year (a site of the god of death's) whose dead reach a sign's "dead"
/// may bring that sign, with its "chance": it is seen by every town within its "reach", whose factions may come to
/// its "belief", and whose harvest has its "harvest" factor for its "years", from the next. Signs roll their own
/// dice ("signs").
/// </summary>
public static class Signs
{
    /// <summary>This year's harvest factor for town `town` from the signs (1 if none; signs.Signs.factor).</summary>
    public static double Factor(Game game, int town, int year)
    {
        double value = 1.0;
        foreach (var (first, last, factor) in game.SignsLasting.GetValueOrDefault(town) ?? new List<(int, int, double)>())
            if (first <= year && year <= last)
                value *= factor;
        return value;
    }

    /// <summary>The signs after this year's great deaths (signs.Signs.year). `random` is the signs' dice.</summary>
    public static void Year(Game game, Func<double> random, int year)
    {
        var table = game.Settings.Signs;
        var sites = game.Deaths.Skip(game.SignsSeen).Select(d => (d.X, d.Y, d.Dead))
            .Concat(game.MassDeaths.Where(m => m.Year == year).Select(m => (m.X, m.Y, m.Dead))).ToList();
        game.SignsSeen = game.Deaths.Count;
        if (table.Count == 0)
            return;
        foreach (var (x, y, dead) in sites)
            foreach (var sign in table)
            {
                if (dead < sign.Dead || sign.Chance <= 0 || random() >= sign.Chance)
                    continue;
                Show(game, sign, x, y, dead, random, year);
            }
    }

    static void Show(Game game, GameSettings.Sign sign, int x, int y, int dead, Func<double> random, int year)
    {
        double reach = sign.Reach * game.Grid.Columns / 256.0;
        var living = Enumerable.Range(0, game.Towns.Count).Where(i => !game.Towns[i].Ruined).ToList();
        var seen = living.Where(i => Migration.Hypot(game.Towns[i].HX - x, game.Towns[i].HY - y) <= reach).ToList();
        int? near = living.Count == 0 ? null
            : living.OrderBy(i => Migration.Hypot(game.Towns[i].HX - x, game.Towns[i].HY - y)).ThenBy(i => i).First();
        string place = near is int n ? game.World.Towns[n].Name : "";
        string shown = sign.Name.Replace("_", " ");
        game.Record(new GameEvent(year, "sign", sign.Name, shown + " near " + place + ", where " + dead + " died",
            near is int t && game.Towns[t].Holder is int h ? new[] { h } : Array.Empty<int>(),
            new Dictionary<string, string> { ["place"] = place, ["dead"] = dead.ToString() }));
        if (sign.Harvest != 1.0 && sign.Years > 0)
            foreach (int town in seen)
            {
                if (!game.SignsLasting.TryGetValue(town, out var lasting))
                    game.SignsLasting[town] = lasting = new List<(int, int, double)>();
                lasting.Add((year + 1, year + sign.Years, sign.Harvest));
            }
        if (sign.Belief is not { } belief)
            return;
        foreach (int f in seen.Where(i => game.Towns[i].Holder != null).Select(i => game.Towns[i].Holder!.Value).Distinct().Order())
        {
            var faction = game.Factions[f];
            if (faction.People <= 0 || faction.Belief == belief.Belief || random() >= Death.FaithShift(faction, belief.Chance))
                continue;
            faction.Belief = belief.Belief;
            game.Record(new GameEvent(year, "belief", "belief_after_sign",
                "the " + faction.Name + " come to " + belief.Belief + " the god of death after " + shown, new[] { f },
                new Dictionary<string, string> { ["faction"] = faction.Name, ["belief"] = belief.Belief, ["sign"] = sign.Name }));
        }
    }
}
