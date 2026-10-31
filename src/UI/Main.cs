// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.World;

namespace WardensReckoning.UI;

/// <summary>
/// The game's first node (src/UI/Main.tscn): it shows the main menu, and swaps it for the map when a
/// world is opened. Everything on screen is a child of this node.
/// </summary>
public partial class Main : Node
{
    Node? screen;

    public override void _Ready()
    {
        Settings.Load();
        Settings.Apply();
        // Started with `-- --world <folder or world.json>`, the game opens that world at once
        // (for testing: Godot passes everything after "--" to the game).
        var arguments = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(arguments, "--world");
        if (at >= 0 && at + 1 < arguments.Length)
            OpenWorld(arguments[at + 1]);
        else
            ShowMainMenu();
        // `-- --year <year>` shows that year of the opened world.
        int pick = Array.IndexOf(arguments, "--year");
        if (pick >= 0 && pick + 1 < arguments.Length && screen is MapScreen map && int.TryParse(arguments[pick + 1], out int year))
            map.CallDeferred(MapScreen.MethodName.PickYear, year);
        // `-- --screenshot <file.png>` saves what is on screen after a few frames and quits (for testing).
        int shot = Array.IndexOf(arguments, "--screenshot");
        if (shot >= 0 && shot + 1 < arguments.Length)
            Screenshot(arguments[shot + 1], Array.IndexOf(arguments, "--menu") >= 0);
    }

    async void Screenshot(string file, bool menu)
    {
        for (int i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (menu)
            Input.ParseInputEvent(new InputEventAction { Action = "ui_cancel", Pressed = true });
        for (int i = 0; i < 10; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(file);
        Quit();
    }

    void Show(Node next)
    {
        screen?.QueueFree();
        screen = next;
        AddChild(next);
    }

    public void ShowMainMenu(string message = "")
    {
        GetTree().Paused = false;
        Show(new MainMenu(this, message));
    }

    /// <summary>Opens the exported world in `path` (its folder or its world.json); back to the menu with the reason if it cannot.</summary>
    public void OpenWorld(string path)
    {
        ExportedWorld world;
        try
        {
            world = ExportedWorld.Read(path);
        }
        catch (Exception e) when (e is WorldFileException or IOException or UnauthorizedAccessException)
        {
            ShowMainMenu("That world could not be opened: " + e.Message);
            return;
        }
        Settings.LastWorld = Path.Combine(world.Folder, "world.json");
        Settings.Save();
        Show(new MapScreen(this, world));
    }

    public void Quit() => GetTree().Quit();
}
