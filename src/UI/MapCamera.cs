// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using Godot;

namespace WardensReckoning.UI;

/// <summary>
/// The camera over the map: the mouse wheel zooms towards the mouse, the right or middle mouse button
/// drags the map, and W, A, S, D or the arrow keys move it.
/// </summary>
public partial class MapCamera : Camera2D
{
    readonly int mapSize;
    const float ZoomStep = 1.15f;     // each turn of the wheel
    const float KeySpeed = 600f;      // screen pixels a second

    public MapCamera() : this(0) { }

    public MapCamera(int mapSize) => this.mapSize = mapSize;

    public override void _Ready()
    {
        // Start with the whole map on screen.
        var screen = GetViewportRect().Size;
        float fit = Math.Min(screen.X, screen.Y) / Math.Max(1, mapSize);
        Zoom = new Vector2(fit, fit);
        Position = new Vector2(mapSize / 2f, mapSize / 2f);
    }

    float Smallest => Math.Min(GetViewportRect().Size.X, GetViewportRect().Size.Y) / Math.Max(1, mapSize) / 2;
    const float Largest = 40f;        // one map cell 40 screen pixels wide

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
                ZoomAt(ZoomStep, button.Position);
            else if (button.ButtonIndex == MouseButton.WheelDown)
                ZoomAt(1 / ZoomStep, button.Position);
        }
        else if (@event is InputEventMouseMotion motion
                 && (motion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0)
        {
            Position -= motion.Relative / Zoom;
            Keep();
        }
    }

    // Zooms by `factor`, keeping the map point under the mouse where it is on screen.
    void ZoomAt(float factor, Vector2 mouse)
    {
        var before = GetGlobalMousePosition();
        float zoom = Math.Clamp(Zoom.X * factor, Smallest, Largest);
        Zoom = new Vector2(zoom, zoom);
        ForceUpdateScroll();
        Position += before - GetGlobalMousePosition();
        Keep();
    }

    public override void _Process(double delta)
    {
        var move = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        if (Input.IsKeyPressed(Key.A)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D)) move.X += 1;
        if (Input.IsKeyPressed(Key.W)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) move.Y += 1;
        if (move != Vector2.Zero)
        {
            Position += move.LimitLength(1) * KeySpeed * (float)delta / Zoom;
            Keep();
        }
    }

    // Keeps the middle of the screen over the map.
    void Keep() => Position = new Vector2(Math.Clamp(Position.X, 0, mapSize), Math.Clamp(Position.Y, 0, mapSize));
}
