// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>
/// The C# reader against the generator's own (north/world_file.py) on a real exported world.
/// fixtures/make_expected.py made the world (fixtures/world-7) and wrote down what the Python reader
/// finds in it (fixtures/expected.json); here the C# reader must find the same.
/// </summary>
public class AgreementTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    static readonly ExportedWorld World = ExportedWorld.Read(Path.Combine(Fixtures, "world-7"));
    static readonly JsonObject Expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")))!.AsObject();

    static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static IEnumerable<object[]> Years() =>
        Expected["years"]!.AsObject().Select(pair => new object[] { int.Parse(pair.Key) });

    [Fact]
    public void Every_grid_reads_as_the_generator_reads_it()
    {
        foreach (var (name, want) in Expected["grids"]!.AsObject())
        {
            var grid = World.Grid(name);
            Assert.Equal((int)want!["width"]!, grid.Width);
            Assert.Equal((int)want["height"]!, grid.Height);
            Assert.True((string)want["sha256"]! == Sha256(string.Join(",", grid.Values)), name);
        }
    }

    [Theory, MemberData(nameof(Years))]
    public void Every_town_in_the_year(int year)
    {
        var towns = Expected["years"]![year.ToString()]!["towns"]!.AsArray();
        Assert.Equal(towns.Count, World.Towns.Count);
        for (int i = 0; i < towns.Count; i++)
        {
            var got = World.Town(i, year);
            if (towns[i] is null)
            {
                Assert.Null(got);
                continue;
            }
            Assert.Equal(new TownState((int?)towns[i]!["holder"], (int)towns[i]!["people"]!, (bool)towns[i]!["ruined"]!), got);
        }
    }

    [Theory, MemberData(nameof(Years))]
    public void Every_faction_in_the_year(int year)
    {
        var factions = Expected["years"]![year.ToString()]!["factions"]!.AsArray();
        for (int i = 0; i < factions.Count; i++)
        {
            Assert.True(JsonNode.DeepEquals(factions[i], World.Faction(i, year)), "faction " + i + " in year " + year);
            Assert.Equal((int?)factions[i]?["capital"], World.CapitalOf(i, year));
        }
    }

    [Theory, MemberData(nameof(Years))]
    public void The_borders_in_the_year(int year)
    {
        var borders = World.Borders(year);
        string flat = borders == null ? "" : string.Join(",", borders);
        Assert.Equal((string)Expected["years"]![year.ToString()]!["borders_sha256"]!, Sha256(flat));
    }

    [Theory, MemberData(nameof(Years))]
    public void The_towns_are_drawn_as_the_atlas_draws_them(int year)
    {
        var want = Expected["years"]![year.ToString()]!["drawn"]!.AsArray()
            .Select(d => new TownDrawing((int)d![0]!, (int)d[1]!, (int)d[2]!, (int?)d[3], (string)d[4]!, (bool)d[5]!, (bool)d[6]!))
            .ToList();
        Assert.Equal(want, MapYear.Towns(World, year));
    }

    [Fact]
    public void The_borders_are_the_same_read_in_any_order()
    {
        // Borders keeps copies along the way; going back in time must not use a later one.
        var fresh = ExportedWorld.Read(Path.Combine(Fixtures, "world-7"));
        var late = fresh.Borders(World.Years)!;
        var early = fresh.Borders(1)!;
        Assert.Equal(World.Borders(1), early);
        Assert.Equal(World.Borders(World.Years), late);
        Assert.Equal(World.BorderColumns * World.BorderRows, late.Length);
    }

    [Fact]
    public void The_world_s_own_facts()
    {
        Assert.Equal(7, World.Seed);
        Assert.Equal(7, World.HistorySeed);
        Assert.Equal(128, World.Size);
        Assert.Equal(100, World.Years);
        Assert.Equal(World.Factions.Count, World.Population.Count);
        Assert.Equal("hamlet", World.SettlementSizes[0].Name);
        Assert.True(File.Exists(World.PathOf("terrain.png")));
        Assert.All(World.Factions, f => Assert.False(string.IsNullOrEmpty(f.Name)));
    }
}

/// <summary>Grids and files that are not right are refused, and the PNG row filters are undone.</summary>
public class FileTests
{
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void A_grid_saved_with_every_row_filter_reads_back()
    {
        var want = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "filtered.json")))!.AsArray();
        var grid = Grid.Read(Path.Combine(Fixtures, "filtered.png"));
        Assert.Equal(want.Count, grid.Height);
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                Assert.Equal((int)want[y]![x]!, grid[x, y]);
    }

    [Fact]
    public void A_colour_picture_is_not_a_grid()
    {
        // terrain.png is in colour (red, green, blue): a picture to look at, not numbers.
        Assert.Throws<WorldFileException>(() => Grid.Read(Path.Combine(Fixtures, "world-7", "terrain.png")));
        Assert.Throws<WorldFileException>(() => Grid.Read(Path.Combine(Fixtures, "expected.json")));
    }

    static string Folder(string json)
    {
        string folder = Path.Combine(Scratch.Root, "wr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        if (json != "")
            File.WriteAllText(Path.Combine(folder, "world.json"), json);
        return folder;
    }

    [Fact]
    public void No_world_json_another_format_or_a_newer_version_is_refused()
    {
        Assert.Throws<WorldFileException>(() => ExportedWorld.Read(Folder("")));
        Assert.Throws<WorldFileException>(() => ExportedWorld.Read(Folder("{\"format\": \"something-else\", \"version\": 1}")));
        Assert.Throws<WorldFileException>(() => ExportedWorld.Read(Folder("{\"format\": \"the-north-world\", \"version\": 3}")));
        Assert.Throws<WorldFileException>(() => ExportedWorld.Read(Folder("not json")));
    }

    [Fact]
    public void The_world_json_itself_may_be_given()
    {
        var world = ExportedWorld.Read(Path.Combine(Fixtures, "world-7", "world.json"));
        Assert.Equal(128, world.Size);
    }
}
