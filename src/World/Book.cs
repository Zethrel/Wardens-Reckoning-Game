// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Text.Json.Nodes;

namespace WardensReckoning.World;

/// <summary>
/// One entry of the book: something that happened, in its year. `Factions` are the factions it concerns, by
/// number; `Told` is the chronicle's sentence for it, or null when the chronicle does not tell it (then the
/// plain `Text` is shown).
/// </summary>
public sealed record BookEntry(int Year, string What, string Text, string? Told, IReadOnlyList<int> Factions)
{
    /// <summary>What the book shows: the chronicle's sentence, or else the plain text with a capital first letter
    /// (as the chronicle writes every sentence).</summary>
    public string Shown => Told ?? (Text.Length > 0 ? char.ToUpperInvariant(Text[0]) + Text[1..] : Text);
}

/// <summary>What a faction's card shows about it in one year.</summary>
public sealed record FactionCard(
    int Index, string Name, byte Red, byte Green, byte Blue,
    string? Path, string? Ruler, string? Belief, string? Government, string? Capital,
    int From, string? Parent, int People, int Peak, int PeakYear, IReadOnlyList<int> PeopleByYear);

/// <summary>
/// The book of everything that happened: the export's events, then the game's own, read either whole or as one
/// faction's history (the maintainer's choice, 31 October 2026: one book, two ways to read it).
/// </summary>
public static class Book
{
    /// <summary>The export's events as book entries, in the order they happened. An older export without
    /// "factions" on its events gives entries that concern no faction.</summary>
    public static List<BookEntry> FromExport(ExportedWorld world) =>
        world.Data["events"]?.AsArray().Select(node =>
        {
            var e = node!.AsObject();
            return new BookEntry((int)e["year"]!, (string?)e["what"] ?? "", (string?)e["text"] ?? "", (string?)e["told"],
                                 e["factions"] is JsonArray f ? f.Select(i => (int)i!).ToList() : new List<int>());
        }).ToList() ?? new List<BookEntry>();

    /// <summary>
    /// The entries to show, the newest first: up to `year`, only those concerning `faction` (all if null), and
    /// only those the chronicle tells when `chronicleOnly`. `entries` are in the order they happened.
    /// </summary>
    public static List<BookEntry> Read(IReadOnlyList<BookEntry> entries, int year, int? faction, bool chronicleOnly)
    {
        var shown = new List<BookEntry>();
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry.Year > year)
                continue;
            if (faction is int f && !entry.Factions.Contains(f))
                continue;
            if (chronicleOnly && entry.Told == null)
                continue;
            shown.Add(entry);
        }
        return shown;
    }

    /// <summary>Faction `index`'s card in `year`: its path, ruler, belief, government and capital as last changed
    /// by then, when it came to be and from whom, and its people every year up to then.</summary>
    public static FactionCard Card(ExportedWorld world, int index, int year)
    {
        var info = world.Factions[index];
        var state = world.Faction(index, Math.Min(year, world.Years)) ?? new JsonObject();
        int from = world.FromOf(index);
        string? parent = info.Parent is int parentIndex && parentIndex < world.Factions.Count
                         ? world.Factions[parentIndex].Name : null;
        string? capital = state["capital"] is JsonValue c && c.TryGetValue(out int town) && town < world.Towns.Count
                          ? world.Towns[town].Name : null;
        var people = Enumerable.Range(0, Math.Max(0, year + 1)).Select(y => world.PeopleOf(index, y)).ToList();
        int peak = 0, peakYear = from;
        for (int y = 0; y < people.Count; y++)
            if (people[y] > peak)
                (peak, peakYear) = (people[y], y);
        return new FactionCard(index, info.Name, info.Red, info.Green, info.Blue,
                               Text(state["path"]), Text(state["ruler"]), Text(state["belief"]), Text(state["government"]),
                               capital, from, parent, world.PeopleOf(index, year), peak, peakYear, people);
    }

    // A value of the record as text: a ruler may be kept as a name, or as an object with one.
    static string? Text(JsonNode? value) => value switch
    {
        null => null,
        JsonObject o => (string?)o["name"],
        JsonValue v when v.TryGetValue(out string? s) => s,
        _ => value.ToJsonString(),
    };
}
