// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>The god of death and her signs (step 9a): against the generator's own death.year_of_death,
/// death.year_of_loss and signs.Signs.year with the same stand-in dice (tests/fixtures/make_expected.py, "death"), and
/// by what must follow.</summary>
public class DeathTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["death"]!.AsObject();

    /// <summary>make_expected.py's FIXED_DICE, handed out in turn, over and over.</summary>
    static readonly double[] FixedDice = { 0.2, 0.0005, 0.7, 0.35, 0.001, 0.9, 0.55, 0.003, 0.15, 0.8, 0.0002, 0.45 };

    static Func<double> Fixed()
    {
        int at = 0;
        return () => FixedDice[at++ % FixedDice.Length];
    }

    /// <summary>A game on a copy of the fixture whose death settings are changed by `change`.</summary>
    static Game WithSettings(Action<JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Scratch.Root, "wr-death-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        change(data["continue"]!["settings"]!["death"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    [Fact]
    public void A_year_of_the_god_of_death_and_a_sign_agree_with_the_generator_s()
    {
        var game = WithSettings(d =>
        {
            d["DIG_ANGER"] = 0.05;
            d["SIGNS"] = new JsonObject
            {
                ["black_birds"] = new JsonObject
                {
                    ["chance"] = 1.0, ["dead"] = 100, ["reach"] = 30, ["belief"] = new JsonArray("fear", 1.0),
                    ["harvest"] = 0.5, ["years"] = 3,
                },
            };
        });
        int year = (int)Expected["year"]!;
        foreach (var site in Expected["sites"]!.AsArray())
            game.Deaths.Add(((int)site![0]!, (int)site[1]!, (int)site[2]!, (int)site[3]!));
        foreach (var site in Expected["mass"]!.AsArray())
            game.MassDeaths.Add(((int)site![0]!, (int)site[1]!, (int)site[2]!, (int)site[3]!));
        int second = (int)Expected["second"]!;

        var dice = Fixed();
        Death.Year(game, dice, year);
        game.Factions[second].People = (int)Expected["second_population"]!;
        Death.Loss(game, new Dictionary<int, double> { [second] = game.Factions[second].People / 4.0 }, dice, year);
        Signs.Year(game, Fixed(), year);

        Json.Same(Expected["hidden"], Death.Write(game.Hidden), "hidden");
        Json.Same(Expected["mines"], new JsonArray(game.Mines.Select(m => (JsonNode)new JsonArray(m.HX, m.HY, m.Metal, m.First,
            m.Owner is int o ? JsonValue.Create(o) : null, m.Name)).ToArray()), "mines");
        Json.Same(Expected["memory"], new JsonArray(game.Memory.OrderBy(m => m.Key.Item1).ThenBy(m => m.Key.Item2)
            .Select(m => (JsonNode)new JsonArray(m.Key.Item1, m.Key.Item2, m.Value)).ToArray()), "memory");
        Assert.Equal(Expected["beliefs"]!.AsArray().Select(b => (string)b!), game.Factions.Select(f => f.Belief));
        Json.Same(Expected["lasting"], new JsonObject(game.SignsLasting.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(
            p.Key.ToString(), (JsonNode?)new JsonArray(p.Value.Select(e => (JsonNode)new JsonArray(e.First, e.Last, e.Factor)).ToArray())))),
            "lasting");
        Assert.Equal(Expected["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                     game.Events.Select(e => (e.Text, e.What)));
        foreach (var what in new[] { "metal_found", "metal_found_mine_grows", "dig_angers", "black_birds", "belief_after_sign" })
            Assert.Contains(game.Events, e => e.What == what);
    }

    /// <summary>Dice that give `values` in turn (and then 0.99, which finds nothing).</summary>
    static Func<double> Given(params double[] values)
    {
        int at = 0;
        return () => at < values.Length ? values[at++] : 0.99;
    }

    [Fact]
    public void Metal_forms_on_land_after_a_rounded_delay_and_joins_the_nearest_earliest_find()
    {
        // METAL_DELAY a range (one draw), METAL_PER_DEATH a number (none): each site draws its two steps, then its delay.
        var game = WithSettings(d => { d["METAL_DELAY"] = new JsonArray(20, 21); d["METAL_PER_DEATH"] = 0.05; });
        int columns = game.Grid.Columns, rows = game.Grid.Rows;
        int spread = Math.Max(1, Py.Round(Death.Scatter * columns / 256.0));
        double Step(int d) => (d + spread + 0.5) / (2 * spread + 1);     // the draw that moves the metal d steps
        game.Hidden.Clear();

        // A step into the sea: the metal forms where they fell. 20.75 years round to 21.
        int sea = Enumerable.Range(0, columns * rows).First(c => game.Grid.Ocean[c] && c % columns > 0
                                                                  && !game.Grid.Ocean[c - 1]);
        int x = sea % columns - 1, y = sea / columns;
        game.Deaths.Add((101, x, y, 1000));
        Death.Year(game, Given(Step(1), Step(0), 0.75), 101);
        var formed = Assert.Single(game.Hidden);
        Assert.Equal((x, y, 101 + 21, 50.0), (formed.X, formed.Y, formed.Ready, formed.Metal));

        // Two finds equally near the next site: it joins the one that forms first, and keeps the later year.
        game.Hidden.Clear();
        game.Hidden.Add(new Hidden { X = 30, Y = 30, Metal = 1, Ready = 400, Died = 90, Dead = 10 });
        game.Hidden.Add(new Hidden { X = 34, Y = 30, Metal = 1, Ready = 300, Died = 90, Dead = 10 });
        game.Deaths.Clear();
        game.DeathsSeen = 0;
        game.Deaths.Add((101, 32, 30, 100));
        Death.Year(game, Given(Step(0), Step(0), 0.25), 101);         // 20.25 rounds to 20: ready in 121
        Assert.Equal((1.0, 10), (game.Hidden[0].Metal, game.Hidden[0].Dead));
        Assert.Equal((6.0, 110, 300), (game.Hidden[1].Metal, game.Hidden[1].Dead, game.Hidden[1].Ready));
    }

    [Fact]
    public void A_town_that_loses_many_at_once_is_a_death_site_and_few_or_a_small_share_are_not()
    {
        Assert.Equal(100.0, Death.MassDeathOf(1000, 900));
        Assert.Equal(0.0, Death.MassDeathOf(1000, 901));               // under a tenth
        Assert.Equal(0.0, Death.MassDeathOf(400, 351));                // under 50
        Assert.Equal(50.0, Death.MassDeathOf(400, 350));
    }

    [Fact]
    public void A_people_who_did_not_know_her_find_metal_and_come_to_deny_or_worship_her()
    {
        foreach (var (risk, belief) in new[] { (0.8, "deny"), (0.2, "worship") })
        {
            var game = WithSettings(_ => { });
            int f = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].People > 0);
            int town = game.Towns.FindIndex(t => t.Holder == f && !t.Ruined);
            game.Factions[f].Belief = "unknowing";
            game.Factions[f].Traits["risk"] = risk;
            game.Hidden = new List<Hidden> { new() { X = game.Towns[town].HX, Y = game.Towns[town].HY, Metal = 10, Ready = 1, Died = 1, Dead = 300 } };
            int mines = game.Mines.Count;
            Death.Year(game, () => 0.0, 101);                          // every roll finds
            Assert.Equal(belief, game.Factions[f].Belief);
            Assert.Contains(game.Events, e => e.What == "belief_after_find");
            Assert.Empty(game.Hidden);
            Assert.True(game.Mines.Count == mines + 1 || game.Events.Any(e => e.What == "metal_found_mine_grows"));
        }
    }

    [Fact]
    public void Metal_not_yet_formed_or_out_of_reach_is_not_found()
    {
        var game = WithSettings(_ => { });
        int town = game.Towns.FindIndex(t => t.Holder != null && !t.Ruined);
        game.Hidden = new List<Hidden> { new() { X = game.Towns[town].HX, Y = game.Towns[town].HY, Metal = 10, Ready = 200, Died = 100, Dead = 300 } };
        Death.Year(game, () => 0.0, 101);
        Assert.Single(game.Hidden);
        Assert.DoesNotContain(game.Events, e => e.What.StartsWith("metal_found"));
    }

    [Fact]
    public void A_great_loss_turns_a_people_who_deny_her_to_fear_and_not_one_that_worships_her()
    {
        var game = WithSettings(_ => { });
        int f = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].People > 0);
        game.Factions[f].Belief = "deny";
        double lost = game.Factions[f].People * 0.2;
        Death.Loss(game, new Dictionary<int, double> { [f] = lost }, () => 0.0, 101);
        Assert.Equal("fear", game.Factions[f].Belief);
        Assert.Equal("the " + game.Factions[f].Name + " come to fear the god of death after losing " + Py.Round(lost) + " of their own",
                     game.Events.Single().Text);
        game.Factions[f].Belief = "worship";
        Death.Loss(game, new Dictionary<int, double> { [f] = lost }, () => 0.0, 102);
        Assert.Equal("worship", game.Factions[f].Belief);
        Death.Loss(game, new Dictionary<int, double> { [f] = game.Factions[f].People * 0.05 }, () => 0.0, 103);
        Assert.Single(game.Events);                                     // a small loss: nothing
    }

    [Fact]
    public void A_played_year_marks_its_battles_and_mass_deaths_and_their_metal_forms_later()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 4);
        for (int i = 0; i < 40; i++)
        {
            int dead = game.Hidden.Sum(h => h.Dead);
            YearCore.RunYear(game);
            var sites = game.Deaths.Concat(game.MassDeaths).Where(d => d.Dead > 0).ToList();
            if (sites.Count == 0 || game.Events.Any(e => e.Year == game.Year && e.Kind == "metal"))
                continue;
            // Every one of this year's dead is hidden metal now, a new find or joined to one close by, not yet formed.
            Assert.Equal(dead + sites.Sum(d => d.Dead), game.Hidden.Sum(h => h.Dead));
            Assert.Contains(game.Hidden, h => h.Ready > game.Year);
            return;
        }
        Assert.Fail("no one died in battle or at once in 40 years");
    }

    [Fact]
    public void A_game_s_god_of_death_plays_on_the_same_from_a_save()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 6);
        for (int i = 0; i < 80; i++)
            YearCore.RunYear(game);
        string path = Path.Combine(Scratch.Root, "wr-death-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Json.Same(Death.Write(game.Hidden), Death.Write(loaded.Hidden), "hidden");
        Assert.Equal(game.Mines.Select(m => (m.Name, m.HX, m.HY, m.Metal, m.First, m.Owner)),
                     loaded.Mines.Select(m => (m.Name, m.HX, m.HY, m.Metal, m.First, m.Owner)));
        for (int i = 0; i < 30; i++)
        {
            YearCore.RunYear(game);
            YearCore.RunYear(loaded);
        }
        Assert.Equal(game.Events.Select(e => e.Text), loaded.Events.Select(e => e.Text));
        Assert.Contains(game.Events, e => e.Kind is "metal" or "belief");
    }
}
