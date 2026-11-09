// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Colony;

namespace WardensReckoning.UI;

/// <summary>
/// Building from the colony screen (step 18b, library Review 38): the second file of ColonyScreen (a "partial"
/// class may be written in several files; C# joins them into one).
///
/// Pick what to build in the build panel (B), then on the map:
///
/// - walls, doors and roads: drag a straight line (a click is one tile);
/// - floors and stockpiles: drag a box;
/// - the well and the small house: click where its top left corner goes; R turns the house, F mirrors it;
/// - take down: drag over what to take down.
///
/// While dragging, every tile shows whether it may go there (ColonyView.Ghost), and the bar says why not. Without a
/// mouse: the arrow keys move the cursor, Enter starts a line or a box and Enter again places it (a well or a house
/// at once). Esc stops building. What is placed only plans it: the colonists build it (Build.cs).
/// </summary>
public partial class ColonyScreen
{
    // How a tool places: along a line, as a box, one thing at the cursor, or a template.
    enum Shape { Line, Box, Single, Template }

    // The tool in hand: what it places ("wall", "small_house"; "stockpile"; "take down") and how.
    sealed record Tool(string What, string Name, Shape Shape);

    Tool? tool;
    Command? target;                 // a command waiting for its tile (moving a bedroll), with the tool "target"
    (int X, int Y)? anchor;          // where the drag (or the first Enter) began
    (int X, int Y) cursor;
    int turns;
    bool mirror;
    Control? buildPanel;

    const string Stockpile = "stockpile";
    const string TakeDown = "take down";
    const string Target = "target";

    /// <summary>Takes a command that needs a tile (Command.RunAt) in hand: the next tile picked is where it goes.</summary>
    void Aim(Command command)
    {
        target = command;            // first: Pick draws the preview, which asks the command
        Pick(new Tool(Target, command.Label, Shape.Single));
        Tell(command.Label + ": pick the tile (Enter or a click). Esc stops.");
        UpdateGhost();
    }

    /// <summary>Every tool the build panel offers, in its order: the pieces of the content, its templates, then
    /// stockpiles and taking down.</summary>
    IEnumerable<Tool> Tools()
    {
        var content = Colony.Content!;
        foreach (var (id, piece) in content.Pieces)
            yield return new Tool(id, piece.Name, piece.Width > 1 || piece.Height > 1 ? Shape.Single : id == "floor" ? Shape.Box : Shape.Line);
        foreach (var (id, template) in content.Templates)
            yield return new Tool(id, template.Name, Shape.Template);
        yield return new Tool(Stockpile, "Stockpile", Shape.Box);
        yield return new Tool(TakeDown, "Take down", Shape.Line);
    }

    /// <summary>Opens or closes the build panel (B): a column of buttons over the map's bottom left corner. The
    /// first has the keyboard's focus: the arrows move between them, Enter picks one.</summary>
    void ToggleBuildPanel()
    {
        if (buildPanel != null)
        {
            CloseBuildPanel();
            return;
        }
        var panel = new PanelContainer { Position = new Vector2(8, 120) };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        panel.AddChild(column);
        column.AddChild(Ui.Label("Build (B)", 16));
        Button? first = null;
        foreach (var t in Tools())
        {
            var picked = t;
            var button = new Button { Text = t.Name, Alignment = HorizontalAlignment.Left,
                                      TooltipText = Hint(t) };
            button.Pressed += () => Pick(picked);
            column.AddChild(button);
            first ??= button;
        }
        hudLayer!.AddChild(panel);
        buildPanel = panel;
        first?.GrabFocus();
    }

    void CloseBuildPanel()
    {
        buildPanel?.QueueFree();
        buildPanel = null;
    }

    // What a tool does, in a sentence (the button's tooltip, and the bar's words while it is in hand).
    static string Hint(Tool t) => t.Shape switch
    {
        Shape.Line when t.What == TakeDown => "Drag over what to take down. Esc stops.",
        Shape.Line => "Drag a straight line of " + t.Name.ToLowerInvariant() + " (a click is one). Esc stops.",
        Shape.Box when t.What == Stockpile => "Drag the stockpile's area. Esc stops.",
        Shape.Box => "Drag a box of " + t.Name.ToLowerInvariant() + ". Esc stops.",
        Shape.Template => "Click where its top left corner goes. R turns it, F mirrors it. Esc stops.",
        _ => "Click where its top left corner goes. Esc stops.",
    };

    /// <summary>Takes a tool in hand (for testing too: `-- --build wall`).</summary>
    public void PickTool(string what)
    {
        if (Tools().FirstOrDefault(t => t.What == what) is Tool t)
            Pick(t);
    }

    void Pick(Tool t)
    {
        CloseBuildPanel();
        tool = t;
        anchor = null;
        turns = 0;
        mirror = false;
        camera!.ArrowsMove = false;
        cursor = TileUnderMouse() ?? cursor;
        Tell(t.Name + ": " + Hint(t));
        UpdateGhost();
    }

    void PutDown()
    {
        tool = null;
        target = null;
        anchor = null;
        camera!.ArrowsMove = true;
        view!.Ghost = Array.Empty<(int, int, bool)>();
        view.Cursor = null;
        Tell("");
    }

    (int X, int Y)? TileUnderMouse()
    {
        var point = view!.GetGlobalMousePosition();
        var tile = ((int)Math.Floor(point.X), (int)Math.Floor(point.Y));
        return Colony.Ground!.Inside(tile.Item1, tile.Item2) ? tile : null;
    }

    /// <summary>With the tool in hand, places from (x0, y0) to (x1, y1) as a drag would (for testing: `-- --place
    /// x0,y0,x1,y1`); with only (x0, y0) given twice, shows the preview there without placing.</summary>
    public void PlaceAt(int x0, int y0, int x1, int y1, bool place)
    {
        if (tool == null)
            return;
        anchor = (x0, y0);
        cursor = (x1, y1);
        if (place)
            Place();
        else
            UpdateGhost();
    }

    /// <summary>Does the picked tile's command with key `key` (for testing: `-- --do M,x,y`); one that needs a tile
    /// is done on (x, y).</summary>
    public void DoAt(char key, int x, int y)
    {
        if (view!.SelectedTile is not var (tx, ty) || Inspect.Commands(Colony, tx, ty).FirstOrDefault(c => c.Key == key) is not Command command)
            return;
        Do(command);
        if (command.Run == null)
            PlaceAt(x, y, x, y, true);
    }

    // ---- The mouse and the keys, while building ---------------------------------------------------------------

    void OnPressed(Vector2 point)
    {
        if (tool == null)
        {
            OnMapClick(point);
            return;
        }
        if (TileUnderMouse() is var (x, y))
        {
            cursor = (x, y);
            anchor = (x, y);
            UpdateGhost();
        }
    }

    void OnReleased(Vector2 point)
    {
        if (tool == null || anchor == null)
            return;
        if (TileUnderMouse() is var (x, y))
            cursor = (x, y);
        Place();
    }

    void OnMoved(Vector2 point)
    {
        if (tool == null || TileUnderMouse() is not var (x, y) || (x, y) == cursor)
            return;
        cursor = (x, y);
        UpdateGhost();
    }

    /// <summary>The keys while building: arrows move the cursor, Enter starts and finishes, R and F turn and
    /// mirror, Esc stops. True if the key was one of them.</summary>
    bool BuildKey(Key key)
    {
        if (tool == null)
            return false;
        var ground = Colony.Ground!;
        (int dx, int dy) = key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        if ((dx, dy) != (0, 0))
        {
            cursor = (Math.Clamp(cursor.X + dx, 0, ground.Width - 1), Math.Clamp(cursor.Y + dy, 0, ground.Height - 1));
            camera!.Position = new Vector2(cursor.X + 0.5f, cursor.Y + 0.5f);
            UpdateGhost();
            return true;
        }
        switch (key)
        {
            case Key.Enter or Key.KpEnter:
                if (anchor == null && tool.Shape is Shape.Line or Shape.Box)
                {
                    anchor = cursor;
                    UpdateGhost();
                }
                else
                {
                    anchor ??= cursor;
                    Place();
                }
                return true;
            case Key.R when tool.Shape == Shape.Template:
                turns = (turns + 1) % 4;
                UpdateGhost();
                return true;
            case Key.F when tool.Shape == Shape.Template:
                mirror = !mirror;
                UpdateGhost();
                return true;
            case Key.Escape:
                PutDown();
                return true;
        }
        return false;
    }

    // ---- What would be placed, and placing it -------------------------------------------------------------

    // The tiles the tool covers now: from the anchor (or the cursor, before a drag) to the cursor.
    IReadOnlyList<(int X, int Y)> Covered()
    {
        var from = anchor ?? cursor;
        return tool!.Shape switch
        {
            Shape.Line => Shapes.Line(from.X, from.Y, cursor.X, cursor.Y),
            Shape.Box => Shapes.Box(from.X, from.Y, cursor.X, cursor.Y).Tiles.ToList(),
            _ => new[] { cursor },
        };
    }

    // Every tile as it would be, each one checked, and what the bar says about it.
    (IReadOnlyList<(int X, int Y, bool Ok)> Tiles, string Says) Look()
    {
        var work = Colony.Work!;
        var t = tool!;
        if (t.What == Target)
        {
            var check = target?.CheckAt?.Invoke(cursor.X, cursor.Y) ?? new Placement(false, "Nothing to place.");
            return (new[] { (cursor.X, cursor.Y, check.Ok) }, check.Ok ? t.Name + " here" : "Not here: " + check.Why);
        }
        if (t.What == Stockpile)
        {
            var from = anchor ?? cursor;
            var area = Shapes.Box(from.X, from.Y, cursor.X, cursor.Y);
            var check = work.CheckStockpile(area);
            return (area.Tiles.Select(p => (p.X, p.Y, check.Ok)).ToList(),
                    check.Ok ? "Stockpile, " + area.Width + " by " + area.Height : "Not here: " + check.Why);
        }
        if (t.What == TakeDown)
        {
            var tiles = Covered().Select(p => (p.X, p.Y, Inspect.Commands(Colony, p.X, p.Y).Any(c => c.Label == "Take down"))).ToList();
            int n = tiles.Count(p => p.Item3);
            return (tiles, n == 0 ? "Nothing there can be taken down." : "Take down " + n + (n == 1 ? " piece" : " pieces"));
        }
        IReadOnlyList<Preview> preview = t.Shape == Shape.Template
            ? work.CheckTemplate(t.What, cursor.X, cursor.Y, turns, mirror)
            : t.Shape == Shape.Single ? work.CheckAll(t.What, new[] { cursor }) : work.CheckAll(t.What, Covered());
        int ok = preview.Count(p => p.Placement.Ok);
        string says = t.Shape == Shape.Template
            ? t.Name + (ok == preview.Count ? "" : ": not here, " + preview.First(p => !p.Placement.Ok).Placement.Why)
              + " (turned " + turns * 90 + "°" + (mirror ? ", mirrored" : "") + ")"
            : t.Name + " x " + ok + (ok < preview.Count ? "; " + (preview.Count - ok) + " cannot: " + preview.First(p => !p.Placement.Ok).Placement.Why : "");
        if (preview.FirstOrDefault(p => p.Placement.Ok && p.Placement.Warning != null) is Preview warned)
            says += " " + warned.Placement.Warning;
        return (preview.Select(p => (p.X, p.Y, p.Placement.Ok)).ToList(), says);
    }

    void UpdateGhost()
    {
        if (tool == null)
            return;
        var (tiles, says) = Look();
        view!.Ghost = tiles;
        view.Cursor = cursor;
        said!.Text = says;
    }

    // Places what the tool covers, says what came of it, and keeps the tool in hand for the next.
    void Place()
    {
        var work = Colony.Work!;
        var t = tool!;
        string result;
        if (t.What == Target && target is Command { CheckAt: not null, RunAt: not null } aimed)
        {
            var check = aimed.CheckAt(cursor.X, cursor.Y);
            if (!check.Ok)
            {
                anchor = null;
                said!.Text = "Not here: " + check.Why;
                return;
            }
            aimed.RunAt(cursor.X, cursor.Y);
            PutDown();                   // a one-off: done once it is placed
            said!.Text = aimed.Label + ": done.";
            ShowInspection();
            return;
        }
        if (t.What == Stockpile)
        {
            var from = anchor ?? cursor;
            var (pile, check) = work.PlaceStockpile(Shapes.Box(from.X, from.Y, cursor.X, cursor.Y));
            result = pile != null ? pile.Name + " laid." : "Not here: " + check.Why;
        }
        else if (t.What == TakeDown)
        {
            int n = 0;
            foreach (var (x, y) in Covered())
                if (Inspect.Commands(Colony, x, y).FirstOrDefault(c => c.Label == "Take down") is Command down)
                {
                    down.Run!();
                    n++;
                }
            result = n == 0 ? "Nothing there can be taken down." : n + (n == 1 ? " piece" : " pieces") + " to take down.";
        }
        else if (t.Shape == Shape.Template)
        {
            var (projects, placement) = work.PlanTemplate(t.What, cursor.X, cursor.Y, turns, mirror);
            result = placement.Ok ? t.Name + " planned: " + projects.Count + " pieces." + (placement.Warning is string w ? " " + w : "")
                                  : "Not here: " + placement.Why;
        }
        else
        {
            var tiles = t.Shape == Shape.Single ? new[] { cursor } : Covered();
            var (planned, refused) = work.PlanAll(t.What, tiles);
            result = planned + " " + t.Name.ToLowerInvariant() + (planned == 1 ? "" : " pieces") + " planned."
                     + (refused != null ? " Not at " + refused : "");
        }
        anchor = null;
        UpdateGhost();
        said!.Text = result;
        ShowInspection();
    }
}
