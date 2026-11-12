// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Diagnostics;
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using WardensReckoning.World;
using Xunit.Abstractions;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 17b: the founders arrive on a local map of the world generator's export (library Review 36): reading the
/// map, the ground made from it (water cannot be walked or built on), drinking from the brook, and the first day.
/// </summary>
public class LocalMapTests(ITestOutputHelper output)
{
    static readonly Content Real = TestContent.Real;
    static readonly Moment Arrival = new(Calendar.TickOf(1, 1, 1, 8, 0));

    /// <summary>A local map's JSON as the generator writes it (its docs/export-format.md, "The local maps"), with
    /// the generator's letters: g grass, r rough, a sand, n snow, m mud, w fresh water, s salt water; T a tree, R a
    /// rock, . nothing.</summary>
    static JsonObject MapJson(string[] ground, string[]? on = null, string? water = "stream") => new()
    {
        ["format"] = "the-north-local", ["version"] = 1, ["site"] = 3, ["score"] = 0.8, ["x"] = 40, ["y"] = 52,
        ["size"] = ground.Length, ["metres_per_cell"] = 3906.25, ["left"] = 1000.0, ["top"] = 2000.0,
        ["biome"] = "taiga", ["fresh_water"] = ground.Any(r => r.Contains('w')), ["water"] = water,
        ["surfaces"] = new JsonObject
        {
            ["g"] = "grass", ["r"] = "rough", ["a"] = "sand", ["n"] = "snow", ["m"] = "mud", ["w"] = "fresh_water", ["s"] = "salt_water",
        },
        ["things"] = new JsonObject { ["T"] = "tree", ["R"] = "rock" },
        ["ground"] = new JsonArray(ground.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
        ["on_ground"] = new JsonArray((on ?? ground.Select(r => new string('.', r.Length)).ToArray()).Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
    };

    static LocalMap Map(string[] ground, string[]? on = null, string? water = "stream") => LocalMap.Parse(MapJson(ground, on, water), "test map");

    /// <summary>
    /// A local map of `size` tiles like the generator's for taiga: grass with rough patches, a brook 3 m wide running
    /// down 30 m east of the middle, and trees on a tenth of the land, none within 14 m of the middle. The trees come
    /// from a seeded dice (System.Random with a seed always gives the same numbers).
    /// </summary>
    static LocalMap Taiga(int size = 256)
    {
        var dice = new Random(17);
        var ground = new string[size];
        var on = new string[size];
        int mid = size / 2;
        for (int y = 0; y < size; y++)
        {
            var g = new char[size];
            var t = new char[size];
            for (int x = 0; x < size; x++)
            {
                g[x] = x >= mid + 30 && x < mid + 33 ? 'w' : (x / 16 + y / 16) % 7 == 0 ? 'r' : 'g';
                bool clear = Math.Max(Math.Abs(x - mid), Math.Abs(y - mid)) <= 14;
                double roll = dice.NextDouble();
                t[x] = g[x] == 'w' || clear ? '.' : roll < 0.1 ? 'T' : roll < 0.11 ? 'R' : '.';
            }
            ground[y] = new string(g);
            on[y] = new string(t);
        }
        return Map(ground, on);
    }

    // ---- Reading a local map --------------------------------------------------------------------------------------

    [Fact]
    public void A_local_map_is_read_tile_by_tile()
    {
        var map = Map(new[] { "gga", "rwm", "nsg" }, new[] { "T..", "...", "..R" });
        Assert.Equal((3, 3, 40, 52, "taiga", "stream"), (map.Size, map.Site, map.X, map.Y, map.Biome, map.Water));
        Assert.True(map.FreshWater);
        Assert.Equal(3906.25, map.MetresPerCell);
        Assert.Equal(("grass", "sand", "fresh_water", "salt_water"), (map.SurfaceAt(0, 0), map.SurfaceAt(2, 0), map.SurfaceAt(1, 1), map.SurfaceAt(1, 2)));
        Assert.Equal(("tree", "rock", (string?)null), (map.ThingAt(0, 0), map.ThingAt(2, 2), map.ThingAt(1, 1)));
        Assert.Equal(new[] { "fresh_water", "grass", "mud", "rough", "salt_water", "sand", "snow" }, map.Surfaces);
    }

    public static IEnumerable<object[]> BrokenMaps()
    {
        object[] Case(Action<JsonObject> change, string reason)
        {
            var data = MapJson(new[] { "gg", "gw" });
            change(data);
            return new object[] { data.ToJsonString(), reason };
        }
        yield return Case(d => d["format"] = "the-north-world", "not a local map of the generator");
        yield return Case(d => d["version"] = 2, "version 2; this game reads local maps up to version 1");
        yield return Case(d => d.Remove("version"), "version ; this game reads");
        yield return Case(d => d["size"] = 0, "its \"size\" is not a whole number above 0");
        yield return Case(d => d["size"] = 3, "\"ground\" should be 3 rows");
        yield return Case(d => d["ground"]![1] = "g", "ground[1] should be 2 letters");
        yield return Case(d => d["ground"]![1] = "ggg", "ground[1] should be 2 letters");
        yield return Case(d => d["ground"]!.AsArray().Add("gg"), "\"ground\" should be 2 rows");
        yield return Case(d => d["ground"]![1] = "gx", "ground[1] has 'x', which its key does not give");
        yield return Case(d => d["on_ground"]![0] = "T?", "on_ground[0] has '?', which its key does not give");
        yield return Case(d => d.Remove("on_ground"), "\"on_ground\" should be 2 rows");
        yield return Case(d => d.Remove("surfaces"), "\"surfaces\" is not a table of letters");
        yield return Case(d => d["surfaces"]!["gg"] = "grass", "\"surfaces\" should give one letter (not '.') for each name");
        yield return Case(d => d["things"]!["."] = "tree", "\"things\" should give one letter (not '.') for each name");
    }

    [Theory]
    [MemberData(nameof(BrokenMaps))]
    public void A_broken_local_map_is_refused_with_its_reason(string text, string reason)
    {
        var e = Assert.Throws<WorldFileException>(() => LocalMap.Parse(JsonNode.Parse(text)!.AsObject(), "site-03.json"));
        Assert.StartsWith("site-03.json: ", e.Message);
        Assert.Contains(reason, e.Message);
    }

    [Fact]
    public void A_file_that_is_not_json_is_refused_and_named()
    {
        string path = Path.Combine(Scratch.Root, "not-a-map.json");
        File.WriteAllText(path, "{ broken");
        Assert.StartsWith(path + " cannot be read as a local map", Assert.Throws<WorldFileException>(() => LocalMap.Read(path)).Message);
    }

    [Fact]
    public void An_export_lists_its_founding_sites_and_reads_their_maps()
    {
        // The test world was exported before step 17a: no founding sites, and no scale.
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "world-7");
        var old = ExportedWorld.Read(fixture);
        Assert.Empty(old.FoundingSites);
        Assert.Null(old.MetresPerCell);
        // A copy with two sites, as the generator now writes them.
        string folder = Path.Combine(Scratch.Root, "export-17a");
        Directory.CreateDirectory(Path.Combine(folder, "local"));
        var world = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture, "world.json")))!.AsObject();
        world["metres_per_cell"] = 3906.25;
        world["founding_sites"] = new JsonArray(
            new JsonObject { ["site"] = 1, ["x"] = 40, ["y"] = 52, ["score"] = 0.8, ["file"] = "local/site-01.json", ["biome"] = "taiga", ["fresh_water"] = true, ["water"] = "stream" },
            new JsonObject { ["site"] = 2, ["x"] = 90, ["y"] = 12, ["score"] = 0.9, ["file"] = "local/site-02.json", ["biome"] = "meadow", ["fresh_water"] = false, ["water"] = null });
        File.WriteAllText(Path.Combine(folder, "world.json"), world.ToJsonString());
        File.WriteAllText(Path.Combine(folder, "local", "site-01.json"), MapJson(new[] { "gg", "gw" }).ToJsonString());
        var export = ExportedWorld.Read(folder);
        Assert.Equal(3906.25, export.MetresPerCell);
        Assert.Equal(new[] { 1, 2 }, export.FoundingSites.Select(s => s.Site));
        Assert.Equal(new FoundingSite(2, 90, 12, 0.9, "local/site-02.json", "meadow", false, null), export.FoundingSites[1]);
        Assert.Equal("fresh_water", export.LocalMap(1).SurfaceAt(1, 1));
        Assert.Contains("cannot be read as a local map", Assert.Throws<WorldFileException>(() => export.LocalMap(2)).Message);   // its file is missing
        Assert.Contains("has no founding site 9", Assert.Throws<WorldFileException>(() => export.LocalMap(9)).Message);
    }

    // ---- The ground of a local map ----------------------------------------------------------------------------------

    [Fact]
    public void The_ground_takes_the_maps_surfaces_trees_and_rocks_and_water_blocks()
    {
        var ground = Ground.FromMap(Map(new[] { "ggr", "wga", "gsm" }, new[] { "T..", "...", "..R" }), Real);
        Assert.Equal("grass", ground.Surface.Id);                               // the most common
        Assert.Equal("rough", Ground.FromMap(Map(new[] { "rrg", "rrg", "rrr" }), Real).Surface.Id);
        Assert.Equal(("rough", 0.55), (ground.SurfaceAt(2, 0).Id, ground.Speed(2, 0)));
        Assert.Equal((0.7, 0.5), (ground.Speed(2, 1), ground.Speed(2, 2)));     // sand, mud
        Assert.Equal(("tree", "rock"), (ground.ObstacleAt(0, 0), ground.ObstacleAt(2, 2)));
        Assert.False(ground.Open(0, 0));
        Assert.False(ground.Open(0, 1));                                         // fresh water
        Assert.False(ground.Open(1, 2));                                         // salt water
        Assert.True(ground.Water(0, 1) && ground.Water(1, 2) && !ground.Water(1, 1));
        Assert.True(double.IsPositiveInfinity(ground.Metres(1, 1, 0, 1)));       // nobody walks onto water
        Assert.Null(ground.Next(1, 1, 0, 1));
    }

    [Fact]
    public void A_map_with_a_surface_the_content_lacks_or_only_water_is_refused()
    {
        var data = MapJson(new[] { "gl", "gg" });
        data["surfaces"]!["l"] = "lava";
        var odd = LocalMap.Parse(data, "odd");
        Assert.Contains("surfaces the content does not know: lava", Assert.Throws<ArgumentException>(() => Ground.FromMap(odd, Real)).Message);
        Assert.Contains("all water", Assert.Throws<ArgumentException>(() => Ground.FromMap(Map(new[] { "ws", "ww" }), Real)).Message);
        Assert.Throws<ArgumentException>(() => new Ground(2, 2, Real.Surfaces["salt_water"]));
    }

    [Fact]
    public void Nothing_is_built_on_water_and_an_island_or_a_bank_is_outdoors()
    {
        // A lake with an island (2, 2), and a pond at (8, 3).
        var map = Map(new[]
        {
            "gggggggggg",
            "gwwwgggggg",
            "gwgwgggggg",
            "gwwwggggwg",
            "gggggggggg",
            "gggggggggg",
            "gggggggggg",
            "gggggggggg",
            "gggggggggg",
            "gggggggggg",
        });
        var (colony, _) = Founding.Found(7, Arrival, Real, map);
        var work = colony.Work!;
        var ground = colony.Ground!;
        Assert.Equal("It is fresh water: nothing can be built on it yet.", work.Check("wall", 1, 1).Why);
        Assert.False(work.Check("well", 7, 2).Ok);                               // its 2 by 2 reaches the pond
        Assert.Empty(ground.Rooms);                                              // the island is not a room
        // Walls round the pond and the grass beside it, (7, 3): the pond is open to the sky, so that is no room.
        foreach (var (x, y) in new[] { (6, 2), (7, 2), (8, 2), (9, 2), (6, 3), (9, 3), (6, 4), (7, 4), (8, 4), (9, 4) })
            ground.AddPiece("wall", true, x, y);
        Assert.False(ground.Indoors(7, 3));
        Assert.Empty(ground.Rooms);
        // Without the pond (on a ground of grass only), the same walls make a room of two tiles.
        var dry = new Ground(10, 10);
        foreach (var (x, y) in new[] { (6, 2), (7, 2), (8, 2), (9, 2), (6, 3), (9, 3), (6, 4), (7, 4), (8, 4), (9, 4) })
            dry.AddPiece("wall", true, x, y);
        Assert.Equal(new[] { (7, 3), (8, 3) }, Assert.Single(dry.Rooms));
    }

    [Fact]
    public void Trees_and_rocks_close_no_room_in_only_walls_and_doors_do()
    {
        // A tile ringed by trees (as often happens in a forest), and a ring of walls with a rock in it.
        var ground = Ground.FromMap(Map(Enumerable.Repeat(new string('g', 12), 12).ToArray(), new[]
        {
            "............",
            "..T.........",
            ".T.T........",
            "..T.........",
            "............",
            "............",
            "......R.....",
            "............",
            "............",
            "............",
            "............",
            "............",
        }), Real);
        Assert.Empty(ground.Rooms);
        for (int i = 5; i <= 9; i++)
            foreach (var (x, y) in new[] { (i, 5), (i, 9), (5, i), (9, i) })
                if (ground.PieceAt(x, y) == null)
                    ground.AddPiece("wall", true, x, y);
        Assert.Equal(9 - 1, Assert.Single(ground.Rooms).Count);                // 3 by 3 inside, less the rock's tile
        Assert.Null(ground.RoomAt(6, 6));
        // Trees in a wall let the outside in: walls in the corners round (2, 2), its trees on the sides.
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (3, 1), (1, 2), (1, 3), (2, 3), (3, 3) })
            if (ground.ObstacleAt(x, y) == null)
                ground.AddPiece("wall", true, x, y);
        Assert.Null(ground.RoomAt(2, 2));
        Assert.Single(ground.Rooms);
    }

    [Fact]
    public void Rooms_are_listed_by_their_first_open_tile_even_when_a_tree_comes_first()
    {
        // Room A (left) has trees all along its top row inside, so its first open tile is on the next row; room B
        // (right) begins on the top row. B is listed first.
        var ground = Ground.FromMap(Map(Enumerable.Repeat(new string('g', 12), 12).ToArray(),
            Enumerable.Range(0, 12).Select(y => y == 2 ? "..TTT......." : "............").ToArray()), Real);
        void Ring(int left, int top)
        {
            for (int i = 0; i <= 4; i++)
                foreach (var (x, y) in new[] { (left + i, top), (left + i, top + 4), (left, top + i), (left + 4, top + i) })
                    if (ground.PieceAt(x, y) == null)
                        ground.AddPiece("wall", true, x, y);
        }
        Ring(1, 1);
        Ring(6, 1);
        Assert.Equal(2, ground.Rooms.Count);
        Assert.Equal((7, 2), ground.Rooms[0][0]);
        Assert.Equal((2, 3), ground.Rooms[1][0]);
        Assert.Equal(1, ground.RoomAt(2, 3));
    }

    // ---- The founders' arrival on a local map ---------------------------------------------------------------------------

    [Fact]
    public void The_founders_arrive_in_the_middle_and_drink_from_the_bank_nearest()
    {
        var map = Taiga(64);
        var (colony, carts) = Founding.Found(7, Arrival, Real, map);
        var work = colony.Work!;
        Assert.Equal((32.5, 32.5), (carts.X, carts.Y));
        Assert.Equal(60, carts.Count("water"));
        Assert.Equal(3, work.Workers.Count);
        var spring = colony.Stocks[Assert.Single(work.Springs)];
        Assert.Equal("The brook", spring.Name);
        // On the bank west of the brook (x = 61), at the shortest walk from the carts of all the bank's tiles.
        Assert.Equal(61.5, spring.X);
        var ground = colony.Ground!;
        double walk = ground.Metres(61, (int)spring.Y, 32, 32);
        Assert.All(Enumerable.Range(0, 64).Where(y => ground.Open(61, y)), y => Assert.True(walk <= ground.Metres(61, y, 32, 32)));
        // A brook (step 19a): its month's litres, January's 600 (needs.json water.brook_litres_by_month).
        Assert.Equal(WaterKind.Brook, work.SourceOf(spring.Id).Kind);
        Assert.Equal(Real.Water.BrookByMonth[0], work.LitresOf(spring.Id));
        // The same seed and map: the same colony.
        Assert.Equal(ColonySave.Text(colony), ColonySave.Text(Founding.Found(7, Arrival, Real, map).Colony));
    }

    [Fact]
    public void Of_two_banks_as_near_the_first_row_by_row_is_taken_and_never_a_tile_already_used()
    {
        // Water down both sides of a 9 by 9 map: (1, 4) and (7, 4) are as near the carts at (4, 4).
        var both = Map(Enumerable.Repeat("wgggggggw", 9).ToArray());
        var (colony, _) = Founding.Found(7, Arrival, Real, both);
        Assert.Equal((1.5, 4.5), (colony.Stocks[colony.Work!.Springs.Single()].X, colony.Stocks[colony.Work.Springs.Single()].Y));
        // Water at (5, 3) and (5, 4), beside the carts: the bank tiles the carts, the founders and the bedrolls take are passed over.
        var close = Map(Enumerable.Range(0, 9).Select(y => y == 3 || y == 4 ? "gggggwggg" : "ggggggggg").ToArray());
        var (near, carts) = Founding.Found(7, Arrival, Real, close);
        var work = near.Work!;
        var taken = work.Workers.Select(w => Ground.TileOf(w.X, w.Y)).Concat(work.Bedrolls.Select(b => Ground.TileOf(b.X, b.Y)))
                        .Append(Ground.TileOf(carts.X, carts.Y)).ToList();
        var spring = near.Stocks[work.Springs.Single()];
        Assert.DoesNotContain(Ground.TileOf(spring.X, spring.Y), taken);
        Assert.Contains((4, 4), taken);                                         // the carts stand on the bank, so this tests something
    }

    [Fact]
    public void Each_water_is_named_and_a_dry_site_has_only_what_the_founders_brought()
    {
        Assert.Equal(("The river", "The brook", "The lake", "The water"),
            (Founding.WaterName("river"), Founding.WaterName("stream"), Founding.WaterName("lake"), Founding.WaterName(null)));
        var dry = Map(Enumerable.Repeat(new string('g', 20), 20).ToArray(), water: null);
        var (colony, carts) = Founding.Found(7, Arrival, Real, dry);
        Assert.Empty(colony.Work!.Springs);
        Assert.Equal(60, carts.Count("water"));
        // The founders drink from the carts.
        var ada = colony.Work.Workers.First();
        ada.Needs["thirst"].Level = 90;
        colony.RunMinutes(60);
        Assert.True(carts.Count("water") < 60);
        // By the sea, salt water is no drink.
        var shore = Map(Enumerable.Range(0, 20).Select(y => y < 4 ? new string('s', 20) : new string('g', 20)).ToArray(), water: "sea");
        Assert.Empty(Founding.Found(7, Arrival, Real, shore).Colony.Work!.Springs);
    }

    [Fact]
    public void The_middle_taken_by_a_rock_moves_the_carts_to_the_nearest_open_tile()
    {
        var map = Map(Enumerable.Repeat(new string('g', 9), 9).ToArray(), Enumerable.Range(0, 9).Select(y => y == 4 ? "....R...." : ".........").ToArray());
        var (_, carts) = Founding.Found(7, Arrival, Real, map);
        Assert.Equal((3.5, 3.5), (carts.X, carts.Y));                           // the ring round (4, 4): its top left tile first
    }

    // ---- A save, and a whole local map --------------------------------------------------------------------------------

    [Fact]
    public void A_colony_on_a_local_map_saves_small_and_loads_the_same()
    {
        var (colony, _) = Founding.Found(7, Arrival, Real, Taiga());
        colony.RunMinutes(60);
        string text = ColonySave.Text(colony);
        output.WriteLine("The save is " + text.Length / 1024 + " KB.");
        Assert.True(text.Length < 400_000, "the save is " + text.Length + " characters");
        var loaded = ColonySave.Read(text, Real);
        Assert.Equal(text, ColonySave.Text(loaded));
        Assert.True(loaded.Ground!.Water(158, 10));
        Assert.Equal("tree", Enumerable.Range(0, 256).Select(x => loaded.Ground.ObstacleAt(x, 0)).First(o => o != null));
        colony.RunMinutes(300);
        loaded.RunMinutes(300);
        Assert.Equal(ColonySave.Fingerprint(colony), ColonySave.Fingerprint(loaded));
    }

    [Fact]
    public void On_a_whole_local_map_the_founders_still_house_themselves_on_the_first_day()
    {
        // As on the test ground (FoundingTests): the small house beside the carts, on a 256 by 256 map with a brook,
        // rough ground and trees. It also shows how long the game takes to work out a day on a ground this big.
        var watch = Stopwatch.StartNew();
        var (colony, carts) = Founding.Found(7, Arrival, Real, Taiga());
        var work = colony.Work!;
        var ground = colony.Ground!;
        var (house, placement) = work.PlanTemplate("small_house", 131, 124);
        Assert.True(placement.Ok, placement.Why);
        var pile = work.AddStockpile("House store", 132, 125, 4, 4);
        long? housed = null;
        colony.RunMinutes(16 * 60, c =>
        {
            if (housed == null && ground.Rooms.Count == 1 && work.Projects.Count == 0)
            {
                housed = c.Now.Tick;
                int i = 0;
                foreach (var bed in work.Bedrolls.ToList())
                    work.MoveBedroll(bed.Id, 132.5 + i++, 128.5);
                carts.Emptying = true;
            }
        });
        output.WriteLine("16 hours on a 256 by 256 map took " + watch.ElapsedMilliseconds + " ms.");
        Assert.NotNull(housed);
        output.WriteLine("The house stood at " + new Moment(housed!.Value) + ".");
        Assert.Equal(Arrival.AbsoluteDay, new Moment(housed.Value).AbsoluteDay);
        Assert.Equal(16, Assert.Single(ground.Rooms).Count);
        Assert.All(work.Bedrolls, b => Assert.True(ground.Indoors(Ground.TileOf(b.X, b.Y).X, Ground.TileOf(b.X, b.Y).Y)));
        Assert.Empty(colony.Stocks[carts.Id].Goods);
        Assert.True(watch.ElapsedMilliseconds < 60_000, "a day took " + watch.ElapsedMilliseconds + " ms");
    }
}
