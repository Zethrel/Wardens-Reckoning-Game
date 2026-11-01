// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Attunement's mark and the harvest ritual (step 8a): against the generator's own
/// attunement.year_of_attunement and rituals.year_of_harvest_rituals (tests/fixtures/make_expected.py,
/// "attunement"), and by what must follow.</summary>
public class AttunementTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["attunement"]!.AsObject();

    /// <summary>A game on a copy of the fixture whose attunement settings are changed by `change`.</summary>
    static Game WithSettings(Action<JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Scratch.Root, "wr-attunement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        change(data["continue"]!["settings"]!["attunement"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    static Market[] OneYearOfTrade(Game game)
    {
        var harvest = Enumerable.Range(0, game.Towns.Count).Select(i =>
        {
            var faction = game.Towns[i].Holder is int h ? game.Factions[h] : null;
            return YearCore.PathHarvest(1.0, faction?.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings);
        }).ToList();
        var markets = Trade.MakeMarkets(game, harvest);
        Trade.TradeGoods(game, markets);
        return markets;
    }

    /// <summary>The setup of make_expected.py's attunement_years: MARK_PER_METAL 30 times the export's, every town
    /// of a living attunement faction using 20 metal a year, each such faction given the next mine with metal, every
    /// town of an attunement faction hungry, and a certain harvest ritual that goes wrong (0.5).</summary>
    static (Game Game, Market[] Markets) HardMarked()
    {
        var game = WithSettings(a =>
        {
            a["MARK_PER_METAL"] = (double)a["MARK_PER_METAL"]! * 30;
            a["HARVEST_RITUAL_CHANCE"] = 1.0;
            a["HARVEST_RITUAL_POOR"] = 0.0;
            a["HARVEST_RITUAL"] = new JsonArray(0.5, 0.5);
        });
        var markets = OneYearOfTrade(game);
        var attuned = Enumerable.Range(0, game.Factions.Count)
            .Where(f => game.Factions[f].Path == "attunement" && game.Factions[f].People > 0).ToList();
        for (int i = 0; i < game.Towns.Count; i++)
            if (!game.Towns[i].Ruined && game.Towns[i].Holder is int h && attuned.Contains(h))
                markets[i].Made[Trade.Metal] = markets[i].Needed[Trade.Metal] = 20.0;
        var stocked = game.Mines.Where(m => m.Metal > 0).GetEnumerator();
        foreach (int f in attuned)
        {
            stocked.MoveNext();
            stocked.Current.Owner = f;
        }
        foreach (var town in game.Towns)
            if (town.Holder is int h && game.Factions[h].Path == "attunement")
                town.Hungry = 1;
        return (game, markets);
    }

    [Fact]
    public void A_harvest_ritual_and_five_years_of_marks_agree_with_the_generator_s()
    {
        var (game, markets) = HardMarked();

        // The harvest ritual: every faction that works rituals with a hungry town works one, and it goes wrong.
        var worked = Rituals.HarvestYear(game, Enumerable.Repeat(1.0, game.Towns.Count).ToList(), 300);
        var ritual = Expected["ritual"]!;
        Json.Same(ritual["worked"], new JsonArray(worked.OrderBy(w => w.Key)
            .Select(w => (JsonNode)new JsonArray(w.Key, w.Value)).ToArray()), "worked");
        Assert.Equal(ritual["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                     game.Events.Select(e => (e.Text, e.What)));
        Json.Same(ritual["marks"], game.Marks!.Write(), "ritual marks");
        Json.Same(ritual["metal"], new JsonArray(game.Mines.Select(m => (JsonNode)m.Metal).ToArray()), "metal");
        Json.Same(ritual["memory"], new JsonArray(game.Memory.OrderBy(m => m.Key.Item1).ThenBy(m => m.Key.Item2)
            .Select(m => (JsonNode)new JsonArray(m.Key.Item1, m.Key.Item2, m.Value)).ToArray()), "memory");

        // Five years of marking, spreading, fading, rifts and what the marks do.
        var years = Expected["years"]!.AsArray();
        for (int step = 0; step < years.Count; step++)
        {
            var want = years[step]!;
            var hardship = game.Factions.Select(_ => new YearCore.Hardship()).ToList();
            var blame = new Dictionary<(int, int), double>();
            int before = game.Events.Count;
            Attunement.Year(game, markets, hardship, blame, 301 + step);
            string at = "year " + (301 + step);
            Json.Same(want["marks"], game.Marks!.Write(), at + " marks");
            Json.Same(want["people"], new JsonArray(game.Towns.Select(t => (JsonNode)t.People).ToArray()), at + " people");
            Assert.Equal(want["ruined"]!.AsArray().Select(r => (bool)r!), game.Towns.Select(t => t.Ruined));
            Assert.Equal(want["lost"]!.AsArray().Select(l => (int)l!), hardship.Select(h => h.Lost));
            Json.Same(want["blame"], new JsonArray(blame.OrderBy(b => b.Key.Item1).ThenBy(b => b.Key.Item2)
                .Select(b => (JsonNode)new JsonArray(b.Key.Item1, b.Key.Item2, b.Value)).ToArray()), at + " blame");
            Assert.Equal(want["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                         game.Events.Skip(before).Select(e => (e.Text, e.What)));
        }
        foreach (var what in new[] { "unease", "prey", "rift", "abandoned_to_mark" })
            Assert.Contains(game.Events, e => e.What == what);
    }

    [Fact]
    public void An_opened_world_holds_the_export_s_marks_as_they_are()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 1);
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "world-7", "world.json")))!;
        Json.Same(data["continue"]!["marks"], game.Marks!.Write(), "marks");
        Assert.Equal(game.Towns.Count, game.Marks!.Lands.Count);
    }

    [Fact]
    public void No_one_settles_in_a_rift_the_game_opens()
    {
        var (game, markets) = HardMarked();
        for (int step = 0; step < 3; step++)
            Attunement.Year(game, markets, game.Factions.Select(_ => new YearCore.Hardship()).ToList(),
                            new Dictionary<(int, int), double>(), 301 + step);
        var rift = game.Marks!.Severe.First();
        int size = game.Marks.Size;
        Assert.True(Migration.IsRift(game, rift.Item1 * size, rift.Item2 * size));
        Assert.False(Migration.IsRift(game, 0, 0) && !game.Marks.Severe.Contains((0, 0)));
    }

    [Fact]
    public void A_town_the_game_founds_has_its_land_by_patch()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 1);
        int towns = game.Marks!.Lands.Count, size = game.Marks.Size;
        game.Marks.AddLand(new[] { (0, 0), (1, 0), (size, 0), (0, 1) });
        Assert.Equal(towns + 1, game.Marks.Lands.Count);
        Assert.Equal(new[] { ((0, 0), 3), ((1, 0), 1) }, game.Marks.Lands[^1].ToArray());
        Assert.Contains((0, 0), game.Marks.Near[^1]);
    }

    [Fact]
    public void A_frightened_faction_works_a_harvest_ritual_in_a_poor_year_the_less_the_better_it_plans()
    {
        int Rituals(double planning)
        {
            int count = 0;
            for (long seed = 1; seed <= 40; seed++)
            {
                var game = WithSettings(a => a["HARVEST_RITUAL_POOR"] = 0.9, seed);
                int f = Enumerable.Range(0, game.Factions.Count)
                    .First(i => game.Factions[i].Path == "attunement" && game.Factions[i].People > 0);
                game.Factions[f].Traits["planning"] = planning;
                foreach (var town in game.Towns)
                    town.Hungry = 0;
                game.Mines.First(m => m.Metal > 0).Owner = f;
                var worked = Sim.Rituals.HarvestYear(game, Enumerable.Repeat(0.5, game.Towns.Count).ToList(), 300);
                if (worked.ContainsKey(f))
                {
                    count++;
                    Assert.Contains(game.Events, e => e.Facts.GetValueOrDefault("why") == "poor harvest");
                }
            }
            return count;
        }
        Assert.Equal(0, Rituals(1.0));
        Assert.True(Rituals(0.0) > Rituals(0.8));
    }

    [Fact]
    public void A_faction_without_metal_works_no_harvest_ritual()
    {
        var game = WithSettings(a => { a["HARVEST_RITUAL_CHANCE"] = 1.0; a["HARVEST_RITUAL_POOR"] = 0.0; });
        foreach (var mine in game.Mines)
            mine.Owner = null;
        foreach (var town in game.Towns)
            town.Hungry = 1;
        Assert.Empty(Sim.Rituals.HarvestYear(game, Enumerable.Repeat(1.0, game.Towns.Count).ToList(), 300));
    }

    /// <summary>A game whose marks only do what they do: no marking, spreading or fading.</summary>
    static Game StillMarks() => WithSettings(a =>
    {
        a["MARK_PER_METAL"] = 0.0;
        a["RIVER_SHARE"] = 0.0;
        a["ANIMAL_SHARE"] = 0.0;
        a["MARK_HALF_LIFE"] = 1e12;
    });

    /// <summary>Every patch of a town's land marked `mark`, by `maker`.</summary>
    static void MarkLand(Game game, int town, double mark, int maker)
    {
        foreach (var (patch, _) in game.Marks!.Lands[town])
        {
            game.Marks.Mark[patch] = mark;
            game.Marks.Maker[patch] = maker;
        }
    }

    [Fact]
    public void A_faint_mark_costs_part_of_the_harvest_in_step_with_its_strength()
    {
        var game = StillMarks();
        var s = game.Settings;
        int town = Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined && game.Towns[t].Holder != null
            && game.Marks!.Lands[t].All(p => !game.Marks.Mark.ContainsKey(p.Patch)) && game.Marks.Lands[t].Count > 0);
        double mid = (s.Faint + s.Strong) / 2;
        MarkLand(game, town, mid, game.Towns[town].Holder!.Value);
        Attunement.Year(game, OneYearOfTrade(game), game.Factions.Select(_ => new YearCore.Hardship()).ToList(),
                        new Dictionary<(int, int), double>(), 301);
        Assert.Equal(1.0 - s.HarvestLoss * mid / s.Strong, game.Marks!.HarvestOf(town), 12);
        Assert.Contains(town, game.Marks.Uneasy);
    }

    [Fact]
    public void A_faction_blames_another_as_strongly_as_its_worst_marked_town()
    {
        var game = StillMarks();
        var s = game.Settings;
        int holder = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].People > 0
            && Enumerable.Range(0, game.Towns.Count).Count(t => !game.Towns[t].Ruined && game.Towns[t].Holder == f
                                                                && game.Marks!.Lands[t].Count > 0) >= 2);
        int maker = Enumerable.Range(0, game.Factions.Count).First(f => f != holder);
        var towns = Enumerable.Range(0, game.Towns.Count).Where(t => !game.Towns[t].Ruined && game.Towns[t].Holder == holder
                                                                     && game.Marks!.Lands[t].Count > 0).Take(2).ToList();
        foreach (var patch in game.Marks!.Mark.Keys())
            game.Marks.Mark.Remove(patch);
        MarkLand(game, towns[0], s.Strong * 0.9, maker);
        MarkLand(game, towns[1], s.Strong * 0.8, maker);
        var blame = new Dictionary<(int, int), double>();
        Attunement.Year(game, OneYearOfTrade(game), game.Factions.Select(_ => new YearCore.Hardship()).ToList(), blame, 301);
        double core = s.Blame + s.PathBlame.GetValueOrDefault(game.Factions[holder].Path!);
        Assert.Equal(Math.Min(1.0, Math.Max(0.0, core * 0.9)), blame[(holder, maker)], 12);
    }

    [Fact]
    public void Equal_marks_arriving_at_a_patch_make_the_faction_with_the_higher_number_its_maker()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 1);
        var marks = new Marks { Size = 1, Columns = 3, Rows = 1 };
        marks.Land.UnionWith(new[] { (0, 0), (1, 0), (2, 0) });
        marks.Settle();
        marks.Mark[(0, 0)] = 1.0;
        marks.Maker[(0, 0)] = 2;
        marks.Mark[(2, 0)] = 1.0;
        marks.Maker[(2, 0)] = 5;
        Attunement.Spread(marks, game.Settings);
        Assert.Equal(5, marks.Maker[(1, 0)]);
    }

    [Fact]
    public void A_town_s_mark_adds_up_as_Python_adds()
    {
        var marks = new Marks { Size = 1, Columns = 3, Rows = 1 };
        marks.Lands.Add(new List<((int, int), int)> { ((0, 0), 1), ((1, 0), 1), ((2, 0), 1) });
        marks.Mark[(0, 0)] = 1e16;
        marks.Mark[(1, 0)] = 1.0;
        marks.Mark[(2, 0)] = 1.0;
        Assert.Equal((1e16 + 2.0) / 3, marks.Strength(0));      // one by one, the two 1.0s would be lost
    }

    [Fact]
    public void A_harvest_ritual_s_outcome_is_the_harvest_of_every_town_of_its_faction()
    {
        var game = WithSettings(a =>
        {
            a["HARVEST_RITUAL_CHANCE"] = 1.0;
            a["HARVEST_RITUAL_POOR"] = 0.0;
            a["HARVEST_RITUAL"] = new JsonArray(1.5, 1.5);
            a["HARVEST_RITUAL_SKILL"] = 0.0;
        });
        int f = Enumerable.Range(0, game.Factions.Count)
            .First(i => game.Factions[i].Path == "attunement" && game.Factions[i].People > 0);
        game.Mines.First(m => m.Metal > 0).Owner = f;
        var theirs = Enumerable.Range(0, game.Towns.Count).Where(t => !game.Towns[t].Ruined && game.Towns[t].Holder == f).ToList();
        game.Towns[theirs[0]].Hungry = 1;
        var plain = theirs.Select(t => YearCore.Harvest(game, t, 301) * game.Marks!.HarvestOf(t)).ToList();
        var harvests = YearCore.Harvests(game, 301);
        for (int k = 0; k < theirs.Count; k++)
            Assert.Equal(plain[k] * 1.5, harvests[theirs[k]], 12);
    }

    [Fact]
    public void A_game_with_marks_plays_on_the_same_from_a_save()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 5);
        for (int i = 0; i < 20; i++)
            YearCore.RunYear(game);
        string path = Path.Combine(Scratch.Root, "wr-attunement-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Json.Same(game.Marks!.Write(), loaded.Marks!.Write(), "saved marks");
        for (int i = 0; i < 20; i++)
        {
            YearCore.RunYear(game);
            YearCore.RunYear(loaded);
        }
        Assert.Equal(game.Events.Select(e => e.Text), loaded.Events.Select(e => e.Text));
        Json.Same(game.Marks!.Write(), loaded.Marks!.Write(), "marks after");
        Assert.Contains(game.Events, e => e.What == "colony_founded");
        Assert.Equal(game.Towns.Count, game.Marks!.Lands.Count);            // every town the game founded has its land
        Assert.Contains(game.Events, e => e.Kind == "attunement");
    }
}
