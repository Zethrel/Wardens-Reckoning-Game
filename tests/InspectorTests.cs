// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 26a (the UI plan, docs/ui-plan.md, UI-01): the compact inspector. A colonist's inspection carries the few
/// lines shown before More is pressed: what they are doing, their schedule now, the needs that are not fine (or one
/// line saying all are), and what they lack or are ill with. A tile's has none: all its lines are shown.
/// </summary>
public class InspectorTests
{
    [Fact]
    public void A_colonist_in_good_shape_is_shown_in_three_lines()
    {
        var colony = FreeTimeTests.Found(9);
        var w = colony.Work!.Workers.First();
        foreach (var need in w.Needs.Values)
            need.Level = 0;
        foreach (var need in w.FreeNeeds.Keys.ToList())
            w.FreeNeeds[need] = 0;
        var shown = Inspect.Worker(colony, w.Id);
        Assert.Equal(new[] { "Doing", "Schedule", "Needs" }, shown.Brief!.Select(l => l.Label));
        Assert.Equal("all fine", shown.Brief!.Single(l => l.Label == "Needs").Text);
        Assert.Same(shown.Brief, Inspect.Shown(shown, expanded: false));
        Assert.Same(shown.Lines, Inspect.Shown(shown, expanded: true));
        Assert.True(shown.Lines.Count > shown.Brief!.Count);
    }

    [Fact]
    public void Only_the_needs_that_are_not_fine_are_in_the_brief_as_they_are_in_the_full_list()
    {
        var colony = FreeTimeTests.Found(9);
        var w = colony.Work!.Workers.First();
        foreach (var need in w.Needs.Values)
            need.Level = 0;
        foreach (var need in w.FreeNeeds.Keys.ToList())
            w.FreeNeeds[need] = 0;
        w.Needs["thirst"].Level = 80;
        w.FreeNeeds["social"] = 90;
        var shown = Inspect.Worker(colony, w.Id);
        var needs = shown.Brief!.Skip(2).ToList();
        Assert.Equal(2, needs.Count);
        Assert.All(needs, l => Assert.Contains(l, shown.Lines));            // word for word the full list's lines
        Assert.Contains(needs, l => l.Text.StartsWith("80 of 100"));
        Assert.Contains(needs, l => l.Text.StartsWith("90 of 100, lonely"));
        Assert.DoesNotContain(shown.Brief!, l => l.Label == "Needs");
    }

    [Fact]
    public void A_tile_has_no_brief_and_shows_every_line()
    {
        var colony = FreeTimeTests.Found(9);
        var shown = Inspect.Tile(colony, 5, 5);
        Assert.Null(shown.Brief);
        Assert.Same(shown.Lines, Inspect.Shown(shown, expanded: false));
    }
}
