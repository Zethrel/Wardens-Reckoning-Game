// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Named threats and the great rituals against them (step 9b): against the generator's own
/// threats.Threats.year and rituals.year_of_threat_rituals with the same stand-in dice
/// (tests/fixtures/make_expected.py, "threats"), and by what must follow.</summary>
public class ThreatTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["threats"]!.AsObject();

    /// <summary>make_expected.py's FIXED_DICE, handed out in turn, over and over.</summary>
    static readonly double[] FixedDice = { 0.2, 0.0005, 0.7, 0.35, 0.001, 0.9, 0.55, 0.003, 0.15, 0.8, 0.0002, 0.45 };

    static Func<double> Fixed(Action? counted = null)
    {
        int at = 0;
        return () => { counted?.Invoke(); return FixedDice[at++ % FixedDice.Length]; };
    }

    /// <summary>A game on a copy of the fixture whose settings ("continue"."settings") are changed by `change`.</summary>
    static Game WithSettings(Action<JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Scratch.Root, "wr-threats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        change(data["continue"]!["settings"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    /// <summary>The fixture with make_expected.py's test kinds before the export's own (the wyrm's first name the
    /// first town's), RISE_CHANCE 1, RITUAL_DESPERATION 0 and RITUAL_CHANCE 0.5.</summary>
    static Game TestGame() => WithSettings(settings =>
    {
        var kinds = Expected["kinds"]!.DeepClone().AsObject();
        var names = kinds["wyrm"]!["names"]!.AsArray();
        names.Insert(0, (string)Expected["first_name"]!);
        var threats = settings["threats"]!.AsObject();
        foreach (var (name, about) in threats["THREAT_KINDS"]!.AsObject())
            kinds[name] = about!.DeepClone();
        threats["THREAT_KINDS"] = kinds;
        threats["RISE_CHANCE"] = 1.0;
        settings["attunement"]!["RITUAL_DESPERATION"] = 0.0;
        settings["attunement"]!["RITUAL_CHANCE"] = 0.5;
    });

    [Fact]
    public void Years_of_threats_and_their_rituals_agree_with_the_generator_s()
    {
        var game = TestGame();
        int used = 0;
        var dice = Fixed(() => used++);
        var supply = game.Factions.Select(_ => new Dictionary<string, double[]>()).ToList();   // fully armed
        var factors = new JsonArray();
        int first = game.Year;
        for (int year = first + 1; year <= first + (int)Expected["years"]!; year++)
        {
            factors.Add(new JsonArray(Enumerable.Range(0, game.Towns.Count)
                .Select(i => (JsonNode)Threats.Factor(game, i, year)).ToArray()));
            Threats.Year(game, dice, supply, year);
            Rituals.ThreatYear(game, dice, year);
        }

        var want = Expected["threats"]!;
        Json.Same(want["threats"], Threats.Write(game.Threats), "threats");
        foreach (var (kind, given) in want["given"]!.AsObject())
            Assert.Equal((int)given!, game.ThreatNamesGiven.GetValueOrDefault(kind));
        Json.Same(want["spoiled"], new JsonObject(game.Spoiled.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(
            p.Key.ToString(), (JsonNode?)new JsonArray(p.Value.Year, p.Value.Factor)))), "spoiled");
        Json.Same(Expected["factors"], factors, "factors");
        Json.Same(Expected["people"], new JsonArray(game.Towns.Select(t => (JsonNode)t.People).ToArray()), "people");
        Json.Same(Expected["deaths"], new JsonArray(game.Deaths.Select(d => (JsonNode)new JsonArray(d.Year, d.X, d.Y, d.Dead)).ToArray()), "deaths");
        Json.Same(Expected["metal"], new JsonArray(game.Mines.Select(m => (JsonNode)m.Metal).ToArray()), "metal");
        Json.Same(Expected["marks"], game.Marks!.Write(), "marks");
        Assert.Equal(Expected["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                     game.Events.Select(e => (e.Text, e.What)));
        Assert.Equal((int)Expected["dice_used"]!, used);
        // Every way a threat's year can go is in it.
        foreach (var what in new[] { "threat_rises", "threat_moves", "threat_raids", "threat_slain", "threat_beaten_off",
                                     "threat_fades", "threat_sleeps", "threat_wakes", "threat_ritual" })
            Assert.Contains(game.Events, e => e.What == what);
        Assert.Contains(game.Threats, t => t.Name == "Old Smoulder");          // the first town's name was taken
        Assert.Contains(game.Threats, t => t.Kind == "ghast");
    }

    [Fact]
    public void The_undead_rise_where_magic_s_mark_is_strong_if_they_can()
    {
        var game = TestGame();
        var marks = game.Marks!;
        var ghast = game.Settings.KindOf("ghast");
        var wyrm = game.Settings.KindOf("wyrm");
        var dice = new Dice(3, "test");
        bool Torn((int X, int Y) cell) =>
            marks.Mark.GetValueOrDefault((cell.X / marks.Size, cell.Y / marks.Size), 0.0) >= game.Settings.Strong;
        var undead = Enumerable.Range(0, 40).Select(_ => Threats.FindLair(game, ghast, dice.Random)).ToList();
        Assert.All(undead, lair => Assert.True(lair != null && Torn(lair.Value)));
        var living = Enumerable.Range(0, 40).Select(_ => Threats.FindLair(game, wyrm, dice.Random)!.Value).ToList();
        Assert.Contains(living, lair => !Torn(lair));
        foreach (var (x, y) in undead.Select(l => l!.Value).Concat(living))
        {
            int cell = y * game.Grid.Columns + x;
            Assert.True(game.Grid.Owner[cell] < 0 && game.LandBiome[cell] >= 0, "a lair is on land no town holds");
            Assert.NotEmpty(Threats.InReach(game, x, y, Threats.Reach(game, "wyrm")));
        }
    }

    [Fact]
    public void A_lair_keeps_to_its_kind_s_biomes()
    {
        var game = TestGame();
        var dragon = game.Settings.KindOf("dragon");
        var dice = new Dice(5, "test");
        for (int i = 0; i < 30; i++)
            if (Threats.FindLair(game, dragon, dice.Random) is { } lair)
                Assert.Contains(game.BiomeNames[game.LandBiome[lair.Y * game.Grid.Columns + lair.X]], dragon.Biomes);
    }

    [Fact]
    public void A_kind_is_drawn_by_weight_as_python_s_choices()
    {
        var kinds = TestGame().Settings.ThreatKinds;
        double total = kinds.Sum(k => k.Weight);
        Assert.Equal("wyrm", Threats.DrawKind(kinds, () => 0.0).Name);
        Assert.Equal("wyrm", Threats.DrawKind(kinds, () => 19.999 / total).Name);
        Assert.Equal("ghast", Threats.DrawKind(kinds, () => 20.0 / total).Name);  // a total equal to the draw is passed
        Assert.Equal(kinds[^1].Name, Threats.DrawKind(kinds, () => 0.9999999).Name);
    }

    [Fact]
    public void A_sample_draws_different_items_and_as_many_as_asked()
    {
        var dice = new Dice(9, "test");
        var items = Enumerable.Range(0, 50).ToList();
        var chosen = Threats.Sample(items, 20, dice.Random);
        Assert.Equal(20, chosen.Count);
        Assert.Equal(20, chosen.Distinct().Count());
        Assert.Equal(items, Threats.Sample(items, 50, dice.Random).Order());
    }

    [Fact]
    public void A_town_defends_with_its_faction_behind_its_walls_or_alone_with_its_people()
    {
        var game = TestGame();
        var supply = game.Factions.Select(_ => new Dictionary<string, double[]>()).ToList();
        int held = Enumerable.Range(0, game.Towns.Count).First(i => !game.Towns[i].Ruined && game.Towns[i].Holder != null);
        int f = game.Towns[held].Holder!.Value;
        Assert.Equal(War.Strength(game, f, supply, game.Towns[held].People, War.Held(game, f), game.Settings.SizeInBattle)
                     * War.DefenderAdvantage, Threats.Defence(game, held, supply));
        game.Towns[held].Holder = null;
        Assert.Equal(game.Towns[held].People * War.DefenderAdvantage, Threats.Defence(game, held, supply));
    }

    [Fact]
    public void A_town_with_no_one_left_is_not_raided_and_the_nearest_town_is_the_first_on_a_tie()
    {
        var game = TestGame();
        var living = Enumerable.Range(0, game.Towns.Count).Where(i => !game.Towns[i].Ruined).ToList();
        int a = living[0], b = living[1];
        foreach (int other in living.Skip(2))
            game.Towns[other].Ruined = true;
        (game.Towns[a].HX, game.Towns[a].HY) = (50, 40);
        (game.Towns[b].HX, game.Towns[b].HY) = (54, 40);
        Assert.Equal(a, Threats.Nearest(game, 52, 40));                      // as far from both: the first
        Assert.Equal(new[] { a, b }, Threats.InReach(game, 52, 40, 5));
        game.Towns[b].People = 0.4;                                         // no one left, not yet a ruin
        Assert.Equal(new[] { a }, Threats.InReach(game, 52, 40, 5));
    }

    /// <summary>A threat at the first town of the first living faction with a ruler, which has harmed it by `harm`
    /// times its people; that faction made an attunement one with metal. Returns the faction.</summary>
    static int Harmed(Game game, Threat threat, double harm)
    {
        int f = Enumerable.Range(0, game.Factions.Count).First(i => game.Factions[i].People > 0 && game.Factions[i].Ruler != null
            && game.Towns.Any(t => t.Holder == i && !t.Ruined));
        MakeAMage(game, f);
        var town = game.Towns.First(t => t.Holder == f && !t.Ruined);
        (threat.X, threat.Y) = (town.HX, town.HY);
        threat.Harm[f] = harm * game.Factions[f].People;
        if (!game.Threats.Contains(threat))
            game.Threats.Add(threat);
        return f;
    }

    static void MakeAMage(Game game, int f)
    {
        game.Factions[f].Path = "attunement";
        var mine = game.Mines.First(m => m.Owner == null || m.Owner == f);
        (mine.Owner, mine.Metal) = (f, 100.0);
    }

    static Game RitualGame(double desperation) => WithSettings(settings =>
    {
        settings["attunement"]!["RITUAL_DESPERATION"] = desperation;
        settings["attunement"]!["RITUAL_CHANCE"] = 1000.0;
    });

    [Fact]
    public void A_ritual_needs_the_share_of_its_people_and_its_dead_a_threat_took()
    {
        var game = RitualGame(0.5);
        var threat = new Threat { Name = "Test", Kind = "dragon" };
        Harmed(game, threat, 0.8);                                          // 0.8 / 1.8 of them: not enough
        Rituals.ThreatYear(game, () => 0.0, 301);
        Assert.Null(threat.Ended);
        Harmed(game, threat, 1.0);                                          // half of them: enough
        Rituals.ThreatYear(game, () => 0.0, 301);
        Assert.Equal("ritual", threat.End);
    }

    [Fact]
    public void A_threat_slain_by_one_ritual_takes_no_other_and_a_ruler_s_recklessness_decides()
    {
        var game = RitualGame(0.0);
        var threat = new Threat { Name = "Test", Kind = "dragon" };
        int f = Harmed(game, threat, 0.1);
        int g = Enumerable.Range(0, game.Factions.Count).First(i => i != f && game.Factions[i].People > 0
            && game.Towns.Any(t => t.Holder == i && !t.Ruined));
        MakeAMage(game, g);
        threat.Harm[g] = 10.0;
        Rituals.ThreatYear(game, () => 0.0, 301);
        Assert.Single(game.Events, e => e.What == "threat_ritual");
        Assert.Single(threat.Rituals);

        var careful = RitualGame(0.0);                                       // no ruler: no recklessness, no ritual
        var other = new Threat { Name = "Other", Kind = "dragon" };
        int h = Harmed(careful, other, 0.1);
        careful.Factions[h].Ruler = null;
        Rituals.ThreatYear(careful, () => 0.0, 301);
        Assert.Null(other.Ended);
    }

    [Fact]
    public void A_raid_spoils_the_town_s_next_harvest_by_its_kind_s_factor()
    {
        var plain = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 4);
        var spoiled = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 4);
        int town = Enumerable.Range(0, spoiled.Towns.Count).First(i => !spoiled.Towns[i].Ruined);
        int year = spoiled.Year + 1;
        spoiled.Spoiled[town] = (year, 0.5);
        var a = YearCore.Harvests(plain, year);
        var b = YearCore.Harvests(spoiled, year);
        Assert.Equal(a[town] * 0.5, b[town]);
        Assert.Equal(a.Where((_, i) => i != town), b.Where((_, i) => i != town));
        Assert.Equal(1.0, Threats.Factor(spoiled, town, year + 1));           // only the year after the raid
    }

    [Fact]
    public void Without_kinds_no_threat_stirs_and_no_dice_are_rolled()
    {
        var game = WithSettings(s => s["threats"]!["THREAT_KINDS"] = new JsonObject());
        var before = Threats.Write(game.Threats).ToJsonString();
        for (int i = 0; i < 10; i++)
            YearCore.RunYear(game);
        Assert.Equal(before, Threats.Write(game.Threats).ToJsonString());
        Assert.False(game.AllDice.ContainsKey("threats"));
        Assert.DoesNotContain(game.Events, e => e.Kind == "threat");
    }

    [Fact]
    public void The_export_s_threats_are_read_exactly()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 1);
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "world-7", "world.json")))!;
        Json.Same(data["continue"]!["threats"]!["threats"], Threats.Write(game.Threats), "threats");
        Assert.Equal(data["threats"]!.AsArray().Select(t => (string)t!["name"]!), game.Threats.Select(t => t.Name));
    }

    [Fact]
    public void Played_years_raise_threats_whose_raids_kill_where_the_god_of_death_sees_and_a_save_plays_on_the_same()
    {
        var game = WithSettings(s => s["threats"]!["RISE_CHANCE"] = 0.3, seed: 6);
        int risen = game.Threats.Count;
        bool raidDead = false;
        for (int i = 0; i < 40; i++)
        {
            int events = game.Events.Count;
            YearCore.RunYear(game);
            var raids = game.Events.Skip(events).Where(e => e.What is "threat_raids" or "threat_beaten_off" or "threat_slain");
            foreach (var raid in raids)
                raidDead |= int.Parse(raid.Facts["dead"]) > 0;
        }
        Assert.True(game.Threats.Count > risen, "threats rise in the game's years");
        Assert.True(raidDead, "a raid kills");
        string path = Path.Combine(Scratch.Root, "wr-threats-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Json.Same(Threats.Write(game.Threats), Threats.Write(loaded.Threats), "threats");
        Assert.Equal(game.Spoiled, loaded.Spoiled);
        for (int i = 0; i < 30; i++)
        {
            YearCore.RunYear(game);
            YearCore.RunYear(loaded);
        }
        Assert.Equal(game.Events.Select(e => e.Text), loaded.Events.Select(e => e.Text));
        Json.Same(Threats.Write(game.Threats), Threats.Write(loaded.Threats), "threats after");
    }
}
