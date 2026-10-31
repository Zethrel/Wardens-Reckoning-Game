// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Cases the test world does not happen to hold, on a small world written out by hand.</summary>
public class HandMadeTests
{
    // Two factions and three towns over 4 years:
    // town 0: faction 0's capital, falls to ruin in year 2 (and no one holds it from then), while
    //         faction 0 still has people that year and still names it its capital;
    // town 1: faction 1's capital throughout; town 2: founded in year 1 by faction 1.
    static ExportedWorld World()
    {
        var data = new JsonObject
        {
            ["format"] = "the-north-world", ["version"] = 2, ["seed"] = 1, ["history_seed"] = 2,
            ["size"] = 4, ["years"] = 4, ["history_factor"] = 2,
            ["factions"] = new JsonArray(
                new JsonObject { ["name"] = "East", ["colour"] = new JsonArray(10, 20, 30), ["lineage"] = "east" },
                new JsonObject { ["name"] = "West", ["colour"] = new JsonArray(40, 50, 60), ["lineage"] = "west" }),
            ["faction_races"] = new JsonArray("dwarf", "elf"),
            ["population"] = new JsonArray(new JsonArray(500, 400, 300, 0, 0), new JsonArray(900, 950, 1500, 2600, 3100)),
            ["settlements"] = new JsonArray(new JsonObject { ["x"] = 0, ["y"] = 0 }, new JsonObject { ["x"] = 3, ["y"] = 3 },
                                            new JsonObject { ["x"] = 3, ["y"] = 0 }),
            ["history"] = new JsonObject
            {
                ["towns"] = new JsonArray(
                    new JsonObject { ["from"] = 0, ["name"] = "Ash", ["people"] = new JsonArray(500, 1200, 0, 0, 0),
                                     ["owners"] = new JsonArray(new JsonArray(0, 0), new JsonArray(2, null)),
                                     ["ruins"] = new JsonArray(new JsonArray(2, null)) },
                    new JsonObject { ["from"] = 0, ["name"] = "Bree", ["people"] = new JsonArray(900, 950, 1500, 2600, 3100),
                                     ["owners"] = new JsonArray(new JsonArray(0, 1)), ["ruins"] = new JsonArray() },
                    new JsonObject { ["from"] = 1, ["name"] = "Cove", ["people"] = new JsonArray(10, 20, 30, 40),
                                     ["owners"] = new JsonArray(new JsonArray(1, 1)), ["ruins"] = new JsonArray() }),
                ["factions"] = new JsonArray(
                    new JsonObject { ["from"] = 0, ["changes"] = new JsonArray(new JsonArray(0, new JsonObject { ["path"] = "handcraft", ["capital"] = 0 })) },
                    new JsonObject { ["from"] = 0, ["changes"] = new JsonArray(new JsonArray(0, new JsonObject { ["path"] = "tidecraft", ["capital"] = 1 })) }),
                ["borders"] = new JsonObject
                {
                    ["columns"] = 2, ["rows"] = 2,
                    ["snapshots"] = new JsonArray(new JsonArray(0, new JsonObject { ["full"] = new JsonArray(0, 1, 1, 3) }),
                                                  new JsonArray(2, new JsonObject { ["changes"] = new JsonArray(0, 1, -1) })),
                },
            },
        };
        string folder = Path.Combine(Path.GetTempPath(), "wr-hand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "world.json"), data.ToJsonString());
        return ExportedWorld.Read(folder);
    }

    [Fact]
    public void A_ruin_keeps_the_pictures_of_the_last_faction_that_held_it_at_its_greatest_size()
    {
        var ash = MapYear.Towns(World(), 3).Single(t => t.Index == 0);
        Assert.Equal(new TownDrawing(0, 0, 0, 0, "settlement", true, false), ash);
    }

    [Fact]
    public void A_ruined_capital_is_drawn_as_a_ruin_not_as_a_capital()
    {
        var world = World();
        Assert.True(MapYear.Towns(world, 1).Single(t => t.Index == 0).Capital);
        Assert.Contains(0, MapYear.Capitals(world, 2));            // East lives on and still names Ash
        Assert.False(MapYear.Towns(world, 2).Single(t => t.Index == 0).Capital);
        Assert.DoesNotContain(0, MapYear.Capitals(world, 3));      // East has no people left
    }

    [Fact]
    public void Towns_appear_in_their_founding_year_largest_first()
    {
        var world = World();
        Assert.Equal(new[] { 1, 0 }, MapYear.Towns(world, 0).Select(t => t.Index));
        Assert.Equal(new[] { 0, 1, 2 }, MapYear.Towns(world, 1).Select(t => t.Index));
    }

    [Fact]
    public void Borders_from_runs_and_changes()
    {
        var world = World();
        Assert.Equal(new[] { 0, 1, 1, 1 }, world.Borders(1));
        Assert.Equal(new[] { -1, 1, 1, 1 }, world.Borders(4));
        Assert.Null(world.Borders(-1));
    }

    [Fact]
    public void The_borders_picture_scales_regions_to_map_cells()
    {
        var world = World();
        var land = Enumerable.Repeat(true, 16).ToArray();
        land[15] = false;                                   // cell (3, 3) is sea
        var rgba = new byte[16 * 4];
        Pictures.BordersInto(rgba, world, world.Borders(1), land, 200);
        Assert.Equal(new byte[] { 10, 20, 30, 200 }, Pixel(rgba, 0, 0));           // (0, 0): region (0, 0), East
        Assert.Equal(new byte[] { 10, 20, 30, 200 }, Pixel(rgba, 1, 1));  // (1, 1): still region (0, 0)
        Assert.Equal(new byte[] { 40, 50, 60, 200 }, Pixel(rgba, 2, 0));       // (2, 0): region (1, 0), West
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(rgba, 3, 3));          // the sea stays clear
    }

    // The four bytes of map cell (x, y) on the 4 x 4 test map.
    static byte[] Pixel(byte[] rgba, int x, int y) => rgba[((y * 4 + x) * 4)..((y * 4 + x) * 4 + 4)];
}
