// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Colony;

namespace WardensReckoning.UI;

/// <summary>
/// The screen while a colony is played (step 18a, library Review 38): the local map filling the screen (ColonyView,
/// with the world map's camera), the inspector over its right side while something is picked (step 26a, UI plan
/// UI-01), the bar along the bottom (pause and the speeds, the date and time, the colonists), and the in-game menu
/// (Esc).
///
/// Time runs through the `Session` (src/Colony/Session.cs): each frame it is told how much real time passed, and it
/// runs the ticks now due at the chosen speed and makes the morning autosave. The screen never runs the colony
/// itself, so how fast the screen draws cannot change what happens (FP-01).
///
/// Keys: Space pauses or plays; 1, 2 and 3 choose the speed; Tab picks the next colonist; B opens the build panel
/// (ColonyScreen.Build.cs); J lists the buildings that wait, and why (FP-06); F3 shows the developer's trace beside
/// the player's words; P, X and R do what the picked tile offers (pause, cancel or take down, remove a stockpile);
/// I hides or shows the inspector, keeping what is picked; M shows more of it, or less (step 26a); Esc stops
/// building, or else lets go of what is picked, or else opens the menu. The mouse: a left click inspects the colonist or the tile under it; the
/// wheel zooms; the right button drags.
/// </summary>
public partial class ColonyScreen : Node
{
    readonly Main main;
    readonly Session session;
    readonly string name;
    ColonyView? view;
    MapCamera? camera;
    Label? clock;
    Label? said;
    Button? pause;
    readonly Button[] speeds = new Button[Pacer.Speeds.Length];
    VBoxContainer? inspector;
    VBoxContainer? commandBox;
    // The inspector panel (step 26a): over the map's right side, shown while something is picked and not hidden;
    // compact unless `expanded`. Hidden with something picked, a small tab at the right edge brings it back.
    const int InspectorWidth = 320;
    PanelContainer? side;
    Button? reopen;
    Button? more;
    bool hidden;
    bool expanded;
    string shownCommands = "";
    Label? inspectorTitle;
    CanvasLayer? hudLayer;
    PanelContainer? bar;
    bool trace;
    bool listWaiting;
    double refresh;
    int newsShown = -1;      // -1: not yet looked (a loaded colony's old news is not shown again as new)
    int reportsShown;

    public ColonyScreen() : this(null!, null!, "") { }

    /// <summary>`name` names the colony in its saves ("colony-1234", the seed it was founded with).</summary>
    public ColonyScreen(Main main, Session session, string name)
    {
        this.main = main;
        this.session = session;
        this.name = name;
    }

    Colony.Colony Colony => session.Colony;

    public override void _ExitTree()
    {
        if (view != null)
            session.BeforeTick -= view.Remember;
    }

    public override void _Ready()
    {
        // Laid out as the world map's screen is: the map in a SubViewport in the middle, panels around it.
        var hud = new CanvasLayer();
        AddChild(hud);
        hudLayer = hud;
        var screen = new VBoxContainer();
        screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddThemeConstantOverride("separation", 0);
        hud.AddChild(screen);
        // The middle: the map fills it; the inspector lies over its right side (step 26a), so opening and closing it
        // never moves the map.
        var middle = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
        screen.AddChild(middle);

        var frame = new SubViewportContainer { Stretch = true };
        frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        middle.AddChild(frame);
        var port = new SubViewport { HandleInputLocally = true };
        frame.AddChild(port);
        view = new ColonyView(() => Colony);
        port.AddChild(view);
        session.BeforeTick += view.Remember;
        camera = new MapCamera(Colony.Ground!.Width);
        port.AddChild(camera);
        // Start close, over the colonists (a whole local map is 256 metres; a colonist would be a speck).
        if (Colony.Work!.Workers.FirstOrDefault() is Worker first)
            camera.Look(new Vector2((float)first.X, (float)first.Y), 32);
        view.Pressed += OnPressed;
        view.Released += OnReleased;
        view.Moved += OnMoved;

        // The inspector (step 26a, UI plan UI-01): a panel over the map's right side, 320 wide, from the top down to
        // the bar; hidden until something is picked. Its height follows what it shows, up to the map's, scrolling
        // beyond that.
        side = new PanelContainer { Visible = false };
        side.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
        side.OffsetLeft = -InspectorWidth;
        middle.AddChild(side);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(column);
        // Its head: the title, More/Less, and × to hide it.
        var head = new HBoxContainer();
        column.AddChild(head);
        inspectorTitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inspectorTitle.AddThemeFontSizeOverride("font_size", 18);
        head.AddChild(inspectorTitle);
        more = new Button { Text = "More", TooltipText = "Show everything about it (M)", FocusMode = Control.FocusModeEnum.None };
        more.Pressed += ToggleMore;
        head.AddChild(more);
        var close = new Button { Text = "×", TooltipText = "Hide the inspector; what is picked stays picked (I)", FocusMode = Control.FocusModeEnum.None };
        close.Pressed += ToggleHidden;
        head.AddChild(close);
        // The tab that brings a hidden inspector back, at the map's right edge.
        reopen = new Button { Text = "◂ Inspector", TooltipText = "Show the inspector again (I)", Visible = false,
                              FocusMode = Control.FocusModeEnum.None };
        reopen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        reopen.GrowHorizontal = Control.GrowDirection.Begin;
        reopen.Pressed += ToggleHidden;
        middle.AddChild(reopen);
        inspector = new VBoxContainer();
        column.AddChild(inspector);
        // The commands of the picked tile (pause, cancel, take down...): made again only when they change, so a
        // button is not replaced under the mouse while it is being pressed.
        commandBox = new VBoxContainer();
        column.AddChild(commandBox);

        // The bar along the bottom.
        // The bar wraps onto a second line when it does not fit (a large interface size, a narrow window).
        bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 48) };
        screen.AddChild(bar);
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        row.AddThemeConstantOverride("v_separation", 4);
        bar.AddChild(row);
        pause = Bar(row, "Pause", "Pause or play (Space)", TogglePause);
        // The speeds (library Review 41): at 1x a day lasts an hour.
        for (int i = 0; i < Pacer.Speeds.Length; i++)
        {
            var speed = Pacer.Speeds[i];
            speeds[i] = Bar(row, (int)speed + "x", SpeedText(speed) + " (key " + (i + 1) + ")", () => SetSpeed(speed));
            speeds[i].ToggleMode = true;
        }
        clock = new Label { CustomMinimumSize = new Vector2(250, 0), VerticalAlignment = VerticalAlignment.Center,
                            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(clock);
        Bar(row, "Build", "Build walls, floors, the house, stockpiles, gathering places (B)", ToggleBuildPanel);
        Bar(row, "Waiting", "What waits, and why (J)", ShowWaiting);
        foreach (var w in Colony.Work!.Workers)
        {
            var id = w.Id;
            Bar(row, w.Name, "Inspect " + w.Name + " (Tab: the next colonist)", () => PickWorker(id, true));
        }
        said = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center,
                           ClipText = true, CustomMinimumSize = new Vector2(240, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        said.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.5f));
        row.AddChild(said);

        var menuLayer = new CanvasLayer { Layer = 10 };
        AddChild(menuLayer);
        menuLayer.AddChild(new PauseMenu(main, Save, LoadPanel.Colonies));

        ShowSpeed();
        newsShown = Colony.Work!.Told.Count;
        if (pending.Count > 0)
        {
            said.Text = pending[^1];
            said.TooltipText = string.Join("\n", pending);
        }
    }

    // A button on the bar. Buttons do not take the keyboard's focus there, so Space and the arrows stay the map's.
    static Button Bar(Container row, string text, string tooltip, Action pressed)
    {
        var button = new Button { Text = text, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += pressed;
        row.AddChild(button);
        return button;
    }

    public override void _Process(double delta)
    {
        session.Frame(delta);
        view!.Due = session.Speed == Speed.Paused ? 1 : session.Pacer.Due;
        FitBuildPanel();
        clock!.Text = Colony.Now.WithSeconds;
        // Anything the session has to tell (a failed autosave, a warning about a loaded save) is shown on the bar.
        if (session.Reports.Count > reportsShown)
        {
            said!.Text = session.Reports[^1];
            said.TooltipText = string.Join("\n", session.Reports);
            reportsShown = session.Reports.Count;
        }
        // News from the colony (step 19b: a sign seen at the water, someone ill): the newest on the bar, the last ten
        // in its tooltip.
        var told = Colony.Work!.Told;
        if (told.Count > newsShown)
        {
            said!.Text = told[^1].Text;
            said.TooltipText = string.Join("\n", told.TakeLast(10).Select(n => new Moment(n.Tick) + ": " + n.Text));
            newsShown = told.Count;
        }
        // The inspector is written again four times a second, so what it shows follows the colony.
        refresh -= delta;
        if (refresh <= 0)
        {
            refresh = 0.25;
            ShowInspection();
        }
    }

    void TogglePause() => SetSpeed(session.Speed == Speed.Paused ? lastSpeed : Speed.Paused);

    Speed lastSpeed = Speed.X1;

    /// <summary>What a speed means, in words: "1x: 24 game seconds a real second (a day in 60 minutes)" (library
    /// Review 41: at 1x a real second runs `Calendar.TicksPerRealSecond` ticks).</summary>
    static string SpeedText(Speed speed)
    {
        int perSecond = Calendar.TicksPerRealSecond * (int)speed;
        double dayMinutes = Calendar.TicksPerDay / (double)perSecond / 60;
        string day = dayMinutes >= 120 ? (dayMinutes / 60).ToString("0.#") + " hours" : dayMinutes.ToString("0.#") + " minutes";
        return (int)speed + "x: " + perSecond + " game seconds a real second (a day in " + day + ")";
    }

    /// <summary>Sets the speed (Paused or one of `Pacer.Speeds`), and shows it on the bar.</summary>
    public void SetSpeed(Speed speed)
    {
        if (speed != Speed.Paused)
            lastSpeed = speed;
        session.Speed = speed;
        ShowSpeed();
    }

    // The pause button says what pressing it does; the speed in use is pressed in, and named in words for those
    // who do not see the pressed look (FP-11: not by its look alone).
    void ShowSpeed()
    {
        bool paused = session.Speed == Speed.Paused;
        pause!.Text = paused ? "Play" : "Pause";
        for (int i = 0; i < speeds.Length; i++)
            speeds[i].ButtonPressed = !paused && session.Speed == Pacer.Speeds[i];
        pause.TooltipText = paused ? "Paused: play at " + (int)lastSpeed + "x (Space)" : "Playing at " + (int)session.Speed + "x: pause (Space)";
    }

    // ---- Inspecting ------------------------------------------------------------------------------------------------

    void OnMapClick(Vector2 point)
    {
        if (Inspect.WorkerAt(Colony, point.X, point.Y) is Worker w)
            PickWorker(w.Id, false);
        else
            PickTile((int)Math.Floor(point.X), (int)Math.Floor(point.Y));
    }

    void PickWorker(ColonistId id, bool centre)
    {
        listWaiting = false;
        hidden = false;                  // picking something shows it (step 26a)
        view!.SelectedWorker = id;
        view.SelectedTile = null;
        if (centre && Colony.Work!.Workers.FirstOrDefault(w => w.Id == id) is Worker w)
            camera!.Position = new Vector2((float)w.X, (float)w.Y);
        ShowInspection();
    }

    /// <summary>Inspects tile (x, y), if it is on the map (for testing too: `-- --inspect x,y`).</summary>
    public void PickTile(int x, int y)
    {
        if (!Colony.Ground!.Inside(x, y))
            return;
        listWaiting = false;
        hidden = false;
        view!.SelectedTile = (x, y);
        view.SelectedWorker = null;
        ShowInspection();
    }

    /// <summary>Lists every building that waits, with why, in the inspector (J); a button for each picks its tile
    /// (for testing too: `-- --waiting`).</summary>
    public void ShowWaiting()
    {
        listWaiting = true;
        hidden = false;
        view!.SelectedTile = null;
        view.SelectedWorker = null;
        ShowInspection();
    }

    // I, ×, the tab: hide the inspector, or show it again; what is picked stays picked (step 26a).
    void ToggleHidden()
    {
        hidden = !hidden;
        ShowInspection();
    }

    // M, More/Less: all the lines, or the brief ones (step 26a).
    void ToggleMore()
    {
        expanded = !expanded;
        ShowInspection();
    }

    // Esc with something picked (step 26a): nothing is picked any more, and the inspector goes.
    void LetGo()
    {
        listWaiting = false;
        view!.SelectedTile = null;
        view.SelectedWorker = null;
        ShowInspection();
    }

    bool Picked => view!.SelectedWorker != null || view.SelectedTile != null || listWaiting;

    void ShowInspection()
    {
        Inspection? shown = null;
        var commands = (IReadOnlyList<Command>)Array.Empty<Command>();
        if (view!.SelectedWorker is ColonistId id && Colony.Work!.Workers.Any(w => w.Id == id))
            shown = Inspect.Worker(Colony, id, trace);
        else if (view.SelectedTile is var (x, y))
        {
            shown = Inspect.Tile(Colony, x, y, trace);
            commands = Inspect.Commands(Colony, x, y);
        }
        else if (listWaiting)
            shown = WaitingList();
        foreach (var child in inspector!.GetChildren())
            child.QueueFree();
        ShowCommands(commands);
        // Shown while something is picked and the player has not hidden it; the tab while it is hidden (step 26a).
        side!.Visible = shown != null && !hidden;
        reopen!.Visible = shown != null && hidden;
        if (shown == null)
            return;
        inspectorTitle!.Text = shown.Title;
        // Compact: the brief lines, if the inspection has them; More shows them all.
        more!.Visible = shown.Brief != null;
        more.Text = expanded ? "Less" : "More";
        more.TooltipText = expanded ? "Show only the main things (M)" : "Show everything about it (M)";
        foreach (var line in Inspect.Shown(shown, expanded))
        {
            var label = new Label { Text = line.Label + ": " + line.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                                    CustomMinimumSize = new Vector2(InspectorWidth - 30, 0) };
            label.AddThemeFontSizeOverride("font_size", 14);
            // The developer's trace looks different from the player's words (FP-06): grey, in its own line.
            if (line.Label == "Trace")
                label.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
            inspector.AddChild(label);
        }
    }

    // The waiting list as an inspection, and a button per entry under it (made again only when the list changes).
    Inspection WaitingList()
    {
        var waiting = Inspect.Waiting(Colony);
        var lines = waiting.Select(w => new Line(w.What + " (" + w.X + ", " + w.Y + ")", w.Why.Player + (trace ? " [" + w.Why.Trace + "]" : ""))).ToList();
        if (lines.Count == 0)
            lines.Add(new Line("Nothing waits", "every building under way is being worked on, or nothing is planned"));
        string signature = "waiting:" + string.Join("|", waiting.Select(w => w.X + "," + w.Y));
        if (signature != shownCommands)
        {
            ClearCommands(signature);
            foreach (var w in waiting)
            {
                var (wx, wy) = (w.X, w.Y);
                var button = new Button { Text = "Go to " + w.What + " (" + wx + ", " + wy + ")", Alignment = HorizontalAlignment.Left };
                button.Pressed += () =>
                {
                    camera!.Position = new Vector2(wx + 0.5f, wy + 0.5f);
                    PickTile(wx, wy);
                };
                commandBox!.AddChild(button);
            }
        }
        return new Inspection("Waiting (" + waiting.Count + ")", lines);
    }

    // The picked tile's commands as buttons, each with its key on it.
    void ShowCommands(IReadOnlyList<Command> commands)
    {
        if (listWaiting)
            return;                  // the waiting list keeps its own buttons
        string signature = string.Join("|", commands.Select(c => c.Label)) + "@" + view!.SelectedTile;
        if (signature == shownCommands)
            return;
        ClearCommands(signature);
        foreach (var command in commands)
        {
            var c = command;
            var button = new Button { Text = c.Label + " (" + c.Key + ")", TooltipText = c.Tooltip };
            button.Pressed += () => Do(c);
            commandBox!.AddChild(button);
        }
    }

    void ClearCommands(string signature)
    {
        shownCommands = signature;
        foreach (var child in commandBox!.GetChildren())
            child.QueueFree();
    }

    void Do(Command command)
    {
        if (command.Run == null)
        {
            Aim(command);            // it needs a tile: pick it first
            return;
        }
        command.Run();
        said!.Text = command.Label + ": done.";
        ShowInspection();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;
        // While building, the arrows, Enter, R, F and Esc are the build tool's (arrows may repeat when held).
        if (BuildKey(key.Keycode))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Echo)
            return;
        // The picked tile's commands by their keys (P, X, R).
        if (view!.SelectedTile is var (tx, ty) && !listWaiting
            && Inspect.Commands(Colony, tx, ty).FirstOrDefault(c => key.Keycode == (Key)c.Key) is Command command)
        {
            Do(command);
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (key.Keycode)
        {
            case Key.Escape when buildPanel != null:
                CloseBuildPanel();
                break;
            case Key.Escape when Picked:
                LetGo();
                break;
            case Key.I when Picked:
                ToggleHidden();
                break;
            case Key.M when Picked:
                ToggleMore();
                break;
            case Key.B:
                ToggleBuildPanel();
                break;
            case Key.J:
                ShowWaiting();
                break;
            case Key.F3:
                trace = !trace;
                said!.Text = trace ? "Developer's trace shown (F3 hides it)." : "Developer's trace hidden.";
                ShowInspection();
                break;
            case Key.Space:
                TogglePause();
                break;
            case >= Key.Key1 and <= Key.Key4:
                SetSpeed(Pacer.Speeds[(int)(key.Keycode - Key.Key1)]);
                break;
            case Key.Tab:
                NextWorker();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    // Tab: the colonist after the one picked (the first if none), with the camera on them.
    void NextWorker()
    {
        var workers = Colony.Work!.Workers.ToList();
        if (workers.Count == 0)
            return;
        int at = view!.SelectedWorker is ColonistId id ? workers.FindIndex(w => w.Id == id) : -1;
        PickWorker(workers[(at + 1) % workers.Count].Id, true);
    }

    /// <summary>Runs the colony `minutes` at once (for testing: `-- --run <minutes>`).</summary>
    public void RunFor(int minutes) => Colony.RunMinutes(minutes);

    /// <summary>Shows a message on the bar (a warning about the loaded save).</summary>
    public void Tell(string message)
    {
        if (said == null)
            pending.Add(message);
        else
            said.Text = message;
    }

    readonly List<string> pending = new();

    // ---- Saving ------------------------------------------------------------------------------------------------------

    /// <summary>Saves the colony into the colonies folder; returns what to tell the player.</summary>
    public string Save()
    {
        var now = Colony.Now;
        string file = string.Format("{0}-year-{1}-month-{2:00}-day-{3:00}-{4:00}{5:00}.json", name, now.Year, now.Month,
                                    now.DayOfMonth, now.Hour, now.Minute);
        Directory.CreateDirectory(Saves.ColonyFolder);
        return session.SaveAs(Path.Combine(Saves.ColonyFolder, file)) ? "Saved as " + file : "Could not save: " + session.Reports[^1];
    }
}
