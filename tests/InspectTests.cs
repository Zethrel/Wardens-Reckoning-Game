// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 18a: what the colony screen's inspector shows for a tile or a colonist (library Review 38; Shared Systems
/// §19.2). The screen only draws these lines, so they are checked here.
/// </summary>
public class InspectTests
{
    static readonly Content Real = TestContent.Real;
    static readonly Moment Arrival = new(Calendar.TickOf(1, 1, 1, 8, 0));

    static (Colony.Colony Colony, Inventory Carts) Founded() => Founding.Found(7, Arrival, Real, 24, 20);

    static string Text(Inspection inspection, string label) => inspection.Lines.Single(l => l.Label == label).Text;

    [Fact]
    public void A_colonist_shows_what_they_do_where_their_needs_and_what_they_carry()
    {
        var (colony, _) = Founded();
        var ada = colony.Work!.Workers.First();
        var shown = Inspect.Worker(colony, ada.Id);
        Assert.Equal(ada.Name, shown.Title);
        Assert.Equal(new[] { "Doing", "Where", "Fatigue", "Hunger", "Hygiene", "Thirst", "Schedule", "Construction", "Food preparation", "Carrying" },
                     shown.Lines.Select(l => l.Label));
        Assert.Equal(colony.Work.Doing(ada.Id), Text(shown, "Doing"));
        var (x, y) = Ground.TileOf(ada.X, ada.Y);
        Assert.Equal("(" + x + ", " + y + "), on grass", Text(shown, "Where"));
        Assert.Equal("0 of 100, fine", Text(shown, "Hunger"));
        Assert.Equal("nothing", Text(shown, "Carrying"));
        Assert.StartsWith("standard_day, now ", Text(shown, "Schedule"));
        ada.Needs["thirst"].Level = 70;
        Assert.Equal("70 of 100, urgent", Text(Inspect.Worker(colony, ada.Id), "Thirst"));
    }

    [Theory]
    [InlineData(0, "fine")]
    [InlineData(15, "peckish")]
    [InlineData(35, "wants")]
    [InlineData(64.9, "wants")]
    [InlineData(65, "urgent")]
    [InlineData(90, "critical")]
    public void A_needs_level_has_the_word_of_its_threshold(double level, string word) =>
        Assert.Equal(word, Inspect.Word(Real.Thirst, level));

    [Fact]
    public void A_tile_shows_its_ground_the_stores_bedrolls_and_who_stands_there()
    {
        var (colony, carts) = Founded();
        var (cx, cy) = Ground.TileOf(carts.X, carts.Y);
        var shown = Inspect.Tile(colony, cx, cy);
        Assert.Equal("Tile (24, 20)", shown.Title);
        Assert.Equal("Grass, walked at 80%", Text(shown, "Ground"));
        Assert.Equal(carts.Name + ": 30 Travel ration, 60 Timber, 60 Water, 50 Wheat", Text(shown, "Store"));   // by the goods' ids
        Assert.Equal("outdoors", Text(shown, "Shelter"));
        var ada = colony.Work!.Workers.First();
        var (ax, ay) = Ground.TileOf(ada.X, ada.Y);
        Assert.Contains(ada.Name, Text(Inspect.Tile(colony, ax, ay), "Here"));
        var bed = colony.Work.Bedrolls.First();
        var (bx, by) = Ground.TileOf(bed.X, bed.Y);
        Assert.EndsWith("'s", Text(Inspect.Tile(colony, bx, by), "Bedroll"));
        // The stream the founders drink from.
        var spring = colony.Stocks[colony.Work.Springs.First()];
        var (sx, sy) = Ground.TileOf(spring.X, spring.Y);
        Assert.StartsWith("The stream: ", Text(Inspect.Tile(colony, sx, sy), "Water"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Inspect.Tile(colony, -1, 0));
    }

    [Fact]
    public void A_tile_shows_a_rock_a_wall_and_a_building_that_waits_with_why()
    {
        var (colony, _) = Founded();
        var ground = colony.Ground!;
        var rock = Enumerable.Range(0, 48).SelectMany(y => Enumerable.Range(0, 48).Select(x => (x, y))).First(t => ground.ObstacleAt(t.x, t.y) == "rock");
        Assert.Equal("Rock", Text(Inspect.Tile(colony, rock.x, rock.y), "In the way"));
        Assert.DoesNotContain(Inspect.Tile(colony, rock.x, rock.y).Lines, l => l.Label == "Here");
        ground.AddPiece("wall", true, 2, 2);
        Assert.Equal("Timber wall", Text(Inspect.Tile(colony, 2, 2), "Built"));
        // A wall planned while everyone is asleep... or simply paused: the tile says so, and why it waits.
        var (project, placement) = colony.Work!.Plan("wall", 30, 30);
        Assert.True(placement.Ok, placement.Why);
        colony.Work.Pause(project!.Id, true);
        var shown = Inspect.Tile(colony, 30, 30);
        Assert.Equal("Building: Timber wall (paused)", Text(shown, "Project"));
        Assert.Equal(colony.Work.WhyBuild(project.Id)!.Player, Text(shown, "Waiting"));
        // Only on its own tile: not beside it, above it, or below it.
        foreach (var (nx, ny) in new[] { (29, 30), (31, 30), (30, 29), (30, 31) })
            Assert.DoesNotContain(Inspect.Tile(colony, nx, ny).Lines, l => l.Label == "Project");
    }

    [Fact]
    public void Water_says_it_cannot_be_walked_on_and_inspecting_changes_nothing()
    {
        var map = LocalMap.Parse(new System.Text.Json.Nodes.JsonObject
        {
            ["format"] = "the-north-local", ["version"] = 1, ["size"] = 4,
            ["surfaces"] = new System.Text.Json.Nodes.JsonObject { ["g"] = "grass", ["w"] = "fresh_water" },
            ["things"] = new System.Text.Json.Nodes.JsonObject(),
            ["ground"] = new System.Text.Json.Nodes.JsonArray("gggw", "gggw", "gggw", "gggw"),
            ["on_ground"] = new System.Text.Json.Nodes.JsonArray("....", "....", "....", "...."),
            ["water"] = "stream",
        }, "map");
        var (colony, _) = Founding.Found(7, Arrival, Real, map);
        string before = ColonySave.Text(colony);
        var shown = Inspect.Tile(colony, 3, 0);
        Assert.Equal("Fresh water, cannot be walked on", Text(shown, "Ground"));
        Assert.DoesNotContain(shown.Lines, l => l.Label == "Shelter");
        foreach (var w in colony.Work!.Workers)
            Inspect.Worker(colony, w.Id);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Inspect.Tile(colony, x, y);
        Assert.Equal(before, ColonySave.Text(colony));
    }

    [Fact]
    public void The_colonist_under_a_click_is_the_nearest_within_reach()
    {
        var (colony, _) = Founded();
        var ada = colony.Work!.Workers.First();
        Assert.Equal(ada.Id, Inspect.WorkerAt(colony, ada.X + 0.3, ada.Y)!.Id);
        Assert.Null(Inspect.WorkerAt(colony, ada.X + 5, ada.Y + 5));
        Assert.Equal("nothing", Inspect.Goods(colony, new Dictionary<string, int> { ["timber"] = 0 }));
        Assert.Equal("2 Timber, 1 Wheat", Inspect.Goods(colony, new Dictionary<string, int> { ["wheat"] = 1, ["timber"] = 2 }));
    }
}
