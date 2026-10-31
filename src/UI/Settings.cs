// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The player's settings, kept between games in settings.cfg in Godot's user folder
/// (on Windows: %APPDATA%\Godot\app_userdata\Wardens' Reckoning\settings.cfg).
/// </summary>
public static class Settings
{
    const string File = "user://settings.cfg";

    /// <summary>The world.json opened last, offered again in the main menu.</summary>
    public static string LastWorld { get; set; } = "";

    public static bool Fullscreen { get; set; }

    /// <summary>How strongly the borders are coloured over the land, from 0 (not at all) to 255 (solid).
    /// The first value is the history atlas's, 105.</summary>
    public static int BorderAlpha { get; set; } = 105;

    public static void Load()
    {
        var config = new ConfigFile();
        if (config.Load(File) != Error.Ok)
            return;                             // the first time there is no file yet
        LastWorld = config.GetValue("game", "last_world", "").AsString();
        Fullscreen = config.GetValue("display", "fullscreen", false).AsBool();
        BorderAlpha = Math.Clamp(config.GetValue("map", "border_alpha", 105).AsInt32(), 0, 255);
    }

    public static void Save()
    {
        var config = new ConfigFile();
        config.SetValue("game", "last_world", LastWorld);
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("map", "border_alpha", BorderAlpha);
        config.Save(File);
    }

    /// <summary>Puts the display settings into effect.</summary>
    public static void Apply()
    {
        var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
    }
}
