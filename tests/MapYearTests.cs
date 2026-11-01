// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>Which pictures a town is drawn with, on the test world.</summary>
public class MapYearTests
{
    static readonly ExportedWorld World = ExportedWorld.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "world-7"));

    [Fact]
    public void A_capital_tries_its_faction_s_pictures_before_its_race_s()
    {
        var capital = MapYear.Towns(World, World.Years).First(t => t.Capital);
        var info = World.Factions[capital.Faction!.Value];
        Assert.Equal(new[] { Path.Combine("sprites", "factions", info.Lineage!, capital.Size + ".png"),
                             Path.Combine("sprites", "races", info.Race!, capital.Size + ".png") },
                     MapYear.SpriteFiles(World, capital));
    }

    [Fact]
    public void A_ruin_uses_its_race_s_ruins_picture()
    {
        var ruin = MapYear.Towns(World, World.Years).First(t => t.Ruined);
        Assert.False(ruin.Capital);
        var info = World.Factions[ruin.Faction!.Value];
        Assert.Equal(new[] { Path.Combine("sprites", "races", info.Race!, "ruins.png") }, MapYear.SpriteFiles(World, ruin));
    }

    [Fact]
    public void A_town_of_no_one_has_no_pictures()
    {
        var nobody = new TownDrawing(0, 0, 0, null, "hamlet", false, false);
        Assert.Empty(MapYear.SpriteFiles(World, nobody));
    }

    [Fact]
    public void Sizes_go_by_the_people()
    {
        Assert.Equal("hamlet", World.SizeOf(0));
        Assert.Equal("hamlet", World.SizeOf(999));
        Assert.Equal("settlement", World.SizeOf(1000));
        Assert.Equal("city", World.SizeOf(1_000_000));
    }

    [Fact]
    public void The_largest_towns_come_first_and_only_founded_ones()
    {
        foreach (int year in new[] { 0, 50, World.Years })
        {
            var towns = MapYear.Towns(World, year);
            Assert.All(towns, t => Assert.True(World.Towns[t.Index].From <= year));
            var peaks = towns.Select(t => ExportedWorld.PeakTo(World.Towns[t.Index], year)).ToList();
            Assert.Equal(peaks.OrderByDescending(p => p), peaks);
        }
    }

    [Fact]
    public void The_living_factions_most_people_first()
    {
        var alive = MapYear.Alive(World, World.Years);
        Assert.NotEmpty(alive);
        Assert.All(alive, a => Assert.True(a.People > 0));
        Assert.Equal(alive.Select(a => a.People).OrderByDescending(p => p), alive.Select(a => a.People));
    }

    [Fact]
    public void Pictures_are_drawn_larger_on_larger_maps_but_never_below_ten_cells()
    {
        Assert.Equal(10, MapYear.SpriteSide(128));
        Assert.Equal(16, MapYear.SpriteSide(512));
        Assert.Equal(64, MapYear.SpriteSide(2048));
    }

    [Fact]
    public void A_click_finds_the_town_on_top()
    {
        var towns = new List<TownDrawing>
        {
            new(0, 10, 10, 1, "city", false, false), new(1, 14, 10, 2, "hamlet", false, false),
        };
        Assert.Equal(0, MapYear.TownAt(towns, 6, 10.5, 10)!.Index);          // only the city covers it
        Assert.Equal(1, MapYear.TownAt(towns, 12.5, 10.5, 10)!.Index);     // both cover it: the one drawn last
        Assert.Equal(1, MapYear.TownAt(towns, 19.4, 10.5, 10)!.Index);
        Assert.Null(MapYear.TownAt(towns, 19.6, 10.5, 10));
        Assert.Null(MapYear.TownAt(towns, 10.5, 30, 10));
    }
}
