// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// Small helpers for building the screens in code. The screens are made in C# rather than in Godot's
/// scene editor, so all of a screen can be read in one file; they can move to scenes later.
/// </summary>
public static class Ui
{
    public static Button Button(string text, Action pressed, string tooltip = "")
    {
        var button = new Button { Text = text, TooltipText = tooltip, CustomMinimumSize = new Vector2(260, 40) };
        button.Pressed += pressed;
        return button;
    }

    public static Label Label(string text, int fontSize = 16)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    /// <summary>A column of controls in the middle of the screen, on a panel.</summary>
    public static VBoxContainer CentredColumn(Control parent)
    {
        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(centre);
        var panel = new PanelContainer();
        centre.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 24);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);
        return column;
    }
}
