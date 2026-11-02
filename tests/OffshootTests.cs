// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Offshoot paths and stances (step 8c): against the generator's own paths.Book
/// (tests/fixtures/make_expected.py, "offshoots"), and by what must follow where dice decide.</summary>
public class OffshootTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["offshoots"]!.AsObject();

    /// <summary>A game on a copy of the fixture whose settings are changed by `change` (factions, splits).</summary>
    static Game WithSettings(Action<JsonObject, JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Scratch.Root, "wr-offshoot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        var settings = data["continue"]!["settings"]!;
        change(settings["factions"]!.AsObject(), settings["splits"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    static Game Named() => WithSettings((factions, _) =>
        factions["PATH_NAMES"] = new JsonObject { ["handcraft"] = new JsonArray("attunement", "Stonehand") });

    [Fact]
    public void Three_paths_founded_agree_with_the_generator_s_book()
    {
        var game = Named();
        var s = game.Settings;
        var names = new List<string> { s.FoundPath("handcraft", "The Kilnrow", () => 0.0) };
        names.Add(s.FoundPath(names[0], "The Kilnrow", () => 0.0));
        names.Add(s.FoundPath("handcraft", "The Kilnrow", () => 0.0));
        Assert.Equal(Expected["names"]!.AsArray().Select(n => (string)n!), names);
        Assert.Equal(Expected["order"]!.AsArray().Select(n => (string)n!), s.PathSuits.Keys);
        foreach (string name in names)
        {
            var want = Expected["paths"]![name]!;
            string at = name + ": ";
            Assert.Equal((string)want["root"]!, s.PathRoot[name]);
            Assert.Equal((double)want["rituals"]!, s.RitualFactor(name));
            Assert.Equal((double)want["metal_need"]!, s.MetalNeed[name]);
            Assert.Equal((double)want["harvest"]!, s.PathHarvest[name]);
            Assert.Equal((double)want["steadiness"]!, s.PathSteadiness[name]);
            Assert.Equal((double)want["blame"]!, s.PathBlame[name]);
            Assert.Equal(want["land"]!.AsObject().Select(p => (p.Key, (string)p.Value!)).OrderBy(p => p.Key),
                         s.PathLand[name].Select(p => (p.Key, p.Value)).OrderBy(p => p.Key));
            Assert.Equal(want["row"]!.AsObject().Select(p => (p.Key, (double)p.Value!)),
                         s.PathOpinions[name].Select(p => (p.Key, p.Value)));
            var column = s.PathOpinions.Where(p => p.Key != name && p.Value.ContainsKey(name))
                                       .ToDictionary(p => p.Key, p => p.Value[name]);
            Assert.Equal(want["column"]!.AsObject().Select(p => (p.Key, (double)p.Value!)).OrderBy(p => p.Key),
                         column.Select(p => (p.Key, p.Value)).OrderBy(p => p.Key));
            Assert.Equal(s.PathSuits["handcraft"], s.PathSuits[name]);
            Assert.Equal(s.PathAbilities["handcraft"], s.PathAbilities[name]);
            Assert.True(at.Length > 0);
        }
    }

    [Fact]
    public void A_founded_path_s_values_move_by_at_most_PATH_SPREAD_and_stay_in_their_bounds()
    {
        var game = WithSettings((_, _) => { });
        var s = game.Settings;
        var dice = new Dice(7, "test");
        for (int i = 0; i < 30; i++)
        {
            string name = s.FoundPath("handcraft", "The Test", () => dice.Uniform(-0.5, 0.5));
            Assert.InRange(s.RitualFactor(name), 0.0, 1.5);
            Assert.InRange(s.MetalNeed[name], Math.Max(0, s.MetalNeed["handcraft"] - 0.5), s.MetalNeed["handcraft"] + 0.5);
            Assert.InRange(s.PathSteadiness[name], 0.0, 1.0);
            Assert.All(s.PathOpinions[name].Values, v => Assert.InRange(v, -1.0, 1.0));
            Assert.InRange(s.PathBlame[name], s.PathBlame["handcraft"] - 0.5, s.PathBlame["handcraft"] + 0.5);
        }
        Assert.Contains("Test way 30", s.PathSuits.Keys);
    }

    [Fact]
    public void A_founded_path_s_values_are_held_to_their_bounds_whatever_the_draw()
    {
        var s = WithSettings((_, _) => { }).Settings;
        string high = s.FoundPath("handcraft", "The High", () => 5.0);
        Assert.Equal(1.0, s.PathSteadiness[high]);
        Assert.All(s.PathOpinions[high].Values, v => Assert.Equal(1.0, v));
        Assert.Equal(1.0, s.PathOpinions["attunement"][high]);
        Assert.Equal(s.PathBlame["handcraft"] + 5.0, s.PathBlame[high], 12);      // blame has no bounds
        string low = s.FoundPath("handcraft", "The Low", () => -5.0);
        Assert.Equal((0.0, 0.0, 0.0, 0.0), (s.RitualFactor(low), s.MetalNeed[low], s.PathHarvest[low], s.PathSteadiness[low]));
        Assert.All(s.PathOpinions[low].Values, v => Assert.Equal(-1.0, v));
    }

    /// <summary>A breakaway of a faction holding at least two towns, where every new faction founds a path.</summary>
    static (Game Game, int Child, int Parent) BreakAway(Action<JsonObject> splits)
    {
        var game = WithSettings((_, s) => { s["OFFSHOOT_CHANCE"] = 1.0; splits(s); });
        game.Waiting.Clear();                        // no faction of the same people rises in its place
        int parent = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].People > 0
            && game.Towns.Count(t => t.Holder == f && !t.Ruined) >= 2);
        int town = game.Towns.FindLastIndex(t => t.Holder == parent && !t.Ruined);
        game.Towns[town].Origin = parent;
        int child = Splits.Secede(game, new List<int> { town }, parent, "divided from their rulers",
                                  game.Factions.Select(_ => new YearCore.Hardship()).ToList(), game.Year + 1);
        return (game, child, parent);
    }

    [Fact]
    public void A_breakaway_founds_a_path_of_its_own_and_the_book_tells_it_after_the_split()
    {
        var (game, child, parent) = BreakAway(_ => { });
        string kin = game.Factions[parent].Path!;
        string path = game.Factions[child].Path!;
        Assert.NotEqual(kin, path);
        Assert.Equal(game.Settings.PathRoot.GetValueOrDefault(kin, kin), game.Settings.PathRoot[path]);
        Assert.Contains(child, game.Founders);
        var whats = game.Events.Select(e => e.What).ToList();
        int split = whats.FindIndex(w => w.StartsWith("split_"));
        Assert.True(split >= 0 && whats.IndexOf("path_founded") > split);
        Assert.Equal("the " + game.Factions[child].Name + " found a path of their own, the " + path + ", from the " + kin,
                     game.Events.Single(e => e.What == "path_founded").Text);
    }

    [Fact]
    public void A_founder_may_take_a_stance_towards_all_others()
    {
        var before = WithSettings((_, _) => { });
        var (game, child, parent) = BreakAway(s => s["STANCES"] = new JsonObject
        {
            ["hostile"] = new JsonObject { ["chance"] = 1.0, ["memory"] = -0.5 },
        });
        Assert.Contains(game.Events, e => e.What == "split_stance" && e.Text == "the " + game.Factions[child].Name
                                                                        + " are hostile towards all others");
        for (int other = 0; other < before.Factions.Count; other++)
        {
            if (other == child)
                continue;
            double inherited = other == parent ? Splits.Bitter : before.Memory.GetValueOrDefault((parent, other));
            Assert.Equal(Math.Max(-1.0, inherited - 0.5), game.Memory[(child, other)], 12);
        }
    }

    [Fact]
    public void No_offshoot_or_stance_without_OFFSHOOT_CHANCE()
    {
        var game = WithSettings((_, s) => s["STANCES"] = new JsonObject
        {
            ["hostile"] = new JsonObject { ["chance"] = 1.0, ["memory"] = -0.5 },
        });
        game.Waiting.Clear();
        int parent = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].People > 0
            && game.Towns.Count(t => t.Holder == f && !t.Ruined) >= 2);
        int paths = game.Settings.PathSuits.Count;
        int town = game.Towns.FindLastIndex(t => t.Holder == parent && !t.Ruined);
        int child = Splits.Secede(game, new List<int> { town }, parent, "divided from their rulers",
                                  game.Factions.Select(_ => new YearCore.Hardship()).ToList(), 301);
        Assert.Equal(game.Factions[parent].Path, game.Factions[child].Path);
        Assert.Equal(paths, game.Settings.PathSuits.Count);
        Assert.DoesNotContain(game.Events, e => e.What is "path_founded" or "split_stance");
    }

    [Fact]
    public void A_path_s_ritual_factor_multiplies_its_rituals_chances()
    {
        var game = WithSettings((_, _) => { });
        var faction = game.Factions.First(f => f.Path == "attunement");
        Assert.Equal(0.3, Rituals.ByPath(game, 0.3, faction));
        string name = game.Settings.FoundPath("attunement", "The Test", () => -0.5);
        faction.Path = name;
        Assert.Equal(0.5, game.Settings.RitualFactor(name), 12);
        Assert.Equal(0.15, Rituals.ByPath(game, 0.3, faction), 12);
    }

    [Fact]
    public void A_save_keeps_the_paths_the_game_founded_and_its_founders()
    {
        var (game, child, _) = BreakAway(_ => { });
        string path = game.Factions[child].Path!;
        YearCore.RunYear(game);                     // the breakaway's year played out: the game saves between years
        string file = Path.Combine(Scratch.Root, "wr-offshoot-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(file, game);
        var loaded = SaveFile.Read(file);
        var s = loaded.Settings;
        Assert.Equal(game.Settings.PathSuits.Keys, s.PathSuits.Keys);
        Assert.Equal(game.Settings.PathOpinions[path], s.PathOpinions[path]);
        Assert.Equal(game.Settings.PathOpinions["handcraft"].GetValueOrDefault(path), s.PathOpinions["handcraft"].GetValueOrDefault(path));
        Assert.Equal(game.Settings.RitualFactor(path), s.RitualFactor(path));
        Assert.Equal(game.Founders, loaded.Founders);
        for (int i = 0; i < 10; i++)
        {
            YearCore.RunYear(game);
            YearCore.RunYear(loaded);
        }
        Assert.Equal(game.Events.Select(e => e.Text), loaded.Events.Select(e => e.Text));
    }
}
