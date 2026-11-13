// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Steps 26a and 26b (the UI plan, docs/ui-plan.md, UI-01 and UI-02): the colonist's card in the inspector. The full
/// list's lines sorted into groups, word for word; the needs as levels for bars, counted as deficits (0 not at all,
/// 100 at the limit) with their words; a status in a few words, from the colony's own state.
/// </summary>
public class InspectorTests
{
    static (Colony.Colony Colony, Worker Worker) Calm(int hour = 9)
    {
        var colony = FreeTimeTests.Found(hour);
        var w = colony.Work!.Workers.First();
        foreach (var need in w.Needs.Values)
            need.Level = 0;
        foreach (var need in w.FreeNeeds.Keys.ToList())
            w.FreeNeeds[need] = 0;
        return (colony, w);
    }

    [Fact]
    public void The_card_sorts_the_full_list_into_groups_word_for_word()
    {
        var (colony, w) = Calm();
        var lines = Inspect.Worker(colony, w.Id).Lines;
        var card = Inspect.Card(colony, w.Id);
        Assert.Equal(w.Name, card.Name);
        Assert.Equal(lines.Single(l => l.Label == "Doing").Text, card.Doing);
        Assert.Equal(lines.Single(l => l.Label == "Schedule").Text, card.Schedule);
        // Every line of a group is one of the full list's, unchanged; and each skill is among the skills.
        Assert.All(card.Alerts.Concat(card.Skills).Concat(card.More), l => Assert.Contains(l, lines));
        Assert.All(w.Skills.Keys, s => Assert.Contains(card.Skills, l => l.Label == colony.Content!.Skills[s].Name));
        Assert.Equal(new[] { "Where", "Carrying" }, card.More.Select(l => l.Label));
        Assert.Empty(card.Alerts);                                     // well, and lacking nothing
        Assert.Empty(card.Diagnostics);                                // the developer's trace is off
        Assert.NotEmpty(Inspect.Card(colony, w.Id, trace: true).Diagnostics);
    }

    [Fact]
    public void Needs_are_bars_of_how_pressing_with_their_words()
    {
        var (colony, w) = Calm();
        w.Needs["thirst"].Level = 80;
        w.FreeNeeds["social"] = 90;
        var card = Inspect.Card(colony, w.Id);
        Assert.Equal(w.Needs.Count + w.FreeNeeds.Count, card.Needs.Count);
        var thirst = card.Needs.Single(n => n.Name == "Thirst");
        Assert.Equal(80, thirst.Level);
        Assert.Equal(Inspect.Word(colony.Content!.Needs["thirst"], 80), thirst.Word);
        Assert.Equal(3, thirst.Pressing);                              // 80 is past "urgent" (65), short of "critical" (90)
        var social = card.Needs.Single(n => n.Name == "Social");
        Assert.Equal(("lonely", 2), (social.Word, social.Pressing));
        Assert.All(card.Needs.Where(n => n != thirst && n != social), n => Assert.Equal(("fine", 0), (n.Word, n.Pressing)));
        // The same as the full list says.
        Assert.Contains(Inspect.Worker(colony, w.Id).Lines, l => l.Label == "Thirst" && l.Text == "80 of 100, " + thirst.Word);
    }

    [Fact]
    public void The_status_says_what_state_they_are_in()
    {
        var (colony, w) = Calm();
        var work = colony.Work!;
        colony.Run(10 * Calendar.TicksPerMinute);                     // a Work block with nothing to do: between jobs
        Assert.Equal("Between jobs · available for work", Inspect.Status(work, w));
        work.Plan("road", 20, 20);
        colony.Run(1);
        var builder = work.Workers.Single(x => x.Job != null);
        Assert.Equal("Working", Inspect.Status(work, builder));
        var (evening, v) = Calm(18);
        colony = evening;
        colony.Run(2 * Calendar.TicksPerMinute);
        Assert.Equal("Free time", Inspect.Status(colony.Work!, v));
    }

    [Fact]
    public void A_tile_shows_every_line_it_has()
    {
        var (colony, _) = Calm();
        var shown = Inspect.Tile(colony, 5, 5);
        Assert.Equal("Tile (5, 5)", shown.Title);
        Assert.Contains(shown.Lines, l => l.Label == "Ground");
    }
}
