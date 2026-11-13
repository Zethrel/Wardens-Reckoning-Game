// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 19b: fouled water (library Review 37): sources fouling on their own dice, fouled water carried and mixed,
/// the signs, the player's marks, boiling, and the upset stomach that drinking it may bring.
/// </summary>
public class SanitationTests
{
    static readonly Content Real = TestContent.Real;

    /// <summary>The real content with some numbers changed, so a test need not wait for the dice: `foulEvery`
    /// days between foulings (1: every morning), a fouling lasting `lasts` days, a sign in `signs` of them, and
    /// `illness` the chance of an upset stomach (shown after `onset` hours, lasting `ill` hours).</summary>
    internal static Content Tweaked(double foulEvery = 1, int lasts = 3, double signs = 1, double illness = 1, int onset = 6, int ill = 24)
    {
        var files = TestContent.Files();                          // at the old pace (step 24: see TestContent)
        files["sanitation.json"] = files["sanitation.json"]!
            .Replace("\"days_between\": 60", "\"days_between\": " + foulEvery.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("\"lasts_days\": [3, 10]", "\"lasts_days\": [" + lasts + ", " + lasts + "]")
            .Replace("\"sign_share\": 0.5", "\"sign_share\": " + signs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        files["health.json"] = files["health.json"]!
            .Replace("\"chance\": 0.3333", "\"chance\": " + illness.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("\"onset_hours\": [6, 12]", "\"onset_hours\": [" + onset + ", " + onset + "]")
            .Replace("\"lasts_hours\": [24, 48]", "\"lasts_hours\": [" + ill + ", " + ill + "]");
        // Founders all at the usual proficiency (step 23a), so that work takes its usual time here.
        files["founding.json"] = files["founding.json"]!
            .Replace("\"skills\": { \"construction\": [ 5, 65 ], \"food_preparation\": [ 5, 65 ] }",
                     "\"skills\": { \"construction\": [ 40, 40 ], \"food_preparation\": [ 40, 40 ] }");
        Assert.Contains("[ 40, 40 ]", files["founding.json"]);
        return Content.Read(TestContent.NotLearning(files));      // and learning nothing (step 23b)
    }

    static Moment On(int month, int hour = 8) => new(Calendar.TickOf(1, month, 1, hour, 0));

    /// <summary>A local map of `size` tiles of grass with a brook down the column x = `water`.</summary>
    internal static LocalMap Map(int size, int? water, string? kind = "stream")
    {
        var rows = Enumerable.Range(0, size).Select(_ => new string(Enumerable.Range(0, size).Select(x => x == water ? 'w' : 'g').ToArray())).ToArray();
        return LocalMap.Parse(new JsonObject
        {
            ["format"] = "the-north-local", ["version"] = 1, ["size"] = size, ["biome"] = "meadow", ["water"] = kind,
            ["surfaces"] = new JsonObject { ["g"] = "grass", ["w"] = "fresh_water" },
            ["things"] = new JsonObject(),
            ["ground"] = new JsonArray(rows.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
            ["on_ground"] = new JsonArray(rows.Select(r => (JsonNode?)JsonValue.Create(new string('.', size))).ToArray()),
        }, "map");
    }

    // As Until, a second at a time: for what is measured from the second it happened.
    static bool EverySecondUntil(Colony.Colony colony, Func<bool> done, int seconds)
    {
        for (int i = 0; i < seconds && !done(); i++)
            colony.Run(1);
        return done();
    }

    static bool Until(Colony.Colony colony, Func<bool> done, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            if (done())
                return true;
            colony.RunMinutes(1);
        }
        return done();
    }

    // Runs to the minute after the next morning (when the sources foul or run clean, and fill).
    static void PastMorning(Colony.Colony colony)
    {
        colony.RunMinutes(1);
        Assert.True(Until(colony, () => colony.Now.MinuteOfDay == Autosave.MorningMinute + 1, 25 * 60));
    }

    // All of `good` free in an inventory is used up (as if drunk or lost).
    static void Empty(Colony.Colony colony, Inventory inventory, string good)
    {
        int free = colony.Stocks.Free(inventory.Id, good);
        if (free == 0)
            return;
        var claim = colony.Stocks.Reserve(inventory.Id, good, free, "test").Reservation!;
        colony.Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { claim.Id }, inventory.Id, new Dictionary<string, int>());
    }

    // A colony by a brook that fouled this morning, at nine (everyone awake, breakfast over): the colony, its carts
    // and the brook.
    static (Colony.Colony Colony, Inventory Carts, Inventory Brook) Fouled(Content content)
    {
        var (colony, carts) = Founding.Found(7, On(1), content, Map(40, 30));
        var brook = colony.Stocks[colony.Work!.Springs.Single()];
        PastMorning(colony);
        Assert.True(Until(colony, () => colony.Now.MinuteOfDay == 9 * Calendar.MinutesPerHour, 4 * 60));
        Assert.NotNull(colony.Work.SourceOf(brook.Id).FouledUntil);
        return (colony, carts, brook);
    }

    static int Total(Colony.Colony colony, string good) => colony.Stocks.Inventories.Sum(i => i.Count(good));

    [Fact]
    public void The_sanitation_and_health_content_is_read_as_review_37_gives_it()
    {
        var s = Real.Sanitation;
        Assert.Equal(("foul_water", 60.0, 3, 10, 0.5), (s.FouledGood, s.DaysBetween, s.LastsFrom, s.LastsTo, s.SignShare));
        Assert.Equal(new[] { "dead fish in the water", "a foul smell", "scum on the water" }, s.Signs);
        Assert.Equal((10, "timber", 1, 30), (s.BoilLitres, s.Fuel, s.FuelUnits, s.BoilMinutes));
        var u = Real.UpsetStomach;
        Assert.Equal(("Upset stomach", 0.3333, 6, 12, 24, 48), (u.Name, u.Chance, u.OnsetFrom, u.OnsetTo, u.LastsFrom, u.LastsTo));
        Assert.Equal((0.7, 1.5, 1.5), (u.Work, u.Thirst, u.Fatigue));
        Assert.Equal(new[] { "water", "foul_water" }, Real.Drinkable);
        Assert.Equal("Fouled water", Real.Goods["foul_water"].Name);
    }

    [Theory]
    [InlineData("\"days_between\": 60", "\"days_between\": 0")]
    [InlineData("\"lasts_days\": [3, 10]", "\"lasts_days\": [10, 3]")]
    [InlineData("\"lasts_days\": [3, 10]", "\"lasts_days\": [0, 3]")]
    [InlineData("\"sign_share\": 0.5", "\"sign_share\": 1.5")]
    [InlineData("\"fouled_good\": \"foul_water\"", "\"fouled_good\": \"mud\"")]
    [InlineData("\"fuel\": \"timber\"", "\"fuel\": \"peat\"")]
    [InlineData("\"litres\": 10", "\"litres\": 0")]
    public void Broken_sanitation_content_is_refused(string from, string to)
    {
        var files = Content.Files.ToDictionary(f => f, f => (string?)File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", f)));
        Assert.Contains(from, files["sanitation.json"]);
        files["sanitation.json"] = files["sanitation.json"]!.Replace(from, to);
        Assert.Throws<ContentException>(() => Content.Read(files));
    }

    [Theory]
    [InlineData("\"chance\": 0.3333", "\"chance\": 1.2")]
    [InlineData("\"onset_hours\": [6, 12]", "\"onset_hours\": [12, 6]")]
    [InlineData("\"lasts_hours\": [24, 48]", "\"lasts_hours\": [0, 48]")]
    [InlineData("\"work\": 0.7", "\"work\": 0")]
    [InlineData("\"thirst\": 1.5", "\"thirst\": -1")]
    public void Broken_health_content_is_refused(string from, string to)
    {
        var files = Content.Files.ToDictionary(f => f, f => (string?)File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", f)));
        Assert.Contains(from, files["health.json"]);
        files["health.json"] = files["health.json"]!.Replace(from, to);
        Assert.Throws<ContentException>(() => Content.Read(files));
    }

    [Fact]
    public void A_brook_fouls_on_its_dice_and_runs_clean_after_its_days()
    {
        var content = Tweaked(lasts: 3);
        var (colony, carts) = Founding.Found(7, On(1), content, Map(40, 30));
        var work = colony.Work!;
        var brook = colony.Stocks[work.Springs.Single()];
        Assert.Null(work.SourceOf(brook.Id).FouledUntil);       // founded clean
        PastMorning(colony);
        long morning = colony.Now.Tick - Calendar.TicksPerMinute;
        var source = work.SourceOf(brook.Id);
        Assert.Equal(morning + 3 * Calendar.TicksPerDay, source.FouledUntil);
        Assert.Contains(source.Sign, content.Sanitation.Signs);
        // All its water turned: none clean in it, the morning's fill fouled too.
        Assert.Equal(0, brook.Count("water"));
        Assert.Equal(work.LitresOf(brook.Id), brook.Count("foul_water"));
        // The water already in the carts was drawn before: it stays clean.
        Assert.Equal(0, carts.Count("foul_water"));
        // On the third morning after, it runs clean (and does not foul again that same morning).
        for (int day = 0; day < 3; day++)
        {
            Assert.NotNull(work.SourceOf(brook.Id).FouledUntil);
            PastMorning(colony);
        }
        Assert.Null(source.FouledUntil);
        Assert.Null(source.Sign);
        Assert.Equal(0, brook.Count("foul_water"));
        Assert.Equal(work.LitresOf(brook.Id), brook.Count("water"));
        // Every morning after, with a fouling due every day: fouled again.
        PastMorning(colony);
        Assert.NotNull(source.FouledUntil);
    }

    [Fact]
    public void Wells_never_foul_and_with_the_real_dice_a_brook_rarely_does()
    {
        var (colony, _) = Founding.Found(7, On(1), Tweaked(), Map(40, null, null));
        var well = colony.Work!.AddSpring(10.5, 10.5, "Well", 200, WaterKind.Well);
        PastMorning(colony);
        PastMorning(colony);
        Assert.Null(colony.Work.SourceOf(well.Id).FouledUntil);
        Assert.Equal(200, well.Count("water"));
        // With a fouling once in a thousand million days, none in three.
        var (calm, _) = Founding.Found(7, On(1), Tweaked(foulEvery: 1e9), Map(40, 30));
        for (int day = 0; day < 3; day++)
            PastMorning(calm);
        Assert.Null(calm.Work!.SourceOf(calm.Work.Springs.Single()).FouledUntil);
    }

    [Fact]
    public void The_same_seed_fouls_the_same_and_another_system_rolling_does_not_change_it()
    {
        // Sanitation has its own dice: two colonies of one seed foul on the same days with the same signs,
        // whatever else happens (here: one of them has someone falling ill on the health dice in between).
        string Story(bool sicken)
        {
            var (colony, _) = Founding.Found(11, On(1), Tweaked(foulEvery: 4, lasts: 2, signs: 0.5), Map(40, 30));
            var work = colony.Work!;
            var brook = work.Springs.Single();
            var story = new List<string>();
            for (int day = 0; day < 20; day++)
            {
                if (sicken)
                    work.Expose(work.Workers.First());
                PastMorning(colony);
                var s = work.SourceOf(brook);
                story.Add(s.FouledUntil + ":" + s.Sign);
            }
            return string.Join("|", story);
        }
        string story = Story(false);
        Assert.Equal(story, Story(true));
        Assert.Contains(story.Split('|'), day => day != ":");         // something fouled in 20 days (one in four)
    }

    [Fact]
    public void FP06_the_player_sees_fouled_water_as_water_and_only_the_trace_tells_it_apart()
    {
        var (colony, _, brook) = Fouled(Tweaked());
        var (x, y) = Ground.TileOf(brook.X, brook.Y);
        var seen = Inspect.Tile(colony, x, y);
        string all = string.Join("\n", seen.Lines.Select(l => l.Label + ": " + l.Text));
        Assert.DoesNotContain("ouled", all);
        Assert.Contains(" Water", seen.Lines.Single(l => l.Label == "Water").Text);
        var traced = Inspect.Tile(colony, x, y, trace: true);
        Assert.Contains("Fouled water", traced.Lines.Single(l => l.Label == "Water").Text);
        Assert.Contains(traced.Lines, l => l.Label == "Trace" && l.Text.StartsWith("fouled until") && l.Text.Contains("sign: "));
        // Counted together as water: 3 water and 4 fouled water are "7 Water".
        Assert.Equal("7 Water", Inspect.Goods(colony, new Dictionary<string, int> { ["water"] = 3, ["foul_water"] = 4 }));
        Assert.Equal("4 Fouled water, 3 Water", Inspect.Goods(colony, new Dictionary<string, int> { ["water"] = 3, ["foul_water"] = 4 }, trace: true));
    }

    [Fact]
    public void Fouled_water_poured_into_clean_water_fouls_all_of_it()
    {
        var (colony, carts, _) = Fouled(Tweaked());
        foreach (var w in colony.Work!.Workers)
            colony.Work.Stop(w.Id);
        int clean = carts.Count("water");
        Assert.True(clean > 0);
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, carts.Id, "foul_water", 5);
        colony.RunMinutes(1);
        Assert.Equal(0, carts.Count("water"));
        Assert.Equal(clean + 5, carts.Count("foul_water"));
    }

    [Fact]
    public void A_colonist_at_the_water_sees_the_sign_and_the_player_is_told_once()
    {
        // Fouled at six, while everyone sleeps; after breakfast they go to wash on the bank, and see it.
        var (colony, _) = Founding.Found(7, On(1), Tweaked(signs: 1), Map(40, 30));
        var work = colony.Work!;
        var brook = colony.Stocks[work.Springs.Single()];
        PastMorning(colony);
        var source = work.SourceOf(brook.Id);
        string sign = source.Sign!;
        Assert.False(source.SignSeen);
        Assert.Empty(work.Told);
        Assert.True(Until(colony, () => source.SignSeen, 4 * 60), "nobody came to the water");
        var told = Assert.Single(work.Told);
        Assert.EndsWith(" saw " + sign + " at " + brook.Name.ToLowerInvariant() + ".", told.Text);
        Assert.Equal(colony.Now.Tick, told.Tick);           // a tick happens at the moment it ends on
        colony.RunMinutes(60);
        Assert.Single(work.Told);
        // Without a sign nobody knows: nothing is told however long they stand there.
        var (quiet, _, quietBrook) = Fouled(Tweaked(signs: 0));
        Assert.Null(quiet.Work!.SourceOf(quietBrook.Id).Sign);
        quiet.RunMinutes(4 * 60);
        Assert.DoesNotContain(quiet.Work.Told, n => n.Text.Contains(" saw "));
    }

    [Fact]
    public void Nobody_drinks_water_marked_do_not_drink_or_boil_first()
    {
        foreach (var mark in new[] { WaterMark.DoNotDrink, WaterMark.BoilFirst })
        {
            var (colony, carts) = Founding.Found(7, On(1), Real, Map(40, 30));
            var work = colony.Work!;
            var brook = colony.Stocks[work.Springs.Single()];
            work.SetMark(carts.Id, mark);
            work.SetMark(brook.Id, mark);
            Assert.Equal(mark, carts.Mark);
            int inCarts = carts.Count("water"), inBrook = brook.Count("water");
            var ada = work.Workers.First();
            ada.Needs["thirst"].Level = 70;
            colony.RunMinutes(30);
            Assert.True(ada.Needs["thirst"].Level >= 70, "Ada drank");
            Assert.Equal((inCarts, inBrook), (carts.Count("water"), brook.Count("water")));
            Assert.Equal("no water free in any store the colonists may drink from (some is marked not to be drunk as it is)", ada.Lacking);
            // Drink freely again: she drinks.
            work.SetMark(carts.Id, WaterMark.None);
            Assert.True(Until(colony, () => ada.Needs["thirst"].Level < 30, 60), "never drank");
        }
    }

    [Fact]
    public void A_barrel_is_never_filled_from_a_marked_source()
    {
        var (colony, carts) = Founding.Found(7, On(1), Real, Map(40, 30));
        var work = colony.Work!;
        var brook = colony.Stocks[work.Springs.Single()];
        work.SetMark(brook.Id, WaterMark.DoNotDrink);
        Empty(colony, carts, "water");                         // nothing to fetch but the brook
        var (project, placement) = work.Plan("water_barrel", 26, 22);
        Assert.True(placement.Ok, placement.Why);
        Assert.True(Until(colony, () => colony.Ground!.PieceAt(26, 22) == "water_barrel", 24 * 60));
        var barrel = colony.Stocks.Inventories.Single(i => i.Name == "Water barrel");
        colony.RunMinutes(6 * 60);
        Assert.Equal(0, barrel.Count("water") + barrel.Count("foul_water"));
        Assert.DoesNotContain(work.Jobs, j => j.Kind == JobKind.Haul && j.To == barrel.Id);
    }

    [Fact]
    public void Water_marked_boil_first_is_boiled_ten_litres_at_a_time_for_a_timber_and_stored_clean()
    {
        var (colony, carts, brook) = Fouled(Tweaked());
        var work = colony.Work!;
        work.SetMark(brook.Id, WaterMark.BoilFirst);
        int timber = Total(colony, "timber");
        int cleanBefore = Total(colony, "water");
        var job = Assert.Single(work.Jobs, j => j.Kind == JobKind.Boil);   // asked for as the mark is set
        Assert.Equal((10, brook.Id, carts.Id, "water"), (job.Amount, job.Boiling!.Value, job.From, job.Good));
        Assert.Equal(WaterMark.None, colony.Stocks[job.To].Mark);
        Assert.NotEqual(brook.Id, job.To);
        Assert.Null(work.WhyNotBoiled(brook.Id));
        // Thirty minutes of boiling at the water, then the boiled water is carried to the store.
        Assert.True(EverySecondUntil(colony, () => job.Step == Step.Working, 60 * 60), "never began to boil");
        Assert.Single(work.Jobs, j => j.Kind == JobKind.Boil);  // one boiling at a time for a store
        var boiler = work.Workers.Single(w => w.Job == job.Id);
        Assert.Equal(1, colony.Stocks[boiler.Arms].Count("timber"));
        Assert.Equal(Ground.TileOf(brook.X, brook.Y), Ground.TileOf(boiler.X, boiler.Y));
        int seconds = 0;
        while (job.Step == Step.Working)
        {
            colony.Run(1);
            seconds++;
        }
        // Thirty minutes from the second it began, seen at the boiler's first think after (every 2 to 7 seconds).
        Assert.InRange(seconds, 30 * 60, 30 * 60 + 7);
        Assert.Equal((0, 10, 0), (colony.Stocks[boiler.Arms].Count("timber"), colony.Stocks[boiler.Arms].Count("water"), colony.Stocks[boiler.Arms].Count("foul_water")));
        Assert.Equal(timber - 1, Total(colony, "timber"));
        var to = colony.Stocks[job.To];
        int before = to.Count("water");
        Assert.True(Until(colony, () => !work.Jobs.Any(j => j.Id == job.Id), 60), "never stored");
        Assert.True(to.Count("water") >= before + 10 - 2, "the boiled water was not stored");   // a drink or two may be taken
        Assert.Equal(0, to.Count("foul_water"));
        Assert.True(Total(colony, "water") > cleanBefore);
        // The next batch follows.
        Assert.True(Until(colony, () => work.Jobs.Any(j => j.Kind == JobKind.Boil && j.Id != job.Id), 5));
    }

    [Fact]
    public void Boiling_waits_for_fuel_and_for_somewhere_to_put_the_water_and_says_why()
    {
        var (colony, carts, brook) = Fouled(Tweaked());
        var work = colony.Work!;
        Empty(colony, carts, "timber");
        work.SetMark(brook.Id, WaterMark.BoilFirst);
        colony.RunMinutes(1);
        Assert.DoesNotContain(work.Jobs, j => j.Kind == JobKind.Boil);
        Assert.Equal("waiting for timber to boil it with: none free in any store", work.WhyNotBoiled(brook.Id));
        var (x, y) = Ground.TileOf(brook.X, brook.Y);
        var lines = Inspect.Tile(colony, x, y).Lines;
        Assert.Equal("boil first", lines.Single(l => l.Label == "Marked").Text);
        Assert.Equal(work.WhyNotBoiled(brook.Id), lines.Single(l => l.Label == "Boiling").Text);
        // Timber again, but every store that takes water marked: nowhere to put it.
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, carts.Id, "timber", 5);
        work.SetMark(carts.Id, WaterMark.DoNotDrink);
        colony.RunMinutes(1);
        Assert.Equal("waiting for somewhere to put boiled water: no unmarked store with room takes water", work.WhyNotBoiled(brook.Id));
        // Unmarked: it boils, and the reason is gone.
        work.SetMark(carts.Id, WaterMark.None);
        colony.RunMinutes(1);
        Assert.Null(work.WhyNotBoiled(brook.Id));
        Assert.DoesNotContain(Inspect.Tile(colony, x, y).Lines, l => l.Label == "Boiling");
        Assert.Contains(work.Jobs, j => j.Kind == JobKind.Boil);
    }

    [Fact]
    public void Boiling_into_a_barrel_counts_against_its_room()
    {
        var (colony, carts, brook) = Fouled(Tweaked());
        var work = colony.Work!;
        var barrel = colony.Stocks.Open("Water barrel", Holder.Store, 20.5, 20.5);
        barrel.Accepts.UnionWith(Real.Drinkable);
        barrel.Capacity = 5;
        carts.Mark = WaterMark.DoNotDrink;                      // the barrel is the only place for boiled water
        work.SetMark(brook.Id, WaterMark.BoilFirst);            // (both marks before the colony asks, as at once)
        var job = Assert.Single(work.Jobs, j => j.Kind == JobKind.Boil);   // asked for as the mark is set
        Assert.Equal((barrel.Id, 5), (job.To, job.Amount));      // a batch only as big as the room
        Assert.Equal(0, work.RoomIn(barrel));
        Assert.DoesNotContain(work.Jobs, j => j.Kind == JobKind.Haul && j.To == barrel.Id);
        // A full barrel is no place for boiled water.
        work.Cancel(job.Id);
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, barrel.Id, "water", 5);
        colony.RunMinutes(1);
        Assert.DoesNotContain(work.Jobs, j => j.Kind == JobKind.Boil);
        Assert.Equal("waiting for somewhere to put boiled water: no unmarked store with room takes water", work.WhyNotBoiled(brook.Id));
    }

    [Fact]
    public void A_boiling_with_its_fuel_in_hand_is_not_cancelled_but_finished()
    {
        var (colony, _, brook) = Fouled(Tweaked());
        var work = colony.Work!;
        work.SetMark(brook.Id, WaterMark.BoilFirst);
        colony.RunMinutes(1);
        var job = work.Jobs.Single(j => j.Kind == JobKind.Boil);
        Assert.True(Until(colony, () => job.Step == Step.Working, 60), "never began to boil");
        work.Cancel(job.Id);
        Assert.Contains(job, work.Jobs);
        var to = colony.Stocks[job.To];
        int before = to.Count("water");
        Assert.True(Until(colony, () => !work.Jobs.Contains(job), 90), "never finished");
        Assert.True(to.Count("water") >= before + 10 - 2);
        // The cancelled boiling's fuel was burnt, not left in anyone's arms. (The water is still marked "boil first",
        // so a new boiling may already be under way, its fuel fetched: that one may hold timber.)
        Assert.All(work.Workers.Where(w => w.Job is not TaskId t || work.Jobs.First(j => j.Id == t).Kind != JobKind.Boil),
                   w => Assert.Equal(0, colony.Stocks[w.Arms].Count("timber")));
    }

    [Fact]
    public void A_new_fouling_has_its_sign_unseen_again()
    {
        // Fouled for a day: the sign is seen; the next morning it runs clean; the morning after it fouls again,
        // and that sign has not been seen.
        var (colony, _) = Founding.Found(7, On(1), Tweaked(lasts: 1, signs: 1), Map(40, 30));
        var work = colony.Work!;
        var source = work.SourceOf(work.Springs.Single());
        PastMorning(colony);
        Assert.True(Until(colony, () => source.SignSeen, 4 * 60), "nobody came to the water");
        PastMorning(colony);
        Assert.Null(source.FouledUntil);
        PastMorning(colony);
        Assert.NotNull(source.Sign);
        Assert.False(source.SignSeen);
    }

    [Fact]
    public void The_mark_commands_are_offered_on_water_and_set_the_mark()
    {
        var (colony, carts) = Founding.Found(7, On(1), Real, Map(40, 30));
        var brook = colony.Stocks[colony.Work!.Springs.Single()];
        var (x, y) = Ground.TileOf(brook.X, brook.Y);
        string Keys() => new string(Inspect.Commands(colony, x, y).Select(c => c.Key).Where(k => "KNU".Contains(k)).ToArray());
        Assert.Equal("KN", Keys());
        Inspect.Commands(colony, x, y).Single(c => c.Label == "Boil first").Run!();
        Assert.Equal(WaterMark.BoilFirst, brook.Mark);
        Assert.Equal("NU", Keys());
        Inspect.Commands(colony, x, y).Single(c => c.Label == "Do not drink").Run!();
        Assert.Equal(WaterMark.DoNotDrink, brook.Mark);
        Assert.Equal("do not drink", Inspect.Tile(colony, x, y).Lines.Single(l => l.Label == "Marked").Text);
        Assert.Equal("KU", Keys());
        Inspect.Commands(colony, x, y).Single(c => c.Label == "Drink freely").Run!();
        Assert.Equal(WaterMark.None, brook.Mark);
        Assert.DoesNotContain(Inspect.Tile(colony, x, y).Lines, l => l.Label == "Marked");
        // The carts too; but not a tile with no store.
        var (cx, cy) = Ground.TileOf(carts.X, carts.Y);
        Assert.Contains(Inspect.Commands(colony, cx, cy), c => c.Key == 'K');
        Assert.DoesNotContain(Inspect.Commands(colony, 2, 2), c => c.Key == 'K');
    }

    [Fact]
    public void Drinking_fouled_water_may_bring_an_upset_stomach_that_shows_and_passes()
    {
        var (colony, carts, brook) = Fouled(Tweaked(illness: 1, onset: 6, ill: 24));
        var work = colony.Work!;
        work.SetMark(carts.Id, WaterMark.DoNotDrink);           // the brook is all there is to drink
        var ada = work.Workers.First();
        ada.Needs["thirst"].Level = 70;
        Assert.True(Until(colony, () => ada.Illness != null, 60), "Ada never drank the brook's water");
        long drank = colony.Now.Tick;
        var illness = ada.Illness!;
        Assert.Equal(("Upset stomach", drank + 6 * Calendar.TicksPerHour, drank + 30 * Calendar.TicksPerHour), (illness.Name, illness.Shows, illness.Ends));
        // Not shown yet: the player sees nothing, the trace does.
        Assert.False(work.Ill(ada));
        Assert.DoesNotContain(Inspect.Worker(colony, ada.Id).Lines, l => l.Label is "Unwell" or "Trace");
        Assert.Contains(Inspect.Worker(colony, ada.Id, trace: true).Lines, l => l.Label == "Trace" && l.Text.StartsWith("caught upset stomach"));
        // Drinking again does not make it worse, nor start it over.
        work.Expose(ada);
        Assert.Same(illness, ada.Illness);
        // It shows, and the player is told.
        Assert.True(Until(colony, () => colony.Now.Tick > illness.Shows, 7 * 60));
        Assert.True(work.Ill(ada));
        Assert.Single(work.Told, n => n.Tick == illness.Shows && n.Text == ada.Name + " is ill: upset stomach.");
        Assert.StartsWith("Upset stomach: working slower", Inspect.Worker(colony, ada.Id).Lines.Single(l => l.Label == "Unwell").Text);
        // It passes by itself.
        Assert.True(Until(colony, () => colony.Now.Tick > illness.Ends, 25 * 60));
        Assert.Null(ada.Illness);
        Assert.False(work.Ill(ada));
        Assert.Single(work.Told, n => n.Text == ada.Name + " is ill: upset stomach.");
        Assert.Contains(work.Told, n => n.Tick == illness.Ends && n.Text == ada.Name + " is well again.");
    }

    [Fact]
    public void Clean_water_never_makes_anyone_ill_and_fouled_water_only_by_the_chance()
    {
        // Clean water: never, even at a chance of one.
        var (colony, _) = Founding.Found(7, On(1), Tweaked(foulEvery: 1e9, illness: 1), Map(40, 30));
        colony.RunMinutes(2 * 24 * 60);
        Assert.All(colony.Work!.Workers, w => Assert.Null(w.Illness));
        // Fouled water at a chance of none: never.
        var (calm, carts, _) = Fouled(Tweaked(illness: 0));
        calm.Work!.SetMark(carts.Id, WaterMark.DoNotDrink);
        calm.RunMinutes(24 * 60);
        Assert.All(calm.Work.Workers, w => Assert.Null(w.Illness));
    }

    [Fact]
    public void An_ill_colonist_loses_three_seconds_in_ten_and_grows_thirsty_and_tired_half_again_as_fast()
    {
        var (colony, _) = Founding.Found(7, On(1), Tweaked(illness: 1, onset: 6, ill: 24), Map(40, 30));
        var work = colony.Work!;
        var ada = work.Workers.First();
        Assert.Equal(10, Work.Effort(ada, 1, 10));               // at the usual pace: every second counts
        Assert.Equal(1, work.Faster(ada, colony.Content!.Thirst));
        work.Expose(ada);
        colony.RunMinutes(6 * 60);
        Assert.True(work.Ill(ada));
        ada.Carry = 0;
        double pace = colony.Content!.UpsetStomach.Work;         // ill, at the usual skill (step 23a): 0.7
        Assert.Equal(7, Work.Effort(ada, pace, 10));             // 3 seconds in 10 lost
        Assert.Equal(70, Enumerable.Range(0, 100).Sum(_ => Work.Effort(ada, pace, 1)));   // the same, a second at a time
        Assert.Equal(35, Work.Effort(ada, pace, 50));
        Assert.Equal(0, Work.Effort(ada, pace, 0));
        Assert.InRange(ada.Carry, 0, 1);
        Assert.Equal(1.5, work.Faster(ada, colony.Content!.Thirst));
        Assert.Equal(1.5, work.Faster(ada, colony.Content.Fatigue));
        Assert.Equal(1, work.Faster(ada, colony.Content.Hunger));
    }

    [Fact]
    public void An_ill_colonist_grows_thirsty_and_tired_half_again_as_fast()
    {
        var (colony, _) = Founding.Found(7, On(1, hour: 9), Tweaked(illness: 1, onset: 0, ill: 48), Map(40, 30));
        var work = colony.Work!;
        var (ill, well) = (work.Workers.First(), work.Workers.Skip(1).First());
        work.Expose(ill);
        colony.RunMinutes(1);
        Assert.True(work.Ill(ill));
        foreach (var w in new[] { ill, well })
        {
            w.Needs["thirst"].Level = 0;
            w.Needs["fatigue"].Level = 0;
        }
        colony.RunMinutes(60);                                         // awake, below peckish: nothing drunk, nobody rests
        Assert.Equal(1.5 * well.Needs["thirst"].Level, ill.Needs["thirst"].Level, 6);
        Assert.Equal(1.5 * well.Needs["fatigue"].Level, ill.Needs["fatigue"].Level, 6);
        Assert.True(well.Needs["thirst"].Level > 0);
    }

    [Fact]
    public void An_ill_colonist_takes_longer_over_a_job()
    {
        // The same boiling (thirty minutes) by a well and an ill colonist: the ill one takes 30 / 0.7, about 43.
        // Measured in seconds, and seen at her first think after the boiling is done (2 to 7 seconds apart).
        int Boiling(bool ill)
        {
            var (colony, carts, brook) = Fouled(Tweaked(illness: 1, onset: 0, ill: 48));
            var work = colony.Work!;
            foreach (var w in work.Workers.Skip(1))
                work.Stop(w.Id);
            var ada = work.Workers.First();
            if (ill)
            {
                work.Expose(ada);
                colony.RunMinutes(1);
                Assert.True(work.Ill(ada));
            }
            work.SetMark(brook.Id, WaterMark.BoilFirst);
            Assert.True(EverySecondUntil(colony, () => work.Jobs.Any(j => j.Kind == JobKind.Boil && j.Step == Step.Working), 120 * 60), "never began to boil");
            var job = work.Jobs.Single(j => j.Kind == JobKind.Boil && j.Step == Step.Working);
            int seconds = 0;
            while (job.Step == Step.Working)
            {
                colony.Run(1);
                seconds++;
            }
            return seconds;
        }
        Assert.InRange(Boiling(false), 30 * 60, 30 * 60 + 7);
        Assert.InRange(Boiling(true), 30 * 60 / 0.7 - 7, 30 * 60 / 0.7 + 7);
    }

    [Fact]
    public void A_save_keeps_the_fouling_the_marks_the_boiling_the_illness_and_the_news()
    {
        var (colony, carts, brook) = Fouled(Tweaked(illness: 1));
        var work = colony.Work!;
        work.SetMark(carts.Id, WaterMark.DoNotDrink);
        var ada = work.Workers.First();
        ada.Needs["thirst"].Level = 70;
        ada.Needs["hygiene"].Level = 70;
        Assert.True(Until(colony, () => ada.Illness != null && work.Told.Count > 0, 120));
        work.SetMark(brook.Id, WaterMark.BoilFirst);
        work.SetMark(carts.Id, WaterMark.None);
        Assert.True(Until(colony, () => work.Jobs.Any(j => j.Kind == JobKind.Boil && j.Step == Step.Working), 120), "never began to boil");
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, carts.Id, "foul_water", 3);
        ada.Carry = 0.25;
        string text = ColonySave.Text(colony);
        var loaded = ColonySave.Read(text, colony.Content!);
        Assert.Equal(0.25, loaded.Work!.Workers.First().Carry);
        Assert.Equal(text, ColonySave.Text(loaded));
        Assert.Equal(work.Told, loaded.Work!.Told);
        Assert.Equal(ada.Illness, loaded.Work.Workers.First().Illness);
        Assert.Equal(WaterMark.BoilFirst, loaded.Stocks[brook.Id].Mark);
        var source = loaded.Work.SourceOf(brook.Id);
        Assert.Equal((work.SourceOf(brook.Id).FouledUntil, work.SourceOf(brook.Id).Sign, work.SourceOf(brook.Id).SignSeen),
                     (source.FouledUntil, source.Sign, source.SignSeen));
        colony.RunMinutes(3 * 24 * 60);
        loaded.RunMinutes(3 * 24 * 60);
        Assert.Equal(ColonySave.Fingerprint(colony), ColonySave.Fingerprint(loaded));
    }

    public static IEnumerable<object[]> BrokenSaves()
    {
        object[] Case(Action<JsonObject> change, string reason)
        {
            var (colony, _, brook) = Fouled(Tweaked(illness: 1));
            var work = colony.Work!;
            work.SetMark(brook.Id, WaterMark.BoilFirst);
            work.Expose(work.Workers.First());
            colony.RunMinutes(1);                                       // a boiling job is made
            var save = ColonySave.Write(colony);
            change(save);
            return new object[] { save.ToJsonString(), reason };
        }
        JsonObject First(JsonObject s) => s["work"]!["workers"]![0]!.AsObject();
        JsonObject Boil(JsonObject s) => s["work"]!["jobs"]!.AsArray().Single(j => j!["boiling"] is not null)!.AsObject();
        yield return Case(s => First(s)["illness"]![0] = "Plague", "illness is not [name, shows, ends] of an illness the content knows");
        yield return Case(s => First(s)["illness"] = "ill", "illness is not [name, shows, ends]");
        yield return Case(s => First(s)["illness"]![2] = 0, "illness ends before it shows");
        yield return Case(s => First(s)["carry"] = 1, "carry is 1, which should be from 0 to below 1");
        yield return Case(s => First(s)["carry"] = -0.5, "carry is -0.5");
        yield return Case(s => s["inventories"]![0]!["mark"] = "Maybe", "mark is not a mark (None, BoilFirst, DoNotDrink)");
        yield return Case(s => s["inventories"]![0]!["mark"] = "7", "mark is not a mark");
        yield return Case(s => s["work"]!["news"] = new JsonArray(5), "work.news holds something that is not [tick, text]");
        yield return Case(s => s["work"]!["news"] = "none", "work.news is not a list");
        yield return Case(s => Boil(s)["boiling"] = null, "a boiling, and only a boiling, names the water it boils");
    }

    [Theory]
    [MemberData(nameof(BrokenSaves))]
    public void A_broken_19b_save_is_refused(string text, string reason) =>
        Assert.Contains(reason, Assert.Throws<ColonySaveException>(() => ColonySave.Read(text, Tweaked(illness: 1))).Message);
}
