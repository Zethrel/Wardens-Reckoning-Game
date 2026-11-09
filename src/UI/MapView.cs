// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.World;

namespace WardensReckoning.UI;

/// <summary>
/// The map of one year: the terrain, the borders over the land, the towns with their pictures, and the export's
/// founding sites (step 18a: white diamonds with their numbers, where the player may found a colony).
/// One map cell is one unit here, so the map is Size units wide and the camera zooms in on it.
/// </summary>
public partial class MapView : Node2D
{
    readonly ExportedWorld world;
    readonly bool[] land;
    readonly byte[] borderBytes;
    readonly Dictionary<string, Texture2D?> sprites = new();
    readonly float side;
    Sprite2D? borders;
    ImageTexture? borderTexture;
    Node2D? townLayer;
    List<TownDrawing> towns = new();

    public int Year { get; private set; } = -1;

    public MapView() : this(null!) { }

    public MapView(ExportedWorld world)
    {
        this.world = world;
        land = world == null ? Array.Empty<bool>() : Pictures.Land(world);
        borderBytes = new byte[world == null ? 0 : world.Size * world.Size * 4];
        side = world == null ? 10 : (float)MapYear.SpriteSide(world.Size);
    }

    public override void _Ready()
    {
        var terrain = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Nearest };
        var image = Image.LoadFromFile(world.PathOf("terrain.png"));
        if (image != null)
            terrain.Texture = ImageTexture.CreateFromImage(image);
        else
            GD.PushWarning("This world has no terrain.png: export it again with the newer generator.");
        AddChild(terrain);

        borders = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Nearest };
        AddChild(borders);
        // A node draws itself before its children, so the towns get a child of their own, added last: on top.
        townLayer = new Node2D();
        townLayer.Draw += DrawTowns;
        AddChild(townLayer);
        ShowYear(world.Years);
    }

    /// <summary>Called when the player clicks a town on the map (the topmost town under the mouse).</summary>
    public event Action<TownDrawing>? TownClicked;

    /// <summary>Called when the player clicks a founding site on the map (a site is picked before a town under it).</summary>
    public event Action<FoundingSite>? SiteClicked;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            return;
        var point = GetGlobalMousePosition();
        if (SiteAt(point) is FoundingSite site)
            SiteClicked?.Invoke(site);
        else if (TownAt(point) is TownDrawing town)
            TownClicked?.Invoke(town);
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    // How big a site's diamond is, in map cells: at least a cell and a half, so it can be clicked when zoomed out.
    float SiteSize => Math.Max(1.5f, side * 0.6f);

    /// <summary>The founding site whose diamond is at map point `point`, or null.</summary>
    public FoundingSite? SiteAt(Vector2 point) =>
        world.FoundingSites.FirstOrDefault(s => Math.Abs(point.X - (s.X + 0.5f)) + Math.Abs(point.Y - (s.Y + 0.5f)) <= SiteSize);

    /// <summary>The town drawn at map point `point` in the year shown (the topmost, as drawn last), or null.</summary>
    public TownDrawing? TownAt(Vector2 point) => MapYear.TownAt(towns, point.X, point.Y, side);

    /// <summary>Shows `year`: the borders and towns as they were then.</summary>
    public void ShowYear(int year)
    {
        Year = Math.Clamp(year, 0, world.Years);
        Redraw();
    }

    /// <summary>Draws the current year again (after a setting changed).</summary>
    public void Redraw()
    {
        Pictures.BordersInto(borderBytes, world, world.Borders(Year), land, (byte)Settings.BorderAlpha);
        var image = Image.CreateFromData(world.Size, world.Size, false, Image.Format.Rgba8, borderBytes);
        if (borderTexture == null)
        {
            borderTexture = ImageTexture.CreateFromImage(image);
            borders!.Texture = borderTexture;
        }
        else
            borderTexture.Update(image);        // the same size every year, so the picture is replaced in place
        towns = MapYear.Towns(world, Year);
        townLayer?.QueueRedraw();
    }

    // Draws every town on townLayer (its Draw signal calls this whenever it is asked to redraw), then the
    // founding sites over them.
    void DrawTowns()
    {
        var layer = townLayer!;
        DrawTownPictures(layer);
        var font = ThemeDB.FallbackFont;
        float size = SiteSize;
        foreach (var site in world.FoundingSites)
        {
            var middle = new Vector2(site.X + 0.5f, site.Y + 0.5f);
            var diamond = new[] { middle + new Vector2(0, -size), middle + new Vector2(size, 0), middle + new Vector2(0, size), middle + new Vector2(-size, 0) };
            layer.DrawColoredPolygon(diamond, new Color(0.98f, 0.97f, 0.92f));
            layer.DrawPolyline(diamond.Append(diamond[0]).ToArray(), new Color(0.1f, 0.1f, 0.1f), size * 0.15f);
            // The number, at a size of its own (fonts are drawn in pixels, so it is drawn small and scaled up).
            string number = site.Site.ToString();
            float scale = size / 12f;
            layer.DrawSetTransform(middle, 0, new Vector2(scale, scale));
            float width = font.GetStringSize(number, HorizontalAlignment.Left, -1, 14).X;
            layer.DrawString(font, new Vector2(-width / 2, 5), number, HorizontalAlignment.Left, -1, 14, new Color(0.1f, 0.1f, 0.1f));
            layer.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    void DrawTownPictures(Node2D layer)
    {
        foreach (var town in towns)
        {
            var picture = Sprite(town);
            var middle = new Vector2(town.X + 0.5f, town.Y + 0.5f);
            if (picture != null)
                layer.DrawTextureRect(picture, new Rect2(middle - new Vector2(side, side) / 2, new Vector2(side, side)), false);
            else
            {
                // No picture for its set: a dot in its faction's colour (a ring for a ruin), as the atlas draws it.
                var colour = new Color(0.53f, 0.53f, 0.53f);
                if (town.Faction is int f && f < world.Factions.Count)
                    colour = Color.Color8(world.Factions[f].Red, world.Factions[f].Green, world.Factions[f].Blue);
                float radius = Math.Max(2, side / 6);
                if (!town.Ruined)
                    layer.DrawCircle(middle, radius, colour);
                layer.DrawArc(middle, radius, 0, Mathf.Tau, 24, new Color(0.12f, 0.12f, 0.12f), 0.5f);
            }
        }
    }

    // The first of the town's picture files that exists, loaded once and kept (ruins dimmed as the generator dims them).
    Texture2D? Sprite(TownDrawing town)
    {
        foreach (var file in MapYear.SpriteFiles(world, town))
        {
            string key = file + (town.Ruined ? "#dim" : "");
            if (!sprites.TryGetValue(key, out var texture))
            {
                texture = null;
                string path = world.PathOf(file);
                if (File.Exists(path) && Image.LoadFromFile(path) is Image image)
                {
                    if (town.Ruined && world.RuinDim > 0)
                    {
                        image.Convert(Image.Format.Rgba8);
                        image = Image.CreateFromData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8,
                                                     Pictures.Dimmed(image.GetData(), world.RuinDim));
                    }
                    texture = ImageTexture.CreateFromImage(image);
                }
                sprites[key] = texture;
            }
            if (texture != null)
                return texture;
        }
        return null;
    }
}
