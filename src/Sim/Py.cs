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
