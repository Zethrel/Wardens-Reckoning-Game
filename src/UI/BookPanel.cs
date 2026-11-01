// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.World;

namespace WardensReckoning.UI;

/// <summary>
/// The book, to the left of the map: everything that happened up to the year shown, the newest first. Opened for
/// one faction (by clicking it in the list on the right, or one of its towns on the map), it shows that faction's
/// card and only its own entries. "Only the chronicle" leaves the chronicle's sentences, the notable events.
/// </summary>
public partial class BookPanel : PanelContainer
{
    // The most entries shown at once: thousands of lines make the panel slow to fill, and no one reads that far.
    const int MostShown = 400;

    readonly ExportedWorld world;
    readonly Func<IReadOnlyList<BookEntry>> entries;
    Label? title;
    VBoxContainer? card;
    RichTextLabel? text;
    CheckBox? chronicleOnly;
    int year;

    /// <summary>The faction whose history is shown, or null for the whole book.</summary>
    public int? Faction { get; private set; }

    public BookPanel() : this(null!, null!) { }

    /// <summary>`entries` gives the book as it stands (the game adds to it as the years run).</summary>
    public BookPanel(ExportedWorld world, Func<IReadOnlyList<BookEntry>> entries)
    {
        this.world = world;
        this.entries = entries;
    }

    public override void _Ready()
    {
        // A column of the screen's layout (MapScreen): 420 pixels wide, as tall as the room; hidden until opened.
        CustomMinimumSize = new Vector2(420, 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        Visible = false;
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 10);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var top = new HBoxContainer();
        title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        title.AddThemeFontSizeOverride("font_size", 20);
        top.AddChild(title);
        var all = new Button { Text = "Whole book", FocusMode = FocusModeEnum.None,
                               TooltipText = "Every faction's entries" };
        all.Pressed += () => Open(null);
        top.AddChild(all);
        var close = new Button { Text = "X", FocusMode = FocusModeEnum.None, TooltipText = "Close (Esc or B)" };
        close.Pressed += Close;
        top.AddChild(close);
        column.AddChild(top);

        card = new VBoxContainer();
        column.AddChild(card);

        chronicleOnly = new CheckBox { Text = "Only the chronicle", FocusMode = FocusModeEnum.None,
                                       TooltipText = "Only the notable events the chronicle tells" };
        chronicleOnly.Toggled += _ => Refresh();
        column.AddChild(chronicleOnly);

        text = new RichTextLabel { BbcodeEnabled = false, ScrollActive = true, SelectionEnabled = true,
                                   SizeFlagsVertical = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        text.AddThemeFontSizeOverride("normal_font_size", 13);
        column.AddChild(text);
    }

    /// <summary>Opens the book: for `faction`'s history, or the whole book if null.</summary>
    public void Open(int? faction)
    {
        Faction = faction;
        Visible = true;
        Refresh();
    }

    public void Close() => Visible = false;

    public void Toggle()
    {
        if (Visible)
            Close();
        else
            Open(Faction);
    }

    /// <summary>Shows the book as it stood in `year` (the year on the map).</summary>
    public void ShowYear(int shown)
    {
        year = shown;
        if (Visible)
            Refresh();
    }

    void Refresh()
    {
        if (text == null)
            return;
        foreach (var child in card!.GetChildren())
            child.QueueFree();
        if (Faction is int f)
            ShowCard(Book.Card(world, f, year));
        else
        {
            title!.Text = "The book, to year " + year;
            title.RemoveThemeColorOverride("font_color");
        }

        var shown = Book.Read(entries(), year, Faction, chronicleOnly!.ButtonPressed);
        var lines = new System.Text.StringBuilder();
        foreach (var entry in shown.Take(MostShown))
            lines.Append(entry.Year).Append("   ").Append(entry.Shown).Append('\n');
        if (shown.Count > MostShown)
            lines.Append("\n(the newest ").Append(MostShown).Append(" of ").Append(shown.Count)
                 .Append(" entries: move the year back to read further)\n");
        if (shown.Count == 0)
            lines.Append("Nothing written down yet.");
        text.Text = lines.ToString();
        text.ScrollToLine(0);
    }

    void ShowCard(FactionCard faction)
    {
        title!.Text = faction.Name;
        title.AddThemeColorOverride("font_color", Color.Color8(faction.Red, faction.Green, faction.Blue).Lightened(0.3f));
        void Line(string label, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            var row = new HBoxContainer();
            var name = new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) };
            name.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            name.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(name);
            var said = new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                                   SizeFlagsHorizontal = SizeFlags.ExpandFill };
            said.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(said);
            card!.AddChild(row);
        }
        Line("Path", faction.Path);
        Line("Ruler", faction.Ruler);
        Line("Belief", faction.Belief);
        Line("Government", faction.Government);
        Line("Capital", faction.Capital);
        Line("Came to be", "year " + faction.From + (faction.Parent != null ? ", breaking from the " + faction.Parent : ""));
        Line("People", faction.People.ToString("N0") + (faction.People == 0 && faction.Peak > 0 ? " (fallen)" : "")
                       + "; most " + faction.Peak.ToString("N0") + " in year " + faction.PeakYear);
        card!.AddChild(new PeopleGraph(faction.PeopleByYear, Color.Color8(faction.Red, faction.Green, faction.Blue))
                       { CustomMinimumSize = new Vector2(0, 60) });
    }

    public override void _Input(InputEvent @event)
    {
        // Esc closes the book before it opens the in-game menu.
        if (Visible && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}

/// <summary>A small line of a faction's people over the years, for its card.</summary>
public partial class PeopleGraph : Control
{
    readonly IReadOnlyList<int> people;
    readonly Color colour;

    public PeopleGraph() : this(Array.Empty<int>(), Colors.White) { }

    public PeopleGraph(IReadOnlyList<int> people, Color colour)
    {
        this.people = people;
        this.colour = colour;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.25f));
        int most = people.Count == 0 ? 0 : people.Max();
        if (people.Count < 2 || most == 0)
            return;
        var points = new Vector2[people.Count];
        for (int i = 0; i < people.Count; i++)
            points[i] = new Vector2(Size.X * i / (people.Count - 1), Size.Y - 2 - (Size.Y - 4) * people[i] / most);
        DrawPolyline(points, colour.Lightened(0.2f), 1.5f, true);
    }
}
