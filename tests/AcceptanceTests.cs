// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 20a: the first playable's determinism and save scenarios (FP-07, FP-08) again, on the colony as it is now:
/// founded on a local map by a brook, with water carried to a barrel and a basin, washing, a brook that fouls, a
/// mark, boiling and an upset stomach. The flat test ground's scenarios (ColonyTests, ColonySaveTests) stay as
/// they are; these add everything built since.
/// </summary>
public class AcceptanceTests
{
    static Moment Start => new(Calendar.TickOf(1, 1, 1, 8, 0));

    // A colony by a brook that fouls on its second morning (the dice are set to foul it on every morning it is
    // clean, a sign in half of the foulings, an upset stomach from every drink of fouled water), with a water barrel
    // and a wash basin planned and no water in the carts; on the third morning, a day after the fouling, the player
    // marks the brook "boil first".
    static Colony.Colony Founded(int seed)
    {
        var content = SanitationTests.Tweaked(foulEvery: 1, lasts: 3, signs: 0.5, illness: 1, onset: 6, ill: 24);
        var (colony, carts) = Founding.Found(seed, Start, content, SanitationTests.Map(40, 30));
        // The carts' water is spilt: the colony lives on the brook from the start.
        var spilt = colony.Stocks.Reserve(carts.Id, "water", carts.Count("water"), "spilt").Reservation!;
        colony.Stocks.Make(colony.Ids.Next<OperationId>(), colony.Now.Tick, new[] { spilt.Id }, carts.Id, new Dictionary<string, int>());
        colony.Work!.Plan("water_barrel", 26, 22);
        colony.Work.Plan("wash_basin", 24, 22);
        return colony;
    }

    // The player's one command, given at the same minute in every copy.
    static readonly long MarkAt = Start.Tick + 46 * Calendar.TicksPerHour;

    static void Command(Colony.Colony c)
    {
        if (c.Now.Tick == MarkAt)
            c.Work!.SetMark(c.Work.Springs.Single(), WaterMark.BoilFirst);
    }

    [Fact]
    public void FP07_a_colony_on_a_local_map_plays_out_the_same_twice_and_otherwise_with_another_seed()
    {
        string Play(int seed)
        {
            var colony = Founded(seed);
            var checkpoints = new List<string>();
            colony.RunMinutes(4 * Calendar.MinutesPerDay, c =>
            {
                Command(c);
                if (c.Now.MinuteOfDay == 0)
                    checkpoints.Add(ColonySave.Fingerprint(c));
            });
            checkpoints.Add(ColonySave.Fingerprint(colony));
            return string.Join("|", checkpoints);
        }
        string once = Play(7);
        Assert.Equal(once, Play(7));
        Assert.NotEqual(once, Play(8));
    }

    [Fact]
    public void FP08_a_colony_on_a_local_map_saved_at_any_of_its_hours_goes_on_exactly_as_if_never_saved()
    {
        var colony = Founded(7);
        long end = Start.Tick + 4 * Calendar.TicksPerDay;
        var copies = new List<Colony.Colony>();
        bool fouled = false, carrying = false, boiling = false, ill = false, marked = false;
        string folder = Path.Combine(Scratch.Root, "fp08-local");
        Directory.CreateDirectory(folder);
        // Second by second (a load of water is carried in a few seconds now: step 21c), saved on the minutes.
        colony.Run(end - Start.Tick, c =>
        {
            Command(c);
            var work = c.Work!;
            fouled |= work.Springs.Any(s => work.SourceOf(s).FouledUntil != null);
            carrying |= work.Jobs.Any(j => j.Step == Step.Carrying && c.Stocks[work.Worker(j.Worker!.Value).Arms].Count("water") + c.Stocks[work.Worker(j.Worker!.Value).Arms].Count("foul_water") > 0);
            boiling |= work.Jobs.Any(j => j.Kind == JobKind.Boil && j.Step == Step.Working);
            ill |= work.Workers.Any(w => w.Illness != null);
            marked |= c.Stocks.Inventories.Any(i => i.Mark != WaterMark.None);
            if (!c.Now.OnTheMinute || c.Now.MinuteOfDay % 120 != 90)   // every two hours, on the half hour
                return;
            string path = Path.Combine(folder, "at-" + c.Now.Tick + ".json");
            ColonySave.Save(path, c);
            var copy = ColonySave.Load(path, c.Content!);
            Assert.Equal(File.ReadAllText(path), ColonySave.Text(copy));
            copies.Add(copy);
        });
        Assert.True(fouled && carrying && boiling && ill && marked,
                    $"fouled {fouled}, carrying water {carrying}, boiling {boiling}, ill {ill}, marked {marked}");
        Assert.True(copies.Count >= 48, copies.Count + " saves");
        string fingerprint = ColonySave.Fingerprint(colony);
        foreach (var copy in copies)
        {
            copy.Run(end - copy.Now.Tick, Command);
            Assert.Equal(fingerprint, ColonySave.Fingerprint(copy));
        }
        Assert.Equal(colony.Stocks.Ledger.Count, colony.Stocks.Ledger.Select(e => (e.Operation, e.Change, e.Good, e.Amount)).Distinct().Count());
    }
}
