// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WardensReckoning.Sim;

namespace WardensReckoning.Colony;

/// <summary>A colony save that cannot be read: its `Message` says why, in words the player can act on.</summary>
public class ColonySaveException(string message) : Exception(message);

/// <summary>A save that reads well but whose checksum does not match: it was changed outside the game, or is
/// damaged (step 16, library Review 35). It may still be loaded, if the player says so (`acceptChanged`).</summary>
public sealed class ColonySaveChangedException(string message) : ColonySaveException(message);

/// <summary>A loaded colony, and what the player should be told about it (the content changed since, or the
/// save had been changed and they chose to load it anyway).</summary>
public sealed record Opened(Colony Colony, IReadOnlyList<string> Warnings);

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
    // 2: the colony's goods (step 13a): inventories, reservations and the ledger. 3: the work (step 13b): what each
    // store takes, the workers, the workshops and their orders, the jobs. 4: the colonists' day (step 14a): needs,
    // schedule, sleep, collapse, what they do for themselves; the bedrolls; the springs. 5: the kinds of work each
    // colonist is not allowed (step 14b). 6: the ground (its size, what is in the way, the finished pieces) and the
    // construction projects (step 15a). 7: stockpiles (a store's tiles) and stores being emptied (step 15b). 8: the
    // ground's surfaces, each colonist's legs, the litres of each spring and well, takings-down (step 15c). 9: a
    // checksum and the content's fingerprint (step 16). 10: the ground's surfaces and obstacles as rows of letters,
    // one a tile, so that a local map of 256 by 256 tiles stays small (step 17b).
    // 11: water by its uses (step 19a): each water source's kind, litres and frost, the colony's cold, the stores'
    // capacity, wash places and sources, and washing.
    // 12: fouled water (step 19b): each source's fouling and sign, the stores' marks, the colonists' illnesses, the
    // boiling job, and what the player was told.
    // 13: the second tick (step 21a, library Review 39): every tick in a save is a second, no longer a minute; and
    // where each colonist is heading.
    // 14: thinking (step 21c): each colonist's rhythm, next think and last think; a job's and a meal's time left
    // in seconds, no longer minutes.
    // 15: skills (step 23a): each colonist's proficiency in each skill, and the work carried over between thinks
    // ("carry", which replaces "slow": it holds a skilled colonist's extra as well as an ill one's shortfall).
    // 16: learning (step 23b): for each skill [proficiency, best reached, aptitude, seconds practised, last practised].
    // 17: the hybrid clock (step 24, library Review 41): each colonist's action deadline, the fourth of "think" (null
    // when nothing is due); 24 ticks are now a real second at ×1, so the same ticks mean slower walking.
    public const int Version = 17;

    /// <summary>The colony as JSON.</summary>
    public static JsonObject Write(Colony colony) => new()
    {
        ["content"] = colony.Content?.Fingerprint,
        ["format"] = Format,
        ["version"] = Version,
        // 64-bit numbers are written as text: JSON numbers are not exact beyond 2^53.
        ["seed"] = colony.Seed.ToString(CultureInfo.InvariantCulture),
        ["founded"] = colony.Founded.Tick,
        ["now"] = colony.Now.Tick,
        ["ids"] = new JsonObject(colony.Ids.Last.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value)))),
        ["dice"] = new JsonObject(colony.AllDice.Select(p => KeyValuePair.Create(p.Key,
            (JsonNode?)new JsonArray(p.Value.State.Select(n => (JsonNode?)n.ToString(CultureInfo.InvariantCulture)).ToArray())))),
        ["closed_inventories"] = new JsonArray(colony.Stocks.Closed.Select(c => (JsonNode?)c.Value).ToArray()),
        ["inventories"] = new JsonArray(colony.Stocks.Inventories.Select(i => (JsonNode?)new JsonObject
        {
            ["id"] = i.Id.Value,
            ["name"] = i.Name,
            ["holder"] = i.Holder.ToString(),
            ["x"] = i.X,
            ["y"] = i.Y,
            ["goods"] = Amounts(i.Goods),
            ["kept"] = Amounts(i.Kept),
            ["accepts"] = new JsonArray(i.Accepts.Select(g => (JsonNode?)g).ToArray()),
            ["emptying"] = i.Emptying,
            ["source_only"] = i.SourceOnly,
            ["capacity"] = i.Capacity,
            ["wash_place"] = i.WashPlace,
            ["mark"] = i.Mark.ToString(),
            ["area"] = i.Area is not Area a ? null : new JsonArray(a.X, a.Y, a.Width, a.Height),
        }).ToArray()),
        ["reservations"] = new JsonArray(colony.Stocks.Reservations.Select(r => (JsonNode?)new JsonObject
        {
            ["id"] = r.Id.Value, ["inventory"] = r.Inventory.Value, ["good"] = r.Good, ["amount"] = r.Amount, ["purpose"] = r.Purpose,
        }).ToArray()),
        ["ground"] = colony.Ground is not Ground g ? null : WriteGround(g),
        ["ledger"] = new JsonArray(colony.Stocks.Ledger.Select(e => (JsonNode?)new JsonArray(
            e.Operation.Value, e.Tick, e.Change.ToString(), e.Good, e.Amount,
            e.From is InventoryId f ? f.Value : null, e.To is InventoryId to ? to.Value : null)).ToArray()),
        ["work"] = colony.Work is not Work work ? null : new JsonObject
        {
            ["workers"] = new JsonArray(work.Workers.Select(w => (JsonNode?)new JsonObject
            {
                ["id"] = w.Id.Value, ["name"] = w.Name, ["x"] = w.X, ["y"] = w.Y, ["arms"] = w.Arms.Value,
                ["job"] = w.Job is TaskId j ? j.Value : null, ["stopped"] = w.Stopped, ["legs"] = w.Legs,
                ["needs"] = new JsonObject(w.Needs.Select(n => KeyValuePair.Create(n.Key,
                    (JsonNode?)new JsonArray(n.Value.Level, n.Value.MinutesAtLimit)))),
                ["schedule"] = w.Schedule, ["asleep"] = w.Asleep, ["on_bedroll"] = w.OnBedroll, ["collapsed"] = w.Collapsed,
                ["lacking"] = w.Lacking,
                ["illness"] = w.Illness is not Illness ill ? null : new JsonArray(ill.Name, ill.Shows, ill.Ends),
                ["carry"] = w.Carry,
                ["skills"] = new JsonObject(w.Skills.Select(s => KeyValuePair.Create(s.Key,
                    (JsonNode?)new JsonArray(s.Value.Proficiency, s.Value.Peak, s.Value.Aptitude, s.Value.Practised, s.Value.LastPractised)))),
                ["heading"] = w.Heading is not var (hx, hy, hnear) ? null : new JsonArray(hx, hy, hnear),
                ["think"] = new JsonArray(w.ThinkEvery, w.NextThink, w.Thought, w.ActionDue),
                ["forbidden"] = new JsonArray(w.Forbidden.Select(k => (JsonNode?)k).ToArray()),
                ["personal"] = w.Personal is not Personal p ? null : new JsonObject
                {
                    ["kind"] = p.Kind.ToString(), ["x"] = p.X, ["y"] = p.Y,
                    ["claim"] = p.Claim is ReservationId c ? c.Value : null, ["operation"] = p.Operation.Value,
                    ["bedroll"] = p.Bedroll, ["left"] = p.Left,
                },
            }).ToArray()),
            ["bedrolls"] = new JsonArray(work.Bedrolls.Select(b => (JsonNode?)new JsonObject
            {
                ["id"] = b.Id.Value, ["x"] = b.X, ["y"] = b.Y, ["owner"] = b.Owner is ColonistId o ? o.Value : null,
            }).ToArray()),
            ["springs"] = new JsonArray(work.Springs.Select(s => work.SourceOf(s) is var w ? (JsonNode?)new JsonArray(s.Value, w.Kind.ToString(),
                w.Litres, w.Frozen, w.FouledUntil, w.Sign, w.SignSeen) : null).ToArray()),
            ["cold"] = work.Cold,
            ["news"] = new JsonArray(work.Told.Select(n => (JsonNode?)new JsonArray(n.Tick, n.Text)).ToArray()),
            ["projects"] = new JsonArray(work.Projects.Select(p => (JsonNode?)new JsonObject
            {
                ["id"] = p.Id.Value, ["piece"] = p.Piece, ["x"] = p.X, ["y"] = p.Y, ["stock"] = p.Stock.Value,
                ["paused"] = p.Paused, ["cancelled"] = p.Cancelled, ["removing"] = p.Removing,
            }).ToArray()),
            ["sites"] = new JsonArray(work.Sites.Select(s => (JsonNode?)new JsonObject
            {
                ["id"] = s.Id.Value, ["workshop"] = s.Workshop, ["input"] = s.Input.Value, ["output"] = s.Output.Value,
                ["x"] = s.X, ["y"] = s.Y, ["recipe"] = s.Recipe, ["batches"] = s.Batches,
            }).ToArray()),
            ["jobs"] = new JsonArray(work.Jobs.Select(j => (JsonNode?)new JsonObject
            {
                ["id"] = j.Id.Value, ["kind"] = j.Kind.ToString(), ["good"] = j.Good, ["amount"] = j.Amount,
                ["from"] = j.From.Value, ["to"] = j.To.Value, ["site"] = j.Site is BuildingId b ? b.Value : null,
                ["recipe"] = j.Recipe, ["claims"] = new JsonArray(j.Claims.Select(c => (JsonNode?)c.Value).ToArray()),
                ["first"] = j.First.Value, ["second"] = j.Second.Value, ["third"] = j.Third.Value,
                ["boiling"] = j.Boiling is InventoryId bw ? bw.Value : null, ["worker"] = j.Worker is ColonistId c ? c.Value : null,
                ["step"] = j.Step.ToString(), ["left"] = j.Left, ["purpose"] = j.Purpose,
            }).ToArray()),
        },
    };

    static JsonObject Amounts(IReadOnlyDictionary<string, int> amounts) =>
        new(amounts.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value))));

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// The colony as the text a save file holds. Last comes its **checksum**: the SHA-256 of the save's text
    /// without it. Loading works it out again; if it differs, something changed the file after the game wrote it.
    /// </summary>
    public static string Text(Colony colony)
    {
        var save = Write(colony);
        save["checksum"] = Hash(save.ToJsonString(Indented));
        return save.ToJsonString(Indented);
    }

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Whether a save's checksum matches the rest of it: worked out from the save with its checksum taken
    /// out, written the way the game writes it (so spacing changed by hand does not count, but any value does).</summary>
    static bool ChecksumMatches(JsonObject save)
    {
        if (save["checksum"] is not JsonValue v || !v.TryGetValue(out string? stated))
            return false;
        var rest = save.DeepClone().AsObject();
        rest.Remove("checksum");
        return Hash(rest.ToJsonString(Indented)) == stated;
    }

    /// <summary>A short fingerprint of the colony's whole state (SHA-256 of its save text): equal fingerprints mean
    /// equal colonies.</summary>
    public static string Fingerprint(Colony colony) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(colony)))).ToLowerInvariant();

    /// <summary>
    /// Saves `colony` to `path` without ever putting a bad save in place of a good one (step 16, library Review 35):
    ///
    /// 1. written beside it first (`path.writing`);
    /// 2. read back and checked as if loading it: if that fails, nothing is replaced;
    /// 3. moved into place; the save it replaces is kept as `path.bak` (if `backup`).
    ///
    /// If any step fails (a full disk, a folder that is not there, no permission), it throws ColonySaveException
    /// saying so, the half-written file is removed, and the save that was at `path` is as it was.
    /// </summary>
    public static void Save(string path, Colony colony, bool backup = true) => Save(path, colony, backup, text => text);

    /// <summary>As `Save`, with `tamper` changing the text before it is written (only for the tests: a way to
    /// make a writing go wrong on purpose).</summary>
    internal static void Save(string path, Colony colony, bool backup, Func<string, string> tamper)
    {
        string fresh = path + ".writing";
        try
        {
            File.WriteAllText(fresh, tamper(Text(colony)), new UTF8Encoding(false));
            Read(File.ReadAllText(fresh), colony.Content);
            if (File.Exists(path))
                File.Replace(fresh, path, backup ? path + ".bak" : null);
            else
                File.Move(fresh, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ColonySaveException)
        {
            try
            {
                if (File.Exists(fresh))
                    File.Delete(fresh);
            }
            catch (Exception tidy) when (tidy is IOException or UnauthorizedAccessException)
            {
                // Removing the half-written file is only tidying; the save at `path` is untouched either way.
            }
            throw new ColonySaveException("The colony could not be saved to " + path + " (" + e.Message + "). "
                + (File.Exists(path) ? "The save already there is as it was." : "Nothing was saved."));
        }
    }

    /// <summary>Loads the colony saved at `path`, or refuses it (ColonySaveException) without changing the file.
    /// A save changed outside the game is refused (ColonySaveChangedException) unless `acceptChanged`.</summary>
    public static Colony Load(string path, Content? content = null, bool acceptChanged = false) =>
        Open(path, content, acceptChanged).Colony;

    /// <summary>As `Load`, with what the player should be told about the save.</summary>
    public static Opened Open(string path, Content? content = null, bool acceptChanged = false)
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
            return Opening(text, content, acceptChanged);
        }
        catch (ColonySaveChangedException e)
        {
            throw new ColonySaveChangedException(path + ": " + e.Message);
        }
        catch (ColonySaveException e)
        {
            throw new ColonySaveException(path + ": " + e.Message);
        }
    }

    /// <summary>The colony a save's text holds (see `Opening`), without its warnings.</summary>
    public static Colony Read(string text, Content? content = null, bool acceptChanged = false) =>
        Opening(text, content, acceptChanged).Colony;

    /// <summary>
    /// The colony a save's text holds, checked part by part (with `content`, everything it names must be in the
    /// content). Then, for a save that reads well (step 16, library Review 35):
    ///
    /// - its **checksum**: if it does not match, the save was changed outside the game or is damaged. It is refused
    ///   (ColonySaveChangedException) unless the player chose to load it anyway (`acceptChanged`), and then a
    ///   warning says so.
    /// - its **content fingerprint**: if the content was changed since, a warning says so, and the colony goes on
    ///   with the current values.
    /// </summary>
    public static Opened Opening(string text, Content? content = null, bool acceptChanged = false)
    {
        var colony = Build(text, content);
        var save = (JsonObject)JsonNode.Parse(text)!;
        var warnings = new List<string>();
        if (!ChecksumMatches(save))
        {
            if (!acceptChanged)
                throw new ColonySaveChangedException("this save was changed outside the game, or is damaged: its checksum does not "
                    + "match. Load it anyway only if you changed it yourself.");
            warnings.Add("This save was changed outside the game (its checksum does not match); it was loaded as you asked.");
        }
        string? made = save["content"] is JsonValue cv && cv.TryGetValue(out string? c) ? c : null;
        if (content != null && made != content.Fingerprint)
            warnings.Add("The game's content (goods, recipes, values) has changed since this save was made; the colony goes on "
                + "with the current values, so it may play out differently from before.");
        return new Opened(colony, warnings);
    }

    /// <summary>The colony a save's text holds, checked part by part; with `content`, every good it names must be
    /// one the content knows.</summary>
    static Colony Build(string text, Content? content)
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
        if (save.ContainsKey("work") && save["work"] is not null && content == null)
            throw new ColonySaveException("it holds work (workers, workshops, jobs), which needs the colony's content to load");
        var colony = new Colony(seed, new Moment(founded), save["work"] is null ? null : content);
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
        colony.SetGround(ReadGround(save, content));
        foreach (var pile in colony.Stocks.Inventories.Where(i => i.Area != null))
            if (colony.Ground is not Ground g || pile.Area!.Tiles.Any(tile => !g.Inside(tile.X, tile.Y)))
                throw new ColonySaveException("the stockpile \"" + pile.Name + "\" lies off the ground");
        if (colony.Work != null)
            ReadWork(Get<JsonObject>(save, "work"), colony, content!);
        return colony;
    }

    static void ReadWork(JsonObject work, Colony colony, Content content)
    {
        var stocks = colony.Stocks;
        var held = new HashSet<long>(stocks.Inventories.Select(i => i.Id.Value));
        long Id(JsonObject o, string key, string at) => Number(o[key], at + "." + key);
        InventoryId Inv(JsonObject o, string key, string at)
        {
            long id = Id(o, key, at);
            return held.Contains(id) ? new InventoryId(id) : throw new ColonySaveException(at + "." + key + " names inventory " + id + ", which does not exist");
        }
        string Text(JsonObject o, string key, string at) =>
            o[key] is JsonValue v && v.TryGetValue(out string? s) ? s : throw new ColonySaveException(at + "." + key + " is not text");
        double Real(JsonObject o, string key, string at) =>
            o[key] is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? d : throw new ColonySaveException(at + "." + key + " is not a number");
        void Given<T>(long id, string at) where T : IStableId<T>
        {
            if (id < 1 || id > colony.Ids.Last.GetValueOrDefault(T.Family))
                throw new ColonySaveException(at + " " + id + " was never given out");
        }
        JsonObject Item(JsonNode? n, string at) => n as JsonObject ?? throw new ColonySaveException(at + " is not a table");

        var sites = new HashSet<long>();
        foreach (var (node, i) in (work["sites"] as JsonArray ?? throw new ColonySaveException("work.sites is not a list")).Select((n, i) => (n, i)))
        {
            string at = "work.sites[" + i + "]";
            var o = Item(node, at);
            long id = Id(o, "id", at);
            Given<BuildingId>(id, at + ".id");
            string kind = Text(o, "workshop", at);
            if (!content.Workshops.ContainsKey(kind))
                throw new ColonySaveException(at + ".workshop \"" + kind + "\" is not in the content");
            var site = new Site(new BuildingId(id), kind, Inv(o, "input", at), Inv(o, "output", at), Real(o, "x", at), Real(o, "y", at));
            if (o["recipe"] is not null)
            {
                string recipe = Text(o, "recipe", at);
                if (!content.Recipes.TryGetValue(recipe, out var r) || r.Workshop != kind)
                    throw new ColonySaveException(at + ".recipe \"" + recipe + "\" is not one this workshop can work");
                site.Recipe = recipe;
            }
            site.Batches = (int)Id(o, "batches", at);
            if (!sites.Add(id))
                throw new ColonySaveException(at + ".id " + id + " is given twice");
            colony.Work!.Put(site);
        }
        // Construction projects (step 15a): their numbers are buildings' numbers, so never a workshop's.
        var projects = new HashSet<long>();
        foreach (var (node, i) in (work["projects"] as JsonArray ?? throw new ColonySaveException("work.projects is not a list")).Select((n, i) => (n, i)))
        {
            string at = "work.projects[" + i + "]";
            var o = Item(node, at);
            long id = Id(o, "id", at);
            Given<BuildingId>(id, at + ".id");
            if (sites.Contains(id) || !projects.Add(id))
                throw new ColonySaveException(at + ".id " + id + " is given twice");
            string piece = Text(o, "piece", at);
            if (!content.Pieces.ContainsKey(piece))
                throw new ColonySaveException(at + ".piece \"" + piece + "\" is not in the content");
            int x = (int)Id(o, "x", at), y = (int)Id(o, "y", at);
            bool Flag(string key) => o[key] is JsonValue fv && fv.TryGetValue(out bool f) ? f : throw new ColonySaveException(at + "." + key + " is not true or false");
            bool removing = Flag("removing"), cancelled = Flag("cancelled");
            var kind = content.Pieces[piece];
            bool fits = colony.Ground is Ground ground
                && (removing
                    // A taking down not yet done: the piece is still there.
                    ? cancelled || ground.PieceAt(x, y) == piece
                    // A building: every tile of it free.
                    : Enumerable.Range(y, kind.Height).All(ty => Enumerable.Range(x, kind.Width).All(tx =>
                        ground.Inside(tx, ty) && ground.ObstacleAt(tx, ty) == null && ground.PieceAt(tx, ty) == null)));
            if (!fits)
                throw new ColonySaveException(at + " is planned where it cannot be (" + x + ", " + y + ")");
            var stock = Inv(o, "stock", at);
            if (stocks[stock].Holder != Holder.Construction)
                throw new ColonySaveException(at + ".stock is not a building site's inventory");
            colony.Work!.Put(new Project(new BuildingId(id), piece, x, y, stock) { Paused = Flag("paused"), Cancelled = cancelled, Removing = removing });
        }
        var claimsNow = new HashSet<long>(stocks.Reservations.Select(r => r.Id.Value));
        var jobIds = new HashSet<long>();
        var jobWorkers = new Dictionary<long, long>();
        foreach (var (node, i) in (work["jobs"] as JsonArray ?? throw new ColonySaveException("work.jobs is not a list")).Select((n, i) => (n, i)))
        {
            string at = "work.jobs[" + i + "]";
            var o = Item(node, at);
            long id = Id(o, "id", at);
            Given<TaskId>(id, at + ".id");
            if (!jobIds.Add(id))
                throw new ColonySaveException(at + ".id " + id + " is given twice");
            if (!Enum.TryParse<JobKind>(Text(o, "kind", at), out var kind) || !Enum.IsDefined(kind))
                throw new ColonySaveException(at + ".kind is not a kind of job");
            if (!Enum.TryParse<Step>(Text(o, "step", at), out var step) || !Enum.IsDefined(step))
                throw new ColonySaveException(at + ".step is not a step of a job");
            var claims = new List<ReservationId>();
            foreach (var c in o["claims"] as JsonArray ?? throw new ColonySaveException(at + ".claims is not a list"))
            {
                long claim = Number(c, at + ".claims");
                if (!claimsNow.Remove(claim))
                    throw new ColonySaveException(at + " holds claim " + claim + ", which does not exist or another job holds");
                claims.Add(new ReservationId(claim));
            }
            BuildingId? site = o["site"] is null ? null : new BuildingId(Id(o, "site", at));
            if (site is BuildingId s && !(kind == JobKind.Build ? projects : sites).Contains(s.Value))
                throw new ColonySaveException(at + ".site " + s.Value + " does not exist");
            if (kind == JobKind.Build && site == null)
                throw new ColonySaveException(at + " is a build without a project");
            string? recipe = o["recipe"] is null ? null : Text(o, "recipe", at);
            if (kind == JobKind.Craft && (site == null || recipe == null || !content.Recipes.ContainsKey(recipe)))
                throw new ColonySaveException(at + " is a craft without a workshop and a recipe it knows");
            ColonistId? worker = o["worker"] is null ? null : new ColonistId(Id(o, "worker", at));
            if ((worker == null) != (step == Step.Open))
                throw new ColonySaveException(at + " is " + step + (worker == null ? " with no worker" : " but has a worker"));
            if (worker is ColonistId w)
                jobWorkers[id] = w.Value;
            int amount = (int)Id(o, "amount", at), left = (int)Id(o, "left", at);
            string good = Text(o, "good", at);
            if (kind is JobKind.Haul or JobKind.Boil && (!content.Goods.ContainsKey(good) || amount < 1))
                throw new ColonySaveException(at + " hauls an unknown good or nothing");
            InventoryId? boiling = o["boiling"] is null ? null : Inv(o, "boiling", at);
            if ((kind == JobKind.Boil) != (boiling != null))
                throw new ColonySaveException(at + ": a boiling, and only a boiling, names the water it boils");
            colony.Work!.Put(new Job
            {
                Id = new TaskId(id), Kind = kind, Good = good, Amount = amount, From = Inv(o, "from", at), To = Inv(o, "to", at),
                Site = site, Recipe = recipe, Claims = claims, First = new OperationId(Id(o, "first", at)),
                Second = new OperationId(Id(o, "second", at)), Third = new OperationId(Id(o, "third", at)), Boiling = boiling,
                Worker = worker, Step = step, Left = left, Purpose = Text(o, "purpose", at),
            });
        }
        // Claims held by colonists eating or drinking are theirs, not a job's.
        var personalClaims = new HashSet<long>();
        foreach (var (node, i) in (work["workers"] as JsonArray ?? new JsonArray()).Select((n, i) => (n, i)))
        {
            if (node?["personal"] is not JsonObject po)
                continue;
            string at = "work.workers[" + i + "].personal";
            if (po["kind"]?.ToString() is "Eat" or "Drink" && po["claim"] is null)
                throw new ColonySaveException(at + ": eating and drinking hold a claim, sleeping none, washing one at a basin");
            if (po["claim"] is null)
                continue;
            long claim = Number(po["claim"], at + ".claim");
            if (!stocks.Reservations.Any(r => r.Id.Value == claim))
                throw new ColonySaveException(at + ".claim " + claim + " does not exist");
            personalClaims.Add(claim);
        }
        claimsNow.ExceptWith(personalClaims);
        if (claimsNow.Count > 0)
            throw new ColonySaveException("claim " + claimsNow.Min() + " belongs to no job"); 
        var workerIds = new HashSet<long>();
        foreach (var (node, i) in (work["workers"] as JsonArray ?? throw new ColonySaveException("work.workers is not a list")).Select((n, i) => (n, i)))
        {
            string at = "work.workers[" + i + "]";
            var o = Item(node, at);
            long id = Id(o, "id", at);
            Given<ColonistId>(id, at + ".id");
            if (!workerIds.Add(id))
                throw new ColonySaveException(at + ".id " + id + " is given twice");
            var arms = Inv(o, "arms", at);
            if (stocks[arms].Holder != Holder.Carrier)
                throw new ColonySaveException(at + ".arms is not a carrier's inventory");
            bool Flag(string key) => o[key] is JsonValue fv && fv.TryGetValue(out bool f) ? f : throw new ColonySaveException(at + "." + key + " is not true or false");
            string schedule = Text(o, "schedule", at);
            if (!content.Schedules.ContainsKey(schedule))
                throw new ColonySaveException(at + ".schedule \"" + schedule + "\" is not in the content");
            string? collapsed = o["collapsed"] is null ? null : Text(o, "collapsed", at);
            if (collapsed != null && !content.Needs.ContainsKey(collapsed))
                throw new ColonySaveException(at + ".collapsed is not a need");
            var worker = new Worker(new ColonistId(id), Text(o, "name", at), Real(o, "x", at), Real(o, "y", at), arms)
            {
                Stopped = Flag("stopped"), Schedule = schedule, Asleep = Flag("asleep"), OnBedroll = Flag("on_bedroll"),
                Collapsed = collapsed, Lacking = o["lacking"] is null ? null : Text(o, "lacking", at),
            };
            double legs = Real(o, "legs", at);
            if (!(legs > 0 && legs <= 1))
                throw new ColonySaveException(at + ".legs is " + legs + ", which should be above 0 and at most 1");
            worker.Legs = legs;
            if (o["illness"] is JsonArray ill)
            {
                if (ill.Count != 3 || ill[0] is not JsonValue nv || !nv.TryGetValue(out string? illness) || illness != content.UpsetStomach.Name)
                    throw new ColonySaveException(at + ".illness is not [name, shows, ends] of an illness the content knows");
                long shows = Number(ill[1], at + ".illness"), ends = Number(ill[2], at + ".illness");
                if (ends <= shows)
                    throw new ColonySaveException(at + ".illness ends before it shows");
                worker.Illness = new Illness(illness, shows, ends);
            }
            else if (o["illness"] is not null)
                throw new ColonySaveException(at + ".illness is not [name, shows, ends]");
            double carry = Real(o, "carry", at);
            if (!(carry >= 0 && carry < 1))
                throw new ColonySaveException(at + ".carry is " + carry + ", which should be from 0 to below 1");
            worker.Carry = carry;
            // Skills (steps 23a, 23b): each one the content knows. One the content no longer has is refused; one the
            // content has added since starts at the usual proficiency, as for a colonist added by hand.
            if (o["skills"] is not JsonObject skills)
                throw new ColonySaveException(at + ".skills is not a table of skill and [proficiency, best, aptitude, practised, last practised]");
            foreach (var (skill, value) in skills)
            {
                if (!content.Skills.ContainsKey(skill))
                    throw new ColonySaveException(at + ".skills." + skill + " is not a skill the content knows");
                if (value is not JsonArray sa || sa.Count != 5
                    || !(sa[0] is JsonValue p0 && p0.TryGetValue(out double proficiency) && proficiency >= 0 && proficiency <= 100)
                    || !(sa[1] is JsonValue p1 && p1.TryGetValue(out double peak) && peak >= proficiency && peak <= 100)
                    || !(sa[2] is JsonValue p2 && p2.TryGetValue(out double aptitude) && aptitude > 0 && double.IsFinite(aptitude))
                    || !(sa[3] is JsonValue p3 && p3.TryGetValue(out long practised) && practised >= 0)
                    || !(sa[4] is JsonValue p4 && p4.TryGetValue(out long last) && last <= colony.Now.Tick))
                    throw new ColonySaveException(at + ".skills." + skill + " is not [proficiency (0 to 100), best reached (not below it, 100 at most), "
                                                  + "aptitude (above 0), seconds practised (0 or more), last practised (not after now)]");
                worker.Skills[skill] = new SkillState(proficiency, aptitude, last) { Peak = peak, Practised = practised };
            }
            foreach (string skill in content.Skills.Keys.Where(k => !worker.Skills.ContainsKey(k)))
                worker.Skills[skill] = new SkillState(content.StandardProficiency, 1, colony.Now.Tick);
            long dueTick = 0;
            if (o["think"] is not JsonArray think || think.Count != 4
                || !(think[0] is JsonValue t0 && t0.TryGetValue(out int every) && every >= 1)
                || !(think[1] is JsonValue t1 && t1.TryGetValue(out long next) && next > colony.Now.Tick)
                || !(think[2] is JsonValue t2 && t2.TryGetValue(out long thought) && thought <= colony.Now.Tick)
                || !(think[3] is null || think[3] is JsonValue t3 && t3.TryGetValue(out dueTick) && dueTick > colony.Now.Tick))
                throw new ColonySaveException(at + ".think is not [every (1 or more ticks), next think (after now), last processed (not after now), "
                                              + "action deadline (after now, or null)]");
            (worker.ThinkEvery, worker.NextThink, worker.Thought) = (every, next, thought);
            worker.ActionDue = think[3] is null ? null : dueTick;
            if (o["heading"] is JsonArray heading)
            {
                if (heading.Count != 3 || !(heading[0] is JsonValue h0 && h0.TryGetValue(out double hx) && double.IsFinite(hx))
                    || !(heading[1] is JsonValue h1 && h1.TryGetValue(out double hy) && double.IsFinite(hy))
                    || !(heading[2] is JsonValue h2 && h2.TryGetValue(out bool hnear)))
                    throw new ColonySaveException(at + ".heading is not [x, y, near]");
                worker.Heading = (hx, hy, hnear);
            }
            else if (o["heading"] is not null)
                throw new ColonySaveException(at + ".heading is not [x, y, near]");
            var kinds = colony.Work!.Kinds.ToHashSet();
            foreach (var k in o["forbidden"] as JsonArray ?? throw new ColonySaveException(at + ".forbidden is not a list"))
            {
                string kind = k is JsonValue kv && kv.TryGetValue(out string? s) ? s : throw new ColonySaveException(at + ".forbidden holds something that is not text");
                if (!kinds.Contains(kind))
                    throw new ColonySaveException(at + ".forbidden names \"" + kind + "\", which is no kind of work");
                worker.Forbidden.Add(kind);
            }
            var needs = Item(o["needs"], at + ".needs");
            foreach (string need in content.Needs.Keys)
            {
                if (needs[need] is not JsonArray pair || pair.Count != 2
                    || !(pair[0] is JsonValue lv && lv.TryGetValue(out double level) && level >= 0 && level <= 100)
                    || !(pair[1] is JsonValue mv && mv.TryGetValue(out int minutes) && minutes >= 0))
                    throw new ColonySaveException(at + ".needs." + need + " is not a level (0 to 100) and minutes at the limit");
                worker.Needs[need].Level = level;
                worker.Needs[need].MinutesAtLimit = minutes;
            }
            if (o["personal"] is JsonObject po)
            {
                string pat = at + ".personal";
                if (!Enum.TryParse<PersonalKind>(Text(po, "kind", pat), out var pk) || !Enum.IsDefined(pk))
                    throw new ColonySaveException(pat + ".kind is not eating, drinking, sleeping or washing");
                ReservationId? claim = po["claim"] is null ? null : new ReservationId(Id(po, "claim", pat));
                if (claim is ReservationId rc && !stocks.Reservations.Any(r => r.Id == rc))
                    throw new ColonySaveException(pat + ".claim " + rc.Value + " does not exist");
                if (pk is PersonalKind.Eat or PersonalKind.Drink && claim == null || pk == PersonalKind.Sleep && claim != null)
                    throw new ColonySaveException(pat + ": eating and drinking hold a claim, sleeping none, washing one at a basin");
                int? left = po["left"] is null ? null : (int)Id(po, "left", pat);
                worker.Personal = new Personal
                {
                    Kind = pk, X = Real(po, "x", pat), Y = Real(po, "y", pat), Claim = claim,
                    Operation = new OperationId(Id(po, "operation", pat)), Bedroll = po["bedroll"] is JsonValue bv && bv.TryGetValue(out bool bb) && bb,
                    Left = left,
                };
            }
            if (o["job"] is not null)
            {
                long job = Id(o, "job", at);
                if (jobWorkers.GetValueOrDefault(job) != id)
                    throw new ColonySaveException(at + " is doing job " + job + ", which is not theirs");
                worker.Job = new TaskId(job);
            }
            colony.Work!.Put(worker);
        }
        var bedIds = new HashSet<long>();
        foreach (var (node, i) in (work["bedrolls"] as JsonArray ?? throw new ColonySaveException("work.bedrolls is not a list")).Select((n, i) => (n, i)))
        {
            string at = "work.bedrolls[" + i + "]";
            var o = Item(node, at);
            long id = Id(o, "id", at);
            Given<BuildingId>(id, at + ".id");
            if (sites.Contains(id) || projects.Contains(id) || !bedIds.Add(id))
                throw new ColonySaveException(at + ".id " + id + " is given twice");
            ColonistId? owner = o["owner"] is null ? null : new ColonistId(Id(o, "owner", at));
            if (owner is ColonistId ow && !workerIds.Contains(ow.Value))
                throw new ColonySaveException(at + ".owner " + ow.Value + " is no colonist");
            colony.Work!.Put(new Bedroll(new BuildingId(id), Real(o, "x", at), Real(o, "y", at), owner));
        }
        foreach (var node in work["springs"] as JsonArray ?? throw new ColonySaveException("work.springs is not a list"))
        {
            if (node is not JsonArray entry || entry.Count != 7)
                throw new ColonySaveException("work.springs holds something that is not [store, kind, litres, frozen, fouled until, sign, sign seen]");
            long id = Number(entry[0], "work.springs"), litres = Number(entry[2], "work.springs");
            if (!held.Contains(id) || stocks[new InventoryId(id)].Holder != Holder.Store || !stocks[new InventoryId(id)].SourceOnly)
                throw new ColonySaveException("work.springs names " + id + ", which is not a store of water that only gives");
            if (!Enum.TryParse<WaterKind>(entry[1]?.ToString(), out var kind) || !Enum.IsDefined(kind))
                throw new ColonySaveException("work.springs gives " + id + " the kind \"" + entry[1] + "\", which is no kind of water");
            if (litres < 0 || litres > int.MaxValue)
                throw new ColonySaveException("work.springs gives " + id + " " + litres + " litres a day, which cannot be");
            if (entry[3] is not JsonValue fv || !fv.TryGetValue(out bool frozen))
                throw new ColonySaveException("work.springs gives " + id + " a frost that is not true or false");
            long? fouledUntil = entry[4] is null ? null : Number(entry[4], "work.springs");
            string? sign = entry[5] is null ? null : entry[5] is JsonValue sv && sv.TryGetValue(out string? s) ? s
                : throw new ColonySaveException("work.springs gives " + id + " a sign that is not text");
            if (entry[6] is not JsonValue ssv || !ssv.TryGetValue(out bool seen))
                throw new ColonySaveException("work.springs gives " + id + " a sign seen that is not true or false");
            if (sign != null && fouledUntil == null)
                throw new ColonySaveException("work.springs gives " + id + " a sign but no fouling");
            colony.Work!.PutSpring(new InventoryId(id), new WaterSource
            {
                Kind = kind, Litres = (int)litres, Frozen = frozen, FouledUntil = fouledUntil, Sign = sign, SignSeen = seen,
            });
        }
        foreach (var node in work["news"] as JsonArray ?? throw new ColonySaveException("work.news is not a list"))
        {
            if (node is not JsonArray item || item.Count != 2 || item[1] is not JsonValue tv || !tv.TryGetValue(out string? told))
                throw new ColonySaveException("work.news holds something that is not [tick, text]");
            colony.Work!.PutNews(new News(Number(item[0], "work.news"), told));
        }
        colony.Work!.Cold = work["cold"] is JsonValue cv && cv.TryGetValue(out bool cold)
            ? cold : throw new ColonySaveException("work.cold is not true or false");
        foreach (var (job, worker) in jobWorkers)
            if (colony.Work!.Worker(new ColonistId(worker)).Job != new TaskId(job))
                throw new ColonySaveException("work.jobs: job " + job + " names worker " + worker + ", who is not doing it");
    }

    /// <summary>The letters a save's rows use: the first surface (or obstacle kind) in its list is 'a', the next 'b',
    /// and so on; '.' is no obstacle.</summary>
    const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// The ground as JSON. Its surfaces and its obstacles are rows of letters, one row a line of tiles, top first:
    ///
    ///     "surfaces": [["grass", "Grass", 0.8], ["rough", "Rough ground", 0.55]],
    ///     "rows": ["aab", ...],                 // a, a: grass; b: rough ground
    ///     "obstacle_kinds": ["rock", "tree"],
    ///     "obstacle_rows": ["..b", ...],        // a tree on the third tile
    ///
    /// The finished pieces are few, so they stay a list of [piece, x, y].
    /// </summary>
    static JsonObject WriteGround(Ground g)
    {
        var surfaces = new SortedDictionary<string, Surface>(StringComparer.Ordinal) { [g.Surface.Id] = g.Surface };
        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
            {
                surfaces.TryAdd(g.SurfaceAt(x, y).Id, g.SurfaceAt(x, y));
                if (g.ObstacleAt(x, y) is string kind)
                    kinds.Add(kind);
            }
        if (surfaces.Count > Letters.Length || kinds.Count > Letters.Length)
            throw new InvalidOperationException("a ground of more than " + Letters.Length + " surfaces or kinds of obstacle cannot be saved");
        var surfaceLetter = surfaces.Keys.Select((id, i) => (id, Letters[i])).ToDictionary(p => p.id, p => p.Item2);
        var kindLetter = kinds.Select((k, i) => (k, Letters[i])).ToDictionary(p => p.k, p => p.Item2);
        var rows = new JsonArray();
        var obstacleRows = new JsonArray();
        var line = new System.Text.StringBuilder();
        for (int y = 0; y < g.Height; y++)
        {
            line.Clear();
            for (int x = 0; x < g.Width; x++)
                line.Append(surfaceLetter[g.SurfaceAt(x, y).Id]);
            rows.Add((JsonNode?)JsonValue.Create(line.ToString()));
            line.Clear();
            for (int x = 0; x < g.Width; x++)
                line.Append(g.ObstacleAt(x, y) is string kind ? kindLetter[kind] : '.');
            obstacleRows.Add((JsonNode?)JsonValue.Create(line.ToString()));
        }
        return new JsonObject
        {
            ["width"] = g.Width, ["height"] = g.Height,
            ["surfaces"] = new JsonArray(surfaces.Values.Select(s => (JsonNode?)new JsonArray(s.Id, s.Name, s.Speed)).ToArray()),
            ["surface"] = g.Surface.Id,
            ["rows"] = rows,
            ["obstacle_kinds"] = new JsonArray(kinds.Select(k => (JsonNode?)k).ToArray()),
            ["obstacle_rows"] = obstacleRows,
            ["pieces"] = new JsonArray(g.Pieces.Select(p => (JsonNode?)new JsonArray(p.Piece, p.X, p.Y)).ToArray()),
        };
    }

    /// <summary>The ground a save holds (or null: none), checked: every tile on it, no two things on one tile, and
    /// every piece one the content knows.</summary>
    static Ground? ReadGround(JsonObject save, Content? content)
    {
        if (!save.ContainsKey("ground"))
            throw new ColonySaveException("its \"ground\" is missing");
        if (save["ground"] is null)
            return null;
        if (save["ground"] is not JsonObject g)
            throw new ColonySaveException("its \"ground\" is not a table");
        long width = Number(g["width"], "ground.width"), height = Number(g["height"], "ground.height");
        if (width < 1 || height < 1 || width * height > 100_000_000)
            throw new ColonySaveException("its ground is " + width + " by " + height + " tiles, which cannot be");
        // Its surfaces: every one named once, with its speed; the ground's own, and the tiles of others.
        var surfaces = new Dictionary<string, Surface>(StringComparer.Ordinal);
        foreach (var (node, i) in (g["surfaces"] as JsonArray ?? throw new ColonySaveException("ground.surfaces is not a list")).Select((n, i) => (n, i)))
        {
            string at = "ground.surfaces[" + i + "]";
            if (node is not JsonArray e || e.Count != 3 || !(e[0] is JsonValue v0 && v0.TryGetValue(out string? sid))
                || !(e[1] is JsonValue v1 && v1.TryGetValue(out string? sname)) || !(e[2] is JsonValue v2 && v2.TryGetValue(out double speed)))
                throw new ColonySaveException(at + " is not [id, name, speed]");
            if (!(speed >= 0 && speed <= 1) || !surfaces.TryAdd(sid, new Surface(sid, sname, speed)))
                throw new ColonySaveException(at + " has a speed that cannot be, or is named twice");
        }
        string own = g["surface"] is JsonValue ov && ov.TryGetValue(out string? os) ? os : throw new ColonySaveException("ground.surface is not text");
        if (!surfaces.TryGetValue(own, out var ownSurface) || !ownSurface.CanWalk)
            throw new ColonySaveException("ground.surface \"" + own + "\" is not in ground.surfaces, or cannot be walked on");
        if (surfaces.Count > Letters.Length)
            throw new ColonySaveException("ground.surfaces has more than " + Letters.Length + " surfaces");
        var ground = new Ground((int)width, (int)height, ownSurface);
        // The order the surfaces are listed in gives their letters ('a' the first).
        var bySurfaceLetter = ((JsonArray)g["surfaces"]!).Select((n, i) => (surfaces[(string)n![0]!], Letters[i])).ToDictionary(p => p.Item2, p => p.Item1);
        var kinds = new List<string>();
        foreach (var (node, i) in (g["obstacle_kinds"] as JsonArray ?? throw new ColonySaveException("ground.obstacle_kinds is not a list")).Select((n, i) => (n, i)))
        {
            if (node is not JsonValue v || !v.TryGetValue(out string? kind) || kinds.Contains(kind) || kinds.Count == Letters.Length)
                throw new ColonySaveException("ground.obstacle_kinds[" + i + "] is not text, or is named twice");
            kinds.Add(kind);
        }
        // Each row: as many letters as the ground is wide, each one a letter its list gives.
        IEnumerable<(int X, int Y, char Letter)> Rows(string key)
        {
            if (g[key] is not JsonArray rows || rows.Count != height)
                throw new ColonySaveException("ground." + key + " should be " + height + " rows");
            for (int y = 0; y < height; y++)
            {
                if (rows[y] is not JsonValue rv || !rv.TryGetValue(out string? row) || row.Length != width)
                    throw new ColonySaveException("ground." + key + "[" + y + "] should be " + width + " letters");
                for (int x = 0; x < width; x++)
                    yield return (x, y, row[x]);
            }
        }
        foreach (var (x, y, letter) in Rows("rows").ToList())
        {
            if (!bySurfaceLetter.TryGetValue(letter, out var surface))
                throw new ColonySaveException("ground.rows[" + y + "] has '" + letter + "', which is not the letter of a surface in ground.surfaces");
            if (surface.Id != own)
                ground.SetSurface(x, y, surface);
        }
        foreach (var (x, y, letter) in Rows("obstacle_rows").ToList())
        {
            if (letter == '.')
                continue;
            int k = Letters.IndexOf(letter);
            if (k < 0 || k >= kinds.Count)
                throw new ColonySaveException("ground.obstacle_rows[" + y + "] has '" + letter + "', which is not the letter of a kind in ground.obstacle_kinds");
            ground.AddObstacle(kinds[k], x, y);
        }
        var used = new HashSet<(long, long)>();
        foreach (var (node, i) in (g["pieces"] as JsonArray ?? throw new ColonySaveException("ground.pieces is not a list")).Select((n, i) => (n, i)))
        {
            string at = "ground.pieces[" + i + "]";
            if (node is not JsonArray e || e.Count != 3 || !(e[0] is JsonValue v && v.TryGetValue(out string? piece)))
                throw new ColonySaveException(at + " is not [piece, x, y]");
            long x = Number(e[1], at), y = Number(e[2], at);
            if (x < 0 || y < 0 || x >= width || y >= height || !used.Add((x, y))
                || ground.ObstacleAt((int)x, (int)y) != null || ground.Water((int)x, (int)y))
                throw new ColonySaveException(at + " is off the ground, or on a tile that already holds something or is water");
            if (content == null || !content.Pieces.TryGetValue(piece, out var kind))
                throw new ColonySaveException("ground.pieces names \"" + piece + "\", which " + (content == null ? "needs the colony's content to load" : "is not in the content"));
            ground.AddPiece(piece, kind.Blocks, (int)x, (int)y, kind.Encloses, kind.Speed);
        }
        return ground;
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
            foreach (var g in o["accepts"] as JsonArray ?? throw new ColonySaveException(at + ".accepts is not a list"))
                inventory.Accepts.Add(Good(g, at + ".accepts"));
            inventory.Emptying = o["emptying"] is JsonValue ev && ev.TryGetValue(out bool emptying)
                ? emptying : throw new ColonySaveException(at + ".emptying is not true or false");
            inventory.SourceOnly = o["source_only"] is JsonValue sv && sv.TryGetValue(out bool sourceOnly)
                ? sourceOnly : throw new ColonySaveException(at + ".source_only is not true or false");
            inventory.WashPlace = o["wash_place"] is JsonValue wv && wv.TryGetValue(out bool washPlace)
                ? washPlace : throw new ColonySaveException(at + ".wash_place is not true or false");
            inventory.Mark = Enum.TryParse<WaterMark>(o["mark"]?.ToString(), out var mark) && Enum.IsDefined(mark)
                ? mark : throw new ColonySaveException(at + ".mark is not a mark (None, BoilFirst, DoNotDrink)");
            if (o["capacity"] is not null)
            {
                long capacity = Number(o["capacity"], at + ".capacity");
                if (capacity < 1 || capacity > int.MaxValue || holder != Holder.Store)
                    throw new ColonySaveException(at + ".capacity is not a whole number above 0, of a store");
                inventory.Capacity = (int)capacity;
            }
            if (o["area"] is not null)
            {
                if (o["area"] is not JsonArray area || area.Count != 4)
                    throw new ColonySaveException(at + ".area is not [x, y, width, height]");
                long[] v = area.Select((n, k) => Number(n, at + ".area[" + k + "]")).ToArray();
                if (v[2] < 1 || v[3] < 1 || v.Any(n => n < 0 || n > int.MaxValue) || holder != Holder.Store)
                    throw new ColonySaveException(at + ".area is not a block of tiles of a store");
                inventory.Area = new Area((int)v[0], (int)v[1], (int)v[2], (int)v[3]);
            }
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
        // Closed inventories (a finished building site's pile): gone, but the ledger's past lines may name them.
        var closed = new HashSet<long>();
        foreach (var node in Get<JsonArray>(save, "closed_inventories"))
        {
            long id = Number(node, "closed_inventories");
            if (id < 1 || id > colony.Ids.Last.GetValueOrDefault(InventoryId.Family) || known.Contains(id) || !closed.Add(id))
                throw new ColonySaveException("closed_inventories names " + id + ", which was never given out, is still open, or is named twice");
            stocks.PutClosed(new InventoryId(id));
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
                return known.Contains(place) || closed.Contains(place) ? new InventoryId(place)
                    : throw new ColonySaveException(at + " names inventory " + place + ", which does not exist");
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
