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
/// The export's founding sites (step 18a) are on the map as numbered diamonds and listed on the right; picking one
/// shows what it is like and offers to found a colony there.
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
    BookPanel? book;
    IReadOnlyList<BookEntry>? exportBook;   // the book of a world that can only be looked at (no game to add to it)
    double untilNextYear;
    Control? siteBox;
    CanvasLayer? hudLayer;

    /// <summary>The world shown.</summary>
    public ExportedWorld World => world;

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
        // The screen is laid out in rows and columns, so nothing covers the map: the book on the left (when
        // open), the map in the middle, the factions on the right, and the bar along the bottom. The map is drawn
        // in a SubViewport of its own: a picture inside the layout, with its own camera, that takes the room the
        // panels leave and grows when the book is closed.
        var hud = new CanvasLayer();
        AddChild(hud);
        hudLayer = hud;
        var screen = new VBoxContainer();
        screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddThemeConstantOverride("separation", 0);
        hud.AddChild(screen);
        var middle = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 0);
        screen.AddChild(middle);

        // The book, on the left: the whole book or one faction's history (BookPanel.cs).
        book = new BookPanel(world, () => game?.BookEntries ?? (exportBook ??= Book.FromExport(world)));
        middle.AddChild(book);

        var frame = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                                               SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        middle.AddChild(frame);
        var view = new SubViewport { HandleInputLocally = true };
        frame.AddChild(view);
        map = new MapView(world);
        map.TownClicked += town => { if (town.Faction is int f) OpenFaction(f); };
        map.SiteClicked += OpenSite;
        view.AddChild(map);
        view.AddChild(new MapCamera(world.Size));

        var side = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        middle.AddChild(side);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(scroll);
        legend = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(legend);

        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 48) };
        screen.AddChild(bar);
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
        var bookButton = new Button { Text = "Book", FocusMode = Control.FocusModeEnum.None,
                                      TooltipText = "Everything that happened (B); click a faction or a town for its own history" };
        bookButton.Pressed += () => { book.ShowYear(map.Year); book.Toggle(); };
        row.AddChild(bookButton);
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
            book?.ShowYear(map.Year);
        }
        wanted = -1;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode == Key.B)
        {
            book!.ShowYear(map!.Year);
            book.Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (game == null)
            return;
        if (key.Keycode == Key.Space)
            TogglePlay();
        else if (key.Keycode == Key.N)
            RunYears(1);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Opens the book: at `faction`'s history, or the whole book for -1 (for testing: `-- --book <faction>`).</summary>
    /// <summary>Types `typed` in the book's search box (for testing: `-- --search <text>`).</summary>
    public void SearchBook(string typed) => book?.SetSearch(typed);

    public void OpenBook(int faction)
    {
        if (faction < 0)
        {
            book!.ShowYear(map!.Year);
            book.Open(null);
        }
        else
            OpenFaction(faction);
    }

    /// <summary>Opens the book at `faction`'s history, in the year shown.</summary>
    public void OpenFaction(int faction)
    {
        book!.ShowYear(map!.Year);
        book.Open(faction);
    }

    // The founding sites, as buttons at the top of the list (so they can be picked with the keyboard too).
    void ShowSites()
    {
        var sites = world.FoundingSites;
        if (sites.Count == 0)
        {
            var none = new Label { Text = "No founding sites: export this world again with the newer generator to found a colony on it.",
                                   AutowrapMode = TextServer.AutowrapMode.WordSmart };
            none.AddThemeFontSizeOverride("font_size", 12);
            legend!.AddChild(none);
            return;
        }
        legend!.AddChild(Ui.Label("Founding sites", 15));
        foreach (var site in sites)
        {
            var chosen = site;
            var button = new Button { Text = SiteLine(site), Alignment = HorizontalAlignment.Left, ClipText = true,
                                      TooltipText = "Look at site " + site.Site + ", and found a colony there" };
            button.AddThemeFontSizeOverride("font_size", 13);
            button.Pressed += () => OpenSite(chosen);
            legend.AddChild(button);
        }
        legend.AddChild(new HSeparator());
    }

    // "3: monsoon forest, a brook".
    static string SiteLine(FoundingSite site) => site.Site + ": " + (site.Biome ?? "unknown land").Replace('_', ' ') + ", " + WaterWords(site);

    static string WaterWords(FoundingSite site) => site.Water switch
    {
        "river" => "a river",
        "stream" => "a brook",
        "lake" => "a lake",
        "sea" => "the sea (salt)",
        _ => site.FreshWater ? "fresh water" : "no water",
    };

    /// <summary>Shows founding site number `number` (for testing: `-- --site <number>`).</summary>
    public void OpenSiteNumber(int number)
    {
        if (world.FoundingSites.FirstOrDefault(s => s.Site == number) is FoundingSite site)
            OpenSite(site);
    }

    /// <summary>Shows founding site `site`: what it is like, and the offer to found a colony there.</summary>
    public void OpenSite(FoundingSite site)
    {
        siteBox?.QueueFree();
        var box = new Control();
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        box.MouseFilter = Control.MouseFilterEnum.Ignore;
        hudLayer!.AddChild(box);
        siteBox = box;
        var column = Ui.CentredColumn(box);
        column.AddChild(Ui.Label("Founding site " + site.Site, 24));
        string drink = site.FreshWater ? "The founders can drink from " + WaterWords(site) + "."
                                       : "No fresh water: the founders live on the 60 litres they bring until they build a well.";
        var about = Ui.Label((site.Biome ?? "Unknown land").Replace('_', ' ') + ", at " + site.X + ", " + site.Y + " on the world map. " + drink, 14);
        about.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        about.CustomMinimumSize = new Vector2(420, 0);
        column.AddChild(about);
        var found = Ui.Button("Found a colony here", () => main.FoundColony(world, site.Site));
        column.AddChild(found);
        column.AddChild(Ui.Button("Not here", CloseSite));
        found.GrabFocus();
    }

    void CloseSite()
    {
        siteBox?.QueueFree();
        siteBox = null;
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
        ShowSites();
        foreach (var (faction, people) in MapYear.Alive(world, map.Year))
        {
            var info = world.Factions[faction];
            // A row is clickable: it opens the book at that faction's history.
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop,
                                           MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                                           TooltipText = "Open the " + info.Name + "'s history" };
            int chosen = faction;
            line.GuiInput += input =>
            {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                    OpenFaction(chosen);
            };
            line.AddChild(new ColorRect { Color = Color.Color8(info.Red, info.Green, info.Blue),
                                          CustomMinimumSize = new Vector2(12, 12),
                                          SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                                          MouseFilter = Control.MouseFilterEnum.Pass });
            string path = (string?)world.Faction(faction, map.Year)?["path"] ?? "";
            var name = new Label { Text = info.Name + (path != "" ? " (" + path + ")" : ""),
                                   SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true,
                                   MouseFilter = Control.MouseFilterEnum.Pass };
            name.AddThemeFontSizeOverride("font_size", 13);
            line.AddChild(name);
            var count = new Label { Text = people.ToString("N0"), MouseFilter = Control.MouseFilterEnum.Pass };
            count.AddThemeFontSizeOverride("font_size", 13);
            line.AddChild(count);
            legend.AddChild(line);
        }
    }
}
