// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Every town fed by the people who hold it now (the generator's north/tastes.py, Tastes and Feeding). With your
/// tastes on (PEOPLE_FOOD, PEOPLE_GOODS or PATH_LAND), what a town's land grows depends on who farms it: a people's
/// own food from each biome, and the land its path makes livable. When a town changes hands, or its holder changes
/// path, its food, capacity and harvest are worked out again, at the start of the next year.
/// </summary>
public static class Feeding
{
    /// <summary>How many cells wide the map the settings' numbers are counted on is (settlements.REFERENCE_SIZE), and
    /// what one cell yields of a good at a yield of 1 (trade.UNITS_PER_CELL), on a map of REFERENCE_CELLS cells.</summary>
    public const int ReferenceSize = 256;
    public const double UnitsPerCell = 0.05;
    public const int ReferenceCells = 256 * 256;

    /// <summary>The biome a path farms this biome as (PATH_LAND), by name: itself if the path changes nothing.</summary>
    public static string FarmedAs(GameSettings settings, string? path, string biome) =>
        path != null && settings.PathLand.TryGetValue(path, out var land) && land.TryGetValue(biome, out var other)
            ? other : biome;

    /// <summary>How much food one region of a biome grows for a people on a path: as PATH_LAND farms it, by
    /// PEOPLE_FOOD, else BIOME_FOOD (DEFAULT_FOOD for a biome it does not list). tastes.food_of.</summary>
    public static double FoodOf(Game game, int? people, string? biome, string? path)
    {
        if (biome == null)
            return 0.0;
        var settings = game.Settings;
        string name = FarmedAs(settings, path, biome);
        string? folk = people is int p && p >= 0 && p < game.PeopleNamesOf.Count ? game.PeopleNamesOf[p] : null;
        if (folk != null && settings.PeopleFood.TryGetValue(folk, out var own) && own.TryGetValue(name, out double value))
            return value;
        return settings.BiomeFood.TryGetValue(name, out double food) ? food : settings.DefaultFood;
    }

    static string? BiomeAt(Game game, int cell) => game.LandBiome[cell] is int b and >= 0 ? game.BiomeNames[b] : null;

    /// <summary>(food, capacity) of these regions for a people on a path. tastes.capacity.</summary>
    public static (double Food, double Capacity) CapacityOf(Game game, IReadOnlyList<int> cells, int? people, string? path)
    {
        double share = (double)ReferenceSize * ReferenceSize / (game.Grid.Columns * game.Grid.Rows);
        double food = Py.Sum(cells.Select(c => FoodOf(game, people, BiomeAt(game, c), path))) * share;
        return (food, Py.Round(food * game.Settings.PeoplePerFood));
    }

    /// <summary>What these regions grow of every land good in an ordinary year, for a people on a path: each as the
    /// biome its path farms it as, by the people's own PEOPLE_GOODS where it has them, else by GOODS. tastes.yields
    /// (and trade.yields_of, which it is with no tastes).</summary>
    public static Dictionary<string, double> YieldsOf(Game game, IReadOnlyList<int> cells, int? people, string? path)
    {
        var settings = game.Settings;
        double share = (double)ReferenceCells / (game.Grid.Columns * game.Grid.Rows);
        string? folk = people is int p && p >= 0 && p < game.PeopleNamesOf.Count ? game.PeopleNamesOf[p] : null;
        var own = folk != null && settings.PeopleGoods.TryGetValue(folk, out var o) ? o
                  : new Dictionary<string, IReadOnlyDictionary<string, double>>();
        bool pathLand = path != null && settings.PathLand.TryGetValue(path, out var land) && land.Count > 0;
        var grown = new Dictionary<string, double>();
        var byBiome = new Dictionary<string, List<(string Good, double Amount)>>();   // each biome's goods, looked up once
        foreach (int cell in cells)
        {
            string? biome = BiomeAt(game, cell);
            if (biome == null)
                continue;
            string farmed = own.Count == 0 && !pathLand ? biome : FarmedAs(settings, path, biome);
            if (!byBiome.TryGetValue(farmed, out var goods))
            {
                goods = new List<(string, double)>();
                foreach (var (good, biomes) in settings.GoodBiomes)
                {
                    double amount = own.TryGetValue(good, out var mine) && mine.TryGetValue(farmed, out double m)
                        ? m : biomes.GetValueOrDefault(farmed);
                    if (amount != 0)
                        goods.Add((good, amount));
                }
                byBiome[farmed] = goods;
            }
            foreach (var (good, amount) in goods)
                grown[good] = grown.GetValueOrDefault(good) + amount * UnitsPerCell * share;
        }
        return grown;
    }

    /// <summary>Works out again the food, capacity and harvest of every town whose holder's people or path has changed
    /// since they were last worked out (tastes.Feeding.refresh). Only with your tastes on.</summary>
    public static void Refresh(Game game)
    {
        if (!game.Settings.FeedsItsOwn)
            return;
        foreach (var town in game.Towns)
        {
            (int?, string?) holder = town.Holder is int h ? (game.Factions[h].Folk, game.Factions[h].Path) : (null, null);
            if (town.Ruined || town.FedAs == holder)
                continue;
            (town.Food, town.Capacity) = CapacityOf(game, town.Cells, holder.Item1, holder.Item2);
            town.Yields = YieldsOf(game, town.Cells, holder.Item1, holder.Item2);
            town.FedAs = holder;
        }
    }
}
