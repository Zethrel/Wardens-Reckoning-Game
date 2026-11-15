// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The people test (step A3e): the first pictures of the peoples a colony can have (Peoples Batch 1, TEST v0.1,
/// `res://assets/prototype-people-v0.1/`), on their own patch of ground to the right of the resource board. Q goes
/// there and back.
///
/// Each people has one picture, standing and facing south, in two sizes: a 48 × 48 canvas and a 64 × 64 one. Both are
/// drawn at the same size in the world, 1.5 tiles square, so the 64-pixel picture shows more detail and is not
/// bigger: the 48-pixel one at 32 pixels a tile (the trees' scale), the 64-pixel one at 42.7. Each stands with the
/// lowest pixel of its feet on the middle of its tile's bottom edge, one people to a tile.
///
/// - The first two rows: all eight at 48 and at 64, with the prototype colonist and a v3 tree for scale.
/// - Then the 48 or 64 pictures (Z) on dark ground and on snow, to see their edges.
/// - Last, a small wood: the peoples standing among trees, behind and in front of them, and next to each other, all
///   drawn in the order of where their feet are, as in the colony.
/// </summary>
public partial class AssetPreview
{
    static readonly string[] Peoples = { "human", "dwarf", "elf", "halfling", "gnome", "orc", "goblin", "kobold" };
    // Each picture's lowest solid row, plus one: where its feet stand. The package's one feet point fits only the elf;
    // the others would float 2 to 10 pixels (see the folder's README).
    static readonly Dictionary<string, int> PeopleFeet = new()
    {
        ["human_48"] = 38, ["human_64"] = 52, ["dwarf_48"] = 38, ["dwarf_64"] = 52, ["elf_48"] = 45, ["elf_64"] = 61,
        ["halfling_48"] = 41, ["halfling_64"] = 56, ["gnome_48"] = 40, ["gnome_64"] = 55, ["orc_48"] = 38, ["orc_64"] = 51,
        ["goblin_48"] = 39, ["goblin_64"] = 53, ["kobold_48"] = 43, ["kobold_64"] = 59,
    };
    const int PeopleLeft = BoardLeft + 2 * BoardHalf + 3;    // the people's patch, right of the resource board
    const int PeopleWide = 20, PeopleHigh = 26;
    const float WorldCanvas = 1.5f;                            // tiles across either canvas

    bool people;                                               // Q: the camera is on the people's patch
    bool large;                                                // Z: the 64-pixel pictures in the lower rows and the wood
    Vector2? peopleCamera;

    void LoadPeople()
    {
        foreach (var size in new[] { 48, 64 })
            foreach (var p in Peoples)
                LoadAs("people/" + size + "/" + p, "res://assets/prototype-people-v0.1/" + size + "/" + p + ".png");
    }

    // Q: to the people's patch and back.
    void TogglePeople()
    {
        people = !people;
        if (people)
        {
            peopleCamera = camera!.Position;
            camera.Position = new Vector2(PeopleLeft + PeopleWide / 2f, PeopleHigh / 2f);
        }
        else if (peopleCamera is Vector2 back)
            camera!.Position = back;
    }

    string PeopleLine() => "People (Q): rows at 48 and 64 px, both 1.5 tiles across; lower rows and the wood at " + (large ? "64" : "48") + " (Z)";

    // One people standing with their feet on `feet`: the picture of `size` pixels, 1.5 tiles across whatever its size.
    void DrawPerson(string name, int size, Vector2 feet)
    {
        if (Picture("people/" + size + "/" + name) is not Texture2D texture)
            return;
        float pixels = size / WorldCanvas;                         // picture pixels a tile: 32 or 42.7
        float footRow = PeopleFeet[name + "_" + size];
        var rect = new Rect2(feet.X - size / 2f / pixels, feet.Y - footRow / pixels, size / pixels, size / pixels);
        DrawTextureRect(texture, rect, false);
        if (anchors)
            Cross(feet);
    }

    void DrawPeople()
    {
        var font = ThemeDB.FallbackFont;
        // The ground: grass, then a band of dark ground and a band of snow, then grass for the wood.
        for (int y = 0; y < PeopleHigh; y++)
            for (int x = 0; x < PeopleWide; x++)
            {
                var tile = new Rect2(PeopleLeft + x, y, 1, 1);
                if (y >= 8 && y < 11)
                    DrawRect(tile, ColonyView.ColourOf("mud"));
                else if (y >= 11 && y < 14)
                    DrawRect(tile, ColonyView.ColourOf("snow"));
                else if (Picture("surfaces/grass_0" + Variant(x, y)) is Texture2D grass)
                    DrawTextureRect(grass, tile, false);
                else
                    DrawRect(tile, ColonyView.ColourOf("grass"));
            }
        // Rows: (bottom edge of the tiles, size, label).
        var rows = new (float Bottom, int Size, string Label)[]
        {
            (3, 48, "48 × 48, at 32 px a tile"), (6, 64, "64 × 64, at 42.7 px a tile: the same size in the world"),
            (10, large ? 64 : 48, "on dark ground"), (13, large ? 64 : 48, "on snow"),
        };
        foreach (var (bottom, size, label) in rows)
        {
            for (int i = 0; i < Peoples.Length; i++)
                DrawPerson(Peoples[i], size, new Vector2(PeopleLeft + 1.5f + 2 * i, bottom));
            DrawSetTransform(new Vector2(PeopleLeft + 1, bottom + 0.35f), 0, new Vector2(1 / 32f, 1 / 32f));
            DrawString(font, Vector2.Zero, label + " · " + string.Join(", ", Peoples), HorizontalAlignment.Left, -1, 10,
                       bottom is 10 or 13 ? new Color(0.95f, 0.9f, 0.75f) : Colors.White);
            DrawSetTransform(Vector2.Zero);
        }
        // For scale beside the first two rows: the prototype colonist and a v3 tree.
        DrawColonist(new Vector2(PeopleLeft + 17.5f, 3), true);
        DrawTree(new Vector2(PeopleLeft + 18.5f, 6), 1, true);

        // The wood: four trees, the peoples around them, everything in the order of its feet.
        int top = 15;
        var standing = new List<(float Feet, Action Draw)>();
        foreach (var (tx, ty, kind) in new[] { (4, 3, 1), (9, 3, 2), (14, 4, 1), (6, 7, 2) })
        {
            var foot = new Vector2(PeopleLeft + tx + 0.5f, top + ty + 1);
            standing.Add((foot.Y, () => DrawTree(foot, kind, true)));
        }
        // Who stands where (tile x, tile y): half behind a tree (a row above its foot, a tile to the side), just in front
        // (a row below), and side by side on neighbouring tiles. Straight behind a tree's crown, a people can't be seen at all.
        var spots = new (int X, int Y)[] { (5, 2), (8, 2), (14, 5), (6, 8), (10, 8), (11, 8), (12, 8), (13, 8) };
        for (int i = 0; i < Peoples.Length; i++)
        {
            string who = Peoples[i];
            var feet = new Vector2(PeopleLeft + spots[i].X + 0.5f, top + spots[i].Y + 1);
            standing.Add((feet.Y, () => DrawPerson(who, large ? 64 : 48, feet)));
        }
        foreach (var (_, draw) in standing.OrderBy(s => s.Feet))
            draw();
        DrawSetTransform(new Vector2(PeopleLeft + 1, top + 10.35f), 0, new Vector2(1 / 32f, 1 / 32f));
        DrawString(font, Vector2.Zero, "among trees, at " + (large ? 64 : 48) + ": human and dwarf half behind trees, elf and halfling just in front, gnome to kobold side by side",
                   HorizontalAlignment.Left, -1, 10, Colors.White);
        DrawSetTransform(Vector2.Zero);
    }
}
