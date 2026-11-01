// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>War and alliance (step 5): against the generator's own functions where no dice are rolled, and by what
/// must follow where they are.</summary>
public class WarTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static Game NewGame(long seed = 1) => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), seed);
    static readonly JsonObject Expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["trade"]!.AsObject();

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

    [Fact]
    public void Armies_are_as_strong_as_the_generator_reckons()
    {
        var game = NewGame();
        var supply = Opinions.Supplies(game, OneYearOfTrade(game));
        foreach (var (faction, value) in Expected["strengths"]!.AsObject())
            Assert.Equal((double)value!, War.Strength(game, int.Parse(faction), supply), 6);
    }

    [Fact]
    public void The_war_constants_are_the_generator_s()
    {
        var want = Expected["war_constants"]!.AsObject();
        var have = new Dictionary<string, double>
        {
            ["BATTLE_BITTERNESS"] = War.BattleBitterness, ["CONQUEST_BITTERNESS"] = War.ConquestBitterness,
            ["BETRAYAL"] = War.Betrayal, ["ALLY_MARGIN"] = War.AllyMargin, ["PEACE_CHANCE"] = War.PeaceChance,
            ["QUIET_YEARS"] = War.QuietYears, ["WINNER_LOSSES"] = War.WinnerLosses, ["REINFORCEMENTS"] = War.Reinforcements,
            ["DEFENDER_ADVANTAGE"] = War.DefenderAdvantage, ["TAKE_CHANCE"] = War.TakeChance, ["REBELLION"] = War.Rebellion,
            ["RESENTMENT_HALF_LIFE"] = War.ResentmentHalfLife, ["BORDER_YEARS"] = War.BorderYears,
            ["WAR_COMPETENCE"] = War.WarCompetence, ["WORSHIP_WAR"] = War.WorshipWar,
        };
        Assert.Equal(want.Select(p => p.Key).OrderBy(k => k), have.Keys.OrderBy(k => k));
        foreach (var (name, value) in want)
            Assert.True((double)value! == have[name], name + ": the generator has " + value + ", the game " + have[name]);
    }

    [Fact]
    public void An_army_fighting_from_one_town_is_as_strong_as_the_generator_reckons()
    {
        var game = NewGame();
        var supply = Opinions.Supplies(game, OneYearOfTrade(game));
        var want = Expected["fighting_from"]!.AsObject();
        Assert.NotEmpty(want);
        foreach (var (faction, node) in want)
        {
            int f = int.Parse(faction), town = (int)node![0]!;
            Assert.Equal((double)node[1]!, War.Strength(game, f, supply, game.Towns[town].Population, War.Held(game, f),
                                                        game.Settings.SizeInBattle), 6);
        }
    }

    [Fact]
    public void The_chance_of_war_its_reason_and_the_odds_in_battle_agree_with_the_generator()
    {
        var game = NewGame();
        var supply = Opinions.Supplies(game, OneYearOfTrade(game));
        var chances = Expected["war_chances"]!.AsArray();
        Assert.NotEmpty(chances);
        foreach (var node in chances)
        {
            var (chance, reason) = War.WarChance(game, (int)node![0]!, (int)node[1]!, supply);
            Assert.Equal((double)node[2]!, chance, 9);
            Assert.Equal((string)node[3]!, reason);
        }
        foreach (var node in Expected["odds"]!.AsArray())
        {
            var (attacker, defender) = War.Odds(game, (int)node![0]!, (int)node[1]!, (int)node[2]!, (int)node[3]!, supply);
            Assert.Equal((double)node[4]!, attacker, 6);
            Assert.Equal((double)node[5]!, defender, 6);
        }
    }

    [Fact]
    public void With_no_reason_at_all_the_reason_given_is_the_first_by_name()
    {
        var game = NewGame();
        var supply = Opinions.Supplies(game, OneYearOfTrade(game));
        var front = Expected["fronts"]!.AsArray()[0]!;
        int a = (int)front[0]!, b = (int)front[1]!;
        game.Factions[a].Opinions[b] = 0.5;      // no hatred
        game.Factions[a].Traits["greed"] = 0.0;  // no want
        game.Factions[a].Ruler = null;           // no ambition
        Assert.Equal((0.0, "ambition"), War.WarChance(game, a, b, supply));
    }

    [Fact]
    public void A_belief_the_settings_make_hateful_is_a_reason_for_war()
    {
        // A copy of the fixture where every belief thinks -0.5 of "worship" (all 0 in the maintainer's settings).
        string folder = Path.Combine(Path.GetTempPath(), "wr-belief-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        foreach (var (_, row) in data["continue"]!["settings"]!["factions"]!["BELIEF_OPINIONS"]!.AsObject())
            row!["worship"] = -0.5;
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        var game = new Game(ExportedWorld.Read(folder), 1);
        var supply = Opinions.Supplies(game, OneYearOfTrade(game));
        int a = 0, b = Enumerable.Range(1, game.Factions.Count - 1).First(f => game.Factions[f].Belief == "worship");
        Assert.Equal(0.5, War.Reasons(game, a, b, supply).Single(r => r.Reason == "belief").Value, 12);
        b = Enumerable.Range(1, game.Factions.Count - 1).First(f => game.Factions[f].Belief != "worship");
        Assert.Equal(0.0, War.Reasons(game, a, b, supply).Single(r => r.Reason == "belief").Value, 12);
    }

    [Fact]
    public void Fronts_and_reasons_for_war_agree_with_the_generator()
    {
        var game = NewGame();
        var supply = Opinions.Supplies(game, OneYearOfTrade(game));
        var fronts = Expected["fronts"]!.AsArray();
        var reasons = Expected["reasons"]!.AsArray();
        Assert.NotEmpty(fronts);
        for (int i = 0; i < fronts.Count; i++)
        {
            int a = (int)fronts[i]![0]!, b = (int)fronts[i]![1]!;
            var want = fronts[i]![2]!.AsArray().Select(p => ((int)p![0]!, (int)p[1]!)).ToList();
            Assert.Equal(want, War.Fronts(game, a, b));
            var why = War.Reasons(game, a, b, supply).ToDictionary(r => r.Reason, r => r.Value);
            foreach (var (reason, value) in reasons[i]![2]!.AsObject())
                Assert.Equal((double)value!, why[reason], 6);
        }
    }

    [Fact]
    public void Conquered_towns_resent_and_stir_as_the_generator_says()
    {
        var game = NewGame();
        int year = game.Year + 1;
        var want = Expected["unrest"]!.AsArray();
        Assert.NotEmpty(want);
        foreach (var node in want)
        {
            int town = (int)node![0]!;
            Assert.Equal((double)node[1]!, War.Resentment(game, game.Towns[town], year), 9);
            Assert.Equal((double)node[2]!, War.Unrest(game, town), 6);
        }
        Assert.Equal(want.Count, Enumerable.Range(0, game.Towns.Count).Count(t => War.Resentment(game, game.Towns[t], year) > 0));
    }

    [Fact]
    public void Borders_are_counted_and_the_land_shared_out_as_the_generator_does()
    {
        var game = NewGame();
        War.CountBorders(game);
        var counted = Expected["borders_counted"]!.AsArray();
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var want = counted[f]!.AsObject().ToDictionary(p => int.Parse(p.Key), p => (double)p.Value!);
            Assert.Equal(want.OrderBy(p => p.Key), game.Factions[f].Border.OrderBy(p => p.Key));
        }
        game.BordersChanged = true;
        War.RedrawBorders(game);
        Assert.Equal(Expected["spread"]!.AsArray().Select(v => (int)v!), game.Regions);
        Assert.False(game.BordersChanged);
    }

    [Fact]
    public void A_conquest_takes_the_town_its_land_and_mines()
    {
        var game = NewGame();
        int town = Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined && game.Towns[t].Holder != null
                                                                     && game.Mines.Any(m => m.LandTown == t));
        int loser = game.Towns[town].Holder!.Value;
        int winner = Enumerable.Range(0, game.Factions.Count).First(f => f != loser && game.Memory.ContainsKey((loser, f)));
        double before = game.Memory[(loser, winner)];
        var hardship = game.Factions.Select(_ => new YearCore.Hardship()).ToArray();
        War.Conquer(game, town, winner, loser, hardship, counted: true);
        Assert.Equal(winner, game.Towns[town].Holder);
        Assert.All(game.Mines.Where(m => m.LandTown == town), m => Assert.Equal(winner, m.Owner));
        Assert.All(Enumerable.Range(0, game.Grid.Owner.Length).Where(c => game.Grid.Owner[c] == town),
                   c => Assert.Equal(winner, game.Regions[c]));
        Assert.Equal(Math.Max(-1, before - War.ConquestBitterness), game.Memory[(loser, winner)], 12);
        Assert.Equal(2, hardship[loser].Lost);
        Assert.Equal(1, game.Conquests);
        Assert.True(game.BordersChanged);
    }

    [Fact]
    public void Friends_ally_and_fall_out()
    {
        var game = NewGame();
        var alive = Enumerable.Range(0, game.Factions.Count).Where(f => game.Factions[f].People > 0).Take(2).ToList();
        int a = alive[0], b = alive[1];
        game.Alliances.Clear();
        game.Wars.Clear();
        game.Factions[a].Opinions[b] = 0.9;
        game.Factions[b].Opinions[a] = 0.9;
        var markets = OneYearOfTrade(game);
        var hardship = game.Factions.Select(_ => new YearCore.Hardship()).ToArray();
        War.Year(game, markets, hardship, game.Year + 1);
        Assert.Contains((Math.Min(a, b), Math.Max(a, b)), game.Alliances);
        game.Factions[b].Opinions[a] = game.Settings.AllyOpinion - War.AllyMargin / 2;   // cooler, but inside the margin
        War.Year(game, markets, hardship, game.Year + 2);
        Assert.Contains((Math.Min(a, b), Math.Max(a, b)), game.Alliances);
        game.Factions[b].Opinions[a] = game.Settings.AllyOpinion - War.AllyMargin - 0.01;
        War.Year(game, markets, hardship, game.Year + 3);
        Assert.DoesNotContain((Math.Min(a, b), Math.Max(a, b)), game.Alliances);
        Assert.Contains(game.Events, e => e.What == "alliance_ends");
    }

    [Fact]
    public void A_war_is_fought_town_by_town_and_ends()
    {
        var game = NewGame(3);
        var front = Expected["fronts"]!.AsArray()[0]!;
        int a = (int)front[0]!, b = (int)front[1]!;
        game.Wars.Clear();
        game.Alliances.Clear();
        War.Declare(game, a, b, "hatred", game.Year + 1);
        var war = game.Wars.Single();
        Assert.Null(war.Ended);
        Assert.InRange(war.Endurance!.Value, game.Settings.PeaceWeariness.Low, game.Settings.PeaceWeariness.High);
        double peopleBefore = game.Towns.Where(t => t.Holder == a || t.Holder == b).Sum(t => t.People);
        for (int i = 0; i < 60 && war.Ended == null; i++)
            YearCore.RunYear(game);
        Assert.Contains(game.Events, e => e.What is "battle" or "conquest");
        Assert.True(war.Dead.Values.Sum() > 0);
        Assert.NotNull(war.Ended);
        Assert.Contains(game.Events, e => e.What is "peace" or "war_fizzles" or "war_ends_one_side_gone");
    }

    [Fact]
    public void A_conquered_town_rises_and_returns_to_its_own_people_in_time()
    {
        var game = NewGame(5);
        int town = Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined && game.Towns[t].Holder != null
                                                                     && game.Towns[t].Origin == game.Towns[t].Holder);
        int own = game.Towns[town].Holder!.Value;
        int conqueror = Enumerable.Range(0, game.Factions.Count).First(f => f != own && game.Factions[f].People > 0);
        War.Conquer(game, town, conqueror, own, null, counted: true);
        game.Towns[town].ConqueredIn = game.Year;
        var hardship = game.Factions.Select(_ => new YearCore.Hardship()).ToArray();
        for (int year = game.Year + 1; year < game.Year + 400 && game.Towns[town].Holder == conqueror; year++)
            War.Rebel(game, town, hardship, year);
        Assert.Equal(own, game.Towns[town].Holder);
        Assert.Equal(1, game.Conquests);   // the conquest; the rising is not one
        Assert.Equal(1, game.Rebellions);
        Assert.Contains(game.Events, e => e.What.StartsWith("rebellion") && e.Factions.Contains(own));
        Assert.Equal(0.0, War.Resentment(game, game.Towns[town], game.Year + 1));
    }

    [Fact]
    public void A_conqueror_with_no_hold_on_a_town_is_never_risen_against()
    {
        var game = NewGame(5);
        int town = Enumerable.Range(0, game.Towns.Count).First(t => !game.Towns[t].Ruined && game.Towns[t].Holder != null
                                                                     && game.Towns[t].Origin == game.Towns[t].Holder);
        int own = game.Towns[town].Holder!.Value;
        int conqueror = Enumerable.Range(0, game.Factions.Count).First(f => f != own && game.Factions[f].People > 0);
        War.Conquer(game, town, conqueror, own, null, counted: true);
        game.Towns[town].ConqueredIn = game.Year;
        game.Factions[conqueror].Hold = 0.0;
        var hardship = game.Factions.Select(_ => new YearCore.Hardship()).ToArray();
        for (int year = game.Year + 1; year < game.Year + 400; year++)
            War.Rebel(game, town, hardship, year);
        Assert.Equal(conqueror, game.Towns[town].Holder);
        Assert.Equal(0, game.Rebellions);
    }

    // A war between the two factions of the first front, with the endurance of the weariest possible war.
    static (Game Game, WarNow War, int A, int B) AtWar(long seed)
    {
        var game = NewGame(seed);
        var front = Expected["fronts"]!.AsArray()[0]!;
        int a = (int)front[0]!, b = (int)front[1]!;
        var war = new WarNow { Attacker = a, Defender = b, Since = game.Year, LastBattle = game.Year,
                               Sides = new[] { new HashSet<int> { a }, new HashSet<int> { b } }, Endurance = 2.0 };
        game.Wars.Clear();
        game.Wars.Add(war);
        return (game, war, a, b);
    }

    [Fact]
    public void A_battle_costs_the_winner_half_what_it_costs_the_loser_and_embitters_both()
    {
        int plain = 0, taken = 0, won = 0;
        for (long seed = 1; seed <= 60; seed++)
        {
            var (game, war, a, b) = AtWar(seed);
            var supply = Opinions.Supplies(game, OneYearOfTrade(game));
            var hardship = game.Factions.Select(_ => new YearCore.Hardship()).ToArray();
            var before = game.Towns.Select(t => (t.People, t.Holder)).ToArray();
            double ab = game.Memory[(a, b)], ba = game.Memory[(b, a)];
            War.Battle(game, war, a, b, War.Fronts(game, a, b), supply, hardship, game.Year + 1);
            var fought = game.Events[^1];
            Assert.Equal("battle", fought.Kind);
            int winner = game.Factions.FindIndex(f => f.Name == fought.Facts["winner"]);
            int attacker = game.Factions.FindIndex(f => f.Name == fought.Facts["attacker"]);
            int defender = attacker == a ? b : a;
            var hit = Enumerable.Range(0, before.Length).Where(t => game.Towns[t].People != before[t].People).ToList();
            Assert.Equal(2, hit.Count);
            int winners = hit.Single(t => before[t].Holder == winner), losers = hit.Single(t => before[t].Holder != winner);
            double share = 1 - game.Towns[losers].People / before[losers].People;
            Assert.Equal(share * War.WinnerLosses, 1 - game.Towns[winners].People / before[winners].People, 9);
            double dead = before[winners].People - game.Towns[winners].People + before[losers].People - game.Towns[losers].People;
            Assert.Equal(((int)Math.Round(dead, MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture), fought.Facts["dead"]);
            if (winner == attacker)
                won++;
            if (fought.What == "conquest")
            {
                taken++;
                // The defender loses the town's people that are left to the war's dead, and the town.
                Assert.Equal(attacker, game.Towns[losers].Holder);
                Assert.Equal(before[losers].People - game.Towns[losers].People + game.Towns[losers].Population,
                             war.Dead[defender], 6);
                Assert.Equal(Math.Max(-1, (defender == a ? ab : ba) - War.BattleBitterness - War.ConquestBitterness),
                             game.Memory[(defender, attacker)], 12);
            }
            else
            {
                plain++;
                Assert.Equal(Math.Max(-1, ab - War.BattleBitterness), game.Memory[(a, b)], 12);
                Assert.Equal(Math.Max(-1, ba - War.BattleBitterness), game.Memory[(b, a)], 12);
            }
        }
        Assert.True(plain > 0 && taken > 0);
        // TAKE_CHANCE: about half the battles the attacker wins take the town.
        Assert.InRange((double)taken / won, 0.25, 0.75);
    }

    [Fact]
    public void A_war_fizzles_out_after_QUIET_YEARS_without_a_battle()
    {
        var (game, war, _, _) = AtWar(1);
        int year = game.Year + 10;
        war.LastBattle = year - War.QuietYears + 1;
        War.MakePeace(game, war, year);
        Assert.Null(war.Ended);
        war.LastBattle = year - War.QuietYears;
        War.MakePeace(game, war, year);
        Assert.Equal(year, war.Ended);
        Assert.Equal("war_fizzles", game.Events[^1].What);
    }

    [Fact]
    public void Peace_comes_once_either_side_is_weary_enough()
    {
        var (game, war, a, b) = AtWar(1);
        int year = game.Year + 1;
        war.LastBattle = year;     // not quiet
        war.Endurance = 0.1;
        for (int i = 0; i < 50; i++)
            War.MakePeace(game, war, year);
        Assert.Null(war.Ended);    // no one has died yet
        war.Dead[b] = game.Factions[b].People;   // half the defender's side is dead: weary enough
        for (int i = 0; i < 50 && war.Ended == null; i++)
            War.MakePeace(game, war, year);
        Assert.Equal(year, war.Ended);
        Assert.Equal("peace", game.Events[^1].What);
        Assert.Equal("the " + game.Factions[a].Name + " and the " + game.Factions[b].Name + " make peace after 1 year",
                     game.Events[^1].Text);
    }

    // An ally of `a` that is not `b`, with its alliance and a memory of `a`.
    static int AllyOf(Game game, int a, int b)
    {
        int ally = Enumerable.Range(0, game.Factions.Count)
            .First(f => f != a && f != b && game.Factions[f].People > 0 && game.Memory.ContainsKey((a, f)));
        game.Alliances.Clear();
        game.Alliances.Add((Math.Min(a, ally), Math.Max(a, ally)));
        return ally;
    }

    [Fact]
    public void A_bold_ally_joins_the_war_and_a_timid_one_betrays_the_alliance()
    {
        var game = NewGame();
        var front = Expected["fronts"]!.AsArray()[0]!;
        int a = (int)front[0]!, b = (int)front[1]!;
        game.Wars.Clear();
        int ally = AllyOf(game, a, b);
        game.Factions[ally].Traits["risk"] = 1.0;
        War.Declare(game, a, b, "hatred", game.Year + 1);
        Assert.Contains(ally, game.Wars[^1].Sides[0]);
        Assert.Equal("war_joined", game.Events[^1].What);

        game = NewGame();
        game.Wars.Clear();
        AllyOf(game, a, b);
        game.Factions[ally].Traits["risk"] = 0.0;
        game.Memory[(a, ally)] = 0.5;   // well above -1, so the betrayal shows
        double remembered = game.Memory[(a, ally)];
        War.Declare(game, a, b, "hatred", game.Year + 1);
        Assert.DoesNotContain(ally, game.Wars[^1].Sides[0]);
        Assert.Empty(game.Alliances);
        Assert.Equal(Math.Max(-1, remembered - War.Betrayal), game.Memory[(a, ally)], 12);
        Assert.Equal("betrayal", game.Events[^1].What);
    }

    [Fact]
    public void An_ally_of_both_sides_stays_out()
    {
        var game = NewGame();
        var front = Expected["fronts"]!.AsArray()[0]!;
        int a = (int)front[0]!, b = (int)front[1]!;
        game.Wars.Clear();
        int ally = AllyOf(game, a, b);
        game.Alliances.Add((Math.Min(b, ally), Math.Max(b, ally)));
        game.Factions[ally].Traits["risk"] = 1.0;
        int events = game.Events.Count;
        War.Declare(game, a, b, "hatred", game.Year + 1);
        Assert.DoesNotContain(ally, game.Wars[^1].Sides[0]);
        Assert.DoesNotContain(ally, game.Wars[^1].Sides[1]);
        Assert.Equal(2, game.Alliances.Count);
        Assert.Equal(events + 1, game.Events.Count);   // only the declaration
    }

    [Fact]
    public void The_borders_are_redrawn_every_BORDER_YEARS()
    {
        var game = NewGame();
        var markets = OneYearOfTrade(game);
        var hardship = game.Factions.Select(_ => new YearCore.Hardship()).ToArray();
        int round = (game.Year / War.BorderYears + 1) * War.BorderYears;
        game.BordersChanged = true;
        War.Year(game, markets, hardship, round + 1);
        Assert.True(game.BordersChanged);
        War.Year(game, markets, hardship, round + War.BorderYears);
        Assert.False(game.BordersChanged);
    }

    [Fact]
    public void Years_of_war_play_out_the_same_and_the_borders_on_the_map_follow()
    {
        string Run(out Game game)
        {
            game = NewGame(8);
            for (int i = 0; i < 100; i++)
                YearCore.RunYear(game);
            return string.Join("|", game.Events.Select(e => e.Year + e.Text));
        }
        Assert.Equal(Run(out var first), Run(out _));
        Assert.Contains(first.Events, e => e.What == "war_declared");
        Assert.Equal(first.Regions, first.World.Borders(first.Year));
    }
}
