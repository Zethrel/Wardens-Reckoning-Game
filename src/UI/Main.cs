// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Sim;
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
        // `-- --run <years>` runs that many years on the opened world.
        int run = Array.IndexOf(arguments, "--run");
        if (run >= 0 && run + 1 < arguments.Length && screen is MapScreen played && int.TryParse(arguments[run + 1], out int count))
            played.RunYears(count);
        // `-- --save` saves the game into the saves folder, as the in-game menu's Save does.
        if (Array.IndexOf(arguments, "--save") >= 0 && screen is MapScreen saving)
            GD.Print(saving.SaveNow());
        // `-- --load <save>` loads a saved game.
        int load = Array.IndexOf(arguments, "--load");
        if (load >= 0 && load + 1 < arguments.Length)
            LoadGame(arguments[load + 1]);
        // `-- --year <year>` shows that year of the opened world.
        int pick = Array.IndexOf(arguments, "--year");
        if (pick >= 0 && pick + 1 < arguments.Length && screen is MapScreen map && int.TryParse(arguments[pick + 1], out int year))
            map.CallDeferred(MapScreen.MethodName.PickYear, year);
        // `-- --book <faction>` opens the book at that faction's history (-1: the whole book).
        int opened = Array.IndexOf(arguments, "--book");
        if (opened >= 0 && opened + 1 < arguments.Length && screen is MapScreen reading && int.TryParse(arguments[opened + 1], out int which))
            reading.CallDeferred(MapScreen.MethodName.OpenBook, which);
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
        // A world exported with "continue" starts a new game on it; an older one can only be looked at.
        Game? game = null;
        string note = "";
        try
        {
            game = new Game(world, SeedFromArguments() ?? Game.NewSeed());
        }
        catch (WorldFileException e)
        {
            note = "Only to look at: " + e.Message;
        }
        Show(new MapScreen(this, world, game, note));
    }

    /// <summary>Loads a saved game; back to the main menu with the reason if it cannot.</summary>
    public void LoadGame(string path)
    {
        Game game;
        try
        {
            game = SaveFile.Read(path);
        }
        catch (Exception e) when (e is WorldFileException or IOException or UnauthorizedAccessException)
        {
            ShowMainMenu("That game could not be loaded: " + e.Message);
            return;
        }
        GetTree().Paused = false;
        Show(new MapScreen(this, game.World, game));
    }

    // `-- --seed <number>` starts the game with that seed (for testing).
    static long? SeedFromArguments()
    {
        var arguments = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(arguments, "--seed");
        return at >= 0 && at + 1 < arguments.Length && long.TryParse(arguments[at + 1], out long seed) ? seed : null;
    }

    public void Quit() => GetTree().Quit();
}
