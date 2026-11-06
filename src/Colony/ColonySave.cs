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
    // 2: the colony's goods (step 13a): inventories, reservations and the ledger.
    public const int Version = 2;

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
        ["inventories"] = new JsonArray(colony.Stocks.Inventories.Select(i => (JsonNode?)new JsonObject
        {
            ["id"] = i.Id.Value,
            ["name"] = i.Name,
            ["holder"] = i.Holder.ToString(),
            ["x"] = i.X,
            ["y"] = i.Y,
            ["goods"] = Amounts(i.Goods),
            ["kept"] = Amounts(i.Kept),
        }).ToArray()),
        ["reservations"] = new JsonArray(colony.Stocks.Reservations.Select(r => (JsonNode?)new JsonObject
        {
            ["id"] = r.Id.Value, ["inventory"] = r.Inventory.Value, ["good"] = r.Good, ["amount"] = r.Amount, ["purpose"] = r.Purpose,
        }).ToArray()),
        ["ledger"] = new JsonArray(colony.Stocks.Ledger.Select(e => (JsonNode?)new JsonArray(
            e.Operation.Value, e.Tick, e.Change.ToString(), e.Good, e.Amount,
            e.From is InventoryId f ? f.Value : null, e.To is InventoryId to ? to.Value : null)).ToArray()),
    };

    static JsonObject Amounts(IReadOnlyDictionary<string, int> amounts) =>
        new(amounts.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value))));

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
    public static Colony Load(string path, Content? content = null)
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
            return Read(text, content);
        }
        catch (ColonySaveException e)
        {
            throw new ColonySaveException(path + ": " + e.Message);
        }
    }

    /// <summary>The colony a save's text holds, checked part by part; with `content`, every good it names must be
    /// one the content knows.</summary>
    public static Colony Read(string text, Content? content = null)
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
        ReadStocks(save, colony, content);
        return colony;
    }

    static void ReadStocks(JsonObject save, Colony colony, Content? content)
    {
        var stocks = colony.Stocks;
        string Good(JsonNode? node, string at)
        {
            string good = node is JsonValue v && v.TryGetValue(out string? s) ? s : throw new ColonySaveException(at + " is not a good's id");
            if (content != null && !content.Goods.ContainsKey(good))
                throw new ColonySaveException(at + " names the good \"" + good + "\", which the content does not have");
            return good;
        }
        int Count(JsonNode? node, string at) =>
            node is JsonValue v && v.TryGetValue(out int n) && n >= 1 ? n : throw new ColonySaveException(at + " is not a whole number of 1 or more");
        var known = new HashSet<long>();
        foreach (var (node, i) in Get<JsonArray>(save, "inventories").Select((n, i) => (n, i)))
        {
            string at = "inventories[" + i + "]";
            if (node is not JsonObject o)
                throw new ColonySaveException(at + " is not a table");
            long id = Number(o["id"], at + ".id");
            string name = o["name"] is JsonValue nv && nv.TryGetValue(out string? s) ? s : throw new ColonySaveException(at + ".name is not text");
            if (!Enum.TryParse<Holder>(o["holder"]?.ToString(), out var holder) || !Enum.IsDefined(holder))
                throw new ColonySaveException(at + ".holder is not a kind of inventory");
            double x = o["x"] is JsonValue xv && xv.TryGetValue(out double dx) ? dx : throw new ColonySaveException(at + ".x is not a number");
            double y = o["y"] is JsonValue yv && yv.TryGetValue(out double dy) ? dy : throw new ColonySaveException(at + ".y is not a number");
            if (id < 1 || id > colony.Ids.Last.GetValueOrDefault(InventoryId.Family) || !known.Add(id))
                throw new ColonySaveException(at + ".id " + id + " was never given out, or is given twice");
            var inventory = new Inventory(new InventoryId(id), name, holder, x, y);
            foreach (var (good, n) in o["goods"] as JsonObject ?? throw new ColonySaveException(at + ".goods is not a table"))
                inventory.Add(Good(JsonValue.Create(good), at + ".goods"), Count(n, at + ".goods." + good));
            foreach (var (good, n) in o["kept"] as JsonObject ?? throw new ColonySaveException(at + ".kept is not a table"))
                inventory.Keep(Good(JsonValue.Create(good), at + ".kept"), Count(n, at + ".kept." + good));
            stocks.Put(inventory);
        }
        var claimed = new Dictionary<(long, string), int>();
        var reserved = new HashSet<long>();
        foreach (var (node, i) in Get<JsonArray>(save, "reservations").Select((n, i) => (n, i)))
        {
            string at = "reservations[" + i + "]";
            if (node is not JsonObject o)
                throw new ColonySaveException(at + " is not a table");
            long id = Number(o["id"], at + ".id"), where = Number(o["inventory"], at + ".inventory");
            if (!known.Contains(where))
                throw new ColonySaveException(at + " claims goods in inventory " + where + ", which does not exist");
            string good = Good(o["good"], at + ".good");
            int amount = Count(o["amount"], at + ".amount");
            string purpose = o["purpose"] is JsonValue pv && pv.TryGetValue(out string? p) ? p : throw new ColonySaveException(at + ".purpose is not text");
            if (id < 1 || id > colony.Ids.Last.GetValueOrDefault(ReservationId.Family) || !reserved.Add(id))
                throw new ColonySaveException(at + ".id " + id + " was never given out, or is given twice");
            claimed[(where, good)] = claimed.GetValueOrDefault((where, good)) + amount;
            if (claimed[(where, good)] > stocks[new InventoryId(where)].Count(good))
                throw new ColonySaveException(at + " claims more " + good + " than inventory " + where + " has");
            stocks.PutReservation(new Reservation(new ReservationId(id), new InventoryId(where), good, amount, purpose));
        }
        foreach (var (node, i) in Get<JsonArray>(save, "ledger").Select((n, i) => (n, i)))
        {
            string at = "ledger[" + i + "]";
            if (node is not JsonArray e || e.Count != 7 || !Enum.TryParse<Change>(e[2]?.ToString(), out var change) || !Enum.IsDefined(change))
                throw new ColonySaveException(at + " is not a ledger line");
            InventoryId? Place(JsonNode? n)
            {
                if (n is null)
                    return null;
                long place = Number(n, at);
                return known.Contains(place) ? new InventoryId(place) : throw new ColonySaveException(at + " names inventory " + place + ", which does not exist");
            }
            stocks.PutEntry(new Entry(new OperationId(Number(e[0], at)), Number(e[1], at), change, Good(e[3], at),
                                      Count(e[4], at), Place(e[5]), Place(e[6])));
        }
        foreach (string good in stocks.Inventories.SelectMany(i => i.Goods.Keys).Concat(stocks.Ledger.Select(e => e.Good)).Distinct())
            if (stocks.Total(good) != stocks.Accounted(good))
                throw new ColonySaveException("its goods do not add up: " + stocks.Total(good) + " " + good + " held, the ledger says "
                                              + stocks.Accounted(good));
    }

    // A part of the save that must be there, of the right kind.
    static T Get<T>(JsonObject save, string key)
    {
        if (!save.TryGetPropertyValue(key, out var node) || node is null)
            throw new ColonySaveException("its \"" + key + "\" is missing");
        if (typeof(T) == typeof(JsonObject))
            return node is JsonObject o ? (T)(object)o : throw new ColonySaveException("its \"" + key + "\" is not a table");
        if (typeof(T) == typeof(JsonArray))
            return node is JsonArray a ? (T)(object)a : throw new ColonySaveException("its \"" + key + "\" is not a list");
        if (typeof(T) == typeof(long))
            return (T)(object)Number(node, key);
        if (node is JsonValue value && value.TryGetValue(out T? got) && got is not null)
            return got;
        throw new ColonySaveException("its \"" + key + "\" is not " + (typeof(T) == typeof(string) ? "text" : "a whole number"));
    }

    static long Number(JsonNode? node, string key) =>
        node is JsonValue v && v.TryGetValue(out long n) ? n : throw new ColonySaveException("its \"" + key + "\" is not a whole number");
}
