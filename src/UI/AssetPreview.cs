// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// A test bench for the first pictures (asset prototype v0.2, `res://assets/prototype/`; and the trees of the
/// vegetation prototype v0.3, `res://assets/prototype-v0.3/`, as a second set to compare), on its own: no colony, no
/// simulation, nothing of the game is loaded or changed. Run it alone (src/UI/AssetPreview.tscn: in the editor, open
/// it and press F6; or `godot --path . res://src/UI/AssetPreview.tscn`).
///
/// It draws the same small field twice, side by side: on the left with the pictures, on the right with the shapes the
/// game draws today (ColonyView), so the two can be compared. One tile is one unit, as in the game: a picture 32 pixels
/// wide covers one tile. Trees stand with the foot of their trunk (16, 63) on the middle of their tile's bottom edge;
/// a colonist with their feet (16, 47) on their position. Trees and colonists are drawn in the order of where their
/// feet are, top first, so whoever stands lower on the screen is in front. One colonist stands still; one walks a
/// loop that passes behind two trees and in front of another.
///
/// Keys: 1–4 zoom to 16, 32, 40 or 64 pixels a tile; the mouse wheel zooms in the game's steps (×1.15); the right
/// mouse button or the arrows move; F nearest or smooth filtering; P snapping to whole pixels; A the anchor points;
/// G a tile grid; [ and ] the walker's speed; Space stops or starts the walker; T the trees of v0.2 or v0.3; with v0.3,
/// V the variant (green, green 2, autumn, winter; the conifer's 1 or 2) and Y the age (mature, young, sapling). A v0.3
/// tree's foot is taken to be the middle of its picture's bottom edge, as its README assumes (not checked by it).
/// </summary>
public partial class AssetPreview : Node2D
{
    const string Folder = "res://assets/prototype/";
    const string Vegetation = "res://assets/prototype-v0.3/vegetation/";
    const int Pixels = 32;                         // a picture's pixels a tile
    const int Wide = 12, High = 8;                 // the field, in tiles
    const int Gap = 1;                             // tiles between the two fields

    // Today's colours, as ColonyView draws them.
    static readonly Color Ink = new(0.08f, 0.08f, 0.08f);
    static readonly Color TreeColour = new(0.13f, 0.33f, 0.16f);

    readonly Dictionary<string, Texture2D?> pictures = new();
    readonly List<string> missing = new();
    Camera2D? camera;
    Label? info;
    bool anchors, grid, nearest = true, walking = true;
    bool v03;                                      // the trees of v0.3 (T)
    int variant;                                   // v0.3: 0 green, 1 green 2, 2 autumn, 3 winter (V)
    int age;                                       // v0.3: 0 mature, 1 young, 2 sapling (Y)
    static readonly string[] Ages = { "mature", "young", "sapling" };
    static readonly string[] Variants = { "green_01", "green_02", "autumn_01", "winter_01" };
    double speed = 2;                              // metres a second of the walker
    double walked;                                 // metres along the loop
    readonly Queue<double> frames = new();

    // The trees, by tile (in each field): two side by side, one in the row below the first, one on the rough ground.
    static readonly (int X, int Y, int Kind)[] Trees = { (3, 3, 1), (4, 3, 2), (3, 4, 1), (8, 3, 2) };
    // The rough patch.
    static bool Rough(int x, int y) => x >= 7 && x <= 10 && y >= 2 && y <= 5;
    // The colonist who stands still, at a tile's middle.
    static readonly Vector2 Standing = new(6.5f, 4.5f);
    // The walker's loop: tile middles, round the first three trees (behind the top two, in front of the lower one).
    static readonly Vector2[] Loop = { new(2.5f, 2.5f), new(5.5f, 2.5f), new(5.5f, 5.5f), new(2.5f, 5.5f) };

    public override void _Ready()
    {
        // Every picture, through Godot's import system (each file's .import file holds how it is imported).
        foreach (var name in new[] { "grass_01", "grass_02", "grass_03", "grass_04", "rough_01", "rough_02", "rough_03", "rough_04" })
            Load("surfaces/" + name);
        Load("things/tree_01");
        Load("things/tree_02");
        foreach (var type in new[] { "deciduous", "conifer" })
            foreach (var young in new[] { "young", "sapling" })
                Load("tree_" + young + "_" + type + "_01", Vegetation);
        foreach (var name in Variants)
            Load("tree_mature_deciduous_" + name, Vegetation);
        Load("tree_mature_conifer_01", Vegetation);
        Load("tree_mature_conifer_02", Vegetation);
        foreach (var layer in new[] { "base", "clothing", "hair" })
            Load("colonists/" + layer + "_idle_down");

        TextureFilter = TextureFilterEnum.Nearest;
        camera = new Camera2D { Position = new Vector2(Wide + Gap / 2f, High / 2f) };
        AddChild(camera);
        SetZoom(32);

        var hud = new CanvasLayer();
        AddChild(hud);
        var panel = new PanelContainer { Position = new Vector2(8, 8) };
        panel.AddThemeStyleboxOverride("panel", Ledger.Panel());
        hud.AddChild(panel);
        info = Ledger.Label("", 13);
        info.AutowrapMode = TextServer.AutowrapMode.Off;
        panel.AddChild(info);
    }

    void Load(string name, string folder = Folder)
    {
        string path = folder + name + ".png";
        Texture2D? texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (texture == null)
            missing.Add(name);
        pictures[name] = texture;
    }

    Texture2D? Picture(string name) => pictures.GetValueOrDefault(name);

    // ---- Each frame ----------------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (walking)
            walked += speed * delta;
        frames.Enqueue(delta * 1000);
        while (frames.Count > 60)
            frames.Dequeue();
        var move = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        camera!.Position += move * 600 * (float)delta / camera.Zoom.X;
        info!.Text = "Asset preview: prototype v0.2. Pictures on the left, today's shapes on the right.\n"
                     + "Zoom " + camera.Zoom.X.ToString("0.#") + " px a tile (×" + (camera.Zoom.X / Pixels).ToString("0.###") + ")"
                     + " · filter " + (nearest ? "nearest" : "smooth") + " · pixel snap " + (GetViewport().Snap2DTransformsToPixel ? "on" : "off")
                     + " · walker " + (walking ? speed.ToString("0.#") + " m/s" : "stopped") + "\n"
                     + "Trees: " + (v03 ? "v0.3, " + Ages[age] + (age == 0 ? ", " + Variants[variant].Replace("_01", "").Replace("_02", " 2") : "") : "v0.2") + "\n"
                     + "Frame " + frames.Average().ToString("0.0") + " ms average, " + frames.Max().ToString("0.0") + " ms slowest (last 60)"
                     + (missing.Count > 0 ? "\nMissing, drawn as shapes: " + string.Join(", ", missing) : "") + "\n"
                     + "1–4 zoom 16/32/40/64 · wheel ×1.15 · right drag or arrows move · F filter · P snap · A anchors · G grid · [ ] speed · Space walker"
                     + " · T trees v0.2/v0.3 · V variant · Y age";
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                SetZoom(camera!.Zoom.X * 1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                SetZoom(camera!.Zoom.X / 1.15f);
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Right) != 0:
                camera!.Position -= motion.Relative / camera.Zoom;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.Keycode)
                {
                    case Key.Key1: SetZoom(16); break;
                    case Key.Key2: SetZoom(32); break;
                    case Key.Key3: SetZoom(40); break;
                    case Key.Key4: SetZoom(64); break;
                    case Key.F:
                        nearest = !nearest;
                        TextureFilter = nearest ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
                        break;
                    case Key.P:
                        GetViewport().Snap2DTransformsToPixel = !GetViewport().Snap2DTransformsToPixel;
                        break;
                    case Key.A: anchors = !anchors; break;
                    case Key.G: grid = !grid; break;
                    case Key.Bracketleft: speed = Math.Max(0.5, speed / 2); break;
                    case Key.Bracketright: speed = Math.Min(32, speed * 2); break;
                    case Key.Space: walking = !walking; break;
                    case Key.T: v03 = !v03; break;
                    case Key.V: variant = (variant + 1) % Variants.Length; break;
                    case Key.Y: age = (age + 1) % Ages.Length; break;
                    default: return;
                }
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    void SetZoom(float pixels) => camera!.Zoom = new Vector2(Math.Clamp(pixels, 4, 128), Math.Clamp(pixels, 4, 128));

    // Where the walker is: `walked` metres along the loop, going round.
    Vector2 Walker()
    {
        double length = 0;
        for (int i = 0; i < Loop.Length; i++)
            length += Loop[i].DistanceTo(Loop[(i + 1) % Loop.Length]);
        double along = walked % length;
        for (int i = 0; ; i = (i + 1) % Loop.Length)
        {
            var (from, to) = (Loop[i], Loop[(i + 1) % Loop.Length]);
            double leg = from.DistanceTo(to);
            if (along <= leg)
                return from.Lerp(to, (float)(along / leg));
            along -= leg;
        }
    }

    // ---- Drawing --------------------------------------------------------------------------------------------------

    public override void _Draw()
    {
        DrawField(0, true);
        DrawField(Wide + Gap, false);
    }

    // One field, `left` tiles from the origin: with the pictures, or with today's shapes.
    void DrawField(int left, bool withPictures)
    {
        for (int y = 0; y < High; y++)
            for (int x = 0; x < Wide; x++)
            {
                string surface = Rough(x, y) ? "rough" : "grass";
                var tile = new Rect2(left + x, y, 1, 1);
                if (withPictures && Picture("surfaces/" + surface + "_0" + Variant(x, y)) is Texture2D texture)
                    DrawTextureRect(texture, tile, false);
                else
                    DrawRect(tile, ColonyView.ColourOf(surface));
            }
        // Trees and colonists, by where their feet are: top first, so the lower stands in front.
        var standing = new List<(float Feet, Action Draw)>();
        foreach (var (tx, ty, kind) in Trees)
        {
            var foot = new Vector2(left + tx + 0.5f, ty + 1);     // the middle of the tile's bottom edge
            standing.Add((foot.Y, () => DrawTree(foot, kind, withPictures)));
        }
        foreach (var at in new[] { Standing, Walker() })
        {
            var feet = new Vector2(left + at.X, at.Y);
            standing.Add((feet.Y, () => DrawColonist(feet, withPictures)));
        }
        foreach (var (_, draw) in standing.OrderBy(s => s.Feet))
            draw();
        if (grid)
        {
            var line = new Color(0, 0, 0, 0.35f);
            for (int x = 0; x <= Wide; x++)
                DrawLine(new Vector2(left + x, 0), new Vector2(left + x, High), line, 1 / camera!.Zoom.X);
            for (int y = 0; y <= High; y++)
                DrawLine(new Vector2(left, y), new Vector2(left + Wide, y), line, 1 / camera!.Zoom.X);
        }
    }

    // The picture of a tree of `kind` (1 deciduous, 2 conifer) in the set shown: v0.2's, or v0.3's of the chosen age and
    // variant.
    Texture2D? TreePicture(int kind)
    {
        if (!v03)
            return Picture("things/tree_0" + kind);
        string type = kind == 1 ? "deciduous" : "conifer";
        string name = age switch
        {
            0 when kind == 1 => "tree_mature_deciduous_" + Variants[variant],
            0 => "tree_mature_conifer_0" + (variant % 2 + 1),
            1 => "tree_young_" + type + "_01",
            _ => "tree_sapling_" + type + "_01",
        };
        return Picture(name);
    }

    // A tree, its foot on `foot` (v0.2: the trunk's foot, picture pixel 16, 64; v0.3: the middle of the picture's bottom
    // edge); or today's triangle on its tile.
    void DrawTree(Vector2 foot, int kind, bool withPictures)
    {
        if (withPictures && TreePicture(kind) is Texture2D texture)
            DrawTextureRect(texture, new Rect2(foot.X - texture.GetWidth() / 2f / Pixels, foot.Y - texture.GetHeight() / (float)Pixels,
                                               texture.GetWidth() / (float)Pixels, texture.GetHeight() / (float)Pixels), false);
        else
        {
            var at = new Vector2(foot.X - 0.5f, foot.Y - 1);
            DrawColoredPolygon(new[] { at + new Vector2(0.5f, 0.08f), at + new Vector2(0.9f, 0.85f), at + new Vector2(0.1f, 0.85f) }, TreeColour);
            DrawPolyline(new[] { at + new Vector2(0.5f, 0.08f), at + new Vector2(0.9f, 0.85f), at + new Vector2(0.1f, 0.85f), at + new Vector2(0.5f, 0.08f) }, Ink, 0.05f);
        }
        if (anchors)
            Cross(foot);
    }

    // A colonist, their feet (picture pixel 16, 47: below the soles on row 46) on `feet`: the three layers in order;
    // or today's circle around `feet`.
    void DrawColonist(Vector2 feet, bool withPictures)
    {
        var layers = new[] { "base", "clothing", "hair" }.Select(l => Picture("colonists/" + l + "_idle_down")).ToList();
        if (withPictures && layers.All(l => l != null))
            foreach (var layer in layers)
                DrawTextureRect(layer!, new Rect2(feet.X - 16f / Pixels, feet.Y - 47f / Pixels, layer!.GetWidth() / (float)Pixels,
                                                  layer.GetHeight() / (float)Pixels), false);
        else
        {
            DrawCircle(feet, 0.38f, new Color(0.95f, 0.85f, 0.65f));
            DrawArc(feet, 0.38f, 0, Mathf.Tau, 20, Ink, 0.07f);
        }
        if (anchors)
            Cross(feet);
    }

    // A small red cross on an anchor point, the same size at any zoom.
    void Cross(Vector2 at)
    {
        float arm = 5 / camera!.Zoom.X, width = 1.5f / camera.Zoom.X;
        var red = new Color(1, 0.15f, 0.15f);
        DrawLine(at - new Vector2(arm, 0), at + new Vector2(arm, 0), red, width);
        DrawLine(at - new Vector2(0, arm), at + new Vector2(0, arm), red, width);
    }

    /// <summary>Which of the four variants a tile shows: from its place alone (a fixed mixing of x and y), so the
    /// same tile always looks the same, and no dice of any colony are used.</summary>
    static int Variant(int x, int y)
    {
        unchecked
        {
            uint h = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return (int)(h % 4) + 1;
        }
    }
}
