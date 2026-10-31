// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// Where the saves are: a folder "saves" in Godot's user folder
/// (on Windows: %APPDATA%\Godot\app_userdata\Wardens' Reckoning\saves).
/// </summary>
public static class Saves
{
    public static string Folder => ProjectSettings.GlobalizePath("user://saves");

    /// <summary>Every save, the newest first.</summary>
    public static List<string> List()
    {
        if (!Directory.Exists(Folder))
            return new List<string>();
        return Directory.GetFiles(Folder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToList();
    }
}

/// <summary>A list of the saves to pick one from, for the main menu and the in-game menu.</summary>
public partial class LoadPanel : Control
{
    readonly Main main;

    /// <summary>Called when the panel closes without loading.</summary>
    public event Action? Closed;

    public LoadPanel() : this(null!) { }

    public LoadPanel(Main main) => this.main = main;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
        var column = Ui.CentredColumn(this);
        column.AddChild(Ui.Label("Load a game", 28));
        var saves = Saves.List();
        var list = new ItemList { CustomMinimumSize = new Vector2(520, 300) };
        foreach (var path in saves)
            list.AddItem(System.IO.Path.GetFileNameWithoutExtension(path) + "   (" +
                         File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm") + ")");
        if (saves.Count == 0)
            column.AddChild(Ui.Label("No saves yet: save from the in-game menu (Esc).", 14));
        else
            column.AddChild(list);
        var load = Ui.Button("Load", () =>
        {
            var picked = list.GetSelectedItems();
            if (picked.Length > 0)
                main.LoadGame(saves[picked[0]]);
        });
        load.Disabled = saves.Count == 0;
        list.ItemActivated += index => main.LoadGame(saves[(int)index]);     // a double click loads too
        column.AddChild(load);
        column.AddChild(Ui.Button("Back", Close));
    }

    void Close()
    {
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
