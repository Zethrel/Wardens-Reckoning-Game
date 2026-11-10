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
}
