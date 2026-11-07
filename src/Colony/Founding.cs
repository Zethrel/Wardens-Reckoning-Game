// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// How a game begins (library Review 26, R26-04, and Review 33): the founders arrive at the spot the player chose,
/// with the supplies they brought. For now the spot is on the flat test ground (step 17 brings the world's own
/// local terrain), and who the founders are is only their names, drawn by the colony's dice from founding.json
/// (their skills, traits and age come later).
/// </summary>
public static class Founding
{
    /// <summary>The founders' arrival: a colony on the test ground with the founders' carts on tile (x, y), holding
    /// the supplies; the founders and their bedrolls on the open tiles nearest the carts; and the stand-in spring
    /// a little way south (until the well, step 15c).</summary>
    public static (Colony Colony, Inventory Carts) Found(long seed, Moment arrival, Content content, int x, int y)
    {
        var colony = new Colony(seed, arrival, content);
        var ground = Ground.Test(content);
        if (!ground.Open(x, y))
            throw new ArgumentException("the founders cannot arrive on (" + x + ", " + y + "): it is not open ground");
        colony.Lay(ground);
        var work = colony.Work!;
        var used = new HashSet<(int, int)> { (x, y) };
        var carts = colony.Stocks.Open("The founders' carts", Holder.Store, x + 0.5, y + 0.5);
        foreach (var (good, amount) in content.Founding.Supplies)
            colony.Stocks.Arrive(colony.Ids.Next<OperationId>(), arrival.Tick, carts.Id, good, amount);
        // Who they are: names drawn from the list, none twice.
        var dice = colony.DiceFor("founders");
        var names = content.Founding.Names.ToList();
        for (int i = 0; i < content.Founding.Founders; i++)
        {
            string name = names[dice.Int(0, names.Count - 1)];
            names.Remove(name);
            var (fx, fy) = OpenNear(ground, x, y, used);
            var founder = work.AddWorker(name, fx + 0.5, fy + 0.5);
            if (content.Founding.BedrollEach)
            {
                var (bx, by) = OpenNear(ground, x, y + 2, used);
                work.AddBedroll(bx + 0.5, by + 0.5, founder.Id);
            }
        }
        var (sx, sy) = OpenNear(ground, x, y + 6, used);
        work.AddSpring(sx + 0.5, sy + 0.5);
        return (colony, carts);
    }

    /// <summary>The open tile nearest (x, y) not yet used: rings outwards, each ring row by row.</summary>
    static (int X, int Y) OpenNear(Ground ground, int x, int y, HashSet<(int, int)> used)
    {
        for (int r = 0; r < Math.Max(ground.Width, ground.Height); r++)
            for (int ty = y - r; ty <= y + r; ty++)
                for (int tx = x - r; tx <= x + r; tx++)
                    if (Math.Max(Math.Abs(tx - x), Math.Abs(ty - y)) == r && ground.Open(tx, ty) && used.Add((tx, ty)))
                        return (tx, ty);
        throw new InvalidOperationException("no open ground left near (" + x + ", " + y + ")");
    }
}
