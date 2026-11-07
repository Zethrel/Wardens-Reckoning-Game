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
        Assert.Equal(new[] { "flour", "ration", "water", "wheat" }, content.Goods.Keys);
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
        yield return Case("colony.json", r => r["pickup_minutes"] = -1, "colony.json.pickup_minutes: should be a whole number of 0 or more");
        // Missing, misspelt and badly formed.
        yield return Case("goods.json", r => Good(r, 0).Remove("name"), "goods.json.goods[0]: \"name\" is missing");
        yield return Case("goods.json", r => Good(r, 0)["mass_kgs"] = 1, "goods.json.goods[0]: unknown key \"mass_kgs\"");
        yield return Case("goods.json", r => Good(r, 0)["id"] = "Wheat Grain", "should be lowercase letters, digits and _");
        yield return Case("goods.json", r => Good(r, 0)["name"] = " ", "goods.json.goods[0].name: should be text that is not empty");
        yield return Case("goods.json", r => r["goods"] = 3, "goods.json.goods: should be a list");
        yield return Case("goods.json", r => r["goods"]!.AsArray().Add(7), "goods.json.goods[4]: should be an object");
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
