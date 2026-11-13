// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Colony;

namespace WardensReckoning.UI;

/// <summary>
/// The colony's local map, drawn (step 18a, library Review 38): one unit here is one tile (a metre), so the map is
/// as many units wide as it has tiles, and MapCamera zooms in on it as it does on the world map.
///
/// The ground is a picture of one pixel a tile, each surface its colour, a tree a dark green pixel and a rock a grey
/// one, painted once and stretched by the camera (crisp squares: TextureFilterEnum.Nearest), so a forest shows at
/// any zoom. Over it, in `_Draw`, everything else, as plain shapes:
///
/// - each surface also has a symbol (water ripples, rough ground's specks...), so no surface is told apart by colour
///   alone (FP-11); trees, rocks and the symbols are drawn only when zoomed in far enough to see them;
/// - walls, doors, floors and roads as squares; buildings under way as outlines with their progress;
/// - stores (the carts, stockpiles), the drinking place and bedrolls as shapes with a letter;
/// - the colonists as circles with their names.
///
/// This class only draws: it reads the colony and never changes it (Shared Systems §19.1).
/// </summary>
public partial class ColonyView : Node2D
{
    // Each surface's colour, and the symbol drawn over it. A surface not in this table (new content) still gets a
    // colour made from its name, and no symbol.
    static readonly Dictionary<string, Color> Colours = new()
    {
        ["grass"] = new Color(0.44f, 0.60f, 0.31f),
        ["rough"] = new Color(0.53f, 0.50f, 0.38f),
        ["sand"] = new Color(0.86f, 0.79f, 0.56f),
        ["snow"] = new Color(0.93f, 0.95f, 0.97f),
        ["mud"] = new Color(0.40f, 0.31f, 0.21f),
        ["fresh_water"] = new Color(0.27f, 0.48f, 0.78f),
        ["salt_water"] = new Color(0.15f, 0.30f, 0.52f),
        ["road"] = new Color(0.62f, 0.57f, 0.47f),
        ["ground"] = new Color(0.50f, 0.47f, 0.40f),
    };

    static readonly Color Ink = new(0.08f, 0.08f, 0.08f);
    static readonly Color Paper = new(0.97f, 0.96f, 0.92f);

    Func<Colony.Colony> colony;
    Sprite2D? groundPicture;

    /// <summary>The tile picked in the inspector, outlined (or null).</summary>
    public (int X, int Y)? SelectedTile { get; set; }

    /// <summary>The colonist picked in the inspector, ringed (or null).</summary>
    public ColonistId? SelectedWorker { get; set; }

    public ColonyView() : this(null!) { }

    // Where each colonist stood before the last tick (step 21b), by their number, and the colony that was: the
    // colonists are drawn on the way from there to where they are now, as far as the next tick is due, so that a
    // walk of a metre a second looks like walking, not a hop a second.
    readonly Dictionary<long, (double X, double Y)> before = new();
    Colony.Colony? beforeOf;

    /// <summary>How far the next tick is due, 0 to 1 (the pacer's), or 1 when paused: where colonists are drawn
    /// between where they stood and where they are.</summary>
    public double Due { get; set; } = 1;

    /// <summary>Notes where everyone stands, just before a tick (the session calls it).</summary>
    public void Remember()
    {
        var now = colony();
        before.Clear();
        beforeOf = now;
        foreach (var w in now.Work?.Workers ?? Array.Empty<Worker>())
            before[w.Id.Value] = (w.X, w.Y);
    }

    /// <summary>Where a colonist is drawn now.</summary>
    Vector2 DrawnAt(Worker w)
    {
        if (beforeOf != colony() || !before.TryGetValue(w.Id.Value, out var from))
            return new Vector2((float)w.X, (float)w.Y);
        float t = (float)Math.Clamp(Due, 0, 1);
        return new Vector2((float)(from.X + (w.X - from.X) * t), (float)(from.Y + (w.Y - from.Y) * t));
    }

    /// <summary>`colony` gives the colony to draw each frame (a loaded save replaces the one being played).</summary>
    public ColonyView(Func<Colony.Colony> colony) => this.colony = colony;

    public override void _Ready()
    {
        // A node draws itself before its children, so the ground picture is told to go behind: under the shapes.
        groundPicture = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Nearest, ShowBehindParent = true };
        AddChild(groundPicture);
        PaintGround();
    }

    static readonly Color TreeColour = new(0.13f, 0.33f, 0.16f);
    static readonly Color RockColour = new(0.55f, 0.55f, 0.55f);

    /// <summary>Paints the ground picture again (when another colony is loaded, or a tree is felled).</summary>
    public void PaintGround()
    {
        var ground = colony().Ground!;
        var image = Image.CreateEmpty(ground.Width, ground.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < ground.Height; y++)
            for (int x = 0; x < ground.Width; x++)
                image.SetPixel(x, y, ground.ObstacleAt(x, y) switch
                {
                    "tree" => TreeColour,
                    "rock" => RockColour,
                    _ => ColourOf(ground.SurfaceAt(x, y).Id),
                });
        groundPicture!.Texture = ImageTexture.CreateFromImage(image);
    }

    /// <summary>The colour a surface is painted.</summary>
    public static Color ColourOf(string surface)
    {
        if (Colours.TryGetValue(surface, out var colour))
            return colour;
        // A made-up but steady colour for a surface this table does not know: the same name, the same colour.
        uint hash = 2166136261;
        foreach (char c in surface)
            hash = (hash ^ c) * 16777619;
        return Color.FromHsv(hash % 360 / 360f, 0.35f, 0.6f);
    }

    /// <summary>Called when the left mouse button goes down on the map, with the point in tiles.</summary>
    public event Action<Vector2>? Pressed;

    /// <summary>Called when the left mouse button comes up again (the end of a click or a drag).</summary>
    public event Action<Vector2>? Released;

    /// <summary>Called when the mouse moves over the map.</summary>
    public event Action<Vector2>? Moved;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            (button.Pressed ? Pressed : Released)?.Invoke(GetGlobalMousePosition());
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion)
            Moved?.Invoke(GetGlobalMousePosition());
    }

    /// <summary>
    /// What would be placed, shown before it is (step 18b): each tile with whether it may go there. A tile that may
    /// is outlined in white; one that may not is shaded and crossed out, so the two differ by shape, not colour only.
    /// </summary>
    public IReadOnlyList<(int X, int Y, bool Ok)> Ghost { get; set; } = Array.Empty<(int, int, bool)>();

    /// <summary>The build cursor's tile (the mouse's, or moved with the arrows), outlined; null when not building.</summary>
    public (int X, int Y)? Cursor { get; set; }

    public override void _Process(double delta) => QueueRedraw();     // the colonists move: draw again each frame

    public override void _Draw()
    {
        var c = colony();
        var ground = c.Ground!;
        // Only the tiles on screen are drawn: the camera's view, in map units.
        var view = GetViewport().GetCanvasTransform().AffineInverse() * GetViewportRect();
        int left = Math.Max(0, (int)Math.Floor(view.Position.X)), top = Math.Max(0, (int)Math.Floor(view.Position.Y));
        int right = Math.Min(ground.Width - 1, (int)Math.Ceiling(view.End.X)), bottom = Math.Min(ground.Height - 1, (int)Math.Ceiling(view.End.Y));
        float pixels = GetViewport().GetCanvasTransform().Scale.X;      // screen pixels a tile
        bool close = pixels >= 6;           // trees and rocks are worth drawing
        bool closer = pixels >= 14;         // and the surfaces' symbols
        var font = ThemeDB.FallbackFont;

        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                var at = new Vector2(x, y);
                if (closer)
                    DrawSymbol(ground.SurfaceAt(x, y).Id, at);
                if (ground.PieceAt(x, y) is string piece)
                    DrawPiece(piece, at);
                if (close && ground.ObstacleAt(x, y) is string thing)
                    DrawThing(thing, at);
            }

        var work = c.Work!;
        // Buildings under way: an outline of their size, and how far along they are.
        foreach (var p in work.Projects)
        {
            var kind = c.Content!.Pieces[p.Piece];
            var box = new Rect2(p.X + 0.1f, p.Y + 0.1f, Math.Max(1, kind.Width) - 0.2f, Math.Max(1, kind.Height) - 0.2f);
            DrawRect(box, p.Removing ? new Color(0.85f, 0.3f, 0.2f) : new Color(1f, 0.85f, 0.2f), false, 0.08f);
            if (p.Paused)
                DrawLine(box.Position, box.End, Ink, 0.08f);       // a paused one is crossed through
        }
        // Stores: a stockpile's area outlined, the carts and other stores as a box with their first letter.
        var springs = work.Springs.ToHashSet();
        // (A water barrel's or a wash basin's water is its piece: drawn with the pieces, not as a store.)
        foreach (var inventory in c.Stocks.Inventories.Where(i => i.Holder == Holder.Store && i.Capacity == null))
        {
            if (inventory.Area is Area area)
            {
                DrawRect(new Rect2(area.X, area.Y, area.Width, area.Height), new Color(0.95f, 0.9f, 0.7f), false, 0.1f);
                continue;
            }
            var middle = new Vector2((float)inventory.X, (float)inventory.Y);
            if (springs.Contains(inventory.Id))
            {
                // A water source: a blue round marked W; frozen, a pale one marked I (ice).
                bool frozen = work.SourceOf(inventory.Id).Frozen;
                DrawCircle(middle, 0.42f, frozen ? new Color(0.88f, 0.94f, 1f) : new Color(0.55f, 0.8f, 1f));
                DrawArc(middle, 0.42f, 0, Mathf.Tau, 20, Ink, 0.06f);
                Letter(font, frozen ? "I" : "W", middle, pixels);
            }
            else
            {
                DrawRect(new Rect2(middle - new Vector2(0.45f, 0.35f), new Vector2(0.9f, 0.7f)), new Color(0.6f, 0.42f, 0.25f));
                DrawRect(new Rect2(middle - new Vector2(0.45f, 0.35f), new Vector2(0.9f, 0.7f)), Ink, false, 0.06f);
                Letter(font, Initial(inventory.Name), middle, pixels);
            }
        }
        foreach (var bed in work.Bedrolls)
        {
            var middle = new Vector2((float)bed.X, (float)bed.Y);
            DrawRect(new Rect2(middle - new Vector2(0.3f, 0.42f), new Vector2(0.6f, 0.84f)), new Color(0.75f, 0.35f, 0.3f));
            DrawRect(new Rect2(middle - new Vector2(0.3f, 0.42f), new Vector2(0.6f, 0.84f)), Ink, false, 0.05f);
        }
        if (SelectedTile is var (sx, sy))
            DrawRect(new Rect2(sx, sy, 1, 1), new Color(1, 1, 1), false, 0.12f);
        foreach (var (gx, gy, ok) in Ghost)
        {
            var tile = new Rect2(gx + 0.08f, gy + 0.08f, 0.84f, 0.84f);
            if (ok)
            {
                DrawRect(tile, new Color(1, 1, 1, 0.25f));
                DrawRect(tile, Paper, false, 0.08f);
            }
            else
            {
                DrawRect(tile, new Color(0.8f, 0.1f, 0.1f, 0.35f));
                DrawLine(tile.Position, tile.End, Ink, 0.1f);
                DrawLine(new Vector2(tile.End.X, tile.Position.Y), new Vector2(tile.Position.X, tile.End.Y), Ink, 0.1f);
            }
        }
        if (Cursor is var (kx, ky))
        {
            DrawRect(new Rect2(kx - 0.05f, ky - 0.05f, 1.1f, 1.1f), Ink, false, 0.14f);
            DrawRect(new Rect2(kx + 0.05f, ky + 0.05f, 0.9f, 0.9f), Paper, false, 0.06f);
        }
        // The colonists last, on top: a circle (at least 5 screen pixels across, so they show when zoomed out), a ring
        // when picked, and the name above, on the lowest line not taken by a name of a colonist standing within a
        // name's width, so names side by side (or of colonists on one spot) do not overlap.
        float radius = Math.Max(0.38f, 5f / pixels);
        var drawn = new List<(Vector2 At, int Level)>();
        foreach (var w in work.Workers)
        {
            var at = DrawnAt(w);
            // The lowest line no name nearby is on already.
            var taken = drawn.Where(d => Math.Abs(d.At.X - at.X) * pixels < 70 && Math.Abs(d.At.Y - at.Y) * pixels < 20)
                             .Select(d => d.Level).ToHashSet();
            int level = 0;
            while (taken.Contains(level))
                level++;
            drawn.Add((at, level));
            var middle = at;
            DrawCircle(middle, radius, w.Collapsed != null ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.95f, 0.85f, 0.65f));
            DrawArc(middle, radius, 0, Mathf.Tau, 20, Ink, radius * 0.18f);
            if (SelectedWorker == w.Id)
                DrawArc(middle, radius * 1.6f, 0, Mathf.Tau, 24, Paper, radius * 0.3f);
            DrawName(font, w.Name, middle - new Vector2(0, radius * 1.6f + level * 19f / pixels), pixels);
        }
        // Talk (step 25b): over whoever's turn it is in a talk, a bubble with what it is about, above their name.
        foreach (var talk in work.Talks)
            if (work.Bubble(talk) is var (speaker, words) && work.Workers.FirstOrDefault(x => x.Id == speaker) is Worker who)
            {
                var at = DrawnAt(who);
                int level = drawn.FirstOrDefault(d => d.At == at).Level;
                DrawBubble(font, words, at - new Vector2(0, radius * 1.6f + (level + 1) * 19f / pixels + 6f / pixels), pixels);
            }
    }

    /// <summary>A chat bubble (step 25b): the words on paper with an ink edge and a little tail pointing down to the
    /// speaker, the same size on screen at any zoom.</summary>
    void DrawBubble(Font font, string text, Vector2 above, float pixels)
    {
        int size = 13;
        float scale = 1f / pixels;
        DrawSetTransform(above, 0, new Vector2(scale, scale));
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var box = new Rect2(-width / 2 - 6, -size - 5, width + 12, size + 11);
        DrawRect(box, Paper);
        DrawRect(box, Ink, false, 1.5f);
        DrawColoredPolygon(new[] { new Vector2(-5, box.End.Y), new Vector2(5, box.End.Y), new Vector2(0, box.End.Y + 7) }, Paper);
        DrawPolyline(new[] { new Vector2(-5, box.End.Y), new Vector2(0, box.End.Y + 7), new Vector2(5, box.End.Y) }, Ink, 1.5f);
        DrawString(font, new Vector2(-width / 2, 0), text, HorizontalAlignment.Left, -1, size, Ink);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    // A small symbol for each surface that has one: what tells it apart besides its colour.
    void DrawSymbol(string surface, Vector2 at)
    {
        var shade = new Color(0, 0, 0, 0.28f);
        switch (surface)
        {
            case "fresh_water":
            case "salt_water":
                var light = new Color(1, 1, 1, 0.45f);
                DrawPolyline(new[] { at + new Vector2(0.15f, 0.4f), at + new Vector2(0.35f, 0.3f), at + new Vector2(0.55f, 0.4f), at + new Vector2(0.75f, 0.3f) }, light, 0.05f);
                if (surface == "salt_water")
                    DrawCircle(at + new Vector2(0.5f, 0.7f), 0.06f, light);
                break;
            case "rough":
                DrawCircle(at + new Vector2(0.3f, 0.35f), 0.05f, shade);
                DrawCircle(at + new Vector2(0.65f, 0.6f), 0.05f, shade);
                break;
            case "sand":
                DrawCircle(at + new Vector2(0.5f, 0.5f), 0.035f, shade);
                break;
            case "mud":
                DrawLine(at + new Vector2(0.25f, 0.55f), at + new Vector2(0.75f, 0.55f), shade, 0.06f);
                break;
            case "snow":
                DrawLine(at + new Vector2(0.4f, 0.5f), at + new Vector2(0.6f, 0.5f), shade, 0.04f);
                DrawLine(at + new Vector2(0.5f, 0.4f), at + new Vector2(0.5f, 0.6f), shade, 0.04f);
                break;
        }
    }

    // A tree: a dark green triangle; a rock: a grey round stone with an outline; anything else: a cross.
    void DrawThing(string thing, Vector2 at)
    {
        switch (thing)
        {
            case "tree":
                // Over the tree's dark pixel: the ground's colour, then the tree, so its shape shows up close.
                DrawRect(new Rect2(at, Vector2.One), ColourOf(colony().Ground!.SurfaceAt((int)at.X, (int)at.Y).Id));
                DrawColoredPolygon(new[] { at + new Vector2(0.5f, 0.08f), at + new Vector2(0.9f, 0.85f), at + new Vector2(0.1f, 0.85f) }, TreeColour);
                DrawPolyline(new[] { at + new Vector2(0.5f, 0.08f), at + new Vector2(0.9f, 0.85f), at + new Vector2(0.1f, 0.85f), at + new Vector2(0.5f, 0.08f) }, Ink, 0.05f);
                break;
            case "rock":
                DrawRect(new Rect2(at, Vector2.One), ColourOf(colony().Ground!.SurfaceAt((int)at.X, (int)at.Y).Id));
                DrawCircle(at + new Vector2(0.5f, 0.55f), 0.36f, RockColour);
                DrawArc(at + new Vector2(0.5f, 0.55f), 0.36f, 0, Mathf.Tau, 16, Ink, 0.05f);
                break;
            default:
                DrawLine(at + new Vector2(0.2f, 0.2f), at + new Vector2(0.8f, 0.8f), Ink, 0.08f);
                DrawLine(at + new Vector2(0.8f, 0.2f), at + new Vector2(0.2f, 0.8f), Ink, 0.08f);
                break;
        }
    }

    // A finished piece: a wall filled, a door with a gap, a floor and a road flat; the well and others as a box with a letter.
    void DrawPiece(string piece, Vector2 at)
    {
        var tile = new Rect2(at, Vector2.One);
        switch (piece)
        {
            case "wall":
                DrawRect(tile, new Color(0.36f, 0.25f, 0.15f));
                DrawRect(tile, Ink, false, 0.04f);
                break;
            case "door":
                DrawRect(tile, new Color(0.62f, 0.45f, 0.26f));
                DrawRect(new Rect2(at + new Vector2(0.3f, 0.1f), new Vector2(0.4f, 0.8f)), Ink, false, 0.06f);
                break;
            case "floor":
                DrawRect(tile, new Color(0.72f, 0.58f, 0.40f));
                DrawLine(at + new Vector2(0, 0.5f), at + new Vector2(1, 0.5f), new Color(0, 0, 0, 0.25f), 0.03f);
                break;
            case "road":
                DrawRect(tile, ColourOf("road"));
                break;
            case "water_barrel":
                // A barrel: round, its staves' wood with water on top.
                DrawCircle(at + new Vector2(0.5f, 0.5f), 0.42f, new Color(0.55f, 0.38f, 0.2f));
                DrawCircle(at + new Vector2(0.5f, 0.5f), 0.28f, new Color(0.35f, 0.6f, 0.9f));
                DrawArc(at + new Vector2(0.5f, 0.5f), 0.42f, 0, Mathf.Tau, 20, Ink, 0.05f);
                break;
            case "wash_basin":
                // A basin: a square tub, water inside, a wide rim.
                DrawRect(new Rect2(at + new Vector2(0.1f, 0.2f), new Vector2(0.8f, 0.6f)), new Color(0.6f, 0.45f, 0.28f));
                DrawRect(new Rect2(at + new Vector2(0.22f, 0.32f), new Vector2(0.56f, 0.36f)), new Color(0.45f, 0.7f, 0.95f));
                DrawRect(new Rect2(at + new Vector2(0.1f, 0.2f), new Vector2(0.8f, 0.6f)), Ink, false, 0.05f);
                break;
            default:
                DrawRect(tile, new Color(0.5f, 0.5f, 0.55f));
                DrawRect(tile, Ink, false, 0.05f);
                break;
        }
    }

    /// <summary>The letter a store is marked with: its name's last word's first letter ("The founders' carts": C,
    /// "House store": S).</summary>
    public static string Initial(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "?" : words[^1][..1].ToUpperInvariant();
    }

    // A letter in the middle of a shape, readable at any zoom (drawn at screen size, then scaled down to the map).
    void Letter(Font font, string text, Vector2 middle, float pixels)
    {
        if (pixels < 10)
            return;
        int size = 14;
        float scale = 1f / pixels;
        DrawSetTransform(middle, 0, new Vector2(scale, scale));
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new Vector2(-width / 2, size * 0.35f), text, HorizontalAlignment.Left, -1, size, Ink);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    // A colonist's name over their head, on a pale strip so it reads on any ground.
    void DrawName(Font font, string text, Vector2 above, float pixels)
    {
        int size = 13;
        float scale = 1f / pixels;
        DrawSetTransform(above, 0, new Vector2(scale, scale));
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawRect(new Rect2(-width / 2 - 3, -size - 2, width + 6, size + 6), new Color(Paper, 0.85f));
        DrawString(font, new Vector2(-width / 2, 0), text, HorizontalAlignment.Left, -1, size, Ink);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
