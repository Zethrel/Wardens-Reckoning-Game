// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;

namespace WardensReckoning.Sim;

/// <summary>
/// Every patch's mark, and what the marks have done (the generator's attunement.Marks). A patch is a square of
/// `Size` regions of the history's grid a side; `Mark` holds each marked patch's mark in the order the generator
/// keeps them (see PyDict).
/// </summary>
public sealed class Marks
{
    public int Size, Columns, Rows;
    /// <summary>Every patch with land in it.</summary>
    public HashSet<(int, int)> Land { get; } = new();
    /// <summary>Patch → the patch its river runs to (patches without a river are left out).</summary>
    public Dictionary<(int, int), (int, int)> Downstream { get; } = new();
    /// <summary>For each town, its patches and how many of its regions lie in each, in the generator's order.</summary>
    public List<List<((int, int) Patch, int Cells)>> Lands { get; } = new();
    public PyDict<(int, int), double> Mark { get; } = new();
    /// <summary>Patch → the faction whose attunement put the most into it.</summary>
    public Dictionary<(int, int), int> Maker { get; } = new();
    /// <summary>Patches whose mark will never fade; patches at SEVERE or more this year (rifts).</summary>
    public HashSet<(int, int)> Permanent { get; } = new();
    public HashSet<(int, int)> Severe { get; set; } = new();
    /// <summary>Every rift named: its name, patch, year and the faction whose attunement made it.</summary>
    public List<(string Name, (int, int) Patch, int Year, int? Maker)> Rifts { get; } = new();
    /// <summary>For each town, its harvest share from the mark for the next year.</summary>
    public List<double> Harvest { get; set; } = new();
    /// <summary>The towns things prey on now, and those on marked land now.</summary>
    public HashSet<int> Preyed { get; } = new();
    public HashSet<int> Uneasy { get; } = new();
    /// <summary>(year, town) for every town given up to the mark.</summary>
    public List<(int Year, int Town)> Abandoned { get; } = new();
    /// <summary>Patch → the patches of land around it; for each town, its patches and those around them.</summary>
    public Dictionary<(int, int), List<(int, int)>> Around { get; } = new();
    public List<HashSet<(int, int)>> Near { get; } = new();

    /// <summary>The marks of an export's "continue" ("marks"), or null when the history had none.</summary>
    public static Marks? Read(JsonNode? node)
    {
        if (node is not JsonObject o)
            return null;
        var marks = new Marks { Size = (int)o["size"]!, Columns = (int)o["columns"]!, Rows = (int)o["rows"]! };
        foreach (var p in o["land"]!.AsArray())
            marks.Land.Add(((int)p![0]!, (int)p[1]!));
        foreach (var d in o["downstream"]!.AsArray())
            marks.Downstream[((int)d![0]!, (int)d[1]!)] = ((int)d[2]!, (int)d[3]!);
        foreach (var land in o["lands"]!.AsArray())
            marks.Lands.Add(land!.AsArray().Select(p => (((int)p![0]!, (int)p[1]!), (int)p[2]!)).ToList());
        foreach (var m in o["mark"]!.AsArray())
        {
            var patch = ((int)m![0]!, (int)m[1]!);
            marks.Mark[patch] = (double)m[2]!;
            if (m[3] is JsonNode maker)
                marks.Maker[patch] = (int)maker;
        }
        foreach (var p in o["permanent"]!.AsArray())
            marks.Permanent.Add(((int)p![0]!, (int)p[1]!));
        foreach (var p in o["severe"]!.AsArray())
            marks.Severe.Add(((int)p![0]!, (int)p[1]!));
        foreach (var r in o["rifts"]!.AsArray())
            marks.Rifts.Add(((string)r![0]!, ((int)r[1]!, (int)r[2]!), (int)r[3]!, (int?)r[4]));
        marks.Harvest = o["harvest"]!.AsArray().Select(h => (double)h!).ToList();
        marks.Preyed.UnionWith(o["preyed"]!.AsArray().Select(t => (int)t!));
        marks.Uneasy.UnionWith(o["uneasy"]!.AsArray().Select(t => (int)t!));
        foreach (var a in o["abandoned"]!.AsArray())
            marks.Abandoned.Add(((int)a![0]!, (int)a[1]!));
        marks.Settle();
        return marks;
    }

    /// <summary>The marks as the export writes them (and Read reads them), for a save.</summary>
    public JsonObject Write()
    {
        static JsonArray Pair((int, int) p) => new(p.Item1, p.Item2);
        static JsonArray Sorted(IEnumerable<(int, int)> patches) =>
            new(patches.OrderBy(p => p.Item1).ThenBy(p => p.Item2).Select(p => (JsonNode)Pair(p)).ToArray());
        return new JsonObject
        {
            ["size"] = Size, ["columns"] = Columns, ["rows"] = Rows,
            ["land"] = Sorted(Land),
            ["downstream"] = new JsonArray(Downstream.OrderBy(d => d.Key.Item1).ThenBy(d => d.Key.Item2)
                .Select(d => (JsonNode)new JsonArray(d.Key.Item1, d.Key.Item2, d.Value.Item1, d.Value.Item2)).ToArray()),
            ["lands"] = new JsonArray(Lands.Select(land => (JsonNode)new JsonArray(land
                .Select(p => (JsonNode)new JsonArray(p.Patch.Item1, p.Patch.Item2, p.Cells)).ToArray())).ToArray()),
            ["mark"] = new JsonArray(Mark.Items().Select(m => (JsonNode)new JsonArray(m.Key.Item1, m.Key.Item2, m.Value,
                Maker.TryGetValue(m.Key, out int maker) ? JsonValue.Create(maker) : null)).ToArray()),
            ["permanent"] = Sorted(Permanent),
            ["severe"] = Sorted(Severe),
            ["rifts"] = new JsonArray(Rifts.Select(r => (JsonNode)new JsonArray(r.Name, r.Patch.Item1, r.Patch.Item2, r.Year,
                r.Maker is int m ? JsonValue.Create(m) : null)).ToArray()),
            ["harvest"] = new JsonArray(Harvest.Select(h => (JsonNode)h).ToArray()),
            ["preyed"] = new JsonArray(Preyed.Order().Select(t => (JsonNode)t).ToArray()),
            ["uneasy"] = new JsonArray(Uneasy.Order().Select(t => (JsonNode)t).ToArray()),
            ["abandoned"] = new JsonArray(Abandoned.Select(a => (JsonNode)new JsonArray(a.Year, a.Town)).ToArray()),
        };
    }

    /// <summary>What lies around each patch of land, and near each town (attunement.build_marks and near_of).</summary>
    public void Settle()
    {
        Around.Clear();
        foreach (var (column, row) in Land)
        {
            var around = new List<(int, int)>();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && Land.Contains((column + dx, row + dy)))
                        around.Add((column + dx, row + dy));
            Around[(column, row)] = around;
        }
        Near.Clear();
        foreach (var land in Lands)
            Near.Add(NearOf(land));
    }

    HashSet<(int, int)> NearOf(List<((int, int) Patch, int Cells)> land)
    {
        var near = land.Select(p => p.Patch).ToHashSet();
        foreach (var (patch, _) in land)
            near.UnionWith(Around.GetValueOrDefault(patch) ?? new List<(int, int)>());
        return near;
    }

    /// <summary>The land of a town founded in the game (attunement.add_land): its regions, by patch.</summary>
    public void AddLand(IEnumerable<(int X, int Y)> cells)
    {
        var patches = new List<((int, int) Patch, int Cells)>();
        foreach (var (x, y) in cells)
        {
            var patch = (x / Size, y / Size);
            int at = patches.FindIndex(p => p.Patch == patch);
            if (at < 0)
                patches.Add((patch, 1));
            else
                patches[at] = (patch, patches[at].Cells + 1);
        }
        Lands.Add(patches);
        Near.Add(NearOf(patches));
    }

    /// <summary>How strongly town `index`'s land is marked: its patches' marks, each counted by its share of the
    /// land (attunement.strength).</summary>
    public double Strength(int index)
    {
        var land = Lands[index];
        int cells = land.Sum(p => p.Cells);
        return cells > 0 ? Py.Sum(land.Select(p => Mark.GetValueOrDefault(p.Patch, 0.0) * p.Cells)) / cells : 0.0;
    }

    /// <summary>Each town's harvest share from the mark on its land (1.0 where it is unmarked or new).</summary>
    public double HarvestOf(int town) => town < Harvest.Count ? Harvest[town] : 1.0;
}

/// <summary>
/// Attunement's mark (the generator's north/attunement.py; your settings are in attunement_settings.py): what
/// attunement leaves on the land. Every year, after the trade and before the people eat:
///
/// 1. Marking: each town of a faction whose path marks the land (attunement) marks its own land by the metal it
///    used for its needs this year, MARK_PER_METAL for every unit, shared over the patches of its land.
/// 2. Spreading: rivers carry RIVER_SHARE of a patch's mark to the patch their water runs to, animals ANIMAL_SHARE
///    to the patches of land around it.
/// 3. Fading: a mark halves every MARK_HALF_LIFE years; one that once reaches PERMANENT never fades.
/// 4. What it does: from FAINT a town's harvests suffer (up to HARVEST_LOSS at STRONG) and its people are uneasy;
///    where a patch on or by its land reaches STRONG, things prey on it (PREY of its people a year); where its land
///    as a whole reaches SEVERE, no one can live there and it falls into ruin. Each patch at SEVERE is a rift: no
///    one settles there, and each new rift is named.
/// 5. Blame: a faction whose land carries another's mark remembers it bitterly (BLAME and PATH_BLAME).
/// </summary>
public static class Attunement
{
    /// <summary>Two severe patches closer than this many patches belong to the same rift (attunement.RIFT_GAP).</summary>
    public const int RiftGap = 3;

    /// <summary>A mark below this share of FAINT is gone (attunement.TRACE).</summary>
    public const double Trace = 0.01;

    /// <summary>Marking, spreading and fading, then what the mark does to the towns and to opinions
    /// (attunement.year_of_attunement). `blame` is the year's blame between factions, `hardship` each faction's.</summary>
    public static void Year(Game game, Market[] markets, List<YearCore.Hardship> hardship,
                            Dictionary<(int, int), double> blame, int year)
    {
        var marks = game.Marks;
        var settings = game.Settings;
        if (marks == null || !settings.Attuning)
            return;
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int holder || !settings.PathCan(game.Factions[holder].Path, "marks_land"))
                continue;
            var market = markets[i];
            double have = market.Made.GetValueOrDefault(Trade.Metal) + market.Bought.GetValueOrDefault(Trade.Metal)
                          - market.Sold.GetValueOrDefault(Trade.Metal);
            double used = Math.Min(have, market.Needed.GetValueOrDefault(Trade.Metal));
            var land = marks.Lands[i];
            int cells = land.Sum(p => p.Cells);
            if (used <= 0 || cells == 0)
                continue;
            foreach (var (patch, count) in land)
                AddMark(marks, patch, used * settings.MarkPerMetal * count / cells, holder);
        }
        Spread(marks, settings);
        Fade(marks, settings);
        marks.Severe = marks.Mark.Items().Where(m => m.Value >= settings.Severe).Select(m => m.Key).ToHashSet();
        NameRifts(game, year);
        Effects(game, hardship, year);
        AddBlame(game, blame);
    }

    /// <summary>Add `amount` to a patch's mark; the faction that adds more than is there becomes its maker.</summary>
    static void AddMark(Marks marks, (int, int) patch, double amount, int faction)
    {
        double before = marks.Mark.GetValueOrDefault(patch, 0.0);
        if (amount > before)
            marks.Maker[patch] = faction;
        marks.Mark[patch] = before + amount;
    }

    /// <summary>Rivers carry a share of each patch's mark downstream, and animals a share to the patches around it;
    /// all that reaches a patch in a year is added at once, and the faction whose mark brought the most becomes its
    /// maker if that is more than was there (attunement.spread).</summary>
    public static void Spread(Marks marks, GameSettings settings)
    {
        // Patch → (maker or null → what it brought), both in the order they first arrived.
        var arriving = new PyDict<(int, int), PyDict<int, double>>();
        const int Nobody = int.MinValue;                     // a mark with no maker
        void Bring((int, int) to, int maker, double amount)
        {
            if (!arriving.TryGetValue(to, out var into))
                arriving[to] = into = new PyDict<int, double>();
            into[maker] = into.GetValueOrDefault(maker, 0.0) + amount;
        }
        foreach (var (patch, value) in marks.Mark.Items())
        {
            int maker = marks.Maker.TryGetValue(patch, out int m) ? m : Nobody;
            double given = 0.0;
            if (marks.Downstream.TryGetValue(patch, out var down))
            {
                double carried = value * settings.RiverShare;
                Bring(down, maker, carried);
                given += carried;
            }
            if (marks.Around.TryGetValue(patch, out var around) && around.Count > 0)
            {
                double each = value * settings.AnimalShare / around.Count;
                foreach (var other in around)
                    Bring(other, maker, each);
                given += value * settings.AnimalShare;
            }
            if (!marks.Permanent.Contains(patch))
                marks.Mark[patch] = value - given;
        }
        foreach (var (patch, makers) in arriving.Items())
        {
            // The maker that brought the most; on a tie the higher number, a maker before no one (as the generator's
            // max over (amount, maker or -1)); among equals the first.
            var list = makers.Items();
            var best = list[0];
            foreach (var item in list.Skip(1))
                if (item.Value > best.Value || (item.Value == best.Value && Rank(item.Key) > Rank(best.Key)))
                    best = item;
            double before = marks.Mark.GetValueOrDefault(patch, 0.0);
            if (best.Value > before)
            {
                if (best.Key == Nobody)
                    marks.Maker.Remove(patch);
                else
                    marks.Maker[patch] = best.Key;
            }
            marks.Mark[patch] = before + Py.Sum(list.Select(item => item.Value));
        }
        static int Rank(int maker) => maker == Nobody ? -1 : maker;
    }

    /// <summary>Every mark halves every MARK_HALF_LIFE years, except those that have reached PERMANENT; one below a
    /// trace is gone (attunement.fade).</summary>
    static void Fade(Marks marks, GameSettings settings)
    {
        double keep = Math.Pow(0.5, 1.0 / settings.MarkHalfLife);
        double trace = settings.Faint * Trace;
        foreach (var patch in marks.Mark.Keys())
        {
            if (marks.Permanent.Contains(patch))
                continue;
            if (marks.Mark[patch] >= settings.Permanent)
            {
                marks.Permanent.Add(patch);
                continue;
            }
            marks.Mark[patch] *= keep;
            if (marks.Mark[patch] < trace)
            {
                marks.Mark.Remove(patch);
                marks.Maker.Remove(patch);
            }
        }
    }

    /// <summary>Name every new rift: a severe patch with no named rift within RIFT_GAP patches, named in the tongue
    /// of the people whose attunement made it (attunement.name_rifts).</summary>
    static void NameRifts(Game game, int year)
    {
        var marks = game.Marks!;
        foreach (var patch in marks.Severe.OrderBy(p => p.Item1).ThenBy(p => p.Item2))
        {
            if (marks.Rifts.Any(r => Math.Max(Math.Abs(patch.Item1 - r.Patch.Item1), Math.Abs(patch.Item2 - r.Patch.Item2)) <= RiftGap))
                continue;
            int? maker = marks.Maker.TryGetValue(patch, out int m) ? m : null;
            string name = game.NextRiftName(maker is int f ? game.Factions[f].Folk : 0);
            marks.Rifts.Add((name, patch, year, maker));
            int? near = Nearest(game, patch, marks.Size);
            string place = near is int n ? game.World.Towns[n].Name : "";
            int? holder = near is int t ? game.Towns[t].Holder : null;
            game.Record(new GameEvent(year, "attunement", "rift",
                "the land" + (near != null ? " near " + place : "") + " turns to a rift: " + name,
                holder is int h ? new[] { h } : Array.Empty<int>(),
                new Dictionary<string, string> { ["rift"] = name, ["place"] = place }));
        }
    }

    /// <summary>The town nearest the middle of `patch`, ruins too, the first on a tie (attunement.nearest).</summary>
    static int? Nearest(Game game, (int, int) patch, int size)
    {
        double x = (patch.Item1 + 0.5) * size, y = (patch.Item2 + 0.5) * size;
        int? best = null;
        double closest = double.PositiveInfinity;
        for (int i = 0; i < game.Towns.Count; i++)
        {
            double distance = Migration.Hypot(game.Towns[i].HX - x, game.Towns[i].HY - y);
            if (distance < closest)
            {
                closest = distance;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Unease, poorer harvests, things that prey, and towns given up to the mark (attunement.effects).</summary>
    static void Effects(Game game, List<YearCore.Hardship> hardship, int year)
    {
        var marks = game.Marks!;
        var settings = game.Settings;
        marks.Harvest = Enumerable.Repeat(1.0, game.Towns.Count).ToList();
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int holder || i >= marks.Lands.Count)
            {
                marks.Preyed.Remove(i);
                marks.Uneasy.Remove(i);
                continue;
            }
            var faction = game.Factions[holder];
            string name = game.World.Towns[i].Name;
            var facts = new Dictionary<string, string> { ["place"] = name, ["faction"] = faction.Name };
            double mark = marks.Strength(i);
            if (mark >= settings.Severe)
            {
                town.Ruined = true;
                town.People = 0;
                marks.Abandoned.Add((year, i));
                marks.Preyed.Remove(i);
                marks.Uneasy.Remove(i);
                if (holder < hardship.Count)
                    hardship[holder].Lost += 2;
                game.Record(new GameEvent(year, "attunement", "abandoned_to_mark",
                    name + " is abandoned: no one can live on its land any more (the " + faction.Name + ")",
                    new[] { holder }, facts));
                continue;
            }
            if (mark >= settings.Faint)
            {
                marks.Harvest[i] = 1.0 - settings.HarvestLoss * Math.Min(1.0, mark / settings.Strong);
                if (marks.Uneasy.Add(i))
                    game.Record(new GameEvent(year, "attunement", "unease",
                        "unease at " + name + ": the land is marked, and the harvests fail", new[] { holder }, facts));
            }
            else
                marks.Uneasy.Remove(i);
            if (marks.Near[i].Any(patch => marks.Mark.GetValueOrDefault(patch, 0.0) >= settings.Strong))
            {
                town.People *= 1.0 - settings.Prey;
                if (marks.Preyed.Add(i))
                    game.Record(new GameEvent(year, "attunement", "prey",
                        "things from the marked land prey on " + name, new[] { holder }, facts));
            }
            else
                marks.Preyed.Remove(i);
        }
    }

    /// <summary>Each faction blames the factions whose mark lies on its towns' land: BLAME and its path's
    /// PATH_BLAME, in full at STRONG and less on fainter land, as strongly as its worst-marked town
    /// (attunement.add_blame).</summary>
    static void AddBlame(Game game, Dictionary<(int, int), double> blame)
    {
        var marks = game.Marks!;
        var settings = game.Settings;
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var town = game.Towns[i];
            if (town.Ruined || town.Holder is not int holder || i >= marks.Lands.Count)
                continue;
            var land = marks.Lands[i];
            int cells = land.Sum(p => p.Cells);
            if (cells == 0)
                continue;
            var theirs = new PyDict<int, double>();
            foreach (var (patch, count) in land)
                if (marks.Maker.TryGetValue(patch, out int maker) && maker != holder)
                    theirs[maker] = theirs.GetValueOrDefault(maker, 0.0) + marks.Mark.GetValueOrDefault(patch, 0.0) * count / cells;
            var faction = game.Factions[holder];
            double core = settings.Blame + (faction.Path != null ? settings.PathBlame.GetValueOrDefault(faction.Path) : 0.0);
            foreach (var (maker, mark) in theirs.Items())
            {
                if (mark < settings.Faint)
                    continue;
                double amount = Math.Min(1.0, Math.Max(0.0, core * Math.Min(1.0, mark / settings.Strong)));
                var key = (holder, maker);
                blame[key] = Math.Max(blame.GetValueOrDefault(key), amount);
            }
        }
    }
}
