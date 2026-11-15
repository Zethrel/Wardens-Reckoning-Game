// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The resource test (steps A3 and A3b): the test pictures of piles and containers (resource asset library TEST v0.1,
/// batches 1 to 6, `res://assets/prototype-resources-v0.1/`; and the refinements of batches 2 and 3, TEST v0.2,
/// `res://assets/prototype-resources-v0.2/`), shrunk ahead of time to the game's scale, on a board to the right of the
/// fields. K goes there and back; L shows the refined pictures (v0.2) of stone, grain, flour, vegetables and fruit in
/// place of v0.1's.
///
/// Below them, three rows from TEST v0.3 (`res://assets/prototype-resources-v0.3/`, step A3c): the empty containers
/// with the cleaned-up mortar; the contents on their own; and the contents laid over the containers, put together ahead
/// of time without any masking, to see what a front rim that hides part of the contents would need. U takes off the
/// white outline these pictures came with.
///
/// Last, three rows from the release candidate (v1.0 RC, `res://assets/prototype-resources-rc/`, step A3d): its four
/// sizes of clean mortar and its cooler steel; the containers drawn in three layers in the game, the empty container,
/// then the contents, then the container's front piece over them, as the RC's README suggests; and the same without
/// the front piece, to compare. U takes off the white outline here too.
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
    // The kinds, in the board's order, two to a row: wood and storage (batch 1), stone and ore (2), food (3),
    // building materials (4), metals and tools (5), more storage (6).
    static readonly string[] ResourceKinds =
    {
        "timber", "lumber", "crate", "barrel", "stone", "rubble", "coal", "iron_ore", "copper_ore", "grain", "flour",
        "harvested_crops", "vegetables", "fruit", "bricks", "clay", "cut_stone", "mortar", "sand", "copper_ingots",
        "bronze_ingots", "iron_ingots", "steel_ingots", "tools", "sacks", "baskets", "shelves", "timber_rack",
    };
    // The kinds that TEST v0.2 drew again (L): loose grain and flour, no sacks; clearer vegetables, fruit and stone.
    static readonly string[] RefinedKinds = { "stone", "grain", "flour", "vegetables", "fruit" };
    static readonly string[] ResourceStates = { "small", "medium", "large", "full" };
    const string ResourceFolder = "res://assets/prototype-resources-v0.1/";
    const int BoardLeft = 2 * Wide + Gap + 3;             // the board's left edge, in tiles, right of the two fields
    const int BoardRow = 2;                               // tiles from one kind's row to the next
    const int BoardHalf = 10;                             // tiles across one half: grass, then dark ground

    bool board;                                           // K: the camera is on the board
    bool fine;                                            // J: the 64-pixel pictures instead of the 32-pixel ones
    bool refined;                                         // L: TEST v0.2's pictures for the kinds it drew again
    bool outlineOff;                                      // U: v0.3's pictures without their white outline

    // TEST v0.3's three rows: the empty containers and the clean mortar; the contents; contents in containers.
    static readonly string[][] V03Rows =
    {
        new[] { "basket_empty", "sack_empty", "shelves_empty", "timber_rack_empty", "mortar" },
        new[] { "grain", "flour", "timber", "lumber", "vegetables", "fruit", "tools" },
        new[] { "basket_with_fruit", "basket_with_vegetables", "sack_with_grain", "sack_with_flour", "shelves_with_tools",
                "timber_rack_with_timber", "timber_rack_with_lumber" },
    };
    static readonly string[] V03Names = { "v0.3: empty containers, clean mortar", "v0.3: contents", "v0.3: contents in containers (no masking)" };

    // The release candidate: its new piles, and its containers with their contents, as (container, contents).
    static readonly string[] RcPiles = { "mortar", "steel_ingots_cool" };
    static readonly (string Container, string Contents)[] RcLayered =
    {
        ("basket", "fruit"), ("basket", "vegetables"), ("sack", "grain"), ("sack", "flour"), ("shelves", "tools"),
        ("timber_rack", "timber"), ("timber_rack", "lumber"),
    };
    static readonly string[] RcNames = { "RC: clean mortar, cooler steel", "RC: in three layers: container, contents, front", "RC: without the front" };
    Vector2? fieldCamera;                                 // where the camera was before going to the board

    void LoadResources()
    {
        foreach (var size in new[] { "compact", "fine" })
            foreach (var kind in ResourceKinds)
                foreach (var state in ResourceStates)
                    LoadAs(size + "/" + kind + "_" + state, ResourceFolder + size + "/" + kind + "_" + state + ".png");
        foreach (var size in new[] { "compact", "fine" })
            foreach (var look in new[] { "outlined", "clean" })
                foreach (var name in V03Rows.SelectMany(r => r))
                    LoadAs("v0.3/" + size + "/" + look + "/" + name, "res://assets/prototype-resources-v0.3/" + size + "/" + look + "/" + name + ".png");
        foreach (var size in new[] { "compact", "fine" })
            foreach (var look in new[] { "outlined", "clean" })
            {
                string at = "res://assets/prototype-resources-rc/" + size + "/" + look + "/";
                var names = RcPiles.SelectMany(p => ResourceStates.Select(s => p + "_" + s))
                    .Concat(RcLayered.SelectMany(l => new[] { l.Container + "_base", l.Container + "_front", l.Container + "_with_" + l.Contents }))
                    .Distinct();
                foreach (var name in names)
                    LoadAs("rc/" + size + "/" + look + "/" + name, at + name + ".png");
            }
        foreach (var size in new[] { "compact", "fine" })
            foreach (var kind in RefinedKinds)
                foreach (var state in ResourceStates)
                    LoadAs("v0.2/" + size + "/" + kind + "_" + state, "res://assets/prototype-resources-v0.2/" + size + "/" + kind + "_" + state + ".png");
    }

    // The picture of a kind in a state, at the size shown: from v0.2 when L is on and v0.2 drew that kind again.
    Texture2D? ResourcePicture(string kind, string state)
    {
        string name = (fine ? "fine" : "compact") + "/" + kind + "_" + state;
        return refined && RefinedKinds.Contains(kind) ? Picture("v0.2/" + name) : Picture(name);
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
        "Resources (K): " + (fine ? "fine, 64 px a tile" : "compact, 32 px a tile") + " (J) · "
        + (refined ? "v0.2 for " + string.Join(", ", RefinedKinds) : "all v0.1") + " (L) · left on grass, right on dark ground · "
        + "each row small, medium, large, full · v0.3 below, outline " + (outlineOff ? "off" : "on") + " (U)";

    // The board: two halves of ground, each kind's four pictures on both, a tree and a colonist for scale, and names.
    void DrawBoard()
    {
        int rows = (ResourceKinds.Length + 1) / 2;
        int high = rows * BoardRow + 6 + (V03Rows.Length + RcNames.Length) * BoardRow;
        for (int y = 0; y < high; y++)
            for (int x = 0; x < 2 * BoardHalf; x++)
            {
                var tile = new Rect2(BoardLeft + x, y, 1, 1);
                if (x < BoardHalf && Picture("surfaces/grass_0" + Variant(x, y)) is Texture2D grass)
                    DrawTextureRect(grass, tile, false);
                else
                    DrawRect(tile, x < BoardHalf ? ColonyView.ColourOf("grass") : ColonyView.ColourOf("mud"));
            }
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
                    if (ResourcePicture(ResourceKinds[i], ResourceStates[s]) is Texture2D texture)
                    {
                        float wide = texture.GetWidth() / pixels, tall = texture.GetHeight() / pixels;
                        float middle = left + 1 + s + 0.5f;
                        DrawTextureRect(texture, new Rect2(middle - wide / 2, bottom - tall, wide, tall), false);
                    }
                // The kind's name under its row, drawn small: 1/32 of a tile a font pixel, 10 pixels high.
                DrawSetTransform(new Vector2(left + 1, bottom + 0.4f), 0, new Vector2(1 / 32f, 1 / 32f));
                string name = ResourceKinds[i].Replace('_', ' ') + (refined && RefinedKinds.Contains(ResourceKinds[i]) ? " (v0.2)" : "");
                DrawString(font, Vector2.Zero, name, HorizontalAlignment.Left, -1, 10,
                           half == 0 ? Colors.White : new Color(0.9f, 0.85f, 0.7f));
                DrawSetTransform(Vector2.Zero);
            }
        }
        // TEST v0.3's rows, under the others: one picture a tile, side by side.
        for (int r = 0; r < V03Rows.Length; r++)
        {
            float bottom = 4 + rows * BoardRow + r * BoardRow + 1;
            foreach (int half in new[] { 0, BoardHalf })
            {
                float left = BoardLeft + half + 1;
                for (int k = 0; k < V03Rows[r].Length; k++)
                    if (Picture("v0.3/" + (fine ? "fine" : "compact") + "/" + (outlineOff ? "clean" : "outlined") + "/" + V03Rows[r][k]) is Texture2D texture)
                    {
                        float wide = texture.GetWidth() / pixels, tall = texture.GetHeight() / pixels;
                        DrawTextureRect(texture, new Rect2(left + k + 0.5f - wide / 2, bottom - tall, wide, tall), false);
                    }
                DrawSetTransform(new Vector2(left, bottom + 0.4f), 0, new Vector2(1 / 32f, 1 / 32f));
                DrawString(font, Vector2.Zero, V03Names[r], HorizontalAlignment.Left, -1, 10, half == 0 ? Colors.White : new Color(0.9f, 0.85f, 0.7f));
                DrawSetTransform(Vector2.Zero);
            }
        }
        // The release candidate's rows, under v0.3's.
        string rc = "rc/" + (fine ? "fine" : "compact") + "/" + (outlineOff ? "clean" : "outlined") + "/";
        for (int r = 0; r < RcNames.Length; r++)
        {
            float bottom = 4 + rows * BoardRow + (V03Rows.Length + r) * BoardRow + 1;
            foreach (int half in new[] { 0, BoardHalf })
            {
                float left = BoardLeft + half + 1;
                // What goes on each tile of the row, in the order it is drawn.
                var tiles = r == 0
                    ? RcPiles.SelectMany((p, i) => ResourceStates.Select((s, k) => (Tile: i * 5 + k, Layers: new[] { p + "_" + s }))).ToList()
                    : RcLayered.Select((l, k) => (Tile: k, Layers: r == 1
                        ? new[] { l.Container + "_base", l.Container + "_with_" + l.Contents, l.Container + "_front" }
                        : new[] { l.Container + "_base", l.Container + "_with_" + l.Contents })).ToList();
                foreach (var (tile, layers) in tiles)
                    foreach (var layer in layers)
                        if (Picture(rc + layer) is Texture2D texture)
                        {
                            float wide = texture.GetWidth() / pixels, tall = texture.GetHeight() / pixels;
                            DrawTextureRect(texture, new Rect2(left + tile + 0.5f - wide / 2, bottom - tall, wide, tall), false);
                        }
                DrawSetTransform(new Vector2(left, bottom + 0.4f), 0, new Vector2(1 / 32f, 1 / 32f));
                DrawString(font, Vector2.Zero, RcNames[r], HorizontalAlignment.Left, -1, 10, half == 0 ? Colors.White : new Color(0.9f, 0.85f, 0.7f));
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
