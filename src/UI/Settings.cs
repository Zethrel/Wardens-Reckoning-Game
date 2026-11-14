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

    /// <summary>How many years run each second while playing.</summary>
    public static double YearsPerSecond { get; set; } = 2;

    /// <summary>How big the text and buttons are drawn, from 0.75 to 2 (1 = as designed; step 18b, FP-11: legible,
    /// scalable text). The maps keep their own zoom.</summary>
    public static double InterfaceSize { get; set; } = 1;

    /// <summary>Canopy fading (step 27b; Forest Visibility specification v1.3, B3): how much the tree crowns in front
    /// of a colonist you're looking at may hide of them together, from 0.1 to 0.9. The first value is the
    /// specification's, 0.45.</summary>
    public static double CanopyOpacity { get; set; } = 0.45;

    /// <summary>Seeing through the trees (B10): every crown's opacity while the bar's Trees button is on or V is
    /// held, from 0.1 to 1. The first value, 0.6, is a first guess; the specification leaves it open.</summary>
    public static double SeeThroughOpacity { get; set; } = 0.6;

    /// <summary>Whether the bar's Trees button was left on (B10: a persistent toggle).</summary>
    public static bool SeeThroughTrees { get; set; }

    /// <summary>Reduced Motion (B5, B17): fades jump instead of gliding.</summary>
    public static bool ReducedMotion { get; set; }

    public static void Load()
    {
        var config = new ConfigFile();
        if (config.Load(File) != Error.Ok)
            return;                             // the first time there is no file yet
        LastWorld = config.GetValue("game", "last_world", "").AsString();
        Fullscreen = config.GetValue("display", "fullscreen", false).AsBool();
        BorderAlpha = Math.Clamp(config.GetValue("map", "border_alpha", 105).AsInt32(), 0, 255);
        YearsPerSecond = Math.Clamp(config.GetValue("game", "years_per_second", 2.0).AsDouble(), 0.5, 20);
        InterfaceSize = Math.Clamp(config.GetValue("display", "interface_size", 1.0).AsDouble(), 0.75, 2);
        CanopyOpacity = Math.Clamp(config.GetValue("canopy", "fade_opacity", 0.45).AsDouble(), 0.1, 0.9);
        SeeThroughOpacity = Math.Clamp(config.GetValue("canopy", "see_through_opacity", 0.6).AsDouble(), 0.1, 1);
        SeeThroughTrees = config.GetValue("canopy", "see_through", false).AsBool();
        ReducedMotion = config.GetValue("accessibility", "reduced_motion", false).AsBool();
    }

    public static void Save()
    {
        var config = new ConfigFile();
        config.SetValue("game", "last_world", LastWorld);
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("map", "border_alpha", BorderAlpha);
        config.SetValue("game", "years_per_second", YearsPerSecond);
        config.SetValue("display", "interface_size", InterfaceSize);
        config.SetValue("canopy", "fade_opacity", CanopyOpacity);
        config.SetValue("canopy", "see_through_opacity", SeeThroughOpacity);
        config.SetValue("canopy", "see_through", SeeThroughTrees);
        config.SetValue("accessibility", "reduced_motion", ReducedMotion);
        config.Save(File);
    }

    /// <summary>Puts the display settings into effect.</summary>
    public static void Apply()
    {
        var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
        // The whole window's drawing is scaled: text, buttons, panels.
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.Root.ContentScaleFactor = (float)InterfaceSize;
    }
}
