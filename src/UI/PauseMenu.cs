// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The in-game menu, opened with Esc: the game pauses while it is open. Saving and loading are shown
/// but switched off: there is nothing to save until the game runs the years on (step 2 and later).
/// </summary>
public partial class PauseMenu : Control
{
    readonly Main main;
    Control? panel;

    /// <summary>Called when a setting changed, so the map can redraw.</summary>
    public event Action? SettingsChanged;

    public PauseMenu() : this(null!) { }

    public PauseMenu(Main main) => this.main = main;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;           // it must work while everything else is paused
        Visible = false;
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);
        var column = Ui.CentredColumn(this);
        panel = column.GetParent().GetParent<Control>();
        column.AddChild(Ui.Label("Paused", 28));
        column.AddChild(Ui.Button("Resume", Close));
        var save = Ui.Button("Save", () => { }, "Saving comes when the game runs the years on.");
        save.Disabled = true;
        column.AddChild(save);
        var load = Ui.Button("Load", () => { }, "Loading comes with saving.");
        load.Disabled = true;
        column.AddChild(load);
        column.AddChild(Ui.Button("Settings", OpenSettings));
        column.AddChild(Ui.Button("Main menu", () => main.ShowMainMenu()));
        column.AddChild(Ui.Button("Quit to desktop", main.Quit));
    }

    public bool IsOpen => Visible;

    public void Open()
    {
        Visible = true;
        panel!.Visible = true;
        GetTree().Paused = true;
    }

    public void Close()
    {
        Visible = false;
        GetTree().Paused = false;
    }

    void OpenSettings()
    {
        panel!.Visible = false;
        var settings = new SettingsPanel();
        settings.Closed += () => panel.Visible = true;
        settings.Changed += () => SettingsChanged?.Invoke();
        AddChild(settings);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
            return;
        GetViewport().SetInputAsHandled();
        if (IsOpen)
            Close();
        else
            Open();
    }
}
