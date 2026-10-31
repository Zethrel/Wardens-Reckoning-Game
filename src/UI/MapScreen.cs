// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.World;

namespace WardensReckoning.UI;

/// <summary>
/// The screen while a world is open: the map with its camera, a bar at the bottom to pick the year,
/// the factions alive that year on the right, and the in-game menu (Esc).
/// </summary>
public partial class MapScreen : Node
{
    readonly Main main;
    readonly ExportedWorld world;
    MapView? map;
    HSlider? slider;
    Label? yearLabel;
    VBoxContainer? legend;
    int wanted = -1;

    public MapScreen() : this(null!, null!) { }

    public MapScreen(Main main, ExportedWorld world)
    {
        this.main = main;
        this.world = world;
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
        yearLabel = new Label { CustomMinimumSize = new Vector2(110, 0), VerticalAlignment = VerticalAlignment.Center };
        row.AddChild(yearLabel);
        slider = new HSlider { MinValue = 0, MaxValue = world.Years, Step = 1, Value = world.Years,
                               SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                               SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
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
        var menu = new PauseMenu(main);
        menu.SettingsChanged += () => map.Redraw();
        menuLayer.AddChild(menu);

        ShowLegend();
    }

    /// <summary>Moves the slider to `year` (the map follows on the next frame).</summary>
    public void PickYear(int year) => slider!.Value = year;

    string Title() => world.HistorySeed is int history ? "Seed " + world.Seed + ", history " + history : "Seed " + world.Seed;

    public override void _Process(double delta)
    {
        if (wanted >= 0 && wanted != map!.Year)
        {
            map.ShowYear(wanted);
            ShowLegend();
        }
        wanted = -1;
    }

    // The factions alive in the shown year, the most people first, each with its colour and path.
    void ShowLegend()
    {
        yearLabel!.Text = "Year " + map!.Year + " of " + world.Years;
        foreach (var child in legend!.GetChildren())
            child.QueueFree();
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
