// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using ColonyState = WardensReckoning.Colony.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 16: the whole loop saved and loaded (FP-08), and saves that fail or are broken handled safely (FP-09; library
/// Review 35): the checksum, the content fingerprint, writing beside and reading back, the backup, and the colony
/// session with its morning autosave.
/// </summary>
public class ColonySaveTests
{
    static readonly Content Real = Content.Load(Path.Combine(AppContext.BaseDirectory, "content"));
    static readonly Moment Arrival = new(Calendar.TickOf(1, 1, 1, 8, 0));

    /// <summary>A folder of its own for one test.</summary>
    static string Folder()
    {
        string path = Path.Combine(Scratch.Root, "save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>The founders' first day, with all its decisions made at the start: the house planned with a
    /// stockpile inside, the bedrolls laid inside it, the carts being emptied, a quern grinding.</summary>
    static ColonyState FirstDay()
    {
        var (colony, carts) = Founding.Found(7, Arrival, Real, 24, 24);
        var work = colony.Work!;
        work.PlanTemplate("small_house", 27, 20);
        work.AddStockpile("House store", 28, 21, 4, 3);
        int i = 0;
        foreach (var bed in work.Bedrolls.ToList())
            work.MoveBedroll(bed.Id, 28.5 + i++, 24.5);
        carts.Emptying = true;
        var quern = work.AddSite("quern", 20.5, 26.5);
        work.Order(quern.Id, "grind_wheat", 2);
        return colony;
    }

    // ---- FP-08: the whole loop, saved and carried on -------------------------------------------------------------

    [Fact]
    public void FP08_saved_at_any_hour_the_colony_goes_on_exactly_as_if_it_had_never_been_saved()
    {
        // One colony plays its first day and the next, and is saved to a file and loaded again every hour (on the
        // half hour), and also the first minute of each hour someone is carrying goods (a load is in someone's arms
        // for only a minute or so). Each loaded copy then plays on to the same moment. All of them must end exactly alike, and
        // like the colony that was never saved: same ledger, same identities, same everything.
        var colony = FirstDay();
        string folder = Folder();
        var copies = new List<ColonyState>();
        bool carrying = false, claimsOpen = false, building = false, personal = false, grinding = false;
        long end = Arrival.Tick + 40 * Calendar.TicksPerHour;
        long carrySavedHour = -1;
        colony.RunMinutes((end - Arrival.Tick) / Calendar.TicksPerMinute, c =>
        {
            var work = c.Work!;
            long hour = c.Now.Tick / Calendar.TicksPerHour;
            bool carries = work.Jobs.Any(j => j.Step == Step.Carrying && c.Stocks[work.Worker(j.Worker!.Value).Arms].Goods.Count > 0);
            bool carrySave = carries && carrySavedHour != hour;
            if (c.Now.MinuteOfDay % 60 != 30 && !carrySave)
                return;
            if (carrySave)
                carrySavedHour = hour;
            carrying |= carries;
            claimsOpen |= work.Jobs.Any(j => j.Step == Step.Open && j.Claims.Count > 0);
            building |= work.Jobs.Any(j => j.Kind == JobKind.Build && j.Step == Step.Working);
            grinding |= work.Jobs.Any(j => j.Kind == JobKind.Craft && j.Step == Step.Working);
            personal |= work.Workers.Any(w => w.Personal != null);
            string path = Path.Combine(folder, "hour-" + c.Now.Tick + ".json");
            ColonySave.Save(path, c);
            var copy = ColonySave.Load(path, Real);
            Assert.Equal(File.ReadAllText(path), ColonySave.Text(copy));    // nothing lost or changed on the way
            copies.Add(copy);
        });
        // The saves were made with goods in someone's arms, claims on goods waiting to be carried, a building and a
        // grinding half done, and someone eating, drinking or sleeping.
        Assert.True(carrying && claimsOpen && building && grinding && personal,
                    "carrying " + carrying + ", claims " + claimsOpen + ", building " + building + ", grinding " + grinding + ", personal " + personal);
        Assert.True(copies.Count > 40, copies.Count + " saves");
        string fingerprint = ColonySave.Fingerprint(colony);
        foreach (var copy in copies)
        {
            copy.RunMinutes((end - copy.Now.Tick) / Calendar.TicksPerMinute);
            Assert.Equal(fingerprint, ColonySave.Fingerprint(copy));
        }
        // Every operation in the ledger happened once.
        Assert.Equal(colony.Stocks.Ledger.Count, colony.Stocks.Ledger.Select(e => (e.Operation, e.Change, e.Good, e.Amount)).Distinct().Count());
        Assert.Equal(Real.Templates["small_house"].Pieces.Count, colony.Ground!.Pieces.Count());
    }

    [Fact]
    public void FP08_identities_go_on_from_where_they_were()
    {
        var colony = FirstDay();
        colony.RunMinutes(300);
        var copy = ColonySave.Read(ColonySave.Text(colony), Real);
        Assert.Equal(colony.Ids.Last, copy.Ids.Last);
        Assert.True(colony.Ids.Last.Count >= 6);                                // colonists, buildings, inventories, tasks...
        // The next of each is the same number in both: nothing given twice, nothing skipped.
        Assert.Equal(colony.Ids.Next<TaskId>(), copy.Ids.Next<TaskId>());
        Assert.Equal(colony.Ids.Next<OperationId>(), copy.Ids.Next<OperationId>());
    }

    [Fact]
    public void FP08_a_save_made_after_pieces_are_finished_or_cancelled_loads()
    {
        // Found in step 16: a finished or cancelled building site's pile is closed, but the ledger still names it.
        // Saves now keep the numbers of closed inventories, so such a save loads.
        var colony = FirstDay();
        var work = colony.Work!;
        var cancelled = work.Plan("floor", 40, 40).Project!;
        colony.RunMinutes(30);
        work.CancelBuild(cancelled.Id);
        colony.RunMinutes(8 * 60);
        Assert.NotEmpty(colony.Stocks.Closed);
        var copy = ColonySave.Read(ColonySave.Text(colony), Real);
        Assert.Equal(colony.Stocks.Closed, copy.Stocks.Closed);
        Assert.Equal(ColonySave.Text(colony), ColonySave.Text(copy));
    }

    public static IEnumerable<object[]> BrokenClosed()
    {
        var colony = FirstDay();
        colony.RunMinutes(8 * 60);
        string good = ColonySave.Text(colony);
        object[] Case(Action<JsonObject> change, string reason)
        {
            var save = JsonNode.Parse(good)!.AsObject();
            change(save);
            return new object[] { save.ToJsonString(), reason };
        }
        long open = colony.Stocks.Inventories.First().Id.Value;
        yield return Case(s => s["closed_inventories"]!.AsArray().Add(open), "which was never given out, is still open, or is named twice");
        yield return Case(s => s["closed_inventories"]!.AsArray().Add(99999), "closed_inventories names 99999");
        yield return Case(s => s["closed_inventories"] = new JsonArray(), "which does not exist");   // the ledger names them
    }

    [Theory]
    [MemberData(nameof(BrokenClosed))]
    public void A_save_with_broken_closed_inventories_is_refused(string text, string reason) =>
        Assert.Contains(reason, Assert.Throws<ColonySaveException>(() => ColonySave.Read(text, Real)).Message);

    // ---- FP-09: the checksum and the content fingerprint ----------------------------------------------------------

    [Fact]
    public void FP09_a_save_changed_outside_the_game_is_only_loaded_if_the_player_says_so()
    {
        var colony = FirstDay();
        colony.RunMinutes(60);
        string text = ColonySave.Text(colony);
        var save = JsonNode.Parse(text)!.AsObject();
        string first = save["work"]!["workers"]![0]!["name"]!.ToString();
        save["work"]!["workers"]![0]!["name"] = "Someone Else";                 // still a good save, but not the game's
        string edited = save.ToJsonString();
        var refused = Assert.Throws<ColonySaveChangedException>(() => ColonySave.Read(edited, Real));
        Assert.Contains("changed outside the game, or is damaged", refused.Message);
        var opened = ColonySave.Opening(edited, Real, acceptChanged: true);
        Assert.Equal("Someone Else", opened.Colony.Work!.Workers.First().Name);
        Assert.Contains(opened.Warnings, w => w.Contains("changed outside the game"));
        // A save without a checksum counts as changed.
        save["work"]!["workers"]![0]!["name"] = first;
        save.Remove("checksum");
        Assert.Throws<ColonySaveChangedException>(() => ColonySave.Read(save.ToJsonString(), Real));
        // Only the spacing changed (written without indents): the same save.
        var compact = JsonNode.Parse(text)!.ToJsonString();
        Assert.Empty(ColonySave.Opening(compact, Real).Warnings);
    }

    [Fact]
    public void FP09_a_save_made_with_other_content_loads_with_a_warning()
    {
        var colony = FirstDay();
        colony.RunMinutes(60);
        string text = ColonySave.Text(colony);
        Assert.Empty(ColonySave.Opening(text, Real).Warnings);
        // The same content with walking a little quicker.
        var files = Content.Files.ToDictionary(f => f, f => (string?)File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", f)));
        files["colony.json"] = files["colony.json"]!.Replace("\"walk_m_per_minute\": 70", "\"walk_m_per_minute\": 75");
        var tweaked = Content.Read(files);
        Assert.NotEqual(Real.Fingerprint, tweaked.Fingerprint);
        var opened = ColonySave.Opening(text, tweaked);
        Assert.Contains("has changed since this save was made", Assert.Single(opened.Warnings));
        Assert.Equal(75, opened.Colony.Content!.WalkMetresPerMinute);
        // Windows line endings in the content files do not change the fingerprint.
        var crlf = Content.Files.ToDictionary(f => f, f => (string?)File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", f)).Replace("\n", "\r\n"));
        Assert.Equal(Real.Fingerprint, Content.Read(crlf).Fingerprint);
    }

    // ---- FP-09: writing never puts a bad save in place of a good one ----------------------------------------------

    [Fact]
    public void FP09_a_save_that_cannot_be_written_leaves_the_old_one_as_it_was()
    {
        string folder = Folder();
        string path = Path.Combine(folder, "colony.json");
        var colony = FirstDay();
        ColonySave.Save(path, colony);
        string before = Sha(path);
        colony.RunMinutes(60);
        // A writing that comes out broken (as a failing disk might): read back, refused, nothing replaced.
        var broken = Assert.Throws<ColonySaveException>(() => ColonySave.Save(path, colony, true, text => text[..(text.Length / 2)]));
        Assert.Contains("could not be saved", broken.Message);
        Assert.Contains("The save already there is as it was.", broken.Message);
        // A writing that reads but not as the game wrote it: also refused.
        Assert.Throws<ColonySaveException>(() => ColonySave.Save(path, colony, true, text => text.Replace("\"Ada\"", "\"Adda\"").Replace("\"Bram\"", "\"Brahm\"")
                                                                                     .Replace("\"Cael\"", "\"Kael\"").Replace("\"Dagny\"", "\"Dagnie\"")
                                                                                     .Replace("\"Eirik\"", "\"Erik\"").Replace("\"Freya\"", "\"Freja\"")));
        Assert.Equal(before, Sha(path));
        Assert.False(File.Exists(path + ".writing"));
        Assert.False(File.Exists(path + ".bak"));
        // A folder that is not there.
        var nowhere = Assert.Throws<ColonySaveException>(() => ColonySave.Save(Path.Combine(folder, "no-such-folder", "c.json"), colony));
        Assert.Contains("Nothing was saved.", nowhere.Message);
        // A half-written file left by a crash is simply written over.
        File.WriteAllText(path + ".writing", "garbage from a crash");
        ColonySave.Save(path, colony);
        Assert.False(File.Exists(path + ".writing"));
        Assert.Equal(ColonySave.Text(colony), File.ReadAllText(path));
    }

    [Fact]
    public void FP09_the_save_a_new_one_replaces_is_kept_as_a_backup()
    {
        string folder = Folder();
        string path = Path.Combine(folder, "colony.json");
        var colony = FirstDay();
        ColonySave.Save(path, colony);
        Assert.False(File.Exists(path + ".bak"));                               // nothing to keep yet
        string first = File.ReadAllText(path);
        colony.RunMinutes(60);
        ColonySave.Save(path, colony);
        Assert.Equal(first, File.ReadAllText(path + ".bak"));
        string second = File.ReadAllText(path);
        colony.RunMinutes(60);
        ColonySave.Save(path, colony);
        Assert.Equal(second, File.ReadAllText(path + ".bak"));                 // one level back
        Assert.Equal(ColonySave.Text(ColonySave.Load(path + ".bak", Real)), second);
        colony.RunMinutes(60);
        ColonySave.Save(path, colony, backup: false);
        Assert.Equal(second, File.ReadAllText(path + ".bak"));                 // without a backup, the old .bak stays
    }

    public static IEnumerable<object[]> DamagedFiles()
    {
        string good = ColonySave.Text(FirstDay());
        yield return new object[] { "", "broken JSON" };
        yield return new object[] { good[..(good.Length / 2)], "broken JSON" };
        yield return new object[] { "\u0001\u0002ÿ not a save", "broken JSON" };
        yield return new object[] { "[1, 2, 3]", "no JSON object" };
        yield return new object[] { "{\"format\": \"something else\"}", "not a colony save of this game" };
        yield return new object[] { good.Replace("\"version\": " + ColonySave.Version, "\"version\": " + (ColonySave.Version - 1)), "older saves are not converted" };
    }

    [Theory]
    [MemberData(nameof(DamagedFiles))]
    public void FP09_a_damaged_file_is_refused_with_a_reason_and_left_as_it_is(string text, string reason)
    {
        string path = Path.Combine(Folder(), "damaged.json");
        File.WriteAllText(path, text);
        string before = Sha(path);
        var e = Assert.Throws<ColonySaveException>(() => ColonySave.Load(path, Real));
        Assert.Contains(reason, e.Message);
        Assert.StartsWith(path, e.Message);
        Assert.Equal(before, Sha(path));
    }

    // ---- The session: time, the morning autosave, failures that do not stop play -----------------------------------

    [Fact]
    public void The_session_runs_ticks_at_the_chosen_speed()
    {
        var session = new Session(FirstDay(), Folder());
        Assert.Equal(120, session.Frame(1.0));                                  // ×1: two game minutes a real second
        session.Speed = Speed.Triple;
        Assert.Equal(360, session.Frame(1.0));
        session.Speed = Speed.Paused;
        Assert.Equal(0, session.Frame(5.0));
        Assert.Equal(Arrival.Tick + 480, session.Colony.Now.Tick);
    }

    [Fact]
    public void The_session_autosaves_every_morning_into_three_slots_in_turn()
    {
        string folder = Folder();
        var session = new Session(FirstDay(), Path.Combine(folder, "autosaves"));
        session.Speed = Speed.Triple;
        var days = new Dictionary<int, long>();
        // Four mornings, a frame at a time (10 real seconds a frame at ×3 is 60 ticks, the most a frame runs).
        while (session.Colony.Now.Tick < Arrival.Tick + 4 * Calendar.TicksPerDay)
        {
            var before = session.Colony.Now;
            session.Frame(10);
            if (Autosave.MorningPassed(before, session.Colony.Now))
                days[Autosave.SlotFor(session.Colony.Now)] = session.Colony.Now.AbsoluteDay;
        }
        Assert.Empty(session.Reports);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(folder, "autosaves"), "autosave-*.json").Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(folder, "autosaves"), "*.bak"));      // the slots are the history
        foreach (var (slot, day) in days)
        {
            var saved = ColonySave.Load(Autosave.PathOf(Path.Combine(folder, "autosaves"), slot), Real);
            Assert.Equal(day, saved.Now.AbsoluteDay);
            Assert.Equal(Autosave.MorningMinute, saved.Now.MinuteOfDay);          // saved at 06:00 exactly
        }
        // The newest one is found, and loads.
        var skipped = new List<string>();
        var newest = Session.Newest(Path.Combine(folder, "autosaves"), Real, skipped)!;
        Assert.Equal(days.Values.Max(), newest.Colony.Now.AbsoluteDay);
        Assert.Empty(skipped);
    }

    [Fact]
    public void A_failed_autosave_is_reported_and_play_goes_on()
    {
        string folder = Folder();
        string blocked = Path.Combine(folder, "autosaves");
        File.WriteAllText(blocked, "a file where the folder should be");
        var session = new Session(FirstDay(), blocked);
        session.Speed = Speed.Triple;
        while (session.Colony.Now.Tick < Arrival.Tick + Calendar.TicksPerDay)
            session.Frame(10);
        Assert.Contains("The morning autosave failed", Assert.Single(session.Reports));
        Assert.True(session.Colony.Now.Tick >= Arrival.Tick + Calendar.TicksPerDay);   // it played on past the morning
    }

    [Fact]
    public void Saving_and_loading_in_a_session_report_what_went_wrong_and_keep_the_colony()
    {
        string folder = Folder();
        var session = new Session(FirstDay(), Path.Combine(folder, "autosaves"));
        Assert.False(session.SaveAs(Path.Combine(folder, "missing", "save.json")));
        Assert.Contains("could not be saved", session.Reports[^1]);
        string path = Path.Combine(folder, "save.json");
        Assert.True(session.SaveAs(path));
        var playing = session.Colony;
        // A damaged save: not loaded, and the colony being played goes on.
        string damaged = Path.Combine(folder, "damaged.json");
        File.WriteAllText(damaged, "{ broken");
        Assert.False(session.LoadFrom(damaged));
        Assert.Same(playing, session.Colony);
        Assert.Contains("broken JSON", session.Reports[^1]);
        // A save changed outside the game: only with the player's yes.
        var save = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        save["work"]!["workers"]![0]!["name"] = "Someone Else";
        File.WriteAllText(path, save.ToJsonString());
        Assert.False(session.LoadFrom(path));
        Assert.Contains("changed outside the game", session.Reports[^1]);
        Assert.True(session.LoadFrom(path, acceptChanged: true));
        Assert.NotSame(playing, session.Colony);
        Assert.Contains("loaded as you asked", session.Reports[^1]);
    }

    [Fact]
    public void A_damaged_autosave_slot_is_skipped_for_the_newest_good_one()
    {
        string folder = Folder();
        var session = new Session(FirstDay(), folder);
        session.AutosaveNow();                                                  // today's slot
        string good = Autosave.PathOf(folder, Autosave.SlotFor(session.Colony.Now));
        int other = Autosave.SlotFor(session.Colony.Now) % Autosave.Slots + 1;
        File.WriteAllText(Autosave.PathOf(folder, other), "{ half a save");
        var skipped = new List<string>();
        var newest = Session.Newest(folder, Real, skipped)!;
        Assert.Equal(session.Colony.Now, newest.Colony.Now);
        Assert.Contains("broken JSON", Assert.Single(skipped));
        Assert.True(File.Exists(good));
        Assert.Null(Session.Newest(Folder(), Real, new List<string>()));      // no autosaves at all
    }
}
