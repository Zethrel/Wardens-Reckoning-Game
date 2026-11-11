// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// The content's files as the tests change them. `EverySecond`: the real content with every colonist thinking every
/// second (colony.json think_seconds [1, 1]), for the tests of one mechanic at a time (walking, loading, building,
/// meals): a colonist decides the second something happens, so their minutes are the mechanic's own. The thinking
/// rhythm itself (2 to 7 seconds) is tested in TickTests, and runs in every scenario test (FP, founding, water).
/// </summary>
internal static class TestContent
{
    public static Dictionary<string, string?> Files() =>
        Content.Files.ToDictionary(f => f, f => (string?)File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", f)));

    /// <summary>`files` with colony.json's think_seconds set to [1, 1].</summary>
    public static Dictionary<string, string?> ThinkingEverySecond(Dictionary<string, string?> files)
    {
        var colony = JsonNode.Parse(files["colony.json"]!)!.AsObject();
        colony["think_seconds"] = new JsonArray(1, 1);
        files["colony.json"] = colony.ToJsonString();
        return files;
    }

    public static readonly Content EverySecond = Content.Read(ThinkingEverySecond(Files()));

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
