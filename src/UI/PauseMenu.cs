// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The in-game menu, opened with Esc: the game pauses while it is open. Save is switched off for a world
/// that can only be looked at (no game to save).
/// </summary>
public partial class PauseMenu : Control
{
    readonly Main main;
    readonly Func<string>? save;
    readonly Func<Main, LoadPanel> loader;
    Control? panel;
    Label? said;

    /// <summary>Called when a setting changed, so the map can redraw.</summary>
    public event Action? SettingsChanged;

    public PauseMenu() : this(null!, null) { }

    /// <summary>`save` saves the game and returns what to tell the player; null when there is no game. `loader`
    /// makes the list Load shows (the world's games if not given; LoadPanel.Colonies for a colony).</summary>
    public PauseMenu(Main main, Func<string>? save, Func<Main, LoadPanel>? loader = null)
    {
        this.main = main;
        this.save = save;
        this.loader = loader ?? (m => new LoadPanel(m));
    }

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
        var saving = Ui.Button("Save", () => said!.Text = save!(), save == null ? "This world can only be looked at." : "");
        saving.Disabled = save == null;
        column.AddChild(saving);
        column.AddChild(Ui.Button("Load", OpenLoad));
        column.AddChild(Ui.Button("Settings", OpenSettings));
        column.AddChild(Ui.Button("Main menu", () => main.ShowMainMenu()));
        column.AddChild(Ui.Button("Quit to desktop", main.Quit));
        said = Ui.Label("", 13);
        column.AddChild(said);
    }

    public bool IsOpen => Visible;

    public void Open()
    {
        Visible = true;
        panel!.Visible = true;
        said!.Text = "";
        GetTree().Paused = true;
    }

    public void Close()
    {
        Visible = false;
        GetTree().Paused = false;
    }

    void OpenLoad()
    {
        panel!.Visible = false;
        var loading = loader(main);
        loading.Closed += () => panel.Visible = true;
        AddChild(loading);
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
