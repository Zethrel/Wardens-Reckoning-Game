// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WardensReckoning.World;

/// <summary>A place a colony can be founded, as world.json's "founding_sites" lists it (the generator's step 17a).</summary>
public sealed record FoundingSite(int Site, int X, int Y, double Score, string File, string? Biome, bool FreshWater, string? Water);

/// <summary>
/// The ground of a founding site, one tile a metre (the generator's local/site-NN.json; its docs/export-format.md,
/// "The local maps"). Each tile has a surface ("grass", "rough", "sand", "snow", "mud", "fresh_water",
/// "salt_water") and may hold a thing ("tree", "rock"). Row y = 0 is the top.
///
///     var map = world.LocalMap(1);         // site 1 of the export
///     map.SurfaceAt(128, 128);             // "grass": the middle, where the founders arrive
///     map.ThingAt(10, 40);                 // "tree", "rock" or null
/// </summary>
public sealed class LocalMap
{
    public const string Format = "the-north-local";

    /// <summary>The newest version this reader understands.</summary>
    public const int SupportedVersion = 1;

    readonly string[] surfaces;
    readonly string?[] things;

    LocalMap(int size, string[] surfaces, string?[] things)
    {
        Size = size;
        this.surfaces = surfaces;
        this.things = things;
    }

    public int Site { get; private init; }
    public int X { get; private init; }
    public int Y { get; private init; }
    /// <summary>Tiles across and down.</summary>
    public int Size { get; }
    public string? Biome { get; private init; }
    public bool FreshWater { get; private init; }
    /// <summary>What moved the site to water: "river", "stream" (a brook), "lake", "sea", or null.</summary>
    public string? Water { get; private init; }
    public double MetresPerCell { get; private init; }

    public string SurfaceAt(int x, int y) => surfaces[y * Size + x];
    public string? ThingAt(int x, int y) => things[y * Size + x];

    /// <summary>Every surface the map uses, in order of the name.</summary>
    public IEnumerable<string> Surfaces => surfaces.Distinct().OrderBy(s => s, StringComparer.Ordinal);

    /// <summary>Reads and checks a local map file: its format and version, its size, and that every row is as long
    /// as the map is wide and uses only the letters its keys give. Throws WorldFileException saying what is wrong.</summary>
    public static LocalMap Read(string path)
    {
        JsonObject data;
        try
        {
            data = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new WorldFileException(path + " cannot be read as a local map: " + e.Message);
        }
        return Parse(data, path);
    }

    /// <summary>As Read, from the file's JSON (`where` names it in the messages).</summary>
    public static LocalMap Parse(JsonObject data, string where)
    {
        string Bad(string what) => throw new WorldFileException(where + ": " + what);
        if ((string?)data["format"] != Format)
            Bad("not a local map of the generator (format \"" + data["format"] + "\")");
        if (data["version"] is not JsonValue v || !v.TryGetValue(out int version) || version > SupportedVersion)
            Bad("version " + data["version"] + "; this game reads local maps up to version " + SupportedVersion);
        int size = data["size"] is JsonValue sv && sv.TryGetValue(out int s) && s > 0 ? s : 0;
        if (size == 0)
            Bad("its \"size\" is not a whole number above 0");
        Dictionary<char, string?> Key(string name, bool withNothing)
        {
            var key = new Dictionary<char, string?>();
            if (withNothing)
                key['.'] = null;
            if (data[name] is not JsonObject table)
                return key.Count > 0 ? key : throw new WorldFileException(where + ": \"" + name + "\" is not a table of letters");
            foreach (var (letter, node) in table)
            {
                if (letter.Length != 1 || letter == "." || node is not JsonValue nv || !nv.TryGetValue(out string? meaning))
                    throw new WorldFileException(where + ": \"" + name + "\" should give one letter (not '.') for each name");
                key[letter[0]] = meaning;
            }
            return key;
        }
        var surfaceKey = Key("surfaces", false);
        var thingKey = Key("things", true);
        string?[] Rows(string name, Dictionary<char, string?> key)
        {
            if (data[name] is not JsonArray rows || rows.Count != size)
                throw new WorldFileException(where + ": \"" + name + "\" should be " + size + " rows");
            var tiles = new string?[size * size];
            for (int y = 0; y < size; y++)
            {
                if (rows[y] is not JsonValue rv || !rv.TryGetValue(out string? row) || row.Length != size)
                    throw new WorldFileException(where + ": " + name + "[" + y + "] should be " + size + " letters");
                for (int x = 0; x < size; x++)
                    tiles[y * size + x] = key.TryGetValue(row[x], out var meaning) ? meaning
                        : throw new WorldFileException(where + ": " + name + "[" + y + "] has '" + row[x] + "', which its key does not give");
            }
            return tiles;
        }
        var ground = Rows("ground", surfaceKey).Select(t => t!).ToArray();
        var on = Rows("on_ground", thingKey);
        int Int(string key) => data[key] is JsonValue n && n.TryGetValue(out int i) ? i : 0;
        double Real(string key) => data[key] is JsonValue n && n.TryGetValue(out double d) ? d : 0;
        string? Text(string key) => data[key] is JsonValue n && n.TryGetValue(out string? t) ? t : null;
        return new LocalMap(size, ground, on)
        {
            Site = Int("site"), X = Int("x"), Y = Int("y"), Biome = Text("biome"),
            FreshWater = data["fresh_water"] is JsonValue fw && fw.TryGetValue(out bool fresh) && fresh,
            Water = Text("water"), MetresPerCell = Real("metres_per_cell"),
        };
    }
}
