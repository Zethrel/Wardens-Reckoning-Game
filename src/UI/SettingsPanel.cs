// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>The settings, from the main menu or the in-game menu. Changes take effect at once and are saved on Close.</summary>
public partial class SettingsPanel : Control
{
    /// <summary>Called when the panel closes, so whoever opened it can show itself again.</summary>
    public event Action? Closed;

    /// <summary>Called whenever a setting changes, so the map can redraw.</summary>
    public event Action? Changed;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;           // it works while the game is paused
        var column = Ui.CentredColumn(this);
        column.AddChild(Ui.Label("Settings", 28));

        var fullscreen = new CheckBox { Text = "Fullscreen", ButtonPressed = Settings.Fullscreen };
        fullscreen.Toggled += on =>
        {
            Settings.Fullscreen = on;
            Settings.Apply();
            Changed?.Invoke();
        };
        column.AddChild(fullscreen);

        column.AddChild(new Label { Text = "Border strength" });
        var borders = new HSlider { MinValue = 0, MaxValue = 255, Step = 1, Value = Settings.BorderAlpha,
                                    CustomMinimumSize = new Vector2(260, 24) };
        borders.ValueChanged += value =>
        {
            Settings.BorderAlpha = (int)value;
            Changed?.Invoke();
        };
        column.AddChild(borders);

        column.AddChild(Ui.Button("Close", Close));
    }

    void Close()
    {
        Settings.Save();
        Closed?.Invoke();
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }
}
