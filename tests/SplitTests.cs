// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Splits and merging (step 6a): against the generator's own functions where no dice are rolled, and by
/// what must follow where they are.</summary>
public class SplitTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["splits"]!.AsObject();
    static Game NewGame(long seed = 1) => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), seed);

    /// <summary>A game on a copy of the fixture whose split and war settings are changed by `change` (for rules whose
    /// dice would otherwise decide). The war tests use it too.</summary>
    internal static Game WithSettings(Action<JsonObject, JsonObject> change, long seed = 1)
    {
        string folder = Path.Combine(Path.GetTempPath(), "wr-splits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        var settings = data["continue"]!["settings"]!;
        change(settings["splits"]!.AsObject(), settings["war"]!.AsObject());
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return new Game(ExportedWorld.Read(folder), seed);
    }

    static (int, int, int) Rgb(Game game, int f) =>
        (game.World.Factions[f].Red, game.World.Factions[f].Green, game.World.Factions[f].Blue);

    static List<YearCore.Hardship> NoHardship(Game game) => game.Factions.Select(_ => new YearCore.Hardship()).ToList();

    [Fact]
    public void The_split_constants_are_the_generator_s()
    {
        var c = Expected["constants"]!;
        Assert.Equal((double)c["BITTER"]!, Splits.Bitter);
        Assert.Equal((double)c["WARM"]!, Splits.Warm);
        Assert.Equal((int)c["SMALL"]!, Splits.Small);
        Assert.Equal((double)c["MERGE_CHANCE"]!, Splits.MergeChance);
        Assert.Equal((double)c["FRAGILITY_RANGE"]![0]!, Splits.FragilityLow);
        Assert.Equal((double)c["FRAGILITY_RANGE"]![1]!, Splits.FragilityHigh);
        Assert.Equal(c["SHADES"]!.AsArray().Select(v => (double)v!), Colours.Shades);
        Assert.Equal(c["SPLIT_KINDS"]!.AsObject().Select(p => (p.Key, (string)p.Value!)),
                     Splits.SplitKinds.Select(p => (p.Key, p.Value)));
        Assert.Equal(c["TRAITS"]!.AsArray().Select(v => (string)v!), Splits.Traits);
        Assert.Equal(c["EXTRA_TRAITS"]!.AsArray().Select(v => (string)v!), Splits.ExtraTraits);
        Assert.Equal(c["BELIEFS"]!.AsArray().Select(v => (string)v!), Splits.Beliefs);
    }

    [Fact]
    public void Capitals_fragility_division_holds_and_the_living_agree_with_the_generator()
    {
        var game = NewGame();
        var capitals = Expected["capitals"]!.AsArray();
        var fragility = Expected["fragility"]!.AsArray();
        var divided = Expected["divided"]!.AsArray();
        for (int f = 0; f < game.Factions.Count; f++)
        {
            Assert.Equal((int?)capitals[f], Splits.CapitalOf(game, f));
            Assert.Equal((double)fragility[f]!, Splits.Fragility(game.Factions[f]), 12);
            Assert.Equal((double)divided[f]!, Splits.Divided(game, game.Factions[f]), 9);
        }
        Splits.UpdateEmpires(game);
        Assert.Equal(Expected["holds"]!.AsArray().Select(v => (double)v!), game.Factions.Select(f => f.Hold));
        Assert.Contains(game.Factions, f => f.Hold < 1.0);
        Assert.Equal((int)Expected["living"]!, Splits.Living(game));
    }

    [Fact]
    public void The_journeys_the_outer_towns_and_the_provinces_agree_with_the_generator()
    {
        var game = NewGame();
        double scale = 256.0 / game.HistoryColumns;
        var provinces = Expected["provinces"]!.AsArray();
        Assert.NotEmpty(provinces);
        foreach (var node in provinces)
        {
            int f = (int)node!["faction"]!;
            int capital = Splits.CapitalOf(game, f)!.Value;
            var distances = game.Journeys[capital].Cost.ToDictionary(p => p.Key, p => p.Value * scale);
            foreach (var (town, value) in node["distances"]!.AsObject())
                Assert.Equal((double)value!, distances.GetValueOrDefault(int.Parse(town)), 6);
            var outer = node["outer"]!.AsArray().Select(t => (int)t!).ToList();
            Assert.Equal(outer, Splits.Holdings(game, f).Where(t => t != capital)
                                      .OrderByDescending(t => distances.GetValueOrDefault(t)).ThenBy(t => t));
            Assert.Equal(node["farthest"]!.AsArray().Select(t => (int)t!), Splits.Province(game, outer[0], f, distances));
            Assert.Equal(node["nearest"]!.AsArray().Select(t => (int)t!), Splits.Province(game, outer[^1], f, distances));
        }
    }

    [Fact]
    public void A_new_faction_s_shade_is_the_generator_s()
    {
        var game = NewGame();
        var living = Enumerable.Range(0, game.Factions.Count).Where(f => Splits.Holdings(game, f).Count > 0).ToList();
        var others = living.Select(f => ((int)game.World.Factions[f].Red, (int)game.World.Factions[f].Green,
                                         (int)game.World.Factions[f].Blue)).ToList();
        foreach (var node in Expected["kin_colours"]!.AsArray())
        {
            int f = (int)node![0]!;
            var want = node[1]!.AsArray().Select(v => (int)v!).ToArray();
            var parent = f switch
            {
                -1 => (22, 18, 28),
                -2 => (200, 40, 40),
                _ => ((int)game.World.Factions[f].Red, (int)game.World.Factions[f].Green, (int)game.World.Factions[f].Blue),
            };
            var (r, g, b) = Colours.KinColour(parent, f == -2 ? Array.Empty<(int, int, int)>() : others);
            Assert.Equal(want, new[] { r, g, b });
        }
    }

    // The faction with the most towns, and a part of it: its farthest province.
    static (int Faction, List<int> Part) FarSide(Game game)
    {
        int f = Enumerable.Range(0, game.Factions.Count).OrderByDescending(i => Splits.Holdings(game, i).Count).First();
        int capital = Splits.CapitalOf(game, f)!.Value;
        var distances = game.Journeys[capital].Cost;
        int far = Splits.Holdings(game, f).Where(t => t != capital).OrderByDescending(t => distances.GetValueOrDefault(t)).First();
        var part = Splits.Province(game, far, f, distances);
        foreach (int t in part)
            game.Towns[t].Origin = f;               // its own people's towns (a conquered one breaks away as another's)
        return (f, part);
    }

    [Fact]
    public void A_province_that_breaks_away_takes_its_towns_land_and_mines_and_starts_bitter()
    {
        var game = NewGame();
        game.Waiting.Clear();                       // no faction of FACTIONS waits: a shade of the parent's own
        var (parent, part) = FarSide(game);
        int other = Enumerable.Range(0, game.Factions.Count).First(f => f != parent && game.Factions[f].People > 0);
        game.Memory[(parent, other)] = 0.25;
        game.Factions[parent].Opinions[other] = 0.33;
        int parentPeople = game.Factions[parent].People, partPeople = part.Sum(t => game.Towns[t].Population);
        var shaded = Colours.KinColour(Rgb(game, parent), Enumerable.Range(0, game.Factions.Count)
            .Where(f => Splits.Holdings(game, f).Count > 0).Select(f => Rgb(game, f)));
        int year = game.Year + 1;
        var hardship = NoHardship(game);
        int child = Splits.Secede(game, part, parent, "divided from their rulers", hardship, year);

        Assert.Equal(game.Factions.Count - 1, child);
        Assert.Equal(game.Factions.Count, game.World.Factions.Count);
        Assert.Equal(game.Factions.Count, hardship.Count);
        var mother = game.Factions[parent];
        var made = game.Factions[child];
        Assert.All(part, t => Assert.Equal(child, game.Towns[t].Holder));
        Assert.All(part, t => Assert.Equal(child, game.Towns[t].Origin));
        Assert.All(game.Mines.Where(m => m.LandTown is int t && part.Contains(t)), m => Assert.Equal(child, m.Owner));
        Assert.All(Enumerable.Range(0, game.Grid.Owner.Length).Where(c => part.Contains(game.Grid.Owner[c])),
                   c => Assert.Equal(child, game.Regions[c]));
        Assert.Equal(partPeople, made.People);
        Assert.Equal(Math.Max(0, parentPeople - partPeople), mother.People);
        Assert.Equal((mother.Path, mother.Government, mother.Belief, mother.Folk, mother.Lineage),
                     (made.Path, made.Government, made.Belief, made.Folk, made.Lineage));
        Assert.Equal(mother.Values, made.Values);
        Assert.Equal(mother.Nature, made.Nature);
        Assert.Equal((part[0], (int?)parent, year), (made.Capital!.Value, made.Parent, made.Founded));
        Assert.NotNull(made.Ruler);
        Assert.Equal(1, made.RulerCount);
        Assert.Equal(game.PeopleNames[made.Folk][0], made.Name);   // the first of its people's names for factions
        Assert.Equal(1, game.PeopleNamesUsed[made.Folk]);
        // Memories and opinions as the parent's, and bitter between the two.
        Assert.Equal(0.25, game.Memory[(child, other)]);
        Assert.Equal(0.33, made.Opinions[other]);
        Assert.Equal(Splits.Bitter, game.Memory[(child, parent)]);
        Assert.Equal(Splits.Bitter, game.Memory[(parent, child)]);
        Assert.Equal(Splits.Bitter, mother.Opinions[child]);
        Assert.Equal(Splits.Bitter, made.Opinions[parent]);
        // In the world's records from this year, in a shade of the parent's colour.
        var info = game.World.Factions[child];
        Assert.Equal((shaded, (int?)parent, year), (Rgb(game, child), info.Parent, info.Founded));
        Assert.Equal(year, game.World.FromOf(child));
        var told = game.Events[^1];
        Assert.Equal(("split", "split_division"), (told.Kind, told.What));
        Assert.StartsWith(game.World.Towns[part[0]].Name, told.Text);
        Assert.Contains("break away from the " + mother.Name + ", divided from their rulers: the " + made.Name, told.Text);
        Assert.True(game.BordersChanged);
    }

    [Fact]
    public void A_colony_far_away_leaves_in_peace_and_a_waiting_faction_of_the_people_rises()
    {
        var game = NewGame();
        var (parent, part) = FarSide(game);
        int folk = game.Factions[parent].Folk;
        var waiting = game.Waiting.First(w => w.Folk == folk);
        var start = game.Settings.Starts[waiting.Key];
        int child = Splits.Secede(game, part, parent, "too far from the capital, in peace", NoHardship(game), game.Year + 1,
                                  peaceful: true);
        var made = game.Factions[child];
        Assert.DoesNotContain(waiting, game.Waiting);
        Assert.Equal((Splits.WithoutArticle(start.Name), start.Path, waiting.Key), (made.Name, made.Path, made.Lineage));
        Assert.Equal(Splits.Traits.Concat(Splits.ExtraTraits).Where(start.Traits.ContainsKey).ToDictionary(t => t, t => start.Traits[t]),
                     made.Traits);
        Assert.Equal((start.Red, start.Green, start.Blue, start.Race),
                     (game.World.Factions[child].Red, game.World.Factions[child].Green, game.World.Factions[child].Blue,
                      game.World.Factions[child].Race));
        Assert.Equal(Splits.Warm, game.Memory[(child, parent)]);
        Assert.Equal(Splits.Warm, made.Opinions[parent]);
        Assert.Equal("split_in_peace", game.Events[^1].What);
    }

    [Fact]
    public void A_town_taken_from_another_people_breaks_away_as_theirs()
    {
        var game = NewGame();
        game.Waiting.Clear();
        int town = Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined && game.Towns[t].Origin is int o
            && o != game.Towns[t].Holder && game.Factions[o].Folk != game.Factions[game.Towns[t].Holder!.Value].Folk);
        int holder = game.Towns[town].Holder!.Value, origin = game.Towns[town].Origin!.Value;
        int child = Splits.Secede(game, new List<int> { town }, holder, "divided from their rulers", NoHardship(game), game.Year + 1);
        var made = game.Factions[child];
        Assert.Equal((game.Factions[origin].Folk, game.Factions[origin].Path, game.Factions[origin].Government),
                     (made.Folk, made.Path, made.Government));
        Assert.Equal(game.Factions[origin].Values, made.Values);
        Assert.Equal(holder, made.Parent);
        Assert.Equal(game.World.Factions[origin].Race, game.World.Factions[child].Race);
    }

    [Fact]
    public void A_strong_ruler_s_death_splits_a_fragile_realm_behind_a_rival()
    {
        var game = NewGame();
        game.Waiting.Clear();
        var (parent, _) = FarSide(game);
        var faction = game.Factions[parent];
        faction.Traits["risk"] = 1.0;
        faction.Traits["planning"] = 0.0;           // fragility 1.75: LEADER_SPLIT 0.6 x 1.75 is a certainty
        faction.Hold = 1.0;
        faction.SplitPending = true;
        int capital = Splits.CapitalOf(game, parent)!.Value;
        var distances = game.Journeys[capital].Cost.ToDictionary(p => p.Key, p => p.Value * 256.0 / game.HistoryColumns);
        int farthest = Splits.Holdings(game, parent).Where(t => t != capital)
            .OrderByDescending(t => distances.GetValueOrDefault(t)).ThenBy(t => t).First();
        var part = Splits.Province(game, farthest, parent, distances);
        int before = game.Factions.Count;
        Splits.Year(game, NoHardship(game), game.Year + 1);
        Assert.False(faction.SplitPending);
        var split = game.Events.First(e => e.What == "split_leader");
        Assert.Equal(new[] { parent, before }, split.Factions);
        Assert.All(part, t => Assert.Equal(before, game.Towns[t].Holder));
        Assert.Equal(capital, Splits.CapitalOf(game, parent));     // the capital always stays
    }

    [Fact]
    public void A_cautious_or_whole_realm_does_not_split_and_none_split_past_MAX_FACTIONS()
    {
        // Division made a certainty, but at most as many factions with land as there are now.
        int living = Splits.Living(NewGame());
        var game = WithSettings((splits, _) => { splits["DIVISION"] = 1000.0; splits["MAX_FACTIONS"] = living; });
        Assert.Equal(living, Splits.Living(game));
        int before = game.Factions.Count;
        Splits.Year(game, NoHardship(game), game.Year + 1);
        Assert.Equal(before, game.Factions.Count);

        game = WithSettings((splits, _) => splits["DIVISION"] = 1000.0);
        foreach (var f in game.Factions)
            f.Hold = 0.0;                           // an empire's hold of nothing: never breaks apart
        Splits.Year(game, NoHardship(game), game.Year + 1);
        Assert.Equal(before, game.Factions.Count);

        game = WithSettings((splits, _) => splits["DIVISION"] = 1000.0);
        Splits.Year(game, NoHardship(game), game.Year + 1);
        Assert.True(game.Factions.Count > before);
        Assert.Contains(game.Events, e => e.What == "split_division");
        // Every faction of two towns or more with a ruler and some division split, each into one more.
        int divided = Enumerable.Range(0, before).Count(f => game.Events.Any(e => e.What == "split_division" && e.Factions[0] == f));
        Assert.Equal(game.Factions.Count - before, divided);
    }

    [Fact]
    public void Far_towns_drift_away_the_nearest_far_one_first()
    {
        var game = WithSettings((splits, _) => { splits["DISTANCE"] = 1000.0; splits["DIVISION"] = 0.0; splits["FAR"] = 100.0; });
        game.Waiting.Clear();
        int f = (int)Expected["provinces"]![0]!["faction"]!;
        var distances = Expected["provinces"]![0]!["distances"]!.AsObject().ToDictionary(p => int.Parse(p.Key), p => (double)p.Value!);
        var outer = Expected["provinces"]![0]!["outer"]!.AsArray().Select(t => (int)t!).ToList();
        int nearestFar = outer.Last(t => distances[t] >= 100.0);
        bool colony = game.Towns[nearestFar].FoundedFrom != null;
        int before = game.Factions.Count;
        Splits.Year(game, NoHardship(game), game.Year + 1);
        var split = game.Events.First(e => e.Factions[0] == f && e.Kind == "split");
        Assert.Equal(colony ? "split_in_peace" : "split_distance", split.What);
        Assert.StartsWith(game.World.Towns[Splits.Province(NewGame(), nearestFar, f,
            NewGame().Journeys[Splits.CapitalOf(NewGame(), f)!.Value].Cost.ToDictionary(p => p.Key, p => p.Value * 256.0 / game.HistoryColumns))[0]].Name,
            split.Text);
        Assert.True(game.Factions.Count > before);
    }

    [Fact]
    public void Small_friendly_neighbours_on_one_path_merge_into_the_larger()
    {
        var game = NewGame();
        game.Wars.Clear();
        // Two neighbours made small: each keeps only its town on a route between them; a third takes the rest.
        var front = Enumerable.Range(0, game.Factions.Count).SelectMany(a => Enumerable.Range(a + 1, game.Factions.Count - a - 1)
            .Select(b => (a, b, War.Fronts(game, a, b)))).First(p => p.Item3.Count > 0);
        var (x, y) = (front.a, front.b);
        var (keepX, keepY) = front.Item3[0];
        int third = Enumerable.Range(0, game.Factions.Count).First(f => f != x && f != y && Splits.Holdings(game, f).Count > 0);
        foreach (int t in Splits.Holdings(game, x).Concat(Splits.Holdings(game, y)).Where(t => t != keepX && t != keepY))
            game.Towns[t].Holder = third;
        game.Factions[x].People = game.Towns[keepX].Population;
        game.Factions[y].People = game.Towns[keepY].Population;
        game.Factions[y].Path = game.Factions[x].Path;   // on one path
        game.Factions[x].Opinions[y] = 0.9;
        game.Factions[y].Opinions[x] = 0.9;
        game.Alliances.Add((x, y));
        var (larger, smaller) = game.Factions[x].People >= game.Factions[y].People ? (x, y) : (y, x);
        var towns = Splits.Holdings(game, smaller);
        foreach (int t in towns)
            (game.Towns[t].Origin, game.Towns[t].ConqueredIn) = (smaller, game.Year);   // its own, lately taken
        int people = game.Factions[larger].People + game.Factions[smaller].People;
        for (int i = 0; i < 100 && Splits.Holdings(game, smaller).Count > 0; i++)
            Splits.Merge(game, game.Year + 1);
        Assert.All(towns, t => Assert.Equal(larger, game.Towns[t].Holder));
        Assert.All(towns, t => Assert.Equal(larger, game.Towns[t].Origin));
        Assert.All(towns, t => Assert.Null(game.Towns[t].ConqueredIn));
        Assert.Equal((people, 0), (game.Factions[larger].People, game.Factions[smaller].People));
        Assert.Null(game.Factions[smaller].Ruler);
        Assert.DoesNotContain((x, y), game.Alliances);
        Assert.Equal("the " + game.Factions[smaller].Name + " join the " + game.Factions[larger].Name, game.Events[^1].Text);
    }

    // Two neighbours made small (each keeping only its town on a route between them), on one path and friendly.
    static (Game Game, int X, int Y, int Third) SmallFriends()
    {
        var game = NewGame();
        game.Wars.Clear();
        var front = Enumerable.Range(0, game.Factions.Count).SelectMany(a => Enumerable.Range(a + 1, game.Factions.Count - a - 1)
            .Select(b => (a, b, War.Fronts(game, a, b)))).First(p => p.Item3.Count > 0);
        var (x, y) = (front.a, front.b);
        var (keepX, keepY) = front.Item3[0];
        int third = Enumerable.Range(0, game.Factions.Count).First(f => f != x && f != y && Splits.Holdings(game, f).Count > 0);
        foreach (int t in Splits.Holdings(game, x).Concat(Splits.Holdings(game, y)).Where(t => t != keepX && t != keepY))
            game.Towns[t].Holder = third;
        game.Factions[y].Path = game.Factions[x].Path;
        game.Factions[x].Opinions[y] = game.Factions[y].Opinions[x] = 0.9;
        return (game, x, y, third);
    }

    [Fact]
    public void Neighbours_on_other_paths_or_too_big_do_not_merge()
    {
        var (game, x, y, third) = SmallFriends();
        game.Factions[y].Path = game.Factions[x].Path + " and more";
        for (int i = 0; i < 100; i++)
            Splits.Merge(game, game.Year + 1);
        Assert.DoesNotContain(game.Events, e => e.What == "merge");

        (game, x, y, third) = SmallFriends();
        // x gets Small + 1 towns: too big to merge.
        foreach (int t in Splits.Holdings(game, third).Take(Splits.Small))
            game.Towns[t].Holder = x;
        Assert.Equal(Splits.Small + 1, Splits.Holdings(game, x).Count);
        for (int i = 0; i < 100; i++)
            Splits.Merge(game, game.Year + 1);
        Assert.DoesNotContain(game.Events, e => e.What == "merge");
    }

    [Fact]
    public void A_capital_lost_is_the_largest_town_left()
    {
        var game = NewGame();
        int f = Enumerable.Range(0, game.Factions.Count).First(i => Splits.Holdings(game, i).Count >= 3);
        int capital = Splits.CapitalOf(game, f)!.Value;
        game.Towns[capital].Holder = null;
        var held = Splits.Holdings(game, f);
        int largest = held.Max(t => game.Towns[t].Population);
        Assert.Equal(held.First(t => game.Towns[t].Population == largest), Splits.CapitalOf(game, f));
        Assert.Equal(game.Factions[f].Capital, Splits.CapitalOf(game, f));
        game.Towns[held[^1]].People = largest + 10;          // a larger one now, but the capital stays where it is
        Assert.NotEqual(held[^1], Splits.CapitalOf(game, f));
    }

    [Fact]
    public void A_province_goes_only_further_out()
    {
        var game = NewGame();
        int f = Enumerable.Range(0, game.Factions.Count).First(i => Splits.Holdings(game, i).Count >= 3);
        var held = Splits.Holdings(game, f);
        var level = held.ToDictionary(t => t, _ => 5.0);       // all as far: none further out than another
        Assert.All(held, t => Assert.Equal(new[] { t }, Splits.Province(game, t, f, level)));
    }

    [Fact]
    public void Fragility_is_kept_within_its_range_and_division_is_the_greater_of_its_two_causes()
    {
        var faction = new FactionNow { Traits = new() { ["risk"] = 1.0, ["planning"] = 0.0 } };
        Assert.Equal(Splits.FragilityHigh, Splits.Fragility(faction));
        faction.Traits = new() { ["risk"] = 0.0, ["planning"] = 1.0 };
        Assert.Equal(Splits.FragilityLow, Splits.Fragility(faction));
        faction.Traits = new() { ["risk"] = 0.0, ["planning"] = 1.0, ["cohesion"] = 0.0 };
        Assert.Equal(Splits.FragilityHigh, Splits.Fragility(faction));
        faction.Traits["cohesion"] = 0.75;
        Assert.Equal(0.5, Splits.Fragility(faction), 12);

        var game = NewGame();
        var ruled = game.Factions.First(f => f.Ruler != null);
        ruled.Grievance = 0.0;                                  // not aggrieved: only how far its ruler is from its ways
        Assert.Equal(Rulers.AgainstValues(ruled.Ruler!, ruled.Values), Splits.Divided(game, ruled), 12);
        Assert.True(Splits.Divided(game, ruled) > 0);
    }

    [Fact]
    public void A_colony_far_away_goes_its_own_way_in_peace()
    {
        var game = WithSettings((splits, _) => { splits["DISTANCE"] = 1000.0; splits["DIVISION"] = 0.0; splits["FAR"] = 100.0; });
        game.Waiting.Clear();
        int f = (int)Expected["provinces"]![0]!["faction"]!;
        var distances = Expected["provinces"]![0]!["distances"]!.AsObject().ToDictionary(p => int.Parse(p.Key), p => (double)p.Value!);
        var outer = Expected["provinces"]![0]!["outer"]!.AsArray().Select(t => (int)t!).ToList();
        int nearestFar = outer.Last(t => distances[t] >= 100.0);
        game.Towns[nearestFar].FoundedFrom = Splits.CapitalOf(game, f);
        int child = game.Factions.Count;
        Splits.Year(game, NoHardship(game), game.Year + 1);
        var split = game.Events.First(e => e.Factions[0] == f && e.Kind == "split");
        Assert.Equal("split_in_peace", split.What);
        Assert.Contains("too far from the capital, in peace", split.Text);
        Assert.Equal(Splits.Warm, game.Memory[(child, f)]);
    }

    [Fact]
    public void Land_handed_over_takes_its_mines()
    {
        var game = NewGame();
        var mine = game.Mines.First(m => m.LandTown != null);
        int to = Enumerable.Range(0, game.Factions.Count).First(f => f != mine.Owner);
        var others = game.Mines.Where(m => m.LandTown != mine.LandTown).ToDictionary(m => m, m => m.Owner);
        Splits.HandOver(game, new[] { mine.LandTown!.Value }, to);
        Assert.Equal(to, mine.Owner);
        Assert.All(others, p => Assert.Equal(p.Value, p.Key.Owner));
    }

    [Fact]
    public void Only_a_small_faction_that_thinks_well_enough_of_an_empire_joins_it()
    {
        Game Ready(out int empire, out int small)
        {
            var game = WithSettings((splits, _) => splits["EMPIRE_JOIN"] = 1.0);
            game.Wars.Clear();
            Splits.UpdateEmpires(game);
            int e = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].Hold < 1.0);
            small = Enumerable.Range(0, game.Factions.Count).First(f => f != e
                && Splits.Holdings(game, f).Count is > 0 and <= 3 && War.Fronts(game, f, e).Count > 0);
            foreach (var f in game.Factions)
                f.Opinions[e] = -0.5;
            empire = e;
            return game;
        }
        var game = Ready(out int empire, out int small);
        game.Factions[small].Opinions[empire] = -0.01;          // just below EMPIRE_JOIN_OPINION (0)
        Splits.JoinEmpires(game, game.Year + 1);
        Assert.DoesNotContain(game.Events, e => e.What == "joins_empire");

        game = Ready(out empire, out small);
        game.Factions[small].Opinions[empire] = 0.0;
        int other = Enumerable.Range(0, game.Factions.Count).First(f => f != small && f != empire && Splits.Holdings(game, f).Count > 3);
        foreach (int t in Splits.Holdings(game, other).Take(game.Settings.EmpireSmall + 1 - Splits.Holdings(game, small).Count))
            game.Towns[t].Holder = small;                      // one town too many to join
        Splits.JoinEmpires(game, game.Year + 1);
        Assert.DoesNotContain(game.Events, e => e.What == "joins_empire");
    }

    [Fact]
    public void A_small_friendly_neighbour_joins_an_empire()
    {
        var game = WithSettings((splits, _) => splits["EMPIRE_JOIN"] = 1.0);
        game.Wars.Clear();
        Splits.UpdateEmpires(game);
        int empire = Enumerable.Range(0, game.Factions.Count).First(f => game.Factions[f].Hold < 1.0);
        int small = Enumerable.Range(0, game.Factions.Count).First(f => f != empire
            && Splits.Holdings(game, f).Count is > 0 and <= 3 && War.Fronts(game, f, empire).Count > 0);
        foreach (var f in game.Factions)
            f.Opinions[empire] = -0.5;              // only `small` thinks well enough of the empire
        game.Factions[small].Opinions[empire] = 0.0;
        var towns = Splits.Holdings(game, small);
        Splits.JoinEmpires(game, game.Year + 1);
        Assert.All(towns, t => Assert.Equal(empire, game.Towns[t].Holder));
        Assert.Equal("joins_empire", game.Events[^1].What);
        Assert.Equal(0, game.Factions[small].People);
    }

    [Fact]
    public void A_town_whose_faction_is_gone_founds_a_faction_of_its_own_when_it_rises()
    {
        // Never rising again, never joining kin: the town founds a faction (war.break_free).
        var game = WithSettings((_, war) => { war["RISE_AGAIN"] = 0.0; war["JOIN_KIN"] = 0.0; }, seed: 5);
        game.Waiting.Clear();
        int town = Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined && game.Towns[t].Holder != null
                                                                     && game.Towns[t].Origin == game.Towns[t].Holder);
        int own = game.Towns[town].Holder!.Value;
        int conqueror = Enumerable.Range(0, game.Factions.Count).First(f => f != own && game.Factions[f].People > 0);
        War.Conquer(game, town, conqueror, own, null, counted: true);
        game.Towns[town].ConqueredIn = game.Year;
        foreach (int t in Splits.Holdings(game, own))
            game.Towns[t].Holder = conqueror;       // the faction is gone
        game.Factions[own].People = 0;
        var hardship = NoHardship(game);
        int before = game.Factions.Count;
        for (int year = game.Year + 1; year < game.Year + 400 && game.Towns[town].Holder == conqueror; year++)
            War.Rebel(game, town, hardship, year);
        Assert.Equal(before, game.Towns[town].Holder);
        Assert.Equal(before + 1, game.Factions.Count);
        Assert.Equal(before + 1, hardship.Count);
        var risen = game.Events[^1];
        Assert.Equal("rebellion_new_faction", risen.What);
        Assert.Equal(new[] { conqueror, own, before }, risen.Factions);
        Assert.Equal(game.Factions[own].Folk, game.Factions[before].Folk);
        Assert.Equal(conqueror, game.Factions[before].Parent);
        Assert.Equal(0, game.Factions[own].People);
    }

    [Fact]
    public void New_factions_may_differ_from_their_kin_by_the_settings()
    {
        var game = WithSettings((splits, _) =>
        {
            splits["TRAIT_SPREAD"] = 0.2;
            splits["BELIEF_CHANCE"] = 1.0;
            splits["GOVERNMENT_CHANCE"] = 1.0;
            splits["SPLIT_NAMES"] = new JsonObject { ["handcraft"] = new JsonArray("The Hearthfall Commons", "The Ash Guild", "Kilnfolk") };
        });
        game.Waiting.Clear();
        int parent = Enumerable.Range(0, game.Factions.Count)
            .First(f => game.Factions[f].Path == "handcraft" && Splits.Holdings(game, f).Count >= 2);
        var mother = game.Factions[parent];
        var part = new List<int> { Splits.Holdings(game, parent).Last() };
        int child = Splits.Secede(game, part, parent, "divided from their rulers", NoHardship(game), game.Year + 1);
        var made = game.Factions[child];
        // A listed name already used is skipped; the next is given, without its "The".
        Assert.Equal("Ash Guild", made.Name);
        Assert.Equal(2, game.SplitNamesGiven["handcraft"]);
        Assert.NotEqual(mother.Belief, made.Belief);
        Assert.NotEqual(mother.Government, made.Government);
        foreach (var (trait, value) in mother.Traits)
            Assert.InRange(made.Traits[trait], Math.Max(0, value - 0.2), Math.Min(1, value + 0.2));
        Assert.NotEqual(mother.Traits, made.Traits);
        var last = game.Events.TakeLast(3).Select(e => e.What).ToList();
        Assert.Equal(new[] { "split_division", "split_new_belief", "split_new_government" }, last);
    }

    [Fact]
    public void Years_of_splits_play_out_the_same_and_saves_carry_the_new_factions()
    {
        string Run(Game game, int years)
        {
            for (int i = 0; i < years; i++)
                YearCore.RunYear(game);
            return string.Join("|", game.Events.Select(e => e.Year + e.Text))
                   + string.Join(",", game.World.Factions.Select(f => f.Name + f.Red + f.Founded));
        }
        var first = WithSettings((splits, _) => { splits["DIVISION"] = 0.5; splits["EMPIRE_JOIN"] = 0.5; }, 8);
        string all = Run(first, 60);
        Assert.Contains(first.Events, e => e.Kind == "split");
        Assert.True(first.Factions.Count > first.World.Data["factions"]!.AsArray().Count);
        var again = WithSettings((splits, _) => { splits["DIVISION"] = 0.5; splits["EMPIRE_JOIN"] = 0.5; }, 8);
        Assert.Equal(all, Run(again, 60));

        var half = WithSettings((splits, _) => { splits["DIVISION"] = 0.5; splits["EMPIRE_JOIN"] = 0.5; }, 8);
        Run(half, 30);
        string path = Path.Combine(Path.GetTempPath(), "wr-split-save-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, half);
        var loaded = SaveFile.Read(path);
        Assert.Equal(half.Factions.Count, loaded.Factions.Count);
        Assert.Equal(half.World.Factions, loaded.World.Factions);
        Assert.Equal(half.Factions.Select(f => (f.Name, f.Folk, f.Capital, f.Parent, f.Founded, f.Hold)),
                     loaded.Factions.Select(f => (f.Name, f.Folk, f.Capital, f.Parent, f.Founded, f.Hold)));
        Assert.Equal(half.Waiting, loaded.Waiting);
        Assert.Equal(half.PeopleNamesUsed, loaded.PeopleNamesUsed);
        Assert.Equal(Enumerable.Range(0, half.Factions.Count).Select(f => Book.Card(half.World, f, half.Year)).Select(c => c.Name + c.From + c.Parent),
                     Enumerable.Range(0, loaded.Factions.Count).Select(f => Book.Card(loaded.World, f, loaded.Year)).Select(c => c.Name + c.From + c.Parent));
        Assert.Equal(all, Run(loaded, 30));
    }
}
