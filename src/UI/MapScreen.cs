// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.UI;

/// <summary>
/// The screen while a world is open: the map with its camera, a bar at the bottom to run the years and pick
/// the year shown, the factions alive that year on the right, and the in-game menu (Esc).
/// With a game (a world exported with "continue"), the years run on: Next year runs one, Play runs them one
/// after another. Without one (an older export), the world can only be looked at.
/// </summary>
public partial class MapScreen : Node
{
    readonly Main main;
    readonly ExportedWorld world;
    readonly Game? game;
    readonly string note;
    MapView? map;
    HSlider? slider;
    Label? yearLabel;
    Button? play;
    Button? next;
    VBoxContainer? legend;
    int wanted = -1;
    bool playing;
    double untilNextYear;

    public MapScreen() : this(null!, null!, null, "") { }

    /// <summary>`game` is null when the world cannot be played on; `note` then says why.</summary>
    public MapScreen(Main main, ExportedWorld world, Game? game, string note = "")
    {
        this.main = main;
        this.world = world;
        this.game = game;
        this.note = note;
    }

    public override void _Ready()
    {
        map = new MapView(world);
        AddChild(map);
        AddChild(new MapCamera(world.Size));

        // The bar and the legend are on a CanvasLayer: drawn on the screen, not on the map, so they do not zoom.
        var hud = new CanvasLayer();
        AddChild(hud);

        var bar = new PanelContainer();
        bar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        bar.OffsetTop = -48;
        hud.AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        bar.AddChild(row);
        play = new Button { Text = "Play", CustomMinimumSize = new Vector2(80, 0), Disabled = game == null,
                            FocusMode = Control.FocusModeEnum.None,
                            TooltipText = game == null ? note : "Run the years on, one after another (Space)" };
        play.Pressed += TogglePlay;
        row.AddChild(play);
        next = new Button { Text = "Next year", Disabled = game == null, FocusMode = Control.FocusModeEnum.None,
                            TooltipText = game == null ? note : "Run one year (N)" };
        next.Pressed += () => RunYears(1);
        row.AddChild(next);
        yearLabel = new Label { CustomMinimumSize = new Vector2(130, 0), VerticalAlignment = VerticalAlignment.Center };
        row.AddChild(yearLabel);
        slider = new HSlider { MinValue = 0, MaxValue = world.Years, Step = 1, Value = world.Years,
                               SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                               SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                               FocusMode = Control.FocusModeEnum.None };   // so the arrow keys keep moving the map
        // Dragging gives many values a second; the map is redrawn once a frame, with the newest.
        slider.ValueChanged += value => wanted = (int)value;
        row.AddChild(slider);
        row.AddChild(new Label { Text = Title(), VerticalAlignment = VerticalAlignment.Center });

        var side = new PanelContainer();
        side.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        side.OffsetLeft = -300;
        side.OffsetBottom = -52;
        hud.AddChild(side);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(scroll);
        legend = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(legend);

        var menuLayer = new CanvasLayer { Layer = 10 };
        AddChild(menuLayer);
        var menu = new PauseMenu(main, game == null ? null : Save);
        menu.SettingsChanged += () => map.Redraw();
        menuLayer.AddChild(menu);

        ShowLegend();
    }

    /// <summary>Moves the slider to `year` (the map follows on the next frame).</summary>
    public void PickYear(int year) => slider!.Value = year;

    /// <summary>Runs `count` years on (for testing too: `-- --run <years>`).</summary>
    public void RunYears(int count)
    {
        if (game == null)
            return;
        // Following the present: when the newest year is shown, the new years are shown as they come.
        bool following = (int)slider!.Value == world.Years;
        for (int i = 0; i < count; i++)
            YearCore.RunYear(game);
        slider.MaxValue = world.Years;
        if (following)
            slider.Value = world.Years;
        ShowLegend();
    }

    void TogglePlay()
    {
        playing = !playing;
        play!.Text = playing ? "Pause" : "Play";
        untilNextYear = 0;
    }

    /// <summary>Saves the game into the saves folder; returns what to tell the player.</summary>
    public string SaveNow() => game == null ? "Nothing to save: this world can only be looked at." : Save();

    string Save()
    {
        string name = string.Format("world-{0}-h{1}-year-{2}.json", world.Seed, world.HistorySeed, game!.Year);
        string path = System.IO.Path.Combine(Saves.Folder, name);
        try
        {
            SaveFile.Write(path, game);
            return "Saved as " + name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "Could not save: " + e.Message;
        }
    }

    string Title() => world.HistorySeed is int history ? "Seed " + world.Seed + ", history " + history : "Seed " + world.Seed;

    public override void _Process(double delta)
    {
        if (playing)
        {
            untilNextYear -= delta;
            if (untilNextYear <= 0)
            {
                RunYears(1);
                untilNextYear = 1.0 / Math.Max(0.1, Settings.YearsPerSecond);
            }
        }
        if (wanted >= 0 && wanted != map!.Year)
        {
            map.ShowYear(wanted);
            ShowLegend();
        }
        wanted = -1;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (game == null || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode == Key.Space)
            TogglePlay();
        else if (key.Keycode == Key.N)
            RunYears(1);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    // The year shown, and the factions alive in it, the most people first, each with its colour and path.
    void ShowLegend()
    {
        string played = game != null && world.Years > world.ExportYears ? " (played from " + world.ExportYears + ")" : "";
        yearLabel!.Text = "Year " + map!.Year + " of " + world.Years + played;
        foreach (var child in legend!.GetChildren())
            child.QueueFree();
        if (note != "")
        {
            var why = new Label { Text = note, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            why.AddThemeFontSizeOverride("font_size", 12);
            why.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.5f));
            legend.AddChild(why);
        }
        foreach (var (faction, people) in MapYear.Alive(world, map.Year))
        {
            var info = world.Factions[faction];
            var line = new HBoxContainer();
            line.AddChild(new ColorRect { Color = Color.Color8(info.Red, info.Green, info.Blue),
                                          CustomMinimumSize = new Vector2(12, 12),
                                          SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            string path = (string?)world.Faction(faction, map.Year)?["path"] ?? "";
            var name = new Label { Text = info.Name + (path != "" ? " (" + path + ")" : ""),
                                   SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
            name.AddThemeFontSizeOverride("font_size", 13);
            line.AddChild(name);
            var count = new Label { Text = people.ToString("N0") };
            count.AddThemeFontSizeOverride("font_size", 13);
            line.AddChild(count);
            legend.AddChild(line);
        }
    }
}
