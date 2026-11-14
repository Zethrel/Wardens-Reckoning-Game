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

        var sizeLabel = new Label { Text = SizeText() };
        column.AddChild(sizeLabel);
        var size = new HSlider { MinValue = 0.75, MaxValue = 2, Step = 0.25, Value = Settings.InterfaceSize,
                                 CustomMinimumSize = new Vector2(260, 24) };
        size.ValueChanged += value =>
        {
            Settings.InterfaceSize = value;
            sizeLabel.Text = SizeText();
            Settings.Apply();
        };
        column.AddChild(size);

        var speedLabel = new Label { Text = SpeedText() };
        column.AddChild(speedLabel);
        var speed = new HSlider { MinValue = 0.5, MaxValue = 20, Step = 0.5, Value = Settings.YearsPerSecond,
                                  CustomMinimumSize = new Vector2(260, 24) };
        speed.ValueChanged += value =>
        {
            Settings.YearsPerSecond = value;
            speedLabel.Text = SpeedText();
        };
        column.AddChild(speed);

        // Canopy fading (step 27b): how much the crowns in front of a colonist you look at may hide of them, and how
        // see-through every crown is with the bar's Trees button on (or V held).
        var fadeLabel = new Label { Text = FadeText() };
        column.AddChild(fadeLabel);
        var fade = new HSlider { MinValue = 0.1, MaxValue = 0.9, Step = 0.05, Value = Settings.CanopyOpacity,
                                 CustomMinimumSize = new Vector2(260, 24) };
        fade.ValueChanged += value =>
        {
            Settings.CanopyOpacity = value;
            fadeLabel.Text = FadeText();
        };
        column.AddChild(fade);

        var throughLabel = new Label { Text = ThroughText() };
        column.AddChild(throughLabel);
        var through = new HSlider { MinValue = 0.1, MaxValue = 1, Step = 0.05, Value = Settings.SeeThroughOpacity,
                                    CustomMinimumSize = new Vector2(260, 24) };
        through.ValueChanged += value =>
        {
            Settings.SeeThroughOpacity = value;
            throughLabel.Text = ThroughText();
        };
        column.AddChild(through);

        var motion = new CheckBox { Text = "Reduced Motion (fades jump instead of gliding)", ButtonPressed = Settings.ReducedMotion };
        motion.Toggled += on => Settings.ReducedMotion = on;
        column.AddChild(motion);

        column.AddChild(Ui.Button("Close", Close));
    }

    static string SizeText() => "Interface size: " + Math.Round(Settings.InterfaceSize * 100) + "%";

    static string FadeText() => "Trees in front of a colonist you look at hide at most " + Math.Round(Settings.CanopyOpacity * 100) + "% of them";

    static string ThroughText() => "Seeing through the trees (Trees button, or hold V): crowns at " + Math.Round(Settings.SeeThroughOpacity * 100) + "%";

    static string SpeedText() => "Years a second while playing: " + Settings.YearsPerSecond.ToString("0.#");

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
