// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// Python's own arithmetic, where C#'s gives a different last digit. The generator is the reference: the same
/// numbers added Python's way give the same world.
/// </summary>
public static class Py
{
    /// <summary>
    /// Python's built-in sum() of decimals (3.12 and later): it keeps the bits each addition loses and adds them
    /// back at the end (Neumaier's compensated summation), so it can differ from adding one by one in the last digit.
    /// </summary>
    public static double Sum(IEnumerable<double> values)
    {
        double total = 0.0, lost = 0.0;
        foreach (double x in values)
        {
            double t = total + x;
            if (Math.Abs(total) >= Math.Abs(x))
                lost += (total - t) + x;
            else
                lost += (x - t) + total;
            total = t;
        }
        return lost != 0.0 && double.IsFinite(lost) ? total + lost : total;
    }

    /// <summary>Python's round() of a number to a whole one: a half goes to the even number.</summary>
    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.ToEven);
}

/// <summary>
/// A table that keeps its keys in the order they were first put in, as a Python dict does: a key taken out and put
/// back goes to the end. C#'s Dictionary may reuse a removed key's place, so its order can differ from Python's;
/// where the generator adds decimals in its dict's order (attunement's marks), the order decides the last digit.
/// </summary>
public sealed class PyDict<TKey, TValue> where TKey : notnull
{
    readonly Dictionary<TKey, int> at = new();
    readonly List<(TKey Key, TValue Value, bool Alive)> items = new();
    int removed;

    public int Count => at.Count;

    public bool ContainsKey(TKey key) => at.ContainsKey(key);

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (at.TryGetValue(key, out int i))
        {
            value = items[i].Value;
            return true;
        }
        value = default!;
        return false;
    }

    public TValue GetValueOrDefault(TKey key, TValue otherwise) => TryGetValue(key, out var value) ? value : otherwise;

    /// <summary>Reads a value (it must be there), or changes it in its place, or adds the key at the end.</summary>
    public TValue this[TKey key]
    {
        get => items[at[key]].Value;
        set
        {
            if (at.TryGetValue(key, out int i))
                items[i] = (key, value, true);
            else
            {
                at[key] = items.Count;
                items.Add((key, value, true));
            }
        }
    }

    public bool Remove(TKey key)
    {
        if (!at.Remove(key, out int i))
            return false;
        items[i] = (key, default!, false);
        if (++removed > 64 && removed > items.Count / 2)
            Compact();
        return true;
    }

    /// <summary>The keys and values in order, as they are now (a copy: the table may change while it is read).</summary>
    public List<(TKey Key, TValue Value)> Items() =>
        items.Where(item => item.Alive).Select(item => (item.Key, item.Value)).ToList();

    public List<TKey> Keys() => items.Where(item => item.Alive).Select(item => item.Key).ToList();

    void Compact()
    {
        var alive = items.Where(item => item.Alive).ToList();
        items.Clear();
        at.Clear();
        foreach (var item in alive)
        {
            at[item.Key] = items.Count;
            items.Add(item);
        }
        removed = 0;
    }
}
