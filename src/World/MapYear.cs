// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.World;

/// <summary>
/// How one settlement is drawn in a year.
/// `Faction` is whose pictures it uses (its holder, or for a ruin the last faction that held it), or null;
/// `Size` the size's name (also the picture's name); `Capital` whether it is a living faction's capital,
/// drawn with that faction's own pictures.
/// </summary>
public sealed record TownDrawing(int Index, int X, int Y, int? Faction, string Size, bool Ruined, bool Capital);

/// <summary>
/// What the map shows in one year, worked out the same way as the generator's history atlas
/// (the JavaScript in north/atlas.py), so the game and the atlas show a year alike.
/// </summary>
public static class MapYear
{
    /// <summary>The settlements standing (or lying in ruins) in `year`, the largest first, so that small
    /// towns are drawn last and stay visible on top of large ones.</summary>
    public static List<TownDrawing> Towns(ExportedWorld world, int year)
    {
        var capitals = Capitals(world, year);
        var order = Enumerable.Range(0, world.Towns.Count).Where(i => world.Towns[i].From <= year).ToList();
        // OrderBy keeps towns of the same peak in their order in the list, as the atlas's sort does.
        order = order.OrderByDescending(i => ExportedWorld.PeakTo(world.Towns[i], year)).ToList();

        var drawn = new List<TownDrawing>();
        foreach (int i in order)
        {
            var town = world.Towns[i];
            var state = world.Town(i, year)!;
            int? builder = state.Holder;
            if (state.Ruined)
            {
                // A ruin is drawn with the pictures of the last faction that held it.
                foreach (var (since, faction) in town.Owners)
                    if (since <= year && faction != null)
                        builder = faction;
            }
            int people = state.Ruined ? ExportedWorld.PeakTo(town, year) : state.People;
            drawn.Add(new TownDrawing(i, town.X, town.Y, builder, world.SizeOf(people), state.Ruined,
                                      !state.Ruined && builder != null && capitals.Contains(i)));
        }
        return drawn;
    }

    /// <summary>The settlements that are a living faction's capital in `year`.</summary>
    public static HashSet<int> Capitals(ExportedWorld world, int year)
    {
        var capitals = new HashSet<int>();
        for (int f = 0; f < world.Factions.Count; f++)
            if (world.PeopleOf(f, year) > 0 && world.CapitalOf(f, year) is int capital)
                capitals.Add(capital);
        return capitals;
    }

    /// <summary>The factions alive in `year` (people above 0), the most people first.</summary>
    public static List<(int Faction, int People)> Alive(ExportedWorld world, int year) =>
        Enumerable.Range(0, world.Factions.Count)
            .Select(f => (f, world.PeopleOf(f, year)))
            .Where(p => p.Item2 > 0)
            .OrderByDescending(p => p.Item2)
            .ToList();

    /// <summary>
    /// The sprite files to try for a drawing, in order, relative to the export's folder: the faction's
    /// own for a capital, then its race's ("ruins" for a ruin). The first that exists is drawn;
    /// if none does, the map draws a dot in the faction's colour.
    /// </summary>
    public static List<string> SpriteFiles(ExportedWorld world, TownDrawing town)
    {
        var files = new List<string>();
        if (town.Faction is not int f || f >= world.Factions.Count)
            return files;
        var info = world.Factions[f];
        if (town.Capital && info.Lineage != null)
            files.Add(Path.Combine("sprites", "factions", info.Lineage, town.Size + ".png"));
        if (info.Race != null)
            files.Add(Path.Combine("sprites", "races", info.Race, (town.Ruined ? "ruins" : town.Size) + ".png"));
        return files;
    }

    /// <summary>The town whose picture covers map point (x, y), or null: `towns` as drawn (Towns), each picture
    /// `side` map cells wide around its cell's middle; where pictures overlap, the one drawn last (on top).</summary>
    public static TownDrawing? TownAt(IReadOnlyList<TownDrawing> towns, double x, double y, double side)
    {
        for (int i = towns.Count - 1; i >= 0; i--)
        {
            var town = towns[i];
            if (Math.Abs(x - (town.X + 0.5)) <= side / 2 && Math.Abs(y - (town.Y + 0.5)) <= side / 2)
                return town;
        }
        return null;
    }

    /// <summary>How many map cells wide a settlement's picture is drawn: the pictures are drawn for a map
    /// 1024 cells wide, and never smaller than 10 cells (as the atlas draws them).</summary>
    public static double SpriteSide(int mapSize) => Math.Max(10, Math.Round(32.0 * mapSize / 1024));
}
