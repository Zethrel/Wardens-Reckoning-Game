// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json;
using System.Text.Json.Nodes;
using WardensReckoning.World;

namespace WardensReckoning.Sim;

/// <summary>
/// Saving and loading a game. A save is a JSON file holding where the exported world is, the game's seed and
/// year, everything the years carry (Game), and every year the game has run (so the map and the slider show
/// them again after loading). It does not copy the export: the export must still be where it was.
/// </summary>
public static class SaveFile
{
    public const string Format = "wardens-reckoning-save";

    /// <summary>The save's version: raised when a part changes its meaning, so an older game refuses a newer save.</summary>
    public const int Version = 1;

    public static void Write(string path, Game game)
    {
        var world = game.World;
        var years = new JsonArray();
        for (int year = world.ExportYears + 1; year <= world.Years; year++)
        {
            var states = Enumerable.Range(0, world.Towns.Count).Select(i => world.Town(i, year)).ToList();
            years.Add(new JsonObject
            {
                ["people"] = Numbers(states.Select(s => s?.People ?? 0)),
                ["holders"] = new JsonArray(states.Select(s => (JsonNode?)(s?.Holder is int h ? JsonValue.Create(h) : null)).ToArray()),
                ["ruined"] = new JsonArray(states.Select(s => (JsonNode?)JsonValue.Create(s?.Ruined ?? false)).ToArray()),
                ["factions"] = Numbers(Enumerable.Range(0, world.Factions.Count).Select(f => world.PeopleOf(f, year))),
            });
        }
        var save = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["world"] = Path.GetFullPath(Path.Combine(world.Folder, "world.json")),
            ["world_seed"] = world.Seed,
            ["history_seed"] = world.HistorySeed,
            ["export_years"] = world.ExportYears,
            ["seed"] = game.Seed,
            ["year"] = game.Year,
            ["towns"] = new JsonArray(game.Towns.Select(t => (JsonNode)new JsonObject
            {
                ["people"] = t.People, ["size"] = t.Size, ["low_years"] = t.LowYears, ["hungry"] = t.Hungry,
                ["ruined"] = t.Ruined, ["holder"] = t.Holder, ["fed"] = t.Fed,
            }).ToArray()),
            ["factions"] = new JsonArray(game.Factions.Select(f => (JsonNode)new JsonObject
            {
                ["people"] = f.People, ["path"] = f.Path, ["government"] = f.Government, ["competence"] = f.Competence,
            }).ToArray()),
            ["events"] = new JsonArray(game.Events.Select(e => (JsonNode)new JsonObject
            {
                ["year"] = e.Year, ["kind"] = e.Kind, ["what"] = e.What, ["text"] = e.Text,
                ["factions"] = Numbers(e.Factions),
                ["facts"] = new JsonObject(e.Facts.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
            }).ToArray()),
            ["years"] = years,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Written to a new file first and then moved over the old, so a crash while saving never leaves half a save.
        string fresh = path + ".new";
        File.WriteAllText(fresh, save.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        File.Move(fresh, path, overwrite: true);
    }

    static JsonArray Numbers(IEnumerable<int> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    /// <summary>Reads a save: opens its export again, replays the years it ran, and puts back what they carry.
    /// Refused (WorldFileException) if the file is not a save this game reads, or its export is gone or changed.</summary>
    public static Game Read(string path)
    {
        JsonObject save;
        try
        {
            save = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new WorldFileException(path + " is not a save: " + e.Message);
        }
        if ((string?)save["format"] != Format)
            throw new WorldFileException(path + " is not a save of this game");
        if (save["version"] is not JsonValue v || !v.TryGetValue(out int version) || version > Version)
            throw new WorldFileException(path + " is save version " + save["version"] + "; this game reads up to " + Version);

        string worldPath = (string)save["world"]!;
        if (!File.Exists(worldPath))
            throw new WorldFileException("the world this game was played on is no longer at " + worldPath);
        var world = ExportedWorld.Read(worldPath);
        if (world.Seed != (int)save["world_seed"]! || world.HistorySeed != (int?)save["history_seed"]
            || world.ExportYears != (int)save["export_years"]!)
            throw new WorldFileException("the world at " + worldPath + " is not the one this game was played on " +
                                         "(it was exported again with other seeds or years)");

        var game = new Game(world, (long)save["seed"]!);
        foreach (var node in save["years"]!.AsArray())
        {
            var year = node!.AsObject();
            world.AddYear(year["people"]!.AsArray().Select(p => (int)p!).ToList(),
                          year["holders"]!.AsArray().Select(h => (int?)h).ToList(),
                          year["ruined"]!.AsArray().Select(r => (bool)r!).ToList(),
                          year["factions"]!.AsArray().Select(p => (int)p!).ToList());
        }
        game.Year = (int)save["year"]!;
        if (game.Year != world.Years)
            throw new WorldFileException(path + " is damaged: its year and its years do not agree");

        var towns = save["towns"]!.AsArray();
        for (int i = 0; i < game.Towns.Count; i++)
        {
            var t = towns[i]!.AsObject();
            var town = game.Towns[i];
            town.People = (double)t["people"]!;
            town.Size = (int)t["size"]!;
            town.LowYears = (int)t["low_years"]!;
            town.Hungry = (int)t["hungry"]!;
            town.Ruined = (bool)t["ruined"]!;
            town.Holder = (int?)t["holder"];
            town.Fed = (double)t["fed"]!;
        }
        var factions = save["factions"]!.AsArray();
        for (int f = 0; f < game.Factions.Count; f++)
        {
            var o = factions[f]!.AsObject();
            game.Factions[f].People = (int)o["people"]!;
            game.Factions[f].Path = (string?)o["path"];
            game.Factions[f].Government = (string?)o["government"] ?? "";
            game.Factions[f].Competence = (double?)o["competence"];
        }
        foreach (var node in save["events"]!.AsArray())
        {
            var e = node!.AsObject();
            game.Record(new GameEvent((int)e["year"]!, (string)e["kind"]!, (string)e["what"]!, (string)e["text"]!,
                                          e["factions"]!.AsArray().Select(f => (int)f!).ToList(),
                                          e["facts"]!.AsObject().ToDictionary(p => p.Key, p => (string?)p.Value ?? "")));
        }
        return game;
    }
}
