// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.Colony;

/// <summary>
/// Something every kind of identifier can do (library Shared Architecture §6, Review 29). An `interface` lists what a
/// type promises; `static abstract` members are promised by the type itself, not by each value, so code can ask
/// "make me a TaskId from this number" without knowing it is a TaskId.
/// </summary>
public interface IStableId<TSelf> where TSelf : IStableId<TSelf>
{
    /// <summary>The family's name in saves and messages ("colonist", "item instance"...).</summary>
    static abstract string Family { get; }
    static abstract TSelf From(long value);
    long Value { get; }
}

// One type for each family of identifiers the library names (Shared Architecture §6.1). Each holds a whole number,
// but they are different types: code that wants a ColonistId will not accept an ItemInstanceId by mistake, though
// both may be 17. 0 is never given out, so it can mean "none".
public readonly record struct ColonistId(long Value) : IStableId<ColonistId> { public static string Family => "colonist"; public static ColonistId From(long v) => new(v); }
public readonly record struct AnimalId(long Value) : IStableId<AnimalId> { public static string Family => "animal"; public static AnimalId From(long v) => new(v); }
public readonly record struct BuildingId(long Value) : IStableId<BuildingId> { public static string Family => "building"; public static BuildingId From(long v) => new(v); }
public readonly record struct RoomId(long Value) : IStableId<RoomId> { public static string Family => "room"; public static RoomId From(long v) => new(v); }
public readonly record struct ItemInstanceId(long Value) : IStableId<ItemInstanceId> { public static string Family => "item instance"; public static ItemInstanceId From(long v) => new(v); }
public readonly record struct InventoryId(long Value) : IStableId<InventoryId> { public static string Family => "inventory"; public static InventoryId From(long v) => new(v); }
public readonly record struct JobOrderId(long Value) : IStableId<JobOrderId> { public static string Family => "job order"; public static JobOrderId From(long v) => new(v); }
public readonly record struct TaskId(long Value) : IStableId<TaskId> { public static string Family => "task"; public static TaskId From(long v) => new(v); }
public readonly record struct ReservationId(long Value) : IStableId<ReservationId> { public static string Family => "reservation"; public static ReservationId From(long v) => new(v); }
public readonly record struct LocationId(long Value) : IStableId<LocationId> { public static string Family => "location"; public static LocationId From(long v) => new(v); }
public readonly record struct CaravanId(long Value) : IStableId<CaravanId> { public static string Family => "caravan"; public static CaravanId From(long v) => new(v); }
public readonly record struct AgreementId(long Value) : IStableId<AgreementId> { public static string Family => "agreement"; public static AgreementId From(long v) => new(v); }
public readonly record struct DiscoveryId(long Value) : IStableId<DiscoveryId> { public static string Family => "discovery"; public static DiscoveryId From(long v) => new(v); }
public readonly record struct OperationId(long Value) : IStableId<OperationId> { public static string Family => "operation"; public static OperationId From(long v) => new(v); }
public readonly record struct EventId(long Value) : IStableId<EventId> { public static string Family => "event"; public static EventId From(long v) => new(v); }

/// <summary>
/// Hands out identifiers: one counter per family, kept in the save, so after a load the next one carries on and an
/// identifier is never given twice. (Definition identifiers such as a resource's, and the world's own regions,
/// factions and settlements, come from content files and the export, not from here.)
/// </summary>
public sealed class IdCounters
{
    // The last number given in each family, by family name. SortedDictionary keeps them in order, so a save writes
    // them the same way every time.
    readonly SortedDictionary<string, long> last = new(StringComparer.Ordinal);

    /// <summary>A new identifier of type T: one more than the last of its family.</summary>
    public T Next<T>() where T : IStableId<T>
    {
        long value = last.GetValueOrDefault(T.Family) + 1;
        last[T.Family] = value;
        return T.From(value);
    }

    /// <summary>The last number given in each family (for a save).</summary>
    public IReadOnlyDictionary<string, long> Last => last;

    /// <summary>Puts back the counters a save kept.</summary>
    public void Restore(IEnumerable<KeyValuePair<string, long>> saved)
    {
        last.Clear();
        foreach (var (family, value) in saved)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(saved), "the " + family + " counter cannot be below 0");
            last[family] = value;
        }
    }
}
