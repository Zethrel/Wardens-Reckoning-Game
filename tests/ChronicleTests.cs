// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;
using System.Text.Json.Nodes;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>The chronicle as it happens (step 10): against the generator's own chronicle.told on the export's events
/// with the same stand-in dice (tests/fixtures/make_expected.py, "chronicle"), and by what must follow.</summary>
public class ChronicleTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly JsonObject Expected =
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!["chronicle"]!.AsObject();

    /// <summary>make_expected.py's FIXED_DICE, handed out in turn, over and over.</summary>
    static readonly double[] FixedDice = { 0.2, 0.0005, 0.7, 0.35, 0.001, 0.9, 0.55, 0.003, 0.15, 0.8, 0.0002, 0.45 };

    static Game NewGame(long seed = 1) => new(ExportedWorld.Read(Path.Combine(Fixtures, "world-7")), seed);

    /// <summary>A fact as Python's str.format writes it: text as it is, a whole number in digits.</summary>
    static string Fact(JsonNode? value) => value switch
    {
        null => "None",
        JsonValue v when v.TryGetValue(out string? text) => text!,
        JsonValue v when v.TryGetValue(out bool yes) => yes ? "True" : "False",
        JsonValue v when v.TryGetValue(out long whole) => whole.ToString(CultureInfo.InvariantCulture),
        JsonValue v => ((double)v).ToString("R", CultureInfo.InvariantCulture),
        _ => value.ToJsonString(),
    };

    [Fact]
    public void The_history_s_eras_are_told_as_the_generator_tells_them()
    {
        var game = NewGame();
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "world-7", "world.json")))!;
        var events = data["events"]!.AsArray().Select(e => new GameEvent((int)e!["year"]!, (string)e["kind"]!, (string)e["what"]!,
            (string)e["text"]!, Array.Empty<int>(), e["facts"]!.AsObject().ToDictionary(p => p.Key, p => Fact(p.Value)))).ToList();
        int at = 0;
        double Dice() => FixedDice[at++ % FixedDice.Length];
        var chapters = new JsonArray();
        foreach (var (start, end) in Chronicle.Eras((int)data["years"]!, game.Settings.ChronicleEraYears))
        {
            var indexes = Enumerable.Range(0, events.Count).Where(i => events[i].Year >= start && events[i].Year <= end).ToList();
            var told = Chronicle.Tell(indexes.Select(i => events[i]).ToList(), game.Settings, Dice);
            chapters.Add(new JsonArray(start, end, new JsonArray(told.Select(t => (JsonNode)new JsonArray(indexes[t.Index], t.Sentence)).ToArray())));
        }
        Json.Same(Expected["chapters"], chapters, "chapters");
        Assert.Equal((int)Expected["dice_used"]!, at);
        Assert.All(chapters, c => Assert.InRange(c![2]!.AsArray().Count, 1, game.Settings.MostPerEra));
    }

    [Fact]
    public void An_era_s_notable_game_events_are_told_when_it_ends_and_not_before()
    {
        var game = NewGame(3);
        int era = game.Settings.ChronicleEraYears;
        int end = (game.Year / era + 1) * era - 1;                          // the history's grid: 100 to 149
        while (game.Year < end - 1)
            YearCore.RunYear(game);
        Assert.All(game.Events, e => Assert.Null(e.Told));
        YearCore.RunYear(game);
        var told = game.Events.Where(e => e.Told != null).ToList();
        Assert.InRange(told.Count, 1, game.Settings.MostPerEra);
        Assert.All(told, e => Assert.True(game.Settings.Templates.ContainsKey(e.What)));
        Assert.All(told, e => Assert.InRange(e.Year, end + 1 - era, end));
        // The book shows them too, and a save keeps them.
        Assert.Equal(told.Select(e => e.Told), game.BookEntries.Where(b => b.Year > end - era && b.Told != null && b.Year <= end
            && game.Events.Any(e => e.Told == b.Told)).Select(b => b.Told));
        string path = Path.Combine(Scratch.Root, "wr-chronicle-" + Guid.NewGuid().ToString("N") + ".json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Assert.Equal(game.Events.Select(e => e.Told), loaded.Events.Select(e => e.Told));
        Assert.Equal(game.BookEntries.Select(b => b.Told), loaded.BookEntries.Select(b => b.Told));
        // The next era tells its own years only: the first era's stay as they were.
        var first = game.Events.Select(e => e.Told).ToList();
        for (int i = 0; i < era; i++)
            YearCore.RunYear(game);
        Assert.Equal(first, game.Events.Take(first.Count).Select(e => e.Told));
        Assert.Contains(game.Events.Skip(first.Count), e => e.Told != null);
        Assert.All(game.Events.Skip(first.Count).Where(e => e.Told != null), e => Assert.InRange(e.Year, end + 1, end + era));
    }

    [Fact]
    public void Every_sentence_fills_with_the_game_s_own_events()
    {
        // Every template of every kind the game told or could tell in 150 years of three games must fill.
        var kinds = new HashSet<string>();
        foreach (long seed in new long[] { 1, 2, 3 })
        {
            var game = NewGame(seed);
            for (int i = 0; i < 150; i++)
                YearCore.RunYear(game);
            foreach (var e in game.Events)
                if (game.Settings.Templates.TryGetValue(e.What, out var templates))
                {
                    kinds.Add(e.What);
                    foreach (var template in templates)
                        Chronicle.Fill(template, e);
                }
        }
        Assert.True(kinds.Count >= 10, "only " + kinds.Count + " kinds with sentences happened: " + string.Join(", ", kinds));
    }

    [Fact]
    public void An_era_tells_only_its_own_years()
    {
        var game = NewGame();
        int era = game.Settings.ChronicleEraYears;
        int end = (game.Year / era + 2) * era - 1;                          // the second era's last year: 199
        string kind = game.Settings.Notable.Where(p => p.Value > 0 && game.Settings.Templates.ContainsKey(p.Key))
                                            .OrderByDescending(p => p.Value).First().Key;
        var facts = (Expected["slots"]![kind] as JsonArray ?? new JsonArray()).ToDictionary(slot => (string)slot!, _ => "x");
        facts["dead"] = "1000000";
        game.Record(new GameEvent(end - era, kind, kind, "earlier", Array.Empty<int>(), facts));   // the era before
        game.Record(new GameEvent(end, kind, kind, "this era", Array.Empty<int>(), facts));
        Chronicle.YearEnds(game, end);
        Assert.Null(game.Events[^2].Told);
        Assert.NotNull(game.Events[^1].Told);
    }

    [Fact]
    public void A_notable_kind_without_a_sentence_is_not_told()
    {
        // A copy of the settings with a notable kind ("unwritten") that has no sentence.
        string folder = Path.Combine(Scratch.Root, "wr-chronicle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(Fixtures, "world-7")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "world.json")))!;
        data["continue"]!["settings"]!["chronicle"]!["NOTABLE"]!["unwritten"] = 100.0;
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        var settings = new Game(ExportedWorld.Read(folder), 1).Settings;
        string written = settings.Notable.Where(p => p.Value > 0 && settings.Templates.ContainsKey(p.Key)).Select(p => p.Key).First();
        string unwritten = "unwritten";
        // Every slot the generator lists for the kind, so its sentence fills.
        GameEvent Of(string what)
        {
            var facts = (Expected["slots"]![what] as JsonArray ?? new JsonArray()).ToDictionary(slot => (string)slot!, _ => "x");
            facts["dead"] = "100000";
            return new(101, what, what, "", Array.Empty<int>(), facts);
        }
        var told = Chronicle.Tell(new[] { Of(unwritten), Of(written) }, settings, () => 0.0);
        Assert.Equal(new[] { 1 }, told.Select(t => t.Index));
    }

    [Fact]
    public void The_game_s_events_carry_every_fact_the_generator_s_do()
    {
        // chronicle.SLOTS: for every kind of event, the facts its sentences can use. Every game event of a kind there
        // must carry them all, in 150 years of three games.
        var slots = Expected["slots"]!.AsObject();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>();
        foreach (long seed in new long[] { 1, 2, 3 })
        {
            var game = NewGame(seed);
            for (int i = 0; i < 150; i++)
                YearCore.RunYear(game);
            foreach (var e in game.Events)
                if (slots[e.What] is JsonArray wanted)
                {
                    seen.Add(e.What);
                    foreach (var slot in wanted.Select(w => (string)w!))
                        if (!e.Facts.ContainsKey(slot))
                            missing.Add(e.What + ": " + slot);
                }
        }
        Assert.True(missing.Count == 0, "facts missing: " + string.Join("; ", missing));
        Assert.True(seen.Count >= 30, "only " + seen.Count + " kinds of event happened");
    }

    [Fact]
    public void A_sentence_fills_like_python_s_format_and_starts_each_sentence_with_a_capital()
    {
        var e = new GameEvent(321, "x", "x", "", Array.Empty<int>(), new Dictionary<string, string> { ["place"] = "dunmor", ["dead"] = "12" });
        Assert.Equal("In 321, dunmor fell. 12 died... and {braces} stayed.",
                     Chronicle.Fill("in {year}, {place} fell. {dead} died... and {{braces}} stayed.", e));
        Assert.Equal("Ruin! \"The end\" came. (Then) silence? Yes.", Chronicle.Capitals("ruin! \"the end\" came. (then) silence? yes."));
        Assert.Throws<ChronicleException>(() => Chronicle.Fill("{nowhere} burned", e));
    }

    [Fact]
    public void The_more_dead_the_more_notable_and_a_repeated_kind_fades()
    {
        var game = NewGame();
        var settings = game.Settings;
        var data = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "world-7", "world.json")))!["continue"]!["settings"]!["chronicle"]!;
        Assert.Equal((double)data["DEAD_BONUS"]!, settings.DeadBonus);      // your setting, not chronicle.py's default
        string kind = settings.Notable.Where(p => p.Value > 0 && settings.Templates.ContainsKey(p.Key)).Select(p => p.Key).First();
        GameEvent Of(string what, int dead) => new(101, what, what, "", Array.Empty<int>(), new Dictionary<string, string> { ["dead"] = dead.ToString() });
        double weight = settings.Notable[kind];
        Assert.Equal(weight * (1 + Math.Log10(1 + 999) * settings.DeadBonus), Chronicle.Score(Of(kind, 999), settings), 12);
        Assert.Equal(weight, Chronicle.Score(Of(kind, 0), settings));
        Assert.Equal(weight, Chronicle.Score(Of(kind, -50), settings));      // no fewer than none touched
        Assert.Equal(0.0, Chronicle.Score(Of("never_notable", 5000), settings));
        // MOST_PER_ERA + 1 of one kind: the most notable are told, in the order they happened.
        var many = Enumerable.Range(0, settings.MostPerEra + 1).Select(i => Of(kind, i * 10)).ToList();
        Assert.Equal(Enumerable.Range(1, settings.MostPerEra), Chronicle.Notable(many, settings));
    }
}
