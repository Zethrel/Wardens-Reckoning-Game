// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;

namespace WardensReckoning.Tests;

/// <summary>Comparing what the generator wrote with what the game writes, as JSON.</summary>
static class Json
{
    /// <summary>Whether two JSON values hold the same: numbers compared as numbers, to the last digit.</summary>
    public static void Same(JsonNode? want, JsonNode? got, string where = "")
    {
        switch (want)
        {
            case null:
                Assert.True(got is null, where + ": expected null, got " + got?.ToJsonString());
                break;
            case JsonArray a:
                var b = Assert.IsType<JsonArray>(got);
                Assert.True(a.Count == b.Count, where + ": " + a.Count + " items, got " + b.Count);
                for (int i = 0; i < a.Count; i++)
                    Same(a[i], b[i], where + "[" + i + "]");
                break;
            case JsonObject o:
                var p = Assert.IsType<JsonObject>(got);
                Assert.Equal(o.Select(k => k.Key).Order(), p.Select(k => k.Key).Order());
                foreach (var (key, value) in o)
                    Same(value, p[key], where + "." + key);
                break;
            default:
                var v = want.AsValue();
                if (v.TryGetValue(out string? text))
                    Assert.Equal(text, (string?)got);
                else if (v.TryGetValue(out bool yes))
                    Assert.Equal(yes, (bool)got!);
                else
                    Assert.True(Number(v) == Number(got!), where + ": " + v.ToJsonString() + " against " + got!.ToJsonString());
                break;
        }
    }

    public static double Number(JsonNode node) =>
        double.Parse(node.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
}
