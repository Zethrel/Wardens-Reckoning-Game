// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.World;

namespace WardensReckoning.UI;

/// <summary>
/// The map of one year: the terrain, the borders over the land, and the towns with their pictures.
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

    // Draws every town on townLayer (its Draw signal calls this whenever it is asked to redraw).
    void DrawTowns()
    {
        var layer = townLayer!;
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
