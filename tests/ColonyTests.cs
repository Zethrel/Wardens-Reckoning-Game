// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 12: the colony's clock and calendar, identifiers, dice and save. The design library's first-playable
/// scenarios FP-01 (time, identity and scheduling) and FP-07 (determinism) are the two marked as such; the rest
/// check each rule on its own. FP-01's third part, work that advances only in its schedule's hours, waits for
/// schedules (step 14).
/// </summary>
public class ColonyTests
{
    static readonly Moment Start = new(Calendar.TickOf(301, 1, 1, 0, 0));

    // ---- The calendar -------------------------------------------------------------------------------------

    [Fact]
    public void A_year_is_336_days_and_every_month_starts_on_Monday()
    {
        Assert.Equal(1440, Calendar.TicksPerDay);
        Assert.Equal(483_840, Calendar.TicksPerYear);
        for (int year = 1; year <= 3; year++)
            for (int month = 1; month <= 12; month++)
            {
                var first = new Moment(Calendar.TickOf(year, month, 1));
                Assert.Equal(0, first.Weekday);                       // Monday
                Assert.Equal(6, first.After(27 * Calendar.TicksPerDay).Weekday);   // the 28th is a Sunday
                Assert.Equal((month - 1) / 3, first.Season);
            }
    }

    [Fact]
    public void A_tick_turns_back_into_its_date()
    {
        var m = new Moment(Calendar.TickOf(12, 7, 14, 6, 30));
        Assert.Equal((12, 7, 14, 6, 30), (m.Year, m.Month, m.DayOfMonth, m.Hour, m.Minute));
        Assert.Equal(6 * 28 + 14, m.DayOfYear);
        Assert.Equal(2, m.Season);                                    // autumn
        Assert.Equal("14th of Orven, Year 12, 06:30", m.ToString());
    }

    [Fact]
    public void The_last_minute_of_a_year_runs_into_the_next()
    {
        var last = new Moment(Calendar.TickOf(5, 12, 28, 23, 59));
        var next = last.After(1);
        Assert.Equal((6, 1, 1, 0, 0), (next.Year, next.Month, next.DayOfMonth, next.Hour, next.Minute));
        Assert.Equal(0, next.Weekday);
        Assert.Equal(0, next.Season);
    }

    [Theory]
    [InlineData(0, 1, 1, 0, 0)]
    [InlineData(1, 13, 1, 0, 0)]
    [InlineData(1, 1, 29, 0, 0)]
    [InlineData(1, 1, 1, 24, 0)]
    [InlineData(1, 1, 1, 0, 60)]
    [InlineData(1, 0, 1, 0, 0)]
    public void An_impossible_date_is_refused(int year, int month, int day, int hour, int minute) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.TickOf(year, month, day, hour, minute));

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    public void Days_are_written_as_ordinals(int n, string written) => Assert.Equal(written, Moment.Ordinal(n));

    // ---- Speed: real time into ticks -----------------------------------------------------------------------

    [Fact]
    public void A_day_lasts_twelve_real_minutes_at_normal_speed()
    {
        var pacer = new Pacer();
        Assert.Equal(2, pacer.TicksFor(1.0));
        long ticks = 0;
        for (int second = 0; second < 12 * 60; second++)
            ticks += pacer.TicksFor(1.0);
        Assert.Equal(Calendar.TicksPerDay, ticks);
    }

    [Theory]
    [InlineData(Speed.Paused, 0)]
    [InlineData(Speed.Normal, 2)]
    [InlineData(Speed.Double, 4)]
    [InlineData(Speed.Triple, 6)]
    public void Each_speed_runs_its_ticks_a_second(Speed speed, int perSecond)
    {
        var pacer = new Pacer { Speed = speed };
        Assert.Equal(perSecond * 10, Enumerable.Range(0, 10).Sum(_ => pacer.TicksFor(1.0)));
    }

    [Fact]
    public void The_frame_rate_does_not_change_how_many_ticks_run()
    {
        // 10 real seconds drawn at 60, 144 or 7 frames a second: the same 20 ticks.
        foreach (int fps in new[] { 60, 144, 7 })
        {
            var pacer = new Pacer();
            int ticks = Enumerable.Range(0, fps * 10).Sum(_ => pacer.TicksFor(1.0 / fps));
            Assert.Equal(20, ticks);
        }
    }

    [Fact]
    public void A_stalled_frame_runs_at_most_sixty_ticks_and_drops_the_rest()
    {
        var pacer = new Pacer { Speed = Speed.Triple };
        Assert.Equal(Pacer.MostTicksAFrame, pacer.TicksFor(60.0));   // 360 owed
        Assert.Equal(2 * 3, pacer.TicksFor(1.0));                     // the rest was not saved up
    }

    // ---- Identifiers --------------------------------------------------------------------------------------

    [Fact]
    public void Each_family_counts_on_its_own_from_one()
    {
        var ids = new IdCounters();
        Assert.Equal(new ColonistId(1), ids.Next<ColonistId>());
        Assert.Equal(new ColonistId(2), ids.Next<ColonistId>());
        Assert.Equal(new TaskId(1), ids.Next<TaskId>());
        Assert.Equal(new ColonistId(3), ids.Next<ColonistId>());
        Assert.Equal(new Dictionary<string, long> { ["colonist"] = 3, ["task"] = 1 }, ids.Last);
    }

    [Fact]
    public void A_counter_cannot_be_put_back_below_zero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdCounters().Restore(new[] { KeyValuePair.Create("task", -1L) }));

    // ---- FP-01: time, identity and scheduling --------------------------------------------------------------

    /// <summary>The same ordered commands, given to a colony: runs of time, new identifiers, rolls of two systems.</summary>
    static List<string> Play(ColonyState colony, IEnumerable<(string What, int Amount)> commands)
    {
        var seen = new List<string>();
        foreach (var (what, amount) in commands)
        {
            switch (what)
            {
                case "run":
                    colony.Run(amount);
                    break;
                case "colonist":
                    for (int i = 0; i < amount; i++)
                        seen.Add("c" + colony.Ids.Next<ColonistId>().Value);
                    break;
                case "task":
                    for (int i = 0; i < amount; i++)
                        seen.Add("t" + colony.Ids.Next<TaskId>().Value);
                    break;
                default:
                    for (int i = 0; i < amount; i++)
                        seen.Add(what + colony.DiceFor(what).Next());
                    break;
            }
            seen.Add("@" + colony.Now.Tick);
        }
        return seen;
    }

    static readonly (string, int)[] Commands =
    {
        ("run", 1), ("colonist", 5), ("weather", 3), ("run", 59), ("task", 4), ("births", 2),
        ("run", Calendar.TicksPerDay), ("colonist", 2), ("weather", 1), ("run", 30 * Calendar.TicksPerDay), ("task", 1),
    };

    [Fact]
    public void FP01_the_same_commands_give_the_same_clock_and_identifiers()
    {
        var a = Play(new ColonyState(42, Start), Commands);
        var b = Play(new ColonyState(42, Start), Commands);
        Assert.Equal(a, b);
        // The clock moved exactly as far as it was told to.
        Assert.Equal("@" + (Start.Tick + 1 + 59 + Calendar.TicksPerDay + 30 * Calendar.TicksPerDay), a[^1]);
    }

    [Fact]
    public void FP01_identifiers_are_unique_and_carry_on_after_a_save()
    {
        var colony = new ColonyState(42, Start);
        var first = Enumerable.Range(0, 50).Select(_ => colony.Ids.Next<TaskId>()).ToList();
        var loaded = ColonySave.Read(ColonySave.Text(colony));
        var after = Enumerable.Range(0, 50).Select(_ => loaded.Ids.Next<TaskId>()).ToList();
        var all = first.Concat(after).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(new TaskId(51), after[0]);
    }

    // ---- FP-07: determinism -------------------------------------------------------------------------------

    [Fact]
    public void FP07_the_same_seed_and_commands_give_the_same_fingerprints()
    {
        var a = new ColonyState(42, Start);
        var b = new ColonyState(42, Start);
        var fingerprintsA = new List<string>();
        var fingerprintsB = new List<string>();
        foreach (var command in Commands)
        {
            Play(a, new[] { command });
            Play(b, new[] { command });
            fingerprintsA.Add(ColonySave.Fingerprint(a));
            fingerprintsB.Add(ColonySave.Fingerprint(b));
        }
        Assert.Equal(fingerprintsA, fingerprintsB);
        Assert.Equal(fingerprintsA.Count, fingerprintsA.Distinct().Count());   // every checkpoint differs from the last
    }

    [Fact]
    public void FP07_saving_and_loading_halfway_changes_nothing()
    {
        // One colony plays all the commands; another is saved to a file and loaded again halfway.
        var straight = new ColonyState(42, Start);
        Play(straight, Commands);
        var halves = new ColonyState(42, Start);
        Play(halves, Commands.Take(5));
        string path = Path.Combine(Scratch.Root, "fp07.json");
        ColonySave.Save(path, halves);
        var loaded = ColonySave.Load(path);
        Play(loaded, Commands.Skip(5));
        Assert.Equal(ColonySave.Fingerprint(straight), ColonySave.Fingerprint(loaded));
    }

    [Fact]
    public void FP07_another_seed_rolls_otherwise()
    {
        var a = new ColonyState(42, Start);
        var b = new ColonyState(43, Start);
        Assert.NotEqual(Play(a, Commands), Play(b, Commands));
    }

    [Fact]
    public void One_system_rolling_more_never_changes_another()
    {
        var a = new ColonyState(42, Start);
        var b = new ColonyState(42, Start);
        for (int i = 0; i < 100; i++)
            b.DiceFor("births").Next();
        Assert.Equal(a.DiceFor("weather").Next(), b.DiceFor("weather").Next());
    }

    [Fact]
    public void The_colonys_dice_differ_from_the_historys_of_the_same_name() =>
        Assert.NotEqual(new Sim.Dice(42, "weather").Next(), new ColonyState(42, Start).DiceFor("weather").Next());

    // ---- The save -----------------------------------------------------------------------------------------

    static JsonObject Saved() => ColonySave.Write(Played());

    static ColonyState Played()
    {
        var colony = new ColonyState(-9_000_000_000_000_000_000, Start);   // beyond JSON's exact numbers
        Play(colony, Commands);
        return colony;
    }

    [Fact]
    public void A_save_keeps_everything_exactly()
    {
        var colony = Played();
        var loaded = ColonySave.Read(ColonySave.Text(colony));
        Assert.Equal(colony.Seed, loaded.Seed);
        Assert.Equal(colony.Founded, loaded.Founded);
        Assert.Equal(colony.Now, loaded.Now);
        Assert.Equal(colony.Ids.Last, loaded.Ids.Last);
        Assert.Equal(colony.AllDice.Keys, loaded.AllDice.Keys);
        Assert.Equal(ColonySave.Text(colony), ColonySave.Text(loaded));
    }

    public static IEnumerable<object[]> BrokenSaves()
    {
        string text(Action<JsonObject> change)
        {
            var save = Saved();
            change(save);
            return save.ToJsonString();
        }
        yield return new object[] { "{ not json", "broken JSON" };
        yield return new object[] { "[1, 2]", "no JSON object" };
        yield return new object[] { text(s => s["format"] = "wardens-reckoning-save"), "not a colony save of this game" };
        yield return new object[] { text(s => s.Remove("format")), "\"format\" is missing" };
        yield return new object[] { text(s => s["version"] = 0), "older saves are not converted" };
        yield return new object[] { text(s => s["version"] = 12), "made by a newer game" };
        yield return new object[] { text(s => s["version"] = "2"), "\"version\" is not a whole number" };
        yield return new object[] { text(s => s["seed"] = "forty-two"), "seed is not a whole number" };
        yield return new object[] { text(s => s["seed"] = 42), "\"seed\" is not text" };
        yield return new object[] { text(s => s.Remove("now")), "\"now\" is missing" };
        yield return new object[] { text(s => s["now"] = 0), "clock is impossible" };
        yield return new object[] { text(s => s["ids"] = 3), "\"ids\" is not a table" };
        yield return new object[] { text(s => s["ids"]!["task"] = -1), "identifier counter is wrong" };
        yield return new object[] { text(s => s["ids"]!["task"] = "one"), "\"ids.task\" is not a whole number" };
        yield return new object[] { text(s => s["dice"]!["weather"] = new JsonArray("1", "2")), "not four numbers" };
        yield return new object[] { text(s => s["dice"]!["weather"]![0] = "x"), "not a number" };
    }

    [Theory]
    [MemberData(nameof(BrokenSaves))]
    public void A_broken_or_foreign_save_is_refused_with_its_reason(string text, string reason)
    {
        var e = Assert.Throws<ColonySaveException>(() => ColonySave.Read(text));
        Assert.Contains(reason, e.Message);
    }

    [Fact]
    public void A_refused_save_is_left_as_it_was()
    {
        string path = Path.Combine(Scratch.Root, "refused.json");
        string text = Saved().ToJsonString().Replace("\"version\":11", "\"version\":99");
        File.WriteAllText(path, text);
        var e = Assert.Throws<ColonySaveException>(() => ColonySave.Load(path));
        Assert.StartsWith(path + ": colony save version 99", e.Message);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void A_missing_file_is_refused_with_a_reason() =>
        Assert.Contains("cannot be read", Assert.Throws<ColonySaveException>(
            () => ColonySave.Load(Path.Combine(Scratch.Root, "no-such-save.json"))).Message);

    [Fact]
    public void Saving_over_an_old_save_leaves_no_half_written_file()
    {
        string path = Path.Combine(Scratch.Root, "over.json");
        ColonySave.Save(path, new ColonyState(1, Start));
        ColonySave.Save(path, Played());
        Assert.False(File.Exists(path + ".writing"));
        Assert.Equal(ColonySave.Text(Played()), File.ReadAllText(path));
    }

    // ---- The autosave -------------------------------------------------------------------------------------

    [Fact]
    public void The_autosave_comes_once_each_morning()
    {
        var colony = new ColonyState(42, Start);    // midnight
        var mornings = new List<long>();
        colony.Run(3 * Calendar.TicksPerDay, c =>
        {
            if (Autosave.MorningPassed(c.Now.After(-1), c.Now))
                mornings.Add(c.Now.Tick - Start.Tick);
        });
        long six = 6 * 60;
        Assert.Equal(new[] { six, six + 1440, six + 2 * 1440 }, mornings);
    }

    [Fact]
    public void A_long_jump_over_a_morning_still_counts_it()
    {
        var evening = new Moment(Calendar.TickOf(301, 1, 1, 20, 0));
        Assert.True(Autosave.MorningPassed(evening, evening.After(12 * 60)));   // to 08:00 next day
        Assert.False(Autosave.MorningPassed(evening, evening.After(9 * 60)));   // to 05:00
        var six = new Moment(Calendar.TickOf(301, 1, 2, 6, 0));
        Assert.False(Autosave.MorningPassed(six, six.After(60)));               // already past this morning
        Assert.False(Autosave.MorningPassed(six, six));
    }

    [Fact]
    public void The_slots_take_turns()
    {
        var slots = Enumerable.Range(0, 6).Select(d => Autosave.SlotFor(Start.After(d * (long)Calendar.TicksPerDay))).ToList();
        Assert.Equal(slots.Take(3).Order().ToList(), new List<int> { 1, 2, 3 });
        Assert.Equal(slots.Take(3), slots.Skip(3));
        Assert.EndsWith("autosave-2.json", Autosave.PathOf("saves", 2));
    }

    [Fact]
    public void Time_does_not_run_backwards() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColonyState(42, Start).Run(-1));
}
