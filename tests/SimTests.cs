// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>The year's core (src/Sim/YearCore.cs) against the generator's own functions, and the years it runs.</summary>
public class SimTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static ExportedWorld Fresh() => ExportedWorld.Read(Path.Combine(Fixtures, "world-7"));
    static readonly JsonArray OneYear = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["one_year"]!.AsArray();

    [Fact]
    public void The_settings_come_from_the_export()
    {
        var settings = GameSettings.Of(Fresh());
        Assert.Equal(new[] { "grain", "herbs", "spices" }, settings.Staples);
        Assert.Equal(10, settings.RuinYears);
        Assert.Equal(new[] { "hamlet", "settlement", "village", "town", "city" }, settings.SizeNames);
        Assert.True(settings.PathHarvest["handcraft"] > 1);
    }

    [Fact]
    public void One_year_s_core_agrees_with_the_generator_s_functions()
    {
        // fixtures/make_expected.py works out an ordinary year (weather 1.0) for every town with the generator's
        // path_harvests, ruled_harvests, fed_share, grow and keep_band; the C# must give the same.
        var game = new Game(Fresh(), 1);
        int hungry = 0;
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var want = OneYear[i];
            var town = game.Towns[i];
            if (want is null)
            {
                Assert.True(town.Ruined);
                continue;
            }
            var faction = town.Holder is int h ? game.Factions[h] : null;
            double harvest = YearCore.PathHarvest(1.0, faction?.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings);
            Assert.Equal((double)want["harvest"]!, harvest, 9);
            double fed = YearCore.FedShare(town, faction?.Path, harvest, game.Settings);
            Assert.Equal((double)want["fed"]!, fed, 9);
            if (fed < 1) hungry++;
            double people = YearCore.Grow(town.People, YearCore.Capacity(i, game), fed, game.Settings);
            Assert.Equal((double)want["people"]!, people, 6);
            int size = YearCore.KeepBand(Math.Round(people, MidpointRounding.ToEven), game.Settings.SizeLows, town.Size,
                                         game.Settings.SizeLows[town.Size] * YearCore.SizeMargin);
            Assert.Equal((int)want["size"]!, size);
        }
        Assert.True(hungry > 0, "the test world has hungry towns, so starving is checked too");
    }

    [Fact]
    public void A_year_adds_itself_to_the_world_s_records()
    {
        var world = Fresh();
        var game = new Game(world, 1);
        int last = world.Years;
        YearCore.RunYear(game);
        Assert.Equal(last + 1, game.Year);
        Assert.Equal(last + 1, world.Years);
        Assert.Equal(last, world.ExportYears);
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var state = world.Town(i, game.Year)!;
            Assert.Equal(game.Towns[i].Ruined, state.Ruined);
            Assert.Equal(game.Towns[i].Ruined ? 0 : game.Towns[i].Population, state.People);
            Assert.Equal(game.Towns[i].Holder, state.Holder);
        }
        for (int f = 0; f < game.Factions.Count; f++)
            Assert.Equal(game.Factions[f].People, world.PeopleOf(f, game.Year));
        // The export's own years are as they were.
        Assert.Equal(world.Town(0, last), Fresh().Town(0, last));
    }

    [Fact]
    public void The_same_seed_gives_the_same_years_and_another_seed_other_years()
    {
        string Run(long seed)
        {
            var game = new Game(Fresh(), seed);
            for (int i = 0; i < 30; i++)
                YearCore.RunYear(game);
            // The weather moves how much each town makes, and so what passes along the routes (with trade, most
            // towns are fed whatever the weather, so their people alone may not tell two seeds apart).
            return string.Join(",", game.Towns.Select(t => t.People.ToString("R"))) + "|" + game.Events.Count
                   + "|" + string.Join(",", game.Routes.Select(r => r.Volume.ToString("R")));
        }
        Assert.Equal(Run(5), Run(5));
        Assert.NotEqual(Run(5), Run(6));
    }

    [Fact]
    public void The_weather_is_the_generator_s_noise()
    {
        // north/noise.py gives these for the same points and seeds.
        Assert.Equal(0.3739265117734522, Noise.Value(3.7, 1.2, 9001 + 5 * 7919), 12);
        Assert.Equal(0.4035415755752183, Noise.Value(-2.5, 0.25, 42), 12);
        Assert.Equal(0.04071670421399176, Noise.Lattice(-3, 7, 123456789012), 12);
    }

    [Fact]
    public void A_town_too_small_for_too_long_falls_into_ruin()
    {
        var game = new Game(Fresh(), 1);
        int index = game.Towns.FindIndex(t => !t.Ruined);
        var town = game.Towns[index];
        town.People = game.Settings.RuinPopulation - 10;
        town.Yields.Clear();                       // it grows too slowly to climb back above RUIN_POPULATION in time
        for (int year = 1; year < game.Settings.RuinYears; year++)
        {
            YearCore.RunYear(game);
            Assert.False(town.Ruined, "year " + year);
        }
        YearCore.RunYear(game);
        Assert.True(town.Ruined);
        Assert.Equal(0, town.People);
        Assert.Contains(game.Events, e => e.What == "ruin" && e.Year == game.Year);
        Assert.True(game.World.Town(index, game.Year)!.Ruined);
        Assert.False(game.World.Town(index, game.Year - 1)!.Ruined);
    }

    [Fact]
    public void A_hungry_town_shrinks_and_counts_its_hungry_years()
    {
        var game = new Game(Fresh(), 1);
        var town = game.Towns.First(t => !t.Ruined && t.Population > 500);
        double before = town.People;
        foreach (var each in game.Towns)
            each.Yields.Clear();                   // no land grows anything, so no one has food to sell either
        YearCore.RunYear(game);
        Assert.Equal(0.0, town.Fed);
        Assert.Equal(1, town.Hungry);
        Assert.Equal(before * (1 - game.Settings.Starvation), town.People, 6);
    }

    [Fact]
    public void A_path_s_steadiness_pulls_a_poor_or_rich_harvest_towards_ordinary_then_its_harvest_scales_it()
    {
        var settings = GameSettings.Of(Fresh());
        double steady = settings.PathSteadiness["handcraft"], larger = settings.PathHarvest["handcraft"];
        Assert.NotEqual(1.0, steady);
        Assert.Equal((1.0 + 0.3 * steady) * larger, YearCore.PathHarvest(1.3, "handcraft", settings), 12);
        Assert.Equal((1.0 - 0.3 * steady) * larger, YearCore.PathHarvest(0.7, "handcraft", settings), 12);
        Assert.Equal(0.7, YearCore.PathHarvest(0.7, null, settings), 12);
    }

    [Fact]
    public void A_town_s_harvest_is_its_weather_its_path_and_its_ruler()
    {
        var game = new Game(Fresh(), 9);
        int i = game.Towns.FindIndex(t => !t.Ruined && t.Holder is int h && game.Factions[h].Competence != null);
        var faction = game.Factions[game.Towns[i].Holder!.Value];
        var record = game.World.Towns[i];
        double weather = YearCore.Weather(game, record.X, record.Y, 301);
        Assert.InRange(weather, 1 - game.Settings.HarvestVariation, 1 + game.Settings.HarvestVariation);
        Assert.NotEqual(1.0, YearCore.RulerFactor(faction, game.Settings));
        Assert.Equal(YearCore.PathHarvest(weather, faction.Path, game.Settings) * YearCore.RulerFactor(faction, game.Settings),
                     YearCore.Harvest(game, i, 301), 12);
        // Nearby towns share the weather; another year has other weather.
        Assert.Equal(weather, YearCore.Weather(game, record.X + 1, record.Y, 301), 1);
        Assert.NotEqual(weather, YearCore.Weather(game, record.X, record.Y, 302));
    }

    [Fact]
    public void Hungry_years_add_up_and_a_fed_year_ends_them()
    {
        var game = new Game(Fresh(), 1);
        var town = game.Towns.First(t => !t.Ruined && t.Population > 500);
        var yields = new Dictionary<string, double>(town.Yields);
        foreach (var each in game.Towns)
            each.Yields.Clear();                   // no land grows anything, so no one has food to sell either
        YearCore.RunYear(game);
        YearCore.RunYear(game);
        Assert.Equal(2, town.Hungry);
        town.Yields = yields.ToDictionary(p => p.Key, p => p.Value * 1000);     // plenty
        YearCore.RunYear(game);
        Assert.Equal(0, town.Hungry);
        Assert.Equal(1.0, town.Fed);
    }

    [Fact]
    public void Sizes_move_down_only_past_the_margin()
    {
        var lows = new[] { 0, 1000, 2000 };
        Assert.Equal(1, YearCore.KeepBand(990, lows, 1, 50));   // just under the edge: still a settlement
        Assert.Equal(0, YearCore.KeepBand(940, lows, 1, 50));
        Assert.Equal(2, YearCore.KeepBand(2000, lows, 1, 50));  // up at once
        Assert.Equal("an ash", YearCore.Article("ash"));
        Assert.Equal("a city", YearCore.Article("city"));
    }

    [Fact]
    public void A_world_without_continue_cannot_be_played_on()
    {
        string folder = Path.Combine(Path.GetTempPath(), "wr-nocont-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "world-7", "world.json")))!.AsObject();
        data.Remove("continue");
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        Assert.Throws<WorldFileException>(() => new Game(ExportedWorld.Read(folder), 1));
        data["continue"] = new JsonObject { ["version"] = GameSettings.SupportedContinue + 1 };
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        Assert.Throws<WorldFileException>(() => new Game(ExportedWorld.Read(folder), 1));
    }
}

/// <summary>Saving and loading.</summary>
public class SaveTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static string Temp(string name) => Path.Combine(Path.GetTempPath(), "wr-save-" + Guid.NewGuid().ToString("N"), name);

    static string Snapshot(Game game) =>
        string.Join(";", game.Towns.Select(t => $"{t.People:R},{t.Size},{t.LowYears},{t.Hungry},{t.Ruined},{t.Holder},{t.Fed:R}"))
        + "|" + string.Join(",", game.Factions.Select(f => $"{f.People},{f.MetalSupply:R},{f.MetalNeed:R}"))
        + "|" + string.Join(",", game.Mines.Select(m => m.Metal.ToString("R")))
        + "|" + string.Join(",", game.Memory.OrderBy(p => p.Key).Select(p => p.Value.ToString("R")))
        + "|" + string.Join("/", game.Events.Select(e => e.Year + e.Text))
        + "|" + string.Join(",", Enumerable.Range(0, game.World.Towns.Count)
                                           .Select(i => string.Join(".", game.World.Towns[i].People)));

    [Fact]
    public void Saving_then_loading_plays_on_as_if_never_stopped()
    {
        var straight = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 11);
        for (int i = 0; i < 40; i++)
            YearCore.RunYear(straight);

        var first = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 11);
        for (int i = 0; i < 25; i++)
            YearCore.RunYear(first);
        string path = Temp("game.json");
        SaveFile.Write(path, first);
        var loaded = SaveFile.Read(path);
        Assert.Equal(Snapshot(first), Snapshot(loaded));
        for (int i = 0; i < 15; i++)
            YearCore.RunYear(loaded);
        Assert.Equal(Snapshot(straight), Snapshot(loaded));
        Assert.Equal(straight.World.Years, loaded.World.Years);
    }

    [Fact]
    public void A_save_of_another_world_or_a_newer_save_is_refused()
    {
        var game = new Game(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), 3);
        YearCore.RunYear(game);
        string path = Temp("game.json");
        SaveFile.Write(path, game);
        var save = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        save["version"] = SaveFile.Version + 1;
        File.WriteAllText(path, save.ToJsonString());
        Assert.Throws<WorldFileException>(() => SaveFile.Read(path));

        save["version"] = SaveFile.Version - 1;
        File.WriteAllText(path, save.ToJsonString());
        Assert.Throws<WorldFileException>(() => SaveFile.Read(path));

        save["version"] = SaveFile.Version;
        save["world_seed"] = 99;
        File.WriteAllText(path, save.ToJsonString());
        Assert.Throws<WorldFileException>(() => SaveFile.Read(path));

        save["world_seed"] = game.World.Seed;
        save["world"] = Path.Combine(Path.GetTempPath(), "no-such-folder", "world.json");
        File.WriteAllText(path, save.ToJsonString());
        Assert.Throws<WorldFileException>(() => SaveFile.Read(path));

        File.WriteAllText(path, "not a save");
        Assert.Throws<WorldFileException>(() => SaveFile.Read(path));
    }
}
