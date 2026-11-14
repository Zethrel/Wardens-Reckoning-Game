// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using WardensReckoning.Colony;

namespace WardensReckoning.Tests;

/// <summary>
/// Step 26d (the UI plan, docs/ui-plan.md, UI-04 and UI-05): the alerts, worked out from the colony's state, few,
/// most serious first, one per condition under a key that stays the same while it holds; and the hover hint for a
/// tile. Founders on the free-time meadow (trees at (10..12, 5), a stream down x = 35) at 09:00.
/// </summary>
public class AlertTests
{
    static Colony.Colony Found() => FreeTimeTests.Found(9);

    [Fact]
    public void A_newly_founded_colony_has_nothing_to_alert()
    {
        var colony = Found();
        colony.Run(10 * Calendar.TicksPerMinute);
        Assert.Empty(Inspect.Alerts(colony));
    }

    [Fact]
    public void A_colonist_in_danger_comes_first_and_leads_to_them()
    {
        var colony = Found();
        var work = colony.Work!;
        var (a, b) = (work.Workers.First(), work.Workers.Skip(1).First());
        a.Needs["thirst"].Level = 95;                     // past critical (90)
        b.Lacking = "no water they may drink";
        var alerts = Inspect.Alerts(colony);
        Assert.Equal(3, alerts[0].Severity);
        Assert.Equal(a.Name + "'s thirst is critical", alerts[0].Text);
        Assert.Equal(a.Id, alerts[0].Who);
        Assert.Contains(alerts, x => x.Severity == 2 && x.Who == b.Id && x.Text == b.Name + ": no water they may drink");
        Assert.Equal(alerts.OrderByDescending(x => x.Severity).Select(x => x.Key), alerts.Select(x => x.Key));
    }

    [Fact]
    public void A_condition_keeps_its_key_while_it_holds_so_it_is_shown_once()
    {
        var colony = Found();
        var w = colony.Work!.Workers.First();
        w.Needs["thirst"].Level = 100;
        w.Collapsed = "thirst";                            // (with nothing to drink in their arms, they stay down)
        var before = Inspect.Alerts(colony).Single(x => x.Who == w.Id);
        colony.Run(30);
        var after = Inspect.Alerts(colony).Single(x => x.Who == w.Id);
        Assert.Equal(before.Key, after.Key);
        Assert.Equal(w.Name + " has collapsed (thirst)", after.Text);
        w.Collapsed = null;
        Assert.DoesNotContain(Inspect.Alerts(colony), x => x.Key == before.Key);
    }

    [Fact]
    public void Buildings_held_up_for_one_reason_are_one_alert_leading_to_the_first()
    {
        var colony = Found();
        var work = colony.Work!;
        work.Plan("road", 38, 10);                         // beyond the stream: nobody can reach them
        work.Plan("road", 38, 12);
        var alert = Inspect.Alerts(colony).Single(x => x.Key.StartsWith("waits:"));
        Assert.Equal(1, alert.Severity);
        Assert.Equal("2 buildings wait: no colonist can reach it", alert.Text);
        Assert.Equal((38, 10), alert.Where);
    }

    [Fact]
    public void No_food_left_free_is_alerted()
    {
        var colony = Found();
        string ration = colony.Content!.Eat.Good;
        foreach (var store in colony.Stocks.Inventories.Where(i => i.Holder == Holder.Store).ToList())
            if (colony.Stocks.Free(store.Id, ration) is int n and > 0)
                colony.Stocks.Reserve(store.Id, ration, n, "test");
        Assert.Contains(Inspect.Alerts(colony), x => x.Key == "no food" && x.Severity == 2);
    }

    [Fact]
    public void A_tile_hint_names_what_is_on_it_and_nothing_for_bare_ground()
    {
        var colony = Found();
        Assert.Equal("Tree", Inspect.Hint(colony, 11, 5));
        Assert.Null(Inspect.Hint(colony, 20, 25));
        colony.Work!.PlaceGathering(new Area(20, 25, 2, 2));
        Assert.Equal("Gathering place 1", Inspect.Hint(colony, 20, 25));
        Assert.Null(Inspect.Hint(colony, -1, 0));
    }
}
