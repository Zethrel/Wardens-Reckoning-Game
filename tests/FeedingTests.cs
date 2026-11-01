// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Feeding by tastes and drift (step 6b): against the generator's own functions, and by what must follow.</summary>
public class FeedingTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["feeding"]!.AsObject();
    static Game NewGame(long seed = 1) => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), seed);

    static void SameYields(JsonNode? want, IReadOnlyDictionary<string, double> have)
    {
        var expected = want!.AsObject().ToDictionary(p => p.Key, p => (double)p.Value!);
        Assert.Equal(expected.Keys.OrderBy(k => k), have.Keys.OrderBy(k => k));
        foreach (var (good, value) in expected)
            Assert.Equal(value, have[good], 9);
    }

    [Fact]
    public void Python_s_sum_keeps_what_each_addition_loses()
    {
        // 0.1 added ten times is 0.9999999999999999 one by one, and exactly 1.0 by Python's sum (3.12 and later).
        Assert.Equal(1.0, Py.Sum(Enumerable.Repeat(0.1, 10)));
        Assert.NotEqual(1.0, Enumerable.Repeat(0.1, 10).Aggregate(0.0, (a, b) => a + b));
        Assert.Equal(0.0, Py.Sum(new[] { 1e100, 1.0, -1e100 }) - 1.0);
        Assert.Equal(0.0, Py.Sum(Array.Empty<double>()));
        Assert.Equal((2, 4, 2), (Py.Round(2.5), Py.Round(3.5), Py.Round(1.5)));
    }

    [Fact]
    public void Each_town_farms_the_cells_the_generator_says()
    {
        var game = NewGame();
        var towns = Expected["towns"]!.AsArray();
        for (int i = 0; i < game.Towns.Count; i++)
            Assert.Equal(towns[i]!["cells"]!.AsArray().Select(c => (int)c!), game.Towns[i].Cells);
    }

    [Fact]
    public void Food_capacity_and_harvest_for_any_people_and_path_agree_with_the_generator()
    {
        var game = NewGame();
        var towns = Expected["towns"]!.AsArray();
        for (int i = 0; i < game.Towns.Count; i++)
            foreach (string which in new[] { "mine", "other" })
            {
                var row = towns[i]![which]!.AsArray();
                int? people = (int?)row[0]![0];
                string? path = (string?)row[0]![1];
                var (food, capacity) = Feeding.CapacityOf(game, game.Towns[i].Cells, people, path);
                Assert.Equal((double)row[1]!, food);     // to the last digit: added as Python's sum() adds
                Assert.Equal((double)row[2]!, capacity);
                SameYields(row[3], Feeding.YieldsOf(game, game.Towns[i].Cells, people, path));
            }
    }

    [Fact]
    public void A_town_taken_is_fed_by_its_new_people_from_the_next_year_and_not_before()
    {
        var game = NewGame();
        var towns = Expected["towns"]!.AsArray();
        // A town whose "other" people and path feed it differently from its own.
        int town = Enumerable.Range(0, game.Towns.Count).First(i => !game.Towns[i].Ruined && game.Towns[i].Holder != null
            && (double)towns[i]!["mine"]![2]! != (double)towns[i]!["other"]![2]!);
        var other = towns[town]!["other"]!.AsArray();
        int? folk = (int?)other[0]![0];
        string? path = (string?)other[0]![1];
        int taker = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].Folk == folk);
        game.Factions[taker].Path = path;
        double before = game.Towns[town].Capacity;
        Feeding.Refresh(game);
        Assert.Equal(before, game.Towns[town].Capacity);        // nothing changed hands yet
        game.Towns[town].Holder = taker;
        Feeding.Refresh(game);
        Assert.Equal((double)other[2]!, game.Towns[town].Capacity);
        Assert.Equal((double)other[1]!, game.Towns[town].Food, 9);
        SameYields(other[3], game.Towns[town].Yields);
        Assert.Equal((folk, path), game.Towns[town].FedAs!.Value);
        Assert.Equal((double)other[2]!, YearCore.Capacity(town, game));
    }

    [Fact]
    public void A_ruin_is_not_fed_and_a_town_no_one_holds_is_fed_as_no_one()
    {
        var game = NewGame();
        int ruin = Enumerable.Range(0, game.Towns.Count).FirstOrDefault(i => game.Towns[i].Ruined, -1);
        int town = Enumerable.Range(0, game.Towns.Count).First(i => !game.Towns[i].Ruined && game.Towns[i].Holder != null);
        if (ruin >= 0)
        {
            game.Towns[ruin].FedAs = (999, "nothing");
            Feeding.Refresh(game);
            Assert.Equal((999, "nothing"), game.Towns[ruin].FedAs!.Value);
        }
        game.Towns[town].Holder = null;
        Feeding.Refresh(game);
        Assert.Equal(((int?)null, (string?)null), game.Towns[town].FedAs!.Value);
        Assert.Equal(Feeding.CapacityOf(game, game.Towns[town].Cells, null, null).Capacity, game.Towns[town].Capacity);
    }

    [Fact]
    public void Shares_of_the_land_and_a_year_of_drift_agree_with_the_generator()
    {
        var game = NewGame();
        var shares = Drift.SharesOf(game);
        var want = Expected["shares"]!.AsArray();
        for (int f = 0; f < game.Factions.Count; f++)
            foreach (string kind in Drift.Kinds)
                Assert.Equal((double)want[f]![kind]!, shares[f][kind], 12);
        Drift.Year(game, game.Year + 1);
        var next = Expected["values_next_year"]!.AsArray();
        for (int f = 0; f < game.Factions.Count; f++)
            foreach (var (trait, value) in next[f]!.AsObject())
                Assert.Equal((double)value!, game.Factions[f].Values[trait], 12);
    }

    [Fact]
    public void Every_LAND_YEARS_the_land_is_looked_at_again()
    {
        var game = NewGame();
        game.DriftTargets.Clear();
        Drift.Year(game, 11);
        var targets = Expected["targets_looked_at"]!.AsObject();
        Assert.Equal(targets.Select(p => int.Parse(p.Key)).OrderBy(k => k), game.DriftTargets.Keys.OrderBy(k => k));
        foreach (var (f, traits) in targets)
            foreach (var (trait, value) in traits!.AsObject())
                Assert.Equal((double)value!, game.DriftTargets[int.Parse(f)][trait], 12);
        var values = Expected["values_looked_at"]!.AsArray();
        for (int f = 0; f < game.Factions.Count; f++)
            foreach (var (trait, value) in values[f]!.AsObject())
                Assert.Equal((double)value!, game.Factions[f].Values[trait], 12);

        // Between those years the targets stay as they were, even if the land changes hands.
        var kept = game.DriftTargets.ToDictionary(p => p.Key, p => new Dictionary<string, double>(p.Value));
        Array.Fill(game.Regions, (int)Expected["living"]![0]!);
        Drift.Year(game, 12);
        Assert.Equal(kept.Keys, game.DriftTargets.Keys);
        foreach (var (f, traits) in kept)
            Assert.Equal(traits, game.DriftTargets[f]);
        Drift.Year(game, 21);
        Assert.NotEqual(kept[(int)Expected["living"]![1]!], game.DriftTargets.GetValueOrDefault((int)Expected["living"]![1]!));
    }

    [Fact]
    public void With_no_tastes_a_town_keeps_its_food_whoever_holds_it()
    {
        string folder = Path.Combine(Scratch.Root, "wr-notastes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        var settings = data["continue"]!["settings"]!;
        settings["settlement"]!["PEOPLE_FOOD"] = new JsonObject();
        settings["trade"]!["PEOPLE_GOODS"] = new JsonObject();
        settings["factions"]!["PATH_LAND"] = new JsonObject();
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        var game = new Game(ExportedWorld.Read(folder), 1);
        Assert.False(game.Settings.FeedsItsOwn);
        foreach (var town in game.Towns)
            town.Holder = town.Ruined ? null : (town.Holder is int h ? (h + 1) % game.Factions.Count : 0);
        var before = game.Towns.Select(t => (t.Capacity, t.FedAs, string.Join(",", t.Yields))).ToList();
        Feeding.Refresh(game);
        Assert.Equal(before, game.Towns.Select(t => (t.Capacity, t.FedAs, string.Join(",", t.Yields))));
    }

    [Fact]
    public void The_land_s_influence_is_kept_within_0_and_1()
    {
        var traits = new Dictionary<string, double> { ["planning"] = 0.95, ["risk"] = 0.05, ["greed"] = 0.5 };
        var shares = new Dictionary<string, double> { ["coast"] = 1.0, ["highlands"] = 1.0 };
        var changed = Drift.Influenced(traits, shares, new[] { ("coast", "planning", 0.1), ("highlands", "risk", -0.2) });
        Assert.Equal((1.0, 0.0, 0.5), (changed["planning"], changed["risk"], changed["greed"]));
    }

    [Fact]
    public void A_year_feeds_towns_by_their_new_holders_and_drifts_the_factions_ways()
    {
        var game = NewGame(4);
        var towns = Expected["towns"]!.AsArray();
        int town = Enumerable.Range(0, game.Towns.Count).First(i => !game.Towns[i].Ruined && game.Towns[i].Holder != null
            && (double)towns[i]!["mine"]![2]! != (double)towns[i]!["other"]![2]!);
        var other = towns[town]!["other"]!.AsArray();
        int taker = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].Folk == (int?)other[0]![0]);
        game.Factions[taker].Path = (string?)other[0]![1];
        game.Towns[town].Holder = taker;
        // Only drift changes a faction's values (rulers pull its traits, never its values): they move in the year.
        var before = game.Factions.Select(f => Splits.Traits.Select(t => f.Values[t]).ToList()).ToList();
        YearCore.RunYear(game);
        Assert.Equal(((int?)other[0]![0], (string?)other[0]![1]), game.Towns[town].FedAs!.Value);
        Assert.Contains(Enumerable.Range(0, before.Count), f => !before[f].SequenceEqual(Splits.Traits.Select(t => game.Factions[f].Values[t])));
    }

    [Fact]
    public void A_save_carries_the_feeding_and_the_drift()
    {
        var game = NewGame(9);
        int town = Enumerable.Range(0, game.Towns.Count).First(i => !game.Towns[i].Ruined && game.Towns[i].Holder != null);
        int taker = Enumerable.Range(0, game.Factions.Count)
            .First(f => game.Factions[f].Folk != game.Factions[game.Towns[town].Holder!.Value].Folk && game.Factions[f].People > 0);
        game.Towns[town].Holder = taker;
        for (int i = 0; i < 12; i++)
            YearCore.RunYear(game);
        string path = Path.Combine(Scratch.Root, "wr-feed-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var (a, b) = (game.Towns[i], loaded.Towns[i]);
            Assert.Equal((a.Food, a.Capacity, a.FedAs), (b.Food, b.Capacity, b.FedAs));
            Assert.Equal(a.Yields.ToList(), b.Yields.ToList());
        }
        Assert.Equal(game.DriftTargets.Keys, loaded.DriftTargets.Keys);
        for (int i = 0; i < 10; i++)
        {
            YearCore.RunYear(game);
            YearCore.RunYear(loaded);
        }
        Assert.Equal(game.Towns.Select(t => t.People), loaded.Towns.Select(t => t.People));
        Assert.Equal(game.Factions.Select(f => string.Join(",", f.Values.Values)), loaded.Factions.Select(f => string.Join(",", f.Values.Values)));
    }
}
