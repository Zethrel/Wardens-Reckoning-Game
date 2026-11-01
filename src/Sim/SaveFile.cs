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
    // 2: trade (the mines' metal, the memories, the factions' metal). 3: rulers (every faction's ruler, ways,
    // grievance, prejudices and opinions; the words between factions; the dice; the names used).
    public const int Version = 3;

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
                ["states"] = new JsonArray(Enumerable.Range(0, world.Factions.Count)
                    .Select(f => (JsonNode?)world.Faction(f, year)?.DeepClone()).ToArray()),
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
                ["people"] = f.People, ["path"] = f.Path, ["government"] = f.Government,
                ["metal_supply"] = f.MetalSupply, ["metal_need"] = f.MetalNeed,
                ["ruler"] = Game.WriteRuler(f.Ruler), ["traits"] = Table(f.Traits), ["values"] = Table(f.Values),
                ["grievance"] = f.Grievance, ["rulers"] = f.RulerCount, ["revolutions"] = f.Revolutions,
                ["path_changes"] = f.PathChanges, ["prejudice"] = Table(f.Prejudice),
                ["cult"] = f.Cult == null ? null : new JsonObject
                {
                    ["traits"] = Table(f.Cult.Traits), ["until"] = f.Cult.Until, ["strength"] = f.Cult.Strength,
                    ["name"] = f.Cult.Name,
                },
                ["split_pending"] = f.SplitPending,
                ["held"] = f.Held == null ? null : new JsonArray(f.Held.OrderBy(p => p, StringComparer.Ordinal)
                                                               .Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
                ["opinions"] = new JsonArray(f.Opinions.OrderBy(o => o.Key)
                    .Select(o => (JsonNode)new JsonArray(o.Key, o.Value)).ToArray()),
            }).ToArray()),
            ["words"] = new JsonArray(game.Words.OrderBy(p => p.Key)
                .Select(p => (JsonNode)new JsonArray(p.Key.Item1, p.Key.Item2, p.Value)).ToArray()),
            ["names_used"] = Numbers(game.NamesUsed),
            // The dice's state is four 64-bit numbers each, written as text: JSON numbers cannot hold them exactly.
            ["dice"] = new JsonObject(game.AllDice.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d =>
                KeyValuePair.Create(d.Key, (JsonNode?)new JsonArray(d.Value.State.Select(n => (JsonNode?)JsonValue.Create(n.ToString())).ToArray())))),
            ["mines"] = new JsonArray(game.Mines.Select(m => (JsonNode?)JsonValue.Create(m.Metal)).ToArray()),
            ["memory"] = new JsonArray(game.Memory.OrderBy(p => p.Key)
                .Select(p => (JsonNode)new JsonArray(p.Key.Item1, p.Key.Item2, p.Value)).ToArray()),
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

    // A table of named numbers (traits, prejudices) as JSON, and back.
    static JsonObject Table(Dictionary<string, double> table) =>
        new(table.Select(t => KeyValuePair.Create(t.Key, (JsonNode?)JsonValue.Create(t.Value))));

    static Dictionary<string, double> ReadTable(JsonNode? node) =>
        node!.AsObject().ToDictionary(p => p.Key, p => (double)p.Value!);

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
        if (version < Version)
            throw new WorldFileException(path + " was saved by an older game (save version " + version +
                                         "), before the rulers came to it: it cannot go on. Start a new game on the world.");

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
                          year["factions"]!.AsArray().Select(p => (int)p!).ToList(),
                          year["states"]!.AsArray().Select(o => o as JsonObject).ToList());
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
            var faction = game.Factions[f];
            faction.MetalSupply = (double)o["metal_supply"]!;
            faction.MetalNeed = (double)o["metal_need"]!;
            faction.Ruler = Game.ReadRuler(o["ruler"] as JsonObject);
            faction.Traits = ReadTable(o["traits"]);
            faction.Values = ReadTable(o["values"]);
            faction.Grievance = (double)o["grievance"]!;
            faction.RulerCount = (int)o["rulers"]!;
            faction.Revolutions = (int)o["revolutions"]!;
            faction.PathChanges = (int)o["path_changes"]!;
            faction.Prejudice = ReadTable(o["prejudice"]);
            faction.Cult = o["cult"] is JsonObject cult ? new Cult
            {
                Traits = ReadTable(cult["traits"]), Until = (int)cult["until"]!, Strength = (double)cult["strength"]!,
                Name = (string)cult["name"]!,
            } : null;
            faction.SplitPending = (bool)o["split_pending"]!;
            faction.Held = o["held"] is JsonArray held ? held.Select(p => (string)p!).ToHashSet() : null;
            faction.Opinions = o["opinions"]!.AsArray().ToDictionary(p => (int)p![0]!, p => (double)p![1]!);
        }
        game.Words.Clear();
        foreach (var pair in save["words"]!.AsArray())
            game.Words[((int)pair![0]!, (int)pair[1]!)] = (int)pair[2]!;
        game.NamesUsed = save["names_used"]!.AsArray().Select(n => (int)n!).ToArray();
        foreach (var (system, state) in save["dice"]!.AsObject())
            game.SetDice(system, new Dice(state!.AsArray().Select(n => ulong.Parse((string)n!)).ToList()));
        var mines = save["mines"]!.AsArray();
        for (int k = 0; k < game.Mines.Count; k++)
            game.Mines[k].Metal = (double)mines[k]!;
        game.Memory.Clear();
        foreach (var pair in save["memory"]!.AsArray())
            game.Memory[((int)pair![0]!, (int)pair[1]!)] = (double)pair[2]!;
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
