// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Migration (step 6c): against the generator's own functions, a whole year of them included (with MIGRATION
/// at 1 the generator rolls no dice), and by what must follow.</summary>
public class MigrationTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["migration"]!.AsObject();
    static Game NewGame(long seed = 1) => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), seed);

    /// <summary>A game whose migration settings are changed (as the expected year was worked out with).</summary>
    static Game Moving(Action<JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Path.GetTempPath(), "wr-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        change(data["continue"]!["settings"]!["migration"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    /// <summary>A game whose settings (all of them, by module) are changed.</summary>
    static Game WithSettings(Action<JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Path.GetTempPath(), "wr-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        change(data["continue"]!["settings"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    /// <summary>A game where only `town` (hungry) sends migrants, every year it can.</summary>
    static Game OnlyFrom(int town, out Game game)
    {
        game = Moving(m => { m["MIGRATION"] = 1.0; m["CROWDED"] = 1000.0; m["HUNGRY_YEARS"] = 1; });
        foreach (var t in game.Towns)
            t.Hungry = 0;
        game.Towns[town].Hungry = 1;
        return game;
    }

    /// <summary>The ruins migrants from `town` could resettle: within REACH, welcome, not a rift.</summary>
    static List<int> RuinsWithinReach(Game game, int town)
    {
        var place = game.Towns[town];
        int holder = place.Holder!.Value;
        double reach = game.Settings.Reach * game.Grid.Columns / 256.0;
        var living = game.Towns.Where(t => !t.Ruined && t.Holder != null).Select(t => t.Holder!.Value).ToHashSet();
        return Enumerable.Range(0, game.Towns.Count).Where(i => game.Towns[i].Ruined
            && (game.Towns[i].Holder == null || game.Towns[i].Holder == holder || !living.Contains(game.Towns[i].Holder!.Value))
            && Migration.Hypot(game.Towns[i].HX - place.HX, game.Towns[i].HY - place.HY) <= reach
            && !Migration.IsRift(game, game.Towns[i].HX, game.Towns[i].HY)).ToList();
    }

    /// <summary>A town of `source`'s own faction within REACH of it, made a ruin; returns it.</summary>
    static int MakeRuinNear(Game game, int source)
    {
        double reach = game.Settings.Reach * game.Grid.Columns / 256.0;
        int ruin = Enumerable.Range(0, game.Towns.Count).First(i => i != source && !game.Towns[i].Ruined
            && game.Towns[i].Holder == game.Towns[source].Holder
            && Migration.Hypot(game.Towns[i].HX - game.Towns[source].HX, game.Towns[i].HY - game.Towns[source].HY) <= reach);
        game.Towns[ruin].Ruined = true;
        return ruin;
    }

    /// <summary>The first town the generator sent migrants from in its year, and the town they settled.</summary>
    static (int Source, int Target, bool Founded) FirstMove(string what)
    {
        var moved = Expected["year"]!["moved"]!.AsArray();
        var events = Expected["year"]!["events"]!.AsArray();
        int k = Enumerable.Range(0, events.Count).First(i => (string)events[i]![0]! == what);
        return ((int)moved[k]![0]!, (int)moved[k]![1]!, moved[k]![2] is JsonArray);
    }

    static Game EveryoneWhoCan(long seed = 1) => Moving(m => { m["MIGRATION"] = 1.0; m["CROWDED"] = 0.2; }, seed);

    /// <summary>The SHA-256 of a grid's exact 8-byte numbers, as the expected file keeps whole grids.</summary>
    static string Sha(IReadOnlyList<double> grid) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(Movements.Exact(grid)))).ToLowerInvariant();

    static List<(double, int, int)> Sites(JsonNode? node) =>
        node!.AsArray().Select(s => ((double)s![0]!, (int)s[1]!, (int)s[2]!)).ToList();

    [Fact]
    public void Free_sites_own_site_scores_and_nearness_agree_with_the_generator()
    {
        var game = NewGame();
        Assert.Equal((int)Expected["spacing"]!, Migration.DistanceCells(game, game.Settings.MinDistance));
        Assert.Equal(Sites(Expected["usual_sites"]), Migration.FreeSites(game));
        Assert.Equal((string)Expected["near_metal"]!,
                     Sha(Migration.Nearness(game, game.Mines.Select(m => (m.HX, m.HY)), Migration.MetalReach)));
        var own = Expected["own_scores"]!.AsArray();
        Assert.NotEmpty(own);
        foreach (var entry in own)
        {
            int people = (int)entry![0]!;
            string? path = (string?)entry[1];
            game.Moving.Tastes.Remove((people, path));          // worked out afresh, as the generator just did
            Assert.Equal((string)entry[2]!, Sha(Migration.ScoreGrid(game, people, path)));
        }
    }

    [Fact]
    public void A_new_town_s_land_and_its_route_home_agree_with_the_generator()
    {
        var game = NewGame();
        var site = Expected["claim"]!["site"]!.AsArray();
        int x = (int)site[1]!, y = (int)site[2]!;
        Assert.Equal(Expected["claim"]!["cells"]!.AsArray().Select(c => (int)c!),
                     Migration.Claim(game, x, y, Migration.DistanceCells(game, game.Settings.MinDistance)));
        int home = (int)Expected["route"]!["from"]!;
        Assert.Equal((double?)Expected["route"]!["cost"], Migration.RouteCost(game, (game.Towns[home].HX, game.Towns[home].HY), (x, y)));
    }

    [Fact]
    public void A_year_of_migration_agrees_with_the_generator_s()
    {
        var game = EveryoneWhoCan();
        int routes = game.Routes.Count;
        int year = game.Year + 1;
        Migration.Year(game, year);
        var want = Expected["year"]!.AsObject();
        Assert.Equal(want["events"]!.AsArray().Select(e => ((string)e![0]!, (string)e[1]!)),
                     game.Events.Select(e => (e.What, e.Text)));
        var towns = want["towns"]!.AsArray();
        Assert.Equal(towns.Count, game.Towns.Count);
        Assert.Equal(towns.Count, game.World.Towns.Count);
        for (int i = 0; i < towns.Count; i++)
        {
            var t = towns[i]!.AsObject();
            var town = game.Towns[i];
            Assert.Equal(((int)t["x"]!, (int)t["y"]!), (town.HX, town.HY));
            Assert.Equal((string)t["name"]!, game.World.Towns[i].Name);
            Assert.Equal((bool)t["ruined"]!, town.Ruined);
            Assert.Equal(((int?)t["culture"], (int?)t["origin"], (int?)t["founded_from"]), (town.Holder, town.Origin, town.FoundedFrom));
            if (i >= game.World.Data["settlements"]!.AsArray().Count)
            {
                Assert.Equal((double)t["food"]!, town.Food);
                Assert.Equal((double)t["capacity"]!, town.Capacity);
                Assert.Equal(year, game.World.Towns[i].From);
            }
            Assert.Equal((int)t["population"]!, town.Population);
            if (i < want["people"]!.AsArray().Count && !want["moved"]!.AsArray().Any(m => (int)m![1]! == i))
                Assert.Equal((double)want["people"]![i]!, town.People);    // to the last digit (a town left behind)
        }
        Assert.Equal(want["owner"]!.AsArray().Select(v => (int)v!), game.Grid.Owner);
        Assert.Equal(want["regions"]!.AsArray().Select(v => (int)v!), game.Regions);
        Assert.Equal(want["routes"]!.AsArray().Select(r => ((int)r![0]!, (int)r[1]!, (double)r[2]!)),
                     game.Routes.Skip(routes).Select(r => (r.A, r.B, r.Cost)));
        Assert.Equal(Sites(want["sites"]), game.Moving.Sites);
        foreach (var entry in want["own_sites"]!.AsArray())
            Assert.Equal(Sites(entry![2]), game.Moving.OwnSites[((int)entry[0]!, (string?)entry[1])]);
        Assert.Equal(want["mine_owners"]!.AsArray().Select(o => (int?)o), game.Mines.Select(m => m.Owner));
    }

    [Fact]
    public void A_new_town_is_part_of_the_world_from_its_first_year()
    {
        var game = EveryoneWhoCan();
        int before = game.Towns.Count;
        YearCore.RunYear(game);
        Assert.True(game.Towns.Count > before);
        int town = before;
        var made = game.Towns[town];
        Assert.Null(made.FedAs);                                  // fed by its people from the next year
        Assert.Equal(game.Year, game.World.Towns[town].From);
        Assert.Equal(made.Population, game.World.Town(town, game.Year)!.People);
        Assert.Equal(made.Holder, game.World.Town(town, game.Year)!.Holder);
        Assert.Contains(game.Routes, r => r.B == town && r.A == made.FoundedFrom);
        Assert.Contains(town, game.Journeys[made.FoundedFrom!.Value].Cost.Keys);   // the journeys know the new route
        Assert.All(made.Cells, c => Assert.Equal(town, game.Grid.Owner[c]));
        Assert.Equal(made.Cells.OrderBy(c => c), made.Cells);
        Assert.Equal(game.World.HistoryFactor, game.World.Size / game.Grid.Columns);
        Assert.InRange(made.X / game.World.HistoryFactor, made.HX, made.HX);
        YearCore.RunYear(game);
        Assert.NotNull(made.FedAs);
        Assert.Equal(Feeding.CapacityOf(game, made.Cells, made.FedAs!.Value.People, made.FedAs.Value.Path).Capacity, made.Capacity);
    }

    [Fact]
    public void No_one_settles_beyond_MOST_SETTLEMENTS_or_in_a_rift_or_on_another_s_land()
    {
        var game = Moving(m => { m["MIGRATION"] = 1.0; m["CROWDED"] = 0.2; m["MOST_SETTLEMENTS"] = 0; });
        int before = game.Towns.Count;
        Migration.Year(game, game.Year + 1);
        Assert.Equal(before, game.Towns.Count);                    // no new towns; ruins may still be resettled
        Assert.All(game.Events, e => Assert.Equal("ruin_resettled", e.What));

        game = EveryoneWhoCan();
        var all = Enumerable.Range(0, game.Grid.Columns * game.Grid.Rows);
        var rifts = new HashSet<(int, int)>();
        foreach (int c in all)
            rifts.Add((c % game.Grid.Columns / game.Moving.RiftSize, c / game.Grid.Columns / game.Moving.RiftSize));
        typeof(Movements).GetProperty(nameof(Movements.Rifts))!.SetValue(game.Moving, rifts);
        Migration.Year(game, game.Year + 1);
        Assert.Empty(game.Events);                                 // the whole land a rift: no one moves

        game = EveryoneWhoCan();
        int stranger = game.Factions.Count - 1;
        Array.Fill(game.Regions, stranger);                        // every region held by one faction, with towns
        game.Towns[0].Holder = stranger;
        game.Towns[0].Ruined = false;
        Migration.Year(game, game.Year + 1);
        Assert.All(game.Events.Where(e => e.What == "colony_founded"), e => Assert.Contains("(the " + game.Factions[stranger].Name + ")", e.Text));
    }

    [Fact]
    public void Crowded_or_hungry_towns_send_MIGRANTS_of_their_people_at_the_chance_of_MIGRATION()
    {
        var game = NewGame(3);
        Assert.Equal(0.1, game.Settings.MigrationChance);
        int crowded = 0;
        foreach (var town in game.Towns.Where(t => !t.Ruined && t.Holder != null))
        {
            town.People = Math.Max(town.People, game.Settings.Crowded * town.Capacity);
            crowded++;
        }
        int moved = 0;
        for (int i = 0; i < 20; i++)
        {
            var trial = NewGame(i + 10);
            foreach (var town in trial.Towns.Where(t => !t.Ruined && t.Holder != null && t.Capacity > 0))
                town.People = trial.Settings.Crowded * town.Capacity;
            Migration.Year(trial, trial.Year + 1);
            moved += trial.Events.Count;
        }
        Assert.InRange(moved / 20.0 / crowded, 0.03, 0.2);          // about a tenth of them a year

        game = Moving(m => { m["MIGRATION"] = 1.0; m["HUNGRY_YEARS"] = 2; m["CROWDED"] = 100.0; });
        int hungry = (int)Expected["year"]!["moved"]![0]![0]!;   // a town the generator sent migrants from
        game.Towns[hungry].Hungry = 2;
        double people = game.Towns[hungry].People;
        Migration.Year(game, game.Year + 1);
        var left = game.Events.Single();
        Assert.Contains("leave hungry " + game.World.Towns[hungry].Name, left.Text);
        Assert.Equal(people - Py.Round(people * game.Settings.Migrants.Low), game.Towns[hungry].People);
    }

    [Fact]
    public void Distances_are_rounded_as_Python_rounds_and_a_new_town_claims_no_lake()
    {
        var game = NewGame();
        Assert.Equal(128, game.Grid.Columns);
        Assert.Equal(8, Migration.DistanceCells(game, 15));        // 7.5 rounds to 8
        Assert.Equal(6, Migration.DistanceCells(game, 13));        // 6.5 rounds to the even 6
        Assert.Equal(1, Migration.DistanceCells(game, 0.1));
        var site = Expected["claim"]!["site"]!.AsArray();
        int x = (int)site[1]!, y = (int)site[2]!;
        var cells = Expected["claim"]!["cells"]!.AsArray().Select(c => (int)c!).ToList();
        game.Lake[cells[1]] = true;
        var claimed = Migration.Claim(game, x, y, Migration.DistanceCells(game, game.Settings.MinDistance));
        Assert.DoesNotContain(cells[1], claimed);
    }

    [Fact]
    public void A_faction_with_no_ruler_sends_no_one()
    {
        var game = EveryoneWhoCan();
        foreach (var f in game.Factions)
            f.Ruler = null;
        Migration.Year(game, game.Year + 1);
        Assert.Empty(game.Events);
    }

    [Fact]
    public void The_best_ruin_by_the_people_s_own_score_and_the_lower_number_on_a_tie()
    {
        var (source, target, _) = FirstMove("ruin_resettled");
        OnlyFrom(source, out var game);
        var faction = game.Factions[game.Towns[source].Holder!.Value];
        var grid = Migration.ScoreGrid(game, faction.Folk, faction.Path);
        var ruins = RuinsWithinReach(game, source);
        Assert.Contains(target, ruins);
        if (ruins.Count < 2)
        {
            int made = MakeRuinNear(game, source);
            grid[game.Towns[made].HY * game.Grid.Columns + game.Towns[made].HX] = 0.0;   // worse by the own score
            ruins.Add(made);
        }
        // The usual scores say another ruin is best: the people's own score decides.
        foreach (int r in ruins)
            game.Moving.TownScores[r] = r == target ? 0.0 : 1.0;
        Migration.Year(game, game.Year + 1);
        Assert.Equal("ruin_resettled", game.Events.Single().What);
        Assert.False(game.Towns[target].Ruined);                // the one the generator resettled, by the own score

        // A tie: every ruin as good as the best; the lower number wins.
        OnlyFrom(source, out game);
        ruins = RuinsWithinReach(game, source);
        if (ruins.Count < 2)
            ruins.Add(MakeRuinNear(game, source));
        grid = Migration.ScoreGrid(game, faction.Folk, faction.Path);
        foreach (int r in ruins)
            grid[game.Towns[r].HY * game.Grid.Columns + game.Towns[r].HX] = 1.0;
        game.Moving.Sites!.Clear();
        foreach (var key in game.Moving.OwnSites.Keys.ToList())
            game.Moving.OwnSites[key] = new();                  // no free land: only ruins
        Migration.Year(game, game.Year + 1);
        Assert.Equal("ruin_resettled", game.Events.Single().What);
        Assert.False(game.Towns[ruins.Min()].Ruined);
        Assert.All(ruins.Where(r => r != ruins.Min()), r => Assert.True(game.Towns[r].Ruined));
    }

    [Fact]
    public void Free_land_wins_over_a_ruin_only_when_strictly_better_and_the_settled_start_afresh()
    {
        var (source, target, _) = FirstMove("colony_founded");
        OnlyFrom(source, out var game);
        var faction = game.Factions[game.Towns[source].Holder!.Value];
        var grid = Migration.ScoreGrid(game, faction.Folk, faction.Path);
        var sites = game.Moving.OwnSites.TryGetValue((faction.Folk, faction.Path), out var listed) ? listed
                    : Migration.FreeSites(game, grid);
        double reach = game.Settings.Reach * game.Grid.Columns / 256.0;
        var first = sites.First(s => Migration.Hypot(s.X - game.Towns[source].HX, s.Y - game.Towns[source].HY) <= reach);
        // A town of its own faction within reach, made a ruin as good as the best free land.
        int ruin = Enumerable.Range(0, game.Towns.Count).First(i => i != source && !game.Towns[i].Ruined
            && game.Towns[i].Holder == game.Towns[source].Holder
            && Migration.Hypot(game.Towns[i].HX - game.Towns[source].HX, game.Towns[i].HY - game.Towns[source].HY) <= reach);
        game.Towns[ruin].Ruined = true;
        game.Towns[ruin].People = 0;                    // a ruin has no one left
        game.Towns[ruin].LowYears = 7;
        game.Towns[ruin].Hungry = 5;
        foreach (int other in RuinsWithinReach(game, source).Where(r => r != ruin))
            grid[game.Towns[other].HY * game.Grid.Columns + game.Towns[other].HX] = 0.0;
        grid[game.Towns[ruin].HY * game.Grid.Columns + game.Towns[ruin].HX] = first.Score;
        Migration.Year(game, game.Year + 1);
        Assert.Equal("ruin_resettled", game.Events.Single().What);
        Assert.False(game.Towns[ruin].Ruined);
        Assert.Equal((0, 0), (game.Towns[ruin].LowYears, game.Towns[ruin].Hungry));
        Assert.True(game.Towns[ruin].People > 0);       // its settlers, counted once the year's moves are done
        Assert.Equal(source, game.Towns[ruin].FoundedFrom);
    }

    [Fact]
    public void The_town_left_behind_keeps_its_size_within_the_margin()
    {
        var (source, _, _) = FirstMove("colony_founded");
        OnlyFrom(source, out var game);
        var town = game.Towns[source];
        var lows = game.Settings.SizeLows;
        int band = Enumerable.Range(1, lows.Count - 1).First(b => lows[b] > 0);
        // After a fifth leaves, it is just under its size's edge, but within the margin.
        double edge = lows[band];
        town.People = Math.Ceiling(edge * 0.99 / (1 - game.Settings.Migrants.Low));
        town.Size = band;
        town.Capacity = Math.Max(town.Capacity, town.People * 2);
        Migration.Year(game, game.Year + 1);
        Assert.Single(game.Events);
        Assert.True(town.Population < edge && town.Population >= edge * (1 - YearCore.SizeMargin));
        Assert.Equal(band, town.Size);
    }

    [Fact]
    public void A_new_town_s_capacity_is_its_food_rounded_as_Python_rounds()
    {
        var game = WithSettings(s =>
        {
            s["migration"]!["MIGRATION"] = 1.0;
            s["migration"]!["CROWDED"] = 0.2;
            s["settlement"]!["PEOPLE_PER_FOOD"] = 33.3;
        });
        int before = game.Towns.Count;
        Migration.Year(game, game.Year + 1);
        Assert.True(game.Towns.Count > before);
        for (int i = before; i < game.Towns.Count; i++)
            Assert.Equal(Py.Round(game.Towns[i].Food * 33.3), game.Towns[i].Capacity);
        Assert.Contains(Enumerable.Range(before, game.Towns.Count - before),
                        i => game.Towns[i].Food * 33.3 % 1 >= 0.5);   // one whose rounding goes up
    }

    [Fact]
    public void Saves_carry_the_new_towns_their_land_and_routes()
    {
        var game = EveryoneWhoCan(5);
        YearCore.RunYear(game);
        YearCore.RunYear(game);
        string path = Path.Combine(Path.GetTempPath(), "wr-move-save-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Assert.Equal(game.Towns.Count, loaded.Towns.Count);
        Assert.Equal(game.World.Towns.Select(t => (t.Name, t.From, t.X, t.Y)), loaded.World.Towns.Select(t => (t.Name, t.From, t.X, t.Y)));
        Assert.Equal(game.Grid.Owner, loaded.Grid.Owner);
        Assert.Equal(game.Routes.Select(r => (r.A, r.B, r.Cost)), loaded.Routes.Select(r => (r.A, r.B, r.Cost)));
        Assert.Equal(game.Moving.Sites, loaded.Moving.Sites);
        Assert.Equal(game.Moving.TownScores, loaded.Moving.TownScores);
        Assert.Equal(game.Moving.Tastes.Keys.OrderBy(k => k.People).ThenBy(k => k.Path), loaded.Moving.Tastes.Keys.OrderBy(k => k.People).ThenBy(k => k.Path));
        for (int i = 0; i < 5; i++)
        {
            YearCore.RunYear(game);
            YearCore.RunYear(loaded);
        }
        Assert.Equal(game.Events.Select(e => e.Year + e.Text), loaded.Events.Select(e => e.Year + e.Text));
        Assert.Equal(game.Towns.Select(t => t.People), loaded.Towns.Select(t => t.People));
    }
}
