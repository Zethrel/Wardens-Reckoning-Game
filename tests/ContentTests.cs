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

    static Dictionary<string, string?> Files() => new[] { "goods.json", "recipes.json", "colony.json" }
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
        Assert.Equal(new[] { "flour", "wheat" }, content.Goods.Keys);
        var grind = content.Recipes["grind_wheat"];
        Assert.Equal("quern", grind.Workshop);
        Assert.Equal(5, grind.Inputs["wheat"]);
        Assert.Equal(4, grind.Outputs["flour"]);
        Assert.Equal(180, grind.WorkMinutes);
        Assert.Equal(25, content.CarryKg);
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
        yield return Case("goods.json", r => r["goods"]!.AsArray().Add(7), "goods.json.goods[2]: should be an object");
        yield return Case("goods.json", r => r["version"] = 2, "goods.json: version 2; this game reads version 1");
        yield return Case("recipes.json", r => r["version"] = 0, "recipes.json: version 0; this game reads version 1");
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
