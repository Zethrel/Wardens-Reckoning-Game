// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.World;

namespace WardensReckoning.Colony;

/// <summary>
/// How a game begins (library Review 26, R26-04, and Review 33): the founders arrive at the spot the player chose,
/// with the supplies they brought. The spot is a founding site of the world's export, on its local map (step 17b,
/// library Review 36), or a tile of the flat test ground. Who the founders are is only their names, drawn by the
/// colony's dice from founding.json (their skills, traits and age come later).
/// </summary>
public static class Founding
{
    /// <summary>
    /// The founders' arrival on a founding site's local map: the carts on the open tile nearest the middle of the map
    /// (the generator keeps the middle clear of trees and rocks), the founders and their bedrolls beside them, as on
    /// the test ground. If the map has fresh water, the founders drink from it: a water source on the bank tile
    /// nearest the carts, of the water's kind (step 19a: a river or a lake has plenty, a brook its month's litres)
    /// and named for it ("The river", "The brook", "The lake"). A site without fresh water has none: the founders
    /// live on the water they brought (founding.json) until they build a well. A colony in a cold biome
    /// (needs.json water.cold_biomes) has its water freeze in the freezing months.
    /// </summary>
    public static (Colony Colony, Inventory Carts) Found(long seed, Moment arrival, Content content, LocalMap map)
    {
        var ground = Ground.FromMap(map, content);
        var (x, y) = OpenNear(ground, map.Size / 2, map.Size / 2, new HashSet<(int, int)>());
        var (colony, carts, used) = Arrive(seed, arrival, content, ground, x, y);
        colony.Work!.Cold = map.Biome != null && content.Water.ColdBiomes.Contains(map.Biome);
        if (Bank(ground, map, x, y, used) is var (bx, by))
            colony.Work.AddSpring(bx + 0.5, by + 0.5, WaterName(map.Water), kind: KindOf(map.Water));
        colony.Work.Asks();                               // what there is to do from the start (step 21c)
        return (colony, carts);
    }

    /// <summary>The kind of water source for what the generator moved the site to: a river, a brook ("stream"), a
    /// lake; fresh water on a map moved for none of these (or to the sea, with a fresh lake on it too) is a lake.</summary>
    public static WaterKind KindOf(string? water) => water switch
    {
        "river" => WaterKind.River,
        "stream" => WaterKind.Brook,
        _ => WaterKind.Lake,
    };

    /// <summary>What the founders call the water they drink from, by what the generator moved the site to.</summary>
    public static string WaterName(string? water) => water switch
    {
        "river" => "The river",
        "stream" => "The brook",
        "lake" => "The lake",
        _ => "The water",
    };

    /// <summary>The open tile beside fresh water (across, not on the slant) that is the shortest walk from (x, y),
    /// and not taken by the carts, a founder or a bedroll; or null if there is none the founders can reach. On a
    /// tie, the first row by row.</summary>
    static (int X, int Y)? Bank(Ground ground, LocalMap map, int x, int y, HashSet<(int, int)> used)
    {
        (int, int)? best = null;
        double near = double.PositiveInfinity;
        for (int ty = 0; ty < map.Size; ty++)
            for (int tx = 0; tx < map.Size; tx++)
            {
                if (!ground.Open(tx, ty) || used.Contains((tx, ty)) || !new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }.Any(d =>
                        ground.Inside(tx + d.Item1, ty + d.Item2) && map.SurfaceAt(tx + d.Item1, ty + d.Item2) == "fresh_water"))
                    continue;
                double metres = ground.Metres(tx, ty, x, y);
                if (metres < near)
                    (best, near) = ((tx, ty), metres);
            }
        return best;
    }

    /// <summary>The founders' arrival: a colony on the test ground with the founders' carts on tile (x, y), holding
    /// the supplies; the founders and their bedrolls on the open tiles nearest the carts; and a stream (the
    /// stand-in spring) a little way south, until step 17 brings the world's rivers (library Review 34).</summary>
    public static (Colony Colony, Inventory Carts) Found(long seed, Moment arrival, Content content, int x, int y)
    {
        var ground = Ground.Test(content);
        if (!ground.Open(x, y))
            throw new ArgumentException("the founders cannot arrive on (" + x + ", " + y + "): it is not open ground");
        var (colony, carts, used) = Arrive(seed, arrival, content, ground, x, y);
        var (sx, sy) = OpenNear(ground, x, y + 6, used);
        colony.Work!.AddSpring(sx + 0.5, sy + 0.5, "The stream");
        colony.Work.Asks();
        return (colony, carts);
    }

    /// <summary>The colony on `ground`, with the carts on tile (x, y) holding the supplies, and the founders and
    /// their bedrolls on the open tiles nearest; and the tiles used.</summary>
    static (Colony, Inventory, HashSet<(int, int)>) Arrive(long seed, Moment arrival, Content content, Ground ground, int x, int y)
    {
        var colony = new Colony(seed, arrival, content);
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
            // How good they are at each skill (step 23a): drawn from the skill's range, on dice of their own, so
            // that adding a skill changes nothing else that is drawn.
            var skillDice = colony.DiceFor("skills");
            // And how fast they learn each (step 23b): on dice of their own again.
            var aptitudeDice = colony.DiceFor("aptitude");
            // And whether they enjoy or dislike each kind of work (step 23c): on dice of their own once more. One roll
            // from 0 to 1 a skill: below the chance to enjoy it, they do; above that, but below both chances
            // together, they dislike it; otherwise neither.
            var interestDice = colony.DiceFor("interests");
            var (enjoys, dislikes) = content.Founding.Interests;
            foreach (var (skill, (from, to)) in content.Founding.Skills)
            {
                founder.Skills[skill] = new SkillState(skillDice.Int(from, to),
                                                       aptitudeDice.Uniform(content.Learning.AptitudeFrom, content.Learning.AptitudeTo),
                                                       colony.Now.Tick);
                double roll = interestDice.Uniform(0, 1);
                if (roll < enjoys)
                    founder.Interests[skill] = Interest.Enjoys;
                else if (roll < enjoys + dislikes)
                    founder.Interests[skill] = Interest.Dislikes;
            }
            // The broad interests (step 25a, leisure.json: nature, the sky, company) the same way, after the skills,
            // so the skills' draws stay as they were.
            foreach (string broad in content.Leisure.Interests.Keys)
            {
                double roll = interestDice.Uniform(0, 1);
                if (roll < enjoys)
                    founder.Interests[broad] = Interest.Enjoys;
                else if (roll < enjoys + dislikes)
                    founder.Interests[broad] = Interest.Dislikes;
            }
            if (content.Founding.BedrollEach)
            {
                var (bx, by) = OpenNear(ground, x, y + 2, used);
                work.AddBedroll(bx + 0.5, by + 0.5, founder.Id);
            }
        }
        work.Note("arrived", "");                          // the journey here: something to talk about (step 25b)
        return (colony, carts, used);
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
