// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Sim;

namespace WardensReckoning.Colony;

/// <summary>
/// The colony's state (step 12: only its foundations). Everything the colony is lives here or below, so that a save
/// can write all of it and the same colony with the same commands always comes out the same (FP-07):
/// - the clock (`Now`), which moves only when `Run` is called;
/// - the identifier counters (`Ids`);
/// - every system's dice (`DiceFor`), each made from the colony's seed and the system's name, as the history's are.
/// </summary>
public sealed class Colony
{
    /// <summary>The seed every system's dice come from.</summary>
    public long Seed { get; }

    /// <summary>When the colony was founded.</summary>
    public Moment Founded { get; }

    /// <summary>The colony's time now.</summary>
    public Moment Now { get; private set; }

    public IdCounters Ids { get; } = new();

    /// <summary>The colony's goods: inventories, reservations and the ledger (step 13).</summary>
    public Stocks Stocks { get; }

    readonly SortedDictionary<string, Dice> dice = new(StringComparer.Ordinal);

    /// <summary>What the colony knows (goods, recipes...), or null for a colony with no work (as in step 12).</summary>
    public Content? Content { get; }

    /// <summary>The colony's work: its workers, workshops and jobs (step 13b; null without content).</summary>
    public Work? Work { get; }

    public Colony(long seed, Moment founded, Content? content = null)
    {
        Seed = seed;
        Founded = founded;
        Now = founded;
        Stocks = new Stocks(Ids);
        Content = content;
        Work = content == null ? null : new Work(this, content);
    }

    /// <summary>The dice of one system ("weather", "births"...): made the first time they are asked for, then kept,
    /// and kept in a save. One system rolling more never changes what another rolls.</summary>
    public Dice DiceFor(string system)
    {
        if (!dice.TryGetValue(system, out var found))
            dice[system] = found = new Dice(Seed, "colony " + system);
        return found;
    }

    /// <summary>Every system's dice made so far, by name (for a save).</summary>
    public IReadOnlyDictionary<string, Dice> AllDice => dice;

    /// <summary>Puts back dice a save kept.</summary>
    public void SetDice(string system, Dice saved) => dice[system] = saved;

    /// <summary>
    /// Runs the colony on by `ticks` minutes, one tick at a time: each minute the work is done (Work.Tick), then
    /// `eachTick`, if given (for the tests).
    /// </summary>
    public void Run(long ticks, Action<Colony>? eachTick = null)
    {
        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks), "time does not run backwards");
        for (long i = 0; i < ticks; i++)
        {
            Now = Now.After(1);
            Work?.Tick();
            eachTick?.Invoke(this);
        }
    }

    /// <summary>Sets the clock (only for loading a save).</summary>
    internal void SetNow(Moment now) => Now = now;
}
