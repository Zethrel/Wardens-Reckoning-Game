// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Sim;
using WardensReckoning.World;

namespace WardensReckoning.Tests;

/// <summary>The book: the export's events and the game's, whole or one faction's, and a faction's card.</summary>
public class BookTests
{
    static ExportedWorld World() => ExportedWorld.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "world-7"));

    [Fact]
    public void The_export_s_events_come_with_their_factions_and_the_chronicle_s_sentences()
    {
        var world = World();
        var entries = Book.FromExport(world);
        Assert.Equal(world.Data["events"]!.AsArray().Count, entries.Count);
        Assert.Contains(entries, e => e.Told != null);
        Assert.True(entries.Count(e => e.Factions.Count > 0) > entries.Count * 9 / 10);
        // The chronicle's sentence is shown, without the year it opens with (the book shows the year in front).
        Assert.Contains(entries, e => e.Told != null && e.Told.StartsWith("In " + e.Year + ", "));
        Assert.All(entries.Where(e => e.Told != null), e => Assert.EndsWith(e.Told!.Split(", ", 2)[^1][1..], e.Shown));
        Assert.All(entries.Where(e => e.Told != null), e => Assert.False(e.Shown.StartsWith("In " + e.Year + ",")));
        var plain = entries.First(e => e.Told == null && e.Text.Length > 0 && char.IsLower(e.Text[0]));
        Assert.Equal(char.ToUpperInvariant(plain.Text[0]), plain.Shown[0]);
        Assert.Equal(plain.Text[1..], plain.Shown[1..]);
    }

    [Fact]
    public void Reading_one_faction_up_to_a_year_newest_first()
    {
        var entries = new List<BookEntry>
        {
            new(1, "a", "one", null, new[] { 0 }), new(2, "b", "two", "Two.", new[] { 1 }),
            new(3, "c", "three", null, new[] { 0, 1 }), new(5, "d", "five", "Five.", new[] { 0 }),
        };
        Assert.Equal(new[] { "three", "one" }, Book.Read(entries, 4, 0, false).Select(e => e.Text));
        Assert.Equal(new[] { "five", "three", "two", "one" }, Book.Read(entries, 9, null, false).Select(e => e.Text));
        Assert.Equal(new[] { "five", "two" }, Book.Read(entries, 9, null, true).Select(e => e.Text));
        Assert.Equal(new[] { "two" }, Book.Read(entries, 9, 1, true).Select(e => e.Text));
        Assert.Empty(Book.Read(entries, 0, null, false));
    }

    [Fact]
    public void A_search_is_a_year_a_span_of_years_or_text()
    {
        Assert.Null(Book.Search.Parse(null));
        Assert.Null(Book.Search.Parse("   "));
        Assert.Equal(new Book.Search(315, 315, null), Book.Search.Parse(" 315 "));
        Assert.Equal(new Book.Search(310, 320, null), Book.Search.Parse("310-320"));
        Assert.Equal(new Book.Search(310, 320, null), Book.Search.Parse("320 - 310"));
        Assert.Equal(new Book.Search(310, null, null), Book.Search.Parse("310-"));
        Assert.Equal(new Book.Search(null, 320, null), Book.Search.Parse("-320"));
        Assert.Equal(new Book.Search(null, null, "Vawyn"), Book.Search.Parse(" Vawyn "));
        Assert.Equal(new Book.Search(null, null, "-"), Book.Search.Parse("-"));
        Assert.Equal(new Book.Search(null, null, "war-band"), Book.Search.Parse("war-band"));
    }

    [Fact]
    public void A_search_finds_entries_by_year_or_by_their_text_in_any_case()
    {
        var entries = new List<BookEntry>
        {
            new(1, "battle", "battle at Vawyn", null, new[] { 0 }),
            new(3, "famine", "famine at Kethe", "In 3 hunger came to Kethe.", new[] { 1 }),
            new(5, "battle", "battle at kethe", null, new[] { 1 }),
            new(7, "ruin", "Vawyn fell", null, new[] { 0 }),
        };
        string[] Found(string typed, int? faction = null, bool chronicle = false) =>
            Book.Read(entries, 9, faction, chronicle, Book.Search.Parse(typed)).Select(e => e.Text).ToArray();
        Assert.Equal(new[] { "battle at kethe", "famine at Kethe" }, Found("KETHE"));
        Assert.Equal(new[] { "famine at Kethe" }, Found("hunger"));          // the chronicle's sentence counts too
        Assert.Equal(new[] { "Vawyn fell" }, Found("RUIN"));                 // and the kind of entry
        entries.Add(new(8, "colony_founded", "people leave crowded Kethe to found Ash", null, new[] { 1 }));
        Assert.Equal(new[] { "people leave crowded Kethe to found Ash" }, Found("colony founded"));
        entries.RemoveAt(4);
        Assert.Equal(new[] { "famine at Kethe" }, Found("3"));
        Assert.Equal(new[] { "battle at kethe", "famine at Kethe" }, Found("2-6"));
        Assert.Equal(new[] { "Vawyn fell", "battle at Vawyn" }, Found("vawyn", faction: 0));
        Assert.Empty(Found("vawyn", faction: 1));
        Assert.Equal(new[] { "famine at Kethe" }, Found("kethe", chronicle: true));
        Assert.Equal(new[] { "Vawyn fell" }, Book.Read(entries, 9, null, false, Book.Search.Parse("6-")).Select(e => e.Text));
        Assert.Empty(Book.Read(entries, 4, null, false, Book.Search.Parse("5-9")));   // not past the year shown
    }

    [Fact]
    public void A_faction_s_card_is_its_record_in_that_year()
    {
        var world = World();
        int f = 0;
        foreach (int year in new[] { 5, 50, world.Years })
        {
            var card = Book.Card(world, f, year);
            var state = world.Faction(f, year)!;
            Assert.Equal(world.Factions[f].Name, card.Name);
            Assert.Equal((string?)state["path"], card.Path);
            Assert.Equal((string?)state["government"], card.Government);
            Assert.Equal((string?)state["ruler"], card.Ruler);
            Assert.Equal(world.Towns[(int)state["capital"]!].Name, card.Capital);
            Assert.Equal(world.PeopleOf(f, year), card.People);
            Assert.Equal(year + 1, card.PeopleByYear.Count);
            Assert.Equal(card.PeopleByYear.Max(), card.Peak);
            Assert.Equal(card.Peak, card.PeopleByYear[card.PeakYear]);
        }
    }

    [Fact]
    public void A_breakaway_s_card_names_where_it_came_from()
    {
        var world = World();
        var data = world.Data["factions"]!.AsArray();
        int child = Enumerable.Range(0, data.Count).First(i => data[i]!["parent"] is not null);
        int parent = (int)data[child]!["parent"]!;
        var card = Book.Card(world, child, world.Years);
        Assert.Equal(world.Factions[parent].Name, card.Parent);
        Assert.Equal((int)world.Data["history"]!["factions"]![child]!["from"]!, card.From);
        Assert.True(card.From > 0);
    }

    [Fact]
    public void The_game_s_years_write_into_the_same_book()
    {
        var game = new Game(World(), 4);
        int before = game.BookEntries.Count;
        var town = game.Towns.First(t => !t.Ruined && t.Holder != null && t.Population > 900);
        town.Yields.Clear();                                     // it starves, and shrinks a size before long
        for (int i = 0; i < 40 && game.Events.Count == 0; i++)
            YearCore.RunYear(game);
        Assert.NotEmpty(game.Events);
        Assert.Equal(before + game.Events.Count, game.BookEntries.Count);
        var last = game.BookEntries[^1];
        Assert.Equal(game.Events[^1].Text, last.Text);
        Assert.Equal(game.Events[^1].Factions, last.Factions);
        Assert.Contains(last, Book.Read(game.BookEntries, game.Year, last.Factions.FirstOrDefault(), false));
    }

    [Fact]
    public void A_loaded_game_s_book_is_the_same()
    {
        var game = new Game(World(), 4);
        for (int i = 0; i < 30; i++)
            YearCore.RunYear(game);
        string path = Path.Combine(Path.GetTempPath(), "wr-book-" + Guid.NewGuid().ToString("N"), "game.json");
        SaveFile.Write(path, game);
        var loaded = SaveFile.Read(path);
        Assert.Equal(game.BookEntries, loaded.BookEntries, new EntryComparer());
    }

    sealed class EntryComparer : IEqualityComparer<BookEntry>
    {
        public bool Equals(BookEntry? a, BookEntry? b) =>
            a!.Year == b!.Year && a.Text == b.Text && a.Told == b.Told && a.Factions.SequenceEqual(b.Factions);
        public int GetHashCode(BookEntry e) => e.Text.GetHashCode();
    }

    [Theory]
    [InlineData("In 262, the Whisperward took the craft.", 262, "The Whisperward took the craft.")]
    [InlineData("By 104, no one among the Deephands remembered.", 104, "No one among the Deephands remembered.")]
    [InlineData("From 7, no salt came to the Sylze.", 7, "From then on, no salt came to the Sylze.")]
    [InlineData("After a long battle in 30, Hearthfall Commons conquered Whisperward.", 30,
                "After a long battle, Hearthfall Commons conquered Whisperward.")]
    [InlineData("In 262, the war of 261 ended.", 262, "The war of 261 ended.")]
    [InlineData("In 26, the Whisperward fell.", 262, "In 26, the Whisperward fell.")]
    [InlineData("The Whisperward fell in 262.", 262, "The Whisperward fell in 262.")]
    [InlineData("Peace came. By 262, all was quiet.", 262, "Peace came. By 262, all was quiet.")]
    public void The_book_drops_the_year_a_chronicle_sentence_opens_with(string told, int year, string shown) =>
        Assert.Equal(shown, new BookEntry(year, "x", "plain", told, Array.Empty<int>()).Shown);
}
