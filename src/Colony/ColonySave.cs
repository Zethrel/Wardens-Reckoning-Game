// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WardensReckoning.Sim;

namespace WardensReckoning.Colony;

/// <summary>A colony save that cannot be read: its `Message` says why, in words the player can act on.</summary>
public sealed class ColonySaveException(string message) : Exception(message);

/// <summary>
/// Saving and loading a colony (library Reviews 29 and 30): plain JSON, with a format name and a version.
///
/// - **Checked before loading.** A file that is not a colony save, is of another version, or has a part missing or
///   wrong is refused with a clear reason; nothing is half-loaded and the file is never changed.
/// - **Older and newer versions are refused** until release 1.0 (no conversion code before then).
/// - **Never destroyed by a failed save.** A save is written to a new file beside the old one and only then put in
///   its place, so if writing fails part-way the old save is still there, whole.
/// - **The same colony always writes the same text**: everything is written in a fixed order, so the text (and its
///   hash) can be compared to prove two runs ended the same (FP-07).
/// </summary>
public static class ColonySave
{
    public const string Format = "wardens-reckoning-colony";

    /// <summary>The save's version: raised whenever what a save holds changes its shape or meaning.</summary>
    public const int Version = 1;

    /// <summary>The colony as JSON.</summary>
    public static JsonObject Write(Colony colony) => new()
    {
        ["format"] = Format,
        ["version"] = Version,
        // 64-bit numbers are written as text: JSON numbers are not exact beyond 2^53.
        ["seed"] = colony.Seed.ToString(CultureInfo.InvariantCulture),
        ["founded"] = colony.Founded.Tick,
        ["now"] = colony.Now.Tick,
        ["ids"] = new JsonObject(colony.Ids.Last.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value)))),
        ["dice"] = new JsonObject(colony.AllDice.Select(p => KeyValuePair.Create(p.Key,
            (JsonNode?)new JsonArray(p.Value.State.Select(n => (JsonNode?)n.ToString(CultureInfo.InvariantCulture)).ToArray())))),
    };

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The colony as the text a save file holds.</summary>
    public static string Text(Colony colony) => Write(colony).ToJsonString(Indented);

    /// <summary>A short fingerprint of the colony's whole state (SHA-256 of its save text): equal fingerprints mean
    /// equal colonies.</summary>
    public static string Fingerprint(Colony colony) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(colony)))).ToLowerInvariant();

    /// <summary>Saves `colony` to `path`: written beside it first, then moved into place.</summary>
    public static void Save(string path, Colony colony)
    {
        string text = Text(colony);
        string fresh = path + ".writing";
        File.WriteAllText(fresh, text, new UTF8Encoding(false));
        File.Move(fresh, path, overwrite: true);
    }

    /// <summary>Loads the colony saved at `path`, or refuses it (ColonySaveException) without changing the file.</summary>
    public static Colony Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ColonySaveException(path + " cannot be read: " + e.Message);
        }
        try
        {
            return Read(text);
        }
        catch (ColonySaveException e)
        {
            throw new ColonySaveException(path + ": " + e.Message);
        }
    }

    /// <summary>The colony a save's text holds, checked part by part.</summary>
    public static Colony Read(string text)
    {
        JsonObject save;
        try
        {
            save = JsonNode.Parse(text) as JsonObject ?? throw new ColonySaveException("not a colony save (no JSON object)");
        }
        catch (JsonException e)
        {
            throw new ColonySaveException("not a colony save (broken JSON: " + e.Message + ")");
        }
        if (Get<string>(save, "format") != Format)
            throw new ColonySaveException("not a colony save of this game");
        int version = Get<int>(save, "version");
        if (version != Version)
            throw new ColonySaveException("colony save version " + version + "; this game reads only version " + Version
                + (version < Version ? " (older saves are not converted before release 1.0)" : " (made by a newer game)"));
        if (!long.TryParse(Get<string>(save, "seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seed))
            throw new ColonySaveException("its seed is not a whole number");
        long founded = Get<long>(save, "founded"), now = Get<long>(save, "now");
        if (founded < 0 || now < founded)
            throw new ColonySaveException("its clock is impossible (founded at tick " + founded + ", now " + now + ")");
        var colony = new Colony(seed, new Moment(founded));
        colony.SetNow(new Moment(now));
        var ids = Get<JsonObject>(save, "ids");
        try
        {
            colony.Ids.Restore(ids.Select(p => KeyValuePair.Create(p.Key, Number(p.Value, "ids." + p.Key))));
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new ColonySaveException("an identifier counter is wrong: " + e.Message);
        }
        foreach (var (system, state) in Get<JsonObject>(save, "dice"))
        {
            if (state is not JsonArray numbers || numbers.Count != 4)
                throw new ColonySaveException("the dice of \"" + system + "\" are not four numbers");
            var values = new List<ulong>();
            foreach (var n in numbers)
                values.Add(n is JsonValue v && v.TryGetValue(out string? s)
                           && ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u)
                    ? u : throw new ColonySaveException("the dice of \"" + system + "\" hold something that is not a number"));
            colony.SetDice(system, new Dice(values));
        }
        return colony;
    }

    // A part of the save that must be there, of the right kind.
    static T Get<T>(JsonObject save, string key)
    {
        if (!save.TryGetPropertyValue(key, out var node) || node is null)
            throw new ColonySaveException("its \"" + key + "\" is missing");
        if (typeof(T) == typeof(JsonObject))
            return node is JsonObject o ? (T)(object)o : throw new ColonySaveException("its \"" + key + "\" is not a table");
        if (typeof(T) == typeof(long))
            return (T)(object)Number(node, key);
        if (node is JsonValue value && value.TryGetValue(out T? got) && got is not null)
            return got;
        throw new ColonySaveException("its \"" + key + "\" is not " + (typeof(T) == typeof(string) ? "text" : "a whole number"));
    }

    static long Number(JsonNode? node, string key) =>
        node is JsonValue v && v.TryGetValue(out long n) ? n : throw new ColonySaveException("its \"" + key + "\" is not a whole number");
}
