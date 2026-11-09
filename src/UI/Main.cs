// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Colony;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.UI;

/// <summary>
/// The game's first node (src/UI/Main.tscn): it shows the main menu, and swaps it for the map when a
/// world is opened, or for the colony when one is founded or loaded. Everything on screen is a child of this node.
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
        // `-- --save` saves the game into the saves folder, as the in-game menu's Save does (a colony: after
        // --found, into the colonies folder).
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
        // `-- --search <text>` types that in the book's search box (with --book, for testing).
        int sought = Array.IndexOf(arguments, "--search");
        if (sought >= 0 && sought + 1 < arguments.Length && screen is MapScreen searching)
            searching.CallDeferred(MapScreen.MethodName.SearchBook, arguments[sought + 1]);
        // `-- --site <site>` shows that founding site's offer on the world map (with --world, for testing).
        int shown = Array.IndexOf(arguments, "--site");
        if (shown >= 0 && shown + 1 < arguments.Length && screen is MapScreen offering && int.TryParse(arguments[shown + 1], out int offered))
            offering.CallDeferred(MapScreen.MethodName.OpenSiteNumber, offered);
        // `-- --found <site>` founds a colony at that founding site of the opened world (with --world).
        int found = Array.IndexOf(arguments, "--found");
        if (found >= 0 && found + 1 < arguments.Length && screen is MapScreen founding && int.TryParse(arguments[found + 1], out int site))
            FoundColony(founding.World, site);
        if (Array.IndexOf(arguments, "--save") >= 0 && screen is ColonyScreen savingColony)
            GD.Print(savingColony.Save());
        // `-- --colony <save>` loads a saved colony.
        int colony = Array.IndexOf(arguments, "--colony");
        if (colony >= 0 && colony + 1 < arguments.Length)
            LoadColony(arguments[colony + 1]);
        // `-- --inspect <x>,<y>` inspects that tile of the colony (with --found or --colony).
        int inspect = Array.IndexOf(arguments, "--inspect");
        if (inspect >= 0 && inspect + 1 < arguments.Length && screen is ColonyScreen looking
            && arguments[inspect + 1].Split(',') is [var ix, var iy] && int.TryParse(ix, out int tx) && int.TryParse(iy, out int ty))
            looking.CallDeferred(ColonyScreen.MethodName.PickTile, tx, ty);
        // For trying the colony screen without a mouse: `-- --build <wall|floor|small_house|stockpile|...>` takes
        // that tool, `-- --place x0,y0,x1,y1` places from one tile to the other (several may follow each other),
        // `-- --preview x0,y0,x1,y1` only shows it, `-- --do <key>,x,y` does the command of that key on the picked tile (a command needing a tile: on (x, y)),
        // `-- --run <minutes>` lets the colony work, `-- --waiting` lists
        // what waits.
        if (screen is ColonyScreen building)
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                string value = arguments[i + 1];
                switch (arguments[i])
                {
                    case "--build":
                        building.PickTool(value);
                        break;
                    case "--place" or "--preview" when value.Split(',').Select(int.Parse).ToArray() is [var x0, var y0, var x1, var y1]:
                        building.PlaceAt(x0, y0, x1, y1, arguments[i] == "--place");
                        break;
                    case "--inspect" when value.Split(',').Select(int.Parse).ToArray() is [var px, var py]:
                        building.PickTile(px, py);
                        break;
                    case "--panel":
                        building.OpenBuildPanel();
                        break;
                    case "--do" when value.Split(',') is [var key, var dx, var dy]:
                        building.DoAt(key[0], int.Parse(dx), int.Parse(dy));
                        break;
                    case "--run" when int.TryParse(value, out int minutes):
                        building.RunFor(minutes);
                        break;
                }
            }
        if (Array.IndexOf(arguments, "--waiting") >= 0 && screen is ColonyScreen listing)
            listing.ShowWaiting();
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

    /// <summary>The colony's content (content/*.json in the game's folder), checked; null, with the main menu
    /// showing why, if it has problems.</summary>
    Content? LoadContent()
    {
        try
        {
            return Content.Load(ProjectSettings.GlobalizePath("res://content"));
        }
        catch (Exception e) when (e is ContentException or IOException or UnauthorizedAccessException)
        {
            ShowMainMenu("The colony's content could not be read: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Founds a colony at founding site `site` of `world` (step 18a): the founders arrive on its local map at 08:00
    /// on the first day of Year 1, with the colony's own seed (drawn at random, or `-- --seed`). Back to the main
    /// menu with the reason if the map cannot be read.
    /// </summary>
    public void FoundColony(ExportedWorld world, int site)
    {
        if (LoadContent() is not Content content)
            return;
        Colony.Colony colony;
        try
        {
            var map = world.LocalMap(site);
            long seed = SeedFromArguments() ?? Game.NewSeed();
            colony = Founding.Found(seed, new Moment(Calendar.TickOf(1, 1, 1, 8, 0)), content, map).Colony;
        }
        catch (Exception e) when (e is WorldFileException or ArgumentException or IOException)
        {
            ShowMainMenu("The colony could not be founded: " + e.Message);
            return;
        }
        Play(colony);
    }

    /// <summary>
    /// Loads a saved colony. A save changed outside the game is only loaded if the player says so (library Review
    /// 35): they are asked first. Back to the main menu with the reason if it cannot be loaded.
    /// </summary>
    public void LoadColony(string path, bool acceptChanged = false)
    {
        if (LoadContent() is not Content content)
            return;
        Opened opened;
        try
        {
            opened = ColonySave.Open(path, content, acceptChanged);
        }
        catch (ColonySaveChangedException e)
        {
            var ask = new ConfirmationDialog
            {
                Title = "This save was changed",
                DialogText = e.Message + "\n\nLoad it anyway?",
                OkButtonText = "Load it",
                CancelButtonText = "Do not load",
                ProcessMode = ProcessModeEnum.Always,
            };
            ask.Confirmed += () => LoadColony(path, true);
            AddChild(ask);
            ask.PopupCentered();
            return;
        }
        catch (ColonySaveException e)
        {
            ShowMainMenu("That colony could not be loaded: " + e.Message);
            return;
        }
        Play(opened.Colony, opened.Warnings);
    }

    // The colony screen for `colony`, its autosaves in a folder of their own, any warnings shown on its bar.
    void Play(Colony.Colony colony, IReadOnlyList<string>? warnings = null)
    {
        GetTree().Paused = false;
        string name = "colony-" + colony.Seed;
        var session = new Session(colony, Saves.AutosavesOf(name));
        var shown = new ColonyScreen(this, session, name);
        Show(shown);
        foreach (var warning in warnings ?? Array.Empty<string>())
            shown.Tell(warning);
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
