// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The resource test (step A3): the test pictures of piles and containers (resource asset library TEST v0.1,
/// `res://assets/prototype-resources-v0.1/`), shrunk ahead of time to the game's scale, on a board to the right of the
/// fields. K goes there and back.
///
/// Each kind has four pictures, small, medium, large and full, for how full its tile is (library Review 45, D3). Each
/// stands on its own tile, its bottom edge on the tile's bottom edge, as goods will lie on a stockpile tile. The board
/// shows every kind twice: on grass (left) and on dark ground (right), since the pictures' edges were cut out of a
/// light background. A v3 tree and a colonist stand at its top for scale.
///
/// J switches between the two sizes made from each picture:
/// - compact: 32 pixels across a tile, as the trees are (the Vegetation Art Standard's scale);
/// - fine: 64 pixels across a tile, drawn at the same size, to see what twice the detail would give when zoomed in.
/// </summary>
public partial class AssetPreview
{
    // The kinds, in the board's order, two to a row: wood and storage, stone and ore, food.
    static readonly string[] ResourceKinds =
    {
        "timber", "lumber", "crate", "barrel", "stone", "rubble", "coal", "iron_ore", "copper_ore", "grain", "flour",
        "harvested_crops", "vegetables", "fruit",
    };
    static readonly string[] ResourceStates = { "small", "medium", "large", "full" };
    const string ResourceFolder = "res://assets/prototype-resources-v0.1/";
    const int BoardLeft = 2 * Wide + Gap + 3;             // the board's left edge, in tiles, right of the two fields
    const int BoardRow = 2;                               // tiles from one kind's row to the next
    const int BoardHalf = 10;                             // tiles across one half: grass, then dark ground

    bool board;                                           // K: the camera is on the board
    bool fine;                                            // J: the 64-pixel pictures instead of the 32-pixel ones
    Vector2? fieldCamera;                                 // where the camera was before going to the board

    void LoadResources()
    {
        foreach (var size in new[] { "compact", "fine" })
            foreach (var kind in ResourceKinds)
                foreach (var state in ResourceStates)
                    LoadAs(size + "/" + kind + "_" + state, ResourceFolder + size + "/" + kind + "_" + state + ".png");
    }

    // K: to the board and back.
    void ToggleBoard()
    {
        board = !board;
        if (board)
        {
            fieldCamera = camera!.Position;
            camera.Position = new Vector2(BoardLeft + BoardHalf, ResourceKinds.Length / 2 * BoardRow / 2f + 3);
        }
        else if (fieldCamera is Vector2 back)
            camera!.Position = back;
    }

    string ResourceLine() =>
        "Resources (K): " + (fine ? "fine, 64 px a tile" : "compact, 32 px a tile") + " (J) · left on grass, right on dark ground · "
        + "rows: " + string.Join(", ", ResourceKinds.Select(k => k.Replace('_', ' '))) + " · small, medium, large, full";

    // The board: two halves of ground, each kind's four pictures on both, a tree and a colonist for scale, and names.
    void DrawBoard()
    {
        int rows = (ResourceKinds.Length + 1) / 2;
        int high = rows * BoardRow + 6;
        for (int y = 0; y < high; y++)
            for (int x = 0; x < 2 * BoardHalf; x++)
            {
                var tile = new Rect2(BoardLeft + x, y, 1, 1);
                if (x < BoardHalf && Picture("surfaces/grass_0" + Variant(x, y)) is Texture2D grass)
                    DrawTextureRect(grass, tile, false);
                else
                    DrawRect(tile, x < BoardHalf ? ColonyView.ColourOf("grass") : ColonyView.ColourOf("mud"));
            }
        string size = fine ? "fine" : "compact";
        float pixels = fine ? 64 : 32;
        var font = ThemeDB.FallbackFont;
        for (int i = 0; i < ResourceKinds.Length; i++)
        {
            int row = i / 2, column = i % 2;
            float bottom = 4 + row * BoardRow + 1;          // the tiles' bottom edge in this row, below the tree
            foreach (int half in new[] { 0, BoardHalf })
            {
                float left = BoardLeft + half + column * 5;
                for (int s = 0; s < ResourceStates.Length; s++)
                    if (Picture(size + "/" + ResourceKinds[i] + "_" + ResourceStates[s]) is Texture2D texture)
                    {
                        float wide = texture.GetWidth() / pixels, tall = texture.GetHeight() / pixels;
                        float middle = left + 1 + s + 0.5f;
                        DrawTextureRect(texture, new Rect2(middle - wide / 2, bottom - tall, wide, tall), false);
                    }
                // The kind's name under its row, drawn small: 1/32 of a tile a font pixel, 10 pixels high.
                DrawSetTransform(new Vector2(left + 1, bottom + 0.4f), 0, new Vector2(1 / 32f, 1 / 32f));
                DrawString(font, Vector2.Zero, ResourceKinds[i].Replace('_', ' '), HorizontalAlignment.Left, -1, 10,
                           half == 0 ? Colors.White : new Color(0.9f, 0.85f, 0.7f));
                DrawSetTransform(Vector2.Zero);
            }
        }
        // For scale: the colony's deciduous tree and a colonist at the board's top, on each half.
        foreach (int half in new[] { 0, BoardHalf })
        {
            DrawColonist(new Vector2(BoardLeft + half + 7.5f, 2.6f), true);
            DrawTree(new Vector2(BoardLeft + half + 8.5f, 3), 1, true);
        }
    }
}
