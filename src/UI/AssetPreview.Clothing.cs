// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The clothing test (step A3i): the clothing-layer trial (Clothing Overlay Trial 01, interchange revision 2;
/// `res://assets/prototype-clothing-v0.1/`), on its own patch of ground right of the people patch. C goes there and back.
///
/// The adopted Human v5, Dwarf v5 and Halfling v7 are taken apart into layers, drawn on top of each other in this
/// order: the clothing-free body (in underclothes), the trousers, the tunic, the boots, and last the head and beard,
/// so a beard always hangs over whatever tunic is worn. Each layer is a 32 × 48 picture, drawn 1 × 1.5 tiles with its
/// bottom edge on the feet, so they all line up.
///
/// Each people stands five times: bare (in underclothes), in its own outfit, in the green tunic, in the blue-grey
/// trousers, and in both. Its own outfit drawn from layers must look exactly like the adopted sprite.
///
/// The rows are grass, dark ground and snow, and last a small wood where the peoples stand among trees in the
/// green tunic and blue-grey trousers. Nothing here is adopted: this is for the maintainer to judge.
/// </summary>
public partial class AssetPreview
{
    // The three peoples' folders, and how each outfit is put together: (name, trousers layer, tunic layer).
    static readonly string[] Dressed = { "human_v5", "dwarf_v5", "halfling_v7" };
    static readonly (string Name, string? Lower, string? Upper)[] Outfits =
    {
        ("bare", null, null),
        ("own", "runtime_reference_lower", "reference_upper"),
        ("tunic", "runtime_reference_lower", "alternate_upper"),
        ("trousers", "alternate_lower", "reference_upper"),
        ("both", "alternate_lower", "alternate_upper"),
    };
    static readonly string[] ClothingLayers =
        { "neutral", "runtime_reference_lower", "reference_upper", "alternate_upper", "alternate_lower", "boots", "head" };

    const int ClothLeft = PeopleLeft + PeopleWide + 3;          // the clothing patch, right of the people patch
    const int ClothWide = 36, ClothHigh = 19;

    bool clothing;                                               // C: the camera is on the clothing patch
    Vector2? clothingCamera;

    void LoadClothing()
    {
        foreach (var who in Dressed)
            foreach (var layer in ClothingLayers)
                LoadAs("clothing/" + who + "/" + layer, "res://assets/prototype-clothing-v0.1/" + who + "/" + layer + ".png");
    }

    // C: to the clothing patch and back.
    void ToggleClothing()
    {
        clothing = !clothing;
        if (clothing)
        {
            clothingCamera = camera!.Position;
            camera.Position = new Vector2(ClothLeft + ClothWide / 2f, ClothHigh / 2f);
        }
        else if (clothingCamera is Vector2 back)
            camera!.Position = back;
    }

    string ClothingLine() => "Clothing (C): human, dwarf, halfling · bare, own outfit, green tunic, blue-grey trousers, both · grass, dark ground, snow, wood";

    // One people in one outfit, standing on `feet`: the layers drawn in order, each 1 × 1.5 tiles.
    void DrawDressed(string who, (string Name, string? Lower, string? Upper) outfit, Vector2 feet)
    {
        var rect = new Rect2(feet.X - 0.5f, feet.Y - 1.5f, 1, 1.5f);
        foreach (string? layer in new[] { "neutral", outfit.Lower, outfit.Upper, "boots", "head" })
        {
            // The bare body wears no boots.
            if (layer == null || (layer == "boots" && outfit.Lower == null))
                continue;
            if (Picture("clothing/" + who + "/" + layer) is Texture2D texture)
                DrawTextureRect(texture, rect, false);
        }
        if (anchors)
            Cross(feet);
    }

    void DrawClothing()
    {
        var font = ThemeDB.FallbackFont;
        // The ground: grass, a band of dark ground, a band of snow, then grass for the wood.
        for (int y = 0; y < ClothHigh; y++)
            for (int x = 0; x < ClothWide; x++)
            {
                var tile = new Rect2(ClothLeft + x, y, 1, 1);
                if (y >= 4 && y < 8)
                    DrawRect(tile, ColonyView.ColourOf("mud"));
                else if (y >= 8 && y < 12)
                    DrawRect(tile, ColonyView.ColourOf("snow"));
                else if (Picture("surfaces/grass_0" + Variant(x, y)) is Texture2D grass)
                    DrawTextureRect(grass, tile, false);
                else
                    DrawRect(tile, ColonyView.ColourOf("grass"));
            }
        // Three rows, one for each ground: each people in its five outfits, two tiles apart, a gap between peoples.
        foreach (float bottom in new[] { 3f, 7f, 11f })
        {
            for (int p = 0; p < Dressed.Length; p++)
                for (int o = 0; o < Outfits.Length; o++)
                    DrawDressed(Dressed[p], Outfits[o], new Vector2(ClothLeft + 1.5f + 12 * p + 2 * o, bottom));
            DrawSetTransform(new Vector2(ClothLeft + 1, bottom + 0.35f), 0, new Vector2(1 / 32f, 1 / 32f));
            DrawString(font, Vector2.Zero, "human · dwarf · halfling, each: bare, own outfit, green tunic, blue-grey trousers, both",
                       HorizontalAlignment.Left, -1, 10, bottom == 11 ? new Color(0.3f, 0.25f, 0.2f) : Colors.White);
            DrawSetTransform(Vector2.Zero);
        }
        // The wood: a deciduous tree and a conifer with the peoples around them, everything in the order of its feet.
        int top = 12;
        var standing = new List<(float Feet, Action Draw)>();
        foreach (var (tx, ty, kind) in new[] { (5, 3, 1), (12, 3, 2), (20, 4, 1) })
        {
            var foot = new Vector2(ClothLeft + tx + 0.5f, top + ty + 1);
            standing.Add((foot.Y, () => DrawTree(foot, kind, true)));
        }
        // Behind and beside the trees (a row above their feet), and in front of them (a row below).
        var spots = new (int X, int Y, int Who, int Outfit)[]
        {
            (6, 2, 0, 4), (11, 2, 1, 4), (13, 3, 2, 4), (4, 5, 1, 2), (8, 5, 2, 3), (19, 5, 0, 1), (21, 5, 2, 1), (16, 5, 0, 4),
        };
        foreach (var s in spots)
        {
            var feet = new Vector2(ClothLeft + s.X + 0.5f, top + s.Y + 1);
            var (who, outfit) = (Dressed[s.Who], Outfits[s.Outfit]);
            standing.Add((feet.Y, () => DrawDressed(who, outfit, feet)));
        }
        foreach (var (_, draw) in standing.OrderBy(s => s.Feet))
            draw();
    }
}
