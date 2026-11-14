// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;
using WardensReckoning.Colony;

namespace WardensReckoning.UI;

/// <summary>
/// What the colony screen points out (step 26d, the UI plan's phase D):
///
/// - **Hover (UI-04).** The colonist or tile under the mouse is lightly marked on the map (ColonyView), and a small
///   tooltip beside the mouse says who and what they are doing, or what is notable on the tile. Not while a build tool
///   is in hand (its own outline is shown then), and not over the panels. Clicks go to the map as before: the tooltip
///   lets the mouse through.
/// - **Alerts (UI-05).** At the map's top right, what the player may want to act on (`Inspect.Alerts`, worked out from
///   the colony's state): most serious first, at most five, each once for as long as its condition holds. One new in
///   the last few seconds is marked. A click goes to the colonist or the place; × puts one away until its condition
///   clears (if it comes back later, it is shown again).
/// </summary>
public partial class ColonyScreen
{
    const int MostAlerts = 5;
    const ulong NewForMs = 8000;

    PanelContainer? tooltip;
    Label? tooltipText;
    VBoxContainer? alertColumn;
    string alertsShown = "";
    readonly HashSet<string> dismissed = new();
    readonly Dictionary<string, ulong> firstSeen = new();

    void MakeNotices(Control middle)
    {
        alertColumn = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        alertColumn.AddThemeConstantOverride("separation", 4);
        alertColumn.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        alertColumn.GrowHorizontal = Control.GrowDirection.Begin;
        middle.AddChild(alertColumn);

        // The tooltip: on the screen's own layer, over everything, never taking the mouse.
        tooltip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 10 };
        var style = Ledger.Panel();
        style.SetContentMarginAll(5);
        style.SetBorderWidthAll(1);
        tooltip.AddThemeStyleboxOverride("panel", style);
        tooltipText = Ledger.Label("", 13);
        tooltipText.AutowrapMode = TextServer.AutowrapMode.Off;
        tooltipText.MouseFilter = Control.MouseFilterEnum.Ignore;
        tooltip.AddChild(tooltipText);
        middle.AddChild(tooltip);
    }

    // ---- Hover ------------------------------------------------------------------------------------------------------

    // The mouse moved over the map, `point` in tiles: the colonist under it, or else the tile.
    void Hover(Vector2 point)
    {
        if (Inspect.WorkerAt(Colony, point.X, point.Y) is Worker w)
            (view!.HoverWorker, view.HoverTile) = (w.Id, null);
        else
        {
            var (x, y) = ((int)Math.Floor(point.X), (int)Math.Floor(point.Y));
            (view!.HoverWorker, view.HoverTile) = (null, Colony.Ground!.Inside(x, y) ? (x, y) : null);
        }
        ShowTooltip();
    }

    // Each frame: off the map (over a panel or the bar), or with a tool in hand, nothing is hovered; otherwise the
    // tooltip follows the mouse, kept on the screen.
    void FollowMouse()
    {
        bool overMap = GetViewport().GuiGetHoveredControl() == mapFrame && tool == null;
        if (!overMap && (view!.HoverWorker != null || view.HoverTile != null))
        {
            view.HoverWorker = null;
            view.HoverTile = null;
            tooltip!.Visible = false;
        }
        if (!tooltip!.Visible)
            return;
        var mouse = stage!.GetLocalMousePosition();
        var size = tooltip.Size;
        float x = Math.Min(mouse.X + 16, stage.Size.X - size.X - 4), y = Math.Min(mouse.Y + 18, stage.Size.Y - size.Y - 4);
        tooltip.Position = new Vector2(Math.Max(4, x), Math.Max(4, y));
    }

    // The tooltip's words: "Cael · between jobs, pottering about"; a tile's notable things; nothing for bare ground.
    void ShowTooltip()
    {
        string? words = null;
        if (view!.HoverWorker is ColonistId id && Colony.Work!.Workers.Any(w => w.Id == id))
            words = Colony.Work.Workers.First(w => w.Id == id).Name + " · " + Colony.Work.Doing(id);
        else if (view.HoverTile is var (x, y))
            words = Inspect.Hint(Colony, x, y);
        tooltip!.Visible = words != null;
        if (words != null && tooltipText!.Text != words)
        {
            tooltipText.Text = words;
            tooltip.ResetSize();             // fit the new words
        }
    }

    // ---- Alerts -----------------------------------------------------------------------------------------------------

    // The alerts, made again only when they change (so a button is not replaced under the mouse); the column kept to
    // the left of the inspector when it is open.
    void ShowAlerts()
    {
        alertColumn!.OffsetRight = side!.Visible ? -InspectorWidth - 8 : -8;
        var alerts = Inspect.Alerts(Colony);
        var keys = alerts.Select(a => a.Key).ToHashSet();
        dismissed.IntersectWith(keys);                     // a condition that cleared may be shown again if it returns
        foreach (var gone in firstSeen.Keys.Where(k => !keys.Contains(k)).ToList())
            firstSeen.Remove(gone);
        ulong now = Time.GetTicksMsec();
        foreach (var a in alerts)
            firstSeen.TryAdd(a.Key, now);
        var shown = alerts.Where(a => !dismissed.Contains(a.Key)).ToList();
        string signature = string.Join("|", shown.Select(a => a.Key + "=" + a.Text + (now - firstSeen[a.Key] < NewForMs ? "*" : "")));
        if (signature == alertsShown)
            return;
        alertsShown = signature;
        foreach (var child in alertColumn.GetChildren())
            child.QueueFree();
        foreach (var alert in shown.Take(MostAlerts))
            alertColumn.AddChild(AlertRow(alert, now - firstSeen[alert.Key] < NewForMs));
        if (shown.Count > MostAlerts)
            alertColumn.AddChild(Ledger.Label("and " + (shown.Count - MostAlerts) + " more", 12, Ledger.Faded));
    }

    // One alert: a charcoal strip with a mark for how serious (! for danger, • for want, · for work held up, so not by
    // colour alone), its words (a click goes to it), and × to put it away.
    Control AlertRow(Alert alert, bool isNew)
    {
        var strip = new PanelContainer();
        var style = Ledger.Panel();
        style.SetContentMarginAll(3);
        style.SetBorderWidthAll(isNew ? 2 : 1);
        style.BorderColor = isNew ? Ledger.Parchment : Ledger.Brass;
        strip.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer();
        strip.AddChild(row);
        var colour = alert.Severity >= 3 ? Ledger.Warning : alert.Severity == 2 ? Ledger.Brass : Ledger.Parchment;
        string mark = alert.Severity >= 3 ? "! " : alert.Severity == 2 ? "• " : "· ";
        var go = new Button { Text = mark + alert.Text, Flat = true, FocusMode = Control.FocusModeEnum.None,
                              Alignment = HorizontalAlignment.Left,
                              TooltipText = alert.Who != null || alert.Where != null ? "Go to it" : "" };
        go.AddThemeColorOverride("font_color", colour);
        go.AddThemeFontSizeOverride("font_size", 13);
        go.Pressed += () => GoTo(alert);
        row.AddChild(go);
        var away = new Button { Text = "×", Flat = true, FocusMode = Control.FocusModeEnum.None,
                                TooltipText = "Put this away until it clears" };
        away.AddThemeColorOverride("font_color", Ledger.Faded);
        away.Pressed += () =>
        {
            dismissed.Add(alert.Key);
            alertsShown = "";
            ShowAlerts();
        };
        row.AddChild(away);
        return strip;
    }

    // To what an alert is about: the colonist picked and the camera on them, or the place.
    void GoTo(Alert alert)
    {
        if (alert.Who is ColonistId id && Colony.Work!.Workers.Any(w => w.Id == id))
            PickWorker(id, true);
        else if (alert.Where is var (x, y))
        {
            camera!.Position = new Vector2(x + 0.5f, y + 0.5f);
            PickTile(x, y);
        }
    }
}
