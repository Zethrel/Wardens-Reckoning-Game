// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The asset preview's benchmark (step A2b; the Forest Visibility specification v1.3, Stage 4). Started with
/// `-- --bench [file]`, it measures the test wood on its own and quits:
///
/// - v0.8's trees and the colony's trees (v3 compact), with the wood empty, a tree on every fourth tile, every other
///   tile and every tile;
/// - for the colony's trees, canopy fading off and on, with the wood's six colonists all picked (fades jump: Reduced
///   Motion, so they're finished when the measuring starts).
///
/// For each, it lets 60 frames go by, then measures 300 frames with vsync off (so frames aren't held back to the
/// screen's refresh): how long `_Draw` took (CPU), how long Godot took to render (CPU and GPU), and the time from one
/// frame to the next. The table goes to the console and to `file` (bench-a2b.md in the folder it is started from, if
/// none is given). The camera looks at the whole wood at 16 px a tile.
/// </summary>
public partial class AssetPreview
{
    // One line of the table: what was measured, and the averages (and the slowest frame).
    sealed record BenchRow(string Trees, string Wood, string Fading, int Count, double Draw, double RenderCpu, double RenderGpu,
                           double Frame, double Slowest);

    /// <summary>Runs the benchmark if the command line asks for it (called from _Ready).</summary>
    void StartBenchIfAsked()
    {
        var arguments = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(arguments, "--bench");
        if (at < 0)
            return;
        string file = at + 1 < arguments.Length && !arguments[at + 1].StartsWith("--") ? arguments[at + 1] : "bench-a2b.md";
        Bench(file);
    }

    // `async void`: it runs alongside the game, waiting for frames with `await`, and nothing waits for it to end.
    async void Bench(string file)
    {
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        walking = false;
        fader.ReducedMotion = true;
        SetZoom(16);
        camera!.Position = new Vector2(WoodWide / 2f, WoodTop + WoodHigh / 2f);
        var rid = GetViewport().GetViewportRid();
        int v08 = Array.IndexOf(Sets, "v0.8"), colony = Sets.Length + Experiments.Length;
        var rows = new List<BenchRow>();

        foreach (int set in new[] { v08, colony })
            for (int density = 0; density < Woods.Length; density++)
                foreach (bool fade in set == colony && density > 0 ? new[] { false, true } : new[] { false })
                {
                    treeSet = set;
                    wood = density;
                    fading = fade;
                    picked.Clear();
                    if (fade)
                        for (int i = 1; i <= WoodColonists.Length; i++)
                            picked.Add("wood " + i);
                    for (int i = 0; i < 60; i++)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                    var (draw, cpu, gpu, frame) = (new List<double>(), new List<double>(), new List<double>(), new List<double>());
                    ulong last = Time.GetTicksUsec();
                    for (int i = 0; i < 300; i++)
                    {
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        ulong now = Time.GetTicksUsec();
                        frame.Add((now - last) / 1000.0);
                        last = now;
                        draw.Add(drawMs);
                        cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid));
                        gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid));
                    }
                    rows.Add(new BenchRow(set == v08 ? "v0.8" : "colony's (v3)", Woods[density], set == colony && density > 0 ? (fade ? "on" : "off") : "–",
                                          WoodTrees().Count(), draw.Average(), cpu.Average(), gpu.Average(), frame.Average(), frame.Max()));
                    GD.Print("bench: " + rows[^1]);
                }

        string table = BenchTable(rows);
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

    // The table, in Markdown, with what it ran on.
    string BenchTable(List<BenchRow> rows)
    {
        var c = CultureInfo.InvariantCulture;
        var size = DisplayServer.WindowGetSize();
        var lines = new List<string>
        {
            "# Asset preview benchmark (step A2b)",
            "",
            "- When: " + DateTime.Now.ToString("dd-MM-yyyy HH:mm", c),
            "- Graphics: " + RenderingServer.GetVideoAdapterName() + " (" + RenderingServer.GetVideoAdapterVendor() + "), "
                + RenderingServer.GetCurrentRenderingDriverName() + ", Godot " + Engine.GetVersionInfo()["string"],
            "- Window " + size.X + " × " + size.Y + ", 16 px a tile, vsync off; 60 frames to settle, then 300 measured",
            "- Fading on: the wood's six colonists picked, fades finished (Reduced Motion)",
            "",
            "| Trees | Wood | Fading | Trees drawn | _Draw (CPU) ms | Rendering CPU ms | Rendering GPU ms | Frame ms | Slowest frame ms | Frames a second |",
            "|---|---|---|---:|---:|---:|---:|---:|---:|---:|",
        };
        foreach (var r in rows)
            lines.Add(string.Create(c, $"| {r.Trees} | {r.Wood} | {r.Fading} | {r.Count} | {r.Draw:0.00} | {r.RenderCpu:0.00} | {r.RenderGpu:0.00} | {r.Frame:0.00} | {r.Slowest:0.00} | {1000 / r.Frame:0} |"));
        return string.Join("\n", lines) + "\n";
    }
}
