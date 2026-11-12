// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// The content's files as the tests change them.
///
/// **The old pace (step 24).** The hybrid clock (library Review 41) made walking, loading and deciding slower in game
/// time: 1.2 m a real second at ×1 is 0.05 m a tick. The tests were written for the pace before it, and test the
/// mechanics, not the balance, so `Files()` (and `Real`) put the physical values back to what they were in ticks:
/// walking 70 m a game minute (28 m a real second), loading and unloading 5 ticks, deciding every 2 to 7 ticks. The
/// content as shipped is `RealFiles()` (`Shipped`); `SpeedTests`, the conversion tests and the first-day scenarios
/// (step 24b: the balance for the 60-minute day is checked on it) use it.
///
/// `EverySecond`: the real content with every colonist thinking every
/// tick (colony.json think_real_seconds a tick each), for the tests of one mechanic at a time (walking, loading, building,
/// meals): a colonist decides the second something happens, so their minutes are the mechanic's own. The thinking
/// rhythm itself (2 to 7 seconds) is tested in TickTests, and runs in every scenario test (FP, founding, water).
/// `EverySecond` also learns nothing (step 23b: skills.json points_per_hour 0), so that a colonist's pace stays the
/// same through a test that times work; learning is tested in LearningTests.
/// </summary>
internal static class TestContent
{
    /// <summary>The content's folder, as shipped.</summary>
    public static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "content");

    /// <summary>The content's files as shipped.</summary>
    public static Dictionary<string, string?> RealFiles() =>
        Content.Files.ToDictionary(f => f, f => (string?)File.ReadAllText(Path.Combine(Folder, f)));

    /// <summary>The content's files at the old pace (see above).</summary>
    public static Dictionary<string, string?> Files() => OldPace(RealFiles());

    /// <summary>The content at the old pace: what most tests of whole colonies use.</summary>
    public static readonly Content Real = Content.Read(Files());

    /// <summary>The content as shipped, at the hybrid clock's pace: what the first-day scenarios use (step 24b).</summary>
    public static readonly Content Shipped = Content.Read(RealFiles());

    /// <summary>`files` with the physical values of colony.json at the pace before step 24, in real seconds at ×1:
    /// a tick is 1/24 of a real second (`Calendar.TicksPerRealSecond`).</summary>
    public static Dictionary<string, string?> OldPace(Dictionary<string, string?> files)
    {
        const double tick = 1.0 / Calendar.TicksPerRealSecond;
        var colony = JsonNode.Parse(files["colony.json"]!)!.AsObject();
        colony["walk_m_per_real_second"] = 70.0 / Calendar.SecondsPerMinute * Calendar.TicksPerRealSecond;   // 28
        colony["pickup_real_seconds"] = 5 * tick;
        colony["drop_real_seconds"] = 5 * tick;
        colony["think_real_seconds"] = new JsonArray(2 * tick, 7 * tick);
        files["colony.json"] = colony.ToJsonString();
        return files;
    }

    /// <summary>`files` with colony.json's think_real_seconds set to a tick each: every colonist decides every tick.</summary>
    public static Dictionary<string, string?> ThinkingEverySecond(Dictionary<string, string?> files)
    {
        const double tick = 1.0 / Calendar.TicksPerRealSecond;
        var colony = JsonNode.Parse(files["colony.json"]!)!.AsObject();
        colony["think_real_seconds"] = new JsonArray(tick, tick);
        files["colony.json"] = colony.ToJsonString();
        return files;
    }

    /// <summary>`files` with skills.json's learning points_per_hour set to 0: nobody learns (step 23b).</summary>
    public static Dictionary<string, string?> NotLearning(Dictionary<string, string?> files)
    {
        var skills = JsonNode.Parse(files["skills.json"]!)!.AsObject();
        skills["learning"]!["points_per_hour"] = 0;
        files["skills.json"] = skills.ToJsonString();
        return files;
    }

    public static readonly Content EverySecond = Content.Read(NotLearning(ThinkingEverySecond(Files())));

    /// <summary>
    /// Puts `amount` of `good` straight onto a project's site, as if already delivered. A planned project asks for
    /// its materials at once (step 21c), so its hauls not yet picked up are dropped first; otherwise they would
    /// bring more on top.
    /// </summary>
    public static void Deliver(Colony.Colony colony, Project project, string good, int amount)
    {
        foreach (var haul in colony.Work!.Jobs.Where(j => j.Kind == JobKind.Haul && j.To == project.Stock
                                                         && j.Step is Step.Open or Step.Going or Step.Loading).ToList())
            colony.Work.Cancel(haul.Id);
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, project.Stock, good, amount);
    }
}
