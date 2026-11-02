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
    // 4: war (the wars and alliances, the towns' origins and conquests, the mines' owners, the regions' holders,
    // the factions' borders, and each year's borders when they changed). 5: splits (the factions the game founded,
    // every faction's capital, nature, parent, hold and the rest, the factions still waiting to rise, the names given).
    // 6: feeding and drift (each town's food, capacity, harvest and who it was fed as; what each faction's land encourages).
    // 7: migration (the towns the game founded, who farms each region, the new routes, the free land left, the own
    // site scores worked out, the names given). 8: attunement (every patch's mark and what the marks have done; the
    // rift names used; and each war's great rituals; 8c and 9a add parts that an older save of version 8 lacks and
    // reads as none: the founded paths and founders, the hidden metal, the signs, the mines found).
    public const int Version = 8;

    public static void Write(string path, Game game)
    {
        var world = game.World;
        var years = new JsonArray();
        var lastBorders = world.Borders(world.ExportYears);
        int exported = world.Data["factions"]!.AsArray().Count;
        int exportedTowns = world.Data["settlements"]!.AsArray().Count;
        int exportedRoutes = world.Data["routes"]!.AsArray().Count;
        for (int year = world.ExportYears + 1; year <= world.Years; year++)
        {
            // The towns there were that year: the export's, and those the game's migrants had founded by then.
            int towns = exportedTowns + Enumerable.Range(exportedTowns, world.Towns.Count - exportedTowns)
                                                  .Count(t => world.Towns[t].From <= year);
            var states = Enumerable.Range(0, towns).Select(i => world.Town(i, year)).ToList();
            // The factions there were that year: the export's, and those the game had founded by then (in order).
            int there = exported + Enumerable.Range(exported, world.Factions.Count - exported).Count(f => world.FromOf(f) <= year);
            var borders = world.Borders(year);
            JsonNode? changed = null;
            if (borders != null && (lastBorders == null || !borders.SequenceEqual(lastBorders)))
                changed = Runs(borders);                   // only the years the borders changed
            lastBorders = borders;
            years.Add(new JsonObject
            {
                // The factions the game founded this year, as the world's records list them.
                ["new_towns"] = new JsonArray(Enumerable.Range(exportedTowns, world.Towns.Count - exportedTowns)
                    .Where(t => world.Towns[t].From == year)
                    .Select(t => (JsonNode?)new JsonObject { ["name"] = world.Towns[t].Name, ["x"] = world.Towns[t].X,
                                                            ["y"] = world.Towns[t].Y }).ToArray()),
                ["new_factions"] = new JsonArray(Enumerable.Range(exported, world.Factions.Count - exported)
                    .Where(f => world.FromOf(f) == year).Select(f => (JsonNode?)Info(world.Factions[f])).ToArray()),
                ["borders"] = changed,
                ["people"] = Numbers(states.Select(s => s?.People ?? 0)),
                ["holders"] = new JsonArray(states.Select(s => (JsonNode?)(s?.Holder is int h ? JsonValue.Create(h) : null)).ToArray()),
                ["ruined"] = new JsonArray(states.Select(s => (JsonNode?)JsonValue.Create(s?.Ruined ?? false)).ToArray()),
                ["factions"] = Numbers(Enumerable.Range(0, there).Select(f => world.PeopleOf(f, year))),
                ["states"] = new JsonArray(Enumerable.Range(0, there)
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
                ["origin"] = t.Origin, ["conquered_in"] = t.ConqueredIn, ["founded_from"] = t.FoundedFrom,
                ["food"] = t.Food, ["capacity"] = t.Capacity,
                ["fed_as"] = t.FedAs is { } fed ? new JsonArray(fed.People, fed.Path) : null,
                ["yields"] = new JsonArray(t.Yields.Select(y => (JsonNode)new JsonArray(y.Key, y.Value)).ToArray()),
                ["x"] = t.X, ["y"] = t.Y, ["hx"] = t.HX, ["hy"] = t.HY, ["cells"] = Numbers(t.Cells),
                ["near"] = new JsonArray(t.Near.OrderBy(k => k, StringComparer.Ordinal).Select(k => (JsonNode?)k).ToArray()),
            }).ToArray()),
            ["owner"] = Runs(game.Grid.Owner),
            ["new_routes"] = new JsonArray(game.Routes.Skip(exportedRoutes)
                .Select(r => (JsonNode)new JsonArray(r.A, r.B, r.Cost)).ToArray()),
            ["mine_land_towns"] = new JsonArray(game.Mines.Select(m => (JsonNode?)(m.LandTown is int t ? JsonValue.Create(t) : null)).ToArray()),
            ["settlement_names_used"] = Numbers(game.SettlementNamesUsed),
            ["rift_names_used"] = Numbers(game.RiftNamesUsed),
            // The god of death (step 9a): the metal still hidden, the signs' lasting harvests, the mine names used, the
            // mines found in the game (every mine's metal and owner are below), and every mine's first metal.
            ["hidden"] = Death.Write(game.Hidden),
            ["signs_lasting"] = new JsonObject(game.SignsLasting.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(
                p.Key.ToString(), (JsonNode?)new JsonArray(p.Value.Select(e => (JsonNode)new JsonArray(e.First, e.Last, e.Factor)).ToArray())))),
            ["mine_names_used"] = Numbers(game.MineNamesUsed),
            ["found_mines"] = new JsonArray(game.Mines.Skip(game.Mines.Count - game.FoundMines).Select(m => (JsonNode)new JsonObject
            {
                ["name"] = m.Name, ["x"] = m.X, ["y"] = m.Y, ["hx"] = m.HX, ["hy"] = m.HY,
            }).ToArray()),
            ["mine_first"] = new JsonArray(game.Mines.Select(m => (JsonNode)m.First).ToArray()),
            // The named threats (step 9b): every threat, the names given from each kind's list, the spoiled
            // harvests, and the threat names used.
            ["threats"] = Threats.Write(game.Threats),
            // The happenings and eras (step 9c).
            ["happenings"] = Happenings.Write(game),
            ["threat_names_given"] = new JsonObject(game.ThreatNamesGiven.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
            ["spoiled"] = new JsonObject(game.Spoiled.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(
                p.Key.ToString(), (JsonNode?)new JsonArray(p.Value.Year, p.Value.Factor)))),
            ["threat_names_used"] = Numbers(game.ThreatNamesUsed),
            ["marks"] = game.Marks?.Write(),
            ["moving"] = new JsonObject
            {
                ["sites"] = game.Moving.Sites == null ? null : Movements.WriteSites(game.Moving.Sites),
                ["own_sites"] = new JsonArray(game.Moving.OwnSites.OrderBy(p => p.Key.People).ThenBy(p => p.Key.Path, StringComparer.Ordinal)
                    .Select(p => (JsonNode)new JsonArray(p.Key.People, p.Key.Path, Movements.WriteSites(p.Value))).ToArray()),
                ["tastes"] = new JsonArray(game.Moving.Tastes.OrderBy(p => p.Key.People).ThenBy(p => p.Key.Path, StringComparer.Ordinal)
                    .Select(p => (JsonNode)new JsonArray(p.Key.People, p.Key.Path, Movements.Exact(p.Value))).ToArray()),
                ["town_scores"] = Movements.Exact(game.Moving.TownScores),
            },
            ["drift_targets"] = new JsonObject(game.DriftTargets.OrderBy(p => p.Key)
                .Select(p => KeyValuePair.Create(p.Key.ToString(), (JsonNode?)Table(p.Value)))),
            ["factions"] = new JsonArray(game.Factions.Select(f => (JsonNode)new JsonObject
            {
                ["name"] = f.Name, ["folk"] = f.Folk, ["lineage"] = f.Lineage, ["capital"] = f.Capital,
                ["parent"] = f.Parent, ["founded"] = f.Founded, ["nature"] = Table(f.Nature), ["belief"] = f.Belief,
                ["hold"] = f.Hold, ["skills"] = Table(f.Skills),
                ["learned"] = new JsonArray(f.Learned.OrderBy(g => g, StringComparer.Ordinal).Select(g => (JsonNode?)g).ToArray()),
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
                ["border"] = new JsonArray(f.Border.OrderBy(b => b.Key).Select(b => (JsonNode)new JsonArray(b.Key, b.Value)).ToArray()),
                ["held"] = f.Held == null ? null : new JsonArray(f.Held.OrderBy(p => p, StringComparer.Ordinal)
                                                               .Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
                ["opinions"] = new JsonArray(f.Opinions.OrderBy(o => o.Key)
                    .Select(o => (JsonNode)new JsonArray(o.Key, o.Value)).ToArray()),
            }).ToArray()),
            ["words"] = new JsonArray(game.Words.OrderBy(p => p.Key)
                .Select(p => (JsonNode)new JsonArray(p.Key.Item1, p.Key.Item2, p.Value)).ToArray()),
            ["names_used"] = Numbers(game.NamesUsed),
            ["people_names_used"] = Numbers(game.PeopleNamesUsed),
            ["waiting"] = new JsonArray(game.Waiting.Select(w => (JsonNode)new JsonArray(w.Key, w.Folk)).ToArray()),
            ["founders"] = Numbers(game.Founders.Order().ToArray()),
            // The paths the game founded (step 8c), with their values, to be put back in order.
            ["founded_paths"] = new JsonArray(game.Settings.Founded.Select(p => (JsonNode)new JsonObject
            {
                ["name"] = p.Name, ["parent"] = p.Parent, ["rituals"] = p.Rituals, ["metal_need"] = p.MetalNeed,
                ["harvest"] = p.Harvest, ["steadiness"] = p.Steadiness, ["blame"] = p.Blame,
                ["row"] = new JsonArray(p.Row.Select(r => (JsonNode)new JsonArray(r.Path, r.Value)).ToArray()),
                ["column"] = new JsonArray(p.Column.Select(r => (JsonNode)new JsonArray(r.Path, r.Value)).ToArray()),
            }).ToArray()),
            ["split_names_given"] = new JsonObject(game.SplitNamesGiven.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
            // The dice's state is four 64-bit numbers each, written as text: JSON numbers cannot hold them exactly.
            ["dice"] = new JsonObject(game.AllDice.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d =>
                KeyValuePair.Create(d.Key, (JsonNode?)new JsonArray(d.Value.State.Select(n => (JsonNode?)JsonValue.Create(n.ToString())).ToArray())))),
            ["mines"] = new JsonArray(game.Mines.Select(m => (JsonNode?)JsonValue.Create(m.Metal)).ToArray()),
            ["mine_owners"] = new JsonArray(game.Mines.Select(m => (JsonNode?)(m.Owner is int o ? JsonValue.Create(o) : null)).ToArray()),
            ["regions"] = Runs(game.Regions),
            ["borders_changed"] = game.BordersChanged,
            ["conquests"] = game.Conquests,
            ["rebellions"] = game.Rebellions,
            ["alliances"] = new JsonArray(game.Alliances.OrderBy(p => p)
                .Select(p => (JsonNode)new JsonArray(p.Item1, p.Item2)).ToArray()),
            ["wars"] = new JsonArray(game.Wars.Select(w => (JsonNode)new JsonObject
            {
                ["attacker"] = w.Attacker, ["defender"] = w.Defender, ["since"] = w.Since,
                ["sides"] = new JsonArray(w.Sides.Select(side => (JsonNode)Numbers(side.OrderBy(f => f))).ToArray()),
                ["dead"] = new JsonArray(w.Dead.OrderBy(d => d.Key).Select(d => (JsonNode)new JsonArray(d.Key, d.Value)).ToArray()),
                ["last_battle"] = w.LastBattle, ["ended"] = w.Ended, ["endurance"] = w.Endurance,
                ["rituals"] = Numbers(w.Rituals.Order().ToArray()),
            }).ToArray()),
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

    // A faction the game founded, as the world's records list it, and back.
    static JsonObject Info(FactionInfo f) => new()
    {
        ["name"] = f.Name, ["colour"] = new JsonArray(f.Red, f.Green, f.Blue), ["lineage"] = f.Lineage, ["race"] = f.Race,
        ["parent"] = f.Parent, ["founded"] = f.Founded,
    };

    static FactionInfo ReadInfo(JsonObject f)
    {
        var c = f["colour"]!.AsArray();
        return new FactionInfo((string)f["name"]!, (byte)c[0]!, (byte)c[1]!, (byte)c[2]!, (string?)f["lineage"],
                               (string?)f["race"], (int?)f["parent"], (int)f["founded"]!);
    }

    // A table of named numbers (traits, prejudices) as JSON, and back.
    static JsonObject Table(Dictionary<string, double> table) =>
        new(table.Select(t => KeyValuePair.Create(t.Key, (JsonNode?)JsonValue.Create(t.Value))));

    static Dictionary<string, double> ReadTable(JsonNode? node) =>
        node!.AsObject().ToDictionary(p => p.Key, p => (double)p.Value!);

    // A grid of holders as runs [holder, count, ...], and back (as the export writes the borders).
    static JsonArray Runs(IReadOnlyList<int> flat)
    {
        var runs = new JsonArray();
        int i = 0;
        while (i < flat.Count)
        {
            int start = i;
            while (i < flat.Count && flat[i] == flat[start])
                i++;
            runs.Add((JsonNode)flat[start]);   // as a JsonNode: a plain Add<int> would need a type resolver to write
            runs.Add((JsonNode)(i - start));
        }
        return runs;
    }

    static int[] Unruns(JsonArray runs)
    {
        var flat = new List<int>();
        for (int i = 0; i < runs.Count; i += 2)
            flat.AddRange(Enumerable.Repeat((int)runs[i]!, (int)runs[i + 1]!));
        return flat.ToArray();
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
        if (version < Version)
            throw new WorldFileException(path + " was saved by an older game (save version " + version +
                                         ") and cannot go on in this one. Start a new game on the world.");

        string worldPath = (string)save["world"]!;
        if (!File.Exists(worldPath))
            throw new WorldFileException("the world this game was played on is no longer at " + worldPath);
        var world = ExportedWorld.Read(worldPath);
        if (world.Seed != (int)save["world_seed"]! || world.HistorySeed != (int?)save["history_seed"]
            || world.ExportYears != (int)save["export_years"]!)
            throw new WorldFileException("the world at " + worldPath + " is not the one this game was played on " +
                                         "(it was exported again with other seeds or years)");

        var game = new Game(world, (long)save["seed"]!);
        var regions = world.Borders(world.Years);
        foreach (var node in save["years"]!.AsArray())
        {
            var year = node!.AsObject();
            if (year["borders"] is JsonArray runs)
                regions = Unruns(runs);
            foreach (var founded in year["new_towns"]!.AsArray())
                world.AddTown(new TownRecord(world.Years + 1, (string)founded!["name"]!, (int)founded["x"]!, (int)founded["y"]!,
                                             new List<int>(), new List<(int, int?)>(), new List<(int, int?)>()));
            foreach (var founded in year["new_factions"]!.AsArray())
            {
                var info = ReadInfo(founded!.AsObject());
                world.AddFaction(info, world.Years + 1);
            }
            world.AddYear(year["people"]!.AsArray().Select(p => (int)p!).ToList(),
                          year["holders"]!.AsArray().Select(h => (int?)h).ToList(),
                          year["ruined"]!.AsArray().Select(r => (bool)r!).ToList(),
                          year["factions"]!.AsArray().Select(p => (int)p!).ToList(),
                          year["states"]!.AsArray().Select(o => o as JsonObject).ToList(), regions);
        }
        game.Year = (int)save["year"]!;
        if (game.Year != world.Years)
            throw new WorldFileException(path + " is damaged: its year and its years do not agree");

        var towns = save["towns"]!.AsArray();
        if (towns.Count != world.Towns.Count)
            throw new WorldFileException(path + " is damaged: its towns and its years do not agree");
        for (int i = 0; i < towns.Count; i++)
        {
            var t = towns[i]!.AsObject();
            if (i == game.Towns.Count)            // one the game's migrants founded
                game.Towns.Add(new TownNow());
            var town = game.Towns[i];
            town.X = (int)t["x"]!;
            town.Y = (int)t["y"]!;
            town.HX = (int)t["hx"]!;
            town.HY = (int)t["hy"]!;
            town.Cells = t["cells"]!.AsArray().Select(c => (int)c!).ToList();
            town.Near = t["near"]!.AsArray().Select(k => (string)k!).ToHashSet();
            town.People = (double)t["people"]!;
            town.Size = (int)t["size"]!;
            town.LowYears = (int)t["low_years"]!;
            town.Hungry = (int)t["hungry"]!;
            town.Ruined = (bool)t["ruined"]!;
            town.Holder = (int?)t["holder"];
            town.Origin = (int?)t["origin"];
            town.ConqueredIn = (int?)t["conquered_in"];
            town.FoundedFrom = (int?)t["founded_from"];
            town.Food = (double)t["food"]!;
            town.Capacity = (double)t["capacity"]!;
            town.FedAs = t["fed_as"] is JsonArray fed ? ((int?)fed[0], (string?)fed[1]) : null;
            // In the order they were added: the market goes through them in that order, as the generator's does.
            town.Yields = t["yields"]!.AsArray().ToDictionary(y => (string)y![0]!, y => (double)y![1]!);
            town.Fed = (double)t["fed"]!;
        }
        var factions = save["factions"]!.AsArray();
        if (factions.Count != world.Factions.Count)
            throw new WorldFileException(path + " is damaged: its factions and its years do not agree");
        for (int f = 0; f < factions.Count; f++)
        {
            var o = factions[f]!.AsObject();
            if (f == game.Factions.Count)          // one the game founded
                game.Factions.Add(new FactionNow());
            game.Factions[f].Name = (string)o["name"]!;
            game.Factions[f].Folk = (int)o["folk"]!;
            game.Factions[f].Lineage = (string?)o["lineage"];
            game.Factions[f].Capital = (int?)o["capital"];
            game.Factions[f].Parent = (int?)o["parent"];
            game.Factions[f].Founded = (int)o["founded"]!;
            game.Factions[f].Nature = ReadTable(o["nature"]);
            game.Factions[f].Belief = (string)o["belief"]!;
            game.Factions[f].Hold = (double)o["hold"]!;
            game.Factions[f].Skills = ReadTable(o["skills"]);
            game.Factions[f].Learned = o["learned"]!.AsArray().Select(g => (string)g!).ToHashSet();
            game.UsedNames.Add(game.Factions[f].Name);
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
            faction.Border = o["border"]!.AsArray().ToDictionary(p => (int)p![0]!, p => (double)p![1]!);
            faction.Opinions = o["opinions"]!.AsArray().ToDictionary(p => (int)p![0]!, p => (double)p![1]!);
        }
        game.Words.Clear();
        foreach (var pair in save["words"]!.AsArray())
            game.Words[((int)pair![0]!, (int)pair[1]!)] = (int)pair[2]!;
        game.NamesUsed = save["names_used"]!.AsArray().Select(n => (int)n!).ToArray();
        game.SettlementNamesUsed = save["settlement_names_used"]!.AsArray().Select(n => (int)n!).ToArray();
        game.RiftNamesUsed = save["rift_names_used"]!.AsArray().Select(n => (int)n!).ToArray();
        game.Marks = Marks.Read(save["marks"]);
        Array.Copy(Unruns(save["owner"]!.AsArray()), game.Grid.Owner, game.Grid.Owner.Length);
        foreach (var r in save["new_routes"]!.AsArray())
            game.Routes.Add(new Route { A = (int)r![0]!, B = (int)r[1]!, Cost = (double)r[2]! });
        game.RoutesChanged();
        // The mines found in the game's years (a save of step 8 or before has none), before their land, metal and
        // owners are read below with every other mine's.
        foreach (var node in save["found_mines"] as JsonArray ?? new JsonArray())
        {
            var m = node!.AsObject();
            game.Mines.Add(new Mine { Name = (string)m["name"]!, X = (int)m["x"]!, Y = (int)m["y"]!, HX = (int)m["hx"]!, HY = (int)m["hy"]! });
            game.FoundMines++;
        }
        if (save["mine_first"] is JsonArray firsts)
            for (int k = 0; k < game.Mines.Count; k++)
                game.Mines[k].First = (double)firsts[k]!;
        if (save["hidden"] is JsonArray hidden)
            game.Hidden = Death.Read(hidden);
        if (save["signs_lasting"] is JsonObject lasting)
        {
            game.SignsLasting.Clear();
            foreach (var (town, entries) in lasting)
                game.SignsLasting[int.Parse(town)] = entries!.AsArray().Select(e => ((int)e![0]!, (int)e[1]!, (double)e[2]!)).ToList();
        }
        if (save["mine_names_used"] is JsonArray minesUsed)
            game.MineNamesUsed = minesUsed.Select(n => (int)n!).ToArray();
        // A save from before step 9c keeps the export's happenings and eras.
        if (save["happenings"] is JsonObject happened)
            Happenings.Read(game, happened);
        // A save from before step 9b keeps the export's threats.
        if (save["threats"] is JsonArray threats)
            game.Threats = Threats.Read(threats);
        if (save["threat_names_given"] is JsonObject namesGiven)
        {
            game.ThreatNamesGiven.Clear();
            foreach (var (kind, count) in namesGiven)
                game.ThreatNamesGiven[kind] = (int)count!;
        }
        if (save["spoiled"] is JsonObject spoiled)
        {
            game.Spoiled.Clear();
            foreach (var (town, entry) in spoiled)
                game.Spoiled[int.Parse(town)] = ((int)entry![0]!, (double)entry[1]!);
        }
        if (save["threat_names_used"] is JsonArray threatsUsed)
            game.ThreatNamesUsed = threatsUsed.Select(n => (int)n!).ToArray();
        var landTowns = save["mine_land_towns"]!.AsArray();
        for (int k = 0; k < game.Mines.Count; k++)
            game.Mines[k].LandTown = (int?)landTowns[k];
        var moving = save["moving"]!.AsObject();
        game.Moving.Sites = moving["sites"] is JsonArray sites
            ? sites.Select(p => ((double)p![0]!, (int)p[1]!, (int)p[2]!)).ToList() : null;
        game.Moving.OwnSites.Clear();
        foreach (var entry in moving["own_sites"]!.AsArray())
            game.Moving.OwnSites[((int)entry![0]!, (string?)entry[1])] =
                entry[2]!.AsArray().Select(p => ((double)p![0]!, (int)p[1]!, (int)p[2]!)).ToList();
        game.Moving.Tastes.Clear();
        foreach (var entry in moving["tastes"]!.AsArray())
            game.Moving.Tastes[((int)entry![0]!, (string?)entry[1])] = Movements.Exact((string)entry[2]!);
        game.Moving.TownScores.Clear();
        game.Moving.TownScores.AddRange(Movements.Exact((string)moving["town_scores"]!));
        foreach (var name in world.Towns.Skip(world.Data["settlements"]!.AsArray().Count).Select(t => t.Name))
            game.UsedNames.Add(name);
        game.PeopleNamesUsed = save["people_names_used"]!.AsArray().Select(n => (int)n!).ToArray();
        game.Waiting.Clear();
        foreach (var w in save["waiting"]!.AsArray())
            game.Waiting.Add(((string)w![0]!, (int)w[1]!));
        // A save of step 8b or before has no founded paths and no founders: none.
        foreach (var node in save["founded_paths"] as JsonArray ?? new JsonArray())
        {
            var p = node!.AsObject();
            static List<(string, double)> Pairs(JsonNode? list) =>
                list!.AsArray().Select(r => ((string)r![0]!, (double)r[1]!)).ToList();
            game.Settings.AddPath(new GameSettings.FoundedPath((string)p["name"]!, (string)p["parent"]!, (double)p["rituals"]!,
                (double?)p["metal_need"], (double?)p["harvest"], (double?)p["steadiness"], Pairs(p["row"]), Pairs(p["column"]),
                (double?)p["blame"]));
        }
        game.Founders.Clear();
        foreach (var founder in save["founders"] as JsonArray ?? new JsonArray())
            game.Founders.Add((int)founder!);
        game.SplitNamesGiven.Clear();
        foreach (var (key, given) in save["split_names_given"]!.AsObject())
            game.SplitNamesGiven[key] = (int)given!;
        foreach (var (system, state) in save["dice"]!.AsObject())
            game.SetDice(system, new Dice(state!.AsArray().Select(n => ulong.Parse((string)n!)).ToList()));
        var mines = save["mines"]!.AsArray();
        var owners = save["mine_owners"]!.AsArray();
        for (int k = 0; k < game.Mines.Count; k++)
        {
            game.Mines[k].Metal = (double)mines[k]!;
            game.Mines[k].Owner = (int?)owners[k];
        }
        Array.Copy(Unruns(save["regions"]!.AsArray()), game.Regions, game.Regions.Length);
        game.BordersChanged = (bool)save["borders_changed"]!;
        game.Conquests = (int)save["conquests"]!;
        game.Rebellions = (int)save["rebellions"]!;
        game.Alliances.Clear();
        foreach (var pair in save["alliances"]!.AsArray())
            game.Alliances.Add(((int)pair![0]!, (int)pair[1]!));
        game.Wars.Clear();
        foreach (var node in save["wars"]!.AsArray())
        {
            var w = node!.AsObject();
            game.Wars.Add(new WarNow
            {
                Attacker = (int)w["attacker"]!, Defender = (int)w["defender"]!, Since = (int)w["since"]!,
                Sides = w["sides"]!.AsArray().Select(side => side!.AsArray().Select(f => (int)f!).ToHashSet()).ToArray(),
                Dead = w["dead"]!.AsArray().ToDictionary(d => (int)d![0]!, d => (double)d![1]!),
                LastBattle = (int)w["last_battle"]!, Ended = (int?)w["ended"], Endurance = (double?)w["endurance"],
                // A save of step 8a has no "rituals" (no war had any in the game then): none.
                Rituals = w["rituals"] is JsonArray worked ? worked.Select(f => (int)f!).ToHashSet() : new HashSet<int>(),
            });
        }
        game.DriftTargets.Clear();
        foreach (var (faction, traits) in save["drift_targets"]!.AsObject())
            game.DriftTargets[int.Parse(faction)] = ReadTable(traits);
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
        game.CapitalsKnown.UnionWith(Enumerable.Range(0, game.Factions.Count));
        Learning.Settle(game);                      // the hybrids each faction holds, from the paths it holds
        return game;
    }
}
