// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Sim;

/// <summary>
/// The game's dice: numbers that look random but come from a seed, so the same seed always rolls the same.
///
/// Each system has dice of its own (`Game.DiceFor("rulers")`), made from the game's seed and the system's name:
/// one system rolling more or less never changes what another rolls. The dice's whole state is four numbers,
/// which a save keeps, so a loaded game rolls on exactly as it would have.
///
/// The method is xoshiro256** (Blackman and Vigna), a small, fast, well-tested generator; the seed is spread over
/// the four numbers with SplitMix64, as its authors advise.
/// </summary>
public sealed class Dice
{
    readonly ulong[] state = new ulong[4];

    /// <summary>Dice for `system`, from the game's `seed`.</summary>
    public Dice(long seed, string system)
    {
        // The system's name turns into a number (FNV-1a, a simple, fixed text hash), mixed into the seed.
        ulong hash = 14695981039346656037UL;
        foreach (char c in system)
            hash = unchecked((hash ^ c) * 1099511628211UL);
        ulong mix = unchecked((ulong)seed ^ hash);
        for (int i = 0; i < 4; i++)
            state[i] = SplitMix(ref mix);
    }

    /// <summary>Dice in a state a save kept (`State`).</summary>
    public Dice(IReadOnlyList<ulong> saved)
    {
        if (saved.Count != 4)
            throw new ArgumentException("dice keep four numbers");
        for (int i = 0; i < 4; i++)
            state[i] = saved[i];
    }

    /// <summary>The four numbers the dice are at now: a save keeps them.</summary>
    public IReadOnlyList<ulong> State => state.ToArray();

    static ulong SplitMix(ref ulong x)
    {
        ulong z = unchecked(x += 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>The next raw 64-bit number.</summary>
    public ulong Next()
    {
        ulong result = unchecked(RotateLeft(state[1] * 5, 7) * 9);
        ulong t = state[1] << 17;
        state[2] ^= state[0];
        state[3] ^= state[1];
        state[1] ^= state[2];
        state[0] ^= state[3];
        state[2] ^= t;
        state[3] = RotateLeft(state[3], 45);
        return result;
    }

    /// <summary>A number from 0 up to (not including) 1, as Python's random.random().</summary>
    public double Random() => (Next() >> 11) * (1.0 / (1UL << 53));

    /// <summary>True `probability` of the time: Chance(0.1) is true one roll in ten.</summary>
    public bool Chance(double probability) => Random() < probability;

    /// <summary>A number from `low` to `high`, as Python's random.uniform.</summary>
    public double Uniform(double low, double high) => low + (high - low) * Random();

    /// <summary>A whole number from `low` to `high`, both included, as Python's random.randint.</summary>
    public int Int(int low, int high) => low + (int)(Random() * (high - low + 1));

    /// <summary>One of `items`, each as likely, as Python's random.choice.</summary>
    public T Choice<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];
}
