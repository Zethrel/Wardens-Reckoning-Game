// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// Where the saves are: a folder "saves" in Godot's user folder for the world's games
/// (on Windows: %APPDATA%\Godot\app_userdata\Wardens' Reckoning\saves), and "colonies" for the colonies, with
/// each colony's three morning autosaves in a folder of its own inside it ("autosaves-colony-1234").
/// </summary>
public static class Saves
{
    public static string Folder => ProjectSettings.GlobalizePath("user://saves");

    public static string ColonyFolder => ProjectSettings.GlobalizePath("user://colonies");

    /// <summary>Where the autosaves of colony `name` go.</summary>
    public static string AutosavesOf(string name) => Path.Combine(ColonyFolder, "autosaves-" + name);

    /// <summary>Every save in `folder` (the games' if not given), the newest first; with `within`, the saves in the
    /// folders inside it too (the colonies' autosaves).</summary>
    public static List<string> List(string? folder = null, bool within = false)
    {
        folder ??= Folder;
        if (!Directory.Exists(folder))
            return new List<string>();
        var option = within ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.GetFiles(folder, "*.json", option).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
    }
}

/// <summary>A list of the saves to pick one from, for the main menu and the in-game menu: the world's games, or
/// (made with `Colonies`) the colonies, their autosaves included.</summary>
public partial class LoadPanel : Control
{
    readonly Main main;
    readonly string title;
    readonly Func<List<string>> list;
    readonly Action<string> load;
    readonly string none;

    /// <summary>Called when the panel closes without loading.</summary>
    public event Action? Closed;

    public LoadPanel() : this(null!) { }

    /// <summary>The world's games.</summary>
    public LoadPanel(Main main) : this(main, "Load a game", () => Saves.List(), main == null ? _ => { } : main.LoadGame,
                                      "No saves yet: save from the in-game menu (Esc).") { }

    LoadPanel(Main main, string title, Func<List<string>> list, Action<string> load, string none)
    {
        this.main = main;
        this.title = title;
        this.list = list;
        this.load = load;
        this.none = none;
    }

    /// <summary>The colonies' saves and autosaves.</summary>
    public static LoadPanel Colonies(Main main) =>
        new(main, "Load a colony", () => Saves.List(Saves.ColonyFolder, within: true), path => main.LoadColony(path),
            "No colonies saved yet: found one from a world's map, and save from the in-game menu (Esc).");

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
        var column = Ui.CentredColumn(this);
        column.AddChild(Ui.Label(title, 28));
        var saves = list();
        var items = new ItemList { CustomMinimumSize = new Vector2(560, 300) };
        foreach (var path in saves)
            items.AddItem(Shown(path) + "   (" +
                         File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm") + ")");
        if (saves.Count == 0)
            column.AddChild(Ui.Label(none, 14));
        else
            column.AddChild(items);
        var loading = Ui.Button("Load", () =>
        {
            var picked = items.GetSelectedItems();
            if (picked.Length > 0)
                load(saves[picked[0]]);
        });
        loading.Disabled = saves.Count == 0;
        items.ItemActivated += index => load(saves[(int)index]);     // a double click loads too
        if (saves.Count > 0)
        {
            items.Select(0);
            items.GrabFocus();      // the keyboard can pick one at once: arrows, then Enter (FP-11)
        }
        column.AddChild(loading);
        column.AddChild(Ui.Button("Back", Close));
    }

    // A save's name as listed: an autosave says so, and which colony it belongs to.
    static string Shown(string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "");
        return folder.StartsWith("autosaves-") ? folder["autosaves-".Length..] + ", morning " + name : name;
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
