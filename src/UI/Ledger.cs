// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The "Warden's Ledger" look (step 26b; the maintainer's visual direction, docs/ui-plan.md): charcoal panels,
/// parchment accents, muted brass highlights, information laid out like a settlement administrator's records. Its
/// colours and panel style in one place, so the screens that take it up later (the UI plan's art pass) use the same.
/// So far only the colony screen's inspector does.
/// </summary>
public static class Ledger
{
    public static readonly Color Charcoal = new(0.13f, 0.12f, 0.11f, 0.96f);
    public static readonly Color CharcoalLight = new(0.2f, 0.19f, 0.17f);
    public static readonly Color Brass = new(0.72f, 0.6f, 0.36f);
    public static readonly Color Parchment = new(0.91f, 0.86f, 0.74f);
    public static readonly Color Faded = new(0.66f, 0.62f, 0.55f);       // lesser words, the developer's trace
    public static readonly Color Warning = new(0.93f, 0.55f, 0.4f);      // what needs seeing to now

    /// <summary>A need bar's colour by how pressing it is (0 fine ... 4 critical, NeedBar.Pressing): green, then
    /// brass, amber, orange, red. The words say the same, so the colour is never the only sign (FP-11).</summary>
    public static Color Pressing(int pressing) => pressing switch
    {
        0 => new Color(0.45f, 0.62f, 0.38f),
        1 => new Color(0.72f, 0.66f, 0.36f),
        2 => new Color(0.85f, 0.6f, 0.25f),
        3 => new Color(0.88f, 0.42f, 0.2f),
        _ => new Color(0.82f, 0.22f, 0.18f),
    };

    /// <summary>A charcoal panel with a thin brass edge and slightly rounded corners.</summary>
    public static StyleBoxFlat Panel()
    {
        var box = new StyleBoxFlat { BgColor = Charcoal, BorderColor = Brass };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(6);
        box.SetContentMarginAll(10);
        return box;
    }

    /// <summary>A flat box of one colour, for a bar's track or its fill.</summary>
    public static StyleBoxFlat Flat(Color color)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(3);
        return box;
    }

    /// <summary>A label in the ledger's colours: `size` points, `color` (parchment if not given).</summary>
    public static Label Label(string text, int size = 14, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Parchment);
        return label;
    }

    /// <summary>A small brass heading over a group ("NEEDS").</summary>
    public static Label Heading(string text)
    {
        var label = Label(text.ToUpperInvariant(), 12, Brass);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        return label;
    }
}
