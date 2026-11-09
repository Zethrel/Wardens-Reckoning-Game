// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>The main menu: open a world (a new game on it), open the last one again, load a game, settings, quit.</summary>
public partial class MainMenu : Control
{
    readonly Main main;
    readonly string message;
    VBoxContainer? column;

    // Godot makes nodes itself when it loads a scene, and for that every node class needs a constructor
    // without arguments; this menu is only ever made in code, with the one below.
    public MainMenu() : this(null!, "") { }

    public MainMenu(Main main, string message)
    {
        this.main = main;
        this.message = message;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        column = Ui.CentredColumn(this);
        column.AddChild(Ui.Label("Wardens' Reckoning", 40));
        column.AddChild(Ui.Label("Open a world the generator exported (its world.json).", 14));
        column.AddChild(Ui.Button("Open a world...", PickWorld));
        if (Settings.LastWorld != "" && File.Exists(Settings.LastWorld))
            column.AddChild(Ui.Button("Open the last world", () => main.OpenWorld(Settings.LastWorld), Settings.LastWorld));
        column.AddChild(Ui.Button("Load a game", OpenLoad));
        column.AddChild(Ui.Button("Load a colony", () => OpenLoad(LoadPanel.Colonies(main)),
                                  "Colonies are founded from a world's map: open a world, then pick a founding site"));
        column.AddChild(Ui.Button("Settings", OpenSettings));
        column.AddChild(Ui.Button("Quit", main.Quit));
        if (message != "")
        {
            var note = Ui.Label(message, 14);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.CustomMinimumSize = new Vector2(420, 0);
            note.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.5f));
            column.AddChild(note);
        }
    }

    void PickWorld()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "world.json ; An exported world" },
            Title = "Open an exported world (output\\world-...\\h...\\export\\world.json)",
            UseNativeDialog = true,
        };
        if (Settings.LastWorld != "")
            dialog.CurrentDir = Path.GetDirectoryName(Path.GetDirectoryName(Settings.LastWorld)) ?? "";
        dialog.FileSelected += path => main.OpenWorld(path);
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(900, 600));
    }

    void OpenLoad() => OpenLoad(new LoadPanel(main));

    void OpenLoad(LoadPanel panel)
    {
        var box = column!.GetParent().GetParent<Control>();
        box.Visible = false;
        panel.Closed += () => box.Visible = true;
        AddChild(panel);
    }

    void OpenSettings()
    {
        column!.GetParent().GetParent<Control>().Visible = false;
        var panel = new SettingsPanel();
        panel.Closed += () => column.GetParent().GetParent<Control>().Visible = true;
        AddChild(panel);
    }
}
