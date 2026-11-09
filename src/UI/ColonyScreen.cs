// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Colony;

namespace WardensReckoning.UI;

/// <summary>
/// The screen while a colony is played (step 18a, library Review 38): the local map in the middle (ColonyView, with
/// the world map's camera), the inspector on the right, the bar along the bottom (pause and the speeds, the date
/// and time, the colonists), and the in-game menu (Esc).
///
/// Time runs through the `Session` (src/Colony/Session.cs): each frame it is told how much real time passed, and it
/// runs the ticks now due at the chosen speed and makes the morning autosave. The screen never runs the colony
/// itself, so how fast the screen draws cannot change what happens (FP-01).
///
/// Keys: Space pauses or plays; 1, 2 and 3 choose the speed; Tab picks the next colonist; Esc opens the menu. The
/// mouse: a left click inspects the colonist or the tile under it; the wheel zooms; the right button drags.
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
    readonly Button[] speeds = new Button[3];
    VBoxContainer? inspector;
    Label? inspectorTitle;
    double refresh;
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

    public override void _Ready()
    {
        // Laid out as the world map's screen is: the map in a SubViewport in the middle, panels around it.
        var hud = new CanvasLayer();
        AddChild(hud);
        var screen = new VBoxContainer();
        screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddThemeConstantOverride("separation", 0);
        hud.AddChild(screen);
        var middle = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 0);
        screen.AddChild(middle);

        var frame = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                                               SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        middle.AddChild(frame);
        var port = new SubViewport { HandleInputLocally = true };
        frame.AddChild(port);
        view = new ColonyView(() => Colony);
        port.AddChild(view);
        camera = new MapCamera(Colony.Ground!.Width);
        port.AddChild(camera);
        // Start close, over the colonists (a whole local map is 256 metres; a colonist would be a speck).
        if (Colony.Work!.Workers.FirstOrDefault() is Worker first)
            camera.Look(new Vector2((float)first.X, (float)first.Y), 32);
        view.Clicked += OnMapClick;

        // The inspector, on the right.
        var side = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        middle.AddChild(side);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(column);
        inspectorTitle = new Label { Text = "Click a colonist or a tile", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        inspectorTitle.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(inspectorTitle);
        inspector = new VBoxContainer();
        column.AddChild(inspector);

        // The bar along the bottom.
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 48) };
        screen.AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        bar.AddChild(row);
        pause = Bar(row, "Pause", "Pause or play (Space)", TogglePause);
        for (int i = 0; i < 3; i++)
        {
            var speed = (Speed)(i + 1);
            speeds[i] = Bar(row, (i + 1) + "x", "Speed " + (i + 1) + " (key " + (i + 1) + ")", () => SetSpeed(speed));
            speeds[i].ToggleMode = true;
        }
        clock = new Label { CustomMinimumSize = new Vector2(250, 0), VerticalAlignment = VerticalAlignment.Center };
        row.AddChild(clock);
        foreach (var w in Colony.Work!.Workers)
        {
            var id = w.Id;
            Bar(row, w.Name, "Inspect " + w.Name + " (Tab: the next colonist)", () => PickWorker(id, true));
        }
        said = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center,
                           ClipText = true };
        said.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.5f));
        row.AddChild(said);

        var menuLayer = new CanvasLayer { Layer = 10 };
        AddChild(menuLayer);
        menuLayer.AddChild(new PauseMenu(main, Save, LoadPanel.Colonies));

        ShowSpeed();
        if (pending.Count > 0)
        {
            said.Text = pending[^1];
            said.TooltipText = string.Join("\n", pending);
        }
    }

    // A button on the bar. Buttons do not take the keyboard's focus there, so Space and the arrows stay the map's.
    static Button Bar(HBoxContainer row, string text, string tooltip, Action pressed)
    {
        var button = new Button { Text = text, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += pressed;
        row.AddChild(button);
        return button;
    }

    public override void _Process(double delta)
    {
        session.Frame(delta);
        clock!.Text = Colony.Now.ToString();
        // Anything the session has to tell (a failed autosave, a warning about a loaded save) is shown on the bar.
        if (session.Reports.Count > reportsShown)
        {
            said!.Text = session.Reports[^1];
            said.TooltipText = string.Join("\n", session.Reports);
            reportsShown = session.Reports.Count;
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

    Speed lastSpeed = Speed.Normal;

    /// <summary>Sets the speed (Paused, Normal, Double, Triple), and shows it on the bar.</summary>
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
        for (int i = 0; i < 3; i++)
            speeds[i].ButtonPressed = !paused && (int)session.Speed == i + 1;
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
        view!.SelectedTile = (x, y);
        view.SelectedWorker = null;
        ShowInspection();
    }

    void ShowInspection()
    {
        Inspection? shown = null;
        if (view!.SelectedWorker is ColonistId id && Colony.Work!.Workers.Any(w => w.Id == id))
            shown = Inspect.Worker(Colony, id);
        else if (view.SelectedTile is var (x, y))
            shown = Inspect.Tile(Colony, x, y);
        foreach (var child in inspector!.GetChildren())
            child.QueueFree();
        if (shown == null)
            return;
        inspectorTitle!.Text = shown.Title;
        foreach (var line in shown.Lines)
        {
            var label = new Label { Text = line.Label + ": " + line.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                                    CustomMinimumSize = new Vector2(290, 0) };
            label.AddThemeFontSizeOverride("font_size", 14);
            inspector.AddChild(label);
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.Keycode)
        {
            case Key.Space:
                TogglePause();
                break;
            case Key.Key1:
                SetSpeed(Speed.Normal);
                break;
            case Key.Key2:
                SetSpeed(Speed.Double);
                break;
            case Key.Key3:
                SetSpeed(Speed.Triple);
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
