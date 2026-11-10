// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using WardensReckoning.Colony;
using WardensReckoning.World;
using Calendar = WardensReckoning.Colony.Calendar;

namespace WardensReckoning.Profile;

/// <summary>
/// The performance scenarios (step 20b; the design library's FP-12 and Review 24's R24-08): five named colony
/// scenarios, each from a fixed start (map, seed, date, commands), measured as they run:
///
/// - **idle**: the three founders and nothing ordered, three days.
/// - **hauling**: twelve colonists, four querns 25 to 60 m from the carts with 50 batches of grinding each (all
///   the wheat carried there and the flour carried back), two days.
/// - **arbitration**: twelve colonists and 300 pieces of wall planned at once around the camp, two days: many
///   jobs, everyone choosing among them every minute.
/// - **save**: the arbitration colony after one day, saved, written and loaded twenty times.
/// - **interrupted**: the hauling colony, with everyone carrying stopped every quarter hour and set going again
///   five minutes later, and a haul cancelled every hour, two days.
///
/// For each it reports the time of each tick (one in-game minute) as mean, median, 95th and 99th percentile and
/// the slowest; the path-finding (ways asked for, worked out, their time, the most kept at once); the managed
/// memory at its highest (measured every in-game hour, after clearing away what earlier scenarios left) and the
/// garbage collections. The first in-game hour of a
/// scenario is left out of the tick times (the program is still being compiled to machine code then).
///
/// Measuring only reads the colony: the same scenario plays out the same, measured or not.
///
/// Run it as a Release build, from the repository's folder:
/// <code>dotnet run -c Release --project tools/Profile -- [scenario...] [--site site-01.json] [--out results.md]</code>
/// Without a scenario, all five run. Without `--site`, the colonies stand on the program's own map (256 × 256
/// tiles, a brook, trees and rocks, always the same); with it, on that local map from a world's export.
/// </summary>
public static class Program
{
    const int WarmUp = 60;

    static readonly Moment Start = new(Calendar.TickOf(1, 4, 1, 8, 0));

    public static int Main(string[] args)
    {
        var names = new List<string>();
        string? site = null, outPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--site" && i + 1 < args.Length)
                site = args[++i];
            else if (args[i] == "--out" && i + 1 < args.Length)
                outPath = args[++i];
            else
                names.Add(args[i]);
        }
        var all = new[] { "idle", "hauling", "arbitration", "save", "interrupted" };
        if (names.Count == 0)
            names.AddRange(all);
        if (names.FirstOrDefault(n => !all.Contains(n)) is string unknown)
        {
            Console.Error.WriteLine("No scenario \"" + unknown + "\": they are " + string.Join(", ", all) + ".");
            return 2;
        }
        string root = FindRoot();
        var content = Content.Load(Path.Combine(root, "content"));
        var map = site == null ? OwnMap() : LocalMap.Parse(JsonNode.Parse(File.ReadAllText(site))!.AsObject(), Path.GetFileName(site));

        var report = new StringBuilder();
        report.AppendLine("Measured " + DateTime.Now.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)
                          + " on " + RuntimeInformation.OSDescription + ", " + Environment.ProcessorCount + " logical processors, "
                          + RuntimeInformation.FrameworkDescription + (Debugger.IsAttached ? " (debugger attached)" : "")
                          + (IsRelease ? ", Release build" : ", DEBUG BUILD: not comparable") + ".");
        report.AppendLine("Map: " + (site == null ? "the profiler's own (256 × 256, a brook, trees and rocks)" : Path.GetFileName(site) + " (" + map.Size + " × " + map.Size + ")") + ".");
        report.AppendLine();
        report.AppendLine("| Scenario | Ticks | Tick mean ms | Median | 95% | 99% | Slowest | Ways asked | Worked out | Their ms | Most kept | Peak MB | GCs (0/1/2) |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        string? saveLine = null;
        var notes = new List<string>();
        foreach (var name in names)
        {
            Console.Error.WriteLine("Running " + name + "...");
            if (name == "save")
            {
                saveLine = SaveScenario(content, map);
                continue;
            }
            var (colony, command, days) = Setup(name, content, map);
            report.AppendLine(Measure(name, colony, command, days));
            if (name == "arbitration")
                notes.Add("Arbitration: " + colony.Ground!.Pieces.Count(p => p.Piece == "wall") + " of the 300 walls built in its two days.");
        }
        if (saveLine != null)
        {
            report.AppendLine();
            report.AppendLine("| Save scenario | Size KB | Text ms (mean / slowest) | Written ms | Loaded ms |");
            report.AppendLine("|---|---|---|---|---|");
            report.AppendLine(saveLine);
        }
        report.AppendLine();
        foreach (var note in notes)
            report.AppendLine(note);
        report.AppendLine("Process peak working set: " + (Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0).ToString("0", CultureInfo.InvariantCulture)
                          + " MB (all the scenarios of this run together).");
        Console.Write(report.ToString());
        if (outPath != null)
            File.WriteAllText(outPath, report.ToString());
        return 0;
    }

#if DEBUG
    const bool IsRelease = false;
#else
    const bool IsRelease = true;
#endif

    // ---- The scenarios -----------------------------------------------------------------------------------------

    // A colony for a scenario, its commands (given after every tick) and how many days it runs.
    static (Colony.Colony Colony, Action<Colony.Colony>? Command, int Days) Setup(string name, Content content, LocalMap map)
    {
        var (colony, carts) = Founding.Found(20, Start, content, map);
        if (name == "idle")
            return (colony, null, 3);
        var work = colony.Work!;
        var ground = colony.Ground!;
        var (cx, cy) = Ground.TileOf(carts.X, carts.Y);
        for (int i = 0; i < 9; i++)
        {
            var (x, y) = OpenNear(ground, cx - 2 + i % 3, cy + 3 + i / 3);
            work.AddWorker("Helper " + (i + 1), x + 0.5, y + 0.5);
        }
        if (name == "arbitration")
        {
            Give(colony, carts, "timber", 3000);
            int planned = 0;
            // Short walls in rings of 2 m tiles around the camp, from 8 m out, until 300 are planned.
            for (int ring = 8; planned < 300 && ring < 60; ring += 3)
                for (int t = -ring; t <= ring && planned < 300; t += 2)
                    foreach (var (x, y) in new[] { (cx + t, cy - ring), (cx + t, cy + ring), (cx - ring, cy + t), (cx + ring, cy + t) })
                        if (planned < 300 && ground.Open(x, y) && work.Plan("wall", x, y).Placement.Ok)
                            planned++;
            return (colony, null, 2);
        }
        // hauling and interrupted: four querns, 50 batches each (250 wheat each).
        Give(colony, carts, "wheat", 1000);
        foreach (var (dx, dy) in new[] { (25, 0), (-35, 10), (0, -45), (40, 40) })
        {
            var (qx, qy) = OpenNear(ground, cx + dx, cy + dy);
            var quern = work.AddSite("quern", qx + 0.5, qy + 0.5);
            work.Order(quern.Id, "grind_wheat", 50);
        }
        if (name == "hauling")
            return (colony, null, 2);
        // interrupted
        var stopped = new List<ColonistId>();
        void Interrupt(Colony.Colony c)
        {
            var w = c.Work!;
            long minute = c.Now.Tick - Start.Tick;
            if (minute % 15 == 0)
                foreach (var job in w.Jobs.Where(j => j.Step == Step.Carrying && j.Worker != null).ToList())
                {
                    w.Stop(job.Worker!.Value);
                    stopped.Add(job.Worker!.Value);
                }
            if (minute % 15 == 5)
            {
                foreach (var id in stopped)
                    w.Resume(id);
                stopped.Clear();
            }
            if (minute % 60 == 30 && w.Jobs.FirstOrDefault(j => j.Kind == JobKind.Haul) is Job haul)
                w.Cancel(haul.Id);
        }
        return (colony, Interrupt, 2);
    }

    // Runs a scenario minute by minute, timing each tick, and gives its row of the table.
    static string Measure(string name, Colony.Colony colony, Action<Colony.Colony>? command, int days)
    {
        var ground = colony.Ground!;
        long ticks = (long)days * Calendar.TicksPerDay;
        // What the scenarios before this one left behind is cleared away first, so its memory is its own.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var times = new double[ticks];
        long peakBytes = 0;
        int mostKept = 0;
        int[] gcBefore = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        long asked = ground.WaysAsked, worked = ground.WaysWorked, wayTime = ground.WayTime;
        for (long t = 0; t < ticks; t++)
        {
            long started = Stopwatch.GetTimestamp();
            colony.Run(1, command);
            times[t] = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            mostKept = Math.Max(mostKept, ground.WaysKept);
            if (t % 60 == 0)
                peakBytes = Math.Max(peakBytes, GC.GetTotalMemory(false));
        }
        var measured = times.Skip(WarmUp).OrderBy(x => x).ToArray();
        double Pct(double p) => measured[Math.Min(measured.Length - 1, (int)Math.Ceiling(p * measured.Length) - 1)];
        string F(double v) => v.ToString(v < 10 ? "0.000" : "0.0", CultureInfo.InvariantCulture);
        return "| " + name + " | " + ticks + " | " + F(measured.Average()) + " | " + F(Pct(0.5)) + " | " + F(Pct(0.95)) + " | " + F(Pct(0.99))
               + " | " + F(measured[^1]) + " | " + (ground.WaysAsked - asked) + " | " + (ground.WaysWorked - worked)
               + " | " + F((ground.WayTime - wayTime) * 1000.0 / Stopwatch.Frequency) + " | " + mostKept
               + " | " + (peakBytes / 1048576.0).ToString("0", CultureInfo.InvariantCulture)
               + " | " + (GC.CollectionCount(0) - gcBefore[0]) + "/" + (GC.CollectionCount(1) - gcBefore[1]) + "/" + (GC.CollectionCount(2) - gcBefore[2]) + " |";
    }

    // The arbitration colony after a day: its save made into text, written to a file and loaded, twenty times.
    static string SaveScenario(Content content, LocalMap map)
    {
        var (colony, _, _) = Setup("arbitration", content, map);
        colony.Run(Calendar.TicksPerDay);
        string folder = Path.Combine(Path.GetTempPath(), "wr-profile-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "save.json");
        var text = new List<double>();
        var written = new List<double>();
        var loaded = new List<double>();
        try
        {
            for (int i = 0; i < 20; i++)
            {
                long a = Stopwatch.GetTimestamp();
                ColonySave.Text(colony);
                long b = Stopwatch.GetTimestamp();
                ColonySave.Save(path, colony, backup: false);
                long c = Stopwatch.GetTimestamp();
                ColonySave.Load(path, content);
                long d = Stopwatch.GetTimestamp();
                text.Add((b - a) * 1000.0 / Stopwatch.Frequency);
                written.Add((c - b) * 1000.0 / Stopwatch.Frequency);
                loaded.Add((d - c) * 1000.0 / Stopwatch.Frequency);
            }
            long size = new FileInfo(path).Length;
            // The first of the twenty includes compiling the code; the mean leaves it out, the slowest does not.
            string F(List<double> v) => v.Skip(1).Average().ToString("0.0", CultureInfo.InvariantCulture) + " / " + v.Max().ToString("0.0", CultureInfo.InvariantCulture);
            return "| after a day of arbitration | " + (size / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " | " + F(text) + " | " + F(written) + " | " + F(loaded) + " |";
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    static void Give(Colony.Colony colony, Inventory to, string good, int amount) =>
        colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), colony.Now.Tick, to.Id, good, amount);

    // The open tile nearest (x, y), searching outwards ring by ring (the same tile every time).
    static (int X, int Y) OpenNear(Ground ground, int x, int y)
    {
        for (int r = 0; r < Math.Max(ground.Width, ground.Height); r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && ground.Open(x + dx, y + dy))
                        return (x + dx, y + dy);
        throw new InvalidOperationException("no open ground");
    }

    // The profiler's own map: 256 × 256 tiles of grass with patches of rough ground, a brook two tiles wide
    // running down from x = 170, and trees (about one tile in fifteen) and rocks (one in a hundred), always the
    // same (a seeded Random: this is a test map, not part of any colony's dice).
    static LocalMap OwnMap()
    {
        const int size = 256;
        var random = new Random(20);
        var ground = new JsonArray();
        var onGround = new JsonArray();
        for (int y = 0; y < size; y++)
        {
            var row = new StringBuilder();
            var things = new StringBuilder();
            int brook = 170 + (int)Math.Round(6 * Math.Sin(y / 20.0));
            for (int x = 0; x < size; x++)
            {
                bool water = x == brook || x == brook + 1;
                bool rough = !water && ((x / 32 + y / 32) % 3 == 0);
                row.Append(water ? 'w' : rough ? 'r' : 'g');
                double roll = random.NextDouble();
                things.Append(water ? '.' : roll < 0.01 ? 'R' : roll < 0.075 ? 'T' : '.');
            }
            ground.Add(row.ToString());
            onGround.Add(things.ToString());
        }
        return LocalMap.Parse(new JsonObject
        {
            ["format"] = "the-north-local", ["version"] = 1, ["size"] = size, ["biome"] = "meadow", ["water"] = "stream",
            ["surfaces"] = new JsonObject { ["g"] = "grass", ["r"] = "rough", ["w"] = "fresh_water" },
            ["things"] = new JsonObject { ["T"] = "tree", ["R"] = "rock" },
            ["ground"] = ground, ["on_ground"] = onGround,
        }, "profiler");
    }

    // The repository's folder (where content/ is), from the working folder or above it.
    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "content")) && File.Exists(Path.Combine(dir.FullName, "WardensReckoning.csproj")))
                return dir.FullName;
        throw new InvalidOperationException("Run this from the Wardens-Reckoning-game folder (content/ was not found).");
    }
}
