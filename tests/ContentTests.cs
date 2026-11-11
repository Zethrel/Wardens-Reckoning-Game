// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 13a: the colony's content (content/*.json). The real files must load; FP-10 (content validation): broken
/// content is caught before the colony uses it, each problem named with its file and place.
/// </summary>
public class ContentTests
{
    static string Folder => Path.Combine(AppContext.BaseDirectory, "content");

    static Dictionary<string, string?> Files() => Content.Files
        .ToDictionary(f => f, f => (string?)File.ReadAllText(Path.Combine(Folder, f)));

    /// <summary>The real files with one change made to one of them (as JSON).</summary>
    static Dictionary<string, string?> Changed(string file, Action<JsonObject> change)
    {
        var files = Files();
        var root = JsonNode.Parse(files[file]!)!.AsObject();
        change(root);
        files[file] = root.ToJsonString();
        return files;
    }

    static ContentException Refused(Dictionary<string, string?> files) =>
        Assert.Throws<ContentException>(() => Content.Read(files));

    [Fact]
    public void The_real_content_loads()
    {
        var content = Content.Load(Folder);
        Assert.Equal(new[] { "flour", "foul_water", "ration", "timber", "water", "wheat" }, content.Goods.Keys);
        var grind = content.Recipes["grind_wheat"];
        Assert.Equal("quern", grind.Workshop);
        Assert.Equal(5, grind.Inputs["wheat"]);
        Assert.Equal(4, grind.Outputs["flour"]);
        Assert.Equal(180, grind.WorkMinutes);
        Assert.Equal(25, content.CarryKg);
        Assert.Equal((6, 3, 72), (content.Hunger.PerHour, content.Hunger.AsleepPerHour, content.Hunger.CollapseHours));
        Assert.Equal(("ration", 60.0, 20), (content.Eat.Good, content.Eat.Eases, content.Eat.Minutes));
        Assert.Equal(200, content.SpringPerDay);
        var day = content.Schedules[content.DefaultSchedule].Hours;
        Assert.Equal((Block.Sleep, Block.Meals, Block.Work, Block.Recreation, Block.Flexible, Block.Sleep),
                     (day[6], day[7], day[8], day[17], day[20], day[23]));
        Assert.Equal(70, content.WalkMetresPerMinute);
        Assert.Equal(new[] { "door", "floor", "ice_hole", "road", "wall", "wash_basin", "water_barrel", "well" }, content.Pieces.Keys);
        Assert.Equal((1.0, 0.8, 0.55), (content.Surfaces["road"].Speed, content.Surfaces["grass"].Speed, content.Surfaces["rough"].Speed));
        var well = content.Pieces["well"];
        Assert.Equal((2, 2, (0, 2), 200, 8), (well.Width, well.Height, well.Access!.Value, well.WaterPerDay, well.Materials["timber"]));
        Assert.Empty(content.Pieces["road"].Materials);
        Assert.Equal(1.0, content.Pieces["road"].Speed);
        Assert.Null(content.Pieces["wall"].Speed);
        Assert.Equal("grass", content.TestGround.Surface);
        var wall = content.Pieces["wall"];
        Assert.Equal((2, 45, true), (wall.Materials["timber"], wall.WorkMinutes, wall.Blocks));
        Assert.False(content.Pieces["door"].Blocks);
        Assert.Equal((48, 48), (content.TestGround.Width, content.TestGround.Height));
        Assert.Contains(new Obstacle("rock", 30, 8), content.TestGround.Obstacles);
        Assert.True(content.Pieces["door"].Encloses);
        Assert.False(content.Pieces["floor"].Encloses);
        Assert.Equal((3, 60, true), (content.Founding.Founders, content.Founding.Supplies["timber"], content.Founding.BedrollEach));
    }

    public static IEnumerable<object[]> Broken()
    {
        object[] Case(string file, Action<JsonObject> change, string problem) => new object[] { Changed(file, change), problem };
        JsonObject Good(JsonObject r, int i) => r["goods"]![i]!.AsObject();
        JsonObject Recipe(JsonObject r) => r["recipes"]![0]!.AsObject();

        // Duplicate identifiers.
        yield return Case("goods.json", r => Good(r, 1)["id"] = "wheat", "goods.json.goods[1]: a second good with the id \"wheat\"");
        yield return Case("recipes.json", r => r["workshops"]!.AsArray().Add(new JsonObject { ["id"] = "quern", ["name"] = "Q" }),
                          "recipes.json.workshops[1]: a second workshop with the id \"quern\"");
        yield return Case("recipes.json", r => r["recipes"]!.AsArray().Add(Recipe(r).DeepClone()),
                          "recipes.json.recipes[1]: a second recipe with the id \"grind_wheat\"");
        // Missing references.
        yield return Case("recipes.json", r => Recipe(r)["inputs"] = new JsonObject { ["barley"] = 5 },
                          "recipes.json.recipes[0].inputs.barley: no good \"barley\"");
        yield return Case("recipes.json", r => Recipe(r)["workshop"] = "mill", "recipes.json.recipes[0].workshop: no workshop \"mill\"");
        // Invalid quantities.
        yield return Case("recipes.json", r => Recipe(r)["outputs"] = new JsonObject { ["flour"] = 0 },
                          "recipes.json.recipes[0].outputs.flour: should be a whole number of 1 or more");
        yield return Case("recipes.json", r => Recipe(r)["inputs"] = new JsonObject { ["wheat"] = 2.5 },
                          "recipes.json.recipes[0].inputs.wheat: should be a whole number of 1 or more");
        yield return Case("recipes.json", r => Recipe(r)["inputs"] = new JsonObject { ["wheat"] = "five" },
                          "recipes.json.recipes[0].inputs.wheat: should be a whole number of 1 or more");
        yield return Case("recipes.json", r => Recipe(r)["inputs"] = new JsonObject(), "recipes.json.recipes[0].inputs: should be a table of goods and amounts, not empty");
        yield return Case("recipes.json", r => Recipe(r)["work_minutes"] = 0, "recipes.json.recipes[0].work_minutes: should be a whole number of 1 or more");
        yield return Case("goods.json", r => Good(r, 0)["mass_kg"] = 0, "goods.json.goods[0].mass_kg: should be a number above 0");
        yield return Case("goods.json", r => Good(r, 0)["volume_l"] = -1, "goods.json.goods[0].volume_l: should be a number above 0");
        yield return Case("colony.json", r => r["carry_kg"] = "a lot", "colony.json.carry_kg: should be a number above 0");
        yield return Case("colony.json", r => r["pickup_seconds"] = -1, "colony.json.pickup_seconds: should be a whole number of 0 or more");
        yield return Case("colony.json", r => r["drop_seconds"] = 2.5, "colony.json.drop_seconds: should be a whole number of 0 or more");
        yield return Case("colony.json", r => r["think_seconds"] = new JsonArray(0, 3), "colony.json.think_seconds: should be two whole numbers [a, b] of 1 or more");
        yield return Case("colony.json", r => r["think_seconds"] = new JsonArray(7, 2), "colony.json.think_seconds: the first should not be more than the second");
        yield return Case("colony.json", r => r.Remove("think_seconds"), "colony.json: \"think_seconds\" is missing");
        // Missing, misspelt and badly formed.
        yield return Case("goods.json", r => Good(r, 0).Remove("name"), "goods.json.goods[0]: \"name\" is missing");
        yield return Case("goods.json", r => Good(r, 0)["mass_kgs"] = 1, "goods.json.goods[0]: unknown key \"mass_kgs\"");
        yield return Case("goods.json", r => Good(r, 0)["id"] = "Wheat Grain", "should be lowercase letters, digits and _");
        yield return Case("goods.json", r => Good(r, 0)["name"] = " ", "goods.json.goods[0].name: should be text that is not empty");
        yield return Case("goods.json", r => r["goods"] = 3, "goods.json.goods: should be a list");
        yield return Case("goods.json", r => r["goods"]!.AsArray().Add(7), "goods.json.goods[6]: should be an object");
        yield return Case("goods.json", r => r["version"] = 2, "goods.json: version 2; this game reads version 1");
        yield return Case("recipes.json", r => r["version"] = 0, "recipes.json: version 0; this game reads version 1");
        // Needs and schedules.
        JsonObject Hunger(JsonObject r) => r["needs"]!["hunger"]!.AsObject();
        JsonObject Day(JsonObject r) => r["templates"]![0]!.AsObject();
        yield return Case("needs.json", r => Hunger(r)["wants"] = 70, "needs.json.needs.hunger: should have peckish < wants < urgent < critical <= 100");
        yield return Case("needs.json", r => Hunger(r)["critical"] = 101, "should have peckish < wants < urgent < critical <= 100");
        yield return Case("needs.json", r => Hunger(r)["per_hour"] = -1, "needs.json.needs.hunger.per_hour: should be a number of 0 or more");
        yield return Case("needs.json", r => r["needs"]!.AsObject().Remove("thirst"), "needs.json.needs: \"thirst\" is missing");
        yield return Case("needs.json", r => r["needs"]!["boredom"] = new JsonObject(), "needs.json.needs: unknown key \"boredom\"");
        // Water (step 19a).
        JsonObject Water(JsonObject r) => r["water"]!.AsObject();
        yield return Case("needs.json", r => r["needs"]!.AsObject().Remove("hygiene"), "needs.json.needs: \"hygiene\" is missing");
        yield return Case("needs.json", r => r["wash"]!["good"] = "timber", "needs.json.wash.good: should be the drink's good, \"water\"");
        yield return Case("needs.json", r => r["wash"]!["good"] = "soap", "needs.json.wash.good: no good \"soap\"");
        yield return Case("needs.json", r => r["wash"]!["litres"] = 0, "needs.json.wash.litres: should be a whole number of 1 or more");
        yield return Case("needs.json", r => r["wash"] = 3, "needs.json.wash: should be an object");
        yield return Case("needs.json", r => Water(r)["brook_litres_by_month"] = new JsonArray(1, 2), "needs.json.water.brook_litres_by_month: should be 12 whole numbers of 0 or more");
        yield return Case("needs.json", r => Water(r)["brook_litres_by_month"]![3] = -5, "needs.json.water.brook_litres_by_month: should be 12");
        yield return Case("needs.json", r => Water(r)["freezing_months"] = new JsonArray(13), "needs.json.water.freezing_months: should be a list of months, 1 to 12");
        yield return Case("needs.json", r => Water(r)["cold_biomes"] = new JsonArray(4), "needs.json.water.cold_biomes: should be a list of biome names");
        yield return Case("needs.json", r => Water(r)["load_litres"] = 0, "needs.json.water.load_litres: should be a whole number of 1 or more");
        yield return Case("needs.json", r => Water(r)["ice_hole"] = "wall", "needs.json.water.ice_hole: \"wall\" is not a piece of construction.json with \"opens_ice\": true");
        yield return Case("needs.json", r => Water(r).Remove("plenty_litres"), "needs.json.water: \"plenty_litres\" is missing");
        yield return Case("needs.json", r => r["water"] = "lots", "needs.json.water: should be an object");
        yield return Case("needs.json", r => r["needs"]!["hygiene"]!["collapse_hours"] = -1, "needs.json.needs.hygiene.collapse_hours: should be a number of 0 or more");
        yield return Case("construction.json", r => Wall(r)["wash_place"] = true, "construction.json.pieces[0].wash_place: a wash place holds water");
        yield return Case("construction.json", r => Wall(r).Remove("holds_litres"), "construction.json.pieces[0]: \"holds_litres\" is missing");
        yield return Case("construction.json", r => Wall(r)["opens_ice"] = "yes", "construction.json.pieces[0].opens_ice: should be true or false");
        yield return Case("needs.json", r => r["needs"]!["hunger"] = 5, "needs.json.needs.hunger: should be an object");
        yield return Case("needs.json", r => r["eat"]!["good"] = "cake", "needs.json.eat.good: no good \"cake\"");
        yield return Case("needs.json", r => r["drink"]!["minutes"] = 0, "needs.json.drink.minutes: should be a whole number of 1 or more");
        yield return Case("needs.json", r => r["drink"] = "water", "needs.json.drink: should be an object");
        yield return Case("needs.json", r => r["sleep"]!["ground_per_hour"] = 0, "needs.json.sleep.ground_per_hour: should be a number above 0");
        yield return Case("needs.json", r => r["spring_litres_per_day"] = -5, "needs.json.spring_litres_per_day: should be a whole number of 0 or more");
        yield return Case("schedules.json", r => Day(r)["hours"]!.AsArray().RemoveAt(0), "hours: should be a list of 24 blocks");
        yield return Case("schedules.json", r => Day(r)["hours"]![3] = "nap", "schedules.json.templates[0].hours[3]: should be one of sleep, work, meals, recreation, flexible");
        yield return Case("schedules.json", r => Day(r)["hours"]![3] = "Work", "hours[3]: should be one of");
        yield return Case("schedules.json", r => r["default"] = "night_shift", "schedules.json.default: no schedule \"night_shift\"");
        yield return Case("schedules.json", r => r["templates"]!.AsArray().Add(Day(r).DeepClone()), "a second schedule with the id \"standard_day\"");
        // Construction pieces and the test ground.
        JsonObject Wall(JsonObject r) => r["pieces"]![0]!.AsObject();
        JsonObject TestGround(JsonObject r) => r["test_ground"]!.AsObject();
        JsonObject Rock(JsonObject r) => r["test_ground"]!["obstacles"]![0]!.AsObject();
        yield return Case("construction.json", r => r["pieces"]!.AsArray().Add(Wall(r).DeepClone()), "construction.json.pieces[8]: a second piece with the id \"wall\"");
        yield return Case("construction.json", r => Wall(r)["materials"] = new JsonObject { ["stone"] = 2 }, "construction.json.pieces[0].materials.stone: no good \"stone\"");
        yield return Case("construction.json", r => Wall(r)["work_minutes"] = 0, "construction.json.pieces[0].work_minutes: should be a whole number of 1 or more");
        yield return Case("construction.json", r => Wall(r)["blocks"] = "yes", "construction.json.pieces[0].blocks: should be true or false");
        yield return Case("construction.json", r => TestGround(r)["width"] = 0, "construction.json.test_ground.width: should be a whole number of 1 or more");
        yield return Case("construction.json", r => Rock(r)["x"] = 48, "construction.json.test_ground.obstacles[0]: (48, 8) is outside the ground (48 by 48)");
        yield return Case("construction.json", r => Rock(r)["x"] = 31, "construction.json.test_ground.obstacles[1]: a second obstacle on (31, 8)");
        yield return Case("construction.json", r => Rock(r)["kind"] = "Big Rock", "construction.json.test_ground.obstacles[0].kind: \"Big Rock\" should be lowercase");
        yield return Case("construction.json", r => r.Remove("test_ground"), "construction.json: \"test_ground\" is missing");
        yield return Case("construction.json", r => r["test_ground"] = 5, "construction.json.test_ground: should be an object");
        // Surfaces, speeds, footprints.
        JsonObject Well(JsonObject r) => r["pieces"]![4]!.AsObject();
        JsonObject Grass(JsonObject r) => r["surfaces"]![1]!.AsObject();
        yield return Case("construction.json", r => Grass(r)["speed"] = 1.2, "construction.json.surfaces[1].speed: should be a number from 0 (cannot be walked on) and at most 1");
        yield return Case("construction.json", r => Grass(r)["speed"] = -0.1, "construction.json.surfaces[1].speed: should be a number from 0 (cannot be walked on) and at most 1");
        yield return Case("construction.json", r => r["surfaces"]!.AsArray().Add(Grass(r).DeepClone()), "construction.json.surfaces[8]: a second surface with the id \"grass\"");
        yield return Case("construction.json", r => r["test_ground"]!["surface"] = "gravel", "construction.json.test_ground.surface: no surface \"gravel\"");
        yield return Case("construction.json", r => r["test_ground"]!["surface"] = "fresh_water", "construction.json.test_ground.surface: \"fresh_water\" cannot be walked on");
        yield return Case("construction.json", r => r["test_ground"]!["patches"]![0]!["surface"] = "gravel", "construction.json.test_ground.patches[0].surface: no surface \"gravel\"");
        yield return Case("construction.json", r => r["test_ground"]!["patches"]![0]!["width"] = 20, "construction.json.test_ground.patches[0]: it reaches outside the ground");
        yield return Case("construction.json", r => Wall(r)["speed"] = 2, "construction.json.pieces[0].speed: should be a number above 0 and at most 1");
        yield return Case("construction.json", r => Wall(r)["speed"] = 0, "construction.json.pieces[0].speed: should be a number above 0 and at most 1");
        yield return Case("construction.json", r => Well(r)["access"] = new JsonArray(1, 1), "construction.json.pieces[4].access: (1, 1) is under the piece itself");
        yield return Case("construction.json", r => Well(r)["access"] = null, "construction.json.pieces[4].access: a piece bigger than one tile needs a tile it is built and used from");
        yield return Case("construction.json", r => Well(r)["size"] = new JsonArray(0, 2), "construction.json.pieces[4].size: should be two whole numbers [a, b] of 1 or more");
        yield return Case("construction.json", r => Well(r)["water_per_day"] = -5, "construction.json.pieces[4].water_per_day: should be a whole number of 0 or more");
        // Templates and the founding.
        JsonObject House(JsonObject r) => r["templates"]![0]!.AsObject();
        yield return Case("construction.json", r => House(r)["rows"]![1] = "W...W", "construction.json.templates[0].rows[1]: every row should be text of the same length");
        yield return Case("construction.json", r => House(r)["rows"]![1] = "W.X..W", "construction.json.templates[0].rows[1]: \"X\" is not in the key");
        yield return Case("construction.json", r => House(r)["key"]!["W"] = "tower", "construction.json.templates[0].key.W: no piece \"tower\"");
        yield return Case("construction.json", r => House(r)["rows"] = new JsonArray("....", "...."), "construction.json.templates[0].rows: no pieces at all");
        yield return Case("construction.json", r => r["templates"]!.AsArray().Add(House(r).DeepClone()), "a second template with the id \"small_house\"");
        yield return Case("construction.json", r => r["pieces"]![1]!.AsObject().Remove("encloses"), "construction.json.pieces[1]: \"encloses\" is missing");
        yield return Case("founding.json", r => r["founders"] = 13, "founding.json.names: 12 names for 13 founders");
        yield return Case("founding.json", r => r["founders"] = 0, "founding.json.founders: should be a whole number of 1 or more");
        yield return Case("founding.json", r => r["names"]![1] = "Ada", "founding.json.names[1]: \"Ada\" is there twice");
        yield return Case("founding.json", r => r["supplies"]!["gold"] = 5, "founding.json.supplies.gold: no good \"gold\"");
        yield return Case("founding.json", r => r["bedroll_each"] = 1, "founding.json.bedroll_each: should be true or false");
        // Goods made from themselves.
        yield return Case("recipes.json", r => Recipe(r)["outputs"] = new JsonObject { ["flour"] = 4, ["wheat"] = 1 },
                          "\"wheat\" is made from itself");
        yield return Case("recipes.json", r => r["recipes"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "unmill", ["name"] = "Unmill", ["workshop"] = "quern", ["inputs"] = new JsonObject { ["flour"] = 1 },
            ["outputs"] = new JsonObject { ["wheat"] = 1 }, ["work_minutes"] = 1,
        }), "\"flour\" is made from itself");
    }

    [Theory]
    [MemberData(nameof(Broken))]
    public void FP10_broken_content_is_caught_and_named(Dictionary<string, string?> files, string problem) =>
        Assert.Contains(Refused(files).Problems, p => p.Contains(problem));

    [Fact]
    public void FP10_broken_JSON_and_a_missing_file_are_named()
    {
        var files = Files();
        files["goods.json"] = "{ \"version\": 1, \"goods\": [ ";
        files["colony.json"] = null;
        var problems = Refused(files).Problems;
        Assert.Contains(problems, p => p.StartsWith("goods.json: not valid JSON"));
        Assert.Contains("colony.json: the file is missing", problems);
    }

    [Fact]
    public void FP10_every_problem_is_reported_at_once()
    {
        // Two problems that do not lead to others (a broken good would also make every recipe naming it wrong).
        var files = Changed("colony.json", r => r["carry_kg"] = 0);
        var recipes = JsonNode.Parse(files["recipes.json"]!)!.AsObject();
        recipes["recipes"]![0]!["workshop"] = "mill";
        files["recipes.json"] = recipes.ToJsonString();
        var e = Refused(files);
        Assert.Equal(2, e.Problems.Count);
        Assert.Contains("2 problems", e.Message);
    }

    [Fact]
    public void Content_that_is_not_an_object_is_refused() =>
        Assert.Contains("recipes.json: should be one JSON object { ... }",
            Refused(new Dictionary<string, string?>(Files()) { ["recipes.json"] = "[]" }).Problems);
}
