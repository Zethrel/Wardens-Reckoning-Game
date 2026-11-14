// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;
using Godot;
using WardensReckoning.Colony;

namespace WardensReckoning.UI;

/// <summary>
/// The colony's drawing benchmark (step 27b; the frame budget, the maintainer 14-11-2026: 60 frames a second, 16.7 ms,
/// in the densest forest view). With a colony founded or loaded, `-- --bench [file]` pauses it, finds the stretch of
/// its map with the most trees on screen at 16 px a tile, and measures there: as it is, with every crown see-through
/// (the Trees button), and closer in at 32 and 40 px a tile. For each it lets 60 frames go by, then measures 300 with
/// vsync off. The table goes to the console and to `file` (bench-colony.md in the folder it is started from if none
/// is given); then the game quits. The see-through setting is put back as it was, unsaved.
/// </summary>
public partial class ColonyScreen
{
    /// <summary>Runs the benchmark (Main calls it for `-- --bench`).</summary>
    public async void Bench(string file)
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        SetSpeed(Speed.Paused);                          // only drawing is measured, not the colony's ticks
        bool through = Settings.SeeThroughTrees;
        var ground = Colony.Ground!;
        var port = view!.GetViewport();
        RenderingServer.ViewportSetMeasureRenderTime(port.GetViewportRid(), true);
        for (int i = 0; i < 10; i++)                     // the map's frame gets its size
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var size = port.GetVisibleRect().Size;
        var (cx, cy, trees) = DensestView(ground, (int)Math.Ceiling(size.X / 16), (int)Math.Ceiling(size.Y / 16));
        var rows = new List<string>();
        foreach (var (pixels, seeThrough) in new[] { (16f, false), (16f, true), (32f, false), (40f, false) })
        {
            camera!.Look(new Vector2(cx, cy), pixels);
            Settings.SeeThroughTrees = seeThrough;
            for (int i = 0; i < 60; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var (frame, draw, gpu) = (new List<double>(), new List<double>(), new List<double>());
            ulong last = Time.GetTicksUsec();
            for (int i = 0; i < 300; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                ulong now = Time.GetTicksUsec();
                frame.Add((now - last) / 1000.0);
                last = now;
                draw.Add(view.DrawMs);
                gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(port.GetViewportRid()));
            }
            double average = frame.Average();
            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {pixels:0} | {(seeThrough ? "on" : "off")} | {view.TreesDrawn} | {draw.Average():0.00} | {gpu.Average():0.00} | {average:0.00} | {frame.Max():0.00} | {1000 / average:0} | {(average <= 1000.0 / 60 ? "yes" : "no")} |"));
            GD.Print("bench: " + rows[^1]);
        }
        Settings.SeeThroughTrees = through;

        var window = DisplayServer.WindowGetSize();
        string table = string.Join("\n", new[]
        {
            "# Colony drawing benchmark (step 27b)",
            "",
            "- When: " + DateTime.Now.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
            "- Graphics: " + RenderingServer.GetVideoAdapterName() + " (" + RenderingServer.GetVideoAdapterVendor() + "), "
                + RenderingServer.GetCurrentRenderingDriverName() + ", Godot " + Engine.GetVersionInfo()["string"],
            "- Window " + window.X + " × " + window.Y + ", the map " + (int)size.X + " × " + (int)size.Y + "; vsync off; the colony paused",
            "- Where: the densest view at 16 px a tile, centred on (" + cx.ToString("0.#", CultureInfo.InvariantCulture) + ", "
                + cy.ToString("0.#", CultureInfo.InvariantCulture) + "), " + trees + " tree tiles in it",
            "- Budget: 60 frames a second (16.7 ms), the maintainer, 14-11-2026",
            "",
            "| Px a tile | See-through | Trees drawn | _Draw (CPU) ms | Map rendering GPU ms | Frame ms | Slowest frame ms | Frames a second | Within budget |",
            "|---:|---|---:|---:|---:|---:|---:|---:|---|",
        }.Concat(rows)) + "\n";
        GD.Print(table);
        try
        {
            File.WriteAllText(file, table);
            GD.Print("bench: written to " + Path.GetFullPath(file));
        }
        catch (Exception e)
        {
            GD.PrintErr("bench: could not write " + file + ": " + e.Message);
        }
        GetTree().Quit();
    }

    // The middle of the `wide` × `high` tiles of the map with the most trees, and how many: counted with running sums,
    // so each window's count is four look-ups instead of counting every tile again.
    static (float X, float Y, int Trees) DensestView(Ground ground, int wide, int high)
    {
        wide = Math.Min(wide, ground.Width);
        high = Math.Min(high, ground.Height);
        var sum = new int[ground.Width + 1, ground.Height + 1];     // sum[x, y]: the trees left of x and above y
        for (int y = 0; y < ground.Height; y++)
            for (int x = 0; x < ground.Width; x++)
                sum[x + 1, y + 1] = sum[x, y + 1] + sum[x + 1, y] - sum[x, y] + (ground.ObstacleAt(x, y) == "tree" ? 1 : 0);
        var best = (X: 0, Y: 0, Trees: -1);
        for (int y = 0; y + high <= ground.Height; y++)
            for (int x = 0; x + wide <= ground.Width; x++)
            {
                int trees = sum[x + wide, y + high] - sum[x, y + high] - sum[x + wide, y] + sum[x, y];
                if (trees > best.Trees)
                    best = (x, y, trees);
            }
        return (best.X + wide / 2f, best.Y + high / 2f, best.Trees);
    }
}
