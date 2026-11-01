// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Great rituals in war (step 8b): against the generator's own rituals.year_of_rituals made certain
/// (tests/fixtures/make_expected.py, "great_ritual"), and by what must follow where dice decide.</summary>
public class RitualTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["great_ritual"]!.AsObject();

    /// <summary>A game on a copy of the fixture whose attunement settings are changed by `change`.</summary>
    static Game WithSettings(Action<JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Scratch.Root, "wr-ritual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        change(data["continue"]!["settings"]!["attunement"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    /// <summary>The setup of make_expected.py's great_ritual: the first living attunement faction with towns at war
    /// with the first other, losing (10 000 dead), with a mine of its own; returns the game, the war and the two.</summary>
    static (Game Game, WarNow War, int Mage, int Enemy) AtWar(double chance = 1000.0, long seed = 1)
    {
        var game = WithSettings(a => { a["RITUAL_CHANCE"] = chance; a["RITUAL_DEATHS"] = 0.3; }, seed);
        var withTowns = Enumerable.Range(0, game.Factions.Count).Where(f => game.Factions[f].People > 0
            && game.Towns.Any(t => t.Holder == f && !t.Ruined)).ToList();
        int mage = withTowns.First(f => game.Factions[f].Path == "attunement");
        int enemy = withTowns.First(f => f != mage);
        game.Mines.First(m => m.Metal > 0).Owner = mage;
        game.Wars.Clear();
        var war = new WarNow { Attacker = enemy, Defender = mage, Since = 300, Sides = new[] { new HashSet<int> { enemy },
                               new HashSet<int> { mage } }, Dead = new Dictionary<int, double> { [mage] = 10000.0 } };
        game.Wars.Add(war);
        return (game, war, mage, enemy);
    }

    static JsonArray Triples(IEnumerable<((int, int) Key, double Value)> pairs) =>
        new(pairs.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2)
                 .Select(p => (JsonNode)new JsonArray(p.Key.Item1, p.Key.Item2, p.Value)).ToArray());

    [Fact]
    public void A_great_ritual_agrees_with_the_generator_s()
    {
        var (game, war, mage, enemy) = AtWar();
        Assert.Equal((int)Expected["mage"]!, mage);
        Assert.Equal((int)Expected["enemy"]!, enemy);
        Rituals.WarYear(game, 301);
        Json.Same(Expected["dead"], new JsonArray(war.Dead.OrderBy(d => d.Key)
            .Select(d => (JsonNode)new JsonArray(d.Key, d.Value)).ToArray()), "dead");
        Assert.Equal(Expected["worked"]!.AsArray().Select(f => (int)f!), war.Rituals.Order());
        Json.Same(Expected["people"], new JsonArray(game.Towns.Select(t => (JsonNode)t.People).ToArray()), "people");
        Json.Same(Expected["memory"], Triples(game.Memory.Select(m => (m.Key, m.Value))), "memory");
        Json.Same(Expected["metal"], new JsonArray(game.Mines.Select(m => (JsonNode)m.Metal).ToArray()), "metal");
        Json.Same(Expected["marks"], game.Marks!.Write(), "marks");
        Assert.Equal(Expected["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                     game.Events.Select(e => (e.Text, e.What)));
        var pair = Rituals.NearestPair(game, mage, war.Sides[0])!.Value;
        Assert.Equal(Expected["pair"]!.AsArray().Select(p => (int)p!), new[] { pair.Source, pair.Target });
    }

    [Fact]
    public void A_faction_works_one_great_ritual_in_a_war_at_most()
    {
        var (game, war, _, _) = AtWar();
        Rituals.WarYear(game, 301);
        Rituals.WarYear(game, 302);
        Assert.Single(game.Events, e => e.What == "great_ritual");
    }

    [Fact]
    public void A_side_that_is_not_losing_or_not_desperate_works_none()
    {
        var (game, war, mage, enemy) = AtWar();
        war.Dead[enemy] = 1e9;                                   // the other side is wearier
        Rituals.WarYear(game, 301);
        Assert.Empty(game.Events);

        (game, war, mage, _) = AtWar();
        double people = game.Factions[mage].People;
        double desperate = game.Settings.RitualDesperation;
        war.Dead[mage] = desperate * people / (1 - desperate) * 0.99;   // just short of RITUAL_DESPERATION
        Assert.True(Rituals.Losing(game, war, mage) < desperate);
        Rituals.WarYear(game, 301);
        Assert.Empty(game.Events);
    }

    [Fact]
    public void A_careful_ruler_or_no_metal_works_none()
    {
        var (game, _, mage, _) = AtWar();
        game.Factions[mage].Ruler!.Traits["planning"] = 1.0;
        game.Factions[mage].Ruler!.Traits["ambition"] = 0.0;      // recklessness 0
        Rituals.WarYear(game, 301);
        Assert.Empty(game.Events);

        (game, _, mage, _) = AtWar();
        foreach (var mine in game.Mines.Where(m => m.Owner == mage))
            mine.Metal = 0;
        Rituals.WarYear(game, 301);
        Assert.Empty(game.Events);
    }

    [Theory]
    [InlineData(0.0, 1.0, 1.0)]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(0.5, 0.5, 0.5)]
    [InlineData(1.0, 1.0, 0.5)]
    [InlineData(0.0, 0.0, 0.5)]
    public void Recklessness_is_low_planning_and_high_ambition(double planning, double ambition, double reckless)
    {
        var ruler = new Ruler { Traits = new Dictionary<string, double> { ["planning"] = planning, ["ambition"] = ambition } };
        Assert.Equal(reckless, Rituals.Recklessness(ruler));
        Assert.Equal(0.0, Rituals.Recklessness(null));
    }

    [Fact]
    public void A_reckless_ruler_works_one_twice_as_often()
    {
        int Worked(double planning, double ambition)
        {
            int count = 0;
            for (long seed = 1; seed <= 300; seed++)
            {
                var (game, _, mage, _) = AtWar(chance: 0.25, seed: seed);
                game.Factions[mage].Ruler!.Traits["planning"] = planning;
                game.Factions[mage].Ruler!.Traits["ambition"] = ambition;
                Rituals.WarYear(game, 301);
                count += game.Events.Count;
            }
            return count;
        }
        int reckless = Worked(0.0, 1.0), middling = Worked(0.5, 0.5);    // chances 0.5 and 0.25
        Assert.InRange(reckless, 120, 180);
        Assert.InRange(middling, 50, 100);
    }

    [Fact]
    public void Every_town_of_the_enemy_s_side_near_the_crater_loses_people_and_none_further()
    {
        var (game, war, mage, enemy) = AtWar();
        var before = game.Towns.Select(t => t.People).ToList();
        Rituals.WarYear(game, 301);
        var (source, target) = Rituals.NearestPair(game, mage, war.Sides[0])!.Value;
        double x = Py.Round((game.Towns[source].HX + game.Towns[target].HX) / 2.0);
        double y = Py.Round((game.Towns[source].HY + game.Towns[target].HY) / 2.0);
        double reach = 2 * game.Settings.CraterRadius * game.Grid.Columns / 256.0;
        for (int t = 0; t < game.Towns.Count; t++)
        {
            bool struck = !game.Towns[t].Ruined && game.Towns[t].Holder == enemy
                          && (t == target || Migration.Hypot(game.Towns[t].HX - x, game.Towns[t].HY - y) <= reach);
            Assert.Equal(struck ? before[t] * 0.7 : before[t], game.Towns[t].People, 9);
        }
    }

    [Fact]
    public void A_war_s_great_rituals_are_kept_in_a_save()
    {
        var (game, war, mage, _) = AtWar();
        Rituals.WarYear(game, 301);
        string path = Path.Combine(Scratch.Root, "wr-ritual-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Assert.Equal(new[] { mage }, loaded.Wars.Single().Rituals);
    }
}
